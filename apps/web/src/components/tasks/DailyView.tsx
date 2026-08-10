"use client";

import { useEffect, useMemo, useState } from "react";

import { TaskRow, taskOptionId, type TaskRowActions } from "@/components/tasks/TaskRow";
import { TaskEditor, type TaskEditorFields } from "@/components/tasks/TaskEditor";
import { RescheduleInput } from "@/components/tasks/RescheduleInput";
import { AssigneePicker } from "@/components/tasks/AssigneePicker";
import { PriorityPicker } from "@/components/tasks/PriorityPicker";
import { LabelSelector } from "@/components/labels/LabelSelector";
import { EmptyState } from "@/components/ui/EmptyState";
import { Skeleton } from "@/components/ui/Skeleton";
import type { components } from "@/lib/api/generated/schema";
import { listboxKeyDown } from "@/lib/listboxKeys";
import { useDuplicateTask } from "@/hooks/useDuplicateTask";
import { useTaskMutations } from "@/hooks/useTaskMutations";
import styles from "./DailyView.module.css";

type TaskResponse = components["schemas"]["TaskResponse"];

/** A row in a daily view — a task plus the Today-only overdue flag. */
export type DailyRow = TaskResponse & { isOverdue?: boolean };

/** A rendered group: a stable key, a visible heading label, and its ordered rows. */
export interface DailyGroup {
  key: string;
  label: string;
  tasks: DailyRow[];
}

interface DailyViewProps {
  /** The view's accessible name (e.g. "Dziś", "Nadchodzące"). */
  label: string;
  /** The server-grouped, R5-ordered rows (the view renders them as-is). */
  groups: DailyGroup[];
  /** Resolves a project id to its display name for a row's project chip (null = Inbox). */
  projectName: (projectId: string | null | undefined) => string | null;
  /** Message shown when the view has no rows. */
  emptyMessage: string;
  /** Optional empty-state action button (FR-110: hint + action). */
  emptyAction?: React.ReactNode;
  /** Genuine network-bound load in progress — render a skeleton, NEVER the empty state (S5.2). */
  loading?: boolean;
}

/**
 * The shared daily view (Today/Upcoming/Assigned — rebuilt in slice 019, T034/T035/T041).
 * The global shortcut gate is GONE (FR-111): keyboard operability lives INSIDE the
 * `role="listbox"` (↑/↓ selection, Space toggle, Enter opens the editor — D5), and every
 * operation has a visible affordance: row quick actions + the complete "⋯" menu
 * (priority/termin/etykiety/przypisz/duplikuj/usuń — FR-108).
 */
export function DailyView({ label, groups, projectName, emptyMessage, emptyAction, loading = false }: DailyViewProps) {
  const { setTaskDone, setTaskPriority, rescheduleTask, editTask, setTaskAssignees, setTaskLabels, deleteTask } =
    useTaskMutations();
  const { duplicateTask } = useDuplicateTask();

  const flat = useMemo(() => groups.flatMap((g) => g.tasks), [groups]);
  const [selectedIndex, setSelectedIndex] = useState(0);
  const [editorId, setEditorId] = useState<string | null>(null);
  const [rescheduleId, setRescheduleId] = useState<string | null>(null);
  const [assignId, setAssignId] = useState<string | null>(null);
  const [labelId, setLabelId] = useState<string | null>(null);
  const [priorityId, setPriorityId] = useState<string | null>(null);

  // Keep the selection in range as the list changes.
  useEffect(() => {
    setSelectedIndex((i) => Math.min(i, Math.max(0, flat.length - 1)));
  }, [flat.length]);

  const selected: DailyRow | undefined = flat[selectedIndex];
  const hasSelection = selected !== undefined;
  const byId = (id: string | null): DailyRow | undefined =>
    id === null ? undefined : flat.find((t) => t.id === id);

  /** The complete daily-row operation set (FR-103). Daily views are date/priority-ordered,
   * so manual reorder does not apply here (it is an Inbox/project-list operation). */
  const rowActions = (task: DailyRow, index: number): TaskRowActions => ({
    onToggleDone: () => setTaskDone(task.id, task.status !== "done"),
    onEdit: () => {
      setSelectedIndex(index);
      setEditorId(task.id);
    },
    onOpenPriority: () => setPriorityId(task.id),
    onOpenReschedule: () => setRescheduleId(task.id),
    onOpenLabels: () => setLabelId(task.id),
    onOpenAssign: task.projectId != null ? () => setAssignId(task.id) : undefined,
    onDuplicate: () => duplicateTask(task),
    onDelete: () => deleteTask(task.id),
  });

  return (
    <div className={styles.view}>
      <h1 className={styles.heading}>{label}</h1>

      {loading && flat.length === 0 ? (
        <Skeleton variant="row" count={4} />
      ) : flat.length === 0 ? (
        <EmptyState hint={emptyMessage} action={emptyAction} />
      ) : (
        <div
          role="listbox"
          tabIndex={0}
          aria-label={label}
          aria-activedescendant={hasSelection ? taskOptionId(selected.id) : undefined}
          className={styles.list}
          onKeyDown={listboxKeyDown({
            count: flat.length,
            selectedIndex,
            onSelectedIndexChange: setSelectedIndex,
            onToggleSelected: selected
              ? () => setTaskDone(selected.id, selected.status !== "done")
              : undefined,
            onActivateSelected: selected ? () => setEditorId(selected.id) : undefined,
          })}
        >
          {groups.map((group) => (
            <div key={group.key} role="group" aria-label={group.label} className={styles.group}>
              <div className={styles.groupHeading} aria-hidden="true">
                {group.label}
              </div>
              {group.tasks.map((task) => {
                const index = flat.findIndex((t) => t.id === task.id);
                return (
                  <TaskRow
                    key={task.id}
                    task={task}
                    selected={index === selectedIndex}
                    isRenaming={false}
                    isOverdue={task.isOverdue ?? false}
                    onCommitRename={() => undefined}
                    onCancelRename={() => undefined}
                    onSelect={() => setSelectedIndex(index)}
                    projectName={projectName(task.projectId)}
                    actions={rowActions(task, index)}
                    style={{}}
                  />
                );
              })}
            </div>
          ))}
        </div>
      )}

      {byId(rescheduleId) ? (
        <RescheduleInput
          open
          onClose={() => setRescheduleId(null)}
          onSubmit={(dueDate, dueHasTime) => {
            rescheduleTask(rescheduleId!, dueDate, dueHasTime);
            setRescheduleId(null);
          }}
        />
      ) : null}

      {byId(editorId) ? (
        <TaskEditor
          open
          task={byId(editorId)!}
          onClose={() => setEditorId(null)}
          onSave={(fields: TaskEditorFields) => {
            editTask(editorId!, fields);
            setEditorId(null);
          }}
        />
      ) : null}

      {byId(assignId)?.projectId ? (
        <AssigneePicker
          open
          projectId={byId(assignId)!.projectId!}
          current={byId(assignId)!.assignees}
          onClose={() => setAssignId(null)}
          onSubmit={(ids) => {
            setTaskAssignees(assignId!, ids);
            setAssignId(null);
          }}
        />
      ) : null}

      {byId(labelId) ? (
        <LabelSelector
          open
          current={byId(labelId)!.labels}
          onClose={() => setLabelId(null)}
          onSubmit={(ids) => {
            setTaskLabels(labelId!, ids);
            setLabelId(null);
          }}
        />
      ) : null}

      {byId(priorityId) ? (
        <PriorityPicker
          open
          current={byId(priorityId)!.priority}
          onClose={() => setPriorityId(null)}
          onSelect={(priority) => setTaskPriority(priorityId!, priority)}
        />
      ) : null}
    </div>
  );
}

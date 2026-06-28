"use client";

import { useEffect, useMemo, useState } from "react";
import { useRouter } from "next/navigation";

import { TaskRow, taskOptionId } from "@/components/tasks/TaskRow";
import { TaskEditor, type TaskEditorFields } from "@/components/tasks/TaskEditor";
import { RescheduleInput } from "@/components/tasks/RescheduleInput";
import { AssigneePicker } from "@/components/tasks/AssigneePicker";
import type { components } from "@/lib/api/generated/schema";
import { useGlobalShortcuts } from "@/hooks/useGlobalShortcuts";
import { useTaskMutations } from "@/hooks/useTaskMutations";

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
  /** The view's accessible name (e.g. "Today", "Upcoming"). */
  label: string;
  /** The server-grouped, R5-ordered rows (the view renders them as-is). */
  groups: DailyGroup[];
  /** Resolves a project id to its display name for a row's project chip (null = Inbox). */
  projectName: (projectId: string | null | undefined) => string | null;
  /** Message shown when the view has no rows. */
  emptyMessage: string;
}

/**
 * The shared keyboard-driven daily view (slice 005) — Today and Upcoming render through it. A WAI-ARIA
 * `role="listbox"` of `role="group"` sections, each with a visible heading, holding `role="option"` rows.
 * Selection is a single flat index across all rows (the page-orchestrator pattern of the Inbox, simplified:
 * a day's list is bounded, so no virtualization). The global gate ({@link useGlobalShortcuts}) drives the
 * operate verbs on the selected row — `Space` toggle-done, `1`-`4` priority, `T` reschedule, `E` editor —
 * and the `G I`/`G T`/`G U` navigation chords; single-key suppression (FR-031) is inherited from the gate.
 */
export function DailyView({ label, groups, projectName, emptyMessage }: DailyViewProps) {
  const router = useRouter();
  const { setTaskDone, setTaskPriority, rescheduleTask, editTask, setTaskAssignees } = useTaskMutations();

  const flat = useMemo(() => groups.flatMap((g) => g.tasks), [groups]);
  const [selectedIndex, setSelectedIndex] = useState(0);
  const [editorOpen, setEditorOpen] = useState(false);
  const [rescheduleOpen, setRescheduleOpen] = useState(false);
  const [assignOpen, setAssignOpen] = useState(false);

  // Keep the selection in range as the list changes (a reschedule/toggle can drop the row out of view).
  useEffect(() => {
    setSelectedIndex((i) => Math.min(i, Math.max(0, flat.length - 1)));
  }, [flat.length]);

  const selected: DailyRow | undefined = flat[selectedIndex];
  const hasSelection = selected !== undefined;

  const handlers = useMemo(
    () => ({
      onMoveUp: () => setSelectedIndex((i) => Math.max(0, i - 1)),
      onMoveDown: () => setSelectedIndex((i) => Math.min(flat.length - 1, i + 1)),
      onToggle: () => {
        if (selected) setTaskDone(selected.id, selected.status !== "done");
      },
      onSetPriority: (priority: "P0" | "P1" | "P2" | "P3") => {
        if (selected) setTaskPriority(selected.id, priority);
      },
      onReschedule: () => {
        if (selected) setRescheduleOpen(true);
      },
      // `E` opens the full editor in the daily views (the Inbox maps `E` to inline rename).
      onRename: () => {
        if (selected) setEditorOpen(true);
      },
      // `A` opens the assignee picker for the selected SHARED-project task (slice 008, AS-01).
      onAssign: () => {
        if (selected?.projectId) setAssignOpen(true);
      },
      onGoInbox: () => router.push("/"),
      onGoToday: () => router.push("/today"),
      onGoUpcoming: () => router.push("/upcoming"),
      onGoAssigned: () => router.push("/assigned"),
    }),
    [flat.length, selected, setTaskDone, setTaskPriority, router],
  );

  useGlobalShortcuts(handlers);

  return (
    <div className="tf-daily-view">
      <h1 className="tf-daily-view__heading">{label}</h1>

      {flat.length === 0 ? (
        <p className="tf-daily-view__empty">{emptyMessage}</p>
      ) : (
        <div
          role="listbox"
          tabIndex={0}
          aria-label={label}
          aria-activedescendant={hasSelection ? taskOptionId(selected.id) : undefined}
          aria-keyshortcuts="1 2 3 4 T E Space"
          className="tf-task-list"
        >
          {groups.map((group) => (
            <div key={group.key} role="group" aria-label={group.label} className="tf-daily-view__group">
              <div className="tf-daily-view__group-heading" aria-hidden="true">
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
                    style={{}}
                  />
                );
              })}
            </div>
          ))}
        </div>
      )}

      {selected && rescheduleOpen ? (
        <RescheduleInput
          open={rescheduleOpen}
          onClose={() => setRescheduleOpen(false)}
          onSubmit={(dueDate, dueHasTime) => {
            rescheduleTask(selected.id, dueDate, dueHasTime);
            setRescheduleOpen(false);
          }}
        />
      ) : null}

      {selected && editorOpen ? (
        <TaskEditor
          open={editorOpen}
          task={selected}
          onClose={() => setEditorOpen(false)}
          onSave={(fields: TaskEditorFields) => {
            editTask(selected.id, fields);
            setEditorOpen(false);
          }}
        />
      ) : null}

      {selected?.projectId && assignOpen ? (
        <AssigneePicker
          open={assignOpen}
          projectId={selected.projectId}
          current={selected.assignees}
          onClose={() => setAssignOpen(false)}
          onSubmit={(ids) => {
            setTaskAssignees(selected.id, ids);
            setAssignOpen(false);
          }}
        />
      ) : null}
    </div>
  );
}

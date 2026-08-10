"use client";

import { use, useEffect, useState } from "react";
import { useRouter } from "next/navigation";

import { LabelSelector } from "@/components/labels/LabelSelector";
import { ProjectSelector } from "@/components/projects/ProjectSelector";
import { BoardView } from "@/components/tasks/BoardView";
import { GroupByControl, GroupedTaskList } from "@/components/tasks/GroupedTaskList";
import { PriorityPicker } from "@/components/tasks/PriorityPicker";
import { RescheduleInput } from "@/components/tasks/RescheduleInput";
import { TaskCapture } from "@/components/tasks/TaskCapture";
import { TaskList } from "@/components/tasks/TaskList";
import type { TaskRowActions } from "@/components/tasks/TaskRow";
import { ViewModeSwitch } from "@/components/tasks/ViewModeSwitch";
import { AssigneePicker } from "@/components/tasks/AssigneePicker";
import { Button } from "@/components/ui/Button";
import { EmptyState } from "@/components/ui/EmptyState";
import { Skeleton } from "@/components/ui/Skeleton";
import { useDuplicateTask } from "@/hooks/useDuplicateTask";
import { usePersistedProjectView } from "@/hooks/usePersistedProjectView";
import { useProjectMembers } from "@/hooks/useProjectMembers";
import { useProjects } from "@/hooks/useProjects";
import { useProjectTasks } from "@/hooks/useProjectTasks";
import { useTaskMutations } from "@/hooks/useTaskMutations";
import type { TaskResponse } from "@/hooks/useTasks";
import { buildProjectGroups } from "@/lib/board";
import styles from "./project.module.css";

const CAPTURE_INPUT_ID = "project-capture";

/**
 * The project-tasks view (rebuilt in slice 019 — T054/T056; slice 010 adds the two
 * projections of US-03): a visible „Lista” | „Tablica” mode switch in the header
 * (per-project last-used mode, default Lista — D8), the groupable List (FR-024, D9:
 * „Grupuj: Brak | Status | Priorytet”) and the Kanban {@link BoardView} (cancelled hidden —
 * EC-11; viewer read-only). Full row/card surface via the shared action architecture (D7);
 * inline quick-add creating IN THIS PROJECT (FR-107); the task drawer via `?task=` (T052).
 * Skeletons render only on the initial project load — never masking a move (Principle III).
 */
export default function ProjectView({ params }: { params: Promise<{ id: string }> }) {
  const { id } = use(params);
  const router = useRouter();
  const { data: projects } = useProjects();
  const project = (projects ?? []).find((p) => p.id === id);
  const { data: tasks, isPending, isError, refetch } = useProjectTasks(id);
  const { mode, setMode, groupBy, setGroupBy } = usePersistedProjectView(id);
  const {
    renameTask,
    setTaskDone,
    deleteTask,
    moveTaskToProject,
    setTaskLabels,
    setTaskPriority,
    rescheduleTask,
    setTaskAssignees,
  } = useTaskMutations();
  const { duplicateTask } = useDuplicateTask();

  // The card avatars resolve names from the members roster — shared projects only
  // (a personal-project members read would 404; the roster is lazy behind `enabled`).
  const isShared = project?.visibility === "shared";
  const isViewer = project?.role === "viewer";
  const { data: members } = useProjectMembers(id, mode === "board" && isShared);
  const assigneeName = (userId: string): string | null =>
    members?.members.find((m) => m.userId === userId)?.displayName ?? null;

  const rows = tasks ?? [];
  const [selectedIndex, setSelectedIndex] = useState(0);
  const [renamingId, setRenamingId] = useState<string | null>(null);
  const [movingId, setMovingId] = useState<string | null>(null);
  const [labelingId, setLabelingId] = useState<string | null>(null);
  const [priorityId, setPriorityId] = useState<string | null>(null);
  const [reschedulingId, setReschedulingId] = useState<string | null>(null);
  const [assignId, setAssignId] = useState<string | null>(null);

  useEffect(() => {
    setSelectedIndex((i) => Math.min(i, Math.max(0, rows.length - 1)));
  }, [rows.length]);

  const byId = (taskId: string | null): TaskResponse | undefined =>
    taskId === null ? undefined : rows.find((t) => t.id === taskId);

  const rowActions = (task: TaskResponse, index: number): TaskRowActions => ({
    onToggleDone: () => setTaskDone(task.id, task.status !== "done"),
    onEdit: () => {
      setSelectedIndex(index);
      setRenamingId(task.id);
    },
    onOpenPriority: () => setPriorityId(task.id),
    onOpenReschedule: () => setReschedulingId(task.id),
    onOpenLabels: () => setLabelingId(task.id),
    onOpenMove: () => setMovingId(task.id),
    onOpenAssign: () => setAssignId(task.id),
    onDuplicate: () => duplicateTask(task),
    onOpenDetails: () => router.push(`/projects/${id}?task=${task.id}`),
    onDelete: () => deleteTask(task.id),
  });

  /** The card menu's standard set (D7): the List's actions minus the inline rename (row-only). */
  const cardBaseActions = (task: TaskResponse): TaskRowActions => {
    const { onEdit: _onEdit, ...rest } = rowActions(task, -1);
    return rest;
  };

  const commitRename = (title: string) => {
    if (renamingId !== null) renameTask(renamingId, title);
    setRenamingId(null);
  };

  const selectedTask = rows[selectedIndex];

  const addTaskAction = (
    <Button onClick={() => document.getElementById(CAPTURE_INPUT_ID)?.focus()}>
      Dodaj zadanie
    </Button>
  );

  const groupedList = (
    <GroupedTaskList
      groups={buildProjectGroups(rows, groupBy)}
      selectedIndex={selectedIndex}
      onSelectedIndexChange={setSelectedIndex}
      renamingId={renamingId}
      onCommitRename={commitRename}
      onCancelRename={() => setRenamingId(null)}
      onToggleSelected={
        selectedTask ? () => setTaskDone(selectedTask.id, selectedTask.status !== "done") : undefined
      }
      onActivateSelected={
        selectedTask ? () => router.push(`/projects/${id}?task=${selectedTask.id}`) : undefined
      }
      rowActions={rowActions}
    />
  );

  const flatList = (
    <TaskList
      tasks={rows}
      selectedIndex={selectedIndex}
      onSelectedIndexChange={setSelectedIndex}
      renamingId={renamingId}
      onCommitRename={commitRename}
      onCancelRename={() => setRenamingId(null)}
      onToggleSelected={
        selectedTask ? () => setTaskDone(selectedTask.id, selectedTask.status !== "done") : undefined
      }
      onActivateSelected={
        selectedTask ? () => router.push(`/projects/${id}?task=${selectedTask.id}`) : undefined
      }
      rowActions={rowActions}
    />
  );

  return (
    <section aria-labelledby="project-heading">
      <div className={styles.header}>
        <h1 id="project-heading">{project?.name ?? "Projekt"}</h1>
        <ViewModeSwitch mode={mode} onChange={setMode} />
      </div>

      <TaskCapture contextProjectId={id} errorId="project-capture-error" inputId={CAPTURE_INPUT_ID} />

      {isError ? (
        <div role="alert">
          <p>Nie udało się wczytać zadań projektu.</p>
          <Button variant="secondary" onClick={() => void refetch()}>
            Spróbuj ponownie
          </Button>
        </div>
      ) : isPending ? (
        <Skeleton variant="row" count={4} />
      ) : mode === "board" ? (
        <BoardView
          tasks={rows}
          readOnly={isViewer}
          baseActions={cardBaseActions}
          assigneeName={isShared ? assigneeName : undefined}
          emptyAction={addTaskAction}
        />
      ) : rows.length === 0 ? (
        <EmptyState hint="Ten projekt nie ma jeszcze zadań." action={addTaskAction} />
      ) : (
        <>
          <div className={styles.toolbar}>
            <GroupByControl value={groupBy} onChange={setGroupBy} />
          </div>
          {groupBy === "none" ? flatList : groupedList}
        </>
      )}

      <ProjectSelector
        open={movingId !== null}
        onClose={() => setMovingId(null)}
        task={byId(movingId)}
        onSelect={(projectId) => {
          if (movingId !== null) moveTaskToProject(movingId, projectId, id);
        }}
      />
      {byId(labelingId) ? (
        <LabelSelector
          open
          current={byId(labelingId)!.labels}
          onClose={() => setLabelingId(null)}
          onSubmit={(ids) => {
            setTaskLabels(labelingId!, ids);
            setLabelingId(null);
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
      {byId(reschedulingId) ? (
        <RescheduleInput
          open
          onClose={() => setReschedulingId(null)}
          onSubmit={(dueDate, dueHasTime) => {
            rescheduleTask(reschedulingId!, dueDate, dueHasTime);
            setReschedulingId(null);
          }}
        />
      ) : null}
      {byId(assignId) ? (
        <AssigneePicker
          open
          projectId={id}
          current={byId(assignId)!.assignees}
          onClose={() => setAssignId(null)}
          onSubmit={(ids) => {
            setTaskAssignees(assignId!, ids);
            setAssignId(null);
          }}
        />
      ) : null}
    </section>
  );
}

"use client";

import { use, useEffect, useState } from "react";
import { useRouter } from "next/navigation";

import { LabelSelector } from "@/components/labels/LabelSelector";
import { ProjectSelector } from "@/components/projects/ProjectSelector";
import { PriorityPicker } from "@/components/tasks/PriorityPicker";
import { RescheduleInput } from "@/components/tasks/RescheduleInput";
import { TaskCapture } from "@/components/tasks/TaskCapture";
import { TaskList } from "@/components/tasks/TaskList";
import type { TaskRowActions } from "@/components/tasks/TaskRow";
import { AssigneePicker } from "@/components/tasks/AssigneePicker";
import { Button } from "@/components/ui/Button";
import { EmptyState } from "@/components/ui/EmptyState";
import { Skeleton } from "@/components/ui/Skeleton";
import { useDuplicateTask } from "@/hooks/useDuplicateTask";
import { useProjects } from "@/hooks/useProjects";
import { useProjectTasks } from "@/hooks/useProjectTasks";
import { useTaskMutations } from "@/hooks/useTaskMutations";
import type { TaskResponse } from "@/hooks/useTasks";

const CAPTURE_INPUT_ID = "project-capture";

/**
 * The project-tasks view (rebuilt in slice 019 — T054/T056, S5.1): full row surface
 * (quick actions + complete "⋯" menu incl. Przypisz/Duplikuj), inline quick-add creating
 * IN THIS PROJECT (FR-107), and the task drawer via `?task=` (T052) replacing the old
 * comments modal (TaskDetailPanel — deleted, §J3.7). Viewer-role affordance gaps stay
 * behaviorally unchanged (server-side denial authoritative — INV-017/S5.1).
 */
export default function ProjectView({ params }: { params: Promise<{ id: string }> }) {
  const { id } = use(params);
  const router = useRouter();
  const { data: projects } = useProjects();
  const project = (projects ?? []).find((p) => p.id === id);
  const { data: tasks, isPending, isError, refetch } = useProjectTasks(id);
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

  const commitRename = (title: string) => {
    if (renamingId !== null) renameTask(renamingId, title);
    setRenamingId(null);
  };

  const selectedTask = rows[selectedIndex];

  return (
    <section aria-labelledby="project-heading">
      <h1 id="project-heading">{project?.name ?? "Projekt"}</h1>

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
      ) : rows.length === 0 ? (
        <EmptyState
          hint="Ten projekt nie ma jeszcze zadań."
          action={
            <Button onClick={() => document.getElementById(CAPTURE_INPUT_ID)?.focus()}>
              Dodaj zadanie
            </Button>
          }
        />
      ) : (
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

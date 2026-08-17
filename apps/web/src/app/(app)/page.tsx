"use client";

import { useEffect, useMemo, useState } from "react";
import { useRouter } from "next/navigation";

import { CyclePicker } from "@/components/cycles/CyclePicker";
import { LabelSelector } from "@/components/labels/LabelSelector";
import { ProjectSelector } from "@/components/projects/ProjectSelector";
import { PriorityPicker } from "@/components/tasks/PriorityPicker";
import { RescheduleInput } from "@/components/tasks/RescheduleInput";
import { TaskCapture } from "@/components/tasks/TaskCapture";
import { TaskList } from "@/components/tasks/TaskList";
import type { TaskRowActions } from "@/components/tasks/TaskRow";
import { Button } from "@/components/ui/Button";
import { EmptyState } from "@/components/ui/EmptyState";
import { useDuplicateTask } from "@/hooks/useDuplicateTask";
import { useTaskMutations } from "@/hooks/useTaskMutations";
import { useTasks, type TaskResponse } from "@/hooks/useTasks";

const CAPTURE_INPUT_ID = "inbox-capture";

/**
 * Workspace home — the Inbox orchestrator, rebuilt in slice 019 (T034/T035/T041/T045).
 * The single-key shortcut system is GONE (FR-111): every operation now has a visible
 * affordance — the inline quick-add (FR-107), row quick actions + the complete "⋯" menu
 * (FR-108, incl. "Duplikuj" FR-112 and "Przenieś wyżej/niżej" S3.7), and composite-widget
 * keyboard operability INSIDE the listbox (↑/↓/Space/Enter — D5).
 *
 * Region branching (FR-049): a FAILED load shows an accessible error alert with retry;
 * a confirmed-empty Inbox shows the EmptyState (hint + action, no shortcut copy — FR-110);
 * loading falls through to the list (shared, deduped `['tasks']` query).
 */
export default function WorkspaceHome() {
  const router = useRouter();
  const { data, isPending, isError, error, refetch } = useTasks();
  const tasks = useMemo(() => data ?? [], [data]);
  const isEmpty = !isPending && !isError && tasks.length === 0;

  const {
    renameTask,
    setTaskDone,
    reorderTask,
    deleteTask,
    moveTaskToProject,
    setTaskLabels,
    setTaskPriority,
    setTaskCycle,
    rescheduleTask,
  } = useTaskMutations();
  const { duplicateTask } = useDuplicateTask();

  const [selectedIndex, setSelectedIndex] = useState(0);
  const [renamingId, setRenamingId] = useState<string | null>(null);
  const [movingId, setMovingId] = useState<string | null>(null);
  const [labelingId, setLabelingId] = useState<string | null>(null);
  const [priorityId, setPriorityId] = useState<string | null>(null);
  const [cyclingId, setCyclingId] = useState<string | null>(null);
  const [reschedulingId, setReschedulingId] = useState<string | null>(null);

  // Keep `selectedIndex` in range as the list shrinks (delete) or grows.
  useEffect(() => {
    setSelectedIndex((i) => Math.min(i, Math.max(0, tasks.length - 1)));
  }, [tasks.length]);

  const byId = (id: string | null): TaskResponse | undefined =>
    id === null ? undefined : tasks.find((t) => t.id === id);

  // Reorder one rank up/down: recomputes `between()` from FRESH neighbour ranks inside
  // `reorderTask`; the menu items are the keyboard-reachable reorder path (S3.7, D9).
  const moveRowUp = (index: number) => {
    const sel = tasks[index];
    if (!sel || index < 1) return;
    reorderTask(sel.id, tasks[index - 2]?.id ?? null, tasks[index - 1]!.id);
    setSelectedIndex(index - 1);
  };
  const moveRowDown = (index: number) => {
    const sel = tasks[index];
    if (!sel || index > tasks.length - 2) return;
    reorderTask(sel.id, tasks[index + 1]!.id, tasks[index + 2]?.id ?? null);
    setSelectedIndex(index + 1);
  };

  /** The complete Inbox row operation set (FR-103: EVERY operation has an affordance). */
  const rowActions = (task: TaskResponse, index: number): TaskRowActions => ({
    onToggleDone: () => setTaskDone(task.id, task.status !== "done"),
    onEdit: () => {
      setSelectedIndex(index);
      setRenamingId(task.id);
    },
    onOpenPriority: () => setPriorityId(task.id),
    onOpenReschedule: () => setReschedulingId(task.id),
    onOpenLabels: () => setLabelingId(task.id),
    onOpenCycle: () => setCyclingId(task.id),
    onOpenMove: () => setMovingId(task.id),
    onDuplicate: () => duplicateTask(task),
    onOpenDetails: () => router.push(`/?task=${task.id}`),
    onMoveUp: index > 0 ? () => moveRowUp(index) : undefined,
    onMoveDown: index < tasks.length - 1 ? () => moveRowDown(index) : undefined,
    onDelete: () => deleteTask(task.id),
  });

  const commitRename = (title: string) => {
    if (renamingId !== null) {
      renameTask(renamingId, title);
    }
    setRenamingId(null);
  };

  const selectedTask = tasks[selectedIndex];

  return (
    <section aria-labelledby="workspace-heading">
      <h1 id="workspace-heading">Inbox</h1>

      <TaskCapture errorId="inbox-capture-error" inputId={CAPTURE_INPUT_ID} />

      <ProjectSelector
        open={movingId !== null}
        onClose={() => setMovingId(null)}
        task={byId(movingId)}
        onSelect={(projectId) => {
          if (movingId !== null) moveTaskToProject(movingId, projectId, null);
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
      {byId(cyclingId) ? (
        <CyclePicker
          open
          current={byId(cyclingId)!.cycleId ?? null}
          onClose={() => setCyclingId(null)}
          onSelect={(cycleId) => setTaskCycle(cyclingId!, cycleId)}
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

      {isError ? (
        <div role="alert">
          <p>Nie udało się wczytać zadań. {error.message}</p>
          <Button variant="secondary" onClick={() => void refetch()}>
            Spróbuj ponownie
          </Button>
        </div>
      ) : isEmpty ? (
        <EmptyState
          hint="Twój Inbox jest pusty."
          action={
            <Button onClick={() => document.getElementById(CAPTURE_INPUT_ID)?.focus()}>
              Dodaj pierwszy task
            </Button>
          }
        />
      ) : (
        <TaskList
          selectedIndex={selectedIndex}
          onSelectedIndexChange={setSelectedIndex}
          renamingId={renamingId}
          onCommitRename={commitRename}
          onCancelRename={() => setRenamingId(null)}
          onToggleSelected={
            selectedTask ? () => setTaskDone(selectedTask.id, selectedTask.status !== "done") : undefined
          }
          onActivateSelected={
            selectedTask ? () => router.push(`/?task=${selectedTask.id}`) : undefined
          }
          rowActions={rowActions}
          // Pointer drag-drop (T043): the dragged row lands at the target index; the
          // fractional rank is recomputed from the FRESH neighbours inside reorderTask.
          onReorder={(from, to) => {
            const task = tasks[from];
            if (!task || from === to) return;
            if (to > from) {
              reorderTask(task.id, tasks[to]?.id ?? null, tasks[to + 1]?.id ?? null);
            } else {
              reorderTask(task.id, tasks[to - 1]?.id ?? null, tasks[to]?.id ?? null);
            }
            setSelectedIndex(to);
          }}
        />
      )}
    </section>
  );
}

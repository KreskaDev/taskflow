"use client";

import { useDraggable, useDroppable } from "@dnd-kit/core";
import type { ReactNode } from "react";

import { BoardCard } from "@/components/tasks/BoardCard";
import type { TaskRowActions } from "@/components/tasks/TaskRow";
import { EmptyState } from "@/components/ui/EmptyState";
import type { TaskResponse } from "@/hooks/useTasks";
import type { BoardStatus } from "@/lib/board";
import styles from "./BoardColumn.module.css";

/** Polish plural for the column count: 1 zadanie · 2–4 zadania · 5+ zadań. */
function taskCountLabel(count: number): string {
  if (count === 1) return "1 zadanie";
  const tens = count % 100;
  const ones = count % 10;
  if (ones >= 2 && ones <= 4 && (tens < 12 || tens > 14)) return `${count} zadania`;
  return `${count} zadań`;
}

interface BoardColumnProps {
  status: BoardStatus;
  /** The Polish column label from `BOARD_COLUMNS` — the heading TEXT carries the status (FR-044). */
  label: string;
  /** The column's cards in position order (from `buildBoardColumns`). */
  tasks: TaskResponse[];
  /** Builds a card's operation set (D7); absent → read-only cards (viewer posture). */
  cardActions?: (task: TaskResponse) => TaskRowActions | undefined;
  /** Resolves an assignee id to a display name for the card avatars. */
  assigneeName?: (userId: string) => string | null;
  /** Enables card dragging (editor+); false renders static cards — viewer read-only board. */
  draggable?: boolean;
  /** Action for the FR-110 empty state (hint + action — the add-task path). */
  emptyAction?: ReactNode;
}

/**
 * One Kanban column (slice 010, T017): a `role="list"` labelled „<label>, N zadań” (live
 * count included) whose cards are `role="listitem"`s. A card's controls (checkbox, „⋯”
 * menu) are ordinary tab stops — an option-role column would forbid focusable children
 * (axe `nested-interactive`), so the guaranteed keyboard move path is the card menu's
 * „Przenieś w lewo/w prawo” reached by Tab (FR-103/FR-046). The column BODY (list + the
 * FR-110 {@link EmptyState}, which must sit OUTSIDE the list element — axe
 * `aria-required-children`) is the dnd-kit drop target (`useDroppable` by status).
 */
export function BoardColumn({
  status,
  label,
  tasks,
  cardActions,
  assigneeName,
  draggable = false,
  emptyAction,
}: BoardColumnProps) {
  const { setNodeRef, isOver } = useDroppable({ id: status });
  const countLabel = taskCountLabel(tasks.length);

  return (
    <section className={styles.column}>
      <div className={styles.heading} aria-hidden="true">
        <span className={styles.headingLabel}>{label}</span>
        <span className={styles.headingCount}>{countLabel}</span>
      </div>

      <div ref={setNodeRef} data-over={isOver || undefined} className={styles.list}>
        <div role="list" aria-label={`${label}, ${countLabel}`} className={styles.cards}>
          {tasks.map((task) => (
            <DraggableBoardCard
              key={task.id}
              task={task}
              draggable={draggable}
              actions={cardActions?.(task)}
              assigneeName={assigneeName}
            />
          ))}
        </div>
        {tasks.length === 0 ? (
          <EmptyState hint="Brak zadań w tej kolumnie." action={emptyAction} />
        ) : null}
      </div>
    </section>
  );
}

/**
 * The draggable card wrapper: the column list's `role="listitem"`, registered as a dnd-kit
 * draggable. Pointer-first (4px activation keeps plain clicks from starting a drag); the
 * wrapper deliberately does NOT take the dnd-kit `attributes` (role="button" would break
 * the list semantics — the TaskList reorder precedent), so the keyboard move path is the
 * card menu (D7/D10). `draggable=false` (viewer) disables drag activation entirely.
 */
function DraggableBoardCard({
  task,
  draggable,
  actions,
  assigneeName,
}: {
  task: TaskResponse;
  draggable: boolean;
  actions?: TaskRowActions;
  assigneeName?: (userId: string) => string | null;
}) {
  const { setNodeRef, listeners, isDragging } = useDraggable({
    id: task.id,
    disabled: !draggable,
  });

  return (
    <div
      ref={setNodeRef}
      {...(draggable ? listeners : {})}
      role="listitem"
      // listitem gets NO name from its contents (accname spec) — label it for AT/tests.
      aria-label={task.title}
      style={isDragging ? { opacity: 0.4 } : undefined}
    >
      <BoardCard task={task} actions={actions} assigneeName={assigneeName} />
    </div>
  );
}

"use client";

import { useDraggable, useDroppable } from "@dnd-kit/core";
import { useState, type ReactNode } from "react";

import { BoardCard } from "@/components/tasks/BoardCard";
import { taskOptionId, type TaskRowActions } from "@/components/tasks/TaskRow";
import { EmptyState } from "@/components/ui/EmptyState";
import type { TaskResponse } from "@/hooks/useTasks";
import type { BoardStatus } from "@/lib/board";
import { listboxKeyDown } from "@/lib/listboxKeys";
import styles from "./BoardColumn.module.css";

/** Polish plural for the column count: 1 zadanie · 2–4 zadania · 5+ zadań. */
export function taskCountLabel(count: number): string {
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
 * One Kanban column (slice 010, T017 — research D10): a `role="listbox"` labelled
 * „<label>, N zadań” (live count included) whose options are the cards. Arrow navigation
 * reuses the shared {@link listboxKeyDown} INSIDE the column; the column is a single tab
 * stop, so Tab moves between columns (and the rest of the page). The column body is the
 * dnd-kit drop target (`useDroppable` by status); an empty column renders the catalog
 * {@link EmptyState} (FR-110) and stays a valid drop target.
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
  const [selectedIndex, setSelectedIndex] = useState(0);
  const { setNodeRef, isOver } = useDroppable({ id: status });

  const clamped = Math.min(selectedIndex, Math.max(0, tasks.length - 1));
  const selected: TaskResponse | undefined = tasks[clamped];
  const countLabel = taskCountLabel(tasks.length);

  return (
    <section className={styles.column}>
      <div className={styles.heading} aria-hidden="true">
        <span className={styles.headingLabel}>{label}</span>
        <span className={styles.headingCount}>{countLabel}</span>
      </div>

      <div
        ref={setNodeRef}
        role="listbox"
        tabIndex={0}
        aria-label={`${label}, ${countLabel}`}
        aria-activedescendant={selected ? taskOptionId(selected.id) : undefined}
        data-over={isOver || undefined}
        className={styles.list}
        onKeyDown={listboxKeyDown({
          count: tasks.length,
          selectedIndex: clamped,
          onSelectedIndexChange: setSelectedIndex,
          onToggleSelected: selected ? () => cardActions?.(selected)?.onToggleDone?.() : undefined,
          onActivateSelected: selected ? () => cardActions?.(selected)?.onOpenDetails?.() : undefined,
        })}
      >
        {tasks.length === 0 ? (
          <EmptyState hint="Brak zadań w tej kolumnie." action={emptyAction} />
        ) : (
          tasks.map((task, index) => (
            <DraggableBoardCard
              key={task.id}
              task={task}
              draggable={draggable}
              selected={index === clamped}
              onSelect={() => setSelectedIndex(index)}
              actions={cardActions?.(task)}
              assigneeName={assigneeName}
            />
          ))
        )}
      </div>
    </section>
  );
}

/**
 * The draggable card wrapper: registers the card body as a dnd-kit draggable. Pointer-first
 * (4px activation keeps plain clicks selecting); the wrapper deliberately does NOT take the
 * dnd-kit `attributes` (role="button" + tabIndex 0 would break the column's single-tab-stop
 * listbox model — the TaskList reorder precedent), so the guaranteed keyboard move path is
 * the card menu's „Przenieś w lewo/w prawo” (D7/D10, FR-103/FR-046). `draggable=false`
 * (viewer) disables drag activation entirely while the card stays a normal option.
 */
function DraggableBoardCard({
  task,
  draggable,
  selected,
  onSelect,
  actions,
  assigneeName,
}: {
  task: TaskResponse;
  draggable: boolean;
  selected: boolean;
  onSelect: () => void;
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
      tabIndex={-1}
      style={isDragging ? { opacity: 0.4 } : undefined}
    >
      <BoardCard task={task} selected={selected} onSelect={onSelect} actions={actions} assigneeName={assigneeName} />
    </div>
  );
}

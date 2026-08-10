"use client";

import {
  DndContext,
  DragOverlay,
  KeyboardSensor,
  PointerSensor,
  rectIntersection,
  useSensor,
  useSensors,
  type Announcements,
  type DragEndEvent,
} from "@dnd-kit/core";
import { createPortal } from "react-dom";
import { useState, type ReactNode } from "react";

import { BoardColumn } from "@/components/tasks/BoardColumn";
import type { TaskRowActions } from "@/components/tasks/TaskRow";
import { useTaskMutations } from "@/hooks/useTaskMutations";
import type { TaskResponse } from "@/hooks/useTasks";
import { BOARD_COLUMNS, adjacentStatus, buildBoardColumns, type BoardStatus } from "@/lib/board";
import styles from "./BoardView.module.css";

interface BoardViewProps {
  /** The project's tasks (the one `['projects', <id>, 'tasks']` query — D5). */
  tasks: TaskResponse[];
  /**
   * Viewer posture (shared project, role=viewer): the board renders READ-ONLY — no drag
   * activation, no card actions, no move items. UI convenience only; the server still
   * denies a forged PATCH with 403 (FR-065/068).
   */
  readOnly?: boolean;
  /**
   * The page's standard per-card operation set (pickers/details/delete — D7); the board
   * merges its own move actions (`adjacentStatus`-mapped) on top. Ignored when read-only.
   */
  baseActions?: (task: TaskResponse) => TaskRowActions;
  /** Resolves an assignee id to a display name for the card avatars. */
  assigneeName?: (userId: string) => string | null;
  /** Action for the empty-column state (FR-110) — the add-task path. */
  emptyAction?: ReactNode;
}

/** The status a card's drop landed on, or null for an out-of-board drop. */
function dropTargetStatus(event: DragEndEvent): BoardStatus | null {
  const overId = event.over?.id;
  return BOARD_COLUMNS.some((c) => c.status === overId) ? (overId as BoardStatus) : null;
}

/** Polish dnd announcements on dnd-kit's own live region (D10). */
function boardAnnouncements(tasks: TaskResponse[]): Announcements {
  const titleOf = (id: unknown): string => tasks.find((t) => t.id === id)?.title ?? "zadanie";
  const columnOf = (id: unknown): string =>
    BOARD_COLUMNS.find((c) => c.status === id)?.label ?? "kolumna";
  return {
    onDragStart: ({ active }) => `Podniesiono „${titleOf(active.id)}”.`,
    onDragOver: ({ active, over }) =>
      over ? `„${titleOf(active.id)}” nad kolumną ${columnOf(over.id)}.` : undefined,
    onDragEnd: ({ active, over }) =>
      over
        ? `Upuszczono „${titleOf(active.id)}” w kolumnie ${columnOf(over.id)}.`
        : `Upuszczono „${titleOf(active.id)}”.`,
    onDragCancel: ({ active }) => `Anulowano przenoszenie „${titleOf(active.id)}”.`,
  };
}

/**
 * The Kanban board (slice 010, T018 — US-03.AS-03..06, D5/D6/D10): four
 * {@link BoardColumn}s from the single `buildBoardColumns` mapping over the one project
 * query (cancelled filtered — EC-11). A drop on another column issues ONE optimistic
 * `PATCH /status` via the established mutation factory — `position` untouched (D6);
 * within-column order stays the List's position rank. The card „⋯” menu is the guaranteed
 * non-drag move path (boundary items omitted via `adjacentStatus` — AS-05). DragOverlay
 * portals at `--z-menu`; the drop animation is disabled under `prefers-reduced-motion`
 * (FR-047); announcements are Polish on dnd-kit's live region (D10).
 */
export function BoardView({ tasks, readOnly = false, baseActions, assigneeName, emptyAction }: BoardViewProps) {
  const { setTaskStatus } = useTaskMutations();
  const [draggingId, setDraggingId] = useState<string | null>(null);

  const sensors = useSensors(
    useSensor(PointerSensor, { activationConstraint: { distance: 4 } }),
    useSensor(KeyboardSensor),
  );

  const columns = buildBoardColumns(tasks);
  const draggingTask = draggingId ? tasks.find((t) => t.id === draggingId) : undefined;

  const reducedMotion =
    typeof window !== "undefined" &&
    typeof window.matchMedia === "function" &&
    window.matchMedia("(prefers-reduced-motion: reduce)").matches;

  const cardActions = (task: TaskResponse): TaskRowActions | undefined => {
    if (readOnly) return undefined;
    const left = adjacentStatus(task.status, "left");
    const right = adjacentStatus(task.status, "right");
    return {
      ...baseActions?.(task),
      // Presence mapped via adjacentStatus (AS-05): a boundary column simply has no item.
      onMoveLeft: left ? () => setTaskStatus(task.id, left) : undefined,
      onMoveRight: right ? () => setTaskStatus(task.id, right) : undefined,
    };
  };

  const onDragEnd = (event: DragEndEvent): void => {
    setDraggingId(null);
    if (readOnly) return;
    const target = dropTargetStatus(event);
    const task = tasks.find((t) => t.id === event.active.id);
    if (!target || !task || task.status === target) return;
    // ONE status write (D6) — position untouched; optimistic paint + rollback in the factory.
    setTaskStatus(task.id, target);
  };

  const grid = (
    <div className={styles.board}>
      {columns.map((column) => (
        <BoardColumn
          key={column.status}
          status={column.status}
          label={column.label}
          tasks={column.tasks}
          cardActions={cardActions}
          assigneeName={assigneeName}
          draggable={!readOnly}
          emptyAction={emptyAction}
        />
      ))}
    </div>
  );

  if (readOnly) return grid;

  return (
    <DndContext
      sensors={sensors}
      collisionDetection={rectIntersection}
      accessibility={{ announcements: boardAnnouncements(tasks) }}
      onDragStart={(event) => setDraggingId(String(event.active.id))}
      onDragCancel={() => setDraggingId(null)}
      onDragEnd={onDragEnd}
    >
      {grid}
      {/* Portal above the column stacking contexts (the documented virtualizer/z trap). */}
      {typeof document !== "undefined"
        ? createPortal(
            <DragOverlay
              className={styles.dragOverlay}
              dropAnimation={reducedMotion ? null : undefined}
            >
              {draggingTask ? <div className={styles.dragPreview}>{draggingTask.title}</div> : null}
            </DragOverlay>,
            document.body,
          )
        : null}
    </DndContext>
  );
}

"use client";

import {
  DndContext,
  DragOverlay,
  PointerSensor,
  closestCenter,
  useSensor,
  useSensors,
  type DragEndEvent,
} from "@dnd-kit/core";
import { SortableContext, useSortable, verticalListSortingStrategy } from "@dnd-kit/sortable";
import { defaultRangeExtractor, useVirtualizer } from "@tanstack/react-virtual";
import { GripVertical } from "lucide-react";
import { createPortal } from "react-dom";
import { useCallback, useEffect, useRef, useState, type ReactNode } from "react";

import { useTasks, type TaskResponse } from "@/hooks/useTasks";
import { listboxKeyDown } from "@/lib/listboxKeys";

import { TaskRow, taskOptionId, type TaskRowActions } from "./TaskRow";
import styles from "./TaskList.module.css";

/** Estimated row height (px) for the virtualizer. Rows are fixed-height (one line). */
const ROW_HEIGHT = 40;

interface TaskListProps {
  /** The selected (active) option index — the parent (page) owns selection state. */
  selectedIndex: number;
  /** Reports a new selection (pointer click / in-widget arrow keys). */
  onSelectedIndexChange: (index: number) => void;
  /** The id of the task currently in inline-rename mode, or `null`. */
  renamingId: string | null;
  onCommitRename: (title: string) => void;
  onCancelRename: () => void;
  /** Space on the listbox — toggle the selected task done↔backlog (composite-widget key, T035). */
  onToggleSelected?: () => void;
  /** Enter on the listbox — open the selected task's details (composite-widget key, T035). */
  onActivateSelected?: () => void;
  /** Builds the per-row operation set (quick actions + "⋯" menu, T040/T041). */
  rowActions?: (task: TaskResponse, index: number) => TaskRowActions;
  /**
   * Pointer drag-reorder (T043, D9): drop lands the dragged row at the target index. The
   * "Przenieś wyżej/niżej" menu items are the keyboard-reachable equivalent — dnd-kit's
   * keyboard sensor is deliberately NOT the accessibility story.
   */
  onReorder?: (fromIndex: number, toIndex: number) => void;
}

interface TaskListViewProps extends TaskListProps {
  tasks: TaskResponse[];
}

/**
 * The virtualized Inbox listbox (rebuilt in slice 019 — T035/T040). Keyboard operability
 * lives INSIDE the widget after the shortcut-system removal (D5): the container handles
 * ↑/↓/Home/End selection, Space toggle, Enter open via {@link listboxKeyDown} — no
 * document-level listeners. Selection stays `aria-activedescendant`-based (the only
 * pattern that survives virtualization × keyboard-nav × screen-reader).
 */
export function TaskList({ tasks, ...props }: TaskListProps & { tasks?: TaskResponse[] }) {
  const { data } = useTasks();
  return <TaskListView {...props} tasks={tasks ?? data ?? []} />;
}

/** Presentational shell, split out so the virtualizer always has a concrete array. */
function TaskListView({
  tasks,
  selectedIndex,
  onSelectedIndexChange,
  renamingId,
  onCommitRename,
  onCancelRename,
  onToggleSelected,
  onActivateSelected,
  rowActions,
  onReorder,
}: TaskListViewProps) {
  const scrollRef = useRef<HTMLDivElement>(null);
  const [draggingId, setDraggingId] = useState<string | null>(null);

  // Pointer-first drag (D9): a small activation distance keeps plain clicks selecting.
  const sensors = useSensors(useSensor(PointerSensor, { activationConstraint: { distance: 4 } }));

  const onDragEnd = (event: DragEndEvent) => {
    setDraggingId(null);
    const { active, over } = event;
    if (!over || active.id === over.id || !onReorder) return;
    const from = tasks.findIndex((t) => t.id === active.id);
    const to = tasks.findIndex((t) => t.id === over.id);
    if (from >= 0 && to >= 0) onReorder(from, to);
  };

  const draggingTask = draggingId ? tasks.find((t) => t.id === draggingId) : undefined;

  const hasSelection = selectedIndex >= 0 && selectedIndex < tasks.length;

  // FORCE-INCLUDE the selected index in the rendered window at ALL times (research R10) so
  // `aria-activedescendant` never dangles after a wheel/scrollbar scroll.
  const rangeExtractor = useCallback(
    (range: Parameters<typeof defaultRangeExtractor>[0]) => {
      const indexes = new Set(defaultRangeExtractor(range));
      if (selectedIndex >= 0 && selectedIndex < range.count) {
        indexes.add(selectedIndex);
      }
      return [...indexes].sort((a, b) => a - b);
    },
    [selectedIndex],
  );

  const virtualizer = useVirtualizer({
    count: tasks.length,
    getScrollElement: () => scrollRef.current,
    estimateSize: () => ROW_HEIGHT,
    overscan: 8,
    rangeExtractor,
    getItemKey: (index) => tasks[index]!.id,
  });

  // Keep the selected row visible when selection moves via arrows.
  useEffect(() => {
    if (hasSelection) {
      virtualizer.scrollToIndex(selectedIndex);
    }
  }, [selectedIndex, hasSelection, virtualizer]);

  // Focus return after inline rename: restore focus to the listbox so arrow-nav resumes.
  const prevRenamingId = useRef<string | null>(null);
  useEffect(() => {
    if (prevRenamingId.current !== null && renamingId === null) {
      scrollRef.current?.focus();
    }
    prevRenamingId.current = renamingId;
  }, [renamingId]);

  const body = (
    <div
      ref={scrollRef}
      role="listbox"
      tabIndex={0}
      aria-label="Zadania"
      aria-activedescendant={hasSelection ? taskOptionId(tasks[selectedIndex]!.id) : undefined}
      className={styles.list}
      onKeyDown={listboxKeyDown({
        count: tasks.length,
        selectedIndex,
        onSelectedIndexChange,
        onToggleSelected,
        onActivateSelected,
      })}
    >
      <div
        role="presentation"
        className={styles.sizer}
        style={{ height: `${virtualizer.getTotalSize()}px` }}
      >
        {virtualizer.getVirtualItems().map((virtualRow) => {
          const task = tasks[virtualRow.index]!;
          const rowStyle = {
            position: "absolute" as const,
            top: 0,
            left: 0,
            width: "100%",
            height: `${virtualRow.size}px`,
            transform: `translateY(${virtualRow.start}px)`,
          };
          const shared = {
            task,
            selected: virtualRow.index === selectedIndex,
            isRenaming: task.id === renamingId,
            onCommitRename,
            onCancelRename,
            onSelect: () => onSelectedIndexChange(virtualRow.index),
            actions: rowActions?.(task, virtualRow.index),
            style: rowStyle,
          };
          return onReorder ? (
            <SortableTaskRow key={virtualRow.key} {...shared} dimmed={task.id === draggingId} />
          ) : (
            <TaskRow key={virtualRow.key} {...shared} />
          );
        })}
      </div>
    </div>
  );

  if (!onReorder) return body;

  return (
    <DndContext
      sensors={sensors}
      collisionDetection={closestCenter}
      onDragStart={(event) => setDraggingId(String(event.active.id))}
      onDragCancel={() => setDraggingId(null)}
      onDragEnd={onDragEnd}
    >
      <SortableContext items={tasks.map((t) => t.id)} strategy={verticalListSortingStrategy}>
        {body}
      </SortableContext>
      {/* DragOverlay in a PORTAL at the token z-layer — the virtualized translateY rows
          create stacking contexts that would otherwise paint over an in-list preview
          (the documented slice-001 trap). */}
      {typeof document !== "undefined"
        ? createPortal(
            <DragOverlay className={styles.dragOverlay}>
              {draggingTask ? <div className={styles.dragPreview}>{draggingTask.title}</div> : null}
            </DragOverlay>,
            document.body,
          )
        : null}
    </DndContext>
  );
}

/**
 * A sortable wrapper row (T043): registers the row as a drop target and mounts the drag
 * HANDLE inside the action zone. The sortable transform is deliberately NOT applied to
 * the row (the virtualizer owns `translateY`); the DragOverlay is the moving visual.
 */
function SortableTaskRow({
  dimmed,
  ...props
}: {
  task: TaskResponse;
  selected: boolean;
  isRenaming: boolean;
  onCommitRename: (title: string) => void;
  onCancelRename: () => void;
  onSelect: () => void;
  actions?: TaskRowActions;
  style: React.CSSProperties;
  dimmed: boolean;
}) {
  const { setNodeRef, attributes, listeners } = useSortable({ id: props.task.id });

  const handle: ReactNode = (
    <span
      className={styles.dragHandle}
      {...attributes}
      {...listeners}
      // The handle is pointer-first (D9); the keyboard-reachable reorder path is the
      // "Przenieś wyżej/niżej" menu items, so the handle stays out of the tab order.
      tabIndex={-1}
      aria-hidden="true"
      data-drag-handle
    >
      <GripVertical size={14} strokeWidth={1.75} />
    </span>
  );

  // The wrapper carries the virtualizer's absolute position and is the sortable's
  // measured drop target; the sortable TRANSFORM is not applied (the overlay moves).
  const { style, ...rowProps } = props;
  return (
    <div ref={setNodeRef} style={{ ...style, opacity: dimmed ? 0.4 : undefined }}>
      <TaskRow {...rowProps} dragHandle={handle} style={{ height: "100%" }} />
    </div>
  );
}

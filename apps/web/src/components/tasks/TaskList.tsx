"use client";

import { defaultRangeExtractor, useVirtualizer } from "@tanstack/react-virtual";
import { useCallback, useEffect, useRef, type ReactNode } from "react";

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
  /** Per-row drag-handle slot (T043). */
  rowDragHandle?: (task: TaskResponse, index: number) => ReactNode;
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
export function TaskList(props: TaskListProps) {
  const { data } = useTasks();
  return <TaskListView {...props} tasks={data ?? []} />;
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
  rowDragHandle,
}: TaskListViewProps) {
  const scrollRef = useRef<HTMLDivElement>(null);

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

  return (
    <div
      ref={scrollRef}
      role="listbox"
      tabIndex={0}
      aria-label="Tasks"
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
          return (
            <TaskRow
              key={virtualRow.key}
              task={task}
              selected={virtualRow.index === selectedIndex}
              isRenaming={task.id === renamingId}
              onCommitRename={onCommitRename}
              onCancelRename={onCancelRename}
              onSelect={() => onSelectedIndexChange(virtualRow.index)}
              actions={rowActions?.(task, virtualRow.index)}
              dragHandle={rowDragHandle?.(task, virtualRow.index)}
              style={{
                position: "absolute",
                top: 0,
                left: 0,
                width: "100%",
                height: `${virtualRow.size}px`,
                transform: `translateY(${virtualRow.start}px)`,
              }}
            />
          );
        })}
      </div>
    </div>
  );
}

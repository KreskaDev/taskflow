"use client";

import { defaultRangeExtractor, useVirtualizer } from "@tanstack/react-virtual";
import { useCallback, useEffect, useRef } from "react";

import { useTasks, type TaskResponse } from "@/hooks/useTasks";

import { TaskRow, taskOptionId } from "./TaskRow";

/** Estimated row height (px) for the virtualizer. Rows are fixed-height (one line). */
const ROW_HEIGHT = 40;

interface TaskListProps {
  /**
   * The selected (active) option index — the parent (page) owns selection state so the
   * global ↑/↓ shortcut gate (T054) and the operate keys (T058) mutate a single source of
   * truth. Out-of-range values (e.g. a stale index after the list shrinks) are tolerated:
   * they simply render no active option until the parent reconciles.
   */
  selectedIndex: number;
  /** Reports a new selection (e.g. a pointer click on a row). Controlled-component shape. */
  onSelectedIndexChange: (index: number) => void;
  /**
   * The id of the task currently in inline-rename mode, or `null` (T058). Owned by the page
   * so only the selected row ever enters edit mode and the page can drive commit/cancel.
   */
  renamingId: string | null;
  /** Commit the inline rename of `renamingId` with a NEW (validated) title. */
  onCommitRename: (title: string) => void;
  /** Cancel the inline rename without changing the title (Esc/blur). */
  onCancelRename: () => void;
}

interface TaskListViewProps extends TaskListProps {
  tasks: TaskResponse[];
}

/**
 * The virtualized task list (T038 render baseline, controlled keyboard selection added in
 * US8/T055). The scroll container is a plain WAI-ARIA `role="listbox"` (NEVER a combobox —
 * the US8 reorder chord depends on it, research R18), is `tabIndex=0` so it can hold DOM
 * focus, and carries an accessible name via `aria-label`. Rows are windowed with
 * `@tanstack/react-virtual` so 10k tasks render at 60fps (SC-010/SC-011).
 *
 * This component is CONTROLLED: `selectedIndex` and `renamingId` are owned by the parent
 * (the app-shell page, T058). The operate keys (Space/E/Del/Alt+↑↓) are dispatched by the
 * GLOBAL gate acting on the page's `selectedIndex` — this component renders the resulting
 * state (selection indicator + the inline-rename input on `renamingId`).
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
}: TaskListViewProps) {
  const scrollRef = useRef<HTMLDivElement>(null);

  // Is the parent-owned selection a real, addressable row right now? Guards every use of
  // `selectedIndex` so a stale/out-of-range value can never index undefined or dangle the
  // `aria-activedescendant` reference.
  const hasSelection = selectedIndex >= 0 && selectedIndex < tasks.length;

  // FORCE-INCLUDE the selected index in the rendered window at ALL times (research R10).
  // `scrollToIndex` only keeps the selection mounted for ↑/↓-driven scrolls — a wheel or
  // scrollbar scroll could still push it out of the window and unmount it, leaving the
  // listbox's `aria-activedescendant` pointing at a node the screen reader can't resolve.
  // Extending the rendered range to always contain the selected index keeps that option a
  // LIVE DOM node regardless of scroll source. `overscan` remains as supplementary slack.
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
    // Stable key per task so React reuses DOM nodes as the window scrolls (R18).
    getItemKey: (index) => tasks[index]!.id,
  });

  // Keep the selected row visible when selection moves via ↑/↓ (the parent updates
  // `selectedIndex`; this scrolls it into view). Force-include above keeps it mounted;
  // this keeps it on-screen. Guarded so an out-of-range index never scrolls.
  useEffect(() => {
    if (hasSelection) {
      virtualizer.scrollToIndex(selectedIndex);
    }
  }, [selectedIndex, hasSelection, virtualizer]);

  // Focus return after inline rename (T058). The rename `<input>` autofocuses (stealing
  // focus from the listbox); when it unmounts — on commit OR cancel — DOM focus would be
  // stranded on `<body>` and arrow-nav would die silently. Detect the renamingId
  // non-null→null transition and restore focus to the listbox container so ↑/↓ resume.
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
      // `aria-activedescendant` points at the SELECTED option's stable id (never `:focus`,
      // never a roving tabindex) — the only pattern that survives virtualization ×
      // keyboard-nav × screen-reader (research R10). `undefined` (not "") when there is no
      // valid selection so AT reports "no active option" rather than a dangling reference.
      aria-activedescendant={
        hasSelection ? taskOptionId(tasks[selectedIndex]!.id) : undefined
      }
      // Discoverability for the FROZEN reorder chord (research R18); the listbox stays
      // PLAIN (no combobox) so the chord is never intercepted as a native select toggle.
      aria-keyshortcuts="Alt+ArrowUp Alt+ArrowDown"
      className="tf-task-list"
    >
      <div
        // role="presentation" so this virtualizer sizer is transparent to AT: the
        // role="option" rows stay logical owned children of the role="listbox" parent
        // (FR-043). Listbox stays plain — no combobox — so the US8 reorder chord works (R18).
        role="presentation"
        className="tf-task-list__sizer"
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

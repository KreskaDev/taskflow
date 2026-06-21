"use client";

import { useVirtualizer } from "@tanstack/react-virtual";
import { useRef } from "react";

import { useTasks, type TaskResponse } from "@/hooks/useTasks";

import { TaskRow } from "./TaskRow";

/** Estimated row height (px) for the virtualizer. Rows are fixed-height (one line). */
const ROW_HEIGHT = 40;

interface TaskListView {
  tasks: TaskResponse[];
}

/**
 * The virtualized task list (render-only baseline, T038). The scroll container is a plain
 * WAI-ARIA `role="listbox"` (NOT a combobox — the US8 reorder chord depends on it,
 * research R18), is `tabIndex=0` so it can hold DOM focus, and carries an accessible name
 * via `aria-label`. Rows are windowed with `@tanstack/react-virtual` so 10k tasks render
 * at 60fps (SC-010/SC-011). Keyboard selection and operate mechanics are added in
 * US8/T055 — they are deliberately absent here.
 */
export function TaskList() {
  const { data } = useTasks();
  return <TaskListView tasks={data ?? []} />;
}

/** Presentational shell, split out so the virtualizer always has a concrete array. */
function TaskListView({ tasks }: TaskListView) {
  const scrollRef = useRef<HTMLDivElement>(null);

  const virtualizer = useVirtualizer({
    count: tasks.length,
    getScrollElement: () => scrollRef.current,
    estimateSize: () => ROW_HEIGHT,
    overscan: 8,
    // Stable key per task so React reuses DOM nodes as the window scrolls (R18).
    getItemKey: (index) => tasks[index]!.id,
  });

  return (
    <div
      ref={scrollRef}
      role="listbox"
      tabIndex={0}
      aria-label="Tasks"
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

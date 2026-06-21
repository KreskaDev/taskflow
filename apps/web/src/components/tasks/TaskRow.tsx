import type { CSSProperties } from "react";

import type { TaskResponse } from "@/hooks/useTasks";

/** Stable, deterministic option id derived from the task id (research R18). */
export function taskOptionId(taskId: string): string {
  return `task-option-${taskId}`;
}

interface TaskRowProps {
  task: TaskResponse;
  /** Absolute position styles supplied by the virtualizer for this row. */
  style: CSSProperties;
}

/**
 * A single listbox option (render-only baseline, T038). `role="option"` with a STABLE
 * `id` derived from the task id so `aria-activedescendant` (US8/T055) can address it even
 * across virtualizer mount/unmount, and an accessible name equal to the task title
 * (FR-043). Shows the title plus a done/backlog visual state. No selection indicator yet —
 * keyboard selection/operate lands in US8/T055.
 */
export function TaskRow({ task, style }: TaskRowProps) {
  const done = task.status === "done";

  return (
    <div
      id={taskOptionId(task.id)}
      role="option"
      aria-selected={false}
      data-status={task.status}
      className={`tf-task-row${done ? " tf-task-row--done" : ""}`}
      style={style}
    >
      <span className="tf-task-row__state" aria-hidden="true">
        {done ? "✓" : "○"}
      </span>
      <span className="tf-task-row__title">{task.title}</span>
    </div>
  );
}

"use client";

import { LabelChips } from "@/components/labels/LabelChips";
import {
  buildMenuItems,
  formatDueDate,
  taskOptionId,
  type TaskRowActions,
} from "@/components/tasks/TaskRow";
import { Avatar } from "@/components/ui/Avatar";
import { Checkbox } from "@/components/ui/Checkbox";
import { Menu } from "@/components/ui/Menu";
import type { TaskResponse } from "@/hooks/useTasks";
import styles from "./BoardCard.module.css";

interface BoardCardProps {
  task: TaskResponse;
  /** Selected (active) option in the column listbox — drives `aria-selected` (D10). */
  selected: boolean;
  /** Selects this card on pointer interaction. */
  onSelect?: () => void;
  /**
   * The wired operation set — the shared `buildMenuItems` architecture (D7): the full
   * standard action set plus `onMoveLeft`/`onMoveRight` mapped by the caller via
   * `adjacentStatus` (boundary items OMITTED — AS-05). ABSENT for a viewer: the card
   * renders read-only with no action zone (the server still enforces 403).
   */
  actions?: TaskRowActions;
  /** Resolves an assignee id to a display name (shared-project roster); unresolved → count badge. */
  assigneeName?: (userId: string) => string | null;
}

/** Maps a priority token to its text chip (FR-044: text always carries the meaning). */
function priorityLabel(priority: string | null | undefined): string | null {
  switch (priority) {
    case "P0":
    case "P1":
    case "P2":
    case "P3":
      return priority;
    default:
      return null;
  }
}

/**
 * A Board column card (slice 010, T016 — contracts/ui-board-list.md). Catalog components
 * only; the title renders as sanitized TEXT (FR-099 posture — no raw-HTML render path);
 * the due chip reuses the List's exact formatter (no new date logic — Principle X); the
 * „⋯” menu is the SHARED `buildMenuItems` (D7), so menu order/copy never drift between
 * the List and the Board. `role="option"` with the stable {@link taskOptionId} keeps the
 * column listbox's `aria-activedescendant` addressing consistent with every other listbox.
 */
export function BoardCard({ task, selected, onSelect, actions, assigneeName }: BoardCardProps) {
  const done = task.status === "done";
  const priority = priorityLabel(task.priority);

  const resolvedAssignees = task.assignees
    .map((userId) => ({ userId, name: assigneeName?.(userId) ?? null }))
    .filter((a): a is { userId: string; name: string } => a.name !== null);

  return (
    <div
      id={taskOptionId(task.id)}
      role="option"
      aria-selected={selected}
      data-status={task.status}
      className={[styles.card, done ? styles.done : null, selected ? styles.selected : null]
        .filter(Boolean)
        .join(" ")}
      onClick={onSelect}
    >
      <div className={styles.header}>
        {actions?.onToggleDone ? (
          <Checkbox
            aria-label={done ? `Oznacz „${task.title}” jako niezrobione` : `Oznacz „${task.title}” jako zrobione`}
            checked={done}
            onChange={() => actions.onToggleDone?.()}
            onClick={(event) => event.stopPropagation()}
          />
        ) : null}

        <span className={styles.title}>{task.title}</span>

        {actions ? (
          <span className={styles.actionZone} onClick={(event) => event.stopPropagation()}>
            <Menu
              items={buildMenuItems(task, actions)}
              triggerLabel={`Więcej akcji: ${task.title}`}
              menuLabel="Akcje taska"
              triggerContent={<span aria-hidden="true">⋯</span>}
            />
          </span>
        ) : null}
      </div>

      {priority || task.dueDate || task.labels.length > 0 || task.assignees.length > 0 ? (
        <div className={styles.chips}>
          {priority ? (
            <span className={styles.priority} data-priority={task.priority}>
              <span className="sr-only">priorytet: </span>
              {priority}
            </span>
          ) : null}

          {task.dueDate ? (
            <span className={styles.due}>
              <span className="sr-only">termin: </span>
              {formatDueDate(task.dueDate, task.dueHasTime)}
            </span>
          ) : null}

          <LabelChips labelIds={task.labels} />

          {resolvedAssignees.length > 0 ? (
            <span className={styles.avatars}>
              <span className="sr-only">przypisani: </span>
              {resolvedAssignees.map((a) => (
                <Avatar key={a.userId} userId={a.userId} displayName={a.name} size="sm" />
              ))}
            </span>
          ) : task.assignees.length > 0 ? (
            <span className={styles.assigneeCount}>
              <span className="sr-only">przypisani: </span>
              {task.assignees.length}
            </span>
          ) : null}
        </div>
      ) : null}
    </div>
  );
}

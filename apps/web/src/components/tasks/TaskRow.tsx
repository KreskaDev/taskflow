"use client";

import { type CSSProperties, type ReactNode, useEffect, useRef, useState } from "react";
import { Pencil } from "lucide-react";

import { LabelChips } from "@/components/labels/LabelChips";
import { Checkbox } from "@/components/ui/Checkbox";
import { IconButton } from "@/components/ui/IconButton";
import { Menu, type MenuItemSpec } from "@/components/ui/Menu";
import { Tooltip } from "@/components/ui/Tooltip";
import type { TaskResponse } from "@/hooks/useTasks";
import { formatInReferenceZone } from "@/lib/timezone";
import { taskTitleSchema } from "@/lib/validation/task";
import styles from "./TaskRow.module.css";

/** Stable, deterministic option id derived from the task id (research R18). */
export function taskOptionId(taskId: string): string {
  return `task-option-${taskId}`;
}

/**
 * Formats a stored due-date UTC instant for display in the reference zone (R9). The instant
 * is interpreted in Europe/Warsaw by {@link formatInReferenceZone} — a date-only `due_date`
 * (midnight-Warsaw → UTC) recovers the correct calendar day. `dd.MM.yyyy HH:mm` when
 * `dueHasTime`, `dd.MM.yyyy` otherwise.
 */
function formatDueDate(dueDate: string, dueHasTime: boolean | null | undefined): string {
  const instant = new Date(dueDate);
  return formatInReferenceZone(instant, dueHasTime ? "dd.MM.yyyy HH:mm" : "dd.MM.yyyy");
}

/**
 * The row's operation set (slice 019, T040/T041 — FR-108/FR-112, US-18.AS-02). Each view
 * wires what it supports; the "⋯" menu exposes EVERY wired operation (FR-103) and the
 * hover/focus quick-action bar carries the 2–3 most frequent (complete, edit, "⋯").
 * All optional — an unwired operation simply does not render.
 */
export interface TaskRowActions {
  /** Complete/uncomplete (the row Checkbox + the menu item). */
  onToggleDone?: () => void;
  /** Edit — inline rename (Inbox) or the full editor (daily views); per-view semantics. */
  onEdit?: () => void;
  /** Priority picker ("Priorytet…"). */
  onOpenPriority?: () => void;
  /** Due-date input ("Termin…"). */
  onOpenReschedule?: () => void;
  /** Label selector ("Etykiety…"). */
  onOpenLabels?: () => void;
  /** Move-to-project selector ("Przenieś do projektu…"). */
  onOpenMove?: () => void;
  /** Assignee picker ("Przypisz…") — shared-project tasks only (FR-069). */
  onOpenAssign?: () => void;
  /** Duplicate (FR-112, "Duplikuj"). */
  onDuplicate?: () => void;
  /** Open the task's detail surface ("Szczegóły i komentarze"). */
  onOpenDetails?: () => void;
  /** Reorder one rank up ("Przenieś wyżej") — the keyboard-reachable reorder (S3.7). */
  onMoveUp?: () => void;
  /** Reorder one rank down ("Przenieś niżej"). */
  onMoveDown?: () => void;
  /** Delete ("Usuń", destructive). */
  onDelete?: () => void;
}

interface TaskRowProps {
  task: TaskResponse;
  /** Selected (active) option — drives `aria-selected` (focus stays on the listbox container). */
  selected: boolean;
  /** Inline-rename mode (Inbox); the row renders an autofocused input instead of the title. */
  isRenaming: boolean;
  onCommitRename: (title: string) => void;
  onCancelRename: () => void;
  /** Selects this row on pointer interaction. */
  onSelect?: () => void;
  /** The containing project's display name (renders the project chip when present). */
  projectName?: string | null;
  /** Overdue flag (Today view): a text label, never color alone (FR-044). */
  isOverdue?: boolean;
  /** The wired operation set — quick actions + the complete "⋯" menu (FR-108). */
  actions?: TaskRowActions;
  /** Drag-handle slot (T043) — rendered inside the action zone when reordering is wired. */
  dragHandle?: ReactNode;
  /** Absolute position styles supplied by the virtualizer for this row. */
  style: CSSProperties;
}

/** Maps a priority token to its human label (FR-044: text always carries the meaning). */
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

/** Builds the complete "⋯" menu (FR-103/FR-108: every wired operation appears). */
function buildMenuItems(task: TaskResponse, actions: TaskRowActions): MenuItemSpec[] {
  const done = task.status === "done";
  const items: (MenuItemSpec | null)[] = [
    actions.onToggleDone
      ? { id: "toggle", label: done ? "Oznacz jako niezrobione" : "Oznacz jako zrobione", onSelect: actions.onToggleDone }
      : null,
    actions.onEdit ? { id: "edit", label: "Edytuj", onSelect: actions.onEdit } : null,
    actions.onOpenPriority ? { id: "priority", label: "Priorytet…", onSelect: actions.onOpenPriority } : null,
    actions.onOpenReschedule ? { id: "due", label: "Termin…", onSelect: actions.onOpenReschedule } : null,
    actions.onOpenLabels ? { id: "labels", label: "Etykiety…", onSelect: actions.onOpenLabels } : null,
    actions.onOpenMove ? { id: "move", label: "Przenieś do projektu…", onSelect: actions.onOpenMove } : null,
    actions.onOpenAssign ? { id: "assign", label: "Przypisz…", onSelect: actions.onOpenAssign } : null,
    actions.onDuplicate ? { id: "duplicate", label: "Duplikuj", onSelect: actions.onDuplicate } : null,
    actions.onOpenDetails ? { id: "details", label: "Szczegóły i komentarze", onSelect: actions.onOpenDetails } : null,
    actions.onMoveUp ? { id: "move-up", label: "Przenieś wyżej", onSelect: actions.onMoveUp } : null,
    actions.onMoveDown ? { id: "move-down", label: "Przenieś niżej", onSelect: actions.onMoveDown } : null,
    actions.onDelete ? { id: "delete", label: "Usuń", onSelect: actions.onDelete, destructive: true } : null,
  ];
  return items.filter((i): i is MenuItemSpec => i !== null);
}

/**
 * A single listbox option (rebuilt in slice 019 — T040/T041; FR-108, US-18.AS-02, 13px
 * density). `role="option"` with a STABLE id (the listbox's `aria-activedescendant`
 * addresses it across virtualizer mount/unmount); accessible name = title + labelled
 * qualifiers (sr-only "termin:"/"priorytet:" prefixes).
 *
 * Quick actions (complete / edit / "⋯") live in an action zone that is VISIBLE on row
 * hover AND on keyboard focus-within, and is Tab/Shift+Tab traversable in both directions
 * (opacity-hidden, never `display:none` — spec Edge Cases, FR-046). The "⋯" {@link Menu}
 * exposes every wired operation, keyboard-navigable, in a portal above the virtualized
 * `translateY` rows (the documented stacking trap). Hit targets ≥32px (FR-108).
 */
export function TaskRow({
  task,
  selected,
  isRenaming,
  onCommitRename,
  onCancelRename,
  onSelect,
  projectName,
  isOverdue = false,
  actions,
  dragHandle,
  style,
}: TaskRowProps) {
  const done = task.status === "done";
  const projected = task.projectId != null;
  const priority = priorityLabel(task.priority);

  return (
    <div
      id={taskOptionId(task.id)}
      role="option"
      aria-selected={selected}
      data-status={task.status}
      className={[styles.row, done ? styles.done : null, selected ? styles.selected : null]
        .filter(Boolean)
        .join(" ")}
      style={style}
      onClick={onSelect}
    >
      {actions?.onToggleDone ? (
        <Checkbox
          aria-label={done ? `Oznacz „${task.title}” jako niezrobione` : `Oznacz „${task.title}” jako zrobione`}
          checked={done}
          onChange={() => actions.onToggleDone?.()}
          onClick={(event) => event.stopPropagation()}
          className={styles.checkbox}
        />
      ) : (
        <span className={styles.stateGlyph} aria-hidden="true" data-done={done} />
      )}

      {isRenaming ? (
        <RenameInput initialTitle={task.title} onCommit={onCommitRename} onCancel={onCancelRename} />
      ) : (
        <RowTitle task={task} onSelect={onSelect} onOpenDetails={actions?.onOpenDetails} />
      )}

      {!isRenaming && projected && projectName ? (
        <span className={styles.projectChip}>
          <span className="sr-only">projekt: </span>
          {projectName}
        </span>
      ) : null}

      {!isRenaming && priority ? (
        <span className={styles.priority} data-priority={task.priority}>
          <span className="sr-only">priorytet: </span>
          {priority}
        </span>
      ) : null}

      {!isRenaming && isOverdue ? <span className={styles.overdue}>zaległe</span> : null}

      {!isRenaming && task.assignees.length > 0 ? (
        <span className={styles.assignees}>
          <span className="sr-only">przypisani: </span>
          {task.assignees.length}
        </span>
      ) : null}

      {!isRenaming ? <LabelChips labelIds={task.labels} /> : null}

      {!isRenaming && task.dueDate ? (
        <span className={styles.due}>
          <span className="sr-only">termin: </span>
          {formatDueDate(task.dueDate, task.dueHasTime)}
        </span>
      ) : null}

      {!isRenaming && actions ? (
        <span
          className={styles.actionZone}
          // The action zone is presentation-level chrome inside the option; its buttons are
          // individually labelled. Clicks inside must not re-fire row selection handlers twice.
          onClick={(event) => event.stopPropagation()}
        >
          {dragHandle}
          {actions.onEdit ? (
            <IconButton aria-label={`Edytuj „${task.title}”`} className={styles.action} onClick={actions.onEdit}>
              <Pencil size={14} strokeWidth={1.75} aria-hidden="true" />
            </IconButton>
          ) : null}
          <Menu
            items={buildMenuItems(task, actions)}
            triggerLabel={`Więcej akcji: ${task.title}`}
            menuLabel="Akcje taska"
            triggerContent={<span aria-hidden="true" className={styles.ellipsis}>⋯</span>}
            triggerClassName={styles.action}
          />
        </span>
      ) : null}
    </div>
  );
}

/**
 * The row title — the drawer trigger when details are wired (UIT-036: a real button,
 * never hover-only), a plain span otherwise. When the rendered title is actually
 * TRUNCATED (measured), the full value becomes focus-reachable via the catalog
 * {@link Tooltip} (S5.4, FR-046) — the button variant rides the child's own focus
 * (no extra tab stop), the span variant makes the otherwise-unreachable value focusable.
 */
function RowTitle({
  task,
  onSelect,
  onOpenDetails,
}: {
  task: TaskResponse;
  onSelect?: () => void;
  onOpenDetails?: () => void;
}) {
  const titleRef = useRef<HTMLSpanElement>(null);
  const [truncated, setTruncated] = useState(false);

  useEffect(() => {
    const el = titleRef.current;
    if (!el) return;
    setTruncated(el.scrollWidth > el.clientWidth);
  }, [task.title]);

  const text = (
    <span ref={titleRef} className={styles.title}>
      {task.title}
    </span>
  );

  if (onOpenDetails) {
    const button = (
      <button
        type="button"
        className={styles.titleButton}
        onClick={(event) => {
          event.stopPropagation();
          onSelect?.();
          onOpenDetails();
        }}
      >
        {text}
      </button>
    );
    return truncated ? (
      <Tooltip label={task.title} focusableChild className={styles.titleTooltip}>
        {button}
      </Tooltip>
    ) : (
      button
    );
  }

  return truncated ? (
    <Tooltip label={task.title} className={styles.titleTooltip}>
      {text}
    </Tooltip>
  ) : (
    text
  );
}

/**
 * The inline-rename editor (Inbox "edit" quick action). Enter validates via the shared Zod
 * schema and commits; Esc/blur cancels. `stopPropagation` keeps the row's select onClick
 * from firing while editing.
 */
function RenameInput({
  initialTitle,
  onCommit,
  onCancel,
}: {
  initialTitle: string;
  onCommit: (title: string) => void;
  onCancel: () => void;
}) {
  const [value, setValue] = useState(initialTitle);

  const onKeyDown = (event: React.KeyboardEvent<HTMLInputElement>) => {
    if (event.key === "Enter") {
      event.preventDefault();
      const result = taskTitleSchema.safeParse(value);
      if (!result.success) return; // Empty after trim: no-op, stay in edit mode.
      onCommit(result.data);
      return;
    }
    if (event.key === "Escape") {
      event.preventDefault();
      event.stopPropagation();
      onCancel();
    }
  };

  return (
    <input
      type="text"
      // Intentional autofocus: the rename input steals focus from the listbox; TaskList
      // refocuses the listbox on commit/cancel so arrow-nav resumes.
      autoFocus
      className={styles.renameInput}
      aria-label="Rename task"
      maxLength={500}
      value={value}
      onClick={(event) => event.stopPropagation()}
      onChange={(event) => setValue(event.target.value)}
      onKeyDown={onKeyDown}
      onBlur={onCancel}
    />
  );
}

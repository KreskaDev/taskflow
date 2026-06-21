"use client";

import { type CSSProperties, useState } from "react";

import type { TaskResponse } from "@/hooks/useTasks";
import { formatInReferenceZone } from "@/lib/timezone";
import { taskTitleSchema } from "@/lib/validation/task";

/** Stable, deterministic option id derived from the task id (research R18). */
export function taskOptionId(taskId: string): string {
  return `task-option-${taskId}`;
}

/**
 * Formats a stored due-date UTC instant for display in the reference zone (R9). The
 * instant is interpreted in Europe/Warsaw by {@link formatInReferenceZone} — passing the
 * raw UTC `Date` straight in (do NOT pre-convert with `toReferenceZone`, which would shift
 * twice), so a date-only `due_date` (midnight-Warsaw → UTC) recovers the correct calendar
 * day rather than landing a day early. Tokens are locale-neutral and numeric, mirroring the
 * two-digit `DD.MM` capture grammar: `dd.MM.yyyy HH:mm` when `dueHasTime`, `dd.MM.yyyy`
 * otherwise (zero-padded day for symmetry with the zero-padded month).
 */
function formatDueDate(dueDate: string, dueHasTime: boolean | null | undefined): string {
  const instant = new Date(dueDate);
  return formatInReferenceZone(instant, dueHasTime ? "dd.MM.yyyy HH:mm" : "dd.MM.yyyy");
}

interface TaskRowProps {
  task: TaskResponse;
  /**
   * Whether this row is the selected (active) option. Drives `aria-selected`, which is
   * BOTH the screen-reader selection state and the CSS hook for the visible selection
   * indicator (`.tf-task-row[aria-selected="true"]`, NOT `:focus` — DOM focus stays on
   * the listbox container, never the row; FR-042 / research R10).
   */
  selected: boolean;
  /**
   * Whether this row is in inline-rename mode (T058). When true the row renders an
   * `<input>` (autofocused) instead of the static title; the page owns which id is
   * renaming so only the selected row ever enters this mode.
   */
  isRenaming: boolean;
  /** Commit the rename with a NEW title (already validated by the row). */
  onCommitRename: (title: string) => void;
  /** Cancel inline rename (Esc or blur) without changing the title. */
  onCancelRename: () => void;
  /** Selects this row on pointer click — the controlled selection path (US8/T055). */
  onSelect?: () => void;
  /** Absolute position styles supplied by the virtualizer for this row. */
  style: CSSProperties;
}

/**
 * A single listbox option (T038 baseline, controlled selection T055, operate affordances
 * T058). `role="option"` with a STABLE `id` derived from the task id so the listbox's
 * `aria-activedescendant` can address it even across virtualizer mount/unmount, and an
 * accessible name equal to the task title — plus, for a due-bearing row, a visually-hidden
 * "termin:" qualifier in front of the date so it is announced as a labelled due date rather
 * than as a bare trailing number; the decorative status glyph is excluded from the name
 * (`aria-hidden`) (FR-043). `aria-selected` reflects the active
 * option and is the ONLY selection signal — the visible indicator is styled on it, not on
 * `:focus`, because focus lives on the stable container (research R10).
 *
 * Operate keys (Space toggle, `E` rename, `Del` delete, Alt+↑/↓ reorder) are dispatched by
 * the GLOBAL gate ({@link useGlobalShortcuts}) acting on the page's `selectedIndex` — the
 * row holds no per-row keydown listener for them (DOM focus stays on the listbox container).
 * The row's ONLY local interaction is the inline-rename `<input>` it renders while
 * `isRenaming`: it validates with the shared Zod schema (Constitution VI), commits on Enter
 * (surfacing the server 422 through the global announcer), and cancels on Esc/blur.
 */
export function TaskRow({
  task,
  selected,
  isRenaming,
  onCommitRename,
  onCancelRename,
  onSelect,
  style,
}: TaskRowProps) {
  const done = task.status === "done";

  return (
    <div
      id={taskOptionId(task.id)}
      role="option"
      aria-selected={selected}
      data-status={task.status}
      className={`tf-task-row${done ? " tf-task-row--done" : ""}`}
      style={style}
      onClick={onSelect}
    >
      <span className="tf-task-row__state" aria-hidden="true">
        {done ? "✓" : "○"}
      </span>
      {isRenaming ? (
        <RenameInput
          initialTitle={task.title}
          onCommit={onCommitRename}
          onCancel={onCancelRename}
        />
      ) : (
        <span className="tf-task-row__title">{task.title}</span>
      )}
      {!isRenaming && task.dueDate ? (
        // Visible, always-rendered text label (FR-046: no hover-only affordance). The
        // calendar day/time is the meaning — never color alone (FR-044) — and it carries no
        // keybinding. The pairing invariant (R8) makes a truthy `dueDate` a sufficient guard.
        // The leading `tf-sr-only` "termin:" is concatenated into the option's accessible
        // name so the date is announced as a labelled due date, not a bare trailing number
        // (FR-043); the visible date text node stays visible for sighted users.
        <span className="tf-task-row__due">
          <span className="tf-sr-only">termin: </span>
          {formatDueDate(task.dueDate, task.dueHasTime)}
        </span>
      ) : null}
    </div>
  );
}

/**
 * The inline-rename editor (T058; FR-093, US-08 `E`). Mounts autofocused inside the selected
 * row, seeded with the current title. Enter validates via the shared Zod schema and commits
 * the trimmed value (a 422 from the server surfaces through the global MutationCache
 * announcer — no bespoke toast here); an empty-after-trim title is a no-op that stays in
 * edit mode (mirrors {@link TaskCapture}). Esc cancels without changing the title; blur
 * cancels too so focus can never get stranded on a hidden input. `stopPropagation` keeps the
 * row's `onClick` (select) from firing while editing.
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
      // Stop the Dialog/global Esc handling — Esc here only cancels the rename.
      event.preventDefault();
      event.stopPropagation();
      onCancel();
    }
  };

  return (
    <input
      type="text"
      // Intentional autofocus: the rename input steals focus from the listbox so the user
      // can type immediately. TaskList refocuses the listbox on commit/cancel so arrow-nav
      // resumes (so focus is never stranded — the usual no-autofocus concern).
      autoFocus
      className="tf-task-row__rename-input"
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

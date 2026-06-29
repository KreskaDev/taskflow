"use client";

import { type CSSProperties, useState } from "react";

import { LabelChips } from "@/components/labels/LabelChips";
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
  /**
   * The display name of the project this task is in (T041). When present (the task is projected,
   * R16), the row renders a project chip; absent → the task is in the Inbox and no chip shows.
   * Resolved by the parent from the loaded project tree so the row holds no query dependency.
   */
  projectName?: string | null;
  /**
   * Opens the move-to-project selector for THIS row (T041; FR-021/AS-05). Optional so the
   * virtualized {@link TaskList} can omit it; wired to the chip button when supplied. The bare
   * `M` shortcut targets the SELECTED row via the global gate — this is the pointer affordance.
   */
  onOpenMove?: () => void;
  /**
   * Whether the task is overdue (slice 005, Today view only). When true the row renders an
   * "overdue" label — a text signal, never color alone (FR-044). Defaults to false.
   */
  isOverdue?: boolean;
  /** Absolute position styles supplied by the virtualizer for this row. */
  style: CSSProperties;
}

/** Maps a priority token to its human label (FR-044: text always accompanies any color cue). */
function priorityLabel(priority: string | null | undefined): string | null {
  switch (priority) {
    case "P0":
      return "P0";
    case "P1":
      return "P1";
    case "P2":
      return "P2";
    case "P3":
      return "P3";
    default:
      return null;
  }
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
  projectName,
  onOpenMove,
  isOverdue = false,
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
      {!isRenaming && projected ? (
        // Project chip (T041, R16): a visible, always-rendered placement label (FR-046: no
        // hover-only affordance). The project NAME carries the meaning (never color alone,
        // FR-044) and is a React text node so it is escaped (FR-099). When `onOpenMove` is
        // supplied the chip is a button that opens the move selector (pointer affordance for
        // the `M` shortcut); `stopPropagation` keeps the row's select `onClick` from also firing.
        onOpenMove ? (
          <button
            type="button"
            className="tf-task-row__project"
            aria-label={`In project ${projectName ?? ""}. Move to another project`}
            onClick={(event) => {
              event.stopPropagation();
              onOpenMove();
            }}
          >
            {projectName ?? "Project"}
          </button>
        ) : (
          <span className="tf-task-row__project">
            <span className="tf-sr-only">projekt: </span>
            {projectName ?? "Project"}
          </span>
        )
      ) : null}
      {!isRenaming && priority ? (
        // Priority badge (slice 005, FR-044): the P0–P3 TEXT label always carries the meaning — a
        // color class may accompany it, but the text is the signal, never color alone. The
        // `data-priority` hook lets CSS tint it without the color being the sole carrier.
        <span className="tf-task-row__priority" data-priority={task.priority}>
          <span className="tf-sr-only">priorytet: </span>
          {priority}
        </span>
      ) : null}
      {!isRenaming && isOverdue ? (
        // Overdue flag (slice 005, Today view): a text label, never color alone (FR-044).
        <span className="tf-task-row__overdue">zaległe</span>
      ) : null}
      {!isRenaming && task.assignees.length > 0 ? (
        // Assignee count (slice 008) — a text badge (never color/avatar alone, FR-044); the names live in
        // the assignee picker. Announced with a labelled count (FR-043).
        <span className="tf-task-row__assignees">
          <span className="tf-sr-only">przypisani: </span>
          {task.assignees.length}
        </span>
      ) : null}
      {!isRenaming ? (
        // Label chips (slice 006, US-08.AS-04): the caller's own labels by NAME (resolved from the roster);
        // renders nothing when the task carries none. Name is the carrier, color decorative (FR-044/FR-099).
        <LabelChips labelIds={task.labels} />
      ) : null}
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

"use client";

import { useState } from "react";
import { usePathname } from "next/navigation";
import { Plus } from "lucide-react";

import { Dialog } from "@/components/ui/Dialog";
import { useTaskMutations } from "@/hooks/useTaskMutations";
import { parseTaskInput, resolveDatePhrase } from "@/lib/dates";
import { createTaskSchema } from "@/lib/validation/task";
import styles from "./TaskCapture.module.css";

const ERROR_ID = "task-capture-error";

/**
 * The recoverable-failure message (FR-006/EC-02): a trailing date *attempt* that
 * cannot resolve (e.g. "Spotkanie 30.02"). Surfaced verbatim per the spec.
 */
const UNRECOGNIZED_MESSAGE = "nie rozpoznano";

interface TaskCaptureProps {
  /** Create in this project's context (FR-107); null/absent = the Inbox. */
  contextProjectId?: string | null;
  /** Today-view context: a dateless entry resolves to due-today (clarification 2026-08-09). */
  defaultDueToday?: boolean;
  autoFocus?: boolean;
  /** Fired after a successful create (the dialog host closes on it). */
  onCreated?: () => void;
  /** Esc inside the field (FR-030) — cancel/close without creating. */
  onCancel?: () => void;
  /** Unique error-node id when several captures mount at once. */
  errorId?: string;
  /** Optional input id so an EmptyState action can focus this capture. */
  inputId?: string;
}

/**
 * Inline quick-add (slice 019 T044 — FR-107, FR-030; rebuilt from the slice-001 `C`
 * capture dialog after the shortcut-system removal). Present within each task list; Enter
 * parses the trailing Polish date phrase (slice-003 grammar) and drives the optimistic
 * create in THIS view's context; Esc cancels; an unresolvable date attempt keeps the value
 * and shows "nie rozpoznano" via the persistent polite status node (EC-02/FR-006).
 */
export function TaskCapture({
  contextProjectId = null,
  defaultDueToday = false,
  autoFocus = false,
  onCreated,
  onCancel,
  errorId = ERROR_ID,
  inputId,
}: TaskCaptureProps) {
  const [title, setTitle] = useState("");
  const [error, setError] = useState<string | null>(null);
  const { createTask } = useTaskMutations();

  const onInputKeyDown = (event: React.KeyboardEvent<HTMLInputElement>) => {
    if (event.key === "Escape") {
      event.preventDefault();
      event.stopPropagation();
      setTitle("");
      setError(null);
      onCancel?.();
      return;
    }
    if (event.key !== "Enter") return;
    event.preventDefault();

    // Parse the raw title for an end-anchored Polish date phrase (R4) — synchronous and
    // in-process, so the optimistic paint stays within one frame (SC-003).
    const parsed = parseTaskInput(title, new Date());

    if (parsed.error) {
      // EC-02/FR-006: create nothing, keep the value, announce politely without focus theft.
      setError(UNRECOGNIZED_MESSAGE);
      return;
    }

    // Today-view context: a dateless entry defaults to due-today (FR-107 clarification);
    // an explicit phrase in the input always wins.
    const contextDue =
      defaultDueToday && parsed.dueDate === undefined
        ? resolveDatePhrase("dzis", new Date())
        : null;

    const result = createTaskSchema.safeParse({
      title: parsed.title,
      dueDate: parsed.dueDate ?? contextDue?.dueDate,
      dueHasTime: parsed.dueHasTime ?? contextDue?.dueHasTime,
    });
    if (!result.success) return; // Empty after trim: nothing to create — a no-op.

    createTask({ ...result.data, projectId: contextProjectId });
    setTitle("");
    setError(null);
    onCreated?.();
  };

  return (
    <div className={styles.capture}>
      <span className={styles.plus} aria-hidden="true">
        <Plus size={15} strokeWidth={1.75} />
      </span>
      <input
        id={inputId}
        type="text"
        className={styles.input}
        aria-label="Nowy task"
        aria-describedby={errorId}
        placeholder="Nowy task… (np. „Raport jutro”)"
        maxLength={500}
        autoFocus={autoFocus}
        value={title}
        onChange={(event) => {
          setTitle(event.target.value);
          if (error) setError(null);
        }}
        onKeyDown={onInputKeyDown}
      />
      {/* Persistent polite status node: mounted always, fed empty text when there is no
          error, so the empty → "nie rozpoznano" transition is what a polite SR announces. */}
      <p id={errorId} className={styles.error} role="status" aria-live="polite">
        {error ?? ""}
      </p>
    </div>
  );
}

const GLOBAL_TITLE_ID = "global-capture-title";

/**
 * The global "+ Nowy task" capture host (T036/FR-107): a modal Dialog around the same
 * capture input, resolving the creation context from the CURRENT route per the 2026-08-09
 * clarification — Inbox → Inbox, project view → that project, Today → due today, all other
 * surfaces → Inbox.
 */
export function GlobalCaptureDialog({ open, onClose }: { open: boolean; onClose: () => void }) {
  const pathname = usePathname();
  const projectMatch = /^\/projects\/([^/]+)/.exec(pathname ?? "");
  const contextProjectId = projectMatch?.[1] ?? null;
  const defaultDueToday = pathname === "/today";

  if (!open) return null;
  return (
    <Dialog open={open} onClose={onClose} titleId={GLOBAL_TITLE_ID}>
      <h2 id={GLOBAL_TITLE_ID} className="sr-only">
        Nowy task
      </h2>
      {/* NO autoFocus here: the input's own autofocus fires during commit, BEFORE the
          Dialog's effect snapshots document.activeElement — the Dialog would remember the
          INPUT as the "invoker" and Esc could never return focus to the topbar button
          (FR-101). The Dialog's initial-focus step focuses the input instead. */}
      <TaskCapture
        contextProjectId={contextProjectId}
        defaultDueToday={defaultDueToday}
        onCreated={onClose}
        onCancel={onClose}
        errorId="global-capture-error"
      />
    </Dialog>
  );
}

"use client";

import { useState } from "react";

import { Dialog } from "@/components/ui/Dialog";
import { useTaskMutations } from "@/hooks/useTaskMutations";
import { parseTaskInput } from "@/lib/dates";
import { createTaskSchema } from "@/lib/validation/task";

const TITLE_ID = "task-capture-title";
const ERROR_ID = "task-capture-error";

/**
 * The recoverable-failure message (FR-006/EC-02): a trailing date *attempt* that
 * cannot resolve (e.g. "Spotkanie 30.02"). Surfaced verbatim per the spec.
 */
const UNRECOGNIZED_MESSAGE = "nie rozpoznano";

interface TaskCaptureProps {
  /** Whether the capture surface is visible — owned by the app-shell page (T058). */
  open: boolean;
  /** Dismiss handler — the page flips its `captureOpen` state (AS-07). */
  onClose: () => void;
}

/**
 * The `C` capture surface (T039/T058; US-01.AS-01/06/07, FR-031/FR-043, Constitution III).
 *
 * CONTROLLED (T058): the surface owns no `open` state and registers NO key listener of its
 * own — the global shortcut gate ({@link useGlobalShortcuts}, T054) owns the bare `C` (and
 * its FR-031/AS-09 text-field suppression) and drives `open`/`onClose` from the page. This
 * removes the duplicate `C` listener that previously lived here.
 *
 * The surface is mounted statically with NO network or lazy import on the `C` path: the
 * Dialog grants its FIRST focusable child (the single title `<input>`) initial focus
 * synchronously within one frame (≤16 ms), satisfying SC-003 / US-01.AS-01. Enter
 * parses the raw title for a trailing Polish date phrase (T012; R4), validates the
 * resulting create payload with the Zod schema (Constitution VI) and, when valid, drives
 * the optimistic create (T037) before clearing + closing (AS-06). A trailing date *attempt*
 * that cannot resolve creates nothing, keeps the field's value (EC-02), and shows a polite
 * "nie rozpoznano" message below the input (T017; FR-006/FR-101). Esc — handled entirely by
 * the Dialog focus contract — cancels, creates nothing, and restores focus to the
 * invoking element (AS-07).
 */
export function TaskCapture({ open, onClose }: TaskCaptureProps) {
  const [title, setTitle] = useState("");
  const [error, setError] = useState<string | null>(null);
  const { createTask } = useTaskMutations();

  const close = () => {
    setTitle("");
    setError(null);
    onClose();
  };

  const onInputKeyDown = (event: React.KeyboardEvent<HTMLInputElement>) => {
    if (event.key !== "Enter") return;
    event.preventDefault();

    // Parse the raw title for an end-anchored Polish date phrase (R4). The injected `now`
    // resolves relative phrases against the reference zone (R5/R9); this is synchronous and
    // in-process — no network, so the optimistic paint stays within one frame (SC-003).
    const parsed = parseTaskInput(title, new Date());

    if (parsed.error) {
      // A genuine trailing date attempt that cannot resolve (EC-02 / FR-006). Create nothing,
      // keep the field's value, and surface "nie rozpoznano" — announced via the polite
      // status region below WITHOUT stealing focus. No mutation fires, so the auto
      // MutationCache error-announcer never runs; this is the sole announcement (FR-101).
      setError(UNRECOGNIZED_MESSAGE);
      return;
    }

    // No date token → `{ title }` (full title); resolves → `{ title, dueDate, dueHasTime }`
    // (stripped prefix). Both flow through one create path — the due fields are simply
    // absent for a dateless task. `safeParse` is the empty-title no-op guard (mirrors the
    // prior `taskTitleSchema.safeParse`): empty/invalid → stay open, create nothing.
    const result = createTaskSchema.safeParse({
      title: parsed.title,
      dueDate: parsed.dueDate,
      dueHasTime: parsed.dueHasTime,
    });
    if (!result.success) return; // Empty after trim: nothing to create — a no-op, stay open.

    createTask(result.data);
    close();
  };

  return (
    <Dialog open={open} onClose={close} titleId={TITLE_ID}>
      <h2 id={TITLE_ID} className="tf-sr-only">
        Create task
      </h2>
      <input
        type="text"
        className="tf-task-capture__input"
        aria-label="Task title"
        placeholder="New task…"
        // Hard cap matching the Zod `.max(500)` so an over-length title can never be
        // entered — closes the silent-drop case where a >500 title would fail validation
        // and create nothing with no feedback (FIX 3; FR-049).
        maxLength={500}
        value={title}
        onChange={(event) => {
          setTitle(event.target.value);
          // Re-edit clears the recoverable-failure message (the user is fixing the phrase).
          if (error) setError(null);
        }}
        onKeyDown={onInputKeyDown}
      />
      {/*
        Single persistent polite status node (mirrors the slice-002 LiveRegion pattern,
        Toast.tsx lines 30-36): mounted with the dialog and fed empty text when there is no
        error, so the empty → "nie rozpoznano" transition is what a polite SR announces. A
        node mounted only on error may be skipped. Visible red text (≥4.5:1, T020) doubles as
        the on-screen affordance; non-focusable, so the Dialog Tab contract is undisturbed.
      */}
      <p id={ERROR_ID} className="tf-task-capture__error" role="status" aria-live="polite">
        {error ?? ""}
      </p>
    </Dialog>
  );
}

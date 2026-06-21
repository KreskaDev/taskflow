"use client";

import { useCallback, useEffect, useState } from "react";

import { Dialog } from "@/components/ui/Dialog";
import { useTaskMutations } from "@/hooks/useTaskMutations";
import { taskTitleSchema } from "@/lib/validation/task";

const TITLE_ID = "task-capture-title";

/**
 * The `C` capture surface (T039; US-01.AS-01/06/07, FR-031/FR-043, Constitution III).
 *
 * A document-level keydown listener opens a {@link Dialog} when the user presses an
 * unmodified `C` while NOT typing — `document.activeElement` is checked so a `C` typed
 * into an input/textarea/contenteditable is never hijacked (precursor to the US8 global
 * gate in T054; FR-031/AS-09). Ctrl/Cmd/Alt+C (copy etc.) are likewise ignored.
 *
 * The surface is mounted statically with NO network or lazy import on the `C` path: the
 * Dialog grants its FIRST focusable child (the single title `<input>`) initial focus
 * synchronously within one frame (≤16 ms), satisfying SC-003 / US-01.AS-01. Enter
 * validates the title with the Zod schema (Constitution VI) and, when valid, drives the
 * optimistic create (T037) before clearing + closing (AS-06). Esc — handled entirely by
 * the Dialog focus contract — cancels, creates nothing, and restores focus to the
 * invoking element (AS-07).
 */
export function TaskCapture() {
  const [open, setOpen] = useState(false);
  const [title, setTitle] = useState("");
  const { createTask } = useTaskMutations();

  const close = useCallback(() => {
    setOpen(false);
    setTitle("");
  }, []);

  useEffect(() => {
    const onKeyDown = (event: KeyboardEvent) => {
      if (event.key !== "c" && event.key !== "C") return;
      // Modifier chords (Ctrl/Cmd/Alt+C = copy etc.) are not the capture shortcut.
      if (event.ctrlKey || event.metaKey || event.altKey) return;

      // Never hijack a `C` the user is typing into an editable element (FR-031/AS-09).
      const active = document.activeElement;
      if (
        active instanceof HTMLInputElement ||
        active instanceof HTMLTextAreaElement ||
        (active instanceof HTMLElement && active.isContentEditable)
      ) {
        return;
      }

      event.preventDefault();
      setOpen(true);
    };

    document.addEventListener("keydown", onKeyDown);
    return () => document.removeEventListener("keydown", onKeyDown);
  }, []);

  const onInputKeyDown = (event: React.KeyboardEvent<HTMLInputElement>) => {
    if (event.key !== "Enter") return;
    event.preventDefault();

    // Trim mirrors the Zod schema's `.trim()`; `safeParse` returns the trimmed value
    // as `result.data`, which is what we create with (FIX 3; FR-049).
    const result = taskTitleSchema.safeParse(title);
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
        onChange={(event) => setTitle(event.target.value)}
        onKeyDown={onInputKeyDown}
      />
    </Dialog>
  );
}

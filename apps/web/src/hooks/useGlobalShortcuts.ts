"use client";

import { useEffect } from "react";

/**
 * The app-shell global keyboard gate (T054; FR-031/EC-08/AS-09, research R11/R18).
 *
 * A SINGLE document-level keydown listener that turns bare keys into task commands and,
 * crucially, SUPPRESSES bare keys while the user is typing in a text field so a `C`/`E`/`?`
 * etc. is never hijacked mid-word (FR-031). The FROZEN R18 Alt+↑/↓ reorder CHORD fires ONLY
 * while the role=listbox (not a text input) has focus, so the chord never reorders the
 * background task out from under a user typing in the capture / inline-rename input.
 *
 * The listener factory is PURE (no React render needed) — mirroring `createTaskMutationOptions`
 * — so it can be unit-tested by calling the returned function with a synthetic `KeyboardEvent`
 * while a real element holds focus. It reads `document.activeElement` (NOT `event.target`).
 */
export interface GlobalShortcutHandlers {
  /** `C` — open the capture surface. */
  onCapture?: () => void;
  /** `ArrowUp` — move selection up the listbox. */
  onMoveUp?: () => void;
  /** `ArrowDown` — move selection down the listbox. */
  onMoveDown?: () => void;
  /** `Space` — toggle the focused task's done state. */
  onToggle?: () => void;
  /** `E` — begin inline rename of the focused task. */
  onRename?: () => void;
  /** `Delete` — delete the focused task. */
  onDelete?: () => void;
  /** `?` — open the shortcuts help. */
  onHelp?: () => void;
  /** FROZEN reorder chord `Alt+ArrowUp` — move the focused task up one rank (R18). */
  onReorderUp?: () => void;
  /** FROZEN reorder chord `Alt+ArrowDown` — move the focused task down one rank (R18). */
  onReorderDown?: () => void;
}

/**
 * Is `document.activeElement` a text field the user could be typing into?
 *
 * Detected by tag / the `contenteditable` ATTRIBUTE / `role="textbox"` — NOT the flaky
 * `isContentEditable` property (jsdom does not compute it reliably). A bare key must not
 * be hijacked while one of these holds focus (FR-031/AS-09).
 */
function isTextFieldFocused(): boolean {
  const active = document.activeElement;
  if (active instanceof HTMLInputElement || active instanceof HTMLTextAreaElement) {
    return true;
  }
  if (active instanceof HTMLElement) {
    if (active.hasAttribute("contenteditable") && active.getAttribute("contenteditable") !== "false") {
      return true;
    }
    if (active.getAttribute("role") === "textbox") {
      return true;
    }
  }
  return false;
}

/**
 * Pure listener factory. The returned function dispatches a single matching handler per
 * keystroke (never two), calling `preventDefault()` on every key it handles — vital for the
 * FROZEN Alt+↑/↓ chord, which would otherwise page-scroll in Safari (R18).
 *
 * The reorder chord (Alt+↑/↓) is itself gated on the text-field check (R18): it fires only
 * while the role=listbox holds focus. Shift is part of normal typing, so `?` (Shift+/) is a
 * BARE key and IS suppressed inside a text field.
 */
export function createGlobalShortcutsListener(
  handlers: GlobalShortcutHandlers,
): (event: KeyboardEvent) => void {
  return (event: KeyboardEvent): void => {
    // The FROZEN reorder chord (R18). It fires ONLY while the role=listbox (not a text input)
    // has focus — so a focused capture/inline-rename input is NOT reordered out from under the
    // typing user. Inside a text field we leave the chord to native behaviour (e.g. macOS
    // Option+Arrow paragraph navigation) and do NOT preventDefault.
    if (event.altKey && !event.ctrlKey && !event.metaKey && !isTextFieldFocused()) {
      if (event.key === "ArrowUp") {
        event.preventDefault();
        handlers.onReorderUp?.();
        return;
      }
      if (event.key === "ArrowDown") {
        event.preventDefault();
        handlers.onReorderDown?.();
        return;
      }
    }

    // Any other modifier chord (Ctrl/Meta/Alt+X = copy/paste/native) is never a bare command.
    if (event.ctrlKey || event.metaKey || event.altKey) return;

    // Bare keys are suppressed while typing — left to land as a character (FR-031/AS-09).
    if (isTextFieldFocused()) return;

    switch (event.key) {
      case "c":
      case "C":
        event.preventDefault();
        handlers.onCapture?.();
        return;
      case "e":
      case "E":
        event.preventDefault();
        handlers.onRename?.();
        return;
      case "ArrowUp":
        event.preventDefault();
        handlers.onMoveUp?.();
        return;
      case "ArrowDown":
        event.preventDefault();
        handlers.onMoveDown?.();
        return;
      case " ":
        event.preventDefault();
        handlers.onToggle?.();
        return;
      case "Delete":
        event.preventDefault();
        handlers.onDelete?.();
        return;
      case "?":
        event.preventDefault();
        handlers.onHelp?.();
        return;
      default:
        return;
    }
  };
}

/**
 * "use client" hook wrapper. Installs exactly ONE `document` keydown listener via `useEffect`
 * and removes it on cleanup, mirroring the add/remove pattern in `TaskCapture.tsx`.
 */
export function useGlobalShortcuts(handlers: GlobalShortcutHandlers): void {
  useEffect(() => {
    const listener = createGlobalShortcutsListener(handlers);
    document.addEventListener("keydown", listener);
    return () => document.removeEventListener("keydown", listener);
  }, [handlers]);
}

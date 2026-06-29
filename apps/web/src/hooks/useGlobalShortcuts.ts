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
  /** `M` — open the move-to-project selector for the selected task (T041; FR-021/AS-05). */
  onMove?: () => void;
  /** `L` — open the label selector for the selected task (slice 006, US-08.AS-04). */
  onLabel?: () => void;
  /** `Delete` — delete the focused task. */
  onDelete?: () => void;
  /** `?` — open the shortcuts help. */
  onHelp?: () => void;
  /** FROZEN reorder chord `Alt+ArrowUp` — move the focused task up one rank (R18). */
  onReorderUp?: () => void;
  /** FROZEN reorder chord `Alt+ArrowDown` — move the focused task down one rank (R18). */
  onReorderDown?: () => void;
  /** `1`-`4` — set the selected task's priority (slice 005, AS-04: `1`→P0 … `4`→P3). */
  onSetPriority?: (priority: "P0" | "P1" | "P2" | "P3") => void;
  /** `T` — open the reschedule input for the selected task (slice 005, AS-05). */
  onReschedule?: () => void;
  /** `G I` — navigate to the Inbox (slice 005, US-08.AS-01). */
  onGoInbox?: () => void;
  /** `G T` — navigate to the Today view (slice 005, US-02.AS-01). */
  onGoToday?: () => void;
  /** `G U` — navigate to the Upcoming view (slice 005, US-08.AS-02). */
  onGoUpcoming?: () => void;
  /** `G A` — navigate to the "Assigned to me" view (slice 008, AS-03). */
  onGoAssigned?: () => void;
  /** `A` — open the assignee picker for the selected shared-project task (slice 008, AS-01). */
  onAssign?: () => void;
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
  if (
    active instanceof HTMLInputElement ||
    active instanceof HTMLTextAreaElement ||
    // A focused <select> (the editor's priority/project fields) must also swallow single-key shortcuts
    // (FR-031), else Space/1-4/T/E leak to the global gate while the dropdown has focus.
    active instanceof HTMLSelectElement
  ) {
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
  // The `G`-prefix navigation chord (slice 005, R8): the first bare `G` arms a pending state; the next
  // key (`I`/`T`/`U`) navigates. State lives in the listener closure (reset whenever the handler set
  // changes, i.e. on re-bind). `T` is overloaded — `G T` navigates, a bare `T` reschedules — so the
  // pending flag disambiguates.
  let awaitingGoto = false;

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

    // The second leg of the `G`-prefix navigation chord (slice 005, R8). Consumes `I`/`T`/`U` and
    // resets the pending state; any other key aborts the chord. Checked BEFORE the bare-key switch so a
    // `G T` is "go to Today", not a bare-`T` reschedule.
    if (awaitingGoto) {
      awaitingGoto = false;
      switch (event.key) {
        case "i":
        case "I":
          event.preventDefault();
          handlers.onGoInbox?.();
          return;
        case "t":
        case "T":
          event.preventDefault();
          handlers.onGoToday?.();
          return;
        case "u":
        case "U":
          event.preventDefault();
          handlers.onGoUpcoming?.();
          return;
        case "a":
        case "A":
          event.preventDefault();
          handlers.onGoAssigned?.();
          return;
        default:
          return; // aborted chord — swallow the stray second key
      }
    }

    switch (event.key) {
      case "g":
      case "G":
        // Arm the navigation chord only when a nav handler is wired (else `G` is inert, preserving the
        // slice-002 behaviour where `G` did nothing).
        if (handlers.onGoInbox || handlers.onGoToday || handlers.onGoUpcoming || handlers.onGoAssigned) {
          event.preventDefault();
          awaitingGoto = true;
        }
        return;
      case "a":
      case "A":
        // Bare `A` opens the assignee picker on the selected task (slice 008). Only when wired (a `G A`
        // navigation was handled above by the chord); inert elsewhere so the key stays free.
        if (handlers.onAssign) {
          event.preventDefault();
          handlers.onAssign();
        }
        return;
      case "1":
      case "2":
      case "3":
      case "4":
        // `1`→P0 … `4`→P3 (AS-04). Only handled when wired, so the key stays inert elsewhere.
        if (handlers.onSetPriority) {
          event.preventDefault();
          handlers.onSetPriority((["P0", "P1", "P2", "P3"] as const)[Number(event.key) - 1]!);
        }
        return;
      case "t":
      case "T":
        // Bare `T` reschedules the selected task (AS-05). Only when wired (a `G T` was handled above).
        if (handlers.onReschedule) {
          event.preventDefault();
          handlers.onReschedule();
        }
        return;
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
      case "m":
      case "M":
        // After the `isTextFieldFocused()` guard above, so `M` is suppressed mid-word in the
        // capture / inline-rename input (FR-031). Opens the move-to-project selector (AS-05).
        event.preventDefault();
        handlers.onMove?.();
        return;
      case "l":
      case "L":
        // Bare `L` opens the label selector on the selected task (slice 006, US-08.AS-04). Suppressed
        // mid-word by the text-field guard above (FR-031); does not collide with native AT bindings
        // (FR-045 — same class as E/M/A). Only when wired, so the key stays inert elsewhere.
        if (handlers.onLabel) {
          event.preventDefault();
          handlers.onLabel();
        }
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

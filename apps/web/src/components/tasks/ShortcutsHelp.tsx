"use client";

import { Dialog } from "@/components/ui/Dialog";

const TITLE_ID = "shortcuts-help-title";

interface ShortcutsHelpProps {
  /** Whether the help overlay is visible. */
  open: boolean;
  /** Dismiss handler — the owning shell (T054) flips its `open` state. */
  onClose: () => void;
}

/** One row of the static shortcut reference: the key token(s) and what they do. */
interface Shortcut {
  keys: string[];
  action: string;
}

/**
 * The complete slice-002 keyboard grammar (Constitution I). The Alt+↑/↓ reorder pair is
 * the FROZEN R18 binding (T003) — listed here for discoverability only; the live binding,
 * `aria-keyshortcuts`, and the `preventDefault`/listbox-focus mitigations live on the
 * listbox (TaskList/T058), not in this static reference.
 */
const SHORTCUTS: readonly Shortcut[] = [
  { keys: ["C"], action: "Create a task" },
  { keys: ["↑"], action: "Move selection up" },
  { keys: ["↓"], action: "Move selection down" },
  { keys: ["Alt", "↑"], action: "Reorder selected task up" },
  { keys: ["Alt", "↓"], action: "Reorder selected task down" },
  { keys: ["Space"], action: "Toggle done" },
  { keys: ["E"], action: "Rename the selected task" },
  { keys: ["Del"], action: "Delete the selected task" },
  { keys: ["Esc"], action: "Cancel the current action" },
  { keys: ["?"], action: "Show this help" },
];

/**
 * The `?` keyboard-shortcuts help overlay (T056; FR-043, Constitution I/II).
 *
 * A CONTROLLED, presentation-only component: it owns no state and registers no key
 * listener — the global shortcut gate (T054) handles `?` and drives `open`/`onClose`.
 * It reuses {@link Dialog}'s FR-101 focus contract (initial focus into the dialog, focus
 * trap, Esc dismiss via `onClose`, focus-restore to the invoker on close) and renders a
 * static, accessible `<table>` of EVERY slice-002 shortcut. The visible `<h2>` is wired as
 * the dialog's accessible name through `titleId` (FR-043).
 *
 * Motion (FR-047): this overlay declares no bespoke transition/animation, and globals.css
 * already zeroes all transitions/animations under `prefers-reduced-motion: reduce`, so the
 * overlay appears instantly for users who request reduced motion — nothing to gate here.
 */
export function ShortcutsHelp({ open, onClose }: ShortcutsHelpProps) {
  return (
    <Dialog open={open} onClose={onClose} titleId={TITLE_ID}>
      <h2 id={TITLE_ID}>Keyboard shortcuts</h2>
      <table className="tf-shortcuts">
        <thead>
          <tr>
            <th scope="col">Shortcut</th>
            <th scope="col">Action</th>
          </tr>
        </thead>
        <tbody>
          {SHORTCUTS.map((shortcut) => (
            <tr key={shortcut.action}>
              <td className="tf-shortcuts__keys">
                {shortcut.keys.map((key, index) => (
                  // The chord separator is decorative; the action cell carries the meaning.
                  <span key={key}>
                    {index > 0 ? <span aria-hidden="true"> + </span> : null}
                    <kbd className="tf-kbd">{key}</kbd>
                  </span>
                ))}
              </td>
              <td>{shortcut.action}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </Dialog>
  );
}

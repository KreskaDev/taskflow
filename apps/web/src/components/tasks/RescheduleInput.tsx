"use client";

import { useState } from "react";

import { Dialog } from "@/components/ui/Dialog";
import { resolveDatePhrase } from "@/lib/dates";

const TITLE_ID = "reschedule-input-title";

interface RescheduleInputProps {
  /** Whether the reschedule input is open (the `T` key opened it for the selected task). */
  open: boolean;
  /** Dismiss without rescheduling (Esc / overlay click) — returns focus to the originating row. */
  onClose: () => void;
  /**
   * Commit a resolved due date (or `null`/`null` to clear). The client resolves the Polish phrase here
   * (slice-003 grammar) and hands the resolved instant to the parent, which calls `rescheduleTask`.
   */
  onSubmit: (dueDate: Date | null, dueHasTime: boolean | null) => void;
}

/**
 * The `T` reschedule input (slice 005, AS-05). A modal {@link Dialog} (FR-101 focus contract: initial
 * focus into the field, trap, Esc dismiss, return focus to the originating row). The user types a Polish
 * date phrase ("jutro", "piatek", "30.06", "za 3 dni"); on Enter the client resolves it against
 * `Europe/Warsaw` via {@link resolveDatePhrase} and submits the instant — an empty input clears the due
 * date, an unrecognized phrase shows an inline error (FR-049) without closing. Single-key shortcuts are
 * suppressed while the field is focused (FR-031, the global gate's text-field guard).
 */
export function RescheduleInput({ open, onClose, onSubmit }: RescheduleInputProps) {
  const [value, setValue] = useState("");
  const [error, setError] = useState<string | null>(null);

  const onKeyDown = (event: React.KeyboardEvent<HTMLInputElement>) => {
    if (event.key !== "Enter") return;
    event.preventDefault();

    const trimmed = value.trim();
    if (trimmed.length === 0) {
      onSubmit(null, null); // empty → clear the due date
      reset();
      return;
    }

    const resolved = resolveDatePhrase(trimmed, new Date());
    if (!resolved) {
      setError("Nie rozpoznano daty. Spróbuj np. „jutro”, „piątek”, „30.06”.");
      return;
    }
    onSubmit(resolved.dueDate, resolved.dueHasTime);
    reset();
  };

  const reset = () => {
    setValue("");
    setError(null);
  };

  const close = () => {
    reset();
    onClose();
  };

  if (!open) return null;

  return (
    <Dialog open={open} onClose={close} titleId={TITLE_ID} descriptionId={error ? "reschedule-input-error" : undefined}>
      <h2 id={TITLE_ID} className="tf-dialog__title">
        Zmień termin
      </h2>
      <input
        type="text"
        className="tf-reschedule-input__field"
        aria-label="Nowy termin (np. jutro, piątek, 30.06)"
        autoFocus
        value={value}
        onChange={(event) => {
          setValue(event.target.value);
          if (error) setError(null);
        }}
        onKeyDown={onKeyDown}
      />
      {error ? (
        <p id="reschedule-input-error" className="tf-reschedule-input__error" role="alert">
          {error}
        </p>
      ) : null}
    </Dialog>
  );
}

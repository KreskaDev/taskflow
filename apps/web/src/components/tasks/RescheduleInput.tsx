"use client";

import { Dialog, DialogTitle } from "@/components/ui/Dialog";
import { DateInput } from "@/components/ui/DateInput";

const TITLE_ID = "reschedule-input-title";

interface RescheduleInputProps {
  /** Whether the reschedule dialog is open (menu "Termin…" / drawer). */
  open: boolean;
  /** Dismiss without rescheduling (Esc / overlay click) — returns focus to the invoker. */
  onClose: () => void;
  /**
   * Commit a resolved due date (or `null`/`null` to clear). The catalog {@link DateInput}
   * resolves the Polish phrase against Europe/Warsaw (slice-003 grammar unchanged).
   */
  onSubmit: (dueDate: Date | null, dueHasTime: boolean | null) => void;
}

/**
 * The reschedule dialog (slice 005, migrated onto the catalog DateInput in slice 019 —
 * T057; Polish NL parse + FR-006 error presentation unchanged). A modal {@link Dialog}
 * (FR-101 focus contract); an empty input clears the due date; an unrecognized phrase
 * shows the inline error without closing.
 */
export function RescheduleInput({ open, onClose, onSubmit }: RescheduleInputProps) {
  if (!open) return null;
  return (
    <Dialog open={open} onClose={onClose} titleId={TITLE_ID}>
      <DialogTitle id={TITLE_ID}>Zmień termin</DialogTitle>
      <DateInput
        id="reschedule-input"
        label="Nowy termin (np. jutro, piątek, 30.06)"
        onCommit={(dueDate, dueHasTime) => {
          onSubmit(dueDate, dueHasTime);
        }}
        onCancel={onClose}
      />
    </Dialog>
  );
}

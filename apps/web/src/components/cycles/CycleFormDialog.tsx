"use client";

import { useEffect, useId, useState } from "react";

import { Button } from "@/components/ui/Button";
import { Dialog, DialogActions, DialogTitle } from "@/components/ui/Dialog";
import { Input } from "@/components/ui/Input";
import { dateInputToUtcIso, utcIsoToDateInput } from "@/lib/cycles";
import styles from "./CycleFormDialog.module.css";

export interface CycleFormFields {
  name: string;
  /** ISO UTC instants on the Warsaw-midnight date-only convention (D18). */
  startDate: string;
  endDate: string;
}

interface CycleFormDialogProps {
  open: boolean;
  /** Dialog heading + submit copy: „Nowy cykl" (create) vs „Edytuj cykl" (edit). */
  mode: "create" | "edit";
  /** Create: the D18 pre-fill (`cycleFormPrefill`); edit: the cycle's current fields. */
  initial: CycleFormFields;
  onClose: () => void;
  onSubmit: (fields: CycleFormFields) => void;
}

/**
 * The create/edit cycle dialog (slice 011, T018 — FR-020, contract ui-cycle.md): name (pre-filled
 * „Cykl N", editable) + date-only start/end inputs (pre-fill per D18/D8), with INLINE validation —
 * name required, `start < end` (the only cross-field rule, Clarifications). Values commit as
 * Warsaw-midnight UTC instants; server errors surface via the FR-049 toast path.
 */
export function CycleFormDialog({ open, mode, initial, onClose, onSubmit }: CycleFormDialogProps) {
  const titleId = useId();
  const [name, setName] = useState(initial.name);
  const [start, setStart] = useState(utcIsoToDateInput(initial.startDate));
  const [end, setEnd] = useState(utcIsoToDateInput(initial.endDate));
  const [nameError, setNameError] = useState<string | null>(null);
  const [dateError, setDateError] = useState<string | null>(null);

  // Re-seed the fields each time the dialog (re)opens with a fresh pre-fill/cycle.
  useEffect(() => {
    if (!open) return;
    setName(initial.name);
    setStart(utcIsoToDateInput(initial.startDate));
    setEnd(utcIsoToDateInput(initial.endDate));
    setNameError(null);
    setDateError(null);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [open, initial.name, initial.startDate, initial.endDate]);

  if (!open) return null;

  const submit = (): void => {
    const trimmed = name.trim();
    const missingName = trimmed.length === 0;
    const badDates = !start || !end || start >= end;
    setNameError(missingName ? "Podaj nazwę cyklu." : null);
    setDateError(badDates ? "Data początku musi być wcześniejsza niż data końca." : null);
    if (missingName || badDates) return;
    onSubmit({ name: trimmed, startDate: dateInputToUtcIso(start), endDate: dateInputToUtcIso(end) });
  };

  return (
    <Dialog open={open} onClose={onClose} titleId={titleId}>
      <DialogTitle id={titleId}>{mode === "create" ? "Nowy cykl" : "Edytuj cykl"}</DialogTitle>
      <form
        className={styles.form}
        onSubmit={(e) => {
          e.preventDefault();
          submit();
        }}
      >
        <label className={styles.field}>
          <span className={styles.label}>Nazwa</span>
          <Input
            value={name}
            onChange={(e) => setName(e.target.value)}
            error={nameError ?? undefined}
            autoFocus
          />
        </label>
        <div className={styles.dates}>
          <label className={styles.field}>
            <span className={styles.label}>Początek</span>
            <Input type="date" value={start} onChange={(e) => setStart(e.target.value)} />
          </label>
          <label className={styles.field}>
            <span className={styles.label}>Koniec</span>
            <Input type="date" value={end} onChange={(e) => setEnd(e.target.value)} />
          </label>
        </div>
        {dateError ? (
          <p className={styles.error} role="alert">
            {dateError}
          </p>
        ) : null}
        <DialogActions>
          <Button variant="secondary" onClick={onClose}>
            Anuluj
          </Button>
          <Button variant="primary" type="submit">
            {mode === "create" ? "Utwórz" : "Zapisz"}
          </Button>
        </DialogActions>
      </form>
    </Dialog>
  );
}

"use client";

import { useEffect, useId, useState } from "react";

import { Button } from "@/components/ui/Button";
import { Input } from "@/components/ui/Input";
import { useMe, usePreferencesMutation } from "@/hooks/useMe";
import styles from "./CycleDurationField.module.css";

/**
 * The /settings FR-015 preference (slice 011, T021 — D8): „Domyślna długość cyklu (dni)"
 * (1..90), seeded from `GET /api/users/me` and persisted server-side via
 * `PATCH /api/users/me/preferences` (the save announces politely through the toast
 * LiveRegion). The create-cycle dialog's end-date pre-fill follows this value (D18).
 */
export function CycleDurationField() {
  const inputId = useId();
  const { data: me } = useMe();
  const { setCycleDefaultDuration } = usePreferencesMutation();
  const [value, setValue] = useState<string>("");
  const [error, setError] = useState<string | null>(null);

  // Seed (and re-seed after a settle) from the server-side profile value — keyed on the
  // PRIMITIVE so an identity-fresh profile object cannot clobber an in-progress edit.
  const seeded = me?.cycleDefaultDurationDays;
  useEffect(() => {
    if (seeded !== undefined) setValue(String(seeded));
  }, [seeded]);

  const save = async (): Promise<void> => {
    const days = Number(value);
    if (!Number.isInteger(days) || days < 1 || days > 90) {
      setError("Podaj liczbę dni od 1 do 90.");
      return;
    }
    setError(null);
    try {
      await setCycleDefaultDuration(days);
    } catch {
      // FR-049: the failure is announced by the global mutation announcer; the value stays editable.
    }
  };

  return (
    <form
      className={styles.field}
      noValidate
      onSubmit={(e) => {
        e.preventDefault();
        void save();
      }}
    >
      <label htmlFor={inputId} className={styles.label}>
        Domyślna długość cyklu (dni)
      </label>
      <div className={styles.controls}>
        <Input
          id={inputId}
          type="number"
          min={1}
          max={90}
          value={value}
          onChange={(e) => setValue(e.target.value)}
          error={error ?? undefined}
        />
        <Button type="submit" variant="secondary">
          Zapisz
        </Button>
      </div>
    </form>
  );
}

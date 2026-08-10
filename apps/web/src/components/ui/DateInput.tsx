"use client";

import { useState } from "react";
import { resolveDatePhrase } from "@/lib/dates";
import styles from "./DateInput.module.css";

interface DateInputProps {
  /** Accessible name of the field. */
  label: string;
  /**
   * Commit a resolved due date. Enter on a recognized Polish phrase resolves it against
   * Europe/Warsaw (slice-003 grammar); an EMPTY input commits `null`/`null` (clear).
   */
  onCommit: (dueDate: Date | null, dueHasTime: boolean | null) => void;
  /** Esc inside the field (FR-030 editing key) — cancel the edit without committing. */
  onCancel?: () => void;
  placeholder?: string;
  autoFocus?: boolean;
  id?: string;
}

/**
 * Catalog natural-language date input (T009) — the extracted presentation of the
 * slice-005 RescheduleInput: type "jutro" / "piątek" / "30.06" / "za 3 dni", Enter
 * resolves via {@link resolveDatePhrase}; an unrecognized phrase shows the FR-006
 * error IN PLACE (input keeps its value for correction, no commit). Consumed by the
 * reschedule dialog (T057) and the task drawer (T051).
 */
export function DateInput({
  label,
  onCommit,
  onCancel,
  placeholder = "np. jutro, piątek, 30.06",
  autoFocus,
  id,
}: DateInputProps) {
  const [value, setValue] = useState("");
  const [error, setError] = useState<string | null>(null);
  const errorId = id ? `${id}-error` : "date-input-error";

  const onKeyDown = (event: React.KeyboardEvent<HTMLInputElement>) => {
    if (event.key === "Escape" && onCancel) {
      event.preventDefault();
      event.stopPropagation();
      onCancel();
      return;
    }
    if (event.key !== "Enter") return;
    event.preventDefault();

    const trimmed = value.trim();
    if (trimmed.length === 0) {
      onCommit(null, null);
      return;
    }
    const resolved = resolveDatePhrase(trimmed, new Date());
    if (!resolved) {
      setError("Nie rozpoznano daty. Spróbuj np. „jutro”, „piątek”, „30.06”.");
      return;
    }
    onCommit(resolved.dueDate, resolved.dueHasTime);
  };

  return (
    <>
      <input
        id={id}
        type="text"
        className={[styles.dateInput, error ? styles.invalid : null].filter(Boolean).join(" ")}
        aria-label={label}
        aria-invalid={error ? true : undefined}
        aria-describedby={error ? errorId : undefined}
        placeholder={placeholder}
        autoFocus={autoFocus}
        value={value}
        onChange={(event) => {
          setValue(event.target.value);
          if (error) setError(null);
        }}
        onKeyDown={onKeyDown}
      />
      {error ? (
        <p id={errorId} className={styles.error} role="alert">
          {error}
        </p>
      ) : null}
    </>
  );
}

"use client";

import { useEffect, useId, useRef, useState } from "react";

import {
  formatNextOccurrence,
  MAX_INTERVAL,
  MIN_INTERVAL,
  monthlyAnchorFor,
  RECURRENCE_TYPE_LABELS,
  type RecurrenceRule,
  type RecurrenceType,
} from "@/lib/recurrence";
import styles from "./RecurrenceField.module.css";

export const RECURRENCE_NO_DUE_HINT = "Ustaw najpierw termin";
export const RECURRENCE_WEEKDAYS_ERROR = "Wybierz co najmniej jeden dzień";
export const RECURRENCE_INTERVAL_ERROR = `Podaj liczbę od ${String(MIN_INTERVAL)} do ${String(MAX_INTERVAL)}`;

/** What the field reports on every change: the rule (null = „Nie powtarza się") and a blocking inline error, if any. */
export interface RecurrenceFieldChange {
  rule: RecurrenceRule | null;
  error: string | null;
}

interface RecurrenceFieldProps {
  /** The task's current rule (seeds the drafts). */
  rule: RecurrenceRule | null;
  /** The task's current due (the anchor the preview and the monthly anchor derive from); null disables the field. */
  dueDate: Date | null;
  dueHasTime: boolean;
  onChange: (change: RecurrenceFieldChange) => void;
  /** Extra disabled state imposed by the host (e.g. a pending save). */
  disabled?: boolean;
}

const WEEKDAYS: ReadonlyArray<{ iso: number; short: string; full: string }> = [
  { iso: 1, short: "Pn", full: "poniedziałek" },
  { iso: 2, short: "Wt", full: "wtorek" },
  { iso: 3, short: "Śr", full: "środa" },
  { iso: 4, short: "Cz", full: "czwartek" },
  { iso: 5, short: "Pt", full: "piątek" },
  { iso: 6, short: "So", full: "sobota" },
  { iso: 7, short: "Nd", full: "niedziela" },
];

const TYPE_ORDER: ReadonlyArray<RecurrenceType | "none"> = ["none", "daily", "every_n_days", "weekdays", "monthly"];

interface Draft {
  type: RecurrenceType | "none";
  interval: string;
  weekdays: number[];
  dayOfMonth: number;
}

function draftFrom(rule: RecurrenceRule | null, dueDate: Date | null): Draft {
  const anchor = dueDate ? monthlyAnchorFor(dueDate) : 1;
  switch (rule?.type) {
    case "daily":
      return { type: "daily", interval: "2", weekdays: [], dayOfMonth: anchor };
    case "every_n_days":
      return { type: "every_n_days", interval: String(rule.interval), weekdays: [], dayOfMonth: anchor };
    case "weekdays":
      return { type: "weekdays", interval: "2", weekdays: [...rule.weekdays], dayOfMonth: anchor };
    case "monthly":
      return { type: "monthly", interval: "2", weekdays: [], dayOfMonth: rule.dayOfMonth };
    default:
      return { type: "none", interval: "2", weekdays: [], dayOfMonth: anchor };
  }
}

/** Derives the rule + blocking error from a draft (the field's single source of truth for validity). */
function resolveRecurrenceDraft(draft: Draft): RecurrenceFieldChange {
  switch (draft.type) {
    case "none":
      return { rule: null, error: null };
    case "daily":
      return { rule: { type: "daily" }, error: null };
    case "every_n_days": {
      const interval = Number(draft.interval);
      if (!Number.isInteger(interval) || interval < MIN_INTERVAL || interval > MAX_INTERVAL) {
        return { rule: null, error: RECURRENCE_INTERVAL_ERROR };
      }
      return { rule: { type: "every_n_days", interval }, error: null };
    }
    case "weekdays":
      if (draft.weekdays.length === 0) {
        return { rule: null, error: RECURRENCE_WEEKDAYS_ERROR };
      }
      return { rule: { type: "weekdays", weekdays: [...draft.weekdays].sort((a, b) => a - b) }, error: null };
    case "monthly":
      return { rule: { type: "monthly", dayOfMonth: draft.dayOfMonth }, error: null };
  }
}

/**
 * The shared „Powtarzanie" field (slice 012, US-06.AS-01 — contracts/ui-recurrence.md), hosted by the
 * `E` task editor and the task details drawer (research D10). A labelled native `<select>` over the five
 * options, per-type inputs beneath it, and the „Następne wystąpienie" preview computed from the task's
 * CURRENT due by the client mirror of the server calendar (D3 — preview only; the server spawn is
 * authoritative). Without a due date the select is disabled with the visible hint „Ustaw najpierw termin"
 * (recurrence requires a due date). Every control is a native element (WCAG keyboard operability,
 * FR-042/FR-043); the error is inline text (FR-049), never color-only.
 */
export function RecurrenceField({ rule, dueDate, dueHasTime, onChange, disabled = false }: RecurrenceFieldProps) {
  const id = useId();
  const [draft, setDraft] = useState<Draft>(() => draftFrom(rule, dueDate));

  // Re-seed when the host switches tasks or the rule changes underneath (drawer deep link / server settle).
  // Keyed by VALUE (the hosts rebuild `rule`/`dueDate` objects every render) so an in-progress draft is not reset.
  // A controlled host echoes what this field just reported (incl. `null` while a draft is still invalid —
  // „W wybrane dni tygodnia" with no day yet); that echo must NOT reset the draft, only an EXTERNAL change may.
  const ruleKey = JSON.stringify(rule);
  const dueKey = dueDate?.getTime() ?? null;
  const latest = useRef({ rule, dueDate });
  latest.current = { rule, dueDate };
  const lastEmitted = useRef<string>(ruleKey);
  useEffect(() => {
    if (ruleKey === lastEmitted.current) return;
    lastEmitted.current = ruleKey;
    setDraft(draftFrom(latest.current.rule, latest.current.dueDate));
  }, [ruleKey, dueKey]);

  const update = (next: Draft): void => {
    setDraft(next);
    const resolved = resolveRecurrenceDraft(next);
    lastEmitted.current = JSON.stringify(resolved.rule);
    onChange(resolved);
  };

  const noDue = dueDate === null;
  const isDisabled = disabled || noDue;
  const resolved = resolveRecurrenceDraft(draft);
  const selectId = `${id}-type`;
  const hintId = `${id}-hint`;
  const errorId = `${id}-error`;

  return (
    <div className={styles.field} data-testid="recurrence-field">
      <label className={styles.label} htmlFor={selectId}>
        Powtarzanie
      </label>
      <select
        id={selectId}
        className={styles.select}
        value={draft.type}
        disabled={isDisabled}
        aria-describedby={noDue ? hintId : resolved.error ? errorId : undefined}
        onChange={(event) => {
          const type = event.target.value as RecurrenceType | "none";
          update({ ...draft, type, dayOfMonth: dueDate ? monthlyAnchorFor(dueDate) : draft.dayOfMonth });
        }}
      >
        {TYPE_ORDER.map((type) => (
          <option key={type} value={type}>
            {RECURRENCE_TYPE_LABELS[type]}
          </option>
        ))}
      </select>

      {noDue ? (
        <p id={hintId} className={styles.hint}>
          {RECURRENCE_NO_DUE_HINT}
        </p>
      ) : null}

      {!noDue && draft.type === "every_n_days" ? (
        <label className={styles.sub}>
          <span>Co ile dni</span>
          <input
            type="number"
            className={styles.number}
            min={MIN_INTERVAL}
            max={MAX_INTERVAL}
            step={1}
            inputMode="numeric"
            value={draft.interval}
            disabled={isDisabled}
            aria-invalid={resolved.error ? true : undefined}
            aria-describedby={resolved.error ? errorId : undefined}
            onChange={(event) => update({ ...draft, interval: event.target.value })}
          />
        </label>
      ) : null}

      {!noDue && draft.type === "weekdays" ? (
        <fieldset className={styles.weekdays} aria-describedby={resolved.error ? errorId : undefined}>
          <legend className={styles.legend}>Dni tygodnia</legend>
          {WEEKDAYS.map((day) => {
            const checked = draft.weekdays.includes(day.iso);
            return (
              <label key={day.iso} className={styles.weekday}>
                <input
                  type="checkbox"
                  checked={checked}
                  disabled={isDisabled}
                  aria-label={day.full}
                  onChange={() =>
                    update({
                      ...draft,
                      weekdays: checked ? draft.weekdays.filter((d) => d !== day.iso) : [...draft.weekdays, day.iso],
                    })
                  }
                />
                <span aria-hidden="true">{day.short}</span>
              </label>
            );
          })}
        </fieldset>
      ) : null}

      {!noDue && draft.type === "monthly" ? (
        <p className={styles.sub}>co miesiąc, {draft.dayOfMonth}. dnia</p>
      ) : null}

      {resolved.error ? (
        <p id={errorId} className={styles.error} role="alert">
          {resolved.error}
        </p>
      ) : null}

      {!noDue && resolved.rule && dueDate ? (
        <p className={styles.preview} data-visual-hide>
          {formatNextOccurrence(resolved.rule, dueDate, dueHasTime)}
        </p>
      ) : null}
    </div>
  );
}

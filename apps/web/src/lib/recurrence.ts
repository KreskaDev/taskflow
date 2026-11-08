import { addDays, addMonths, getDaysInMonth, getISODay, setDate, startOfMonth } from "date-fns";
import { z } from "zod";

import { formatInReferenceZone, fromReferenceZone, referenceDateKey, toReferenceZone } from "@/lib/timezone";

/**
 * The recurrence rule (ENT-05, slice 012 — contracts/recurrence-api.md D1): the closed union
 * mirrored from the server DTO. Validated at the trust boundary (Constitution VI) before it rides
 * the whole-object `editTask` save. ISO weekdays: Monday = 1 … Sunday = 7.
 */
export const recurrenceRuleSchema = z.discriminatedUnion("type", [
  z.object({ type: z.literal("daily") }),
  z.object({ type: z.literal("every_n_days"), interval: z.number().int().min(2).max(365) }),
  z.object({
    type: z.literal("weekdays"),
    weekdays: z
      .array(z.number().int().min(1).max(7))
      .min(1)
      .refine((days) => new Set(days).size === days.length, { message: "weekdays must be distinct" }),
  }),
  z.object({ type: z.literal("monthly"), dayOfMonth: z.number().int().min(1).max(31) }),
]);

export type RecurrenceRule = z.infer<typeof recurrenceRuleSchema>;

export type RecurrenceType = RecurrenceRule["type"];

/** The wire shape as the generated client carries it (flat, optional members) — normalized by {@link parseRecurrenceRule}. */
export interface RecurrenceRuleWire {
  type: string;
  interval?: number | null;
  weekdays?: number[] | null;
  dayOfMonth?: number | null;
}

export const MIN_INTERVAL = 2;
export const MAX_INTERVAL = 365;

/** ISO weekday → the Polish two-letter abbreviation used by the field and the row chip. */
const WEEKDAY_ABBREVIATIONS: Readonly<Record<number, string>> = {
  1: "pn",
  2: "wt",
  3: "śr",
  4: "cz",
  5: "pt",
  6: "so",
  7: "nd",
};

/** The field's option labels, in display order (contracts/ui-recurrence.md). */
export const RECURRENCE_TYPE_LABELS: Readonly<Record<RecurrenceType | "none", string>> = {
  none: "Nie powtarza się",
  daily: "Codziennie",
  every_n_days: "Co N dni",
  weekdays: "W wybrane dni tygodnia",
  monthly: "Co miesiąc",
};

/**
 * Normalizes a wire rule (flat DTO, possibly null/undefined) into the typed union, or `null` when absent.
 * An invalid document is reported as `null` + a boundary error by the caller (never silently coerced).
 */
export function parseRecurrenceRule(wire: RecurrenceRuleWire | null | undefined): RecurrenceRule | null {
  if (!wire) return null;
  const candidate: Record<string, unknown> = { type: wire.type };
  if (wire.interval != null) candidate.interval = wire.interval;
  if (wire.weekdays != null) candidate.weekdays = [...wire.weekdays].sort((a, b) => a - b);
  if (wire.dayOfMonth != null) candidate.dayOfMonth = wire.dayOfMonth;
  const parsed = recurrenceRuleSchema.safeParse(candidate);
  return parsed.success ? parsed.data : null;
}

/** The flat wire shape the generated client expects for `editTask` (null members omitted). */
export function toWireRule(rule: RecurrenceRule | null): RecurrenceRuleWire | null {
  if (!rule) return null;
  switch (rule.type) {
    case "daily":
      return { type: "daily" };
    case "every_n_days":
      return { type: "every_n_days", interval: rule.interval };
    case "weekdays":
      return { type: "weekdays", weekdays: [...rule.weekdays].sort((a, b) => a - b) };
    case "monthly":
      return { type: "monthly", dayOfMonth: rule.dayOfMonth };
  }
}

/**
 * The FIRST occurrence strictly after `dueDate` in Europe/Warsaw (research D3 — the client mirror of the
 * Domain's `RecurrenceRule.NextDueAfter`, asserted against the same fact table). Date-only dues return the
 * Warsaw-midnight instant of the new date; timed dues keep the same Warsaw wall-clock time across DST
 * (date-fns-tz re-interprets the wall-clock in the zone — never fixed-offset arithmetic, FR-092).
 * Monthly rules clamp to the target month's length while the anchor `dayOfMonth` is preserved in the rule.
 * This value is a PREVIEW; the server's successor is authoritative (Principle V).
 */
export function nextOccurrence(rule: RecurrenceRule, dueDate: Date, dueHasTime: boolean): Date {
  const wall = toReferenceZone(dueDate); // fields read as Warsaw wall-clock
  let nextWall: Date;
  switch (rule.type) {
    case "daily":
      nextWall = addDays(wall, 1);
      break;
    case "every_n_days":
      nextWall = addDays(wall, rule.interval);
      break;
    case "weekdays": {
      nextWall = wall;
      for (let offset = 1; offset <= 7; offset += 1) {
        const candidate = addDays(wall, offset);
        if (rule.weekdays.includes(getISODay(candidate))) {
          nextWall = candidate;
          break;
        }
      }
      break;
    }
    case "monthly": {
      const firstOfNext = addMonths(startOfMonth(wall), 1);
      const day = Math.min(rule.dayOfMonth, getDaysInMonth(firstOfNext));
      nextWall = setDate(firstOfNext, day);
      // keep the wall-clock time of the anchor
      nextWall.setHours(wall.getHours(), wall.getMinutes(), wall.getSeconds(), wall.getMilliseconds());
      break;
    }
  }
  if (!dueHasTime) {
    nextWall.setHours(0, 0, 0, 0);
  }
  return fromReferenceZone(nextWall);
}

/** The Warsaw day-of-month of a due instant — the monthly anchor the field proposes when „Co miesiąc" is chosen. */
export function monthlyAnchorFor(dueDate: Date): number {
  return toReferenceZone(dueDate).getDate();
}

/** The short Polish label rendered by the row chip („codziennie" / „co 3 dni" / „pn, śr" / „co miesiąc, 31."). */
export function recurrenceLabel(rule: RecurrenceRule): string {
  switch (rule.type) {
    case "daily":
      return "codziennie";
    case "every_n_days":
      return `co ${rule.interval} dni`;
    case "weekdays":
      return [...rule.weekdays]
        .sort((a, b) => a - b)
        .map((day) => WEEKDAY_ABBREVIATIONS[day])
        .join(", ");
    case "monthly":
      return `co miesiąc, ${rule.dayOfMonth}.`;
  }
}

/**
 * True when the due's Warsaw calendar day is today or earlier (D4/D13 — the "on or after its due date" boundary,
 * day granularity for timed dues too). Decides whether a completion spawns NOW (toast) or is deferred (silence).
 */
export function isDueDayReached(dueDate: Date, now: Date): boolean {
  return referenceDateKey(dueDate) <= referenceDateKey(now);
}

/** The editor/drawer preview text: „Następne wystąpienie: dd.MM.yyyy" (plus ` HH:mm` for timed dues). */
export function formatNextOccurrence(rule: RecurrenceRule, dueDate: Date, dueHasTime: boolean): string {
  const next = nextOccurrence(rule, dueDate, dueHasTime);
  return `Następne wystąpienie: ${formatInReferenceZone(next, dueHasTime ? "dd.MM.yyyy HH:mm" : "dd.MM.yyyy")}`;
}

import { addDays, getDaysInMonth } from "date-fns";
import { fromReferenceZone, toReferenceZone } from "@/lib/timezone";

/**
 * Closed-set Polish natural-language date parser (slice 003, R1–R5/R9/R12).
 *
 * Pure and deterministic: `parseTaskInput(raw, now)` examines only an end-anchored
 * trailing date phrase (R4), resolves it against the Europe/Warsaw reference zone via
 * `lib/timezone.ts` (R5 — all arithmetic in the Warsaw wall-clock domain, then
 * `fromReferenceZone`; never fixed-offset), and strips it from the title. No NL library
 * (R1); the grammar (R2) is the entire recognized surface — anything else is plain title.
 *
 * Three outcomes (R4):
 *  1. No date-shaped trailing phrase  → `{ title }` (the full title, no due, no error).
 *  2. Date-shaped phrase that resolves → `{ title, dueDate, dueHasTime }` (stripped prefix).
 *  3. Date-shaped phrase that fails    → `{ title, error: "unrecognized" }` (full raw retained).
 *
 * "Date-shaped" = matches an R2 surface shape. A shape that is out of clock/calendar RANGE
 * (e.g. `2.0` MM=0, `3.14` MM=14, `po 25`) is NOT a date attempt → plain title, no error.
 * A shape that is in range but impossible (`30.02`, `31.04`) IS a failed attempt → error.
 */
export interface ParseResult {
  title: string;
  dueDate?: Date;
  dueHasTime?: boolean;
  error?: "unrecognized";
}

/** Weekday keywords (post-normalization) → JS `getDay()` index (Sun = 0). */
const WEEKDAYS: Record<string, number> = {
  niedziela: 0,
  poniedzialek: 1,
  wtorek: 2,
  sroda: 3,
  czwartek: 4,
  piatek: 5,
  sobota: 6,
};

/** lowercase → NFD diacritic-strip → ASCII (so `Piątek`/`PIĄTEK` collapse to `piatek`). */
function normalize(token: string): string {
  return token
    .toLowerCase()
    .normalize("NFD")
    .replace(/[̀-ͯ]/g, "");
}

/** A resolved trailing phrase: the due instant + has-time flag (a successful R2 match). */
interface Resolved {
  dueDate: Date;
  dueHasTime: boolean;
}

/**
 * The outcome of trying to interpret a normalized trailing keyword/phrase candidate.
 * `null` = not date-shaped (caller leaves it as plain title). `resolved` = a valid due date.
 * `error` = a genuine date attempt that cannot resolve (caller surfaces "nie rozpoznano").
 */
type Interpretation = { kind: "none" } | { kind: "resolved"; value: Resolved } | { kind: "error" };

/** Date-only: midnight Warsaw of the given zoned wall-clock day → UTC instant (R9). */
function dateOnly(wallClockDay: Date): Resolved {
  const midnight = new Date(
    wallClockDay.getFullYear(),
    wallClockDay.getMonth(),
    wallClockDay.getDate(),
    0,
    0,
    0,
    0,
  );
  return { dueDate: fromReferenceZone(midnight), dueHasTime: false };
}

/** Date-time: the given wall-clock HH:MM on the given zoned day → UTC instant (R5). */
function dateTime(wallClockDay: Date, hours: number, minutes: number): Resolved {
  const wall = new Date(
    wallClockDay.getFullYear(),
    wallClockDay.getMonth(),
    wallClockDay.getDate(),
    hours,
    minutes,
    0,
    0,
  );
  return { dueDate: fromReferenceZone(wall), dueHasTime: true };
}

/**
 * Interprets a single trailing keyword (`dzis`/`jutro`/`pojutrze`/weekday) against the
 * Warsaw "now". Returns `{ kind: "none" }` when the keyword is not recognized.
 */
function interpretKeyword(keyword: string, nowWall: Date): Interpretation {
  switch (keyword) {
    case "dzis":
    case "dzisiaj":
      return { kind: "resolved", value: dateOnly(nowWall) };
    case "jutro":
      return { kind: "resolved", value: dateOnly(addDays(nowWall, 1)) };
    case "pojutrze":
      return { kind: "resolved", value: dateOnly(addDays(nowWall, 2)) };
    default: {
      const target = WEEKDAYS[keyword];
      if (target === undefined) return { kind: "none" };
      // Next strictly-future occurrence; weekday == today → +7 (R3-A).
      let delta = (target - nowWall.getDay() + 7) % 7;
      if (delta === 0) delta = 7;
      return { kind: "resolved", value: dateOnly(addDays(nowWall, delta)) };
    }
  }
}

// End-anchored phrase shapes (R2/R4). Order matters only for clarity; each is tested in turn.
const RE_ZA_N_DNI = /^za\s+(\d+)\s+dni$/;
const RE_PO_HH = /^po\s+(\d{1,2})$/;
const RE_O_HHMM = /^o\s+(\d{1,2})(?::(\d{2}))?$/;
const RE_DD_MM = /^(\d{1,2})\.(\d{1,2})$/;

/**
 * Interprets a normalized trailing *phrase* candidate (which may be multi-word, e.g.
 * `za 3 dni`). Distinguishes "not date-shaped" (→ `none`, plain title) from "date-shaped
 * but impossible" (→ `error`, "nie rozpoznano"), per the R4 range gates.
 */
function interpretPhrase(candidate: string, nowWall: Date): Interpretation {
  const single = interpretKeyword(candidate, nowWall);
  if (single.kind !== "none") return single;

  const zaMatch = RE_ZA_N_DNI.exec(candidate);
  if (zaMatch) {
    const n = Number(zaMatch[1]);
    // `za 0 dni` is not in-grammar (N ≥ 1) — out of range → plain title, no error.
    if (n < 1) return { kind: "none" };
    return { kind: "resolved", value: dateOnly(addDays(nowWall, n)) };
  }

  const poMatch = RE_PO_HH.exec(candidate);
  if (poMatch) {
    const hh = Number(poMatch[1]);
    if (hh > 23) return { kind: "none" }; // out of clock range → plain title.
    return { kind: "resolved", value: dateTime(nowWall, hh, 0) };
  }

  const oMatch = RE_O_HHMM.exec(candidate);
  if (oMatch) {
    const hh = Number(oMatch[1]);
    const mm = oMatch[2] === undefined ? 0 : Number(oMatch[2]);
    if (hh > 23 || mm > 59) return { kind: "none" }; // out of clock range → plain title.
    return { kind: "resolved", value: dateTime(nowWall, hh, mm) };
  }

  const ddMatch = RE_DD_MM.exec(candidate);
  if (ddMatch) {
    const dd = Number(ddMatch[1]);
    const mm = Number(ddMatch[2]);
    // Out of calendar range (DD 1–31, MM 1–12) → not a date attempt → plain title (R4).
    if (dd < 1 || dd > 31 || mm < 1 || mm > 12) return { kind: "none" };
    // In range: resolve `DD.MM` this year, roll to next year if already past (R3-C).
    const year = nowWall.getFullYear();
    const resolved = resolveDayMonth(dd, mm, year, nowWall);
    return resolved ?? { kind: "error" };
  }

  return { kind: "none" };
}

/**
 * Resolves an in-range `DD.MM` to a date-only Resolved, or `null` if the day does not exist
 * in that month (e.g. `30.02`, `31.04`) — an in-range-but-impossible attempt → caller errors.
 * Rolls to next year when the date is already past in the current year (R3-C).
 */
function resolveDayMonth(dd: number, mm: number, year: number, nowWall: Date): Interpretation | null {
  const monthIndex = mm - 1;
  const probe = new Date(year, monthIndex, 1, 0, 0, 0, 0);
  if (dd > getDaysInMonth(probe)) return null; // impossible day for this month.

  let target = new Date(year, monthIndex, dd, 0, 0, 0, 0);
  const todayMidnight = new Date(
    nowWall.getFullYear(),
    nowWall.getMonth(),
    nowWall.getDate(),
    0,
    0,
    0,
    0,
  );
  if (target.getTime() < todayMidnight.getTime()) {
    // Past this year → roll to next year (the day is valid there too — same month).
    target = new Date(year + 1, monthIndex, dd, 0, 0, 0, 0);
  }
  return { kind: "resolved", value: dateOnly(target) };
}

/**
 * Builds the ordered list of end-anchored trailing-phrase candidates to try, longest first
 * (R4 longest-trailing-match). Each candidate carries the prefix (title) that remains if it
 * is consumed; only candidates with a NON-EMPTY remainder are admitted (R4), so a bare
 * `jutro` / `piatek` / `30.06` (the whole string) is never consumed.
 */
function trailingCandidates(words: string[]): { phrase: string; prefix: string }[] {
  const candidates: { phrase: string; prefix: string }[] = [];
  // Up to 3 trailing words covers the longest grammar phrase (`za N dni`); longest first.
  const maxWords = Math.min(3, words.length);
  for (let take = maxWords; take >= 1; take--) {
    const prefix = words.slice(0, words.length - take).join(" ");
    if (prefix.length === 0) continue; // non-empty-remainder required (R4).
    const phrase = normalize(words.slice(words.length - take).join(" "));
    candidates.push({ phrase, prefix });
  }
  return candidates;
}

/**
 * Parses a raw capture string against an injected `now` (a UTC instant). See the module
 * doc for the three-outcome contract. Word boundaries are whitespace; the trailing phrase
 * is matched end-anchored, longest first, with a required non-empty remainder.
 */
export function parseTaskInput(raw: string, now: Date): ParseResult {
  const title = raw.trim();
  if (title.length === 0) return { title };

  const words = title.split(/\s+/);
  const nowWall = toReferenceZone(now);

  for (const { phrase, prefix } of trailingCandidates(words)) {
    const interp = interpretPhrase(phrase, nowWall);
    if (interp.kind === "resolved") {
      return { title: prefix, dueDate: interp.value.dueDate, dueHasTime: interp.value.dueHasTime };
    }
    if (interp.kind === "error") {
      // A genuine end-anchored date attempt that cannot resolve (EC-02 / FR-006).
      return { title, error: "unrecognized" };
    }
    // kind === "none": not date-shaped at this length — try the next (shorter) candidate.
  }

  return { title };
}

/**
 * Resolves a BARE date phrase (slice 005, AS-05) — e.g. "jutro", "piatek", "30.06", "za 3 dni" — to a
 * `{ dueDate, dueHasTime }` against an injected `now`, or `null` when the phrase is not a recognized date.
 * Unlike {@link parseTaskInput} there is no title remainder: the whole input IS the phrase (the `T`
 * reschedule input contains only a date). Reuses the same interpretation grammar so the reschedule and
 * capture flows resolve dates identically (one source of truth).
 */
export function resolveDatePhrase(raw: string, now: Date): { dueDate: Date; dueHasTime: boolean } | null {
  const phrase = normalize(raw.trim());
  if (phrase.length === 0) return null;
  const interp = interpretPhrase(phrase, toReferenceZone(now));
  return interp.kind === "resolved" ? interp.value : null;
}

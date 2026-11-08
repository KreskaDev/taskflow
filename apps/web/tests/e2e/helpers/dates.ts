/**
 * Warsaw-calendar seeding helpers (slice 013 hardening). The app evaluates every date-relative fact in
 * Europe/Warsaw (FR-092); a seed computed from the UTC calendar date lags by one day between 00:00 and
 * 01:00/02:00 Warsaw and turned the cycle/[V]/daily suites red for an hour every night. Always derive the
 * calendar day from the Warsaw wall-clock, then pin the instant to 12:00Z (inside that Warsaw day at every offset).
 */

const WARSAW = "Europe/Warsaw";

/** Today's Warsaw calendar date as `[y, m, d]`. */
export function warsawToday(now: Date = new Date()): [number, number, number] {
  const parts = new Intl.DateTimeFormat("en-CA", { timeZone: WARSAW, year: "numeric", month: "2-digit", day: "2-digit" })
    .format(now)
    .split("-")
    .map(Number);
  return [parts[0]!, parts[1]!, parts[2]!];
}

/** `yyyy-MM-dd` of the Warsaw calendar day `offset` days from today (pure calendar arithmetic). */
export function warsawDateString(offset = 0, now: Date = new Date()): string {
  const [y, m, d] = warsawToday(now);
  const utc = new Date(Date.UTC(y, m - 1, d + offset));
  return utc.toISOString().slice(0, 10);
}

/** An ISO instant at 12:00Z on the Warsaw calendar day `offset` days from today — the seeding convention. */
export function warsawDayIso(offset = 0, now: Date = new Date()): string {
  return `${warsawDateString(offset, now)}T12:00:00Z`;
}

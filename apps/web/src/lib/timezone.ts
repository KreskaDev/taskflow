import { addDays, startOfDay } from "date-fns";
import { formatInTimeZone, fromZonedTime, toZonedTime } from "date-fns-tz";

/**
 * Single instance reference timezone (Constitution X / ASM-12). All timestamps are
 * stored in UTC; every date-relative computation ("Today", "Upcoming", cycle
 * boundaries, recurrence, NL date resolution) is evaluated against this zone,
 * identically on client and server. Per-user timezones are out of scope.
 */
export const REFERENCE_TIME_ZONE = "Europe/Warsaw";

/** Converts a UTC instant to the wall-clock time in the reference zone. */
export function toReferenceZone(utc: Date): Date {
  return toZonedTime(utc, REFERENCE_TIME_ZONE);
}

/** Interprets a reference-zone wall-clock time and returns the corresponding UTC instant. */
export function fromReferenceZone(wallClock: Date): Date {
  return fromZonedTime(wallClock, REFERENCE_TIME_ZONE);
}

/** Formats a UTC instant for display in the reference zone (DST handled by the library). */
export function formatInReferenceZone(utc: Date, format: string): string {
  return formatInTimeZone(utc, REFERENCE_TIME_ZONE, format);
}

/**
 * The UTC instant at the start of the reference-zone calendar day `days` after the day containing
 * `now` (slice 005, R7) — the client mirror of the server's `WarsawDayBounds.StartOfDayPlusUtc`, so the
 * optimistic Today/Upcoming membership decision equals the authoritative one (FR-092). DST is handled by
 * date-fns-tz, never fixed-offset arithmetic.
 */
export function startOfReferenceDayPlusUtc(now: Date, days: number): Date {
  // toZonedTime yields a Date whose fields read as Warsaw wall-clock; startOfDay/addDays operate on those
  // fields; fromZonedTime re-interprets them as Warsaw and returns the corresponding UTC instant.
  const wall = startOfDay(addDays(toZonedTime(now, REFERENCE_TIME_ZONE), days));
  return fromReferenceZone(wall);
}

/** The UTC instant at the start of today in the reference zone, relative to `now`. */
export function startOfReferenceTodayUtc(now: Date): Date {
  return startOfReferenceDayPlusUtc(now, 0);
}

/** The UTC instant at the start of tomorrow in the reference zone — the Today/Upcoming split point. */
export function startOfReferenceTomorrowUtc(now: Date): Date {
  return startOfReferenceDayPlusUtc(now, 1);
}

/** The reference-zone calendar date (`YYYY-MM-DD`) a UTC instant falls on — the Upcoming group key (R3). */
export function referenceDateKey(utc: Date): string {
  return formatInReferenceZone(utc, "yyyy-MM-dd");
}

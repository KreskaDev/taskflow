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

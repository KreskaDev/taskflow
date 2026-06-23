// @vitest-environment node
import { describe, expect, it } from "vitest";
import { parseTaskInput } from "@/lib/dates";
import { formatInReferenceZone } from "@/lib/timezone";

/**
 * Closed-set Polish natural-language date parser (T011/T012, slice 003, R1–R5/R9/R12).
 *
 * The parser is PURE with an injected `now` (R12) so every expectation is deterministic.
 * Reference "now" = 2026-06-21 (Sunday), CEST (+02:00) — the same anchor the spec/quickstart
 * use. `now` is supplied as a UTC instant; 2026-06-21 12:00 Warsaw (CEST) == 2026-06-21T10:00:00Z.
 *
 * All resolution runs in the Warsaw wall-clock domain then converts via `fromReferenceZone`
 * (R5) — NEVER fixed-offset arithmetic. Date-only = midnight Warsaw → UTC instant (R9), so under
 * CEST a date-only day D resolves to the prior day at 22:00:00Z.
 */

// 2026-06-21 12:00 Warsaw (Sunday, CEST +02:00) == 10:00:00Z.
const NOW = new Date("2026-06-21T10:00:00Z");

describe("parseTaskInput — R2 phrase classes (now = 2026-06-21 Sun CEST)", () => {
  it("dzis → today date-only (midnight Warsaw)", () => {
    const r = parseTaskInput("Sprzatanie dzis", NOW);
    expect(r.title).toBe("Sprzatanie");
    expect(r.dueDate?.toISOString()).toBe("2026-06-20T22:00:00.000Z");
    expect(r.dueHasTime).toBe(false);
    expect(r.error).toBeUndefined();
  });

  it("dzisiaj → today date-only", () => {
    const r = parseTaskInput("Sprzatanie dzisiaj", NOW);
    expect(r.title).toBe("Sprzatanie");
    expect(r.dueDate?.toISOString()).toBe("2026-06-20T22:00:00.000Z");
    expect(r.dueHasTime).toBe(false);
  });

  it("jutro → tomorrow date-only", () => {
    const r = parseTaskInput("Raport jutro", NOW);
    expect(r.title).toBe("Raport");
    expect(r.dueDate?.toISOString()).toBe("2026-06-21T22:00:00.000Z");
    expect(r.dueHasTime).toBe(false);
  });

  it("pojutrze → day-after-tomorrow date-only", () => {
    const r = parseTaskInput("Raport pojutrze", NOW);
    expect(r.title).toBe("Raport");
    expect(r.dueDate?.toISOString()).toBe("2026-06-22T22:00:00.000Z");
    expect(r.dueHasTime).toBe(false);
  });

  it("weekday (piatek) → next strictly-future occurrence date-only", () => {
    const r = parseTaskInput("Meeting piatek", NOW);
    expect(r.title).toBe("Meeting");
    expect(r.dueDate?.toISOString()).toBe("2026-06-25T22:00:00.000Z");
    expect(r.dueHasTime).toBe(false);
  });

  it("za N dni → today + N days date-only", () => {
    const r = parseTaskInput("Zakupy za 3 dni", NOW);
    expect(r.title).toBe("Zakupy");
    expect(r.dueDate?.toISOString()).toBe("2026-06-23T22:00:00.000Z");
    expect(r.dueHasTime).toBe(false);
  });

  it("po HH → today at HH:00 date-time", () => {
    const r = parseTaskInput("Kupic mleko po 17", NOW);
    expect(r.title).toBe("Kupic mleko");
    expect(r.dueDate?.toISOString()).toBe("2026-06-21T15:00:00.000Z");
    expect(r.dueHasTime).toBe(true);
  });

  it("o HH → today at HH:00 date-time", () => {
    const r = parseTaskInput("Telefon o 9", NOW);
    expect(r.title).toBe("Telefon");
    expect(r.dueDate?.toISOString()).toBe("2026-06-21T07:00:00.000Z");
    expect(r.dueHasTime).toBe(true);
  });

  it("o HH:MM → today at HH:MM date-time", () => {
    const r = parseTaskInput("Telefon o 9:30", NOW);
    expect(r.title).toBe("Telefon");
    expect(r.dueDate?.toISOString()).toBe("2026-06-21T07:30:00.000Z");
    expect(r.dueHasTime).toBe(true);
  });

  it("DD.MM → that day this year, date-only", () => {
    const r = parseTaskInput("Urlop 30.06", NOW);
    expect(r.title).toBe("Urlop");
    expect(r.dueDate?.toISOString()).toBe("2026-06-29T22:00:00.000Z");
    expect(r.dueHasTime).toBe(false);
  });
});

describe("parseTaskInput — the four AS scenarios (verbatim)", () => {
  it('AS-02 "Kupic mleko po 17" → title "Kupic mleko", 2026-06-21T15:00:00Z, has_time', () => {
    const r = parseTaskInput("Kupic mleko po 17", NOW);
    expect(r.title).toBe("Kupic mleko");
    expect(r.dueDate?.toISOString()).toBe("2026-06-21T15:00:00.000Z");
    expect(r.dueHasTime).toBe(true);
    expect(r.error).toBeUndefined();
  });

  it('AS-03 "Raport jutro" → title "Raport", tomorrow date-only', () => {
    const r = parseTaskInput("Raport jutro", NOW);
    expect(r.title).toBe("Raport");
    expect(r.dueDate?.toISOString()).toBe("2026-06-21T22:00:00.000Z");
    expect(r.dueHasTime).toBe(false);
  });

  it('AS-04 "Meeting piatek" → title "Meeting", next Friday 2026-06-26 date-only', () => {
    const r = parseTaskInput("Meeting piatek", NOW);
    expect(r.title).toBe("Meeting");
    expect(r.dueDate?.toISOString()).toBe("2026-06-25T22:00:00.000Z");
    expect(r.dueHasTime).toBe(false);
    // 2026-06-26 Warsaw calendar day recovered from the instant.
    expect(formatInReferenceZone(r.dueDate!, "yyyy-MM-dd")).toBe("2026-06-26");
  });

  it('AS-05 "Zakupy za 3 dni" → title "Zakupy", 2026-06-24 date-only', () => {
    const r = parseTaskInput("Zakupy za 3 dni", NOW);
    expect(r.title).toBe("Zakupy");
    expect(r.dueDate?.toISOString()).toBe("2026-06-23T22:00:00.000Z");
    expect(r.dueHasTime).toBe(false);
    expect(formatInReferenceZone(r.dueDate!, "yyyy-MM-dd")).toBe("2026-06-24");
  });
});

describe("parseTaskInput — R3 ambiguity edges", () => {
  it("R3-A: weekday == today (niedziela on a Sunday) resolves to +7 days", () => {
    const r = parseTaskInput("Odpoczynek niedziela", NOW);
    expect(r.title).toBe("Odpoczynek");
    // now is Sunday 2026-06-21 → next niedziela is 2026-06-28 (not today).
    expect(r.dueDate?.toISOString()).toBe("2026-06-27T22:00:00.000Z");
    expect(formatInReferenceZone(r.dueDate!, "yyyy-MM-dd")).toBe("2026-06-28");
    expect(r.dueHasTime).toBe(false);
  });

  it("R3-B: po HH when that time already passed today → still today", () => {
    // now = 2026-06-21 18:00 Warsaw (CEST) == 16:00:00Z; "po 17" already passed.
    const now18 = new Date("2026-06-21T16:00:00Z");
    const r = parseTaskInput("Kupic mleko po 17", now18);
    expect(r.title).toBe("Kupic mleko");
    expect(r.dueDate?.toISOString()).toBe("2026-06-21T15:00:00.000Z");
    expect(r.dueHasTime).toBe(true);
  });

  it("R3-C: DD.MM already past this year → rolls to next year", () => {
    // now = 2026-07-01 12:00 Warsaw (CEST) == 10:00:00Z; 30.06 is past → 2027-06-30.
    const nowJul = new Date("2026-07-01T10:00:00Z");
    const r = parseTaskInput("Urlop 30.06", nowJul);
    expect(r.title).toBe("Urlop");
    expect(r.dueDate?.toISOString()).toBe("2027-06-29T22:00:00.000Z");
    expect(formatInReferenceZone(r.dueDate!, "yyyy-MM-dd")).toBe("2027-06-30");
    expect(r.dueHasTime).toBe(false);
  });
});

describe("parseTaskInput — EC-02 (impossible in-range date attempt)", () => {
  it('"Spotkanie 30.02" → error "unrecognized", no due date, full title retained', () => {
    const r = parseTaskInput("Spotkanie 30.02", NOW);
    expect(r.error).toBe("unrecognized");
    expect(r.dueDate).toBeUndefined();
    expect(r.dueHasTime).toBeUndefined();
    // Field retains its full value so the user can correct it (FR-006/EC-02).
    expect(r.title).toBe("Spotkanie 30.02");
  });

  it('"Plan 31.04" → error "unrecognized" (April has 30 days)', () => {
    const r = parseTaskInput("Plan 31.04", NOW);
    expect(r.error).toBe("unrecognized");
    expect(r.dueDate).toBeUndefined();
  });
});

describe("parseTaskInput — guards (no date, no error)", () => {
  it('"Wersja 2.0" → no date, no error (MM=0 out of range → plain title)', () => {
    const r = parseTaskInput("Wersja 2.0", NOW);
    expect(r.title).toBe("Wersja 2.0");
    expect(r.dueDate).toBeUndefined();
    expect(r.error).toBeUndefined();
  });

  it('"skala 3.14" → no date, no error (MM=14 out of range → plain title)', () => {
    const r = parseTaskInput("skala 3.14", NOW);
    expect(r.title).toBe("skala 3.14");
    expect(r.dueDate).toBeUndefined();
    expect(r.error).toBeUndefined();
  });

  it('bare "jutro" alone → title "jutro", no due, no error (non-empty remainder required)', () => {
    const r = parseTaskInput("jutro", NOW);
    expect(r.title).toBe("jutro");
    expect(r.dueDate).toBeUndefined();
    expect(r.error).toBeUndefined();
  });

  it('"Kupic mleko" → no due (no date-shaped trailing token)', () => {
    const r = parseTaskInput("Kupic mleko", NOW);
    expect(r.title).toBe("Kupic mleko");
    expect(r.dueDate).toBeUndefined();
    expect(r.error).toBeUndefined();
  });

  it("po HH out of clock range (po 25) → plain title, no error", () => {
    const r = parseTaskInput("Bieg po 25", NOW);
    expect(r.title).toBe("Bieg po 25");
    expect(r.dueDate).toBeUndefined();
    expect(r.error).toBeUndefined();
  });
});

describe("parseTaskInput — diacritic normalization (R2 NFD strip)", () => {
  // Exercises the normalize() strip directly: real Polish diacritics (NOT pre-stripped ASCII)
  // must collapse to the ASCII keyword table so a user typing natural Polish still matches. A
  // no-op strip would leave "piątek"/"środa" unmatched → the phrase silently stays in the title.
  it('weekday with diacritics ("piątek") → next Friday, stripped (T002 NFD-strip)', () => {
    const r = parseTaskInput("Spotkanie piątek", NOW);
    expect(r.title).toBe("Spotkanie");
    expect(r.dueDate?.toISOString()).toBe("2026-06-25T22:00:00.000Z");
    expect(formatInReferenceZone(r.dueDate!, "yyyy-MM-dd")).toBe("2026-06-26");
    expect(r.dueHasTime).toBe(false);
  });

  it('weekday with diacritics ("środa") → next Wednesday, stripped', () => {
    const r = parseTaskInput("Zadanie środa", NOW);
    expect(r.title).toBe("Zadanie");
    // now = Sunday 2026-06-21 → next środa is 2026-06-24.
    expect(formatInReferenceZone(r.dueDate!, "yyyy-MM-dd")).toBe("2026-06-24");
    expect(r.dueHasTime).toBe(false);
  });

  it('uppercase + diacritics ("DZIŚ") → today date-only (lowercase + strip)', () => {
    const r = parseTaskInput("Sprzatanie DZIŚ", NOW);
    expect(r.title).toBe("Sprzatanie");
    expect(r.dueDate?.toISOString()).toBe("2026-06-20T22:00:00.000Z");
    expect(r.dueHasTime).toBe(false);
  });
});

describe("parseTaskInput — DST boundary (FR-092)", () => {
  it("za 2 dni crossing the spring-forward (2026-03-29) maps to midnight Warsaw, not a fixed-offset slip", () => {
    // now = 2026-03-28 12:00 Warsaw (CET +01:00) == 11:00:00Z; +2 days = 2026-03-30 (CEST +02:00).
    const nowCet = new Date("2026-03-28T11:00:00Z");
    const r = parseTaskInput("Zadanie za 2 dni", nowCet);
    expect(r.title).toBe("Zadanie");
    // Correct (library/IANA): 2026-03-30 00:00 Warsaw (CEST) == 2026-03-29T22:00:00Z.
    // A fixed-+01:00 (CET) slip would wrongly yield ...23:00:00Z.
    expect(r.dueDate?.toISOString()).toBe("2026-03-29T22:00:00.000Z");
    expect(formatInReferenceZone(r.dueDate!, "yyyy-MM-dd HH:mm")).toBe("2026-03-30 00:00");
    expect(r.dueHasTime).toBe(false);
  });
});

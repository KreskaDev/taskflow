import { describe, expect, it } from "vitest";

import {
  formatNextOccurrence,
  monthlyAnchorFor,
  nextOccurrence,
  parseRecurrenceRule,
  recurrenceLabel,
  recurrenceRuleSchema,
  toWireRule,
  type RecurrenceRule,
} from "@/lib/recurrence";
import { fromReferenceZone } from "@/lib/timezone";

/**
 * The recurrence pure module (slice 012, T015 — contracts/recurrence-api.md): the closed union at the
 * trust boundary, the Polish chip labels, and `nextOccurrence` over the SAME fact table the Domain's
 * `RecurrenceRuleTests` assert (FR-092 — client preview equals server truth), incl. the DST and leap-year
 * rows. [INV-180] [INV-182]
 */

/** A date-only due stored by the Warsaw-midnight convention. */
function warsawMidnight(y: number, m: number, d: number): Date {
  return fromReferenceZone(new Date(y, m - 1, d, 0, 0, 0, 0));
}

function warsawWallClock(y: number, m: number, d: number, hh: number, mm: number): Date {
  return fromReferenceZone(new Date(y, m - 1, d, hh, mm, 0, 0));
}

describe("recurrenceRuleSchema", () => {
  it.each<[unknown]>([
    [{ type: "daily" }],
    [{ type: "every_n_days", interval: 3 }],
    [{ type: "weekdays", weekdays: [1, 3] }],
    [{ type: "monthly", dayOfMonth: 31 }],
  ])("accepts %j", (rule) => {
    expect(recurrenceRuleSchema.safeParse(rule).success).toBe(true);
  });

  it.each<[unknown, string]>([
    [{ type: "yearly" }, "unknown type"],
    [{ type: "every_n_days", interval: 1 }, "interval below 2"],
    [{ type: "every_n_days", interval: 366 }, "interval above 365"],
    [{ type: "every_n_days" }, "interval missing"],
    [{ type: "weekdays", weekdays: [] }, "empty weekdays"],
    [{ type: "weekdays", weekdays: [1, 1] }, "duplicate weekdays"],
    [{ type: "weekdays", weekdays: [8] }, "weekday above 7"],
    [{ type: "monthly", dayOfMonth: 0 }, "dayOfMonth below 1"],
    [{ type: "monthly", dayOfMonth: 32 }, "dayOfMonth above 31"],
  ])("rejects %j (%s)", (rule) => {
    expect(recurrenceRuleSchema.safeParse(rule).success).toBe(false);
  });

  it("parseRecurrenceRule normalizes the flat wire shape (nulls dropped, weekdays sorted) and maps absent → null", () => {
    expect(parseRecurrenceRule(null)).toBeNull();
    expect(parseRecurrenceRule(undefined)).toBeNull();
    expect(parseRecurrenceRule({ type: "daily", interval: null, weekdays: null, dayOfMonth: null })).toEqual({ type: "daily" });
    expect(parseRecurrenceRule({ type: "weekdays", weekdays: [3, 1] })).toEqual({ type: "weekdays", weekdays: [1, 3] });
    expect(parseRecurrenceRule({ type: "nope" })).toBeNull();
  });

  it("toWireRule round-trips every type", () => {
    const rules: RecurrenceRule[] = [
      { type: "daily" },
      { type: "every_n_days", interval: 5 },
      { type: "weekdays", weekdays: [5] },
      { type: "monthly", dayOfMonth: 15 },
    ];
    for (const rule of rules) {
      expect(parseRecurrenceRule(toWireRule(rule))).toEqual(rule);
    }
    expect(toWireRule(null)).toBeNull();
  });
});

describe("recurrenceLabel", () => {
  it("renders the Polish chip text per type", () => {
    expect(recurrenceLabel({ type: "daily" })).toBe("codziennie");
    expect(recurrenceLabel({ type: "every_n_days", interval: 3 })).toBe("co 3 dni");
    expect(recurrenceLabel({ type: "weekdays", weekdays: [3, 1] })).toBe("pn, śr");
    expect(recurrenceLabel({ type: "weekdays", weekdays: [1, 2, 3, 4, 5, 6, 7] })).toBe("pn, wt, śr, cz, pt, so, nd");
    expect(recurrenceLabel({ type: "monthly", dayOfMonth: 31 })).toBe("co miesiąc, 31.");
  });
});

describe("nextOccurrence — the shared fact table (date-only, Warsaw midnight)", () => {
  it.each<[RecurrenceRule, [number, number, number], [number, number, number], string]>([
    [{ type: "daily" }, [2026, 6, 10], [2026, 6, 11], "daily"],
    [{ type: "every_n_days", interval: 3 }, [2026, 6, 10], [2026, 6, 13], "every 3 days (AS-05)"],
    [{ type: "weekdays", weekdays: [1, 3] }, [2026, 6, 8], [2026, 6, 10], "Mon → Wed (AS-03)"],
    [{ type: "weekdays", weekdays: [1, 3] }, [2026, 6, 10], [2026, 6, 15], "Wed → next Mon (wraps)"],
    [{ type: "weekdays", weekdays: [5] }, [2026, 6, 12], [2026, 6, 19], "single day = weekly"],
    [{ type: "weekdays", weekdays: [1, 3] }, [2026, 6, 9], [2026, 6, 10], "Tue (not listed) → Wed"],
    [{ type: "monthly", dayOfMonth: 31 }, [2026, 1, 31], [2026, 2, 28], "clamp"],
    [{ type: "monthly", dayOfMonth: 31 }, [2028, 1, 31], [2028, 2, 29], "leap clamp"],
    [{ type: "monthly", dayOfMonth: 31 }, [2026, 2, 28], [2026, 3, 31], "anchor restored after a clamped instance"],
    [{ type: "monthly", dayOfMonth: 15 }, [2026, 12, 15], [2027, 1, 15], "year wrap"],
    [{ type: "every_n_days", interval: 2 }, [2026, 3, 28], [2026, 3, 30], "across the spring DST switch"],
    [{ type: "daily" }, [2026, 10, 24], [2026, 10, 25], "across the autumn DST switch"],
  ])("%j from %j → %j (%s)", (rule, anchor, expected) => {
    const next = nextOccurrence(rule, warsawMidnight(...anchor), false);
    expect(next.toISOString()).toBe(warsawMidnight(...expected).toISOString());
  });

  it("is a pure function of rule + anchor — never of the clock (ASM-09, Clarifications Q2)", () => {
    const rule: RecurrenceRule = { type: "every_n_days", interval: 3 };
    const anchor = warsawMidnight(2026, 6, 10);
    expect(nextOccurrence(rule, anchor, false).toISOString()).toBe(warsawMidnight(2026, 6, 13).toISOString());
  });
});

describe("nextOccurrence — timed dues keep the Warsaw wall-clock across DST", () => {
  it("spring: 2026-03-28 09:00 CET → 2026-03-29 09:00 CEST", () => {
    const next = nextOccurrence({ type: "daily" }, warsawWallClock(2026, 3, 28, 9, 0), true);
    expect(next.toISOString()).toBe("2026-03-29T07:00:00.000Z");
  });

  it("autumn: 2026-10-24 09:00 CEST → 2026-10-31 09:00 CET", () => {
    const next = nextOccurrence({ type: "every_n_days", interval: 7 }, warsawWallClock(2026, 10, 24, 9, 0), true);
    expect(next.toISOString()).toBe("2026-10-31T08:00:00.000Z");
  });

  it("monthly keeps the time of the anchor", () => {
    const next = nextOccurrence({ type: "monthly", dayOfMonth: 31 }, warsawWallClock(2026, 1, 31, 14, 30), true);
    expect(next.toISOString()).toBe(warsawWallClock(2026, 2, 28, 14, 30).toISOString());
  });
});

describe("monthlyAnchorFor / formatNextOccurrence", () => {
  it("proposes the Warsaw day-of-month of the due as the monthly anchor", () => {
    expect(monthlyAnchorFor(warsawMidnight(2026, 1, 31))).toBe(31);
    // 2026-06-09T22:30Z is already June 10 in Warsaw.
    expect(monthlyAnchorFor(new Date("2026-06-09T22:30:00Z"))).toBe(10);
  });

  it("formats the preview line in the reference zone", () => {
    expect(formatNextOccurrence({ type: "daily" }, warsawMidnight(2026, 6, 10), false)).toBe("Następne wystąpienie: 11.06.2026");
    expect(formatNextOccurrence({ type: "daily" }, warsawWallClock(2026, 3, 28, 9, 0), true)).toBe(
      "Następne wystąpienie: 29.03.2026 09:00",
    );
  });
});

import { describe, expect, it } from "vitest";

import type { components } from "@/lib/api/generated/schema";
import {
  buildCycleGroups,
  buildCyclePickerOptions,
  cycleStatusLabel,
  daysRemaining,
  daysRemainingLabel,
  isCycleOverdue,
  orderCycles,
  percentDone,
} from "@/lib/cycles";

type CycleResponse = components["schemas"]["CycleResponse"];
type TaskResponse = components["schemas"]["TaskResponse"];

/**
 * The cycles pure module (slice 011, T014 — contracts/ui-cycle.md): D5 ordering incl. the
 * tiebreaker, Warsaw days-remaining across DST (FR-092), Polish labels, the by-cycle List groups
 * (EC-10 „Bez cyklu” LAST) and the CyclePicker options.
 */

function cycle(overrides: Partial<CycleResponse> & Pick<CycleResponse, "id">): CycleResponse {
  return {
    id: overrides.id,
    name: overrides.name ?? "Cykl",
    startDate: overrides.startDate ?? "2026-01-05T00:00:00Z",
    endDate: overrides.endDate ?? "2026-01-19T00:00:00Z",
    status: overrides.status ?? "planned",
    version: overrides.version ?? 0,
    createdAt: overrides.createdAt ?? "2026-01-01T00:00:00Z",
    metrics: overrides.metrics ?? {
      total: 0,
      done: 0,
      breakdown: { backlog: 0, todo: 0, in_progress: 0, done: 0, cancelled: 0 },
    },
  };
}

function task(overrides: Partial<TaskResponse> & Pick<TaskResponse, "id" | "position">): TaskResponse {
  return {
    id: overrides.id,
    title: overrides.title ?? "T",
    status: overrides.status ?? "backlog",
    position: overrides.position,
    version: 0,
    createdAt: "2026-01-01T00:00:00Z",
    updatedAt: "2026-01-01T00:00:00Z",
    completedAt: null,
    assignees: [],
    labels: [],
    cycleId: overrides.cycleId ?? null,
    carriedOver: overrides.carriedOver ?? false,
  };
}

describe("orderCycles — the D5 deterministic order [INV-162]", () => {
  it("orders by startDate, then createdAt, then id", () => {
    const laterStart = cycle({ id: "b", startDate: "2026-02-01T00:00:00Z", createdAt: "2026-01-01T00:00:00Z" });
    const earlyStart = cycle({ id: "c", startDate: "2026-01-05T00:00:00Z", createdAt: "2026-01-03T00:00:00Z" });
    const tieEarlierCreated = cycle({ id: "d", startDate: "2026-02-01T00:00:00Z", createdAt: "2025-12-30T00:00:00Z" });

    const ordered = orderCycles([laterStart, earlyStart, tieEarlierCreated]);

    expect(ordered.map((c) => c.id)).toEqual(["c", "d", "b"]);
  });

  it("breaks a full (startDate, createdAt) tie on id — fully deterministic", () => {
    const a = cycle({ id: "a" });
    const b = cycle({ id: "b" });

    expect(orderCycles([b, a]).map((c) => c.id)).toEqual(["a", "b"]);
  });
});

describe("daysRemaining — Warsaw calendar days (FR-092) [INV-163]", () => {
  it("counts full Warsaw days to a future end date", () => {
    // end = Warsaw midnight of 2026-08-24 (CEST → stored 2026-08-23T22:00Z); now = 2026-08-17 noon.
    expect(daysRemaining("2026-08-23T22:00:00Z", new Date("2026-08-17T12:00:00Z"))).toBe(7);
  });

  it("crosses the spring DST seam without fixed-offset drift", () => {
    // CET→CEST on 2026-03-29 sits inside the span: Mar 25 → Apr 5 is 11 Warsaw calendar days,
    // though the raw instant delta is only ~10.4 days (one hour vanishes at the seam).
    const end = "2026-04-04T22:00:00Z"; // Warsaw midnight of 2026-04-05 (CEST)
    expect(daysRemaining(end, new Date("2026-03-25T12:00:00Z"))).toBe(11);
  });

  it("crosses the autumn DST seam over a 2-week span", () => {
    // CEST→CET on 2026-10-25: Oct 20 → Nov 3 is 14 Warsaw calendar days (25h day inside).
    const end = "2026-11-02T23:00:00Z"; // Warsaw midnight of 2026-11-03 (CET)
    expect(daysRemaining(end, new Date("2026-10-20T10:00:00Z"))).toBe(14);
  });

  it("is zero/negative once the end day's Warsaw midnight passed — the overdue fact", () => {
    const now = new Date("2026-08-17T12:00:00Z");
    expect(daysRemaining("2026-08-16T22:00:00Z", now)).toBe(0); // end day = today
    expect(daysRemaining("2026-08-10T22:00:00Z", now)).toBeLessThan(0);
    expect(isCycleOverdue("2026-08-16T22:00:00Z", now)).toBe(true);
    expect(isCycleOverdue("2026-08-23T22:00:00Z", now)).toBe(false);
  });
});

describe("daysRemainingLabel — Polish agreement + the overdue copy [INV-163]", () => {
  const now = new Date("2026-08-17T12:00:00Z");

  it("agrees the verb with the count (1 / paucal / plural)", () => {
    expect(daysRemainingLabel("2026-08-17T22:00:00Z", now)).toBe("1 dzień pozostał");
    expect(daysRemainingLabel("2026-08-18T22:00:00Z", now)).toBe("2 dni pozostały");
    expect(daysRemainingLabel("2026-08-23T22:00:00Z", now)).toBe("7 dni pozostało");
    expect(daysRemainingLabel("2026-08-28T22:00:00Z", now)).toBe("12 dni pozostało");
    expect(daysRemainingLabel("2026-09-08T22:00:00Z", now)).toBe("23 dni pozostały");
  });

  it('reads „0 dni (po terminie)” once the end passed (US-05.AS-04 Given)', () => {
    expect(daysRemainingLabel("2026-08-16T22:00:00Z", now)).toBe("0 dni (po terminie)");
    expect(daysRemainingLabel("2026-08-01T22:00:00Z", now)).toBe("0 dni (po terminie)");
  });
});

describe("percentDone — cancelled never deflates progress (D6) [INV-163]", () => {
  it("computes done over non-cancelled total", () => {
    expect(
      percentDone({ total: 4, done: 2, breakdown: { backlog: 1, todo: 0, in_progress: 0, done: 2, cancelled: 1 } }),
    ).toBe(67); // 2 / (4 − 1)
  });

  it("is 0 for an empty cycle and 100 when only cancelled work remains undone", () => {
    expect(percentDone({ total: 0, done: 0, breakdown: { backlog: 0, todo: 0, in_progress: 0, done: 0, cancelled: 0 } })).toBe(0);
    expect(percentDone({ total: 3, done: 2, breakdown: { backlog: 0, todo: 0, in_progress: 0, done: 2, cancelled: 1 } })).toBe(100);
    // All-cancelled: denominator empty → 0, not NaN/Infinity.
    expect(percentDone({ total: 2, done: 0, breakdown: { backlog: 0, todo: 0, in_progress: 0, done: 0, cancelled: 2 } })).toBe(0);
  });
});

describe("cycleStatusLabel", () => {
  it("maps the three wire tokens to Polish", () => {
    expect(cycleStatusLabel("planned")).toBe("planowany");
    expect(cycleStatusLabel("active")).toBe("aktywny");
    expect(cycleStatusLabel("closed")).toBe("zamknięty");
  });
});

describe("buildCycleGroups — the FR-024 by-cycle dimension (EC-10) [INV-174]", () => {
  const early = cycle({ id: "c-early", name: "Wczesny", startDate: "2026-01-05T00:00:00Z" });
  const late = cycle({ id: "c-late", name: "Późny", startDate: "2026-02-02T00:00:00Z" });

  it("orders groups per D5, labels them by cycle name, and puts „Bez cyklu” LAST", () => {
    const tasks = [
      task({ id: "t1", position: "a0", cycleId: "c-late" }),
      task({ id: "t2", position: "a1", cycleId: "c-early" }),
      task({ id: "t3", position: "a2", cycleId: null }),
    ];

    const groups = buildCycleGroups(tasks, [late, early]);

    expect(groups.map((g) => g.label)).toEqual(["Wczesny", "Późny", "Bez cyklu"]);
    expect(groups.map((g) => g.key)).toEqual(["cycle:c-early", "cycle:c-late", "cycle:none"]);
    expect(groups[2]!.label).not.toBe("Backlog"); // EC-10: never the task-status name
  });

  it("omits empty groups and preserves the position order within a group", () => {
    const tasks = [
      task({ id: "t2", position: "a1", cycleId: "c-early" }),
      task({ id: "t1", position: "a0", cycleId: "c-early" }),
    ];

    const groups = buildCycleGroups(tasks, [early, late]);

    expect(groups).toHaveLength(1);
    expect(groups[0]!.tasks.map((t) => t.id)).toEqual(["t1", "t2"]);
  });

  it("routes a task referencing an unknown cycle into „Bez cyklu” instead of dropping it", () => {
    const tasks = [task({ id: "t1", position: "a0", cycleId: "ghost" })];

    const groups = buildCycleGroups(tasks, [early]);

    expect(groups).toHaveLength(1);
    expect(groups[0]!.label).toBe("Bez cyklu");
  });
});

describe("buildCyclePickerOptions — ALL cycles + „Bez cyklu” (US-05.AS-01) [INV-173]", () => {
  const active = cycle({ id: "c-a", name: "Bieżący", status: "active", startDate: "2026-01-05T00:00:00Z" });
  const planned = cycle({ id: "c-p", name: "Następny", status: "planned", startDate: "2026-01-19T00:00:00Z" });
  const closed = cycle({ id: "c-c", name: "Miniony", status: "closed", startDate: "2025-12-01T00:00:00Z" });

  it("lists every status incl. closed, D5-ordered, with the Polish suffix and „Bez cyklu” last", () => {
    const options = buildCyclePickerOptions([active, planned, closed], null);

    expect(options.map((o) => o.label)).toEqual([
      "Miniony (zamknięty)",
      "Bieżący (aktywny)",
      "Następny (planowany)",
      "Bez cyklu",
    ]);
    expect(options.at(-1)!.id).toBeNull();
  });

  it("checks the current assignment; no assignment checks „Bez cyklu”", () => {
    const withCycle = buildCyclePickerOptions([active, planned], "c-p");
    expect(withCycle.find((o) => o.checked)?.id).toBe("c-p");

    const backlog = buildCyclePickerOptions([active, planned], null);
    expect(backlog.find((o) => o.checked)?.id).toBeNull();
  });
});

// @vitest-environment node
import { describe, expect, it } from "vitest";
import type { components } from "@/lib/api/generated/schema";
import {
  buildTodayGroups,
  buildUpcomingGroups,
  isInToday,
  isInUpcoming,
} from "@/lib/dailyViews";

type TaskResponse = components["schemas"]["TaskResponse"];

/**
 * The client membership recompute + R5 assembly (slice 005, T024; the mirror of the server's
 * Today/Upcoming grouping/order). Frozen "now" = 2026-06-27 12:00Z (= 14:00 Warsaw, CEST).
 */
const NOW = new Date("2026-06-27T12:00:00Z");

function task(overrides: Partial<TaskResponse> & Pick<TaskResponse, "id">): TaskResponse {
  return {
    id: overrides.id,
    title: overrides.title ?? "T",
    status: overrides.status ?? "backlog",
    position: overrides.position ?? "a0",
    version: overrides.version ?? 0,
    createdAt: overrides.createdAt ?? "2026-06-01T00:00:00Z",
    updatedAt: overrides.updatedAt ?? "2026-06-01T00:00:00Z",
    completedAt: overrides.completedAt ?? null,
    dueDate: overrides.dueDate ?? null,
    dueHasTime: overrides.dueHasTime ?? null,
    projectId: overrides.projectId ?? null,
    priority: overrides.priority ?? null,
    description: overrides.description ?? null,
    assignees: overrides.assignees ?? [],
  };
}

describe("Today membership + assembly", () => {
  it("includes due-today and overdue (flagged), excludes done/cancelled/no-due/tomorrow", () => {
    const tasks = [
      task({ id: "a", dueDate: "2026-06-27T10:00:00Z", dueHasTime: true, priority: "P1" }),
      task({ id: "b", dueDate: "2026-06-25T10:00:00Z", dueHasTime: true, priority: "P0" }),
      task({ id: "c", dueDate: "2026-06-28T10:00:00Z", dueHasTime: true }), // tomorrow
      task({ id: "d" }), // no due
      task({ id: "e", dueDate: "2026-06-27T10:00:00Z", dueHasTime: true, status: "done" }),
    ];

    const today = buildTodayGroups(tasks, NOW);
    const flat = today.groups.flatMap((g) => g.tasks);
    expect(flat.map((t) => t.id)).toEqual(["b", "a"]); // P0 overdue before P1 due-today
    expect(flat.find((t) => t.id === "b")!.isOverdue).toBe(true);
    expect(flat.find((t) => t.id === "a")!.isOverdue).toBe(false);
  });

  it("groups by project (Inbox first) and sorts NULL priority last", () => {
    const tasks = [
      task({ id: "p2", dueDate: "2026-06-27T10:00:00Z", dueHasTime: true, priority: "P2" }),
      task({ id: "none", dueDate: "2026-06-27T10:00:00Z", dueHasTime: true, priority: null }),
      task({ id: "proj", dueDate: "2026-06-27T10:00:00Z", dueHasTime: true, priority: "P0", projectId: "11111111-1111-1111-1111-111111111111" }),
    ];

    const today = buildTodayGroups(tasks, NOW);
    expect(today.groups[0]!.projectId).toBeNull();
    expect(today.groups[0]!.tasks.map((t) => t.id)).toEqual(["p2", "none"]); // null priority last
    expect(today.groups[1]!.projectId).toBe("11111111-1111-1111-1111-111111111111");
  });

  it("date-only sorts as start-of-day, before same-day timed tasks", () => {
    const tasks = [
      task({ id: "timed", dueDate: "2026-06-27T09:00:00Z", dueHasTime: true }),
      task({ id: "dateOnly", dueDate: "2026-06-26T22:00:00Z", dueHasTime: false }), // 00:00 Warsaw 27th
    ];
    const today = buildTodayGroups(tasks, NOW);
    expect(today.groups[0]!.tasks.map((t) => t.id)).toEqual(["dateOnly", "timed"]);
  });

  it("membership is by the Warsaw day at the UTC-midnight seam", () => {
    expect(isInToday(task({ id: "x", dueDate: "2026-06-27T21:30:00Z", dueHasTime: true }), NOW)).toBe(true);
    expect(isInToday(task({ id: "y", dueDate: "2026-06-27T22:30:00Z", dueHasTime: true }), NOW)).toBe(false);
  });
});

describe("Upcoming membership + assembly", () => {
  it("groups the next 7 Warsaw days ascending, excludes today/no-due", () => {
    const tasks = [
      task({ id: "tom", dueDate: "2026-06-28T10:00:00Z", dueHasTime: true }),
      task({ id: "d3", dueDate: "2026-06-30T10:00:00Z", dueHasTime: true }),
      task({ id: "today", dueDate: "2026-06-27T10:00:00Z", dueHasTime: true }),
      task({ id: "none" }),
    ];
    const upcoming = buildUpcomingGroups(tasks, NOW);
    expect(upcoming.groups.map((g) => g.date)).toEqual(["2026-06-28", "2026-06-30"]);
    expect(upcoming.groups.flatMap((g) => g.tasks).map((t) => t.id)).toEqual(["tom", "d3"]);
  });

  it("groups by the Warsaw local date, not the truncated UTC date", () => {
    // 22:30Z is 00:30 Warsaw the 29th — grouped under 2026-06-29, not the UTC date 2026-06-28.
    const tasks = [task({ id: "seam", dueDate: "2026-06-28T22:30:00Z", dueHasTime: true })];
    const upcoming = buildUpcomingGroups(tasks, NOW);
    expect(upcoming.groups[0]!.date).toBe("2026-06-29");
  });

  it("a task due tomorrow is in Upcoming, not Today (Constitution X partition)", () => {
    const tomorrow = task({ id: "t", dueDate: "2026-06-28T10:00:00Z", dueHasTime: true });
    expect(isInUpcoming(tomorrow, NOW)).toBe(true);
    expect(isInToday(tomorrow, NOW)).toBe(false);
  });
});

describe("client-tier DST boundary (FR-092 identical-rule, R13 — both tiers)", () => {
  // Warsaw springs forward 2026-03-29 02:00 CET → 03:00 CEST (a 23h day). "now" = 05:00Z that day.
  // start-of-tomorrow-Warsaw = 2026-03-29T22:00Z — proven by the tzdb library, never a fixed offset.
  const DST_NOW = new Date("2026-03-29T05:00:00Z");

  it("uses the 23h spring-forward day for Today membership", () => {
    expect(isInToday(task({ id: "a", dueDate: "2026-03-29T21:30:00Z", dueHasTime: true }), DST_NOW)).toBe(true); // 23:30 Warsaw 29th
    expect(isInToday(task({ id: "b", dueDate: "2026-03-29T22:30:00Z", dueHasTime: true }), DST_NOW)).toBe(false); // 00:30 Warsaw 30th
  });

  it("groups the next Warsaw day correctly across the DST seam", () => {
    const upcoming = buildUpcomingGroups([task({ id: "b", dueDate: "2026-03-29T22:30:00Z", dueHasTime: true })], DST_NOW);
    expect(upcoming.groups[0]!.date).toBe("2026-03-30");
  });
});

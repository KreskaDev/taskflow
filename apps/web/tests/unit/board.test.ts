// @vitest-environment node
import { describe, expect, it } from "vitest";

import type { components } from "@/lib/api/generated/schema";
import {
  BOARD_COLUMNS,
  adjacentStatus,
  buildBoardColumns,
  buildProjectGroups,
} from "@/lib/board";

type TaskResponse = components["schemas"]["TaskResponse"];

/**
 * Pure board/grouping module coverage (slice 010, T011 — data-model.md "Column ↔ status
 * mapping", FR-024/FR-025, EC-11, US-03.AS-05). Pins the public shape of `lib/board.ts`:
 * `BOARD_COLUMNS` (four Polish-labelled columns, cancelled NEVER a column),
 * `buildBoardColumns` (cancelled filtered, position-ranked), `adjacentStatus` (menu-move
 * neighbours with boundary nulls) and `buildProjectGroups` (status/priority groupings).
 */

function makeTask(
  overrides: Partial<TaskResponse> & Pick<TaskResponse, "id" | "position">,
): TaskResponse {
  return {
    title: overrides.title ?? "zadanie",
    status: "backlog",
    version: 0,
    createdAt: "2026-08-01T00:00:00.000Z",
    updatedAt: "2026-08-01T00:00:00.000Z",
    completedAt: null,
    assignees: [],
    labels: [],
    cycleId: null,
    carriedOver: false,
    ...overrides,
  };
}

describe("BOARD_COLUMNS — the single column↔status map (FR-025, EC-11)", () => {
  it("declares exactly four columns in order with the Polish labels — cancelled is never a column [INV-142]", () => {
    expect(BOARD_COLUMNS.map((c) => c.status)).toEqual(["backlog", "todo", "in_progress", "done"]);
    expect(BOARD_COLUMNS.map((c) => c.label)).toEqual(["Backlog", "Do zrobienia", "W toku", "Zrobione"]);
  });
});

describe("buildBoardColumns — four columns, cancelled hidden, position order (EC-11) [INV-142] [INV-143]", () => {
  it("distributes tasks into their status columns preserving position rank order", () => {
    const tasks = [
      makeTask({ id: "t1", position: "a0", status: "todo" }),
      makeTask({ id: "t2", position: "a2", status: "todo" }),
      makeTask({ id: "t3", position: "a1", status: "in_progress" }),
      makeTask({ id: "t4", position: "a3", status: "done" }),
    ];

    const columns = buildBoardColumns(tasks);

    expect(columns).toHaveLength(4);
    expect(columns.map((c) => c.status)).toEqual(["backlog", "todo", "in_progress", "done"]);
    expect(columns[0]!.tasks).toEqual([]);
    expect(columns[1]!.tasks.map((t) => t.id)).toEqual(["t1", "t2"]);
    expect(columns[2]!.tasks.map((t) => t.id)).toEqual(["t3"]);
    expect(columns[3]!.tasks.map((t) => t.id)).toEqual(["t4"]);
  });

  it("sorts within a column by position rank even when the input arrives unordered", () => {
    const tasks = [
      makeTask({ id: "late", position: "a5", status: "todo" }),
      makeTask({ id: "early", position: "a1", status: "todo" }),
    ];

    const [, todo] = buildBoardColumns(tasks);

    expect(todo!.tasks.map((t) => t.id)).toEqual(["early", "late"]);
  });

  it("a cancelled task appears in NO column (EC-11)", () => {
    const tasks = [
      makeTask({ id: "live", position: "a0", status: "todo" }),
      makeTask({ id: "gone", position: "a1", status: "cancelled" }),
    ];

    const columns = buildBoardColumns(tasks);

    expect(columns.flatMap((c) => c.tasks.map((t) => t.id))).toEqual(["live"]);
  });
});

describe("adjacentStatus — menu-move neighbours with boundary omission (US-03.AS-05) [INV-145]", () => {
  it("returns the right/left neighbour inside the column strip", () => {
    expect(adjacentStatus("backlog", "right")).toBe("todo");
    expect(adjacentStatus("todo", "right")).toBe("in_progress");
    expect(adjacentStatus("in_progress", "right")).toBe("done");
    expect(adjacentStatus("done", "left")).toBe("in_progress");
    expect(adjacentStatus("in_progress", "left")).toBe("todo");
    expect(adjacentStatus("todo", "left")).toBe("backlog");
  });

  it("returns null at the boundaries — Zrobione has no right move, Backlog no left", () => {
    expect(adjacentStatus("done", "right")).toBeNull();
    expect(adjacentStatus("backlog", "left")).toBeNull();
  });

  it("returns null for a status that is not a column (cancelled)", () => {
    expect(adjacentStatus("cancelled", "right")).toBeNull();
    expect(adjacentStatus("cancelled", "left")).toBeNull();
  });
});

describe("buildProjectGroups — the groupable List (FR-024, US-03.AS-07)", () => {
  const tasks = [
    makeTask({ id: "g1", position: "a0", status: "done", priority: "P1" }),
    makeTask({ id: "g2", position: "a1", status: "todo", priority: null }),
    makeTask({ id: "g3", position: "a2", status: "todo", priority: "P0" }),
    makeTask({ id: "g4", position: "a3", status: "cancelled", priority: "P3" }),
  ];

  it("'none' yields a single flat group with all tasks in flat order", () => {
    const groups = buildProjectGroups(tasks, "none");

    expect(groups).toHaveLength(1);
    expect(groups[0]!.tasks.map((t) => t.id)).toEqual(["g1", "g2", "g3", "g4"]);
  });

  it("'status' groups in column order with „Anulowane” LAST and empty groups omitted [INV-146]", () => {
    const groups = buildProjectGroups(tasks, "status");

    expect(groups.map((g) => g.label)).toEqual(["Do zrobienia", "Zrobione", "Anulowane"]);
    expect(groups[0]!.tasks.map((t) => t.id)).toEqual(["g2", "g3"]);
    expect(groups[1]!.tasks.map((t) => t.id)).toEqual(["g1"]);
    expect(groups[2]!.tasks.map((t) => t.id)).toEqual(["g4"]);
  });

  it("'status' omits the „Anulowane” group when no task is cancelled [INV-146]", () => {
    const groups = buildProjectGroups(
      tasks.filter((t) => t.status !== "cancelled"),
      "status",
    );

    expect(groups.map((g) => g.label)).toEqual(["Do zrobienia", "Zrobione"]);
  });

  it("'priority' groups P0→P3 then „Bez priorytetu” last, empty groups omitted [INV-147]", () => {
    const groups = buildProjectGroups(tasks, "priority");

    expect(groups.map((g) => g.label)).toEqual(["P0", "P1", "P3", "Bez priorytetu"]);
    expect(groups[0]!.tasks.map((t) => t.id)).toEqual(["g3"]);
    expect(groups[1]!.tasks.map((t) => t.id)).toEqual(["g1"]);
    expect(groups[2]!.tasks.map((t) => t.id)).toEqual(["g4"]);
    expect(groups[3]!.tasks.map((t) => t.id)).toEqual(["g2"]);
  });

  it("keeps the flat (position) order INSIDE every group", () => {
    const groups = buildProjectGroups(
      [
        makeTask({ id: "b", position: "a1", status: "todo" }),
        makeTask({ id: "a", position: "a0", status: "todo" }),
      ],
      "status",
    );

    expect(groups[0]!.tasks.map((t) => t.id)).toEqual(["a", "b"]);
  });
});

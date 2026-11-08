import { describe, expect, it } from "vitest";

import type { components } from "@/lib/api/generated/schema";
import {
  buildPaletteGroups,
  flattenOptions,
  matchActions,
  normalizeQuery,
  PALETTE_ACTIONS,
  projectHref,
  resultCount,
  taskHref,
} from "@/lib/palette";

type TaskResponse = components["schemas"]["TaskResponse"];
type ProjectResponse = components["schemas"]["ProjectResponse"];
type LabelResponse = components["schemas"]["LabelResponse"];

/**
 * The palette pure model (slice 013, T009 — contracts/ui-palette.md): the fixed action catalogue (no shortcut
 * labels — FR-033 deferred), alias matching, navigation targets and the fixed group order. [INV-191] [INV-192] [INV-193]
 */

function task(id: string, projectId: string | null = null): TaskResponse {
  return {
    id,
    title: `Zadanie ${id}`,
    status: "backlog",
    position: "a0",
    version: 0,
    createdAt: "2026-06-01T10:00:00Z",
    updatedAt: "2026-06-01T10:00:00Z",
    completedAt: null,
    dueDate: null,
    dueHasTime: null,
    projectId,
    assignees: [],
    labels: [],
    cycleId: null,
    carriedOver: false,
  } as TaskResponse;
}

function project(id: string): ProjectResponse {
  return { id, name: `Projekt ${id}`, color: "blue", icon: "folder", parentId: null, visibility: "personal", archivedAt: null, version: 0, createdAt: "2026-06-01T10:00:00Z", updatedAt: "2026-06-01T10:00:00Z" } as ProjectResponse;
}

function label(id: string): LabelResponse {
  return { id, name: `Etykieta ${id}`, color: "blue" } as LabelResponse;
}

describe("PALETTE_ACTIONS / matchActions [INV-191] [INV-193]", () => {
  it("the empty query lists the whole fixed catalogue in display order", () => {
    expect(matchActions("")).toEqual([...PALETTE_ACTIONS]);
    expect(PALETTE_ACTIONS.map((a) => a.label)).toEqual([
      "Nowy task",
      "Nowy projekt",
      "Nowy cykl",
      "Przejdź: Inbox",
      "Przejdź: Dziś",
      "Przejdź: Nadchodzące",
      "Przejdź: Przypisane",
      "Przejdź: Cykl",
      "Przejdź: Ustawienia",
    ]);
  });

  it("„create project” (the product-vision alias) and „nowy projekt” both resolve to the Nowy projekt action", () => {
    expect(matchActions("create project").map((a) => a.id)).toEqual(["new-project"]);
    expect(matchActions("NOWY projekt").map((a) => a.id)).toEqual(["new-project"]);
  });

  it("matches diacritic-insensitively and token-AND („przejdz dzis”)", () => {
    expect(matchActions("przejdz dzis").map((a) => a.id)).toEqual(["go-today"]);
    expect(matchActions("xyz")).toEqual([]);
  });

  it("carries no shortcut labels (FR-033 deferred)", () => {
    for (const action of PALETTE_ACTIONS) {
      expect(Object.keys(action)).not.toContain("shortcut");
    }
  });
});

describe("navigation targets [INV-192]", () => {
  it("a task opens its location with the drawer", () => {
    expect(taskHref(task("t1"))).toBe("/?task=t1");
    expect(taskHref(task("t2", "p9"))).toBe("/projects/p9?task=t2");
    expect(projectHref(project("p9"))).toBe("/projects/p9");
  });

  it("normalizeQuery trims and case-folds for the query key", () => {
    expect(normalizeQuery("  Mleko ")).toBe("mleko");
  });
});

describe("buildPaletteGroups", () => {
  it("empty query → only Akcje (no data groups even when data is present)", () => {
    const groups = buildPaletteGroups("", { tasks: [task("t1")], projects: [], labels: [] });
    expect(groups.map((g) => g.key)).toEqual(["actions"]);
  });

  it("orders Akcje → Zadania → Projekty → Etykiety and omits empty groups", () => {
    const groups = buildPaletteGroups("projekt", { tasks: [task("t1")], projects: [project("p1")], labels: [] });
    expect(groups.map((g) => g.key)).toEqual(["actions", "tasks", "projects"]);
    expect(groups.map((g) => g.heading)).toEqual(["Akcje", "Zadania", "Projekty"]);
    expect(flattenOptions(groups).map((o) => o.id)).toEqual(["action:new-project", "task:t1", "project:p1"]);
  });

  it("a query matching no action still renders the data groups", () => {
    const groups = buildPaletteGroups("zzz", { tasks: [], projects: [], labels: [label("l1")] });
    expect(groups.map((g) => g.key)).toEqual(["labels"]);
    expect(resultCount({ tasks: [], projects: [], labels: [label("l1")] })).toBe(1);
  });
});

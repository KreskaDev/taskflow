// @vitest-environment node
import { describe, expect, it } from "vitest";
import type { components } from "@/lib/api/generated/schema";
import { buildProjectTree, type ProjectTreeNode } from "@/components/layout/Sidebar";

/**
 * Sidebar tree assembly (T027, RED — drives T028; research R16). The API returns a FLAT
 * `ProjectResponse[]`; the sidebar assembles the one-level parent/child tree client-side. Pins the
 * pure `buildProjectTree(flatList)` helper (extracted like `createGlobalShortcutsListener`) so the
 * grouping logic is unit-testable without a full render. The list query already returns ACTIVE-only
 * rows (R8), but the helper still defensively drops any archived row from the default tree.
 */

type ProjectResponse = components["schemas"]["ProjectResponse"];

function makeProject(overrides: Partial<ProjectResponse> & Pick<ProjectResponse, "id">): ProjectResponse {
  return {
    id: overrides.id,
    name: overrides.name ?? "Project",
    color: overrides.color ?? "blue",
    icon: overrides.icon ?? "folder",
    parentId: overrides.parentId ?? null,
    visibility: overrides.visibility ?? "personal",
    archivedAt: overrides.archivedAt ?? null,
    version: overrides.version ?? 0,
    createdAt: overrides.createdAt ?? "2026-06-20T00:00:00.000Z",
    updatedAt: overrides.updatedAt ?? "2026-06-20T00:00:00.000Z",
  };
}

const PARENT = "11111111-1111-7111-8111-111111111111";
const CHILD_A = "22222222-2222-7222-8222-222222222222";
const CHILD_B = "33333333-3333-7333-8333-333333333333";
const TOP_OTHER = "44444444-4444-7444-8444-444444444444";

function ids(nodes: ProjectTreeNode[]): string[] {
  return nodes.map((n) => n.project.id);
}

describe("buildProjectTree — one-level tree assembly from the flat list (R16)", () => {
  it("groups children under their parent by parentId", () => {
    const flat = [
      makeProject({ id: PARENT, name: "Parent", parentId: null }),
      makeProject({ id: CHILD_A, name: "Child A", parentId: PARENT }),
      makeProject({ id: CHILD_B, name: "Child B", parentId: PARENT }),
      makeProject({ id: TOP_OTHER, name: "Other", parentId: null }),
    ];

    const tree = buildProjectTree(flat);

    // Two top-level nodes: Parent and Other.
    expect(ids(tree)).toEqual([PARENT, TOP_OTHER]);
    // Parent has both children nested under it.
    const parentNode = tree.find((n) => n.project.id === PARENT)!;
    expect(ids(parentNode.children)).toEqual([CHILD_A, CHILD_B]);
    // A top-level project with no children has an empty children array.
    const otherNode = tree.find((n) => n.project.id === TOP_OTHER)!;
    expect(otherNode.children).toEqual([]);
  });

  it("promotes an orphan (parentId points at a project not in the list) to top-level", () => {
    // A child whose parent is absent from the active list (e.g. parent archived) must still render
    // — it surfaces as a top-level node rather than vanishing.
    const flat = [makeProject({ id: CHILD_A, name: "Orphan", parentId: PARENT })];
    const tree = buildProjectTree(flat);
    expect(ids(tree)).toEqual([CHILD_A]);
    expect(tree[0]!.children).toEqual([]);
  });

  it("excludes archived rows from the default tree", () => {
    const flat = [
      makeProject({ id: PARENT, name: "Parent", parentId: null }),
      makeProject({ id: TOP_OTHER, name: "Archived", parentId: null, archivedAt: "2026-06-23T00:00:00Z" }),
    ];
    const tree = buildProjectTree(flat);
    expect(ids(tree)).toEqual([PARENT]);
  });

  it("returns an empty tree for an empty list", () => {
    expect(buildProjectTree([])).toEqual([]);
  });
});

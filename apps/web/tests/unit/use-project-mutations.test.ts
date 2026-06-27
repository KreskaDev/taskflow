// @vitest-environment node
import { QueryClient } from "@tanstack/react-query";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import type { components } from "@/lib/api/generated/schema";
import {
  ACTIVE_PROJECTS_KEY,
  ARCHIVED_PROJECTS_KEY,
  archiveProjectMutationOptions,
  type ArchiveProjectVariables,
  createProjectMutationOptions,
  type CreateProjectVariables,
  deleteProjectMutationOptions,
  type DeleteProjectVariables,
  editProjectMutationOptions,
  type EditProjectVariables,
  type ProjectMutationContext,
  nestingPreventionMessage,
  unarchiveProjectMutationOptions,
  type UnarchiveProjectVariables,
} from "@/hooks/useProjectMutations";

/**
 * Optimistic project mutations (T025, RED — drives T026; research R15). Pins the public shape of
 * `@/hooks/useProjectMutations`, mirroring the slice-002 `createTaskMutationOptions` recipe altitude:
 * each is a PURE TanStack-Query options factory whose `onMutate` cancels + snapshots + patches the
 * flat `['projects']` list in place, `onError` rolls the snapshot back, and `onSettled` invalidates.
 *
 * Cache shape: `['projects']` holds the flat ACTIVE `ProjectResponse[]` (the Sidebar assembles the
 * one-level tree at render); `['projects','archived']` holds the archived listing (R8). Archive moves
 * a row from active → (server) archived (optimistically REMOVED from active); unarchive removes from
 * archived. `onSettled` invalidates BOTH keys so the moved row reconciles on either side.
 *
 * The typed client is mocked so each recipe's `mutationFn` (the wire surface) is an observable spy.
 */
vi.mock("@/lib/api/client", async (importOriginal) => {
  const actual = await importOriginal<typeof import("@/lib/api/client")>();
  return {
    ...actual,
    apiClient: { GET: vi.fn(), PUT: vi.fn(), PATCH: vi.fn(), DELETE: vi.fn() },
  };
});

const { apiClient } = await import("@/lib/api/client");
const putSpy = apiClient.PUT as unknown as ReturnType<typeof vi.fn>;
const patchSpy = apiClient.PATCH as unknown as ReturnType<typeof vi.fn>;
const deleteSpy = apiClient.DELETE as unknown as ReturnType<typeof vi.fn>;

type ProjectResponse = components["schemas"]["ProjectResponse"];

const PARENT_ID = "11111111-1111-7111-8111-111111111111";
const CHILD_ID = "22222222-2222-7222-8222-222222222222";
const OTHER_ID = "33333333-3333-7333-8333-333333333333";

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

/** A parent + its child + an unrelated top-level project — the flat ACTIVE list as cached. */
function seedActive(): ProjectResponse[] {
  return [
    makeProject({ id: PARENT_ID, name: "Parent", parentId: null }),
    makeProject({ id: CHILD_ID, name: "Child", parentId: PARENT_ID }),
    makeProject({ id: OTHER_ID, name: "Other", parentId: null }),
  ];
}

function primedClient(active: ProjectResponse[], archived: ProjectResponse[] = []): QueryClient {
  const queryClient = new QueryClient();
  queryClient.setQueryData<ProjectResponse[]>(ACTIVE_PROJECTS_KEY, active);
  queryClient.setQueryData<ProjectResponse[]>(ARCHIVED_PROJECTS_KEY, archived);
  return queryClient;
}

beforeEach(() => {
  putSpy.mockReset();
  patchSpy.mockReset();
  deleteSpy.mockReset();
});

afterEach(() => {
  vi.restoreAllMocks();
});

describe("createProjectMutationOptions — optimistic create", () => {
  it("onMutate cancels in-flight ['projects'] queries before touching the cache", async () => {
    const seed = seedActive();
    const queryClient = primedClient(seed);
    const cancelSpy = vi.spyOn(queryClient, "cancelQueries");

    const variables: CreateProjectVariables = { id: "44444444-4444-7444-8444-444444444444", name: "New", color: "red", icon: "star" };
    await createProjectMutationOptions(queryClient).onMutate?.(variables);

    expect(cancelSpy).toHaveBeenCalledWith({ queryKey: ACTIVE_PROJECTS_KEY });
  });

  it("onMutate snapshots the active list and appends the optimistic project", async () => {
    const seed = seedActive();
    const queryClient = primedClient(seed);
    const variables: CreateProjectVariables = {
      id: "44444444-4444-7444-8444-444444444444",
      name: "New",
      color: "red",
      icon: "star",
      parentId: PARENT_ID,
    };

    const options = createProjectMutationOptions(queryClient);
    const context = (await options.onMutate?.(variables)) as ProjectMutationContext;

    expect(context.previousActive).toEqual(seed);
    const next = queryClient.getQueryData<ProjectResponse[]>(ACTIVE_PROJECTS_KEY);
    expect(next).toHaveLength(seed.length + 1);
    const added = next?.find((p) => p.id === variables.id);
    expect(added?.name).toBe("New");
    expect(added?.color).toBe("red");
    expect(added?.icon).toBe("star");
    expect(added?.parentId).toBe(PARENT_ID);
    expect(added?.version).toBe(0);
  });

  it("mutationFn PUTs the create body (name/color/icon/parentId) to /api/projects/{id}", async () => {
    const queryClient = primedClient(seedActive());
    const variables: CreateProjectVariables = { id: "44444444-4444-7444-8444-444444444444", name: "New", color: "red", icon: "star", parentId: null };
    putSpy.mockResolvedValue({ data: makeProject({ id: variables.id }), error: undefined });

    await createProjectMutationOptions(queryClient).mutationFn(variables);

    expect(putSpy).toHaveBeenCalledTimes(1);
    const [path, init] = putSpy.mock.calls[0]!;
    expect(path).toBe("/api/projects/{id}");
    expect((init as { params: { path: { id: string } } }).params.path.id).toBe(variables.id);
    expect((init as { body: Record<string, unknown> }).body).toEqual({
      name: "New",
      color: "red",
      icon: "star",
      parentId: null,
    });
  });

  it("onError rolls the active list back to the snapshot", async () => {
    const seed = seedActive();
    const queryClient = primedClient(seed);
    const variables: CreateProjectVariables = { id: "44444444-4444-7444-8444-444444444444", name: "New", color: "red", icon: "star" };

    const options = createProjectMutationOptions(queryClient);
    const context = (await options.onMutate?.(variables)) as ProjectMutationContext;
    expect(queryClient.getQueryData<ProjectResponse[]>(ACTIVE_PROJECTS_KEY)).toHaveLength(seed.length + 1);

    await options.onError?.(new Error("network"), variables, context);

    expect(queryClient.getQueryData<ProjectResponse[]>(ACTIVE_PROJECTS_KEY)).toEqual(seed);
  });

  it("onSettled invalidates the active projects key", async () => {
    const queryClient = primedClient(seedActive());
    const invalidateSpy = vi.spyOn(queryClient, "invalidateQueries");
    const variables: CreateProjectVariables = { id: "44444444-4444-7444-8444-444444444444", name: "New", color: "red", icon: "star" };

    const options = createProjectMutationOptions(queryClient);
    const context = (await options.onMutate?.(variables)) as ProjectMutationContext;
    await options.onSettled?.(makeProject({ id: variables.id }), null, variables, context);

    expect(invalidateSpy).toHaveBeenCalledWith({ queryKey: ACTIVE_PROJECTS_KEY });
  });
});

describe("editProjectMutationOptions — optimistic edit / re-parent", () => {
  it("onMutate re-stamps name/color/icon/parentId on the target row in place", async () => {
    const seed = seedActive();
    const queryClient = primedClient(seed);
    const variables: EditProjectVariables = {
      id: OTHER_ID,
      name: "Renamed",
      color: "teal",
      icon: "flag",
      parentId: PARENT_ID,
      version: 0,
    };

    const options = editProjectMutationOptions(queryClient);
    const context = (await options.onMutate?.(variables)) as ProjectMutationContext;

    expect(context.previousActive).toEqual(seed);
    const edited = queryClient.getQueryData<ProjectResponse[]>(ACTIVE_PROJECTS_KEY)?.find((p) => p.id === OTHER_ID);
    expect(edited?.name).toBe("Renamed");
    expect(edited?.color).toBe("teal");
    expect(edited?.icon).toBe("flag");
    expect(edited?.parentId).toBe(PARENT_ID);
  });

  it("mutationFn PATCHes the whole-object edit body (incl. parentId + version)", async () => {
    const queryClient = primedClient(seedActive());
    const variables: EditProjectVariables = { id: OTHER_ID, name: "Renamed", color: "teal", icon: "flag", parentId: null, version: 4 };
    patchSpy.mockResolvedValue({ data: makeProject({ id: OTHER_ID, version: 5 }), error: undefined });

    await editProjectMutationOptions(queryClient).mutationFn(variables);

    const [path, init] = patchSpy.mock.calls[0]!;
    expect(path).toBe("/api/projects/{id}");
    expect((init as { params: { path: { id: string } } }).params.path.id).toBe(OTHER_ID);
    expect((init as { body: Record<string, unknown> }).body).toEqual({
      name: "Renamed",
      color: "teal",
      icon: "flag",
      parentId: null,
      version: 4,
    });
  });

  it("onError rolls the active list back", async () => {
    const seed = seedActive();
    const queryClient = primedClient(seed);
    const variables: EditProjectVariables = { id: OTHER_ID, name: "X", color: "teal", icon: "flag", parentId: null, version: 0 };

    const options = editProjectMutationOptions(queryClient);
    const context = (await options.onMutate?.(variables)) as ProjectMutationContext;
    await options.onError?.(new Error("network"), variables, context);

    expect(queryClient.getQueryData<ProjectResponse[]>(ACTIVE_PROJECTS_KEY)).toEqual(seed);
  });
});

describe("archiveProjectMutationOptions — optimistic archive (removes from active)", () => {
  it("onMutate optimistically REMOVES the archived project from the active list", async () => {
    const seed = seedActive();
    const queryClient = primedClient(seed);
    const variables: ArchiveProjectVariables = { id: OTHER_ID, version: 0 };

    const options = archiveProjectMutationOptions(queryClient);
    const context = (await options.onMutate?.(variables)) as ProjectMutationContext;

    expect(context.previousActive).toEqual(seed);
    const next = queryClient.getQueryData<ProjectResponse[]>(ACTIVE_PROJECTS_KEY);
    expect(next?.some((p) => p.id === OTHER_ID)).toBe(false);
  });

  it("mutationFn PATCHes /archive carrying version + optional childDisposition", async () => {
    const queryClient = primedClient(seedActive());
    const variables: ArchiveProjectVariables = { id: PARENT_ID, version: 2, childDisposition: "orphan_to_top" };
    patchSpy.mockResolvedValue({ data: makeProject({ id: PARENT_ID, archivedAt: "2026-06-23T00:00:00Z" }), error: undefined });

    await archiveProjectMutationOptions(queryClient).mutationFn(variables);

    const [path, init] = patchSpy.mock.calls[0]!;
    expect(path).toBe("/api/projects/{id}/archive");
    expect((init as { body: Record<string, unknown> }).body).toEqual({ version: 2, childDisposition: "orphan_to_top" });
  });

  it("onSettled invalidates BOTH the active and archived keys", async () => {
    const queryClient = primedClient(seedActive());
    const invalidateSpy = vi.spyOn(queryClient, "invalidateQueries");
    const variables: ArchiveProjectVariables = { id: OTHER_ID, version: 0 };

    const options = archiveProjectMutationOptions(queryClient);
    const context = (await options.onMutate?.(variables)) as ProjectMutationContext;
    await options.onSettled?.(makeProject({ id: OTHER_ID }), null, variables, context);

    expect(invalidateSpy).toHaveBeenCalledWith({ queryKey: ACTIVE_PROJECTS_KEY });
    expect(invalidateSpy).toHaveBeenCalledWith({ queryKey: ARCHIVED_PROJECTS_KEY });
  });
});

describe("unarchiveProjectMutationOptions — optimistic unarchive (removes from archived)", () => {
  it("onMutate optimistically REMOVES the project from the archived list", async () => {
    const archived = [makeProject({ id: OTHER_ID, archivedAt: "2026-06-23T00:00:00Z" })];
    const queryClient = primedClient([], archived);
    const variables: UnarchiveProjectVariables = { id: OTHER_ID, version: 1 };

    const options = unarchiveProjectMutationOptions(queryClient);
    const context = (await options.onMutate?.(variables)) as ProjectMutationContext;

    expect(context.previousArchived).toEqual(archived);
    const next = queryClient.getQueryData<ProjectResponse[]>(ARCHIVED_PROJECTS_KEY);
    expect(next?.some((p) => p.id === OTHER_ID)).toBe(false);
  });

  it("mutationFn PATCHes /unarchive carrying version", async () => {
    const queryClient = primedClient([], [makeProject({ id: OTHER_ID })]);
    const variables: UnarchiveProjectVariables = { id: OTHER_ID, version: 1 };
    patchSpy.mockResolvedValue({ data: makeProject({ id: OTHER_ID }), error: undefined });

    await unarchiveProjectMutationOptions(queryClient).mutationFn(variables);

    const [path, init] = patchSpy.mock.calls[0]!;
    expect(path).toBe("/api/projects/{id}/unarchive");
    expect((init as { body: Record<string, unknown> }).body).toEqual({ version: 1 });
  });
});

describe("deleteProjectMutationOptions — optimistic delete (removes from active)", () => {
  it("onMutate optimistically REMOVES the deleted project from the active list", async () => {
    const seed = seedActive();
    const queryClient = primedClient(seed);
    const variables: DeleteProjectVariables = { id: OTHER_ID, version: 0, taskDisposition: "cascade", childDisposition: "cascade" };

    const options = deleteProjectMutationOptions(queryClient);
    const context = (await options.onMutate?.(variables)) as ProjectMutationContext;

    expect(context.previousActive).toEqual(seed);
    expect(queryClient.getQueryData<ProjectResponse[]>(ACTIVE_PROJECTS_KEY)?.some((p) => p.id === OTHER_ID)).toBe(false);
  });

  it("mutationFn DELETEs with version/taskDisposition/childDisposition as QUERY params (no body)", async () => {
    const queryClient = primedClient(seedActive());
    const variables: DeleteProjectVariables = { id: PARENT_ID, version: 3, taskDisposition: "move_to_inbox", childDisposition: "orphan_to_top" };
    deleteSpy.mockResolvedValue({ data: undefined, error: undefined });

    await deleteProjectMutationOptions(queryClient).mutationFn(variables);

    const [path, init] = deleteSpy.mock.calls[0]!;
    expect(path).toBe("/api/projects/{id}");
    expect((init as { params: { path: { id: string } } }).params.path.id).toBe(PARENT_ID);
    expect((init as { params: { query: Record<string, unknown> } }).params.query).toEqual({
      version: 3,
      taskDisposition: "move_to_inbox",
      childDisposition: "orphan_to_top",
    });
  });

  it("onError rolls the active list back so the deleted project reappears", async () => {
    const seed = seedActive();
    const queryClient = primedClient(seed);
    const variables: DeleteProjectVariables = { id: OTHER_ID, version: 0 };

    const options = deleteProjectMutationOptions(queryClient);
    const context = (await options.onMutate?.(variables)) as ProjectMutationContext;
    await options.onError?.(new Error("network"), variables, context);

    expect(queryClient.getQueryData<ProjectResponse[]>(ACTIVE_PROJECTS_KEY)).toEqual(seed);
  });
});

describe("nestingPreventionMessage — client-side one-level guard (R15, AS-03/AS-09)", () => {
  const tree = seedActive();

  it("returns null when there is no parent (a top-level project is always allowed)", () => {
    expect(nestingPreventionMessage(tree, OTHER_ID, null)).toBeNull();
  });

  it("returns a message when the chosen parent is ITSELF a child (would create a grandchild)", () => {
    // CHILD_ID has parentId = PARENT_ID, so parenting OTHER under CHILD breaks the one-level rule.
    const message = nestingPreventionMessage(tree, OTHER_ID, CHILD_ID);
    expect(message).not.toBeNull();
    expect(typeof message).toBe("string");
  });

  it("returns a message when the project being parented HAS children", () => {
    // PARENT_ID has a child (CHILD_ID); giving PARENT a parent would push its child to depth 2.
    const message = nestingPreventionMessage(tree, PARENT_ID, OTHER_ID);
    expect(message).not.toBeNull();
  });

  it("returns null for a legal re-parent (top-level parent, childless project)", () => {
    expect(nestingPreventionMessage(tree, OTHER_ID, PARENT_ID)).toBeNull();
  });
});

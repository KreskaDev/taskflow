// @vitest-environment node
import { QueryClient } from "@tanstack/react-query";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import type { components } from "@/lib/api/generated/schema";
import { between } from "@/lib/position";
import {
  type CreateTaskContext,
  type CreateTaskVariables,
  createTaskMutationOptions,
  // ── NEW public shape pinned by T057 (RED — these exports do not exist yet) ──
  type DeleteTaskContext,
  type DeleteTaskVariables,
  deleteTaskMutationOptions,
  type RenameTaskContext,
  type RenameTaskVariables,
  renameTaskMutationOptions,
  type ReorderTaskContext,
  type ReorderTaskVariables,
  reorderTaskMutationOptions,
  TaskMutationError,
  type ToggleDoneContext,
  type ToggleDoneVariables,
  toggleDoneMutationOptions,
  // ── NEW public shape pinned by T038 (RED — these exports do not exist yet) ──
  type MoveTaskToProjectContext,
  type MoveTaskToProjectVariables,
  moveTaskToProjectMutationOptions,
} from "@/hooks/useTaskMutations";

/**
 * The typed client is mocked so the versioned recipes' `mutationFn` (the surface the
 * 409 intent-reapply re-issues through) is observable as a spy. `mapError` and the
 * other exports keep their real behaviour — only `apiClient.PATCH`/`DELETE` are stubs.
 * The CREATE suite below never invokes its `mutationFn`, so this mock leaves it untouched.
 */
vi.mock("@/lib/api/client", async (importOriginal) => {
  const actual = await importOriginal<typeof import("@/lib/api/client")>();
  return {
    ...actual,
    apiClient: {
      GET: vi.fn(),
      PUT: vi.fn(),
      PATCH: vi.fn(),
      DELETE: vi.fn(),
    },
  };
});

// Bound after the mock so the spies are the same instances the recipes import.
const { apiClient } = await import("@/lib/api/client");
const putSpy = apiClient.PUT as unknown as ReturnType<typeof vi.fn>;
const patchSpy = apiClient.PATCH as unknown as ReturnType<typeof vi.fn>;
const deleteSpy = apiClient.DELETE as unknown as ReturnType<typeof vi.fn>;

/**
 * Optimistic CREATE recipe (T036, RED — covers T037 create only; FR-001, SC-003, research R10).
 *
 * SCOPE: create only. Rename/toggle/reorder/delete + the once-only `409 version_conflict`
 * intent-reapply recipe are authored in US8/T057 — keeping this US1 suite fully GREEN at the
 * US1 checkpoint (Constitution VIII: a failing suite blocks merge, and US1 is independently shippable).
 *
 * PUBLIC SHAPE this test pins for the (not-yet-existing) `@/hooks/useTaskMutations`:
 *   - `createTaskMutationOptions(queryClient)` — a pure TanStack-Query `UseMutationOptions`
 *     factory (the tested surface; no React render needed). Its `onMutate`/`onError`/`onSettled`
 *     implement the cancel-snapshot-rollback recipe against the SINGLE `['tasks']` key.
 *   - `useTaskMutations(): { createTask(title) }` — the "use client" hook wrapper (NOT tested here;
 *     it mints the id via `lib/id.ts` and the newest-first rank via `lib/position.ts` `between(null, head)`,
 *     then calls `mutate({ id, title, position })`).
 *
 * WHY id + position live in `variables` (not minted inside `onMutate`): TanStack v5 passes the
 * `onMutate` return value as context to `onError`/`onSettled` but NEVER to `mutationFn`. The
 * `mutationFn` is the idempotent `PUT /api/tasks/{id}` carrying `{ title, position }`, so the
 * client-minted UUIDv7 id (research R5/R7) and the fractional rank MUST already be on `variables`
 * to reach both the optimistic cache write and the server call with one stable identity.
 */

type TaskResponse = components["schemas"]["TaskResponse"];

const TASKS_KEY = ["tasks"] as const;

/** A fully-formed read-model row, mirroring the lean `TaskResponse` DTO the list query caches. */
function makeTask(overrides: Partial<TaskResponse> & Pick<TaskResponse, "id" | "position">): TaskResponse {
  return {
    id: overrides.id,
    title: overrides.title ?? "seed task",
    status: overrides.status ?? "backlog",
    position: overrides.position,
    version: overrides.version ?? 0,
    createdAt: overrides.createdAt ?? "2026-06-20T00:00:00.000Z",
    updatedAt: overrides.updatedAt ?? "2026-06-20T00:00:00.000Z",
    completedAt: overrides.completedAt ?? null,
    assignees: overrides.assignees ?? [],
    labels: overrides.labels ?? [],
  };
}

/** A two-row seed list ordered ascending by `position` (top-is-lowest), as the cache holds it. */
function seedTasks(): TaskResponse[] {
  const p0 = between(null, null);
  const head = between(null, p0); // sorts before p0 → the current top row
  return [
    makeTask({ id: "11111111-1111-7111-8111-111111111111", title: "head", position: head }),
    makeTask({ id: "22222222-2222-7222-8222-222222222222", title: "tail", position: p0 }),
  ];
}

/** Builds the create variables a wrapper would pass: client-minted id + newest-first rank. */
function makeVariables(seed: TaskResponse[]): CreateTaskVariables {
  const head = seed[0];
  return {
    id: "99999999-9999-7999-8999-999999999999",
    title: "newest task",
    position: between(null, head ? head.position : null),
  };
}

function primedClient(seed: TaskResponse[]): QueryClient {
  const queryClient = new QueryClient();
  queryClient.setQueryData<TaskResponse[]>(TASKS_KEY, seed);
  return queryClient;
}

/**
 * Builds the structured 409 the API emits on a stale write. The mutation `mutationFn`
 * MUST throw an error that KEEPS the machine-readable `errorCode` (so `onError` can
 * branch on `version_conflict`) while `message` stays the FR-049 friendly text the
 * global `MutationCache` announcer surfaces verbatim. A plain `Error` (which the create
 * recipe throws) would erase `errorCode` and make 409 undetectable — hence `TaskMutationError`.
 */
function versionConflict(): TaskMutationError {
  return new TaskMutationError("version_conflict", "This item was changed elsewhere.");
}

/** A non-conflict failure (e.g. network) — `onError` must NOT treat this as a 409 reapply. */
function networkError(): Error {
  return new Error("network down");
}

beforeEach(() => {
  putSpy.mockReset();
  patchSpy.mockReset();
  deleteSpy.mockReset();
});

afterEach(() => {
  vi.restoreAllMocks();
});

describe("createTaskMutationOptions — optimistic create recipe", () => {
  it("onMutate cancels in-flight ['tasks'] queries before touching the cache", async () => {
    const seed = seedTasks();
    const queryClient = primedClient(seed);
    const cancelSpy = vi.spyOn(queryClient, "cancelQueries");

    const options = createTaskMutationOptions(queryClient);
    expect(options.onMutate).toBeDefined();

    await options.onMutate?.(makeVariables(seed));

    expect(cancelSpy).toHaveBeenCalledWith({ queryKey: TASKS_KEY });
  });

  it("onMutate snapshots the current ['tasks'] data and returns it as context", async () => {
    const seed = seedTasks();
    const queryClient = primedClient(seed);

    const options = createTaskMutationOptions(queryClient);
    const context = (await options.onMutate?.(makeVariables(seed))) as CreateTaskContext;

    expect(context.previousTasks).toEqual(seed);
  });

  it("onMutate optimistically inserts the new task at the TOP (newest-first) of the cache", async () => {
    const seed = seedTasks();
    const queryClient = primedClient(seed);
    const variables = makeVariables(seed);

    const options = createTaskMutationOptions(queryClient);
    await options.onMutate?.(variables);

    const next = queryClient.getQueryData<TaskResponse[]>(TASKS_KEY);
    expect(next).toBeDefined();
    expect(next).toHaveLength(seed.length + 1);

    const top = next?.[0];
    // The optimistic row carries the client-minted id and the rank from variables verbatim...
    expect(top?.id).toBe(variables.id);
    expect(top?.title).toBe(variables.title);
    expect(top?.position).toBe(variables.position);
    // ...and its rank sorts strictly before the previous head — that IS newest-first placement.
    expect(top?.position !== undefined && seed[0] !== undefined && top.position < seed[0].position).toBe(true);
    // A brand-new optimistic row starts at backlog/version 0 (the round-trip token).
    expect(top?.status).toBe("backlog");
    expect(top?.version).toBe(0);
    // Timestamps are present (don't pin volatile values — keeps T037 deterministically GREEN-able).
    expect(typeof top?.createdAt).toBe("string");
    expect(typeof top?.updatedAt).toBe("string");
    // The pre-existing rows are preserved in order below the new top row.
    expect(next?.slice(1)).toEqual(seed);
  });

  it("onMutate seeds an empty cache so the first task still paints at the top", async () => {
    const queryClient = primedClient([]);
    const variables: CreateTaskVariables = {
      id: "99999999-9999-7999-8999-999999999999",
      title: "first ever",
      position: between(null, null),
    };

    const options = createTaskMutationOptions(queryClient);
    const context = (await options.onMutate?.(variables)) as CreateTaskContext;

    expect(context.previousTasks).toEqual([]);
    const next = queryClient.getQueryData<TaskResponse[]>(TASKS_KEY);
    expect(next).toHaveLength(1);
    expect(next?.[0]?.id).toBe(variables.id);
  });

  it("onError restores the snapshot in place (the optimistic row is rolled back)", async () => {
    const seed = seedTasks();
    const queryClient = primedClient(seed);
    const variables = makeVariables(seed);

    const options = createTaskMutationOptions(queryClient);
    const context = (await options.onMutate?.(variables)) as CreateTaskContext;

    // Sanity: the optimistic write happened.
    expect(queryClient.getQueryData<TaskResponse[]>(TASKS_KEY)).toHaveLength(seed.length + 1);

    options.onError?.(new Error("network down"), variables, context);

    expect(queryClient.getQueryData<TaskResponse[]>(TASKS_KEY)).toEqual(seed);
  });

  it("onSettled invalidates ['tasks'] to reconcile with server truth", async () => {
    const seed = seedTasks();
    const queryClient = primedClient(seed);
    const variables = makeVariables(seed);
    const invalidateSpy = vi.spyOn(queryClient, "invalidateQueries");

    const options = createTaskMutationOptions(queryClient);
    const context = (await options.onMutate?.(variables)) as CreateTaskContext;

    const created = makeTask({ id: variables.id, title: variables.title, position: variables.position });
    await options.onSettled?.(created, null, variables, context);

    expect(invalidateSpy).toHaveBeenCalledWith({ queryKey: TASKS_KEY });
  });
});

/**
 * NEW (T015, RED — drives T016; US1; FR-001, research R8/R9).
 *
 * Pins the optimistic-CREATE due-date surface threaded through the recipe (T016): the
 * `CreateTaskVariables` carries an OPTIONAL resolved `dueDate` (ISO string) + `dueHasTime`
 * (boolean). The `Date → ISO string` conversion lives in the `createTask` wrapper (NOT here),
 * so the recipe layer only ever sees the wire-shaped string. Three guarantees:
 *   - `mutationFn` PUT body carries `dueDate`/`dueHasTime` (the wire contract).
 *   - `onMutate` paints both onto the optimistic top row (`?? null` to satisfy `TaskResponse`).
 *   - `onError` rolls a due-date-carrying variable back like any other create.
 */
describe("createTaskMutationOptions — optimistic create carries the resolved due date", () => {
  /** Create variables resolved WITH a due date: an ISO-string instant + `has_time`. */
  function makeDatedVariables(seed: TaskResponse[]): CreateTaskVariables {
    const head = seed[0];
    return {
      id: "99999999-9999-7999-8999-999999999999",
      title: "Kupic mleko",
      position: between(null, head ? head.position : null),
      dueDate: "2026-06-21T15:00:00.000Z",
      dueHasTime: true,
    };
  }

  it("mutationFn PUT body sends dueDate/dueHasTime alongside title + position", async () => {
    const seed = seedTasks();
    const queryClient = primedClient(seed);
    const variables = makeDatedVariables(seed);
    putSpy.mockResolvedValue({
      data: makeTask({
        id: variables.id,
        title: variables.title,
        position: variables.position,
        dueDate: variables.dueDate,
        dueHasTime: variables.dueHasTime,
      }),
      error: undefined,
    });

    const options = createTaskMutationOptions(queryClient);
    await options.mutationFn(variables);

    expect(putSpy).toHaveBeenCalledTimes(1);
    const [path, init] = putSpy.mock.calls[0]!;
    expect(path).toBe("/api/tasks/{id}");
    expect((init as { params: { path: { id: string } } }).params.path.id).toBe(variables.id);
    expect((init as { body: Record<string, unknown> }).body).toEqual({
      title: variables.title,
      position: variables.position,
      dueDate: "2026-06-21T15:00:00.000Z",
      dueHasTime: true,
    });
  });

  it("onMutate paints dueDate/dueHasTime on the optimistic top row", async () => {
    const seed = seedTasks();
    const queryClient = primedClient(seed);
    const variables = makeDatedVariables(seed);

    const options = createTaskMutationOptions(queryClient);
    await options.onMutate?.(variables);

    const top = queryClient.getQueryData<TaskResponse[]>(TASKS_KEY)?.[0];
    expect(top?.dueDate).toBe("2026-06-21T15:00:00.000Z");
    expect(top?.dueHasTime).toBe(true);
  });

  it("onMutate paints null due fields for a dateless create (the optimistic row stays TaskResponse-shaped)", async () => {
    const seed = seedTasks();
    const queryClient = primedClient(seed);
    // No dueDate/dueHasTime carried — a plain dateless capture.
    const variables = makeVariables(seed);

    const options = createTaskMutationOptions(queryClient);
    await options.onMutate?.(variables);

    const top = queryClient.getQueryData<TaskResponse[]>(TASKS_KEY)?.[0];
    expect(top?.dueDate).toBeNull();
    expect(top?.dueHasTime).toBeNull();
  });

  it("onError rolls back a due-date-carrying optimistic row", async () => {
    const seed = seedTasks();
    const queryClient = primedClient(seed);
    const variables = makeDatedVariables(seed);

    const options = createTaskMutationOptions(queryClient);
    const context = (await options.onMutate?.(variables)) as CreateTaskContext;
    expect(queryClient.getQueryData<TaskResponse[]>(TASKS_KEY)).toHaveLength(seed.length + 1);

    options.onError?.(new Error("network down"), variables, context);

    expect(queryClient.getQueryData<TaskResponse[]>(TASKS_KEY)).toEqual(seed);
  });
});

/**
 * NEW (T057, RED — drives T058/T057-impl; US8; FR-001/FR-093/FR-097/FR-102, research R10/R18).
 *
 * Pins the public shape the four edit recipes add to `@/hooks/useTaskMutations`, mirroring
 * the create factory exactly: each is a pure TanStack-Query options factory
 * (`<verb>TaskMutationOptions(queryClient)`) whose `onMutate` cancels+snapshots+applies-in-place,
 * `onError` rolls the snapshot back in place, and `onSettled` invalidates the SINGLE `['tasks']`
 * key. `useTaskMutations()` (the "use client" wrapper) returns the driven helpers and is NOT
 * tested here — same altitude split as create.
 *
 * The 409 `version_conflict` once-only intent-reapply is SELF-CONTAINED in `onError`: there is
 * no `mutate` handle inside a pure factory, so on a `version_conflict` the recipe refetches
 * `['tasks']` then re-issues the user's INTENT exactly ONCE through its own `mutationFn` (the
 * mocked `apiClient`). The cap is STRUCTURAL — a repeat 409 on the reapply is caught and stops
 * (no livelock), surfacing the FR-049 message. A non-conflict error (network) is a plain
 * rollback with NO reapply. DELETE is version-free (research R10) — it has NO 409 path; its
 * recipe optimistically REMOVES the row and rollback REAPPEARS it at its original index.
 *
 * Why the structured `TaskMutationError`: the create recipe throws `new Error(mapError(...).message)`,
 * which DROPS `errorCode` before `onError` sees it. A versioned write MUST preserve `errorCode`
 * so `onError` can branch on `version_conflict` while `message` stays the announced friendly text.
 */

/** Three-row seed ordered ascending by position (top-is-lowest), the cache's natural order. */
function seedThree(): TaskResponse[] {
  const p0 = between(null, null);
  const pMid = between(null, p0); // sorts before p0
  const pTop = between(null, pMid); // sorts before pMid → visual top
  return [
    makeTask({ id: "aaaaaaaa-aaaa-7aaa-8aaa-aaaaaaaaaaaa", title: "top", position: pTop, version: 3 }),
    makeTask({ id: "bbbbbbbb-bbbb-7bbb-8bbb-bbbbbbbbbbbb", title: "mid", position: pMid, version: 5 }),
    makeTask({ id: "cccccccc-cccc-7ccc-8ccc-cccccccccccc", title: "tail", position: p0, version: 7 }),
  ];
}

describe("TaskMutationError — structured error carrying the machine-readable errorCode", () => {
  it("is an Error subclass that preserves errorCode while message stays the friendly text", () => {
    const err = new TaskMutationError("version_conflict", "This item was changed elsewhere.");
    expect(err).toBeInstanceOf(Error);
    expect(err.errorCode).toBe("version_conflict");
    expect(err.message).toBe("This item was changed elsewhere.");
  });
});

describe("renameTaskMutationOptions — optimistic rename recipe + once-only 409 reapply", () => {
  it("onMutate cancels in-flight ['tasks'] queries before touching the cache", async () => {
    const seed = seedThree();
    const queryClient = primedClient(seed);
    const cancelSpy = vi.spyOn(queryClient, "cancelQueries");

    const options = renameTaskMutationOptions(queryClient);
    const variables: RenameTaskVariables = { id: seed[1]!.id, title: "renamed", version: seed[1]!.version };
    await options.onMutate?.(variables);

    expect(cancelSpy).toHaveBeenCalledWith({ queryKey: TASKS_KEY });
  });

  it("onMutate snapshots ['tasks'] and re-stamps the typed title on the target row in place", async () => {
    const seed = seedThree();
    const queryClient = primedClient(seed);
    const variables: RenameTaskVariables = { id: seed[1]!.id, title: "renamed mid", version: seed[1]!.version };

    const options = renameTaskMutationOptions(queryClient);
    const context = (await options.onMutate?.(variables)) as RenameTaskContext;

    expect(context.previousTasks).toEqual(seed);
    const next = queryClient.getQueryData<TaskResponse[]>(TASKS_KEY);
    // Title re-stamped on the target; siblings + ordering untouched.
    expect(next?.[1]?.title).toBe("renamed mid");
    expect(next?.[0]).toEqual(seed[0]);
    expect(next?.[2]).toEqual(seed[2]);
  });

  it("onError restores the snapshot in place on a non-conflict (network) error — no reapply", async () => {
    const seed = seedThree();
    const queryClient = primedClient(seed);
    const variables: RenameTaskVariables = { id: seed[1]!.id, title: "renamed", version: seed[1]!.version };

    const options = renameTaskMutationOptions(queryClient);
    const context = (await options.onMutate?.(variables)) as RenameTaskContext;

    await options.onError?.(networkError(), variables, context);

    expect(queryClient.getQueryData<TaskResponse[]>(TASKS_KEY)).toEqual(seed);
    expect(patchSpy).not.toHaveBeenCalled();
  });

  it("onError on 409 refetches then re-issues the typed title ONCE against the refetched version", async () => {
    const seed = seedThree();
    const queryClient = primedClient(seed);
    const variables: RenameTaskVariables = { id: seed[1]!.id, title: "the typed title", version: 5 };

    // Server truth after the refetch: the row's version moved on (was 5, now 9).
    const refetched = seed.map((t) => (t.id === seed[1]!.id ? makeTask({ ...t, version: 9 }) : t));
    const refetchSpy = vi
      .spyOn(queryClient, "refetchQueries")
      .mockImplementation(async () => {
        queryClient.setQueryData<TaskResponse[]>(TASKS_KEY, refetched);
      });
    // The single reapply PATCH succeeds.
    patchSpy.mockResolvedValue({ data: makeTask({ ...refetched[1]!, title: "the typed title", version: 10 }), error: undefined });

    const options = renameTaskMutationOptions(queryClient);
    const context = (await options.onMutate?.(variables)) as RenameTaskContext;
    await options.onError?.(versionConflict(), variables, context);

    expect(refetchSpy).toHaveBeenCalledWith({ queryKey: TASKS_KEY });
    // Re-issued exactly once, re-stamping the typed title with the FRESH version (9), not the stale 5.
    expect(patchSpy).toHaveBeenCalledTimes(1);
    const [path, init] = patchSpy.mock.calls[0]!;
    expect(path).toBe("/api/tasks/{id}/title");
    expect((init as { params: { path: { id: string } } }).params.path.id).toBe(seed[1]!.id);
    expect((init as { body: { title: string; version: number } }).body).toEqual({ title: "the typed title", version: 9 });
  });

  it("caps reapply at one: a repeat 409 on the reapply stops without looping (no livelock)", async () => {
    const seed = seedThree();
    const queryClient = primedClient(seed);
    const variables: RenameTaskVariables = { id: seed[1]!.id, title: "x", version: 5 };

    vi.spyOn(queryClient, "refetchQueries").mockImplementation(async () => {
      queryClient.setQueryData<TaskResponse[]>(
        TASKS_KEY,
        seed.map((t) => (t.id === seed[1]!.id ? makeTask({ ...t, version: 9 }) : t)),
      );
    });
    // The reapply itself conflicts again — the recipe must NOT loop.
    patchSpy.mockResolvedValue({ data: undefined, error: { errorCode: "version_conflict" } });

    const options = renameTaskMutationOptions(queryClient);
    const context = (await options.onMutate?.(variables)) as RenameTaskContext;
    await options.onError?.(versionConflict(), variables, context);

    expect(patchSpy).toHaveBeenCalledTimes(1);
  });

  it("onSettled invalidates ['tasks'] to reconcile with server truth", async () => {
    const seed = seedThree();
    const queryClient = primedClient(seed);
    const variables: RenameTaskVariables = { id: seed[1]!.id, title: "renamed", version: seed[1]!.version };
    const invalidateSpy = vi.spyOn(queryClient, "invalidateQueries");

    const options = renameTaskMutationOptions(queryClient);
    const context = (await options.onMutate?.(variables)) as RenameTaskContext;
    await options.onSettled?.(makeTask({ ...seed[1]!, title: "renamed" }), null, variables, context);

    expect(invalidateSpy).toHaveBeenCalledWith({ queryKey: TASKS_KEY });
  });
});

describe("toggleDoneMutationOptions — optimistic setDone recipe + idempotent 409 reapply", () => {
  it("onMutate optimistically flips the target row's status in place and snapshots the rest", async () => {
    const seed = seedThree();
    const queryClient = primedClient(seed);
    const variables: ToggleDoneVariables = { id: seed[0]!.id, status: "done", version: seed[0]!.version };

    const options = toggleDoneMutationOptions(queryClient);
    const context = (await options.onMutate?.(variables)) as ToggleDoneContext;

    expect(context.previousTasks).toEqual(seed);
    const next = queryClient.getQueryData<TaskResponse[]>(TASKS_KEY);
    expect(next?.[0]?.status).toBe("done");
    expect(next?.[1]).toEqual(seed[1]);
  });

  it("onError restores the snapshot in place on a non-conflict error — no reapply", async () => {
    const seed = seedThree();
    const queryClient = primedClient(seed);
    const variables: ToggleDoneVariables = { id: seed[0]!.id, status: "done", version: seed[0]!.version };

    const options = toggleDoneMutationOptions(queryClient);
    const context = (await options.onMutate?.(variables)) as ToggleDoneContext;
    await options.onError?.(networkError(), variables, context);

    expect(queryClient.getQueryData<TaskResponse[]>(TASKS_KEY)).toEqual(seed);
    expect(patchSpy).not.toHaveBeenCalled();
  });

  it("onError on 409 NO-OPs when the refetched status already reflects the desired state", async () => {
    const seed = seedThree();
    const queryClient = primedClient(seed);
    // Intent: drive row to "done".
    const variables: ToggleDoneVariables = { id: seed[0]!.id, status: "done", version: seed[0]!.version };

    // Server truth after refetch: the row is ALREADY done (someone else toggled it).
    vi.spyOn(queryClient, "refetchQueries").mockImplementation(async () => {
      queryClient.setQueryData<TaskResponse[]>(
        TASKS_KEY,
        seed.map((t) => (t.id === seed[0]!.id ? makeTask({ ...t, status: "done", version: 99 }) : t)),
      );
    });

    const options = toggleDoneMutationOptions(queryClient);
    const context = (await options.onMutate?.(variables)) as ToggleDoneContext;
    await options.onError?.(versionConflict(), variables, context);

    // Already in the desired state → no retry write.
    expect(patchSpy).not.toHaveBeenCalled();
  });

  it("onError on 409 re-issues ONCE with the fresh version when the server still differs", async () => {
    const seed = seedThree();
    const queryClient = primedClient(seed);
    const variables: ToggleDoneVariables = { id: seed[0]!.id, status: "done", version: seed[0]!.version };

    // Server truth after refetch: still "backlog" (so the intent is unmet), version moved to 42.
    vi.spyOn(queryClient, "refetchQueries").mockImplementation(async () => {
      queryClient.setQueryData<TaskResponse[]>(
        TASKS_KEY,
        seed.map((t) => (t.id === seed[0]!.id ? makeTask({ ...t, status: "backlog", version: 42 }) : t)),
      );
    });
    patchSpy.mockResolvedValue({ data: makeTask({ ...seed[0]!, status: "done", version: 43 }), error: undefined });

    const options = toggleDoneMutationOptions(queryClient);
    const context = (await options.onMutate?.(variables)) as ToggleDoneContext;
    await options.onError?.(versionConflict(), variables, context);

    expect(patchSpy).toHaveBeenCalledTimes(1);
    const [path, init] = patchSpy.mock.calls[0]!;
    expect(path).toBe("/api/tasks/{id}/status");
    expect((init as { body: { status: string; version: number } }).body).toEqual({ status: "done", version: 42 });
  });

  it("onSettled invalidates ['tasks'] to reconcile with server truth", async () => {
    const seed = seedThree();
    const queryClient = primedClient(seed);
    const variables: ToggleDoneVariables = { id: seed[0]!.id, status: "done", version: seed[0]!.version };
    const invalidateSpy = vi.spyOn(queryClient, "invalidateQueries");

    const options = toggleDoneMutationOptions(queryClient);
    const context = (await options.onMutate?.(variables)) as ToggleDoneContext;
    await options.onSettled?.(makeTask({ ...seed[0]!, status: "done" }), null, variables, context);

    expect(invalidateSpy).toHaveBeenCalledWith({ queryKey: TASKS_KEY });
  });
});

describe("reorderTaskMutationOptions — optimistic reorder recipe + intent-based 409 reapply", () => {
  it("onMutate optimistically re-ranks the row to the carried position and snapshots the ordering", async () => {
    const seed = seedThree();
    const queryClient = primedClient(seed);
    // Intent: move the tail row (index 2) to sit between top (index 0) and mid (index 1).
    const above = seed[0]!;
    const below = seed[1]!;
    const variables: ReorderTaskVariables = {
      id: seed[2]!.id,
      position: between(above.position, below.position),
      aboveId: above.id,
      belowId: below.id,
      version: seed[2]!.version,
    };

    const options = reorderTaskMutationOptions(queryClient);
    const context = (await options.onMutate?.(variables)) as ReorderTaskContext;

    expect(context.previousTasks).toEqual(seed);
    const next = queryClient.getQueryData<TaskResponse[]>(TASKS_KEY);
    const moved = next?.find((t) => t.id === seed[2]!.id);
    expect(moved?.position).toBe(variables.position);
    // Strictly between the two neighbours' ranks (top-is-lowest).
    expect(moved!.position > above.position && moved!.position < below.position).toBe(true);
    // The row must actually RELOCATE in array order (the list renders in array order, not
    // sorted-on-read), so moving the old tail between top+mid lands it at index 1 — an impl
    // that only rewrites `position` in place and leaves the row stuck at index 2 must fail.
    expect(next?.map((t) => t.id)).toEqual([seed[0]!.id, seed[2]!.id, seed[1]!.id]);
    // And the cache stays ascending by position (the invariant the server orders on).
    const positions = next!.map((t) => t.position);
    expect([...positions].sort()).toEqual(positions);
  });

  it("onError restores the snapshot in place on a non-conflict error — no reapply", async () => {
    const seed = seedThree();
    const queryClient = primedClient(seed);
    const variables: ReorderTaskVariables = {
      id: seed[2]!.id,
      position: between(seed[0]!.position, seed[1]!.position),
      aboveId: seed[0]!.id,
      belowId: seed[1]!.id,
      version: seed[2]!.version,
    };

    const options = reorderTaskMutationOptions(queryClient);
    const context = (await options.onMutate?.(variables)) as ReorderTaskContext;
    await options.onError?.(networkError(), variables, context);

    expect(queryClient.getQueryData<TaskResponse[]>(TASKS_KEY)).toEqual(seed);
    expect(patchSpy).not.toHaveBeenCalled();
  });

  it("onError on 409 RECOMPUTES between() from the FRESH neighbour ranks (never re-sends the stale rank)", async () => {
    const seed = seedThree();
    const queryClient = primedClient(seed);
    const above = seed[0]!;
    const below = seed[1]!;
    const stalePosition = between(above.position, below.position);
    const variables: ReorderTaskVariables = {
      id: seed[2]!.id,
      position: stalePosition,
      aboveId: above.id,
      belowId: below.id,
      version: seed[2]!.version,
    };

    // Server truth after refetch: BOTH neighbours moved to brand-new ranks, target's version bumped.
    const freshAbove = makeTask({ ...above, position: between(null, null) });
    const freshBelow = makeTask({ ...below, position: between(freshAbove.position, null) });
    const freshTarget = makeTask({ ...seed[2]!, version: 71, position: between(freshBelow.position, null) });
    const expectedFreshPosition = between(freshAbove.position, freshBelow.position);
    vi.spyOn(queryClient, "refetchQueries").mockImplementation(async () => {
      queryClient.setQueryData<TaskResponse[]>(TASKS_KEY, [freshAbove, freshBelow, freshTarget]);
    });
    patchSpy.mockResolvedValue({ data: makeTask({ ...freshTarget, position: expectedFreshPosition, version: 72 }), error: undefined });

    const options = reorderTaskMutationOptions(queryClient);
    const context = (await options.onMutate?.(variables)) as ReorderTaskContext;
    await options.onError?.(versionConflict(), variables, context);

    expect(patchSpy).toHaveBeenCalledTimes(1);
    const [path, init] = patchSpy.mock.calls[0]!;
    expect(path).toBe("/api/tasks/{id}/position");
    const body = (init as { body: { position: string; version: number } }).body;
    // Recomputed from FRESH neighbours and the FRESH version — and demonstrably NOT the stale rank.
    expect(body.version).toBe(71);
    expect(body.position).toBe(expectedFreshPosition);
    expect(body.position).not.toBe(stalePosition);
  });

  it("drops the move on 409 if the row was concurrently deleted (no retry write)", async () => {
    const seed = seedThree();
    const queryClient = primedClient(seed);
    const variables: ReorderTaskVariables = {
      id: seed[2]!.id,
      position: between(seed[0]!.position, seed[1]!.position),
      aboveId: seed[0]!.id,
      belowId: seed[1]!.id,
      version: seed[2]!.version,
    };

    // Server truth after refetch: the moved row is gone (concurrent delete).
    vi.spyOn(queryClient, "refetchQueries").mockImplementation(async () => {
      queryClient.setQueryData<TaskResponse[]>(TASKS_KEY, [seed[0]!, seed[1]!]);
    });

    const options = reorderTaskMutationOptions(queryClient);
    const context = (await options.onMutate?.(variables)) as ReorderTaskContext;
    await options.onError?.(versionConflict(), variables, context);

    expect(patchSpy).not.toHaveBeenCalled();
  });

  it("onSettled invalidates ['tasks'] to reconcile with server truth", async () => {
    const seed = seedThree();
    const queryClient = primedClient(seed);
    const variables: ReorderTaskVariables = {
      id: seed[2]!.id,
      position: between(seed[0]!.position, seed[1]!.position),
      aboveId: seed[0]!.id,
      belowId: seed[1]!.id,
      version: seed[2]!.version,
    };
    const invalidateSpy = vi.spyOn(queryClient, "invalidateQueries");

    const options = reorderTaskMutationOptions(queryClient);
    const context = (await options.onMutate?.(variables)) as ReorderTaskContext;
    await options.onSettled?.(makeTask({ ...seed[2]!, position: variables.position }), null, variables, context);

    expect(invalidateSpy).toHaveBeenCalledWith({ queryKey: TASKS_KEY });
  });
});

describe("deleteTaskMutationOptions — optimistic remove recipe (version-free, no 409 path)", () => {
  it("onMutate optimistically REMOVES the target row and snapshots the full ordering", async () => {
    const seed = seedThree();
    const queryClient = primedClient(seed);
    const variables: DeleteTaskVariables = { id: seed[1]!.id };

    const options = deleteTaskMutationOptions(queryClient);
    const context = (await options.onMutate?.(variables)) as DeleteTaskContext;

    expect(context.previousTasks).toEqual(seed);
    const next = queryClient.getQueryData<TaskResponse[]>(TASKS_KEY);
    expect(next).toHaveLength(seed.length - 1);
    expect(next?.some((t) => t.id === seed[1]!.id)).toBe(false);
    // Surviving rows keep their relative order.
    expect(next).toEqual([seed[0], seed[2]]);
  });

  it("onError REAPPEARS the rolled-back row at its ORIGINAL position (snapshot restores ordering)", async () => {
    const seed = seedThree();
    const queryClient = primedClient(seed);
    const variables: DeleteTaskVariables = { id: seed[1]!.id };

    const options = deleteTaskMutationOptions(queryClient);
    const context = (await options.onMutate?.(variables)) as DeleteTaskContext;

    // Sanity: the row was optimistically removed.
    expect(queryClient.getQueryData<TaskResponse[]>(TASKS_KEY)).toHaveLength(seed.length - 1);

    await options.onError?.(networkError(), variables, context);

    const restored = queryClient.getQueryData<TaskResponse[]>(TASKS_KEY);
    expect(restored).toEqual(seed);
    // The deleted middle row reappears at its original index 1 — not at the head/tail.
    expect(restored?.[1]?.id).toBe(seed[1]!.id);
  });

  it("onSettled invalidates ['tasks'] to reconcile with server truth", async () => {
    const seed = seedThree();
    const queryClient = primedClient(seed);
    const variables: DeleteTaskVariables = { id: seed[1]!.id };
    const invalidateSpy = vi.spyOn(queryClient, "invalidateQueries");

    const options = deleteTaskMutationOptions(queryClient);
    const context = (await options.onMutate?.(variables)) as DeleteTaskContext;
    await options.onSettled?.(undefined, null, variables, context);

    expect(invalidateSpy).toHaveBeenCalledWith({ queryKey: TASKS_KEY });
  });
});

/**
 * NEW (T038, RED — drives T039; US2; FR-021/AS-05, research R6/R7/R15/R16).
 *
 * Pins the optimistic MOVE-TO-PROJECT recipe `moveTaskToProjectMutationOptions(queryClient)`,
 * mirroring the create/edit factory altitude (pure TanStack-Query options factory; no React
 * render). A task lives in exactly ONE list cache: the Inbox is the SINGLE `['tasks']` key
 * (project_id IS NULL, R6); a project's task list is `['projects', <projectId>, 'tasks']`
 * (R16, the natural extension of the `['projects']` namespace). Moving a task therefore
 * RELOCATES the row between two caches — pull from the source, re-stamp `projectId`, insert
 * into the target — and `projectId = null` moves it back to the Inbox.
 *
 * Both ends ride on `variables` (`fromProjectId`/`toProjectId`, each `string | null`), so the
 * factory is self-contained — the source/target keys are derived inside the recipe, never via
 * a wrapper-side lookup (the wrapper is not tested here, matching every other recipe).
 *
 * There is NO once-only 409 reapply (that was a task-edit-specific recipe, R10) — a 409/422/
 * network error here is a PLAIN rollback of BOTH caches + the FR-049 message surfaced through
 * the global announcer, with the authoritative state reconciled by the `onSettled` invalidate
 * of the two specific keys (never the bare `['projects']` prefix, which would also evict the
 * sidebar list).
 */
const PROJECT_A = "33333333-3333-7333-8333-333333333333";
const PROJECT_B = "44444444-4444-7444-8444-444444444444";

/** The task-list cache key for a project, or the Inbox `['tasks']` key when `projectId` is null. */
function listKeyFor(projectId: string | null): readonly unknown[] {
  return projectId === null ? TASKS_KEY : (["projects", projectId, "tasks"] as const);
}

describe("moveTaskToProjectMutationOptions — optimistic cross-cache move recipe (no 409 reapply)", () => {
  it("TaskMutationError still carries errorCode so the global announcer surfaces the friendly text", () => {
    // The move recipe throws the structured error like the other versioned writes.
    const err = new TaskMutationError("version_conflict", "This item was changed elsewhere.");
    expect(err).toBeInstanceOf(Error);
    expect(err.errorCode).toBe("version_conflict");
  });

  it("mutationFn PATCHes /api/tasks/{id}/project with { projectId, version }", async () => {
    const seed = seedThree();
    const queryClient = primedClient(seed);
    const variables: MoveTaskToProjectVariables = {
      id: seed[0]!.id,
      fromProjectId: null,
      toProjectId: PROJECT_A,
      version: seed[0]!.version,
    };
    patchSpy.mockResolvedValue({
      data: makeTask({ ...seed[0]!, projectId: PROJECT_A, version: seed[0]!.version + 1 }),
      error: undefined,
    });

    const options = moveTaskToProjectMutationOptions(queryClient);
    await options.mutationFn(variables);

    expect(patchSpy).toHaveBeenCalledTimes(1);
    const [path, init] = patchSpy.mock.calls[0]!;
    expect(path).toBe("/api/tasks/{id}/project");
    expect((init as { params: { path: { id: string } } }).params.path.id).toBe(seed[0]!.id);
    expect((init as { body: { projectId: string | null; version: number } }).body).toEqual({
      projectId: PROJECT_A,
      version: seed[0]!.version,
    });
  });

  it("onMutate cancels BOTH the source and the target list caches before touching them", async () => {
    const seed = seedThree();
    const queryClient = primedClient(seed);
    const cancelSpy = vi.spyOn(queryClient, "cancelQueries");

    const options = moveTaskToProjectMutationOptions(queryClient);
    const variables: MoveTaskToProjectVariables = {
      id: seed[0]!.id,
      fromProjectId: null,
      toProjectId: PROJECT_A,
      version: seed[0]!.version,
    };
    await options.onMutate?.(variables);

    expect(cancelSpy).toHaveBeenCalledWith({ queryKey: TASKS_KEY });
    expect(cancelSpy).toHaveBeenCalledWith({ queryKey: listKeyFor(PROJECT_A) });
  });

  it("onMutate REMOVES the row from the Inbox and INSERTS it (projectId re-stamped) into the target project cache", async () => {
    const seed = seedThree();
    const queryClient = primedClient(seed);
    queryClient.setQueryData(listKeyFor(PROJECT_A), []); // target project list starts empty
    const moved = seed[1]!;
    const variables: MoveTaskToProjectVariables = {
      id: moved.id,
      fromProjectId: null,
      toProjectId: PROJECT_A,
      version: moved.version,
    };

    const options = moveTaskToProjectMutationOptions(queryClient);
    await options.onMutate?.(variables);

    // Gone from the Inbox.
    const inbox = queryClient.getQueryData<TaskResponse[]>(TASKS_KEY);
    expect(inbox?.some((t) => t.id === moved.id)).toBe(false);
    expect(inbox).toHaveLength(seed.length - 1);

    // Present in the project cache, with projectId re-stamped to the target.
    const projectList = queryClient.getQueryData<TaskResponse[]>(listKeyFor(PROJECT_A));
    const inserted = projectList?.find((t) => t.id === moved.id);
    expect(inserted).toBeDefined();
    expect(inserted?.projectId).toBe(PROJECT_A);
  });

  it("onMutate moves a projected task back to the Inbox when toProjectId is null (projectId cleared)", async () => {
    const seed = seedThree();
    const queryClient = primedClient([]); // Inbox empty
    const projected = makeTask({ id: seed[0]!.id, position: seed[0]!.position, projectId: PROJECT_A });
    queryClient.setQueryData(listKeyFor(PROJECT_A), [projected]);
    const variables: MoveTaskToProjectVariables = {
      id: projected.id,
      fromProjectId: PROJECT_A,
      toProjectId: null,
      version: projected.version,
    };

    const options = moveTaskToProjectMutationOptions(queryClient);
    await options.onMutate?.(variables);

    // Gone from the project list, back in the Inbox with projectId cleared.
    expect(queryClient.getQueryData<TaskResponse[]>(listKeyFor(PROJECT_A))).toHaveLength(0);
    const inbox = queryClient.getQueryData<TaskResponse[]>(TASKS_KEY);
    const back = inbox?.find((t) => t.id === projected.id);
    expect(back).toBeDefined();
    expect(back?.projectId).toBeNull();
  });

  it("onError restores BOTH list caches in place (the moved row reappears in its source)", async () => {
    const seed = seedThree();
    const queryClient = primedClient(seed);
    const targetSeed: TaskResponse[] = [];
    queryClient.setQueryData(listKeyFor(PROJECT_A), targetSeed);
    const moved = seed[1]!;
    const variables: MoveTaskToProjectVariables = {
      id: moved.id,
      fromProjectId: null,
      toProjectId: PROJECT_A,
      version: moved.version,
    };

    const options = moveTaskToProjectMutationOptions(queryClient);
    const context = (await options.onMutate?.(variables)) as MoveTaskToProjectContext;
    // Sanity: the optimistic move happened.
    expect(queryClient.getQueryData<TaskResponse[]>(TASKS_KEY)).toHaveLength(seed.length - 1);

    await options.onError?.(new TaskMutationError("version_conflict", "stale"), variables, context);

    // Both caches back to their pre-move snapshots.
    expect(queryClient.getQueryData<TaskResponse[]>(TASKS_KEY)).toEqual(seed);
    expect(queryClient.getQueryData<TaskResponse[]>(listKeyFor(PROJECT_A))).toEqual(targetSeed);
    // No reapply: a single failed PATCH is a plain rollback.
    expect(patchSpy).not.toHaveBeenCalled();
  });

  it("onSettled invalidates the two SPECIFIC list keys (never the bare ['projects'] prefix)", async () => {
    const seed = seedThree();
    const queryClient = primedClient(seed);
    queryClient.setQueryData(listKeyFor(PROJECT_B), []);
    const moved = seed[0]!;
    const variables: MoveTaskToProjectVariables = {
      id: moved.id,
      fromProjectId: null,
      toProjectId: PROJECT_B,
      version: moved.version,
    };
    const invalidateSpy = vi.spyOn(queryClient, "invalidateQueries");

    const options = moveTaskToProjectMutationOptions(queryClient);
    const context = (await options.onMutate?.(variables)) as MoveTaskToProjectContext;
    await options.onSettled?.(
      makeTask({ ...moved, projectId: PROJECT_B }),
      null,
      variables,
      context,
    );

    expect(invalidateSpy).toHaveBeenCalledWith({ queryKey: TASKS_KEY });
    expect(invalidateSpy).toHaveBeenCalledWith({ queryKey: listKeyFor(PROJECT_B) });
    // The bare ['projects'] prefix is NEVER invalidated (it prefix-matches the sidebar list).
    const invalidatedKeys = invalidateSpy.mock.calls.map((c) => JSON.stringify(c[0]?.queryKey));
    expect(invalidatedKeys).not.toContain(JSON.stringify(["projects"]));
  });
});

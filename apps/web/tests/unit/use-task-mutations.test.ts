// @vitest-environment node
import { QueryClient } from "@tanstack/react-query";
import { describe, expect, it, vi } from "vitest";
import type { components } from "@/lib/api/generated/schema";
import { between } from "@/lib/position";
import {
  type CreateTaskContext,
  type CreateTaskVariables,
  createTaskMutationOptions,
} from "@/hooks/useTaskMutations";

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

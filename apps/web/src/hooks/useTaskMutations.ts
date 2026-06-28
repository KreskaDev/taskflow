"use client";

import { type QueryClient, useMutation, useQueryClient } from "@tanstack/react-query";

import { apiClient, mapError, type ProblemDetails } from "@/lib/api/client";
import type { components } from "@/lib/api/generated/schema";
import { buildTodayGroups, buildUpcomingGroups, flattenToday, flattenUpcoming } from "@/lib/dailyViews";
import { newTaskId } from "@/lib/id";
import { between } from "@/lib/position";
import { createTaskSchema, editTaskSchema, type Priority, taskTitleSchema } from "@/lib/validation/task";
import { TASKS_QUERY_KEY } from "@/hooks/useTasks";
import { TODAY_QUERY_KEY } from "@/hooks/useTodayTasks";
import { UPCOMING_QUERY_KEY } from "@/hooks/useUpcomingTasks";

type TaskResponse = components["schemas"]["TaskResponse"];
type TodayResponse = components["schemas"]["TodayResponse"];
type UpcomingResponse = components["schemas"]["UpcomingResponse"];

/**
 * The triage-view cache snapshot (slice 005, R7): the Inbox flat list + the grouped Today/Upcoming caches.
 * Captured before an optimistic mutation so any of them can be rolled back on error.
 */
interface ViewCachesSnapshot {
  previousTasks: TaskResponse[] | undefined;
  previousToday: TodayResponse | undefined;
  previousUpcoming: UpcomingResponse | undefined;
}

/** Snapshots the Inbox + Today + Upcoming caches (for rollback). */
function snapshotViewCaches(queryClient: QueryClient): ViewCachesSnapshot {
  return {
    previousTasks: queryClient.getQueryData<TaskResponse[]>(TASKS_QUERY_KEY),
    previousToday: queryClient.getQueryData<TodayResponse>(TODAY_QUERY_KEY),
    previousUpcoming: queryClient.getQueryData<UpcomingResponse>(UPCOMING_QUERY_KEY),
  };
}

/**
 * Optimistically applies an updated task across the triage caches using the client membership recompute
 * (R7, FR-092): the Today and Upcoming grouped caches are flattened, the task is upserted, and the groups
 * are rebuilt with the SAME Warsaw boundary + R5 order the server uses — so a reschedule-to-tomorrow drops
 * the row out of Today (into Upcoming), a toggle-done drops it from both, and a priority change re-sorts it.
 * The Inbox flat list is updated in place (a dated Inbox task lives in both `['tasks']` and the views).
 */
function applyTaskToViewCaches(queryClient: QueryClient, updated: TaskResponse, now: Date): void {
  queryClient.setQueryData<TaskResponse[]>(TASKS_QUERY_KEY, (old) =>
    old?.map((t) => (t.id === updated.id ? updated : t)),
  );

  // A projected task may also live in its project board cache (slice 010 surface) — update it in place.
  if (updated.projectId != null) {
    queryClient.setQueryData<TaskResponse[]>(projectTasksQueryKey(updated.projectId), (old) =>
      old?.map((t) => (t.id === updated.id ? updated : t)),
    );
  }

  queryClient.setQueryData<TodayResponse>(TODAY_QUERY_KEY, (old) => {
    if (old === undefined) return old;
    const flat = flattenToday(old).filter((t) => t.id !== updated.id);
    flat.push(updated);
    return buildTodayGroups(flat, now);
  });

  queryClient.setQueryData<UpcomingResponse>(UPCOMING_QUERY_KEY, (old) => {
    if (old === undefined) return old;
    const flat = flattenUpcoming(old).filter((t) => t.id !== updated.id);
    flat.push(updated);
    return buildUpcomingGroups(flat, now);
  });
}

/** Rolls the triage caches back to a pre-mutation snapshot (on error). */
function rollbackViewCaches(queryClient: QueryClient, snapshot: ViewCachesSnapshot): void {
  queryClient.setQueryData<TaskResponse[] | undefined>(TASKS_QUERY_KEY, snapshot.previousTasks);
  queryClient.setQueryData<TodayResponse | undefined>(TODAY_QUERY_KEY, snapshot.previousToday);
  queryClient.setQueryData<UpcomingResponse | undefined>(UPCOMING_QUERY_KEY, snapshot.previousUpcoming);
}

/** Reconciles the triage caches with server truth (on settle). */
async function settleViewCaches(queryClient: QueryClient): Promise<void> {
  await queryClient.invalidateQueries({ queryKey: TASKS_QUERY_KEY });
  await queryClient.invalidateQueries({ queryKey: TODAY_QUERY_KEY });
  await queryClient.invalidateQueries({ queryKey: UPCOMING_QUERY_KEY });
}

/** Finds a task row across the Inbox + Today + Upcoming caches (the source for an optimistic edit). */
function findTaskInViewCaches(queryClient: QueryClient, id: string): TaskResponse | undefined {
  const inbox = queryClient.getQueryData<TaskResponse[]>(TASKS_QUERY_KEY)?.find((t) => t.id === id);
  if (inbox) return inbox;
  const today = flattenToday(queryClient.getQueryData<TodayResponse>(TODAY_QUERY_KEY)).find((t) => t.id === id);
  if (today) return today;
  return flattenUpcoming(queryClient.getQueryData<UpcomingResponse>(UPCOMING_QUERY_KEY)).find((t) => t.id === id);
}

/**
 * Structured mutation error carrying the machine-readable `errorCode` alongside the
 * FR-049 friendly `message` (T057). The versioned recipes (rename/toggle/reorder) throw
 * this from their `mutationFn` so `onError` can branch on `version_conflict` to drive the
 * once-only intent-reapply, while the global `MutationCache` announcer still surfaces
 * `error.message` verbatim. A plain `Error` (which the CREATE recipe throws) would erase
 * `errorCode` and make the 409 path undetectable.
 */
export class TaskMutationError extends Error {
  readonly errorCode: string;

  constructor(errorCode: string, message: string) {
    super(message);
    this.name = "TaskMutationError";
    this.errorCode = errorCode;
  }
}

/** True when the error is a structured 409 version conflict (the intent-reapply trigger). */
function isVersionConflict(error: unknown): boolean {
  return error instanceof TaskMutationError && error.errorCode === "version_conflict";
}

/** Sorts a task list ascending by `position` under code-unit ordering (the cache invariant). */
function sortByPosition(tasks: TaskResponse[]): TaskResponse[] {
  return [...tasks].sort((a, b) => (a.position < b.position ? -1 : a.position > b.position ? 1 : 0));
}

/**
 * Optimistic CREATE recipe (T037; FR-001, SC-003, research R10).
 *
 * The client-minted id (UUIDv7) and the newest-first fractional rank ride on the
 * mutation `variables` rather than being generated inside `onMutate`: TanStack v5
 * passes the `onMutate` return value as context to `onError`/`onSettled` but NEVER
 * to `mutationFn`. The idempotent `PUT /api/tasks/{id}` carries `{ title, position }`,
 * so a single stable identity must already exist on `variables` to reach both the
 * optimistic cache write and the server call.
 */
export interface CreateTaskVariables {
  id: string;
  title: string;
  position: string;
  /** Resolved due-date instant as an ISO string (wire shape), or absent for a dateless task. */
  dueDate?: string;
  /** Whether the due date carries a wall-clock time (R8 pairing) — paired with `dueDate`. */
  dueHasTime?: boolean;
}

/** Context handed from `onMutate` to `onError`/`onSettled` — the pre-mutation snapshot. */
export interface CreateTaskContext {
  previousTasks: TaskResponse[] | undefined;
}

/**
 * The optimistic-create recipe shape consumed by `useMutation`. The callback arities
 * are the canonical TanStack-v5 lifecycle ones — `useMutation`'s wider 5.101 callback
 * signatures accept these (TS drops trailing params), and the `context` param lines up
 * positionally with the library's `onMutateResult`, so no cast is needed.
 */
interface OptimisticCreateOptions {
  mutationFn: (variables: CreateTaskVariables) => Promise<TaskResponse>;
  onMutate: (variables: CreateTaskVariables) => Promise<CreateTaskContext>;
  onError: (error: Error, variables: CreateTaskVariables, context: CreateTaskContext | undefined) => void;
  onSettled: (
    data: TaskResponse | undefined,
    error: Error | null,
    variables: CreateTaskVariables,
    context: CreateTaskContext | undefined,
  ) => Promise<void>;
}

/**
 * Pure TanStack-Query options factory implementing the optimistic
 * cancel → snapshot → optimistic-prepend → rollback → invalidate recipe against the
 * single `['tasks']` key. The paint is synchronous/optimistic (Constitution III).
 */
export function createTaskMutationOptions(queryClient: QueryClient): OptimisticCreateOptions {
  return {
    mutationFn: async ({ id, title, position, dueDate, dueHasTime }: CreateTaskVariables): Promise<TaskResponse> => {
      const { data, error } = await apiClient.PUT("/api/tasks/{id}", {
        params: { path: { id } },
        body: { title, position, dueDate, dueHasTime },
      });
      if (error || !data) {
        const errorCode = (error as ProblemDetails | undefined)?.errorCode;
        throw new Error(mapError(errorCode).message);
      }
      return data;
    },

    onMutate: async (variables: CreateTaskVariables): Promise<CreateTaskContext> => {
      // Stop in-flight refetches so they can't clobber the optimistic write.
      await queryClient.cancelQueries({ queryKey: TASKS_QUERY_KEY });

      const previousTasks = queryClient.getQueryData<TaskResponse[]>(TASKS_QUERY_KEY);

      const now = new Date().toISOString();
      const optimisticTask: TaskResponse = {
        id: variables.id,
        title: variables.title,
        status: "backlog",
        position: variables.position,
        version: 0,
        createdAt: now,
        updatedAt: now,
        completedAt: null,
        dueDate: variables.dueDate ?? null,
        dueHasTime: variables.dueHasTime ?? null,
        assignees: [], // a freshly captured (Inbox) task has no assignees (slice 008)
      };

      // Newest-first: the rank already sorts before the current head, so prepend verbatim.
      queryClient.setQueryData<TaskResponse[]>(TASKS_QUERY_KEY, (old) => [
        optimisticTask,
        ...(old ?? []),
      ]);

      return { previousTasks };
    },

    onError: (
      _error: Error,
      _variables: CreateTaskVariables,
      context: CreateTaskContext | undefined,
    ): void => {
      // Restore the snapshot in place — the optimistic row disappears.
      queryClient.setQueryData<TaskResponse[] | undefined>(
        TASKS_QUERY_KEY,
        context?.previousTasks,
      );
    },

    onSettled: async (
      _data: TaskResponse | undefined,
      _error: Error | null,
      _variables: CreateTaskVariables,
      _context: CreateTaskContext | undefined,
    ): Promise<void> => {
      // Reconcile with server truth regardless of success or failure.
      await queryClient.invalidateQueries({ queryKey: TASKS_QUERY_KEY });
    },
  };
}

// ── Shared snapshot context for the four versioned/version-free edit recipes (T057). ──

/** Context handed from every edit recipe's `onMutate` to `onError`/`onSettled`. */
interface EditTaskContext {
  previousTasks: TaskResponse[] | undefined;
}

export type RenameTaskContext = EditTaskContext;
/** Toggle-done also recomputes the Today/Upcoming caches (a completed task leaves both), so it carries the full view snapshot (slice 005, R7). */
export type ToggleDoneContext = ViewCachesSnapshot;
export type ReorderTaskContext = EditTaskContext;
export type DeleteTaskContext = EditTaskContext;

/**
 * Refetches `['tasks']` and returns the fresh server row for `id`, or `undefined` if the
 * row was concurrently deleted. The shared first leg of every once-only 409 reapply: the
 * recipe re-reads server truth, then re-issues the user's INTENT against the FRESH state.
 */
async function refetchFreshRow(
  queryClient: QueryClient,
  id: string,
): Promise<TaskResponse | undefined> {
  await queryClient.refetchQueries({ queryKey: TASKS_QUERY_KEY });
  const fresh = queryClient.getQueryData<TaskResponse[]>(TASKS_QUERY_KEY);
  return fresh?.find((t) => t.id === id);
}

/* ───────────────────────────── RENAME (PATCH /title) ───────────────────────────── */

export interface RenameTaskVariables {
  id: string;
  title: string;
  version: number;
}

interface RenameTaskOptions {
  mutationFn: (variables: RenameTaskVariables) => Promise<TaskResponse>;
  onMutate: (variables: RenameTaskVariables) => Promise<RenameTaskContext>;
  onError: (
    error: Error,
    variables: RenameTaskVariables,
    context: RenameTaskContext | undefined,
  ) => Promise<void>;
  onSettled: (
    data: TaskResponse | undefined,
    error: Error | null,
    variables: RenameTaskVariables,
    context: RenameTaskContext | undefined,
  ) => Promise<void>;
}

function renameRequest(id: string, title: string, version: number) {
  return apiClient.PATCH("/api/tasks/{id}/title", {
    params: { path: { id } },
    body: { title, version },
  });
}

/**
 * Optimistic RENAME recipe (T057; FR-093, research R10). Re-stamps the typed title on the
 * target row in place against the single `['tasks']` key. On a `version_conflict` the recipe
 * refetches and re-issues the typed title ONCE against the FRESH version (capped — a repeat
 * 409 on the reapply stops without looping). A non-conflict error is a plain rollback.
 */
export function renameTaskMutationOptions(queryClient: QueryClient): RenameTaskOptions {
  return {
    mutationFn: async ({ id, title, version }: RenameTaskVariables): Promise<TaskResponse> => {
      const { data, error } = await renameRequest(id, title, version);
      if (error || !data) {
        const errorCode = (error as ProblemDetails | undefined)?.errorCode;
        throw new TaskMutationError(errorCode ?? "internal_error", mapError(errorCode).message);
      }
      return data;
    },

    onMutate: async (variables: RenameTaskVariables): Promise<RenameTaskContext> => {
      await queryClient.cancelQueries({ queryKey: TASKS_QUERY_KEY });
      const previousTasks = queryClient.getQueryData<TaskResponse[]>(TASKS_QUERY_KEY);

      queryClient.setQueryData<TaskResponse[]>(TASKS_QUERY_KEY, (old) =>
        (old ?? []).map((t) => (t.id === variables.id ? { ...t, title: variables.title } : t)),
      );

      return { previousTasks };
    },

    onError: async (
      error: Error,
      variables: RenameTaskVariables,
      context: RenameTaskContext | undefined,
    ): Promise<void> => {
      if (!isVersionConflict(error)) {
        queryClient.setQueryData<TaskResponse[] | undefined>(TASKS_QUERY_KEY, context?.previousTasks);
        return;
      }
      // Once-only reapply: re-read server truth, re-stamp the typed title on the FRESH version.
      const fresh = await refetchFreshRow(queryClient, variables.id);
      if (!fresh) return; // row gone (concurrent delete) → drop the rename.
      await renameRequest(variables.id, variables.title, fresh.version);
      // Capped structurally: no recursion, so a repeat 409 on the reapply cannot loop.
    },

    onSettled: async (data): Promise<void> => {
      // On success, write the server's returned row (with its FRESH bumped `version`) back into
      // the cache synchronously, so a rapid SECOND rename on the same row reads the current
      // version instead of the stale optimistic one — otherwise the next PATCH would 409 and
      // race the once-only reapply path (research R10; fixes sequential same-row renames).
      if (data) {
        queryClient.setQueryData<TaskResponse[]>(TASKS_QUERY_KEY, (old) =>
          (old ?? []).map((t) => (t.id === data.id ? data : t)),
        );
      }
      await queryClient.invalidateQueries({ queryKey: TASKS_QUERY_KEY });
    },
  };
}

/* ──────────────────────────── TOGGLE (PATCH /status) ──────────────────────────── */

export interface ToggleDoneVariables {
  id: string;
  status: string;
  version: number;
}

interface ToggleDoneOptions {
  mutationFn: (variables: ToggleDoneVariables) => Promise<TaskResponse>;
  onMutate: (variables: ToggleDoneVariables) => Promise<ToggleDoneContext>;
  onError: (
    error: Error,
    variables: ToggleDoneVariables,
    context: ToggleDoneContext | undefined,
  ) => Promise<void>;
  onSettled: (
    data: TaskResponse | undefined,
    error: Error | null,
    variables: ToggleDoneVariables,
    context: ToggleDoneContext | undefined,
  ) => Promise<void>;
}

function statusRequest(id: string, status: string, version: number) {
  return apiClient.PATCH("/api/tasks/{id}/status", {
    params: { path: { id } },
    body: { status, version },
  });
}

/**
 * Optimistic TOGGLE (setDone) recipe (T057; FR-097, research R10). Flips the target row's
 * status in place. On a `version_conflict` the recipe refetches and, if the server still
 * differs from the desired status, re-issues ONCE against the FRESH version; if the server
 * already reflects the desired state it NO-OPs (idempotent). Non-conflict → plain rollback.
 */
export function toggleDoneMutationOptions(queryClient: QueryClient): ToggleDoneOptions {
  return {
    mutationFn: async ({ id, status, version }: ToggleDoneVariables): Promise<TaskResponse> => {
      const { data, error } = await statusRequest(id, status, version);
      if (error || !data) {
        const errorCode = (error as ProblemDetails | undefined)?.errorCode;
        throw new TaskMutationError(errorCode ?? "internal_error", mapError(errorCode).message);
      }
      return data;
    },

    onMutate: async (variables: ToggleDoneVariables): Promise<ToggleDoneContext> => {
      // slice 005 (R7): toggle-done recomputes the Today/Upcoming caches too — a completed task leaves
      // BOTH triage views — alongside the slice-002 Inbox patch. Snapshot all three for rollback.
      await queryClient.cancelQueries({ queryKey: TASKS_QUERY_KEY });
      await queryClient.cancelQueries({ queryKey: TODAY_QUERY_KEY });
      await queryClient.cancelQueries({ queryKey: UPCOMING_QUERY_KEY });
      const snapshot = snapshotViewCaches(queryClient);

      const row = findTaskInViewCaches(queryClient, variables.id);
      if (row) {
        const done = variables.status === "done";
        applyTaskToViewCaches(
          queryClient,
          { ...row, status: variables.status, completedAt: done ? new Date().toISOString() : null },
          new Date(),
        );
      }

      return snapshot;
    },

    onError: async (
      error: Error,
      variables: ToggleDoneVariables,
      context: ToggleDoneContext | undefined,
    ): Promise<void> => {
      if (!isVersionConflict(error)) {
        if (context) rollbackViewCaches(queryClient, context);
        return;
      }
      const fresh = await refetchFreshRow(queryClient, variables.id);
      if (!fresh) return; // row gone → drop the toggle.
      if (fresh.status === variables.status) return; // already in the desired state → idempotent no-op.
      await statusRequest(variables.id, variables.status, fresh.version);
    },

    onSettled: async (data): Promise<void> => {
      // On success, write the server's returned row (with its FRESH bumped `version`) back into the
      // caches synchronously, so a rapid SECOND toggle on the same row reads the current version instead
      // of the stale optimistic one (research R10; fixes sequential same-row toggles). Then reconcile
      // all three triage caches with server truth.
      if (data) {
        queryClient.setQueryData<TaskResponse[]>(TASKS_QUERY_KEY, (old) =>
          (old ?? []).map((t) => (t.id === data.id ? data : t)),
        );
        applyTaskToViewCaches(queryClient, data, new Date());
      }
      await settleViewCaches(queryClient);
    },
  };
}

/* ─────────────────────────── REORDER (PATCH /position) ─────────────────────────── */

export interface ReorderTaskVariables {
  id: string;
  position: string;
  /** The id of the neighbour above the drop target, or `null` for the list head. */
  aboveId: string | null;
  /** The id of the neighbour below the drop target, or `null` for the list tail. */
  belowId: string | null;
  version: number;
}

interface ReorderTaskOptions {
  mutationFn: (variables: ReorderTaskVariables) => Promise<TaskResponse>;
  onMutate: (variables: ReorderTaskVariables) => Promise<ReorderTaskContext>;
  onError: (
    error: Error,
    variables: ReorderTaskVariables,
    context: ReorderTaskContext | undefined,
  ) => Promise<void>;
  onSettled: (
    data: TaskResponse | undefined,
    error: Error | null,
    variables: ReorderTaskVariables,
    context: ReorderTaskContext | undefined,
  ) => Promise<void>;
}

function positionRequest(id: string, position: string, version: number) {
  return apiClient.PATCH("/api/tasks/{id}/position", {
    params: { path: { id } },
    body: { position, version },
  });
}

/**
 * Optimistic REORDER recipe (T057; FR-102, research R10/R18). Re-ranks the target row to the
 * carried fractional position and re-sorts the cache (the list renders in array order, so the
 * row must physically RELOCATE). On a `version_conflict` the recipe refetches, RECOMPUTES
 * `between()` from the FRESH neighbour ranks (looked up by `aboveId`/`belowId`, never the stale
 * rank) and re-issues ONCE against the FRESH version; if the moved row was concurrently deleted
 * it drops the move. Non-conflict → plain rollback.
 */
export function reorderTaskMutationOptions(queryClient: QueryClient): ReorderTaskOptions {
  return {
    mutationFn: async ({ id, position, version }: ReorderTaskVariables): Promise<TaskResponse> => {
      const { data, error } = await positionRequest(id, position, version);
      if (error || !data) {
        const errorCode = (error as ProblemDetails | undefined)?.errorCode;
        throw new TaskMutationError(errorCode ?? "internal_error", mapError(errorCode).message);
      }
      return data;
    },

    onMutate: async (variables: ReorderTaskVariables): Promise<ReorderTaskContext> => {
      await queryClient.cancelQueries({ queryKey: TASKS_QUERY_KEY });
      const previousTasks = queryClient.getQueryData<TaskResponse[]>(TASKS_QUERY_KEY);

      queryClient.setQueryData<TaskResponse[]>(TASKS_QUERY_KEY, (old) => {
        const reranked = (old ?? []).map((t) =>
          t.id === variables.id ? { ...t, position: variables.position } : t,
        );
        // The list renders in array order, so re-sort by the new ranks to RELOCATE the row.
        return sortByPosition(reranked);
      });

      return { previousTasks };
    },

    onError: async (
      error: Error,
      variables: ReorderTaskVariables,
      context: ReorderTaskContext | undefined,
    ): Promise<void> => {
      if (!isVersionConflict(error)) {
        queryClient.setQueryData<TaskResponse[] | undefined>(TASKS_QUERY_KEY, context?.previousTasks);
        return;
      }
      // Once-only reapply: re-read server truth, recompute the rank from the FRESH neighbours.
      await queryClient.refetchQueries({ queryKey: TASKS_QUERY_KEY });
      const fresh = queryClient.getQueryData<TaskResponse[]>(TASKS_QUERY_KEY);
      const freshTarget = fresh?.find((t) => t.id === variables.id);
      if (!freshTarget) return; // row gone (concurrent delete) → drop the move.

      const freshAbove = variables.aboveId ? fresh?.find((t) => t.id === variables.aboveId) : undefined;
      const freshBelow = variables.belowId ? fresh?.find((t) => t.id === variables.belowId) : undefined;
      const recomputed = between(freshAbove?.position ?? null, freshBelow?.position ?? null);
      await positionRequest(variables.id, recomputed, freshTarget.version);
    },

    onSettled: async (data): Promise<void> => {
      // On success, write the server's returned row (with its FRESH bumped `version`) back into
      // the cache synchronously, so a rapid SECOND reorder on the same row reads the current
      // version instead of the stale optimistic one — otherwise the next PATCH would 409 and
      // race the once-only reapply path (research R10; fixes sequential same-row reorders).
      if (data) {
        queryClient.setQueryData<TaskResponse[]>(TASKS_QUERY_KEY, (old) =>
          (old ?? []).map((t) => (t.id === data.id ? data : t)),
        );
      }
      await queryClient.invalidateQueries({ queryKey: TASKS_QUERY_KEY });
    },
  };
}

/* ─────────────────────────────── DELETE (DELETE) ──────────────────────────────── */

export interface DeleteTaskVariables {
  id: string;
}

interface DeleteTaskOptions {
  mutationFn: (variables: DeleteTaskVariables) => Promise<void>;
  onMutate: (variables: DeleteTaskVariables) => Promise<DeleteTaskContext>;
  onError: (
    error: Error,
    variables: DeleteTaskVariables,
    context: DeleteTaskContext | undefined,
  ) => Promise<void>;
  onSettled: (
    data: void | undefined,
    error: Error | null,
    variables: DeleteTaskVariables,
    context: DeleteTaskContext | undefined,
  ) => Promise<void>;
}

/**
 * Optimistic DELETE recipe (T057; FR-093, research R10). Version-free (the DELETE carries no
 * `version`, so there is NO 409 path): `onMutate` REMOVES the target row, and `onError` rolls
 * the snapshot back so a rolled-back delete REAPPEARS at its original index. Always reconciles
 * with server truth via `onSettled` invalidation.
 */
export function deleteTaskMutationOptions(queryClient: QueryClient): DeleteTaskOptions {
  return {
    mutationFn: async ({ id }: DeleteTaskVariables): Promise<void> => {
      const { error } = await apiClient.DELETE("/api/tasks/{id}", {
        params: { path: { id } },
      });
      if (error) {
        const errorCode = (error as ProblemDetails | undefined)?.errorCode;
        throw new TaskMutationError(errorCode ?? "internal_error", mapError(errorCode).message);
      }
    },

    onMutate: async (variables: DeleteTaskVariables): Promise<DeleteTaskContext> => {
      await queryClient.cancelQueries({ queryKey: TASKS_QUERY_KEY });
      const previousTasks = queryClient.getQueryData<TaskResponse[]>(TASKS_QUERY_KEY);

      queryClient.setQueryData<TaskResponse[]>(TASKS_QUERY_KEY, (old) =>
        (old ?? []).filter((t) => t.id !== variables.id),
      );

      return { previousTasks };
    },

    onError: async (
      _error: Error,
      _variables: DeleteTaskVariables,
      context: DeleteTaskContext | undefined,
    ): Promise<void> => {
      // Restore the full ordering — the removed row reappears at its original index.
      queryClient.setQueryData<TaskResponse[] | undefined>(TASKS_QUERY_KEY, context?.previousTasks);
    },

    onSettled: async (): Promise<void> => {
      await queryClient.invalidateQueries({ queryKey: TASKS_QUERY_KEY });
    },
  };
}

/* ──────────────────────── MOVE TO PROJECT (PATCH /project) ──────────────────────── */

/**
 * The task-list cache key for a project's tasks: `['projects', <projectId>, 'tasks']` — the
 * natural extension of the `['projects']` namespace (R16). A `null` projectId is the Inbox,
 * which is the SINGLE `['tasks']` key (project_id IS NULL, R6). Used to derive the source +
 * target list caches the move recipe relocates a row between.
 */
export function projectTasksQueryKey(projectId: string): readonly ["projects", string, "tasks"] {
  return ["projects", projectId, "tasks"] as const;
}

/** Resolves the list cache key a task lives in for the given project (or the Inbox when null). */
function listKeyForProject(projectId: string | null): readonly unknown[] {
  return projectId === null ? TASKS_QUERY_KEY : projectTasksQueryKey(projectId);
}

export interface MoveTaskToProjectVariables {
  id: string;
  /** The list the task currently lives in (`null` = the Inbox) — the source cache. */
  fromProjectId: string | null;
  /** The destination project (`null` moves the task back to the Inbox) — the target cache. */
  toProjectId: string | null;
  version: number;
}

/** Context handed from `onMutate` to `onError`/`onSettled` — both pre-move list snapshots + keys. */
export interface MoveTaskToProjectContext {
  sourceKey: readonly unknown[];
  targetKey: readonly unknown[];
  previousSource: TaskResponse[] | undefined;
  previousTarget: TaskResponse[] | undefined;
}

interface MoveTaskToProjectOptions {
  mutationFn: (variables: MoveTaskToProjectVariables) => Promise<TaskResponse>;
  onMutate: (variables: MoveTaskToProjectVariables) => Promise<MoveTaskToProjectContext>;
  onError: (
    error: Error,
    variables: MoveTaskToProjectVariables,
    context: MoveTaskToProjectContext | undefined,
  ) => Promise<void>;
  onSettled: (
    data: TaskResponse | undefined,
    error: Error | null,
    variables: MoveTaskToProjectVariables,
    context: MoveTaskToProjectContext | undefined,
  ) => Promise<void>;
}

/**
 * Optimistic MOVE-TO-PROJECT recipe (T039; FR-021/AS-05, research R6/R7/R15/R16). A task lives
 * in exactly ONE list cache, so a move RELOCATES the row ACROSS two caches: it is pulled from
 * the source list, re-stamped with the new `projectId`, and inserted (re-sorted by `position`)
 * into the target list — `toProjectId = null` moves it back to the Inbox. Both ends ride on
 * `variables` so the factory is self-contained (the wrapper does no row lookup).
 *
 * Unlike the rename/toggle/reorder recipes there is NO once-only 409 reapply (R10) — mirroring
 * `useProjectMutations`, a 409/422/network error is a PLAIN rollback of BOTH caches, the FR-049
 * message surfacing through the global MutationCache announcer, with server truth reconciled by
 * the `onSettled` invalidate of the two SPECIFIC keys (never the bare `['projects']` prefix,
 * which prefix-matches — and would evict — the sidebar project list).
 */
export function moveTaskToProjectMutationOptions(queryClient: QueryClient): MoveTaskToProjectOptions {
  return {
    mutationFn: async ({ id, toProjectId, version }: MoveTaskToProjectVariables): Promise<TaskResponse> => {
      const { data, error } = await apiClient.PATCH("/api/tasks/{id}/project", {
        params: { path: { id } },
        body: { projectId: toProjectId, version },
      });
      if (error || !data) {
        const errorCode = (error as ProblemDetails | undefined)?.errorCode;
        throw new TaskMutationError(errorCode ?? "internal_error", mapError(errorCode).message);
      }
      return data;
    },

    onMutate: async (variables: MoveTaskToProjectVariables): Promise<MoveTaskToProjectContext> => {
      const sourceKey = listKeyForProject(variables.fromProjectId);
      const targetKey = listKeyForProject(variables.toProjectId);

      // Stop in-flight refetches on BOTH lists so neither can clobber the optimistic relocate.
      await queryClient.cancelQueries({ queryKey: sourceKey });
      await queryClient.cancelQueries({ queryKey: targetKey });

      const previousSource = queryClient.getQueryData<TaskResponse[]>(sourceKey);
      const previousTarget = queryClient.getQueryData<TaskResponse[]>(targetKey);

      // The row being moved (read from the source snapshot), re-stamped with the new placement.
      const row = previousSource?.find((t) => t.id === variables.id);

      // Remove from the source list.
      queryClient.setQueryData<TaskResponse[]>(sourceKey, (old) =>
        (old ?? []).filter((t) => t.id !== variables.id),
      );

      // Insert into the target list (re-stamped projectId), keeping the position ordering invariant.
      if (row) {
        const moved: TaskResponse = { ...row, projectId: variables.toProjectId };
        queryClient.setQueryData<TaskResponse[]>(targetKey, (old) =>
          sortByPosition([...(old ?? []), moved]),
        );
      }

      return { sourceKey, targetKey, previousSource, previousTarget };
    },

    onError: async (
      _error: Error,
      _variables: MoveTaskToProjectVariables,
      context: MoveTaskToProjectContext | undefined,
    ): Promise<void> => {
      // Plain rollback of BOTH caches — the moved row reappears in its source. No reapply.
      if (!context) return;
      queryClient.setQueryData<TaskResponse[] | undefined>(context.sourceKey, context.previousSource);
      queryClient.setQueryData<TaskResponse[] | undefined>(context.targetKey, context.previousTarget);
    },

    onSettled: async (
      _data: TaskResponse | undefined,
      _error: Error | null,
      _variables: MoveTaskToProjectVariables,
      context: MoveTaskToProjectContext | undefined,
    ): Promise<void> => {
      // Reconcile the two SPECIFIC list caches with server truth — never the bare `['projects']`
      // prefix (it would also evict the sidebar list + the archived listing).
      if (!context) return;
      await queryClient.invalidateQueries({ queryKey: context.sourceKey });
      await queryClient.invalidateQueries({ queryKey: context.targetKey });
    },
  };
}

/* ─────────────────────────── SET PRIORITY (PATCH /priority) ─────────────────────────── */

export interface SetPriorityVariables {
  id: string;
  priority: Priority;
  version: number;
}

type ViewMutationOptions<TVariables> = {
  mutationFn: (variables: TVariables) => Promise<TaskResponse>;
  onMutate: (variables: TVariables) => Promise<ViewCachesSnapshot>;
  onError: (error: Error, variables: TVariables, context: ViewCachesSnapshot | undefined) => void;
  onSettled: (
    data: TaskResponse | undefined,
    error: Error | null,
    variables: TVariables,
    context: ViewCachesSnapshot | undefined,
  ) => Promise<void>;
};

/**
 * Optimistic SET-PRIORITY recipe (slice 005, AS-04, R2/R7). Re-stamps the priority on the target row and
 * re-sorts the Today/Upcoming groups via the client membership recompute (priority does not change
 * membership, only order). Like move-to-project there is NO once-only 409 reapply — a 409/422/network error
 * is a PLAIN rollback (the FR-049 message surfaces through the global MutationCache announcer).
 */
export function setPriorityMutationOptions(queryClient: QueryClient): ViewMutationOptions<SetPriorityVariables> {
  return {
    mutationFn: async ({ id, priority, version }: SetPriorityVariables): Promise<TaskResponse> => {
      const { data, error } = await apiClient.PATCH("/api/tasks/{id}/priority", {
        params: { path: { id } },
        body: { priority, version },
      });
      if (error || !data) {
        const errorCode = (error as ProblemDetails | undefined)?.errorCode;
        throw new TaskMutationError(errorCode ?? "internal_error", mapError(errorCode).message);
      }
      return data;
    },
    onMutate: async (variables: SetPriorityVariables): Promise<ViewCachesSnapshot> => {
      await queryClient.cancelQueries({ queryKey: TASKS_QUERY_KEY });
      await queryClient.cancelQueries({ queryKey: TODAY_QUERY_KEY });
      await queryClient.cancelQueries({ queryKey: UPCOMING_QUERY_KEY });
      const snapshot = snapshotViewCaches(queryClient);
      const row = findTaskInViewCaches(queryClient, variables.id);
      if (row) applyTaskToViewCaches(queryClient, { ...row, priority: variables.priority }, new Date());
      return snapshot;
    },
    onError: (_error, _variables, context): void => {
      if (context) rollbackViewCaches(queryClient, context);
    },
    onSettled: async (data): Promise<void> => {
      // Write the server row (with its FRESH bumped version) back so a rapid SECOND op on the same row
      // reads the current version instead of the stale optimistic one (mirrors toggle/rename/reorder).
      if (data) applyTaskToViewCaches(queryClient, data, new Date());
      await settleViewCaches(queryClient);
    },
  };
}

/* ─────────────────────────── RESCHEDULE (PATCH /due-date) ─────────────────────────── */

export interface RescheduleDueDateVariables {
  id: string;
  dueDate: string | null;
  dueHasTime: boolean | null;
  version: number;
}

/**
 * Optimistic RESCHEDULE recipe (slice 005, AS-05, R4/R7). Re-stamps the due pair and recomputes view
 * membership: a reschedule to tomorrow DROPS the row from Today and adds it to Upcoming (AS-05's "it
 * disappears from Today"); clearing the due date drops it from both. Plain rollback on error.
 */
export function rescheduleDueDateMutationOptions(
  queryClient: QueryClient,
): ViewMutationOptions<RescheduleDueDateVariables> {
  return {
    mutationFn: async ({ id, dueDate, dueHasTime, version }: RescheduleDueDateVariables): Promise<TaskResponse> => {
      const { data, error } = await apiClient.PATCH("/api/tasks/{id}/due-date", {
        params: { path: { id } },
        body: { dueDate, dueHasTime, version },
      });
      if (error || !data) {
        const errorCode = (error as ProblemDetails | undefined)?.errorCode;
        throw new TaskMutationError(errorCode ?? "internal_error", mapError(errorCode).message);
      }
      return data;
    },
    onMutate: async (variables: RescheduleDueDateVariables): Promise<ViewCachesSnapshot> => {
      await queryClient.cancelQueries({ queryKey: TASKS_QUERY_KEY });
      await queryClient.cancelQueries({ queryKey: TODAY_QUERY_KEY });
      await queryClient.cancelQueries({ queryKey: UPCOMING_QUERY_KEY });
      const snapshot = snapshotViewCaches(queryClient);
      const row = findTaskInViewCaches(queryClient, variables.id);
      if (row) {
        applyTaskToViewCaches(
          queryClient,
          { ...row, dueDate: variables.dueDate, dueHasTime: variables.dueHasTime },
          new Date(),
        );
      }
      return snapshot;
    },
    onError: (_error, _variables, context): void => {
      if (context) rollbackViewCaches(queryClient, context);
    },
    onSettled: async (data): Promise<void> => {
      if (data) applyTaskToViewCaches(queryClient, data, new Date());
      await settleViewCaches(queryClient);
    },
  };
}

/* ──────────────────────────────── EDIT (PATCH /edit) ──────────────────────────────── */

export interface EditTaskVariables {
  id: string;
  title: string;
  description: string | null;
  priority: Priority;
  dueDate: string | null;
  dueHasTime: boolean | null;
  projectId: string | null;
  version: number;
}

/**
 * Optimistic EDIT recipe (slice 005, AS-06/07/08, R4/R7) — the combined whole-object replace. Re-stamps all
 * editable fields and recomputes view membership/order. Plain rollback on error. `onSettled` additionally
 * invalidates the source + target project caches when the edit moved the task across projects.
 */
export function editTaskMutationOptions(queryClient: QueryClient): ViewMutationOptions<EditTaskVariables> {
  return {
    mutationFn: async (variables: EditTaskVariables): Promise<TaskResponse> => {
      const { id, title, description, priority, dueDate, dueHasTime, projectId, version } = variables;
      const { data, error } = await apiClient.PATCH("/api/tasks/{id}/edit", {
        params: { path: { id } },
        body: { title, description, priority, dueDate, dueHasTime, projectId, version },
      });
      if (error || !data) {
        const errorCode = (error as ProblemDetails | undefined)?.errorCode;
        throw new TaskMutationError(errorCode ?? "internal_error", mapError(errorCode).message);
      }
      return data;
    },
    onMutate: async (variables: EditTaskVariables): Promise<ViewCachesSnapshot> => {
      await queryClient.cancelQueries({ queryKey: TASKS_QUERY_KEY });
      await queryClient.cancelQueries({ queryKey: TODAY_QUERY_KEY });
      await queryClient.cancelQueries({ queryKey: UPCOMING_QUERY_KEY });
      const snapshot = snapshotViewCaches(queryClient);
      const row = findTaskInViewCaches(queryClient, variables.id);
      if (row) {
        applyTaskToViewCaches(
          queryClient,
          {
            ...row,
            title: variables.title,
            description: variables.description,
            priority: variables.priority,
            dueDate: variables.dueDate,
            dueHasTime: variables.dueHasTime,
            projectId: variables.projectId,
          },
          new Date(),
        );
      }
      return snapshot;
    },
    onError: (_error, _variables, context): void => {
      if (context) rollbackViewCaches(queryClient, context);
    },
    onSettled: async (data, _error, variables): Promise<void> => {
      // Write the server row (FRESH version) back so a rapid second edit on the same row isn't a 409 lost
      // update (mirrors toggle/rename/reorder).
      if (data) applyTaskToViewCaches(queryClient, data, new Date());
      await settleViewCaches(queryClient);
      // An edit can move the task across projects — reconcile the (possibly two) project board caches.
      if (variables.projectId != null) {
        await queryClient.invalidateQueries({ queryKey: projectTasksQueryKey(variables.projectId) });
      }
    },
  };
}

/**
 * "use client" hook wrapper. `createTask(title)` validates the title at the trust
 * boundary (Constitution VI), mints the client-side UUIDv7 id, computes the
 * newest-first rank from the current `['tasks']` cache head, then drives the
 * optimistic create recipe. The edit helpers (rename/toggle/reorder/delete) drive their
 * matching versioned recipes; each reads the current row from the `['tasks']` cache to
 * stamp the optimistic `version`/neighbours onto the mutation variables.
 */
export function useTaskMutations() {
  const queryClient = useQueryClient();
  const createMutation = useMutation<TaskResponse, Error, CreateTaskVariables, CreateTaskContext>(
    createTaskMutationOptions(queryClient),
  );
  const renameMutation = useMutation<TaskResponse, Error, RenameTaskVariables, RenameTaskContext>(
    renameTaskMutationOptions(queryClient),
  );
  const toggleMutation = useMutation<TaskResponse, Error, ToggleDoneVariables, ToggleDoneContext>(
    toggleDoneMutationOptions(queryClient),
  );
  const reorderMutation = useMutation<TaskResponse, Error, ReorderTaskVariables, ReorderTaskContext>(
    reorderTaskMutationOptions(queryClient),
  );
  const deleteMutation = useMutation<void, Error, DeleteTaskVariables, DeleteTaskContext>(
    deleteTaskMutationOptions(queryClient),
  );
  const moveMutation = useMutation<TaskResponse, Error, MoveTaskToProjectVariables, MoveTaskToProjectContext>(
    moveTaskToProjectMutationOptions(queryClient),
  );
  const setPriorityMutation = useMutation<TaskResponse, Error, SetPriorityVariables, ViewCachesSnapshot>(
    setPriorityMutationOptions(queryClient),
  );
  const rescheduleMutation = useMutation<TaskResponse, Error, RescheduleDueDateVariables, ViewCachesSnapshot>(
    rescheduleDueDateMutationOptions(queryClient),
  );
  const editMutation = useMutation<TaskResponse, Error, EditTaskVariables, ViewCachesSnapshot>(
    editTaskMutationOptions(queryClient),
  );

  const currentTasks = (): TaskResponse[] => queryClient.getQueryData<TaskResponse[]>(TASKS_QUERY_KEY) ?? [];

  const createTask = (input: { title: string; dueDate?: Date; dueHasTime?: boolean }): void => {
    // Defensive boundary parse (Constitution VI) — validates the title AND the R8 due-date
    // pairing in one schema, replacing the prior `taskTitleSchema.parse`.
    const parsed = createTaskSchema.parse(input);

    const head = currentTasks()[0];
    const position = between(null, head ? head.position : null);

    // The resolved `Date` becomes the wire/optimistic ISO string here, once — the recipe
    // layer only ever handles the string shape (matching the nullable `TaskResponse` type).
    createMutation.mutate({
      id: newTaskId(),
      title: parsed.title,
      position,
      dueDate: parsed.dueDate?.toISOString(),
      dueHasTime: parsed.dueHasTime,
    });
  };

  const renameTask = (id: string, title: string): void => {
    const parsedTitle = taskTitleSchema.parse(title);
    const row = currentTasks().find((t) => t.id === id);
    if (!row) return;
    renameMutation.mutate({ id, title: parsedTitle, version: row.version });
  };

  const setTaskDone = (id: string, done: boolean): void => {
    // Find the row across the Inbox + Today + Upcoming caches — toggle-done is exercised from all three.
    const row = findTaskInViewCaches(queryClient, id);
    if (!row) return;
    toggleMutation.mutate({ id, status: done ? "done" : "backlog", version: row.version });
  };

  /** Sets (or clears) the selected task's priority — the `1`-`4` keys (slice 005, AS-04). */
  const setTaskPriority = (id: string, priority: Priority): void => {
    const row = findTaskInViewCaches(queryClient, id);
    if (!row) return;
    setPriorityMutation.mutate({ id, priority, version: row.version });
  };

  /** Reschedules (or clears) the selected task's due date — the `T` key (slice 005, AS-05). */
  const rescheduleTask = (id: string, dueDate: Date | null, dueHasTime: boolean | null): void => {
    const row = findTaskInViewCaches(queryClient, id);
    if (!row) return;
    rescheduleMutation.mutate({
      id,
      dueDate: dueDate ? dueDate.toISOString() : null,
      dueHasTime,
      version: row.version,
    });
  };

  /** Saves the task editor's whole-object replace — `Ctrl+Enter` (slice 005, AS-06/07/08). */
  const editTask = (
    id: string,
    fields: {
      title: string;
      description: string | null;
      priority: Priority;
      dueDate: Date | null;
      dueHasTime: boolean | null;
      projectId: string | null;
    },
  ): void => {
    const row = findTaskInViewCaches(queryClient, id);
    if (!row) return;
    // Defensive boundary parse (Constitution VI) — the whole-object replace, validated as one schema.
    const parsed = editTaskSchema.parse(fields);
    editMutation.mutate({
      id,
      title: parsed.title,
      description: parsed.description,
      priority: parsed.priority,
      dueDate: parsed.dueDate ? parsed.dueDate.toISOString() : null,
      dueHasTime: parsed.dueHasTime,
      projectId: parsed.projectId,
      version: row.version,
    });
  };

  /** Moves `id` to sit between the rows currently above/below the drop target (by id). */
  const reorderTask = (id: string, aboveId: string | null, belowId: string | null): void => {
    const tasks = currentTasks();
    const row = tasks.find((t) => t.id === id);
    if (!row) return;
    const above = aboveId ? tasks.find((t) => t.id === aboveId) : undefined;
    const below = belowId ? tasks.find((t) => t.id === belowId) : undefined;
    const position = between(above?.position ?? null, below?.position ?? null);
    reorderMutation.mutate({ id, position, aboveId, belowId, version: row.version });
  };

  const deleteTask = (id: string): void => {
    deleteMutation.mutate({ id });
  };

  /**
   * Moves a task to `toProjectId` (or back to the Inbox when `null`). Reads the row's CURRENT
   * placement (`projectId`) + `version` from the live caches so the recipe can relocate it
   * between the source and target list caches and carry the optimistic `version` to the server.
   * Looks the row up in the Inbox first, then in the source project cache when supplied.
   */
  const moveTaskToProject = (
    id: string,
    toProjectId: string | null,
    fromProjectId: string | null = null,
  ): void => {
    const source =
      queryClient.getQueryData<TaskResponse[]>(listKeyForProject(fromProjectId)) ?? [];
    const row = source.find((t) => t.id === id);
    if (!row) return;
    moveMutation.mutate({ id, fromProjectId, toProjectId, version: row.version });
  };

  return {
    createTask,
    renameTask,
    setTaskDone,
    reorderTask,
    deleteTask,
    moveTaskToProject,
    setTaskPriority,
    rescheduleTask,
    editTask,
  };
}

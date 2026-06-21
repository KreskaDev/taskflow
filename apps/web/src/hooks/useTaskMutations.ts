"use client";

import { type QueryClient, useMutation, useQueryClient } from "@tanstack/react-query";

import { apiClient, mapError, type ProblemDetails } from "@/lib/api/client";
import type { components } from "@/lib/api/generated/schema";
import { newTaskId } from "@/lib/id";
import { between } from "@/lib/position";
import { taskTitleSchema } from "@/lib/validation/task";
import { TASKS_QUERY_KEY } from "@/hooks/useTasks";

type TaskResponse = components["schemas"]["TaskResponse"];

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
    mutationFn: async ({ id, title, position }: CreateTaskVariables): Promise<TaskResponse> => {
      const { data, error } = await apiClient.PUT("/api/tasks/{id}", {
        params: { path: { id } },
        body: { title, position },
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
export type ToggleDoneContext = EditTaskContext;
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
      await queryClient.cancelQueries({ queryKey: TASKS_QUERY_KEY });
      const previousTasks = queryClient.getQueryData<TaskResponse[]>(TASKS_QUERY_KEY);

      queryClient.setQueryData<TaskResponse[]>(TASKS_QUERY_KEY, (old) =>
        (old ?? []).map((t) => (t.id === variables.id ? { ...t, status: variables.status } : t)),
      );

      return { previousTasks };
    },

    onError: async (
      error: Error,
      variables: ToggleDoneVariables,
      context: ToggleDoneContext | undefined,
    ): Promise<void> => {
      if (!isVersionConflict(error)) {
        queryClient.setQueryData<TaskResponse[] | undefined>(TASKS_QUERY_KEY, context?.previousTasks);
        return;
      }
      const fresh = await refetchFreshRow(queryClient, variables.id);
      if (!fresh) return; // row gone → drop the toggle.
      if (fresh.status === variables.status) return; // already in the desired state → idempotent no-op.
      await statusRequest(variables.id, variables.status, fresh.version);
    },

    onSettled: async (data): Promise<void> => {
      // On success, write the server's returned row (with its FRESH bumped `version`) back into
      // the cache synchronously, so a rapid SECOND toggle on the same row reads the current
      // version instead of the stale optimistic one — otherwise the next PATCH would 409 and
      // race the once-only reapply path (research R10; fixes sequential same-row toggles).
      if (data) {
        queryClient.setQueryData<TaskResponse[]>(TASKS_QUERY_KEY, (old) =>
          (old ?? []).map((t) => (t.id === data.id ? data : t)),
        );
      }
      await queryClient.invalidateQueries({ queryKey: TASKS_QUERY_KEY });
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

  const currentTasks = (): TaskResponse[] => queryClient.getQueryData<TaskResponse[]>(TASKS_QUERY_KEY) ?? [];

  const createTask = (title: string): void => {
    const parsedTitle = taskTitleSchema.parse(title);

    const head = currentTasks()[0];
    const position = between(null, head ? head.position : null);

    createMutation.mutate({ id: newTaskId(), title: parsedTitle, position });
  };

  const renameTask = (id: string, title: string): void => {
    const parsedTitle = taskTitleSchema.parse(title);
    const row = currentTasks().find((t) => t.id === id);
    if (!row) return;
    renameMutation.mutate({ id, title: parsedTitle, version: row.version });
  };

  const setTaskDone = (id: string, done: boolean): void => {
    const row = currentTasks().find((t) => t.id === id);
    if (!row) return;
    toggleMutation.mutate({ id, status: done ? "done" : "backlog", version: row.version });
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

  return { createTask, renameTask, setTaskDone, reorderTask, deleteTask };
}

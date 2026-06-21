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

/**
 * "use client" hook wrapper. `createTask(title)` validates the title at the trust
 * boundary (Constitution VI), mints the client-side UUIDv7 id, computes the
 * newest-first rank from the current `['tasks']` cache head, then drives the
 * optimistic create recipe.
 */
export function useTaskMutations() {
  const queryClient = useQueryClient();
  const createMutation = useMutation<TaskResponse, Error, CreateTaskVariables, CreateTaskContext>(
    createTaskMutationOptions(queryClient),
  );

  const createTask = (title: string): void => {
    const parsedTitle = taskTitleSchema.parse(title);

    const tasks = queryClient.getQueryData<TaskResponse[]>(TASKS_QUERY_KEY);
    const head = tasks?.[0];
    const position = between(null, head ? head.position : null);

    createMutation.mutate({ id: newTaskId(), title: parsedTitle, position });
  };

  return { createTask };
}

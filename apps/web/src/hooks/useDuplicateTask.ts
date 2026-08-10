"use client";

import { useMutation, useQueryClient } from "@tanstack/react-query";
import { invalidateViewCounts } from "@/hooks/useViewCounts";
import { projectTasksQueryKey } from "@/hooks/useTaskMutations";
import { TASKS_QUERY_KEY, type TaskResponse } from "@/hooks/useTasks";
import { apiClient, mapError, type ProblemDetails } from "@/lib/api/client";
import { newTaskId } from "@/lib/id";
import { between } from "@/lib/position";

/**
 * "Duplikuj" (slice 019, T042 — FR-112, contracts/task-duplicate.md) on the established
 * optimistic factory pattern: `onMutate` inserts a copied-field duplicate with a
 * client-generated UUIDv7 id DIRECTLY AFTER the source row in its context cache (Inbox
 * `['tasks']` or the project's task list); `onError` restores the snapshot; `onSettled`
 * invalidates the view-key family + `['views','counts']`. The same id rides the request
 * (`newTaskId`), so the server row and the optimistic row are one identity (idempotent
 * replay-safe).
 */
export function useDuplicateTask() {
  const queryClient = useQueryClient();

  const mutation = useMutation({
    mutationFn: async (input: { source: TaskResponse; newId: string }) => {
      const { data, error } = await apiClient.POST("/api/tasks/{id}/duplicate", {
        params: { path: { id: input.source.id } },
        body: { newTaskId: input.newId },
      });
      if (error || !data) {
        const errorCode = (error as ProblemDetails | undefined)?.errorCode;
        throw new Error(mapError(errorCode).message);
      }
      return data;
    },
    onMutate: async (input) => {
      const contextKey =
        input.source.projectId != null
          ? projectTasksQueryKey(input.source.projectId)
          : TASKS_QUERY_KEY;
      await queryClient.cancelQueries({ queryKey: contextKey });
      const previous = queryClient.getQueryData<TaskResponse[]>(contextKey);

      if (previous) {
        const index = previous.findIndex((t) => t.id === input.source.id);
        if (index >= 0) {
          const successor = previous[index + 1];
          const now = new Date().toISOString();
          const optimistic: TaskResponse = {
            ...input.source,
            id: input.newId,
            status: "backlog",
            completedAt: null,
            version: 0,
            createdAt: now,
            updatedAt: now,
            position: between(input.source.position, successor?.position ?? null),
          };
          const next = [...previous];
          next.splice(index + 1, 0, optimistic);
          queryClient.setQueryData<TaskResponse[]>(contextKey, next);
        }
      }
      return { contextKey, previous };
    },
    onError: (_error, _input, context) => {
      if (context) {
        queryClient.setQueryData(context.contextKey, context.previous);
      }
    },
    onSettled: async (_data, _error, _input, context) => {
      if (context) {
        await queryClient.invalidateQueries({ queryKey: context.contextKey });
      }
      await queryClient.invalidateQueries({ queryKey: TASKS_QUERY_KEY });
      await invalidateViewCounts(queryClient);
    },
  });

  return {
    /** Duplicate `source` into its own context; paints optimistically adjacent to it. */
    duplicateTask: (source: TaskResponse) => mutation.mutate({ source, newId: newTaskId() }),
  };
}

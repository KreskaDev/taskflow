"use client";

import { useQuery } from "@tanstack/react-query";

import { apiClient, mapError, type ProblemDetails } from "@/lib/api/client";
import { projectTasksQueryKey } from "@/hooks/useTaskMutations";
import type { TaskResponse } from "@/hooks/useTasks";

/**
 * A single project's task list (`GET /api/projects/{id}/tasks`; slice-004 T051, FR-021/R6). Keyed on
 * `projectTasksQueryKey(id)` — the SAME key the move recipe's source/target caches use — so an
 * optimistic move INTO or OUT OF this project reconciles this view with no extra refetch. Owner- and
 * project-scoped server-side (a foreign/absent project → 404).
 */
export function useProjectTasks(projectId: string) {
  return useQuery<TaskResponse[]>({
    queryKey: projectTasksQueryKey(projectId),
    queryFn: async (): Promise<TaskResponse[]> => {
      const { data, error } = await apiClient.GET("/api/projects/{id}/tasks", {
        params: { path: { id: projectId } },
      });
      if (error || !data) {
        const errorCode = (error as ProblemDetails | undefined)?.errorCode;
        throw new Error(mapError(errorCode).message);
      }
      return data;
    },
  });
}

"use client";

import { useQuery } from "@tanstack/react-query";

import { apiClient, mapError, type ProblemDetails } from "@/lib/api/client";
import type { components } from "@/lib/api/generated/schema";

export type TaskResponse = components["schemas"]["TaskResponse"];

/** The single TanStack Query key for the task list (slice-002). */
export const TASKS_QUERY_KEY = ["tasks"] as const;

/**
 * Task list state. Reads the API's `GET /api/tasks` (via the BFF proxy) through the
 * typed openapi-fetch client so the response is typed straight from the generated
 * OpenAPI schema. Mirrors `useSession` for the "use client"/`useQuery` shape.
 */
export function useTasks() {
  return useQuery<TaskResponse[]>({
    queryKey: TASKS_QUERY_KEY,
    queryFn: async (): Promise<TaskResponse[]> => {
      const { data, error } = await apiClient.GET("/api/tasks");
      if (error || !data) {
        const errorCode = (error as ProblemDetails | undefined)?.errorCode;
        throw new Error(mapError(errorCode).message);
      }
      return data;
    },
  });
}

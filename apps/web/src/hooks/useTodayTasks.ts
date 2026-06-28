"use client";

import { useQuery } from "@tanstack/react-query";

import { apiClient, mapError, type ProblemDetails } from "@/lib/api/client";
import type { components } from "@/lib/api/generated/schema";

export type TodayResponse = components["schemas"]["TodayResponse"];

/** The TanStack Query key for the Today view (slice 005, R7). */
export const TODAY_QUERY_KEY = ["tasks", "today"] as const;

/**
 * Today-view state (slice 005, AS-01/AS-02). Reads `GET /api/tasks/today` through the typed
 * openapi-fetch client; the server computes the Warsaw day boundary, the project grouping, and the
 * R5 order, so the client renders the grouped, ready-to-paint {@link TodayResponse} as-is.
 */
export function useTodayTasks() {
  return useQuery<TodayResponse>({
    queryKey: TODAY_QUERY_KEY,
    queryFn: async (): Promise<TodayResponse> => {
      const { data, error } = await apiClient.GET("/api/tasks/today");
      if (error || !data) {
        const errorCode = (error as ProblemDetails | undefined)?.errorCode;
        throw new Error(mapError(errorCode).message);
      }
      return data;
    },
  });
}

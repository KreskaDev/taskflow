"use client";

import { useQuery } from "@tanstack/react-query";

import { apiClient, mapError, type ProblemDetails } from "@/lib/api/client";
import type { components } from "@/lib/api/generated/schema";

export type UpcomingResponse = components["schemas"]["UpcomingResponse"];

/** The TanStack Query key for the Upcoming view (slice 005, R7). */
export const UPCOMING_QUERY_KEY = ["tasks", "upcoming"] as const;

/**
 * Upcoming-view state (slice 005, US-08.AS-02). Reads `GET /api/tasks/upcoming`; the server computes
 * the 7-day Warsaw window, the day grouping (by Warsaw `LocalDate`), and the R5 order, so the client
 * renders the grouped {@link UpcomingResponse} as-is.
 */
export function useUpcomingTasks() {
  return useQuery<UpcomingResponse>({
    queryKey: UPCOMING_QUERY_KEY,
    queryFn: async (): Promise<UpcomingResponse> => {
      const { data, error } = await apiClient.GET("/api/tasks/upcoming");
      if (error || !data) {
        const errorCode = (error as ProblemDetails | undefined)?.errorCode;
        throw new Error(mapError(errorCode).message);
      }
      return data;
    },
  });
}

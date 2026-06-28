"use client";

import { useQuery } from "@tanstack/react-query";

import { apiClient, mapError, type ProblemDetails } from "@/lib/api/client";
import type { components } from "@/lib/api/generated/schema";

export type AssignedResponse = components["schemas"]["AssignedResponse"];

/** The TanStack Query key for the "Assigned to me" view (slice 008). */
export const ASSIGNED_QUERY_KEY = ["tasks", "assigned"] as const;

/**
 * "Assigned to me" state (slice 008, AS-03/FR-071). Reads `GET /api/tasks/assigned`; the server scopes to the
 * caller's assigned tasks across shared projects they currently belong to (membership/ownership gates), groups
 * by project, and R5-orders — the client renders the grouped {@link AssignedResponse} as-is.
 */
export function useAssignedTasks() {
  return useQuery<AssignedResponse>({
    queryKey: ASSIGNED_QUERY_KEY,
    queryFn: async (): Promise<AssignedResponse> => {
      const { data, error } = await apiClient.GET("/api/tasks/assigned");
      if (error || !data) {
        const errorCode = (error as ProblemDetails | undefined)?.errorCode;
        throw new Error(mapError(errorCode).message);
      }
      return data;
    },
  });
}

"use client";

import { useQuery, type QueryClient } from "@tanstack/react-query";
import { apiClient } from "@/lib/api/client";
import type { components } from "@/lib/api/generated/schema";

export type ViewCountsResponse = components["schemas"]["ViewCountsResponse"];

/**
 * The sidebar counts query key (slice 019, D6). Deliberately OUTSIDE the `['tasks']`
 * prefix family, so it is invalidated EXPLICITLY wherever task/membership/project
 * mutations already reconcile the view caches (their `onSettled`) — counts track
 * optimistic flows without new plumbing (data-model.md).
 */
export const VIEW_COUNTS_QUERY_KEY = ["views", "counts"] as const;

/** Invalidate helper the mutation factories call from their `onSettled`. */
export async function invalidateViewCounts(queryClient: QueryClient): Promise<void> {
  await queryClient.invalidateQueries({ queryKey: VIEW_COUNTS_QUERY_KEY });
}

/**
 * The authorization-scoped sidebar counts (FR-109, contracts/view-counts.md): incomplete
 * tasks per primary view + one entry per accessible project, computed server-side with
 * the same Europe/Warsaw boundaries as the views themselves.
 */
export function useViewCounts() {
  return useQuery({
    queryKey: VIEW_COUNTS_QUERY_KEY,
    queryFn: async (): Promise<ViewCountsResponse> => {
      const { data, error } = await apiClient.GET("/api/views/counts");
      if (error || !data) {
        throw new Error("Nie udało się wczytać liczników.");
      }
      return data;
    },
  });
}

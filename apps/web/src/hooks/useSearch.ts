"use client";

import { keepPreviousData, type QueryClient, useQuery } from "@tanstack/react-query";

import { apiClient, mapError, type ProblemDetails } from "@/lib/api/client";
import type { components } from "@/lib/api/generated/schema";
import { logError } from "@/lib/logError";
import { normalizeQuery } from "@/lib/palette";

export type SearchResponse = components["schemas"]["SearchResponse"];

/** The query-key family for the palette search (slice 013, research D9/D10). */
export const SEARCH_QUERY_KEY = ["search"] as const;

export function searchQueryKey(q: string) {
  return [...SEARCH_QUERY_KEY, normalizeQuery(q)] as const;
}

/**
 * The command palette search (slice 013 — contracts/search-api.md): `GET /api/search?q=` keyed by the
 * normalized query, enabled only for a non-blank query, and keeping the previous result set visible while the
 * next one loads (no flicker between keystrokes — Principle IV). Failures surface through the palette's FR-049
 * row and are logged with structured context (FR-050). No debounce: TanStack dedupes identical keys and the
 * latest key wins (the 50 ms posture of SC-009).
 */
export function useSearch(q: string) {
  const normalized = normalizeQuery(q);
  return useQuery<SearchResponse, Error>({
    queryKey: searchQueryKey(q),
    enabled: normalized.length > 0,
    placeholderData: keepPreviousData,
    staleTime: 5_000,
    retry: false,
    queryFn: async (): Promise<SearchResponse> => {
      const { data, error } = await apiClient.GET("/api/search", { params: { query: { q: normalized } } });
      if (error || !data) {
        const errorCode = (error as ProblemDetails | undefined)?.errorCode;
        const message = mapError(errorCode).message;
        logError({ severity: "warning", operation: "search.query", error: new Error(message), details: { errorCode, length: normalized.length } });
        throw new Error(message);
      }
      return data;
    },
  });
}

/**
 * Drops every cached search result (slice 013 D10 / FR-095 posture): called on the access-loss mutation settle
 * paths (leave / remove / unshare) and when the palette opens, so a now-inaccessible item never surfaces from a
 * stale cache.
 */
export function invalidateSearch(queryClient: QueryClient): Promise<void> {
  return queryClient.invalidateQueries({ queryKey: SEARCH_QUERY_KEY });
}

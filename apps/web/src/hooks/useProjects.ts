"use client";

import { useQuery } from "@tanstack/react-query";

import { apiClient, mapError, type ProblemDetails } from "@/lib/api/client";
import type { components } from "@/lib/api/generated/schema";

export type ProjectResponse = components["schemas"]["ProjectResponse"];

/** The TanStack Query key for the ACTIVE (non-archived) project list — the sidebar tree (R8/R16). */
export const ACTIVE_PROJECTS_KEY = ["projects"] as const;

/** The query key for the ARCHIVED listing, surfaced behind the sidebar "Archived" disclosure (R8). */
export const ARCHIVED_PROJECTS_KEY = ["projects", "archived"] as const;

/**
 * Active project list (T026, research R8/R16). Reads `GET /api/projects` (active, owner-scoped,
 * tombstone-excluded) through the typed openapi-fetch client. The Sidebar assembles the one-level
 * tree from this FLAT list client-side (R16) — the API returns no nested structure.
 */
export function useProjects() {
  return useQuery<ProjectResponse[]>({
    queryKey: ACTIVE_PROJECTS_KEY,
    queryFn: async (): Promise<ProjectResponse[]> => {
      const { data, error } = await apiClient.GET("/api/projects");
      if (error || !data) {
        const errorCode = (error as ProblemDetails | undefined)?.errorCode;
        throw new Error(mapError(errorCode).message);
      }
      return data;
    },
  });
}

/**
 * Archived project listing (T026, R8). Reads `GET /api/projects?archived=true` — the minimal
 * keyboard-reachable surface that makes unarchive (AS-11) reachable before slice-013 search. Kept
 * lazy via `enabled` so the archived rows are only fetched when the disclosure is opened.
 */
export function useArchivedProjects(enabled: boolean) {
  return useQuery<ProjectResponse[]>({
    queryKey: ARCHIVED_PROJECTS_KEY,
    enabled,
    queryFn: async (): Promise<ProjectResponse[]> => {
      const { data, error } = await apiClient.GET("/api/projects", {
        params: { query: { archived: true } },
      });
      if (error || !data) {
        const errorCode = (error as ProblemDetails | undefined)?.errorCode;
        throw new Error(mapError(errorCode).message);
      }
      return data;
    },
  });
}

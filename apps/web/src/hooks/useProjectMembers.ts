"use client";

import { useQuery } from "@tanstack/react-query";

import { apiClient, mapError, type ProblemDetails } from "@/lib/api/client";
import type { components } from "@/lib/api/generated/schema";

export type MembersResponse = components["schemas"]["MembersResponse"];
export type MemberResponse = components["schemas"]["MemberResponse"];

/** The TanStack Query key for a shared project's members roster (slice 007, R17). */
export const membersKey = (projectId: string) => ["projects", projectId, "members"] as const;

/**
 * The members-roster query (slice 007, T041, R17). Reads `GET /api/projects/{id}/members` — the composed
 * owner ∪ editor/viewer roster plus the project `version` (the token the non-optimistic membership
 * mutations carry, R11). Kept lazy via `enabled` so the roster is fetched only when the members surface
 * is opened. A non-member read is a 404 (existence not disclosed, R9), surfaced as the query error.
 */
export function useProjectMembers(projectId: string, enabled: boolean) {
  return useQuery<MembersResponse>({
    queryKey: membersKey(projectId),
    enabled,
    queryFn: async (): Promise<MembersResponse> => {
      const { data, error } = await apiClient.GET("/api/projects/{id}/members", {
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

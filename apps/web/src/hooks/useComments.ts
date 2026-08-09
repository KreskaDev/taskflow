"use client";

import { useQuery } from "@tanstack/react-query";

import { apiClient, mapError, type ProblemDetails } from "@/lib/api/client";
import type { components } from "@/lib/api/generated/schema";

export type CommentResponse = components["schemas"]["CommentResponse"];
export type CommentMention = components["schemas"]["CommentMentionResponse"];
export type CommentListResponse = components["schemas"]["CommentListResponse"];

/**
 * The TanStack Query key for a task's comment thread (slice 009, R14):
 * `['tasks', <taskId>, 'comments']` — the natural extension of the `['tasks']` namespace. NOTE: the
 * slice-005 `settleViewCaches` prefix-invalidate of `['tasks']` also touches this key; that is harmless
 * (an occasional thread refetch), never load-bearing.
 */
export const commentsKey = (taskId: string) => ["tasks", taskId, "comments"] as const;

/**
 * The LAZY thread read (slice 009, T032, R14/R15): `GET /api/tasks/{taskId}/comments` — the chronological
 * live thread with tombstone-safe author identity, typed mention tokens, and the caller-scoped `canEdit`.
 * Fetched only when the task detail panel opens (`enabled`), per the plan's lazy-fetch performance goal.
 * A non-member / personal-task read is a 404 (existence not disclosed, R3) surfaced as the query error
 * with the FR-049 `mapError` message.
 */
export function useComments(taskId: string, enabled: boolean) {
  return useQuery<CommentListResponse>({
    queryKey: commentsKey(taskId),
    enabled,
    queryFn: async (): Promise<CommentListResponse> => {
      const { data, error } = await apiClient.GET("/api/tasks/{taskId}/comments", {
        params: { path: { taskId } },
      });
      if (error || !data) {
        const errorCode = (error as ProblemDetails | undefined)?.errorCode;
        throw new Error(mapError(errorCode).message);
      }
      return data;
    },
  });
}

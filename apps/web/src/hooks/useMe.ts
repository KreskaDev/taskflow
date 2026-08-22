"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";

import { useToast } from "@/components/ui/Toast";
import { apiClient, mapError, type ProblemDetails } from "@/lib/api/client";
import type { components } from "@/lib/api/generated/schema";

export type UserProfile = components["schemas"]["UserProfile"];

/** The authenticated user's API profile incl. preferences (widened in slice 011 — D8). */
const ME_QUERY_KEY = ["me"] as const;

function errorFrom(error: unknown): Error {
  return new Error(mapError((error as ProblemDetails | undefined)?.errorCode).message);
}

/** `GET /api/users/me` — profile + `cycleDefaultDurationDays` (the D18 create-form pre-fill input). */
export function useMe() {
  return useQuery<UserProfile>({
    queryKey: ME_QUERY_KEY,
    queryFn: async (): Promise<UserProfile> => {
      const { data, error } = await apiClient.GET("/api/users/me");
      if (error || !data) throw errorFrom(error);
      return data;
    },
  });
}

/**
 * The FR-015 preference write (`PATCH /api/users/me/preferences`, D8): optimistic on the `["me"]`
 * cache with snapshot/rollback; success announces politely via the toast LiveRegion (FR-101).
 */
export function usePreferencesMutation() {
  const queryClient = useQueryClient();
  const { push } = useToast();

  const mutation = useMutation<UserProfile, Error, number, { previous: UserProfile | undefined }>({
    mutationKey: ["me", "preferences"],
    mutationFn: async (cycleDefaultDurationDays: number): Promise<UserProfile> => {
      const { data, error } = await apiClient.PATCH("/api/users/me/preferences", {
        body: { cycleDefaultDurationDays },
      });
      if (error || !data) throw errorFrom(error);
      return data;
    },
    onMutate: async (cycleDefaultDurationDays) => {
      await queryClient.cancelQueries({ queryKey: ME_QUERY_KEY });
      const previous = queryClient.getQueryData<UserProfile>(ME_QUERY_KEY);
      queryClient.setQueryData<UserProfile>(ME_QUERY_KEY, (old) =>
        old ? { ...old, cycleDefaultDurationDays } : old,
      );
      return { previous };
    },
    onError: (_error, _vars, context) => {
      if (context) queryClient.setQueryData(ME_QUERY_KEY, context.previous);
    },
    onSettled: async () => {
      await queryClient.invalidateQueries({ queryKey: ME_QUERY_KEY });
    },
  });

  return {
    /** Persists the default cycle duration (1..90 — server-validated) and announces the save. */
    setCycleDefaultDuration: async (days: number): Promise<void> => {
      await mutation.mutateAsync(days);
      push("Zapisano ustawienia.", { variant: "success" });
    },
  };
}

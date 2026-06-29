"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";

import { apiClient, mapError, type ProblemDetails } from "@/lib/api/client";
import type { components } from "@/lib/api/generated/schema";
import { newLabelId } from "@/lib/id";
import { TASKS_QUERY_KEY } from "@/hooks/useTasks";

export type LabelResponse = components["schemas"]["LabelResponse"];

/** The TanStack Query key for the caller's label roster (slice 006). */
export const LABELS_QUERY_KEY = ["labels"] as const;

function errorFrom(error: unknown): Error {
  return new Error(mapError((error as ProblemDetails | undefined)?.errorCode).message);
}

function sortByName(labels: LabelResponse[]): LabelResponse[] {
  return [...labels].sort((a, b) => a.name.localeCompare(b.name));
}

/**
 * The caller's label roster (slice 006, R6). Reads `GET /api/labels` — the per-user labels (Tier A),
 * ordered by name. Drives the label selector's options and the name/color for the row chips. Shared,
 * deduped query (the chips read it from cache, so many rows do not refetch).
 */
export function useLabelRoster() {
  return useQuery<LabelResponse[]>({
    queryKey: LABELS_QUERY_KEY,
    queryFn: async (): Promise<LabelResponse[]> => {
      const { data, error } = await apiClient.GET("/api/labels");
      if (error || !data) throw errorFrom(error);
      return data;
    },
  });
}

/**
 * Label create + delete mutations (slice 006). Create is a client-id idempotent PUT-upsert with an optimistic
 * roster insert (SC-003); delete patches the roster optimistically and invalidates the task caches so the
 * deleted label drops from every row's chips (the server FK cascade removed its applications). Rename/recolor
 * (UpdateLabel) are backend-complete + tested but have NO UI this slice (the chosen scope — research R11), so
 * no web mutation is wired for them.
 */
export function useLabelMutations() {
  const queryClient = useQueryClient();

  const createMutation = useMutation<LabelResponse, Error, { id: string; name: string; color?: string | null }, { previous: LabelResponse[] | undefined }>({
    mutationFn: async ({ id, name, color }): Promise<LabelResponse> => {
      const { data, error } = await apiClient.PUT("/api/labels/{id}", {
        params: { path: { id } },
        body: { name, color: color ?? null },
      });
      if (error || !data) throw errorFrom(error);
      return data;
    },
    onMutate: async ({ id, name, color }) => {
      await queryClient.cancelQueries({ queryKey: LABELS_QUERY_KEY }); // stop an in-flight refetch clobbering the insert
      const previous = queryClient.getQueryData<LabelResponse[]>(LABELS_QUERY_KEY);
      // Optimistic roster insert (R11) with the SAME client id the PUT upserts, so the placeholder and the
      // server row share identity (onSettled re-fetches to reconcile name/color).
      queryClient.setQueryData<LabelResponse[]>(LABELS_QUERY_KEY, (old) =>
        sortByName([...(old ?? []), { id, name, color: color ?? null }]),
      );
      return { previous };
    },
    onError: (_error, _vars, context) => {
      if (context) queryClient.setQueryData(LABELS_QUERY_KEY, context.previous);
    },
    onSettled: () => {
      void queryClient.invalidateQueries({ queryKey: LABELS_QUERY_KEY });
    },
  });

  const deleteMutation = useMutation<void, Error, { id: string }, { previous: LabelResponse[] | undefined }>({
    mutationFn: async ({ id }): Promise<void> => {
      const { error } = await apiClient.DELETE("/api/labels/{id}", { params: { path: { id } } });
      if (error) throw errorFrom(error);
    },
    onMutate: async ({ id }) => {
      await queryClient.cancelQueries({ queryKey: LABELS_QUERY_KEY }); // stop an in-flight refetch clobbering the removal
      const previous = queryClient.getQueryData<LabelResponse[]>(LABELS_QUERY_KEY);
      queryClient.setQueryData<LabelResponse[]>(LABELS_QUERY_KEY, (old) => (old ?? []).filter((l) => l.id !== id));
      return { previous };
    },
    onError: (_error, _vars, context) => {
      if (context) queryClient.setQueryData(LABELS_QUERY_KEY, context.previous);
    },
    onSettled: () => {
      void queryClient.invalidateQueries({ queryKey: LABELS_QUERY_KEY });
      // The FK cascade removed the label's task applications — refetch the task caches so the deleted label
      // drops from every row's chips (a PREFIX invalidate also covers ['tasks','assigned']).
      void queryClient.invalidateQueries({ queryKey: TASKS_QUERY_KEY });
    },
  });

  return {
    /** Creates a label (client-id idempotent upsert) and returns it — the selector adds its id to the set. */
    createLabel: (name: string, color?: string | null) => createMutation.mutateAsync({ id: newLabelId(), name, color }),
    /** Hard-deletes a label (the server cascade clears its task applications). */
    deleteLabel: (id: string) => deleteMutation.mutateAsync({ id }),
  };
}

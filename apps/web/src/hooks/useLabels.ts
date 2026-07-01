"use client";

import { type QueryClient, useMutation, useQuery, useQueryClient } from "@tanstack/react-query";

import { apiClient, mapError, type ProblemDetails } from "@/lib/api/client";
import type { components } from "@/lib/api/generated/schema";
import { newLabelId } from "@/lib/id";
import { TASKS_QUERY_KEY } from "@/hooks/useTasks";
import { TODAY_QUERY_KEY } from "@/hooks/useTodayTasks";
import { UPCOMING_QUERY_KEY } from "@/hooks/useUpcomingTasks";

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

/** Context handed from `onMutate` to `onError`/`onSettled` — the pre-mutation roster snapshot for rollback. */
interface LabelRosterContext {
  previous: LabelResponse[] | undefined;
}

export interface CreateLabelVariables {
  id: string;
  name: string;
  color?: string | null;
}

interface CreateLabelOptions {
  mutationFn: (variables: CreateLabelVariables) => Promise<LabelResponse>;
  onMutate: (variables: CreateLabelVariables) => Promise<LabelRosterContext>;
  onError: (error: Error, variables: CreateLabelVariables, context: LabelRosterContext | undefined) => void;
  onSettled: () => void;
}

/**
 * Optimistic label CREATE recipe (slice 006). A client-id idempotent PUT-upsert with an optimistic roster
 * insert (SC-003): the placeholder carries the SAME client id the PUT upserts, so it and the server row share
 * identity; `onSettled` re-fetches to reconcile the server-canonical name/color.
 */
export function createLabelMutationOptions(queryClient: QueryClient): CreateLabelOptions {
  return {
    mutationFn: async ({ id, name, color }: CreateLabelVariables): Promise<LabelResponse> => {
      const { data, error } = await apiClient.PUT("/api/labels/{id}", {
        params: { path: { id } },
        body: { name, color: color ?? null },
      });
      if (error || !data) throw errorFrom(error);
      return data;
    },
    onMutate: async ({ id, name, color }: CreateLabelVariables): Promise<LabelRosterContext> => {
      await queryClient.cancelQueries({ queryKey: LABELS_QUERY_KEY }); // stop an in-flight refetch clobbering the insert
      const previous = queryClient.getQueryData<LabelResponse[]>(LABELS_QUERY_KEY);
      queryClient.setQueryData<LabelResponse[]>(LABELS_QUERY_KEY, (old) =>
        sortByName([...(old ?? []), { id, name, color: color ?? null }]),
      );
      return { previous };
    },
    onError: (_error, _vars, context): void => {
      if (context) queryClient.setQueryData(LABELS_QUERY_KEY, context.previous);
    },
    onSettled: (): void => {
      void queryClient.invalidateQueries({ queryKey: LABELS_QUERY_KEY });
    },
  };
}

export interface DeleteLabelVariables {
  id: string;
}

interface DeleteLabelOptions {
  mutationFn: (variables: DeleteLabelVariables) => Promise<void>;
  onMutate: (variables: DeleteLabelVariables) => Promise<LabelRosterContext>;
  onError: (error: Error, variables: DeleteLabelVariables, context: LabelRosterContext | undefined) => void;
  onSettled: () => void;
}

/**
 * Optimistic label DELETE recipe (slice 006). Patches the roster optimistically, then on settle reconciles
 * the roster AND every task cache the server FK cascade touched — so the deleted id drops from every row's
 * chips and is never re-submitted by a later setTaskLabels.
 */
export function deleteLabelMutationOptions(queryClient: QueryClient): DeleteLabelOptions {
  return {
    mutationFn: async ({ id }: DeleteLabelVariables): Promise<void> => {
      const { error } = await apiClient.DELETE("/api/labels/{id}", { params: { path: { id } } });
      if (error) throw errorFrom(error);
    },
    onMutate: async ({ id }: DeleteLabelVariables): Promise<LabelRosterContext> => {
      await queryClient.cancelQueries({ queryKey: LABELS_QUERY_KEY }); // stop an in-flight refetch clobbering the removal
      const previous = queryClient.getQueryData<LabelResponse[]>(LABELS_QUERY_KEY);
      queryClient.setQueryData<LabelResponse[]>(LABELS_QUERY_KEY, (old) => (old ?? []).filter((l) => l.id !== id));
      return { previous };
    },
    onError: (_error, _vars, context): void => {
      if (context) queryClient.setQueryData(LABELS_QUERY_KEY, context.previous);
    },
    onSettled: (): void => {
      void queryClient.invalidateQueries({ queryKey: LABELS_QUERY_KEY });
      // The FK cascade removed the label's task applications server-side — refetch EVERY task cache that can
      // hold the label so the deleted id drops from every row (and is never re-submitted by a later
      // setTaskLabels). ['tasks'] is a PREFIX invalidate (also covers ['tasks','assigned']); the grouped
      // Today/Upcoming caches are separate keys that the prefix does NOT cover, so invalidate them too —
      // otherwise a dated task there keeps the dead id and a subsequent label save re-sends it → 422.
      void queryClient.invalidateQueries({ queryKey: TASKS_QUERY_KEY });
      void queryClient.invalidateQueries({ queryKey: TODAY_QUERY_KEY });
      void queryClient.invalidateQueries({ queryKey: UPCOMING_QUERY_KEY });
    },
  };
}

/**
 * Label create + delete mutations (slice 006). Rename/recolor (UpdateLabel) are backend-complete + tested but
 * have NO UI this slice (the chosen scope — research R11), so no web mutation is wired for them.
 */
export function useLabelMutations() {
  const queryClient = useQueryClient();

  const createMutation = useMutation<LabelResponse, Error, CreateLabelVariables, LabelRosterContext>(
    createLabelMutationOptions(queryClient),
  );
  const deleteMutation = useMutation<void, Error, DeleteLabelVariables, LabelRosterContext>(
    deleteLabelMutationOptions(queryClient),
  );

  return {
    /** Creates a label (client-id idempotent upsert) and returns it — the selector adds its id to the set. */
    createLabel: (name: string, color?: string | null) => createMutation.mutateAsync({ id: newLabelId(), name, color }),
    /** Hard-deletes a label (the server cascade clears its task applications). */
    deleteLabel: (id: string) => deleteMutation.mutateAsync({ id }),
  };
}

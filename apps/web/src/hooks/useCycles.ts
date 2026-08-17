"use client";

import { type QueryClient, useMutation, useQuery, useQueryClient } from "@tanstack/react-query";

import { useToast } from "@/components/ui/Toast";
import { apiClient, mapError, type ProblemDetails } from "@/lib/api/client";
import type { components } from "@/lib/api/generated/schema";
import { orderCycles, type CycleResponse } from "@/lib/cycles";
import { newCycleId } from "@/lib/id";
import { TASKS_QUERY_KEY } from "@/hooks/useTasks";
import { TODAY_QUERY_KEY } from "@/hooks/useTodayTasks";
import { UPCOMING_QUERY_KEY } from "@/hooks/useUpcomingTasks";

export type { CycleResponse } from "@/lib/cycles";
export type CloseCycleResponse = components["schemas"]["CloseCycleResponse"];
type TaskResponse = components["schemas"]["TaskResponse"];
type RolloverChoice = "next" | "backlog" | "keep";

/** The TanStack Query key for the team-wide cycle list + metrics (slice 011, D6). */
export const CYCLES_QUERY_KEY = ["cycles"] as const;

/** The per-cycle caller-visible task rows (D10). */
export function cycleTasksQueryKey(cycleId: string): readonly [string, string] {
  return ["cycle-tasks", cycleId] as const;
}

function errorFrom(error: unknown): Error {
  return new Error(mapError((error as ProblemDetails | undefined)?.errorCode).message);
}

/**
 * The team-wide cycle list with computed metrics (`GET /api/cycles`, D5-ordered, D6). Drives the
 * `/cycle` switcher + metrics, the sidebar active-cycle entry, the CyclePicker options and the
 * by-cycle grouping — one shared, deduped query.
 */
export function useCycles() {
  return useQuery<CycleResponse[]>({
    queryKey: CYCLES_QUERY_KEY,
    queryFn: async (): Promise<CycleResponse[]> => {
      const { data, error } = await apiClient.GET("/api/cycles");
      if (error || !data) throw errorFrom(error);
      return data;
    },
  });
}

/** The selected cycle's caller-visible task rows (`GET /api/cycles/{id}/tasks`, D10; EC-12 archived included). */
export function useCycleTasks(cycleId: string | null) {
  return useQuery<TaskResponse[]>({
    queryKey: cycleId ? cycleTasksQueryKey(cycleId) : ["cycle-tasks", "none"],
    enabled: cycleId !== null,
    queryFn: async (): Promise<TaskResponse[]> => {
      const { data, error } = await apiClient.GET("/api/cycles/{id}/tasks", {
        params: { path: { id: cycleId! } },
      });
      if (error || !data) throw errorFrom(error);
      return data;
    },
  });
}

/** Context handed from `onMutate` to `onError` — the pre-mutation cycle-list snapshot for rollback. */
export interface CycleListContext {
  previous: CycleResponse[] | undefined;
}

async function snapshotCycles(queryClient: QueryClient): Promise<CycleListContext> {
  // Stop in-flight refetches so they can't clobber the optimistic write.
  await queryClient.cancelQueries({ queryKey: CYCLES_QUERY_KEY });
  return { previous: queryClient.getQueryData<CycleResponse[]>(CYCLES_QUERY_KEY) };
}

function rollbackCycles(queryClient: QueryClient, context: CycleListContext | undefined): void {
  if (context) queryClient.setQueryData(CYCLES_QUERY_KEY, context.previous);
}

async function settleCycles(queryClient: QueryClient): Promise<void> {
  await queryClient.invalidateQueries({ queryKey: CYCLES_QUERY_KEY });
  await queryClient.invalidateQueries({ queryKey: ["cycle-tasks"] });
}

const EMPTY_METRICS: CycleResponse["metrics"] = {
  total: 0,
  done: 0,
  breakdown: { backlog: 0, todo: 0, in_progress: 0, done: 0, cancelled: 0 },
};

export interface CreateCycleVariables {
  id: string;
  name: string;
  /** ISO UTC instant (the Warsaw-midnight date-only convention, D18). */
  startDate: string;
  endDate: string;
}

interface CycleMutationOptions<TVariables, TData = CycleResponse> {
  mutationFn: (variables: TVariables) => Promise<TData>;
  onMutate: (variables: TVariables) => Promise<CycleListContext>;
  onError: (error: Error, variables: TVariables, context: CycleListContext | undefined) => void;
  onSettled: () => Promise<void>;
}

/**
 * Optimistic cycle CREATE recipe (slice 011): a client-id idempotent PUT with an optimistic D5-ordered
 * list insert — the placeholder carries the SAME client id the PUT upserts, so it and the server row
 * share identity; `onSettled` reconciles the server-canonical row (createdAt, trimmed name).
 */
export function createCycleMutationOptions(queryClient: QueryClient): CycleMutationOptions<CreateCycleVariables> {
  return {
    mutationFn: async ({ id, name, startDate, endDate }: CreateCycleVariables): Promise<CycleResponse> => {
      const { data, error } = await apiClient.PUT("/api/cycles/{id}", {
        params: { path: { id } },
        body: { name, startDate, endDate },
      });
      if (error || !data) throw errorFrom(error);
      return data;
    },
    onMutate: async ({ id, name, startDate, endDate }: CreateCycleVariables): Promise<CycleListContext> => {
      const context = await snapshotCycles(queryClient);
      const optimistic: CycleResponse = {
        id,
        name,
        startDate,
        endDate,
        status: "planned",
        version: 0,
        createdAt: new Date().toISOString(),
        metrics: EMPTY_METRICS,
      };
      queryClient.setQueryData<CycleResponse[]>(CYCLES_QUERY_KEY, (old) => orderCycles([...(old ?? []), optimistic]));
      return context;
    },
    onError: (_error, _vars, context): void => {
      rollbackCycles(queryClient, context);
    },
    onSettled: () => settleCycles(queryClient),
  };
}

export interface EditCycleVariables {
  id: string;
  name: string;
  startDate: string;
  endDate: string;
  version: number;
}

/** Optimistic cycle EDIT recipe: re-stamps name/dates in place (re-sorted — dates drive the D5 order). */
export function editCycleMutationOptions(queryClient: QueryClient): CycleMutationOptions<EditCycleVariables> {
  return {
    mutationFn: async ({ id, name, startDate, endDate, version }: EditCycleVariables): Promise<CycleResponse> => {
      const { data, error } = await apiClient.PATCH("/api/cycles/{id}", {
        params: { path: { id } },
        body: { name, startDate, endDate, version },
      });
      if (error || !data) throw errorFrom(error);
      return data;
    },
    onMutate: async ({ id, name, startDate, endDate }: EditCycleVariables): Promise<CycleListContext> => {
      const context = await snapshotCycles(queryClient);
      queryClient.setQueryData<CycleResponse[]>(CYCLES_QUERY_KEY, (old) =>
        old ? orderCycles(old.map((c) => (c.id === id ? { ...c, name, startDate, endDate } : c))) : old,
      );
      return context;
    },
    onError: (_error, _vars, context): void => {
      rollbackCycles(queryClient, context);
    },
    onSettled: () => settleCycles(queryClient),
  };
}

export interface CycleIdVersionVariables {
  id: string;
  version: number;
}

/** Optimistic ACTIVATE recipe (D3): flips the target to `active` (the single-active invariant is server-guarded). */
export function activateCycleMutationOptions(queryClient: QueryClient): CycleMutationOptions<CycleIdVersionVariables> {
  return {
    mutationFn: async ({ id, version }: CycleIdVersionVariables): Promise<CycleResponse> => {
      const { data, error } = await apiClient.PATCH("/api/cycles/{id}/activate", {
        params: { path: { id } },
        body: { version },
      });
      if (error || !data) throw errorFrom(error);
      return data;
    },
    onMutate: async ({ id }: CycleIdVersionVariables): Promise<CycleListContext> => {
      const context = await snapshotCycles(queryClient);
      queryClient.setQueryData<CycleResponse[]>(CYCLES_QUERY_KEY, (old) =>
        old?.map((c) => (c.id === id ? { ...c, status: "active" } : c)),
      );
      return context;
    },
    onError: (_error, _vars, context): void => {
      rollbackCycles(queryClient, context);
    },
    onSettled: () => settleCycles(queryClient),
  };
}

export interface CloseCycleVariables {
  id: string;
  rollover?: RolloverChoice;
  overrides?: { taskId: string; choice: RolloverChoice }[];
  version: number;
}

/**
 * The close review commit (D4): ONE transactional PATCH carrying the bulk rollover + per-task
 * overrides; the response's counts feed the confirmation toast/announcement (FR-101). The
 * optimistic flip only marks the cycle closed — the task moves are server-resolved and reconciled
 * by the settle invalidations (every task cache the rollover may have touched).
 */
export function closeCycleMutationOptions(
  queryClient: QueryClient,
): CycleMutationOptions<CloseCycleVariables, CloseCycleResponse> {
  return {
    mutationFn: async ({ id, rollover, overrides, version }: CloseCycleVariables): Promise<CloseCycleResponse> => {
      const { data, error } = await apiClient.PATCH("/api/cycles/{id}/close", {
        params: { path: { id } },
        body: { rollover: rollover ?? null, overrides: overrides ?? null, version },
      });
      if (error || !data) throw errorFrom(error);
      return data;
    },
    onMutate: async ({ id }: CloseCycleVariables): Promise<CycleListContext> => {
      const context = await snapshotCycles(queryClient);
      queryClient.setQueryData<CycleResponse[]>(CYCLES_QUERY_KEY, (old) =>
        old?.map((c) => (c.id === id ? { ...c, status: "closed" } : c)),
      );
      return context;
    },
    onError: (_error, _vars, context): void => {
      rollbackCycles(queryClient, context);
    },
    onSettled: async (): Promise<void> => {
      await settleCycles(queryClient);
      // The rollover rewrote cycleId/carriedOver on tasks server-side — reconcile every task
      // cache that can render them: the ['tasks'] prefix (incl. Assigned), the grouped daily
      // views, and the project lists the by-cycle grouping reads.
      await queryClient.invalidateQueries({ queryKey: TASKS_QUERY_KEY });
      await queryClient.invalidateQueries({ queryKey: TODAY_QUERY_KEY });
      await queryClient.invalidateQueries({ queryKey: UPCOMING_QUERY_KEY });
      await queryClient.invalidateQueries({ queryKey: ["projects"] });
    },
  };
}

/** Optimistic DELETE recipe (FR-020 guards are server-side; the refused delete rolls back). */
export function deleteCycleMutationOptions(queryClient: QueryClient): CycleMutationOptions<CycleIdVersionVariables, void> {
  return {
    mutationFn: async ({ id, version }: CycleIdVersionVariables): Promise<void> => {
      const { error } = await apiClient.DELETE("/api/cycles/{id}", {
        params: { path: { id }, query: { version } },
      });
      if (error) throw errorFrom(error);
    },
    onMutate: async ({ id }: CycleIdVersionVariables): Promise<CycleListContext> => {
      const context = await snapshotCycles(queryClient);
      queryClient.setQueryData<CycleResponse[]>(CYCLES_QUERY_KEY, (old) => old?.filter((c) => c.id !== id));
      return context;
    },
    onError: (_error, _vars, context): void => {
      rollbackCycles(queryClient, context);
    },
    onSettled: () => settleCycles(queryClient),
  };
}

/** The Polish close-confirmation copy carrying the returned counts (FR-101). */
export function closeCycleAnnouncement(result: CloseCycleResponse): string {
  const parts: string[] = [];
  if (result.rolledToNext > 0) parts.push(`przeniesione do następnego cyklu: ${result.rolledToNext}`);
  if (result.rolledToBacklog > 0) parts.push(`przeniesione do backlogu: ${result.rolledToBacklog}`);
  if (result.kept > 0) parts.push(`pozostawione jako „przeniesione”: ${result.kept}`);
  return parts.length > 0 ? `Cykl zamknięty — ${parts.join(", ")}.` : "Cykl zamknięty.";
}

/**
 * "use client" cycle lifecycle mutations (slice 011). Each success pushes a Polish confirmation
 * toast, which the ToastProvider routes through the single polite LiveRegion (FR-101); failures
 * announce + log through the global MutationCache announcer (FR-049/FR-050) and roll back.
 */
export function useCycleMutations() {
  const queryClient = useQueryClient();
  const { push } = useToast();

  const createMutation = useMutation<CycleResponse, Error, CreateCycleVariables, CycleListContext>({
    ...createCycleMutationOptions(queryClient),
    mutationKey: ["cycles", "create"],
  });
  const editMutation = useMutation<CycleResponse, Error, EditCycleVariables, CycleListContext>({
    ...editCycleMutationOptions(queryClient),
    mutationKey: ["cycles", "edit"],
  });
  const activateMutation = useMutation<CycleResponse, Error, CycleIdVersionVariables, CycleListContext>({
    ...activateCycleMutationOptions(queryClient),
    mutationKey: ["cycles", "activate"],
  });
  const closeMutation = useMutation<CloseCycleResponse, Error, CloseCycleVariables, CycleListContext>({
    ...closeCycleMutationOptions(queryClient),
    mutationKey: ["cycles", "close"],
  });
  const deleteMutation = useMutation<void, Error, CycleIdVersionVariables, CycleListContext>({
    ...deleteCycleMutationOptions(queryClient),
    mutationKey: ["cycles", "delete"],
  });

  return {
    /** Creates a planned cycle (client-minted UUIDv7, idempotent PUT) and returns it. */
    createCycle: async (name: string, startDate: string, endDate: string): Promise<CycleResponse> => {
      const created = await createMutation.mutateAsync({ id: newCycleId(), name, startDate, endDate });
      push("Utworzono cykl.", { variant: "success" });
      return created;
    },
    /** Edits name/dates (any status) under OCC. */
    editCycle: async (variables: EditCycleVariables): Promise<CycleResponse> => {
      const edited = await editMutation.mutateAsync(variables);
      push("Zapisano zmiany cyklu.", { variant: "success" });
      return edited;
    },
    /** Activates a planned cycle (single-active guarded server-side). */
    activateCycle: async (id: string, version: number): Promise<CycleResponse> => {
      const activated = await activateMutation.mutateAsync({ id, version });
      push("Aktywowano cykl.", { variant: "success" });
      return activated;
    },
    /** Commits the close review (close + rollover, one transaction); announces the counts. */
    closeCycle: async (variables: CloseCycleVariables): Promise<CloseCycleResponse> => {
      const result = await closeMutation.mutateAsync(variables);
      push(closeCycleAnnouncement(result), { variant: "success" });
      return result;
    },
    /** Hard-deletes an empty planned/closed cycle (server guards refuse otherwise). */
    deleteCycle: async (id: string, version: number): Promise<void> => {
      await deleteMutation.mutateAsync({ id, version });
      push("Usunięto cykl.", { variant: "success" });
    },
  };
}

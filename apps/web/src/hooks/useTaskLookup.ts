"use client";

import { useQueryClient, type QueryClient } from "@tanstack/react-query";
import { useCallback, useSyncExternalStore } from "react";
import type { AssignedResponse } from "@/hooks/useAssignedTasks";
import type { TodayResponse } from "@/hooks/useTodayTasks";
import type { UpcomingResponse } from "@/hooks/useUpcomingTasks";
import type { TaskResponse } from "@/hooks/useTasks";

function findAnywhere(queryClient: QueryClient, taskId: string): TaskResponse | undefined {
  const inbox = queryClient.getQueryData<TaskResponse[]>(["tasks"])?.find((t) => t.id === taskId);
  if (inbox) return inbox;
  const today = queryClient
    .getQueryData<TodayResponse>(["tasks", "today"])
    ?.groups.flatMap((g) => g.tasks)
    .find((t) => t.id === taskId);
  if (today) return today;
  const upcoming = queryClient
    .getQueryData<UpcomingResponse>(["tasks", "upcoming"])
    ?.groups.flatMap((g) => g.tasks)
    .find((t) => t.id === taskId);
  if (upcoming) return upcoming;
  const assigned = queryClient
    .getQueryData<AssignedResponse>(["tasks", "assigned"])
    ?.groups.flatMap((g) => g.tasks)
    .find((t) => t.id === taskId);
  if (assigned) return assigned;
  for (const [key, data] of queryClient.getQueriesData<TaskResponse[]>({ queryKey: ["projects"] })) {
    if (key.length === 3 && key[2] === "tasks") {
      const row = data?.find((t) => t.id === taskId);
      if (row) return row;
    }
  }
  return undefined;
}

/**
 * Resolves a task row by id across EVERY listing cache the current route may have loaded
 * (Inbox / Today / Upcoming / Assigned / project lists) — the drawer's data source (T051).
 * Subscribes to the query cache, so optimistic updates re-render the drawer instantly.
 * Returns `undefined` while nothing has loaded the id — after the route's listing settles
 * that means an invalid/inaccessible id (the drawer's FR-049 error state).
 */
export function useTaskLookup(taskId: string | null): TaskResponse | undefined {
  const queryClient = useQueryClient();

  const subscribe = useCallback(
    (onStoreChange: () => void) => queryClient.getQueryCache().subscribe(onStoreChange),
    [queryClient],
  );
  const getSnapshot = useCallback(
    () => (taskId === null ? undefined : findAnywhere(queryClient, taskId)),
    [queryClient, taskId],
  );

  return useSyncExternalStore(subscribe, getSnapshot, getSnapshot);
}

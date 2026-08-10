"use client";

import { useMemo } from "react";

import { DailyView, type DailyGroup } from "@/components/tasks/DailyView";
import { Button } from "@/components/ui/Button";
import { ErrorState } from "@/components/ui/ErrorState";
import { useProjects } from "@/hooks/useProjects";
import { useUpcomingTasks } from "@/hooks/useUpcomingTasks";

/**
 * The Upcoming view (slice 005, US-08.AS-02). Renders the server-grouped, R5-ordered Upcoming read model
 * through the shared {@link DailyView}: one group per Warsaw calendar day (ascending), each row showing its
 * priority badge. The server owns the 7-day window, the day grouping, and the order.
 */
export function UpcomingView() {
  const { data, isError, isPending, refetch } = useUpcomingTasks();
  const { data: projects } = useProjects();

  const projectNames = useMemo(() => {
    const map = new Map<string, string>();
    for (const p of projects ?? []) map.set(p.id, p.name);
    return map;
  }, [projects]);

  const projectName = (projectId: string | null | undefined): string | null =>
    projectId == null ? null : (projectNames.get(projectId) ?? null);

  const groups: DailyGroup[] = (data?.groups ?? []).map((g) => ({
    key: g.date,
    label: g.date,
    tasks: g.tasks,
  }));

  if (isError) {
    return (
      <ErrorState
        message="Nie udało się wczytać widoku Nadchodzące."
        action={
          <Button variant="secondary" onClick={() => void refetch()}>
            Spróbuj ponownie
          </Button>
        }
      />
    );
  }

  return (
    <DailyView
      label="Nadchodzące"
      groups={groups}
      projectName={projectName}
      loading={isPending}
      emptyMessage="Brak zadań w najbliższych 7 dniach."
    />
  );
}

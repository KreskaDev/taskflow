"use client";

import { useMemo } from "react";

import { DailyView, type DailyGroup } from "@/components/tasks/DailyView";
import { useProjects } from "@/hooks/useProjects";
import { useUpcomingTasks } from "@/hooks/useUpcomingTasks";

/**
 * The Upcoming view (slice 005, US-08.AS-02). Renders the server-grouped, R5-ordered Upcoming read model
 * through the shared {@link DailyView}: one group per Warsaw calendar day (ascending), each row showing its
 * priority badge. The server owns the 7-day window, the day grouping, and the order.
 */
export function UpcomingView() {
  const { data, isError, refetch } = useUpcomingTasks();
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
      <div className="tf-daily-view" role="alert">
        <p>Nie udało się wczytać widoku Nadchodzące.</p>
        <button type="button" className="tf-button" onClick={() => void refetch()}>
          Spróbuj ponownie
        </button>
      </div>
    );
  }

  return (
    <DailyView
      label="Nadchodzące"
      groups={groups}
      projectName={projectName}
      emptyMessage="Brak zadań w najbliższych 7 dniach."
    />
  );
}

"use client";

import { useMemo } from "react";

import { DailyView, type DailyGroup } from "@/components/tasks/DailyView";
import { useProjects } from "@/hooks/useProjects";
import { useTodayTasks } from "@/hooks/useTodayTasks";

/**
 * The Today view (slice 005, AS-01/AS-02). Renders the server-grouped, R5-ordered Today read model through
 * the shared {@link DailyView}: project groups (Inbox first), each row showing its priority badge and an
 * overdue flag. The server owns the Warsaw day boundary, grouping, and order — this component only resolves
 * project ids to names and maps the wire groups to the view's shape.
 */
export function TodayView() {
  const { data, isError, refetch } = useTodayTasks();
  const { data: projects } = useProjects();

  const projectNames = useMemo(() => {
    const map = new Map<string, string>();
    for (const p of projects ?? []) map.set(p.id, p.name);
    return map;
  }, [projects]);

  const projectName = (projectId: string | null | undefined): string | null =>
    projectId == null ? null : (projectNames.get(projectId) ?? null);

  const groups: DailyGroup[] = (data?.groups ?? []).map((g) => ({
    key: g.projectId ?? "inbox",
    label: g.projectId == null ? "Inbox" : (projectNames.get(g.projectId) ?? "Projekt"),
    tasks: g.tasks,
  }));

  if (isError) {
    return (
      <div className="tf-daily-view" role="alert">
        <p>Nie udało się wczytać widoku Dziś.</p>
        <button type="button" className="tf-button" onClick={() => void refetch()}>
          Spróbuj ponownie
        </button>
      </div>
    );
  }

  return (
    <DailyView
      label="Dziś"
      groups={groups}
      projectName={projectName}
      emptyMessage="Brak zadań na dziś. Naciśnij C, aby dodać zadanie."
    />
  );
}

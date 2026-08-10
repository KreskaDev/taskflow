"use client";

import { useMemo } from "react";

import { DailyView, type DailyGroup } from "@/components/tasks/DailyView";
import { Button } from "@/components/ui/Button";
import { ErrorState } from "@/components/ui/ErrorState";
import { useAssignedTasks } from "@/hooks/useAssignedTasks";
import { useProjects } from "@/hooks/useProjects";

/**
 * The "Assigned to me" view (slice 008, AS-03/FR-071). Renders the server-grouped read model (tasks across
 * shared projects where the caller is a current member/owner AND an assignee) through the shared
 * {@link DailyView} — grouped by project, R5-ordered, with the same keyboard operate verbs. The server scopes
 * and orders; this component resolves project ids to names.
 */
export function AssignedView() {
  const { data, isError, isPending, refetch } = useAssignedTasks();
  const { data: projects } = useProjects();

  const projectNames = useMemo(() => {
    const map = new Map<string, string>();
    for (const p of projects ?? []) map.set(p.id, p.name);
    return map;
  }, [projects]);

  const projectName = (projectId: string | null | undefined): string | null =>
    projectId == null ? null : (projectNames.get(projectId) ?? null);

  const groups: DailyGroup[] = (data?.groups ?? []).map((g) => ({
    key: g.projectId,
    label: projectNames.get(g.projectId) ?? "Projekt",
    tasks: g.tasks,
  }));

  if (isError) {
    return (
      <ErrorState
        message="Nie udało się wczytać widoku Przypisane do mnie."
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
      label="Przypisane do mnie"
      groups={groups}
      projectName={projectName}
      loading={isPending}
      emptyMessage="Brak zadań przypisanych do Ciebie."
    />
  );
}

"use client";

import { use, useState } from "react";

import { ProjectSelector } from "@/components/projects/ProjectSelector";
import { useProjects } from "@/hooks/useProjects";
import { useProjectTasks } from "@/hooks/useProjectTasks";
import { useTaskMutations } from "@/hooks/useTaskMutations";

/**
 * The project-tasks view (T051; FR-021/R6/R7/R16). Lists the tasks of one project via
 * {@link useProjectTasks} and lets the user move any row to another project — or back to the Inbox
 * (`projectId = null`) — through the {@link ProjectSelector}. A projected task is, by FR-021, absent
 * from the Inbox (`/`), so this is the surface its row + move affordance live on. The move rides the
 * shared optimistic recipe ({@link useTaskMutations}.moveTaskToProject) keyed on this project's
 * cache, so a move out of here removes the row optimistically.
 *
 * `params` is a Promise in the Next 15 App Router; unwrapped with React `use()`. The project NAME is
 * rendered as a React text node (escaped, FR-099).
 */
export default function ProjectView({ params }: { params: Promise<{ id: string }> }) {
  const { id } = use(params);
  const { data: projects } = useProjects();
  const project = (projects ?? []).find((p) => p.id === id);
  const { data: tasks, isPending } = useProjectTasks(id);
  const { moveTaskToProject } = useTaskMutations();
  const [movingId, setMovingId] = useState<string | null>(null);

  const rows = tasks ?? [];

  return (
    <section aria-labelledby="project-heading" className="tf-workspace">
      <h1 id="project-heading">{project?.name ?? "Project"}</h1>

      {isPending ? (
        <p className="tf-workspace__empty">Loading…</p>
      ) : rows.length === 0 ? (
        <p className="tf-workspace__empty">No tasks in this project yet.</p>
      ) : (
        <ul role="list" className="tf-project-tasks">
          {rows.map((task) => (
            <li key={task.id} className="tf-project-tasks__row">
              <span className="tf-project-tasks__title">{task.title}</span>
              {/* The pointer move affordance (the bare `M` shortcut targets the Inbox selection).
                  Its accessible name names the task so AT users hear which row moves (FR-043). */}
              <button
                type="button"
                className="tf-task-row__project"
                aria-label={`Move ${task.title} to another project`}
                onClick={() => setMovingId(task.id)}
              >
                Move to another project
              </button>
            </li>
          ))}
        </ul>
      )}

      <ProjectSelector
        open={movingId !== null}
        onClose={() => setMovingId(null)}
        task={rows.find((t) => t.id === movingId)}
        // The source is THIS project; choosing Inbox (null) returns the task to the Inbox (R6/R7).
        onSelect={(projectId) => {
          if (movingId !== null) moveTaskToProject(movingId, projectId, id);
        }}
      />
    </section>
  );
}

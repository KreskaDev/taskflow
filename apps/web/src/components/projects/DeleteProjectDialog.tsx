"use client";

import { useState } from "react";

import { Button } from "@/components/ui/Button";
import { Dialog, DialogActions, DialogChoices, DialogTitle } from "@/components/ui/Dialog";
import { useProjectMutations } from "@/hooks/useProjectMutations";
import type { ProjectResponse } from "@/hooks/useProjects";
import type { ChildDisposition, TaskDisposition } from "@/lib/validation/project";

const TITLE_ID = "delete-project-title";
const DESC_ID = "delete-project-desc";

interface DeleteProjectDialogProps {
  open: boolean;
  onClose: () => void;
  /** The project being deleted; drives the blast-radius copy and the disposition prompts. */
  project: ProjectResponse;
  /** Count of tasks directly in this project (drives the task-disposition prompt + blast radius). */
  taskCount: number;
  /** Count of direct child projects (drives the child-disposition prompt + blast radius). */
  childCount: number;
  /**
   * True while the task count is still loading. The confirm button is disabled until it resolves so
   * a delete can never be confirmed before the (possibly required) task disposition is shown — which
   * would send a missing disposition and 422 on the server (a confusing optimistic-vanish flash).
   */
  busy?: boolean;
}

/**
 * The delete-with-dispositions dialog (T030; FR-014/EC-03/AS-10, Principle VII, research R5). A modal
 * (FR-101 focus contract via {@link Dialog}) that states its BLAST RADIUS (the count of affected tasks
 * and child projects) and collects the caller-chosen dispositions:
 *   - the THREE-way TASK disposition (shown only when the project has tasks): cascade (delete them
 *     too) | move_to_inbox (un-project them) | archive_with_tasks (archive instead of delete);
 *   - the TWO-way CHILD disposition (shown only when the project has children): cascade (children
 *     share the parent's fate) | orphan_to_top (promote to top-level).
 *
 * Defaults are the least-destructive safe choices (move_to_inbox / orphan_to_top), never a silent
 * cascade. The dispositions are sent only when the corresponding count is non-zero.
 */
export function DeleteProjectDialog({ open, onClose, project, taskCount, childCount, busy = false }: DeleteProjectDialogProps) {
  const { deleteProject } = useProjectMutations();

  const [taskDisposition, setTaskDisposition] = useState<TaskDisposition>("move_to_inbox");
  const [childDisposition, setChildDisposition] = useState<ChildDisposition>("orphan_to_top");

  const confirm = () => {
    deleteProject(project.id, {
      taskDisposition: taskCount > 0 ? taskDisposition : undefined,
      childDisposition: childCount > 0 ? childDisposition : undefined,
    });
    onClose();
  };

  return (
    <Dialog open={open} onClose={onClose} titleId={TITLE_ID} descriptionId={DESC_ID}>
      <DialogTitle id={TITLE_ID}>Usuń projekt</DialogTitle>
      <p id={DESC_ID}>
        Usunięcie projektu <strong>{project.name}</strong> dotyczy {taskCount}{" "}
        {taskCount === 1 ? "zadania" : "zadań"} oraz {childCount}{" "}
        {childCount === 1 ? "podprojektu" : "podprojektów"}. Wybierz, co ma się z nimi stać.
      </p>

      {taskCount > 0 ? (
        <DialogChoices legend={`Zadania (${taskCount})`}>
          <label>
            <input
              type="radio"
              name="task-disposition"
              value="move_to_inbox"
              checked={taskDisposition === "move_to_inbox"}
              onChange={() => setTaskDisposition("move_to_inbox")}
            />
            Przenieś je do Inboxu
          </label>
          <label>
            <input
              type="radio"
              name="task-disposition"
              value="archive_with_tasks"
              checked={taskDisposition === "archive_with_tasks"}
              onChange={() => setTaskDisposition("archive_with_tasks")}
            />
            Zamiast tego zarchiwizuj projekt (zachowaj zadania)
          </label>
          <label>
            <input
              type="radio"
              name="task-disposition"
              value="cascade"
              checked={taskDisposition === "cascade"}
              onChange={() => setTaskDisposition("cascade")}
            />
            Usuń je również
          </label>
        </DialogChoices>
      ) : null}

      {childCount > 0 ? (
        <DialogChoices legend={`Podprojekty (${childCount})`}>
          <label>
            <input
              type="radio"
              name="child-disposition"
              value="orphan_to_top"
              checked={childDisposition === "orphan_to_top"}
              onChange={() => setChildDisposition("orphan_to_top")}
            />
            Przenieś na najwyższy poziom
          </label>
          <label>
            <input
              type="radio"
              name="child-disposition"
              value="cascade"
              checked={childDisposition === "cascade"}
              onChange={() => setChildDisposition("cascade")}
            />
            Usuń je również
          </label>
        </DialogChoices>
      ) : null}

      <DialogActions>
        <Button variant="secondary" onClick={onClose}>
          Anuluj
        </Button>
        <Button variant="danger" onClick={confirm} disabled={busy}>
          Usuń projekt
        </Button>
      </DialogActions>
    </Dialog>
  );
}

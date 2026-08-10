"use client";

import { useState } from "react";

import { Button } from "@/components/ui/Button";
import { Dialog, DialogActions, DialogChoices, DialogTitle } from "@/components/ui/Dialog";
import { useProjectMutations } from "@/hooks/useProjectMutations";
import type { ProjectResponse } from "@/hooks/useProjects";
import type { ChildDisposition } from "@/lib/validation/project";

const TITLE_ID = "archive-project-title";
const DESC_ID = "archive-project-desc";

interface ArchiveProjectDialogProps {
  open: boolean;
  onClose: () => void;
  /** The project being archived; drives the blast-radius copy and the child-disposition prompt. */
  project: ProjectResponse;
  /** Count of direct (active) child projects (drives the child-disposition prompt + blast radius). */
  childCount: number;
}

/**
 * The archive-with-children dialog (AS-10, Principle VII, research R5). Archiving a parent that has
 * child projects must prompt how to handle them — exactly like delete — rather than silently choosing
 * a default: cascade (archive the whole subtree) vs orphan_to_top (promote the children to top-level).
 * Archiving keeps a project's TASKS (archive is reversible), so unlike delete there is no task
 * disposition. A childless project never reaches this dialog (the sidebar archives it directly).
 *
 * The default is the least-destructive `orphan_to_top`. The FR-101 focus contract is owned by
 * {@link Dialog}; the project name is a React text node (escaped, FR-099).
 */
export function ArchiveProjectDialog({ open, onClose, project, childCount }: ArchiveProjectDialogProps) {
  const { archiveProject } = useProjectMutations();
  const [childDisposition, setChildDisposition] = useState<ChildDisposition>("orphan_to_top");

  const confirm = () => {
    archiveProject(project.id, childDisposition);
    onClose();
  };

  return (
    <Dialog open={open} onClose={onClose} titleId={TITLE_ID} descriptionId={DESC_ID}>
      <DialogTitle id={TITLE_ID}>Archiwizuj projekt</DialogTitle>
      <p id={DESC_ID}>
        Archiwizacja projektu <strong>{project.name}</strong> ukrywa go i dotyczy {childCount}{" "}
        {childCount === 1 ? "podprojektu" : "podprojektów"}. Wybierz, co ma się z nimi stać.
      </p>

      <DialogChoices legend={`Podprojekty (${childCount})`}>
        <label>
          <input
            type="radio"
            name="archive-child-disposition"
            value="orphan_to_top"
            checked={childDisposition === "orphan_to_top"}
            onChange={() => setChildDisposition("orphan_to_top")}
          />
          Przenieś na najwyższy poziom
        </label>
        <label>
          <input
            type="radio"
            name="archive-child-disposition"
            value="cascade"
            checked={childDisposition === "cascade"}
            onChange={() => setChildDisposition("cascade")}
          />
          Archiwizuj je również
        </label>
      </DialogChoices>

      <DialogActions>
        <Button variant="secondary" onClick={onClose}>
          Anuluj
        </Button>
        <Button onClick={confirm}>Archiwizuj projekt</Button>
      </DialogActions>
    </Dialog>
  );
}

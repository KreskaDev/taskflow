"use client";

import { useState } from "react";

import { Button } from "@/components/ui/Button";
import { Dialog } from "@/components/ui/Dialog";
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
      <h2 id={TITLE_ID}>Archive project</h2>
      <p id={DESC_ID}>
        Archiving <strong>{project.name}</strong> hides it and affects {childCount}{" "}
        {childCount === 1 ? "sub-project" : "sub-projects"}. Choose what happens to them.
      </p>

      <fieldset className="tf-archive-project__children">
        <legend>Its {childCount} {childCount === 1 ? "sub-project" : "sub-projects"}</legend>
        <label>
          <input
            type="radio"
            name="archive-child-disposition"
            value="orphan_to_top"
            checked={childDisposition === "orphan_to_top"}
            onChange={() => setChildDisposition("orphan_to_top")}
          />
          Promote them to top-level
        </label>
        <label>
          <input
            type="radio"
            name="archive-child-disposition"
            value="cascade"
            checked={childDisposition === "cascade"}
            onChange={() => setChildDisposition("cascade")}
          />
          Archive them too
        </label>
      </fieldset>

      <div className="tf-dialog__actions">
        <Button variant="secondary" onClick={onClose}>
          Cancel
        </Button>
        <Button onClick={confirm}>Archive project</Button>
      </div>
    </Dialog>
  );
}

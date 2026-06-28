"use client";

import { Button } from "@/components/ui/Button";
import { Dialog } from "@/components/ui/Dialog";
import { useMembershipMutations } from "@/hooks/useMembershipMutations";
import type { ProjectResponse } from "@/hooks/useProjects";

const TITLE_ID = "share-project-title";
const DESC_ID = "share-project-desc";

interface ShareProjectDialogProps {
  open: boolean;
  onClose: () => void;
  /** The project being shared/unshared; its visibility selects the mode and its version is the token. */
  project: ProjectResponse;
  /** Members losing access on unshare (drives the blast-radius copy). Ignored in share mode. */
  memberCount?: number;
}

/**
 * The share / unshare confirmation dialog (slice 007, T044; FR-058/FR-064, research R3/R12). Confirmation-
 * gated and NON-optimistic (no undo): the change takes effect only on the confirmed round-trip. In SHARE
 * mode (a personal project) it explains that the project becomes shareable; in UNSHARE mode (a shared
 * project) it states the BLAST RADIUS — every member loses access — before re-personalizing. Owns no focus
 * logic (the {@link Dialog} provides the FR-101 contract); the project name is React-escaped (FR-099).
 */
export function ShareProjectDialog({ open, onClose, project, memberCount = 0 }: ShareProjectDialogProps) {
  const { shareProject, unshareProject } = useMembershipMutations();
  const isShared = project.visibility === "shared";

  const confirm = () => {
    if (isShared) {
      unshareProject(project.id, project.version);
    } else {
      shareProject(project.id, project.version);
    }
    onClose();
  };

  return (
    <Dialog open={open} onClose={onClose} titleId={TITLE_ID} descriptionId={DESC_ID}>
      <h2 id={TITLE_ID}>{isShared ? "Unshare project" : "Share project"}</h2>
      {isShared ? (
        <p id={DESC_ID}>
          Unsharing <strong>{project.name}</strong> makes it personal again. {memberCount}{" "}
          {memberCount === 1 ? "member" : "members"} will lose all access immediately. Its tasks are kept.
        </p>
      ) : (
        <p id={DESC_ID}>
          Sharing <strong>{project.name}</strong> lets you invite members by email at an editor or viewer
          role. You stay the owner. You can make it personal again at any time.
        </p>
      )}

      <div className="tf-dialog__actions">
        <Button variant="secondary" onClick={onClose}>
          Cancel
        </Button>
        <Button variant={isShared ? "danger" : "primary"} onClick={confirm}>
          {isShared ? "Unshare project" : "Share project"}
        </Button>
      </div>
    </Dialog>
  );
}

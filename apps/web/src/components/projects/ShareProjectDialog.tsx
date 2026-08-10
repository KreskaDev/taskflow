"use client";

import { Button } from "@/components/ui/Button";
import { Dialog, DialogActions, DialogTitle } from "@/components/ui/Dialog";
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
      <DialogTitle id={TITLE_ID}>{isShared ? "Cofnij udostępnianie" : "Udostępnij projekt"}</DialogTitle>
      {isShared ? (
        <p id={DESC_ID}>
          Cofnięcie udostępniania projektu <strong>{project.name}</strong> czyni go znowu osobistym.{" "}
          {memberCount === 1 ? `1 członek natychmiast traci` : `${memberCount} członków natychmiast traci`} wszelki dostęp. Zadania projektu zostają.
        </p>
      ) : (
        <p id={DESC_ID}>
          Udostępnienie projektu <strong>{project.name}</strong> pozwala zapraszać członków przez e-mail
          w roli edytora lub podglądu. Pozostajesz właścicielem i możesz to cofnąć w każdej chwili.
        </p>
      )}

      <DialogActions>
        <Button variant="secondary" onClick={onClose}>
          Anuluj
        </Button>
        <Button variant={isShared ? "danger" : "primary"} onClick={confirm}>
          {isShared ? "Cofnij udostępnianie" : "Udostępnij projekt"}
        </Button>
      </DialogActions>
    </Dialog>
  );
}

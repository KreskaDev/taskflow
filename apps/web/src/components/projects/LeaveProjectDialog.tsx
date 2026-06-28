"use client";

import { Button } from "@/components/ui/Button";
import { Dialog } from "@/components/ui/Dialog";
import { useMembershipMutations } from "@/hooks/useMembershipMutations";

const TITLE_ID = "leave-project-title";
const DESC_ID = "leave-project-desc";

interface LeaveProjectDialogProps {
  open: boolean;
  onClose: () => void;
  projectId: string;
  projectName: string;
  version: number;
}

/**
 * Leave-project dialog (slice 007, T045; FR-063/FR-064, research R7/R10). Self-service for a NON-owner
 * member — confirmation-gated, NON-optimistic (no undo). States the BLAST RADIUS: you lose ALL access
 * immediately. (The owner never sees this control — the owner cannot leave; they would have to transfer
 * ownership first, the last-owner guard. R7.) The {@link Dialog} owns the focus contract.
 */
export function LeaveProjectDialog({ open, onClose, projectId, projectName, version }: LeaveProjectDialogProps) {
  const { leaveProject } = useMembershipMutations();

  const confirm = () => {
    leaveProject(projectId, version);
    onClose();
  };

  return (
    <Dialog open={open} onClose={onClose} titleId={TITLE_ID} descriptionId={DESC_ID}>
      <h2 id={TITLE_ID}>Leave project</h2>
      <p id={DESC_ID}>
        Leaving <strong>{projectName}</strong> removes all your access immediately and unassigns you from its
        tasks. You&apos;d need the owner to invite you again to return.
      </p>

      <div className="tf-dialog__actions">
        <Button variant="secondary" onClick={onClose}>
          Cancel
        </Button>
        <Button variant="danger" onClick={confirm}>
          Leave project
        </Button>
      </div>
    </Dialog>
  );
}

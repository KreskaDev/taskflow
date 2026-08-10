"use client";

import { Button } from "@/components/ui/Button";
import { Dialog, DialogActions, DialogTitle } from "@/components/ui/Dialog";
import { useMembershipMutations } from "@/hooks/useMembershipMutations";
import type { MemberResponse } from "@/hooks/useProjectMembers";

const TITLE_ID = "remove-member-title";
const DESC_ID = "remove-member-desc";

interface RemoveMemberDialogProps {
  open: boolean;
  onClose: () => void;
  projectId: string;
  version: number;
  /** The member to remove. */
  member: MemberResponse;
}

/**
 * Remove-member dialog (slice 007, T045; FR-062/FR-064, research R10). Confirmation-gated, NON-optimistic
 * (no undo). States the BLAST RADIUS: the member loses ALL access immediately and is unassigned from the
 * project's tasks. The {@link Dialog} owns the focus contract; the display name is React-escaped (FR-099).
 */
export function RemoveMemberDialog({ open, onClose, projectId, version, member }: RemoveMemberDialogProps) {
  const { removeMember } = useMembershipMutations();

  const confirm = () => {
    removeMember(projectId, member.userId, version);
    onClose();
  };

  return (
    <Dialog open={open} onClose={onClose} titleId={TITLE_ID} descriptionId={DESC_ID}>
      <DialogTitle id={TITLE_ID}>Usuń członka</DialogTitle>
      <p id={DESC_ID}>
        Usunięcie użytkownika <strong>{member.displayName}</strong> natychmiast odbiera mu wszelki dostęp do
        projektu i cofa przypisania do zadań. Nie można tego cofnąć — trzeba by zaprosić go ponownie.
      </p>

      <DialogActions>
        <Button variant="secondary" onClick={onClose}>
          Anuluj
        </Button>
        <Button variant="danger" onClick={confirm}>
          Usuń członka
        </Button>
      </DialogActions>
    </Dialog>
  );
}

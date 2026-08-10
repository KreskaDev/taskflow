"use client";

import { useState } from "react";

import { Button } from "@/components/ui/Button";
import { Dialog, DialogActions, DialogTitle } from "@/components/ui/Dialog";
import dialogStyles from "@/components/ui/Dialog.module.css";
import { useMembershipMutations } from "@/hooks/useMembershipMutations";
import type { MemberResponse } from "@/hooks/useProjectMembers";

const TITLE_ID = "transfer-owner-title";
const DESC_ID = "transfer-owner-desc";

interface TransferOwnershipDialogProps {
  open: boolean;
  onClose: () => void;
  projectId: string;
  version: number;
  /** The non-owner members eligible to receive ownership. */
  members: MemberResponse[];
}

/**
 * Transfer-ownership dialog (slice 007, T045; FR-094, research R6). Confirmation-gated, NON-optimistic.
 * Pick a current member to become the new owner; the blast-radius copy states that YOU become an editor.
 * The target must be an existing member (the server 422s a non-member). The {@link Dialog} owns the focus
 * contract; member display names are React-escaped (FR-099).
 */
export function TransferOwnershipDialog({ open, onClose, projectId, version, members }: TransferOwnershipDialogProps) {
  const { transferOwnership } = useMembershipMutations();
  const [userId, setUserId] = useState<string>(members[0]?.userId ?? "");

  const confirm = () => {
    if (!userId) return;
    transferOwnership(projectId, userId, version);
    onClose();
  };

  return (
    <Dialog open={open} onClose={onClose} titleId={TITLE_ID} descriptionId={DESC_ID}>
      <DialogTitle id={TITLE_ID}>Transfer ownership</DialogTitle>
      <p id={DESC_ID}>
        Choose a member to become the new owner. You become an <strong>editor</strong> — this cannot be undone
        from here (the new owner would have to transfer it back).
      </p>

      {members.length === 0 ? (
        <p className={dialogStyles.muted}>Invite a member first — ownership can only move to a current member.</p>
      ) : (
        <label className={dialogStyles.fieldRow}>
          <span>New owner</span>
          <select value={userId} onChange={(e) => setUserId(e.target.value)}>
            {members.map((m) => (
              <option key={m.userId} value={m.userId}>
                {m.displayName}
              </option>
            ))}
          </select>
        </label>
      )}

      <DialogActions>
        <Button variant="secondary" onClick={onClose}>
          Cancel
        </Button>
        <Button variant="danger" onClick={confirm} disabled={members.length === 0 || !userId}>
          Transfer ownership
        </Button>
      </DialogActions>
    </Dialog>
  );
}

"use client";

import { useState } from "react";

import { InviteMemberForm } from "@/components/projects/InviteMemberForm";
import { LeaveProjectDialog } from "@/components/projects/LeaveProjectDialog";
import { RemoveMemberDialog } from "@/components/projects/RemoveMemberDialog";
import { RoleBadge } from "@/components/projects/RoleBadge";
import { ShareProjectDialog } from "@/components/projects/ShareProjectDialog";
import { TransferOwnershipDialog } from "@/components/projects/TransferOwnershipDialog";
import { Button } from "@/components/ui/Button";
import { Dialog } from "@/components/ui/Dialog";
import { useMembershipMutations } from "@/hooks/useMembershipMutations";
import { useProjectMembers, type MemberResponse } from "@/hooks/useProjectMembers";
import type { ProjectResponse } from "@/hooks/useProjects";
import type { MembershipRole } from "@/lib/validation/membership";

const TITLE_ID = "members-dialog-title";

interface MembersDialogProps {
  open: boolean;
  onClose: () => void;
  /** The shared project whose roster this manages; `project.role` drives the role-aware gating. */
  project: ProjectResponse;
}

/**
 * The members roster hub (slice 007, T044; research R17, Principle II/IV). Loads the composed roster
 * (owner ∪ editor/viewer rows) and gates affordances by the CALLER's effective role (`project.role`):
 *  - the OWNER sees the invite form, per-member role change + remove, transfer-ownership, and unshare;
 *  - a non-owner MEMBER sees a read-only roster and a Leave action (the owner never sees Leave — they
 *    must transfer first, R7). Role badges carry text + icon (never color alone, FR-044). The version the
 *    mutations carry comes from the roster (R11). The {@link Dialog} owns the FR-101 focus contract.
 */
export function MembersDialog({ open, onClose, project }: MembersDialogProps) {
  const isOwner = project.role === "owner";
  const { data: roster, isPending, error } = useProjectMembers(project.id, open);
  const { changeMemberRole } = useMembershipMutations();

  const [transferring, setTransferring] = useState(false);
  const [removing, setRemoving] = useState<MemberResponse | null>(null);
  const [leaving, setLeaving] = useState(false);
  const [unsharing, setUnsharing] = useState(false);

  const version = roster?.version ?? project.version;
  const members = roster?.members ?? [];
  const nonOwnerMembers = members.filter((m) => !m.isOwner);

  return (
    <>
      <Dialog open={open} onClose={onClose} titleId={TITLE_ID}>
        <h2 id={TITLE_ID}>Members of {project.name}</h2>

        {isPending ? <p className="tf-members__status">Loading members…</p> : null}
        {error ? (
          <p className="tf-members__status" role="alert">
            {error.message}
          </p>
        ) : null}

        {roster ? (
          <ul className="tf-members__list" aria-label="Project members">
            {members.map((member) => (
              <li key={member.userId} className="tf-members__row">
                <span className="tf-members__name">{member.displayName}</span>
                <RoleBadge role={member.role} />
                {isOwner && !member.isOwner ? (
                  <span className="tf-members__actions">
                    <label className="tf-members__role-select">
                      <span className="tf-visually-hidden">Role for {member.displayName}</span>
                      <select
                        aria-label={`Role for ${member.displayName}`}
                        value={member.role}
                        onChange={(e) => changeMemberRole(project.id, member.userId, e.target.value as MembershipRole, version)}
                      >
                        <option value="editor">Editor</option>
                        <option value="viewer">Viewer</option>
                      </select>
                    </label>
                    <button
                      type="button"
                      className="tf-icon-button"
                      aria-label={`Remove ${member.displayName}`}
                      onClick={() => setRemoving(member)}
                    >
                      Remove
                    </button>
                  </span>
                ) : null}
              </li>
            ))}
          </ul>
        ) : null}

        {isOwner ? (
          <>
            <InviteMemberForm projectId={project.id} version={version} />
            <div className="tf-members__owner-actions">
              <Button variant="secondary" onClick={() => setTransferring(true)} disabled={nonOwnerMembers.length === 0}>
                Transfer ownership
              </Button>
              <Button variant="danger" onClick={() => setUnsharing(true)}>
                Unshare project
              </Button>
            </div>
          </>
        ) : (
          <div className="tf-members__member-actions">
            <Button variant="danger" onClick={() => setLeaving(true)}>
              Leave project
            </Button>
          </div>
        )}

        <div className="tf-dialog__actions">
          <Button variant="secondary" onClick={onClose}>
            Close
          </Button>
        </div>
      </Dialog>

      <TransferOwnershipDialog
        open={transferring}
        onClose={() => setTransferring(false)}
        projectId={project.id}
        version={version}
        members={nonOwnerMembers}
      />
      {removing ? (
        <RemoveMemberDialog
          open
          onClose={() => setRemoving(null)}
          projectId={project.id}
          version={version}
          member={removing}
        />
      ) : null}
      <LeaveProjectDialog
        open={leaving}
        onClose={() => setLeaving(false)}
        projectId={project.id}
        projectName={project.name}
        version={version}
      />
      <ShareProjectDialog
        open={unsharing}
        // Carry the LIVE roster version (the sidebar's project.version can be stale after invites/role
        // changes) so unshare does not 409 against a concurrency token from an earlier render (R11).
        project={{ ...project, version }}
        onClose={() => setUnsharing(false)}
        memberCount={nonOwnerMembers.length}
      />
    </>
  );
}

"use client";

import { useState } from "react";

import { Button } from "@/components/ui/Button";
import { useMembershipMutations } from "@/hooks/useMembershipMutations";
import { inviteSchema, type MembershipRole } from "@/lib/validation/membership";

interface InviteMemberFormProps {
  projectId: string;
  /** The current Project version (from the roster) — the optimistic-concurrency token the invite carries. */
  version: number;
}

/**
 * Invite-by-email form (slice 007, T044; FR-031/FR-049/FR-060, research R4). A standard text input (so the
 * global single-key shortcuts are suppressed while it is focused — FR-031) plus an editor/viewer role
 * picker. The invite is NON-optimistic (it takes effect on the confirmed round-trip); the server resolves
 * the email and an unknown / self / duplicate address surfaces the FR-049 message via the mutation error.
 */
export function InviteMemberForm({ projectId, version }: InviteMemberFormProps) {
  const { inviteMember, inviteError, isInvitePending } = useMembershipMutations();
  const [email, setEmail] = useState("");
  const [role, setRole] = useState<MembershipRole>("editor");
  const [localError, setLocalError] = useState<string | null>(null);

  const submit = (event: React.FormEvent) => {
    event.preventDefault();
    const parsed = inviteSchema.safeParse({ email, role, version });
    if (!parsed.success) {
      setLocalError("Enter a valid email address.");
      return;
    }
    setLocalError(null);
    inviteMember(projectId, parsed.data.email, parsed.data.role, version);
    setEmail("");
  };

  const error = localError ?? inviteError;

  return (
    <form className="tf-invite-form" onSubmit={submit} aria-label="Invite a member">
      <label className="tf-invite-form__email">
        <span>Invite by email</span>
        <input
          type="email"
          name="invite-email"
          autoComplete="off"
          placeholder="name@example.com"
          value={email}
          onChange={(e) => setEmail(e.target.value)}
        />
      </label>
      <label className="tf-invite-form__role">
        <span>Role</span>
        <select value={role} onChange={(e) => setRole(e.target.value as MembershipRole)}>
          <option value="editor">Editor</option>
          <option value="viewer">Viewer</option>
        </select>
      </label>
      <Button type="submit" disabled={isInvitePending || email.trim().length === 0}>
        Invite
      </Button>
      {error ? (
        <p className="tf-invite-form__error" role="alert">
          {error}
        </p>
      ) : null}
    </form>
  );
}

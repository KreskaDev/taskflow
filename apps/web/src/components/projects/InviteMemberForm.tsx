"use client";

import { useState } from "react";

import { Button } from "@/components/ui/Button";
import { Input } from "@/components/ui/Input";
import { useMembershipMutations } from "@/hooks/useMembershipMutations";
import { inviteSchema, type MembershipRole } from "@/lib/validation/membership";
import styles from "./InviteMemberForm.module.css";

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
      setLocalError("Podaj poprawny adres e-mail.");
      return;
    }
    setLocalError(null);
    inviteMember(projectId, parsed.data.email, parsed.data.role, version);
    setEmail("");
  };

  const error = localError ?? inviteError;

  return (
    <form className={styles.form} onSubmit={submit} aria-label="Zaproś członka">
      <label className={styles.field}>
        <span>Zaproś przez e-mail</span>
        <Input
          type="email"
          name="invite-email"
          autoComplete="off"
          placeholder="name@example.com"
          value={email}
          onChange={(e) => setEmail(e.target.value)}
        />
      </label>
      <label className={styles.field}>
        <span>Rola</span>
        <select value={role} onChange={(e) => setRole(e.target.value as MembershipRole)}>
          <option value="editor">Edytor</option>
          <option value="viewer">Podgląd</option>
        </select>
      </label>
      <Button type="submit" disabled={isInvitePending || email.trim().length === 0}>
        Zaproś
      </Button>
      {error ? (
        <p className={styles.error} role="alert">
          {error}
        </p>
      ) : null}
    </form>
  );
}

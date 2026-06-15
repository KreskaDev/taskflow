# ADR-0006 — Sharing model

**Status:** Accepted (2026-06-14); amended (2026-06-15) under the design-review
remediation ledger to align with constitution v4.0.0 — invite-by-email
resolution (B13), owner as immutable `ownerId` with an explicit transfer command
and last-owner guard (B1), and blast-radius confirmation on unshare/remove. The
personal↔shared lifecycle of the original decisions stands.
**Builds on:** ADR-0001 (stack), ADR-0003 (domain model), constitution v4.0.0
(Principle VII Data Integrity, Principle IX Authentication & Authorization,
Principle XI Privacy & Personal Data)
**Scope:** how a `Project` moves between personal and shared, how membership is
granted and revoked, who the owner is and how ownership moves, and what happens
to assignments at each transition.
**Maps:** FR-057..FR-064, FR-069, FR-094; realizes US-12 (and the assignment edge
of US-13).

## Context

The product is a collaborative, multi-user (~10) app (constitution v4.0.0). A
`Project` carries an `ownerId` and a `visibility` of **personal** or **shared**
(ENT-02), and shared projects carry a `ProjectMembership` set with per-project roles
(ENT-07). Assignment of users to tasks (ENT-01 `assignees`) exists **only** on
shared-project tasks. This ADR fixes the lifecycle that ties those pieces together:
sharing, inviting, role changes, ownership transfer, removal, leaving, and unsharing.

Three cross-cutting constraints from the constitution shape every decision below:

- **Authorization is membership + role, deny-by-default** (Principle IX): viewer =
  read-only (no commenting), editor = change tasks/comment, owner = manage members /
  share-unshare / delete. Only the owner manages membership and visibility. The owner
  is the immutable `ownerId`, **not** a freely assignable role.
- **Membership and role changes are NOT data-undo operations** (Principle VII). They
  do not get the 30-second undo of FR-040; they require an explicit confirmation
  dialog that MUST show its **blast radius** (members losing access, assignments
  cleared) before taking effect.
- **Admission is gated and invitations resolve against existing Users only**
  (Principle IX admission control; ledger B13). There is no pre-account or pending
  invitation (OOS-18); a person must have signed in once before they can be invited.

## Decisions

1. **Visibility is an aggregate-owned attribute, personal by default.** `Project`
   has `visibility ∈ {personal, shared}` and defaults to **personal** on creation
   (FR-057). Personal means private to the owner; shared means access is governed by
   the `ProjectMembership` set. Visibility transitions are owner-only commands on the
   `Project` aggregate.

2. **Owner converts personal → shared and shared → personal (unshare).** Only the
   project owner may flip visibility either direction (FR-058). Sharing a personal
   project and inviting the first member is a single user intent (US-12.AS-01): the
   project becomes shared and the invitee gains access at the assigned role. Reverting
   to personal is the unshare path (Decision 8). Personal↔shared remains reversible:
   unshare returns a shared project to personal, and a personal project may be shared
   again later.

3. **Invitation is by email, resolved against an existing User.** Inviting names a
   person **by email address**, which is resolved against the set of existing,
   admitted, signed-in Users (B13). On a match, a `ProjectMembership` is attached
   linking that User to the Project at an assignable role (Decision 4). If the email
   does not resolve to an existing User, the invite is rejected with a clear,
   recoverable FR-049 message ("ask them to sign in once first") and no membership is
   created. **Pending / pre-account invitations are out of scope** (OOS-18): there is
   no invite record awaiting a future account.

4. **Assignable roles are editor and viewer only; owner is not assignable.** Invite
   and role-change set a member's role to **editor or viewer** (FR-060, FR-061,
   ENT-07). **Owner is NOT a freely assignable role** — it is the project's immutable
   `ownerId`. The owning user is always a member at the owner role; the invariant "a
   shared project has exactly one owner who is a member" holds. Role is the unit of
   authorization (FR-067): each operation requires sufficient role and insufficient
   role is denied (FR-068).

5. **Ownership moves only via an explicit transfer-owner command, guarded against
   losing the last owner.** The owner does not change hands by promoting a member to
   an "owner role" (there is no such assignable role — Decision 4). Instead a
   dedicated **transfer-owner** command (FR-094) moves `ownerId` to a chosen existing
   member of the shared project; the prior owner becomes an editor (or another
   assignable role) and the new owner gains the owner capabilities. **Last-owner
   guard:** the sole owner cannot be removed, demoted, or leave such that the project
   is left without an owner — any such attempt fails with a recoverable FR-049 error
   (B1). Transfer is the only path to a different owner short of deletion.

6. **Owner changes a member's role and removes a member.** The owner may set any
   non-owner member's role to viewer/editor and may remove a member entirely (FR-062,
   US-12.AS-02), subject to the last-owner guard (Decision 5). **Removal requires
   explicit confirmation** (a dialog showing the blast radius — who loses access and
   how many of their assignments will be cleared — not the 30s undo; FR-064,
   Principle VII) and, on confirmation, **clears that member's assignments on the
   project's tasks** (FR-062, US-12.AS-04): the removed user loses access and is
   unassigned from every task in that project. A plain role change does not touch
   assignments.

7. **A non-owner member leaves.** Any non-owner member may leave a shared project
   (FR-063, US-12.AS-05). Leaving is symmetric to removal from the data side: it
   **requires explicit confirmation showing the count of assignments to be cleared**
   (FR-064) and, on confirmation, the leaver loses access and **their assignments on
   the project's tasks are cleared**. The owner cannot "leave" (last-owner guard,
   Decision 5); the owner transfers ownership, unshares, or deletes the project
   instead.

8. **Unshare reverts to personal and clears all non-owner membership + assignments.**
   When the owner unshares (shared → personal), it **requires explicit confirmation
   showing the blast radius — the count of members losing access and assignments
   cleared** (FR-064) and, on confirmation: all non-owner members lose access,
   **every non-owner member's assignments on the project's tasks are cleared**, the
   `ProjectMembership` set is reduced to the owner alone, and `visibility` returns to
   **personal** (FR-059, US-12.AS-06). Unshare is the bulk form of "remove every
   member at once".

9. **Personal-project tasks have no assignment.** Assignment is a shared-only concept:
   personal-project tasks neither carry nor offer assignees (FR-069, US-13.AS-04,
   ENT-01). This is why each access-loss transition above must clear assignments — a
   task that ends up in a project the user can no longer reach (removal/leave), or in a
   project that is no longer shared at all (unshare), must not retain a dangling
   assignee. Conversely, converting personal → shared simply enables assignment going
   forward; it creates no assignments.

10. **Membership changes are not the 30s data undo; they are confirmed actions.** Per
    Principle VII and FR-064, invite, role change, ownership transfer, remove, leave,
    and unshare each require an explicit confirmation step (with blast radius where
    access is lost) and are excluded from the FR-040 undo window. They are deliberate,
    immediately-effective authorization changes, not reversible data edits.

## Where it lives

- **Aggregate (`Project`, Task Management context):** owns `ownerId`, `visibility`,
  the `ProjectMembership` set, and the visibility/membership/transfer commands;
  enforces the single-owner, last-owner-guard, and personal-default invariants.
- **Invitation resolution (Identity & Access context):** resolving an invite email to
  an existing admitted User happens against the User set before a `ProjectMembership`
  is created; an unresolved email never produces a membership (B13).
- **Application-layer authorization policy (per Principle IX / ADR-0001):** the
  deny-by-default membership + role check on every command and query; it is the gate
  that "loses access" means at runtime, and it treats owner capability as derived from
  `ownerId`, not from an assignable role.
- **Assignment-clearing as a cross-aggregate effect:** removing a member, leaving, and
  unsharing each clear assignees on the project's `Task`s. Consistent with ADR-0003
  Decision 1, this is expressed as a domain event (e.g. `MembershipRevoked` /
  `ProjectUnshared`) whose handler unassigns the affected user(s) across the project's
  tasks, delivered through the Wolverine transactional outbox. One aggregate is
  modified per transaction; the eventual-consistency window is negligible at ~10 users.

## Consequences

- **Slice 007 (project-sharing-membership)** realizes FR-057..FR-064 and FR-094:
  visibility, invite-by-email/role/remove/leave/unshare, ownership transfer, the
  blast-radius confirmation-dialog UX, the last-owner guard, and the authorization
  policy. **Slice 008 (task-assignment)** depends on this — FR-069 and the "no
  assignment on personal projects" rule are the contract slice 008 builds on, and the
  assignment-clearing handlers are exercised by the transitions defined here.
- **Owner is structural, not a role.** The role enum exposed to invite/role-change is
  **{editor, viewer}** only; owner capability is derived from `ownerId`. Any UI or
  contract that previously offered "owner" as a selectable role is corrected to a
  separate transfer-owner action.
- **Invite UX surfaces the resolve failure.** The invite flow MUST present the FR-049
  "ask them to sign in once first" message on an unresolved email, and MUST NOT create
  a pending record (OOS-18).
- **Confirmation with blast radius, not undo:** the membership UX uses a confirmation
  dialog that shows members losing access and the count of assignments cleared, and
  must not present a 30-second undo toast for these actions; this keeps FR-040/FR-064
  and Principle VII consistent. Test coverage MUST include the deny cases (Principle
  IX, SC-013/SC-016), the last-owner-guard failures, and the assignment-clearing on
  remove/leave/unshare.
- **No tenancy dimension:** authorization stays ownership + per-project membership;
  this ADR adds no organization/tenant concept (constitution Constraints & Scope,
  OOS-17).

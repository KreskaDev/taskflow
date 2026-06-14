# ADR-0006 — Sharing model

**Status:** Accepted (2026-06-14)
**Builds on:** ADR-0001 (stack), ADR-0003 (domain model), constitution v3.0.0
(Principle VII Data Integrity, Principle IX Authentication & Authorization)
**Scope:** how a `Project` moves between personal and shared, how membership is
granted and revoked, and what happens to assignments at each transition.
**Maps:** FR-057..FR-064, FR-069; realizes US-12 (and the assignment edge of US-13).

## Context

The product is now a collaborative, multi-user (~10) app (constitution v3.0.0). A
`Project` carries an `ownerId` and a `visibility` of **personal** or **shared**
(ENT-02), and shared projects carry a `ProjectMembership` set with per-project roles
(ENT-07). Assignment of users to tasks (ENT-01 `assignees`) exists **only** on
shared-project tasks. This ADR fixes the lifecycle that ties those pieces together:
sharing, inviting, role changes, removal, leaving, and unsharing.

Two cross-cutting constraints from the constitution shape every decision below:

- **Authorization is membership + role, deny-by-default** (Principle IX): viewer =
  read-only, editor = change tasks/comment, owner = manage members / share-unshare /
  delete. Only the owner manages membership and visibility.
- **Membership and role changes are NOT data-undo operations** (Principle VII). They
  do not get the 30-second undo of FR-040; they require an explicit confirmation
  dialog before taking effect.

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
   to personal is the unshare path (Decision 6).

3. **Invitation adds a `ProjectMembership` with a role.** Inviting a user attaches a
   `ProjectMembership` linking that User to the Project at one of the three roles —
   **owner / editor / viewer** (FR-060, FR-061, ENT-07). The owning user is always a
   member at the owner role; the invariant "a shared project has exactly one owner who
   is a member" holds. Role is the unit of authorization (FR-067): each operation
   requires sufficient role and insufficient role is denied (FR-068).

4. **Owner changes a member's role and removes a member.** The owner may set any
   non-owner member's role to viewer/editor/owner and may remove a member entirely
   (FR-062, US-12.AS-02). **Removal requires explicit confirmation** (a dialog, not
   the 30s undo — FR-064, Principle VII) and, on confirmation, **clears that member's
   assignments on the project's tasks** (FR-062, US-12.AS-04): the removed user loses
   access and is unassigned from every task in that project. A plain role change does
   not touch assignments.

5. **A non-owner member leaves.** Any non-owner member may leave a shared project
   (FR-063, US-12.AS-05). Leaving is symmetric to removal from the data side: it
   **requires explicit confirmation** (FR-064) and, on confirmation, the leaver loses
   access and **their assignments on the project's tasks are cleared**. The owner
   cannot "leave"; the owner unshares or transfers/deletes the project instead.

6. **Unshare reverts to personal and clears all non-owner membership + assignments.**
   When the owner unshares (shared → personal), it **requires explicit confirmation**
   (FR-064) and, on confirmation: all non-owner members lose access, **every non-owner
   member's assignments on the project's tasks are cleared**, the `ProjectMembership`
   set is reduced to the owner alone, and `visibility` returns to **personal**
   (FR-059, US-12.AS-06). Unshare is the bulk form of "remove every member at once".

7. **Personal-project tasks have no assignment.** Assignment is a shared-only concept:
   personal-project tasks neither carry nor offer assignees (FR-069, US-13.AS-04,
   ENT-01). This is why each access-loss transition above must clear assignments — a
   task that ends up in a project the user can no longer reach (removal/leave), or in a
   project that is no longer shared at all (unshare), must not retain a dangling
   assignee. Conversely, converting personal → shared simply enables assignment going
   forward; it creates no assignments.

8. **Membership changes are not the 30s data undo; they are confirmed actions.** Per
   Principle VII and FR-064, invite, role change, remove, leave, and unshare each
   require an explicit confirmation step and are excluded from the FR-040 undo window.
   They are deliberate, immediately-effective authorization changes, not reversible
   data edits.

## Where it lives

- **Aggregate (`Project`, Task Management context):** owns `visibility`, the
  `ProjectMembership` set, and the visibility/membership transition commands; enforces
  the single-owner and personal-default invariants.
- **Application-layer authorization policy (per Principle IX / ADR-0001):** the
  deny-by-default membership + role check on every command and query; it is the gate
  that "loses access" means at runtime.
- **Assignment-clearing as a cross-aggregate effect:** removing a member, leaving, and
  unsharing each clear assignees on the project's `Task`s. Consistent with ADR-0003
  Decision 1, this is expressed as a domain event (e.g. `MembershipRevoked` /
  `ProjectUnshared`) whose handler unassigns the affected user(s) across the project's
  tasks, delivered through the Wolverine transactional outbox. One aggregate is
  modified per transaction; the eventual-consistency window is negligible at ~10 users.

## Consequences

- **Slice 007 (project-sharing-membership)** realizes FR-057..FR-064: visibility,
  invite/role/remove/leave/unshare, the confirmation-dialog UX, and the
  authorization policy. **Slice 008 (task-assignment)** depends on this — FR-069 and
  the "no assignment on personal projects" rule are the contract slice 008 builds on,
  and the assignment-clearing handlers are exercised by the transitions defined here.
- **Confirmation, not undo:** the membership UX uses a confirmation dialog and must
  not present a 30-second undo toast for these actions; this keeps FR-040/FR-064 and
  Principle VII consistent. Test coverage MUST include the deny cases (Principle IX,
  SC-013) and the assignment-clearing on remove/leave/unshare.
- **No tenancy dimension:** authorization stays ownership + per-project membership;
  this ADR adds no organization/tenant concept (constitution Constraints & Scope,
  OOS-17). Owner-to-owner transfer beyond promoting a member to the owner role is not
  introduced here.

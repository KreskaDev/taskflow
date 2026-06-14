# ADR-0005 — Authorization

**Status:** Accepted (2026-06-14)
**Implements:** constitution v3.0.0 Principle IX (Authentication & Authorization) and the
"Collaborative, Multi-User" scope; product-vision FR-061, FR-065..068
**Relates to:** ADR-0001 (stack: DDD + CQRS via Wolverine), ADR-0003 (domain model:
Identity & Access context, `User` / `ProjectMembership`), and the Identity/Access ADR-0004

## Context

TaskFlow is a connected, multi-user (~10) collaborative web app (constitution v3.0.0).
Every request is authenticated (Google OAuth, ADR-0004) and every data operation must be
authorized. The product carries two distinct access shapes that the data model already
encodes (ENT-02 `Project.ownerId` / `visibility`, ENT-07 `ProjectMembership`, ENT-01
`Task.createdBy` / `assignees`):

1. **Personal data** — a user's own tasks and personal projects, private by default.
2. **Shared projects** — a project an owner has shared, with a set of members at roles.

Authentication establishes *who* the caller is; it says nothing about *what* they may
touch. Without a deliberate, uniform authorization layer, a missed check is a cross-user
data leak — a correctness and trust failure, not a cosmetic bug. Authorization therefore
needs a single, testable home rather than ad-hoc checks scattered across handlers, and it
must default to denial so that a forgotten check fails closed, not open.

This ADR fixes *how* authorization is modeled and enforced. It does not introduce
organizations/multi-tenancy, guest access, or public share links — those remain out of
scope (OOS-16, OOS-17); the single-team boundary keeps the model to ownership +
per-project membership.

## Decisions

1. **Two-tier authorization model.**
   - **Tier A — Per-user isolation (ownership).** Every entity carries an ownership
     anchor: tasks have `createdBy`, projects have `ownerId`. A caller may only read or
     write data they own. Every query is scoped to the caller's identity at the
     application layer — there is no unscoped "fetch by id" path that bypasses the
     caller. This tier alone fully governs personal (non-shared) data. (FR-065)
   - **Tier B — Membership + role.** For shared projects, access additionally requires
     membership in that project (FR-066), and each operation requires a sufficient role
     (FR-061, FR-067):
     - **viewer** — read-only. May read tasks and comments; MUST NOT write, and MUST NOT
       comment.
     - **editor** — may change tasks and post comments, in addition to all viewer reads.
     - **owner** — full management: invite/remove members, change roles, share/unshare,
       delete the project, in addition to all editor capabilities.
     Roles are cumulative and enforced per operation under least privilege: the handler
     authorizes against the *minimum* role the specific action requires (viewer = read,
     editor = write, owner = manage).

2. **Deny-by-default.** Authorization fails closed. Every read and write MUST be
   explicitly authorized; the absence of an applicable grant is a denial, never an
   implicit allow. An unauthenticated or under-privileged request is rejected. (FR-068)

3. **Application-layer policy, backed by `ProjectMembership`.** Authorization lives in a
   dedicated application-layer policy (Identity & Access context, ADR-0003), evaluated
   against the caller identity, the target's ownership anchor, and — for shared targets —
   the caller's `ProjectMembership` and role. It is NOT scattered across UI, controllers,
   or duplicated per handler, and it is NOT pushed into the aggregates: aggregates remain
   focused on domain invariants (single active cycle, one-level nesting, recurrence/status
   rules), while the policy owns the access decision. (constitution Principle IX)

4. **Enforced at every command and query handler.** The check sits at the API/handler
   boundary in the Wolverine command/query pipeline (ADR-0001), so it covers both the
   write side and the CQRS read-projection (query) side uniformly. Read projections are
   scoped through the same policy; a DTO query cannot return data the caller is not
   entitled to. There is no handler — read or write — that executes without an
   authorization decision. (FR-068)

5. **Authorization integration tests are mandatory.** Per constitution Principle VIII,
   integration tests run command/query handlers through the real database and MUST cover
   authorization, including the negative cases: a request lacking the required ownership
   is denied (Tier A); a non-member is denied access to a shared project (Tier B); a
   viewer's write or comment attempt is denied; an editor's member-management attempt is
   denied. Deny-by-default is verified, not assumed. These tests gate merging (SC-013:
   authorization enforced on 100% of data operations).

## Consequences

- **A single, auditable choke point.** Because the decision is one application-layer
  policy in the Wolverine pipeline rather than per-handler code, the development-workflow
  authorization review (constitution Governance) has one place to inspect, and new data
  operations inherit enforcement by construction. Adding a handler without a policy
  decision is the reviewable defect to guard against.
- **Ownership fields are load-bearing, not optional.** `createdBy`, `ownerId`,
  `visibility`, and `ProjectMembership` (ENT-01, ENT-02, ENT-07) are required inputs to
  every authorization decision; they cannot be dropped or left nullable without breaking
  the model. Queries must select the ownership/membership context needed to authorize.
- **Read paths cost a scope.** Per-user isolation means query handlers filter by caller
  identity and membership; there is no global list. This is the intended cost of
  fail-closed isolation and aligns with the CQRS read-projection design (ADR-0001).
- **Role transitions interact with sharing semantics (cross-ADR).** Removing a member,
  changing a role, unsharing, or leaving (FR-062..064) immediately changes what the
  policy grants and clears assignments (FR-059); these membership changes require explicit
  confirmation and are NOT covered by the 30-second data undo (constitution Principle VII,
  FR-064). The mechanics of sharing/membership lifecycle are owned by the Sharing ADR;
  this ADR governs only the access decision that membership/role drive.
- **Viewer "no comment" is an authorization rule, not just UI.** The viewer read-only
  constraint (incl. no commenting, FR-061, FR-072) is enforced server-side at the comment
  command handler; hiding the comment input in the client is a convenience, not the
  control.
- **Negative tests become first-class.** Test suites grow a permanent class of "MUST be
  denied" cases; a failing or missing deny test blocks merge. This is deliberate — the
  most dangerous authorization bug is the silent allow, which only a negative test catches.

## Notes

- **Why not enforce in the aggregate?** Co-locating authorization with invariants would
  entangle "who may act" with "what is a valid state," forcing every aggregate to know
  about identity and membership. Keeping the policy in the application layer preserves the
  aggregates' single responsibility (domain invariants) and keeps the access rule reusable
  across command and query sides.
- **Why no resource-level ACLs or capability tokens?** The single-team scope (ASM-10,
  OOS-17) makes ownership + per-project role sufficient. A finer-grained ACL system would
  be premature (YAGNI) and would add a configuration surface the constitution explicitly
  resists. If a future requirement needs per-task or field-level grants, that is a new
  amendment, not a silent extension here.

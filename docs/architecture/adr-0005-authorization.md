# ADR-0005 — Authorization

**Status:** Accepted (2026-06-14); amended 2026-06-15 — the two-tier *conjunction* model
(Decision 1) is superseded by **dispatch-by-containing-project-visibility** under
constitution v4.0.0 Principle IX; authorship is recognized as a distinct object-level
grant (Decision 6), and the access decision is extended to live SignalR subscriptions
(Decision 7). Decisions 2–4 remain in force as restated.
**Implements:** constitution v4.0.0 Principle IX (Authentication & Authorization) and the
"Collaborative, Multi-User" scope; product-vision FR-061, FR-065..068, FR-073, FR-075,
FR-094, FR-095; SC-013, SC-016
**Relates to:** ADR-0001 (stack: DDD + CQRS via Wolverine), ADR-0003 (domain model:
Identity & Access context, `User` / `ProjectMembership`), the Identity/Access ADR-0004, and
the Sharing and Real-time ADRs (membership lifecycle and SignalR group mechanics)

## Context

TaskFlow is a connected, multi-user (~10) collaborative web app (constitution v4.0.0).
Every request is authenticated (Google OAuth, admission-gated, ADR-0004) and every data
operation must be authorized. The product carries two distinct access shapes that the data
model already encodes (ENT-02 `Project.ownerId` / `visibility`, ENT-07 `ProjectMembership`,
ENT-01 `Task.createdBy` / `assignees`):

1. **Personal data** — a user's own tasks and unprojected/personal-project entities,
   private by default.
2. **Shared projects** — a project an owner has shared, with a set of members at roles.

Authentication establishes *who* the caller is; it says nothing about *what* they may
touch. Without a deliberate, uniform authorization layer, a missed check is a cross-user
data leak — a correctness and trust failure, not a cosmetic bug. Authorization therefore
needs a single, testable home rather than ad-hoc checks scattered across handlers, and it
must default to denial so that a forgotten check fails closed, not open.

The original (v3.0.0) framing modeled access as a **conjunction**: a per-user ownership
tier *and* a membership+role tier, both applying. The 2026-06-15 design review (ledger B3)
found this conflates two regimes that should be *mutually selected*, not combined: it left
`createdBy`/assignee looking like residual grants on shared data, so that an author or
assignee might appear to retain access after losing membership. Constitution v4.0.0
resolves this by making authorization **dispatched by resource visibility, not a
conjunction**. This ADR is amended to match.

This ADR fixes *how* authorization is modeled and enforced. It does not introduce
organizations/multi-tenancy, guest access, or public share links — those remain out of
scope (OOS-16, OOS-17); the single-team boundary keeps the model to ownership +
per-project membership.

## Decisions

1. **Authorization is dispatched by the containing project's visibility — not a
   conjunction of tiers.** The first act of every authorization decision is to determine
   which regime governs the target, by the **visibility of the project that contains it**
   (for unprojected entities, by the caller-ownership anchor). Exactly one regime governs;
   they are never `AND`-ed together. (FR-065..067)
   - **Personal / unprojected regime (ownership).** For a user's own tasks, personal
     projects, and Inbox/unprojected entities, access is governed solely by the
     **ownership anchor** (`createdBy` / `ownerId`). A caller may read or write only data
     they own; every query is scoped to the caller's identity at the application layer —
     there is no unscoped "fetch by id" path that bypasses the caller.
   - **Shared-project regime (membership + role).** For any entity whose containing
     project has `visibility = shared`, access is governed solely by the caller's
     **current `ProjectMembership` and role** (FR-066, FR-061, FR-067):
     - **viewer** — read-only. May read tasks and comments; MUST NOT write, and MUST NOT
       comment.
     - **editor** — may change tasks and post comments, in addition to all viewer reads.
     - **owner** — full management: invite/remove members, change roles, share/unshare,
       delete the project, in addition to all editor capabilities. Owner is the
       **immutable `ownerId`**, moved only by an explicit transfer-owner command
       (FR-094); it is NOT a freely assignable role (assignable roles are viewer/editor).
     Roles are cumulative and enforced per operation under least privilege: the handler
     authorizes against the *minimum* role the specific action requires (viewer = read,
     editor = write, owner = manage).
   - **`createdBy` and assignee are provenance only.** On a shared-project entity, having
     authored or being assigned a task confers **no standalone access**: they record
     history and drive notifications, never grant or preserve a grant. Consequently, when
     a user **leaves, is removed, or a project is unshared**, that user loses **ALL**
     access to the project's data — regardless of any authorship or assignment they hold.
     There is no residual read or write path through `createdBy`/assignee. (ledger B3)

2. **Deny-by-default.** Authorization fails closed. Every read and write MUST be
   explicitly authorized; the absence of an applicable grant is a denial, never an
   implicit allow. An unauthenticated, non-admitted, or under-privileged request is
   rejected. (FR-068)

3. **Application-layer policy, backed by `ProjectMembership`.** Authorization lives in a
   dedicated application-layer policy (Identity & Access context, ADR-0003), evaluated
   against the caller identity, the target's containing-project visibility and ownership
   anchor, and — for shared targets — the caller's `ProjectMembership` and role. It is NOT
   scattered across UI, controllers, or duplicated per handler, and it is NOT pushed into
   the aggregates: aggregates remain focused on domain invariants (single active cycle,
   one-level nesting, recurrence/status rules), while the policy owns the access decision.
   (constitution Principle IX)

4. **Enforced at every command and query handler.** The check sits at the API/handler
   boundary in the Wolverine command/query pipeline (ADR-0001), so it covers both the
   write side and the CQRS read-projection (query) side uniformly. Read projections are
   scoped through the same policy; a DTO query cannot return data the caller is not
   entitled to. There is no handler — read or write — that executes without an
   authorization decision. (FR-068)

5. **Authorization tests are mandatory, organized around a role×operation deny matrix.**
   Per constitution Principle VIII, integration tests run command/query handlers through
   the real database and MUST cover authorization, including the negative cases. The
   coverage target is made *enumerable* by a **role×operation deny matrix**: for each
   operation (read task, write task, comment, manage members, share/unshare, delete
   project, transfer owner, …) the matrix states the minimum required regime/role and
   therefore which caller states (non-owner on personal data; non-member on a shared
   project; viewer writing or commenting; editor managing members; non-owner transferring
   ownership; author after losing membership) MUST be denied. Every **allow cell** gets an
   allow test and every **deny cell** gets a deny test — the **allow+deny test gate**.
   Deny-by-default is verified, not assumed. These tests gate merging: SC-013 (authz
   enforced on 100% of data operations) and **SC-016** (every handler has both allow and
   deny tests; the role×operation deny matrix is fully covered).

6. **Authorship is a distinct object-level grant — the one principled exception, not an
   ACL.** A comment carries an intrinsic author identity, and **only that author** may
   edit or delete their own comment. This grant is *narrow and structural*: it is the
   author field on the comment itself, not a configurable per-resource access list.
   - Project role does **not** override it: an editor or even the project **owner** may
     not edit or delete another member's comment (they may moderate via the project's
     own affordances where defined, but not impersonate authorship). (ledger B4,
     FR-073, FR-075)
   - Loss of membership **does** override it: a user who has left, been removed, or whose
     project was unshared retains no author right, consistent with Decision 1's revoke-all
     rule. The author grant lives *inside* the shared-project regime; it does not survive
     it.
   This reconciles the prior "no resource-level ACLs" stance (see Notes): authorship is an
   inherent property of the object, decided by identity comparison, not a general grant
   system with its own configuration surface.

7. **Live SignalR subscriptions are authorized too; a membership change evicts.** The
   access decision governs not only request/response handlers but the **live real-time
   stream**. A caller may hold a SignalR subscription to a shared project only while the
   policy would grant them read access to it. When a membership or role change removes or
   downgrades that access, the policy outcome MUST be applied to the affected user's active
   subscriptions **immediately**: the connection is evicted from (or re-checked at fan-out
   for) that project's group, so a removed member receives **no further live patches** and
   is forced to a 403 / no-access re-sync. (ledger B5, FR-095) This ADR governs the
   *access decision* that drives eviction; the SignalR group mechanics and connection
   lifecycle are owned by the Real-time ADR, and the membership-change events that trigger
   it are owned by the Sharing ADR.

## Consequences

- **A single, auditable choke point.** Because the decision is one application-layer
  policy in the Wolverine pipeline rather than per-handler code, the development-workflow
  authorization review (constitution Governance) has one place to inspect, and new data
  operations inherit enforcement by construction. Adding a handler without a policy
  decision is the reviewable defect to guard against.
- **Visibility, not authorship, decides shared access.** The dispatch rule means a task in
  a shared project is governed by the caller's membership and role, never by who created
  it or who it is assigned to. This is the intended fix for the conjunction bug: there is
  no code path where `createdBy`/assignee can be read as a grant on shared data, and
  losing membership is sufficient to lose all access.
- **Ownership and membership fields are load-bearing, not optional.** `createdBy`,
  `ownerId`, `visibility`, and `ProjectMembership` (ENT-01, ENT-02, ENT-07) are required
  inputs to every authorization decision; they cannot be dropped or left nullable without
  breaking the model. Queries must select the containing-project visibility and the
  ownership/membership context needed to authorize.
- **Read paths cost a scope.** Per-user/per-membership isolation means query handlers
  filter by caller identity and membership; there is no global list. This is the intended
  cost of fail-closed isolation and aligns with the CQRS read-projection design (ADR-0001).
- **Role transitions interact with sharing semantics (cross-ADR).** Removing a member,
  changing a role, unsharing, or leaving (FR-062..064) immediately changes what the policy
  grants, clears assignments (FR-059), and evicts live subscriptions (Decision 7); these
  membership changes require explicit confirmation showing their blast radius and are NOT
  covered by the 30-second data undo (constitution Principle VII, FR-064). The mechanics
  of the sharing/membership lifecycle are owned by the Sharing ADR; this ADR governs only
  the access decision that membership/role drive.
- **Viewer "no comment" is an authorization rule, not just UI.** The viewer read-only
  constraint (incl. no commenting, FR-061, FR-072) is enforced server-side at the comment
  command handler; hiding the comment input in the client is a convenience, not the
  control.
- **The deny matrix turns coverage into a checklist.** Authorization tests are no longer a
  judgment call about "enough negative cases": the role×operation matrix names every deny
  cell, and SC-016 requires a deny test for each. A failing or missing deny test blocks
  merge. This is deliberate — the most dangerous authorization bug is the silent allow,
  which only a negative test catches.

## Notes

- **Why not enforce in the aggregate?** Co-locating authorization with invariants would
  entangle "who may act" with "what is a valid state," forcing every aggregate to know
  about identity and membership. Keeping the policy in the application layer preserves the
  aggregates' single responsibility (domain invariants) and keeps the access rule reusable
  across command and query sides.
- **Why no resource-level ACLs or capability tokens — and how authorship fits.** The
  single-team scope (ASM-10, OOS-17) makes ownership + per-project role sufficient as the
  general access model; a configurable ACL system (per-resource grant lists, capability
  tokens) would be premature (YAGNI) and add a configuration surface the constitution
  explicitly resists. The **authorship grant (Decision 6) is not an exception to this**:
  it is not a configurable grant attached to a resource but an *intrinsic identity
  property* of a comment, decided by comparing the caller to the comment's author field —
  no grant tables, no per-resource policy configuration. It is the one place where
  object-level identity, not project role, decides an action, and it stays bounded by the
  shared-project regime (membership loss revokes it). If a future requirement needs true
  per-task or field-level configurable grants, that is a new amendment, not a silent
  extension here.
- **Why authorize live subscriptions and not only requests?** Real-time fan-out is a
  second data path: once subscribed, a client keeps receiving patches without issuing new
  authorized requests. Treating the subscription as un-authorized would let a just-removed
  member keep seeing changes until their next page load — a live data leak. Decision 7
  closes that path so the request side and the stream side enforce the *same* decision.

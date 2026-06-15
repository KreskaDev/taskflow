# ADR-0003 — Domain model (DDD)

**Status:** Accepted (2026-06-14); amended 2026-06-15 to apply the design-review
remediation ledger (`docs/reviews/remediation-decisions.md`) under constitution v4.0.0.
**Builds on:** ADR-0001 (stack: C# + Wolverine + EF Core + Postgres, full tactical DDD)
**Amends:** this ADR's prior multi-user revision — sharpening ownership, authorization
dispatch, undo persistence, recurrence, and time per the ledger; and the single-user
domain model that revision itself superseded.
**Driven by:** constitution v4.0.0 (Principles IX Authentication & Authorization,
X Time & Timezone, XI Privacy & Personal Data, XII Security by Default) and the
2026-06-15 remediation ledger (blockers B1–B4, B6, B7/B8, B10, B15).
**Scope:** the backend domain model shared by all slice plans. Not anemic CRUD —
aggregates own behavior and invariants; authorization is a first-class application-layer
concern. Out of scope here: SignalR transport, deployment, and the sharing/real-time/
notification *delivery* mechanics (ADR-0002, ADR-0004..0009).

## Context

TaskFlow is a connected, **multi-user** collaborative web app for a small team (~10), not
the single-user, no-auth product the original model assumed. Users sign in with Google
(admission-gated, Principle IX), own personal data by default, and share projects with
role-scoped collaborators who can be assigned tasks, comment, @mention, and receive
notifications.

The 2026-06-15 design review resolved a set of domain-level ambiguities that this revision
now bakes into the model. They cluster around five questions: **who owns a project and how
ownership moves** (B1); **what authorizes access to an entity** (B3); **how undo is
persisted** (B7/B8); **how recurrence and its server-side spawn continue a chain** (B15);
and **how time is stored and computed** (B10). Three smaller resolutions sharpen specific
entities: **Label and Cycle ownership/authz anchors** (B2), **comment authorship as an
object-level grant** (B4), and **Notification sources as live references** (B6).

Two architectural facts are unchanged. First, **identity and access are first-class**:
every aggregate that holds user data carries ownership/membership, and every command and
query is authorized deny-by-default. Second, the model stays **event-driven and
state-stored** without a multi-tenancy dimension (no organization/tenant; constitution
Constraints & Scope).

The core modelling decisions below are **retained**; they are restated and amended only
where the ledger touches them. The MVP feature scope is unchanged in kind — extended for
collaboration.

## Bounded contexts

Two contexts (constitution Architecture & Stack):

- **Task Management** — aggregate roots `Task`, `Project`, `Cycle`; the `Label`, `Comment`,
  `Notification`, and **`UndoSnapshot`** entities; value objects; the task/project/cycle
  domain events. Owns the task-domain invariants (single active cycle, one-level nesting,
  recurrence/status rules).
- **Identity & Access** — aggregate root `User`; and the application-layer **Authorization
  policy** that consumes `ProjectMembership` (owned by the Project aggregate, see below).
  This context answers "who is the caller" and "may this caller perform this operation,"
  and is consulted by every command/query in Task Management. It adds no
  organization/tenant concept.

Contexts and aggregates reference each other **by ID only** — never by direct object graph
across a boundary.

## Aggregates, entities, value objects

- **Task** *(aggregate root, Task Management)* — title (required), description, priority,
  status, due date (see Decision 8), references (by ID) to project, cycle, and labels, and
  the recurrence rule. `createdBy` (the `User` who created it, required, **immutable**) and
  `assignees` (zero or more current-member `User` IDs). `createdBy` and `assignee` are
  **provenance only** — they record history and route notifications, but confer **no
  standalone access** (Decision 6). Assignment exists **only on tasks in shared projects**
  (FR-002, FR-069); personal-project tasks carry no assignees. Owns timestamp rules and the
  `deleted_at` soft-delete tombstone (Decision 3). (ENT-01)
  - **RecurrenceRule** *(value object, part of the Task aggregate)* — frequency type
    (daily / every-N-days / specific-weekdays / monthly with end-of-month clamping) +
    parameters, plus the **anchor** (the original due date the schedule is computed from).
    Requires a due date; recurrence is unavailable without one. (ENT-05)
- **Project** *(aggregate root, Task Management)* — name, color, icon, optional parent
  project **ID**, archived flag. `ownerId` (the owning `User`, required, **immutable** —
  the single owner; see Decision 7) and `visibility` ∈ {personal, shared}; new projects
  default to **personal** (FR-057). A shared project owns a **`ProjectMembership` set** —
  the sharing state of that project. (ENT-02)
- **Cycle** *(aggregate root, Task Management)* — start/end dates, status
  (planned / active / closed), carried-over handling. Cycles are **team-wide** (a single
  shared instance, ASM-10), not per-user; create/activate/close/delete are explicitly
  authorized team operations (Decision 6), and the global active-cycle invariant is kept
  (Decision 2). (ENT-03)
- **Label** *(entity, Task Management)* — name, optional color, and **`ownerId`** (Tier A
  per-user: a label belongs to the user who created it). *Applying* a label to a task
  follows the **task's** authorization, not the label's. Many-to-many with tasks via a
  join. (ENT-04)
- **User** *(aggregate root, Identity & Access)* — Google identity: subject id, email,
  display name, avatar. The trace anchor for ownership, membership, assignment, comment
  authorship, label ownership, and notification recipiency. (ENT-06)
- **ProjectMembership** *(entity)* — links a `User` (by ID) to a shared `Project` with a
  **role** ∈ {editor, viewer}. **Owner is not a membership role** — it is the immutable
  `Project.ownerId` (Decision 7). Modelled as part of the Project aggregate (it is that
  project's sharing state and changes transactionally with the project); the Identity &
  Access **Authorization policy** *consumes* the membership set to decide access. (ENT-07)
- **Comment** *(entity, Task Management)* — author `User` (by ID), parent `Task` (by ID),
  body, `@mention` references (typed User-ID tokens), created/edited timestamps. Lives only
  on tasks in shared projects. **Authorship is a distinct object-level grant** (Decision 9).
  (ENT-08)
- **Notification** *(entity, Task Management)* — recipient `User` (by ID), type
  ∈ {assigned, mentioned, changed}, a **source reference (by ID) that is a live reference,
  not a snapshot** (Decision 10), read flag, created timestamp. (ENT-09)
- **UndoSnapshot** *(entity, Task Management)* — a typed, persisted pre-action snapshot /
  tombstone record of the affected aggregates' prior state, the originating `actorUserId`,
  the action kind, and an expiry timestamp (the 30 s window). Backs undo of bulk and
  multi-aggregate destructive actions; restore replays it as a normal write (Decision 3).
  (ENT-10)
- **Value objects:** `Priority` (P0–P3), `TaskStatus` and `CycleStatus` enums, `Role`
  (editor/viewer), `Visibility` (personal/shared), `DateRange` (cycle span), `DueDate`
  (UTC instant + `has_time` flag, Decision 8), `RecurrenceRule` (with anchor).

## Invariants (enforced inside the aggregate unless noted)

- **Task:** status ∈ {backlog, todo, in_progress, done, cancelled}, default backlog
  (FR-003); `completed_at` set iff status=done (FR-004); recurrence carry-forward rules
  (FR-008) — the successor copies fields **including `assignees` and the recurrence rule +
  anchor** (so the chain continues) except status (reset), timestamps (fresh), and cycle
  (unassigned); next due is computed from the **anchor**, not completion (ASM-09); the spawn
  is **idempotent** (at most one successor per instance); cancelling stops recurrence
  (FR-010), and undoing a completion removes the successor. **Assignees are permitted only
  when the task's project is shared** (FR-069) and **must be current members** of that
  project; `createdBy` and `ownerId`/`createdBy` provenance is immutable.
- **Project:** one-level nesting only — a project whose parent already has a parent is
  rejected (FR-012); re-parenting allowed within that rule; archive
  hides-but-keeps-searchable (FR-013). `ownerId` is set at creation, is **immutable**, and
  moves only via the **transfer-owner** command (Decision 7); a project always has exactly
  one owner. Membership entries exist only while `visibility = shared` and carry roles
  editor/viewer only.
- **Cycle:** lifecycle planned → active → closed; an active cycle cannot be deleted
  (FR-019); planned/closed cycles deletable only if they have no assigned tasks (FR-020);
  team-wide, single active instance (Decision 2).
- **Task ↔ Cycle:** a task belongs to at most one cycle (FR-016).
- **Comment:** body length-bounded and non-empty (FR-098); a comment is editable/deletable
  only by its **author** (Decision 9). Membership-based read/comment rights are checked by
  the Authorization policy, not stored on the comment.
- **Soft-delete:** a soft-deleted (`deleted_at` set) aggregate is excluded from all
  authorization-scoped queries and is reaped after the 30 s undo window (Decision 3).

## Decision 1 — Cross-aggregate consistency: **event-driven (eventual)**

One aggregate is modified per transaction. Effects that span aggregates are published as
**domain events** and applied by handlers, delivered reliably through **Wolverine's
transactional outbox** (over the configured transport — Postgres-backed local queues by
default; RabbitMQ only when a real cross-process consumer exists, per constitution v4.0.0).
Eventual-consistency windows are small and acceptable for a ~10-user team.

Events and their (single-step) handlers:

*Task / project / cycle lifecycle (retained):*
- `TaskCompleted` → if recurring and on/after the anchor due date, generate the next
  instance, carrying the **recurrence rule + anchor + assignees** forward (FR-008);
  idempotent.
- `TaskCompletedEarly` → schedule a **server-side Wolverine scheduled job** to generate the
  instance on/after the original (anchor) due date (FR-009) — not a client-startup task and
  not a saga; idempotent (≤1 successor/instance), and re-validates carried assignees against
  current membership (no re-notification).
- `ProjectDeleted` → apply the chosen policy to its tasks: cascade-delete / move-to-Inbox /
  archive (FR-014); cascade-delete is undoable (Decision 3).
- `CycleClosed` → roll incomplete tasks over per the chosen option: next / backlog /
  carried-over (FR-018); rollover is undoable (Decision 3).

*Collaboration (new):*
- `TaskAssigned` → carries the assignee **delta** + `actorUserId`; generates an `assigned`
  Notification for each newly added assignee (self-assignment allowed, no self-notification)
  (FR-070, FR-079).
- `UserMentioned` → generate a `mentioned` Notification for the @mentioned member
  (FR-074, FR-079).
- `ProjectShared` → the project becomes shared; the inviting owner's authority is
  established and editor/viewer memberships are created (FR-058).
- `ProjectUnshared` → all memberships are removed, **every assignment on that project's
  tasks is cleared** (FR-059), and visibility reverts to personal.
- `OwnerTransferred` → `Project.ownerId` moves to the named member via the transfer-owner
  command; the prior owner becomes an editor unless otherwise specified (Decision 7).
- `MembershipRevoked` → a member is removed / leaves / is demoted; that user's assignments
  on the project's tasks are cleared and **all** their access to the project's data is
  revoked (Decision 6). (Live SignalR eviction is the delivery concern of ADR-0004/0006.)

A `changed` Notification (FR-079) is raised by a handler on the closed trigger set
(status / due date / assignee / project-move), coalesced for rapid changes and suppressed
for self-actions via `actorUserId`. Notification generation is therefore a set of
**single-step event handlers** — there is no saga (see below).

## Decision 2 — "Single active cycle": **domain service + DB partial unique index**

A set-based invariant across all (team-wide) Cycle instances, enforced outside the
aggregate:

- Domain service **`CycleActivation`** owns the activation workflow and expresses the rule
  in the model. **Activation policy: a cycle may become active only when no other cycle is
  active — the current active cycle must be closed first** (reject otherwise), matching the
  explicit close lifecycle (FR-019/FR-020). "Next cycle" is the next *planned* cycle by
  start date; planned → active is a manual activation.
- A PostgreSQL **partial unique index** — `UNIQUE (is_active) WHERE is_active = true` — is
  the hard backstop guaranteeing the invariant even under a multi-tab race or a bypassing
  code path. Because cycles are team-wide, this is a **global** index (no per-user scoping).
  DB violations are translated to a domain error.

## Decision 3 — Undo (FR-040): **soft-delete + persisted snapshot, restore is a write under LWW**

Undo is persisted from day one, not a retrofit, and restore is a normal optimistic write —
not a privileged rewind.

- **Single-aggregate destructive deletes use soft-delete.** Task delete and project delete
  set a **`deleted_at`** tombstone (ENT-01/ENT-02). Soft-deleted rows are **excluded from
  all authorization-scoped queries** and are **reaped after the 30 s window** (FR-097),
  making the deletion permanent. Shipping soft-delete in slice 002 means the undo slice
  (014) is a pure retrofit, and the optimistic-delete rollback UX is simply "the row
  reappears" plus an FR-049 message.
- **Multi-aggregate / bulk destructive actions use a persisted `UndoSnapshot`** (ENT-10):
  bulk move, bulk status change, cascade project-delete, and cycle rollover capture a typed
  snapshot of the affected aggregates' prior state. Each destructive action creates an
  **independent** snapshot with its own 30 s timer (EC-09); an expiry sweep purges it.
- **Restore is a normal write subject to last-write-wins** (Principle VII; B7). "Fully
  restores previous state" holds **only in the no-concurrent-edit case**; when restore
  overwrites a concurrent edit it **surfaces that** (FR-049). Restore **fans out over
  SignalR**, restores a child to Inbox/backlog with a message if its parent was deleted, and
  may be performed **only by the original actor**, scoped by that actor's **current** role.
- **Membership and role changes are excluded from this undo.** Sharing, unsharing, inviting,
  transferring ownership, changing a role, removing a member, and leaving (FR-058..FR-064,
  FR-094) are **confirm-gated** — they require an explicit confirmation dialog (which MUST
  show its blast radius) before taking effect and are **not** captured as undoable snapshots
  (constitution Principle VII; FR-064). Data is undoable for 30 s; access changes are
  confirmed once and committed.

## Decision 4 — Identifiers: **UUIDv7**

All aggregate roots (including `User`) use **UUIDv7** — time-ordered (good index locality,
unlike random v4) and client-generatable, which enables clean optimistic creates before the
server confirms.

## Decision 5 — Persistence: **state-stored (EF Core), not event-sourced**

Aggregates are state-stored via EF Core code-first migrations over PostgreSQL. Domain events
are **transient notifications** dispatched via Wolverine — they are not the source of truth
and are not an event store. (Confirmed by the ADR-0001 stack choice; Marten/event-sourcing
was not adopted.) `ProjectMembership`, `Label`, `Comment`, `Notification`, and
`UndoSnapshot` are likewise state-stored rows.

## Decision 6 — Authorization: **deny-by-default, dispatched by resource visibility**

Authorization is a first-class, tested concern living in the **application layer** of the
Identity & Access context (Principle IX; FR-065..FR-068). It is **not** scattered ad hoc and
does **not** live in the aggregates, which stay focused on domain invariants.

- **Invoked by every command and query.** No read or write reaches an aggregate or a read
  projection without passing the policy. The policy is **deny-by-default** — absence of an
  explicit grant denies (FR-068).
- **Dispatch by visibility, not a conjunction of tiers** (B3). The containing project's
  visibility selects the rule:
  - *Personal / unprojected data (Inbox), and per-user Labels* authorize on **ownership**
    (`createdBy` / `ownerId`); every query is scoped to the caller's identity (FR-065).
  - *Shared-project entities* authorize on **current `ProjectMembership` + role** (FR-066,
    FR-067):
    - **viewer** — read-only; **no commenting**;
    - **editor** — change tasks, assign, comment, @mention;
    - **owner** (the immutable `Project.ownerId`, not a role) — all of editor, plus manage
      members, share/unshare, transfer ownership, and delete the project.
- **`createdBy` and assignee are provenance only** and confer **no standalone access** (B3).
  On leave / remove / unshare, a user loses **all** access to that project's data regardless
  of authorship or assignment, and their assignments are cleared (event `MembershipRevoked`
  / `ProjectUnshared`).
- **Team-wide Cycle operations** (create/activate/close/delete) are explicitly authorized as
  team operations (B2), not scoped to a single owner.
- The policy reads `Project.ownerId`, `Project.visibility`, and the `ProjectMembership` set
  to decide. Because it gates assignment and commenting, it is also where "assignees only on
  shared projects" (FR-069) and "viewers cannot comment" (FR-072) are enforced at the
  boundary.

## Decision 7 — Ownership: **immutable single `ownerId`, moved only by transfer-owner**

`Project.ownerId` is the **single, immutable owner** and the source of unshare / delete /
transfer authority (B1; FR-094).

- **Roles are editor/viewer only.** Owner is **not** a freely assignable membership role; it
  cannot be granted by promotion (FR-061/FR-062).
- **Ownership moves only via an explicit `transfer-owner` command** (event
  `OwnerTransferred`), targeting a current member; the command is confirm-gated (Decision 3)
  and not undoable.
- **Last-owner guard.** The sole owner cannot be removed, demoted, or leave; such an attempt
  is rejected with a recoverable FR-049 error. Ownership must be transferred first.

## Decision 8 — Time: **UTC storage, `Europe/Warsaw` reference, `has_time` flag**

Time follows constitution Principle X and ledger B10.

- All timestamps are stored in **UTC** (`timestamptz`).
- The `DueDate` value object carries a **`has_time` flag** distinguishing a date-only due
  date from a date-time one.
- Every date-relative computation — "Today" / "Upcoming" membership, cycle boundaries, and
  recurrence rollover ("on or after the due date") — is evaluated against the single
  **instance reference timezone `Europe/Warsaw`**, applied identically on client and server.
  "Equal to today" = same calendar day in Europe/Warsaw; a task due tomorrow appears in
  Upcoming, not Today.
- Natural-language date entry is parsed **client-side** for the optimistic paint; the client
  sends a **resolved ISO instant + offset**, and the **server validates but does not
  re-parse** Polish. DST is handled by the timezone library, never fixed-offset arithmetic.

## Decision 9 — Comment authorship: **object-level grant**

A comment's authorship is a **distinct object-level grant** (B4), independent of project
role:

- The **author — and only the author** — may edit or delete their own comment; **project
  role (including owner) does not override** this (FR-073/FR-075).
- **Loss of membership overrides the author right**: a user who is no longer a member of the
  project loses the ability to act on their comments there.
- Comment bodies are length-bounded, non-empty, plain-text + a safe markdown subset, and
  output-sanitized; `@mention` is stored as a **typed token referencing a User id** (FR-098;
  Principle XII).

## Decision 10 — Notification source: **live reference, re-authorized on access**

A `Notification.source` is a **live reference (by ID), re-checked at dereference time, not a
content snapshot** (B6; ENT-09).

- Resolving a notification source **re-runs deny-by-default authorization**; the stored
  payload carries no content beyond what current authz permits.
- If access has since been lost (membership revoked, project unshared, source deleted), the
  dereference returns **"no longer available"** rather than stale content (FR-096).

## Sagas / process managers: **not used in the MVP** (YAGNI)

Every cross-aggregate flow above — lifecycle effects, recurrence spawn, and notification
generation — is a single-step event handler or a Wolverine scheduled message; none is a
stateful, multi-step, compensating workflow. A Wolverine **saga** would add persisted-process
state with no current justification. The pattern is held in reserve: if a genuine multi-step
process appears later, a saga slots into the existing Wolverine + outbox setup without rework.

## Consequences

- Slice plans (`/speckit-plan`) inherit this model. Identity is foundational: slice 001
  (accounts-and-auth) stands up the `User` aggregate and the authorization policy seam;
  slice 002 stands up the `Task` aggregate (title/status/timestamps + `createdBy` + UUIDv7 +
  **soft-delete `deleted_at` from day one**); later slices add Project + immutable owner
  (004), sharing/membership + the role policy + transfer-owner (007), assignment +
  `TaskAssigned` (008), comments/@mentions + authorship grant + `UserMentioned` (009), Cycle
  team-wide (011), recurrence + rule/anchor carry-forward + scheduled spawn (012),
  `UndoSnapshot` + restore-under-LWW (014), and notification handlers + live-reference
  re-auth (017).
- **Every** command/query handler is written against the dispatch-by-visibility policy from
  the start; authorization is covered by integration tests with **both allow and deny cases**
  (Principle VIII; SC-013, SC-016).
- CQRS read projections (ADR-0001) serve the views (Today/Upcoming/Board/Cycle/Assigned-to-me)
  off the same Postgres database, separate from the write aggregates, and are **scoped by the
  same authorization policy** and **exclude soft-deleted rows** — a read projection never
  returns rows the caller may not see.
- Undo is durable: single deletes via `deleted_at`, bulk/multi-aggregate via `UndoSnapshot`,
  restore as an LWW write by the original actor — so concurrent collaboration and the 30 s
  window coexist correctly.
- The model adds ownership/membership, immutable single-owner projects, team-wide cycles,
  per-user labels, and a UTC-with-Warsaw time rule, but **no organization/tenant dimension**;
  the single-team scope keeps authorization tractable (ownership + per-project membership).

## Notes

- **Privacy cascade (Principle XI, FR-085).** Account deletion and member departure use the
  same residual-attribution rule the events above already imply: authored comments are
  anonymized to a tombstone identity (not hard-deleted where they anchor a thread),
  `createdBy`/assignee references are nulled or reassigned to the tombstone, owned shared
  projects are transferred or deleted, and the user's notifications are purged. This is an
  application-layer concern over these aggregates, detailed in US-17 / slice 015.
- **`MembershipRevoked` vs live transport.** This ADR raises the domain event and clears
  assignments/access; the SignalR connection eviction and forced re-sync it triggers
  (B5/FR-095) are the delivery concern of ADR-0004/ADR-0006, kept out of the domain model
  deliberately.

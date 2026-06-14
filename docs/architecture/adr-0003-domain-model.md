# ADR-0003 — Domain model (DDD)

**Status:** Accepted (2026-06-14)
**Builds on:** ADR-0001 (stack: C# + Wolverine + EF Core + Postgres, full tactical DDD)
**Amends:** the single-user domain model of this ADR's prior revision
**Driven by:** constitution v3.0.0 (Collaborative, Multi-User; Principle IX
Authentication & Authorization) — this is the "Phase B" domain-model work the
constitution's Sync Impact Report defers to here.
**Scope:** the backend domain model shared by all slice plans. Not anemic CRUD —
aggregates own behavior and invariants; authorization is a first-class application-layer
concern. Out of scope here: SignalR transport, deployment, and the sharing/real-time/
notification *delivery* mechanics (ADR-0002, ADR-0004..0008).

## Context

TaskFlow is now a connected, **multi-user** collaborative web app for a small team (~10),
not the single-user, no-auth product the prior model assumed. Users sign in with Google,
own personal data by default, and share projects with role-scoped collaborators who can be
assigned tasks, comment, @mention, and receive notifications.

Two things change at the domain level. First, **identity and access become first-class**:
every aggregate that holds user data now carries ownership/membership, and every command
and query is authorized deny-by-default (Principle IX, FR-065..068). Second, **collaboration
adds aggregates and events** — sharing, assignment, comments/mentions, and notifications —
that must fit the existing event-driven, state-stored model without introducing
multi-tenancy (no organization/tenant dimension; constitution Constraints & Scope).

The five core modelling decisions below are **retained**; they are restated and, where the
multi-user move touches them, amended. The MVP feature scope is unchanged in kind — it is
extended for collaboration.

## Bounded contexts

Two contexts (constitution Architecture & Stack):

- **Task Management** — aggregate roots `Task`, `Project`, `Cycle`; the `Comment` and
  `Notification` entities; value objects; the task/project/cycle domain events. Owns the
  task-domain invariants (single active cycle, one-level nesting, recurrence/status rules).
- **Identity & Access** — aggregate root `User`; and the application-layer **Authorization
  policy** that consumes `ProjectMembership` (owned by the Project aggregate, see below).
  This context answers "who is the caller"
  and "may this caller perform this operation," and is consulted by every command/query in
  Task Management. It adds no organization/tenant concept.

Contexts and aggregates reference each other **by ID only** — never by direct object graph
across a boundary.

## Aggregates, entities, value objects

- **Task** *(aggregate root, Task Management)* — title (required), description, priority,
  status, due date, references (by ID) to project, cycle, and labels, and the recurrence
  rule. **New:** `createdBy` (the `User` who created it, required) and `assignees` (zero or
  more `User` IDs). Assignment exists **only on tasks in shared projects** (FR-002, FR-069);
  personal-project tasks carry no assignees. Owns timestamp rules. (ENT-01)
  - **RecurrenceRule** *(value object, part of the Task aggregate)* — frequency type
    (daily / every-N-days / specific-weekdays / monthly) + parameters. (ENT-05)
- **Project** *(aggregate root, Task Management)* — name, color, icon, optional parent
  project **ID**, archived flag. **New:** `ownerId` (the owning `User`, required) and
  `visibility` ∈ {personal, shared}; new projects default to **personal** (FR-057). A shared
  project owns a **`ProjectMembership` set** — the sharing state of that project. (ENT-02)
- **Cycle** *(aggregate root, Task Management)* — start/end dates, status
  (planned / active / closed), carried-over handling. (ENT-03)
- **Label** *(entity, Task Management)* — name, optional color; many-to-many with tasks via a
  join. The simplest, most CRUD-like element. (ENT-04)
- **User** *(aggregate root, Identity & Access)* — Google identity: subject id, email,
  display name, avatar. The trace anchor for ownership, membership, assignment, comment
  authorship, and notification recipiency. (ENT-06)
- **ProjectMembership** *(entity)* — links a `User` (by ID) to a shared `Project` with a
  **role** ∈ {owner, editor, viewer}. Modelled as part of the Project aggregate (it is that
  project's sharing state and changes transactionally with the project); the Identity &
  Access **Authorization policy** *consumes* the membership set to decide access. (ENT-07)
- **Comment** *(entity, Task Management)* — author `User` (by ID), parent `Task` (by ID),
  body, `@mention` references (User IDs), created/edited timestamps. Lives only on tasks in
  shared projects. (ENT-08)
- **Notification** *(entity, Task Management)* — recipient `User` (by ID), type
  ∈ {assigned, mentioned, changed}, a source reference (by ID), read flag, created
  timestamp. (ENT-09)
- **Value objects:** `Priority` (P0–P3), `TaskStatus` and `CycleStatus` enums, `Role`
  (owner/editor/viewer), `Visibility` (personal/shared), `DateRange` (cycle span),
  `RecurrenceRule`.

## Invariants (enforced inside the aggregate unless noted)

- **Task:** status ∈ {backlog, todo, in_progress, done, cancelled}, default backlog
  (FR-003); `completed_at` set iff status=done (FR-004); recurrence carry-forward rules
  (FR-008) — next instance copies fields **including `assignees`** except status (reset),
  timestamps (fresh), and cycle (unassigned); next due computed from original due, not
  completion (ASM-09); cancelling stops recurrence (FR-010). **Assignees are permitted only
  when the task's project is shared** (FR-069); `createdBy` is immutable.
- **Project:** one-level nesting only — a project whose parent already has a parent is
  rejected (FR-012); archive hides-but-keeps-searchable (FR-013). `ownerId` is set at
  creation; a project always has exactly one owner. Membership entries exist only while
  `visibility = shared`.
- **Cycle:** lifecycle planned → active → closed; an active cycle cannot be deleted
  (FR-019); planned/closed cycles deletable only if they have no assigned tasks (FR-020).
- **Task ↔ Cycle:** a task belongs to at most one cycle (FR-016).
- **Comment:** only an editor/owner of the parent task's project may exist as a comment
  author; a comment is editable/deletable only by its author (FR-072, FR-075). (Role is
  checked by the Authorization policy below, not stored on the comment.)

## Decision 1 — Cross-aggregate consistency: **event-driven (eventual)**

One aggregate is modified per transaction. Effects that span aggregates are published as
**domain events** and applied by handlers, delivered reliably through **Wolverine's
transactional outbox** (over RabbitMQ). Eventual-consistency windows are small and
acceptable for a ~10-user team.

Events and their (single-step) handlers:

*Task / project / cycle lifecycle (retained):*
- `TaskCompleted` → if recurring and on/after due, generate the next instance, carrying
  assignees forward (FR-008).
- `TaskCompletedEarly` → schedule a **delayed message** to generate the instance on/after
  the original due date (FR-009) — a Wolverine scheduled message, not a saga.
- `ProjectDeleted` → apply the chosen policy to its tasks: cascade-delete / move-to-Inbox /
  archive (FR-014).
- `CycleClosed` → roll incomplete tasks over per the chosen option: next / backlog /
  carried-over (FR-018).

*Collaboration (new):*
- `TaskAssigned` → generate an `assigned` Notification for each newly added assignee
  (FR-070, FR-079).
- `UserMentioned` → generate a `mentioned` Notification for the @mentioned member
  (FR-074, FR-079).
- `ProjectShared` → the project becomes shared; the inviting owner's membership is
  established (FR-058).
- `ProjectUnshared` → all non-owner memberships are removed, and **every non-owner
  assignment on that project's tasks is cleared** (FR-059); visibility reverts to personal.

Notification generation is therefore a set of **single-step event handlers** — there is no
saga (see below). A `changed` Notification (FR-079) is likewise raised by a handler on the
relevant task-change event for users who are assignees.

## Decision 2 — "Single active cycle": **domain service + DB partial unique index**

A set-based invariant across all Cycle instances (a single Cycle can't see its siblings), so
it is enforced outside the aggregate:

- Domain service **`CycleActivation`** owns the activation workflow and expresses the rule in
  the model. **Activation policy: a cycle may become active only when no other cycle is
  active — the current active cycle must be closed first** (reject otherwise), matching the
  explicit close lifecycle (FR-019/FR-020).
- A PostgreSQL **partial unique index** — `UNIQUE (is_active) WHERE is_active = true` — is the
  hard backstop guaranteeing the invariant even under a multi-tab race or a bypassing code
  path. DB violations are translated to a domain error.

## Decision 3 — Undo (FR-040): **pre-action snapshot (memento), DATA only**

Before any destructive command **on task/project data** (task delete, project delete, bulk
move, bulk status change, cycle rollover), capture a typed **snapshot of the affected
aggregates' prior state**. Undo restores the snapshot. Each destructive action creates an
**independent** snapshot entry with its own 30-second timer (EC-09); an expiry sweep purges
snapshots past the window, making the deletion permanent. This is an application-layer
concern layered over the aggregates, uniform across all destructive **data** actions.
Soft-delete is therefore **not** the mechanism (it wouldn't cover bulk moves / status
changes uniformly).

**Membership and role changes are excluded from this undo.** Sharing, unsharing, inviting,
changing a role, removing a member, and leaving (FR-058..FR-064) are **confirm-gated** — they
require an explicit confirmation dialog before taking effect and are **not** captured as
undoable snapshots (constitution Principle VII; FR-064). This keeps a clear line: data is
undoable for 30 s; access changes are confirmed once and committed.

## Decision 4 — Identifiers: **UUIDv7**

All aggregate roots (including `User`) use **UUIDv7** — time-ordered (good index locality,
unlike random v4) and client-generatable, which enables clean optimistic creates before the
server confirms.

## Decision 5 — Persistence: **state-stored (EF Core), not event-sourced**

Aggregates are state-stored via EF Core code-first migrations over PostgreSQL. Domain events
are **transient notifications** dispatched via Wolverine — they are not the source of truth
and are not an event store. (Confirmed by the ADR-0001 stack choice; Marten/event-sourcing
was not adopted.) `ProjectMembership`, `Comment`, and `Notification` are likewise state-stored
rows.

## Decision 6 (new) — Authorization: **deny-by-default application-layer policy**

Authorization is a first-class, tested concern living in the **application layer** of the
Identity & Access context (Principle IX; FR-065..FR-068). It is **not** scattered ad hoc and
does **not** live in the aggregates, which stay focused on domain invariants.

- **Invoked by every command and query.** No read or write reaches an aggregate or a read
  projection without passing the policy. The policy is **deny-by-default** — absence of an
  explicit grant denies (FR-068).
- **Per-user isolation.** A caller may only read or write data they own — their tasks, their
  personal projects. Every query is scoped to the caller's identity (FR-065).
- **Membership + role.** For a shared project, access requires **membership** in that project
  (FR-066), and the operation requires a **sufficient role** (FR-067):
  - **viewer** — read-only; **no commenting**;
  - **editor** — change tasks, assign, comment, @mention;
  - **owner** — all of editor, plus manage members, share/unshare, and delete the project.
  Least privilege is enforced per operation; an insufficient role is denied.
- The policy reads `Project.ownerId`, `Project.visibility`, and the `ProjectMembership` set
  to decide. Because it gates assignment and commenting, it is also where "assignees only on
  shared projects" (FR-069) and "viewers cannot comment" (FR-072) are enforced at the
  boundary.

## Sagas / process managers: **not used in the MVP** (YAGNI)

Every cross-aggregate flow above — lifecycle effects **and** notification generation — is a
single-step event handler or a scheduled message; none is a stateful, multi-step,
compensating workflow. A Wolverine **saga** would add persisted-process state with no current
justification. The pattern is held in reserve: if a genuine multi-step process appears later,
a saga slots into the existing Wolverine + outbox setup without rework.

## Consequences

- Slice plans (`/speckit-plan`) inherit this model. Identity is foundational: slice 001
  (accounts-and-auth) stands up the `User` aggregate and the authorization policy seam;
  slice 002 stands up the `Task` aggregate (title/status/timestamps + `createdBy` + UUIDv7 +
  snapshot-ready delete); later slices add Project (004), sharing/membership + the role
  policy (007), assignment + `TaskAssigned` (008), comments/@mentions + `UserMentioned` (009),
  Cycle (011), recurrence (012), undo snapshots (014), and notification handlers (017).
- **Every** command/query handler is written against the authorization policy from the start;
  authorization is covered by integration tests (Principle VIII; SC-013), including
  deny cases.
- CQRS read projections (ADR-0001) serve the views (Today/Upcoming/Board/Cycle/Assigned-to-me)
  off the same Postgres database, separate from the write aggregates, and are **scoped by the
  same authorization policy** — a read projection never returns rows the caller may not see.
- The model adds ownership/membership but **no organization/tenant dimension**; the single-
  team scope keeps authorization tractable (ownership + per-project membership).

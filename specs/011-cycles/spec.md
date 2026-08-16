# Feature Specification: Cycles

**Feature Branch**: `011-cycles`

**Created**: 2026-06-13

**Updated**: 2026-08-16 — aligned to constitution v5.1.0 (Principle I: UI-First Operability) and
the product-vision UI-first reinterpretation clause; slice 019 (ui-design-system) executed BEFORE
this slice and removed the single-key shortcut system (FR-111, OOS-20), so the `#` and `G C`
triggers named in US-05's scenarios are reinterpreted as visible-UI-control triggers with
unchanged behavior. Slice 010 (project-board-kanban) shipped the List group-by control with
status/priority dimensions and deferred the by-cycle dimension of FR-024 to this slice.

**Status**: Draft

**Input**: Slice 011 of the TaskFlow MVP. Source of truth: `.specify/memory/product-vision.md`. Goal: 2-week cycles (sprints) — assigning tasks to a cycle, the cycle metrics view (% done, days remaining, status breakdown), end-of-cycle rollover of unfinished tasks, and deletion guards that prevent removing an active cycle. Cycle assignment and navigation are reachable through visible affordances (the task "⋯" menu and the sidebar's active-cycle entry) per the UI-first reinterpretation clause. This slice completes the by-cycle grouping of the project List view deferred from slice 010, and builds on the daily-planning task model (005), the project Board/List views (010), and the slice-019 UI catalog.

## Provenance

This slice realizes the following items from product-vision.md:

Slice-specific:
- US-05 (Cycle Management & Review) — full: AS-01, AS-02, AS-03, AS-04, AS-05, AS-06, AS-07
- FR-015 (cycles with default 2-week duration, configurable in settings)
- FR-016 (each task belongs to at most one cycle, or none = backlog)
- FR-017 (active cycle visible in the sidebar)
- FR-018 (rollover options for incomplete tasks at cycle close)
- FR-019 (prevent deletion of an active cycle)
- FR-020 (create/edit/close/delete cycles; deletion guards)
- FR-024 (Project List view group-by **cycle** dimension) — the by-cycle grouping deferred from slice 010 is OWNED and completed here, now that cycle assignment exists (group-by status/priority shipped in 010)
- FR-026 (Cycle view: % done, days remaining, breakdown by status)
- EC-04 (deleting an active cycle is prevented)
- EC-10 ("backlog" naming distinction: task status vs. cycle backlog)
- EC-12 (archiving a project with cycle-assigned tasks)
- ENT-03 (Cycle)

Cross-cutting (realized in this slice):
- FR-103 (every operation reachable through a visible affordance — UI-first operability; per the
  slicing rule it is realized in every slice that adds an operation)
- FR-108 (task rows/cards expose quick actions + a complete "⋯" menu; the cycle-assignment
  action joins the shared menu contract established in slices 019/010)
- FR-110 (empty states present a hint + action; applies to the empty Cycle view, the empty
  cycle selector, and empty by-cycle List groups)
- FR-042 (visible focus indicator)
- FR-043 (ARIA roles/labels)
- FR-044 (text contrast ≥ 4.5:1)
- FR-045 (no collision with assistive-technology bindings)
- FR-046 (no hover-only content)
- FR-047 (prefers-reduced-motion)
- FR-101 (ARIA-live for server-initiated updates/toasts + dialog focus contract)
- FR-099 (output-sanitize user-authored content — the cycle name; CSP/security headers)
- FR-049 (error message + recovery action)
- FR-050 (structured error logging)
- FR-051 (auto-backup before migration — infrastructure in place)

Access control (realized in this slice — verbatim from product-vision.md):
- **FR-065**: Authorization MUST be dispatched by the containing resource's visibility (not a conjunction of tiers): personal/unprojected data authorizes on ownership (`createdBy`/`ownerId`) with queries scoped to the caller; shared-project entities authorize on current `ProjectMembership` + role. Every query MUST be scoped accordingly (per-user isolation).
- **FR-066**: Access to a shared project's data MUST require current membership in that project. `createdBy` and assignee are provenance only and confer NO standalone access; on leave/remove/unshare a user MUST lose ALL access to that project's data regardless of authorship or assignment.
- **FR-067**: Each operation on a shared-project resource MUST require sufficient role (viewer=read, editor=write, owner=manage); insufficient role MUST be denied.
- **FR-068**: Authorization MUST be deny-by-default and enforced at the API/handler layer for every read and write.

  This slice has two distinct authorization surfaces, both dispatched by the containing resource's visibility (not a conjunction of tiers). (1) **The Cycle entity is team-wide** (a single shared instance, ASM-10) — it is NOT a per-user resource. All admitted users see the same cycles; cycle reads (the cycle selector, the metrics view, the sidebar active cycle) are NOT scoped per-user. The lifecycle operations — **create, activate, close, delete** — are explicit, deny-by-default authorized operations enforced at the handler layer (a non-admitted or unauthenticated request is rejected); no project-membership/role surface exists on the Cycle entity itself, and no `createdBy`/owner grant gates cycle reads. (2) **Assigning a task to a cycle authorizes on the task's own visibility**: a personal/unprojected task authorizes on ownership (FR-065); a shared-project task authorizes on current `ProjectMembership` + sufficient role (FR-066/FR-067) — a caller cannot assign a task they may not write. The `G C`-equivalent metrics view reflects the cycle's assigned tasks with per-task authorization still governing access to any individual task's detail. All reads and writes are denied by default unless the policy admits them (FR-068). The slice's cycle command and query handlers ENFORCE this at the handler layer — they do not merely reference it.

MVP boundary confirmed:
- OOS-01..OOS-20 (full MVP out-of-scope confirmation)

Entity touchpoint(s):
- ENT-01 (Task) — this slice populates the `cycle reference` attribute via cycle assignment (US-05.AS-02); the Task entity is owned by slice 002, which reserved this attribute as a nullable, forward-compatible column for slice 011
- ENT-02 (Project) — read by EC-12 when archiving a project whose tasks are assigned to an active cycle (the Project entity is owned by slice 004); no attribute is introduced here

Depends on:
- Slice 005 (daily-planning) — provides priorities, the full task editor, and the per-task selection/status model that the cycle metrics breakdown and per-task rollover choices reuse
- Slice 010 (project-board-kanban) — provides the Project List view's group-by control (FR-024, with group-by status and priority) and the status-column model whose statuses feed the Cycle view's breakdown. Slice 010 explicitly DEFERRED the group-by **cycle** dimension of FR-024 to this slice; this slice owns and completes it, since cycle assignment first exists here.
- Slice 019 (ui-design-system) — provides the UI catalog (menus, dialogs, EmptyState, tokens) and the FR-108 row/card "⋯" menu contract that the cycle-assignment affordance joins

Keyboard-trigger deferral (product-vision UI-first reinterpretation clause, constitution v5.0.0+):
- The `#` list shortcut (US-05.AS-01) and the `G C` navigation chord (US-05.AS-03) are members of
  FR-029/FR-028, which are **[DEFERRED]** with the custom shortcut system (OOS-20) — no slice
  currently owns delivering them. The June draft of this spec claimed FR-028/FR-029 (and FR-031)
  as owned-and-completed here; that framing is obsolete and removed. This slice owns the
  scenarios' BEHAVIOR: cycle assignment triggers from a visible control per FR-103/FR-108 (the
  task "⋯" menu's cycle action, following the „Priorytet…"/„Etykiety…" precedent), and the
  current-cycle view is reached through the sidebar's active-cycle entry, which FR-017 already
  requires to be visible. FR-031's suppression rule is **[DORMANT]** while no single-key
  shortcuts exist (post-FR-111) and is NOT realized here. If the accelerator slice ever lands
  FR-028/FR-029, those bindings layer ON TOP of the affordances shipped here.

## User Scenarios & Testing *(mandatory)*

### User Story 5 - Cycle Management & Review (Priority: P3)

User works with 2-week cycles (sprints). They assign tasks to the current cycle, track progress via metrics (% done, days remaining, status breakdown), and at cycle end, roll unfinished tasks to the next cycle or back to backlog. (Product-vision narrative retains the `#`/`G C` phrasing for the future accelerator slice; triggers here follow the UI-first reinterpretation clause.)

**Why this priority**: Cycles add structured time-boxing on top of basic task management. Valuable for disciplined planning but not required for basic daily use.

**Independent Test**: Can be tested by creating a cycle, assigning tasks, completing some, closing the cycle, and verifying rollover behavior.

> Scope note: the keyboard triggers named in the acceptance scenarios (`#`, `G C`) belong to
> FR-029/FR-028, **[DEFERRED]** with the shortcut system per OOS-20 — the scenarios are read
> through the product-vision UI-first reinterpretation clause: same behavior, visible-UI triggers
> (the task "⋯" menu's cycle action per FR-103/FR-108; the sidebar active-cycle entry per
> FR-017). The undo of the bulk move-all-to-next-cycle operation (US-09.AS-05) is delivered in
> slice 014 (undo); see Constitution Compliance.

**Acceptance Scenarios** (owned by this slice; triggers read through the UI-first
reinterpretation clause — original keyboard phrasing retained in product-vision.md for the
future accelerator slice, OOS-20):

1. **(US-05.AS-01) Given** the user is on any view with a task selected, **When** they invoke the task's cycle-assignment action from its "⋯" menu (the original `#` trigger is deferred per OOS-20), **Then** a cycle selector appears listing available cycles and a "backlog" option.
2. **(US-05.AS-02) Given** the cycle selector is open, **When** user selects a cycle, **Then** the task is assigned to that cycle.
3. **(US-05.AS-03) Given** the user navigates to the current cycle view via the sidebar's active-cycle entry (FR-017; the original `G C` chord is deferred per OOS-20), **When** the view renders, **Then** it shows: percentage of tasks done, days remaining in the cycle, and a breakdown of tasks by status.
4. **(US-05.AS-04) Given** a cycle has ended with unfinished tasks, **When** user opens the cycle review, **Then** they see a list of incomplete tasks with options to: move all to next cycle, move all to backlog, or handle individually.
5. **(US-05.AS-05) Given** the cycle review shows incomplete tasks, **When** user selects "move all to next cycle" and confirms, **Then** all incomplete tasks are reassigned to the next cycle in a single operation.
6. **(US-05.AS-06) Given** no next cycle exists when rollover is attempted, **When** user selects "move to next cycle", **Then** the system prompts to create a new cycle first.
7. **(US-05.AS-07) Given** an active cycle exists, **When** user attempts to delete it, **Then** the system prevents deletion and shows a message explaining the cycle must be closed first.

> Cycle ordering & lifecycle (resolves the rollover "next cycle" ambiguity for US-05.AS-05/AS-06): cycles are ordered by start date, and the **"next cycle"** for rollover is the next **planned** cycle by start date. A cycle has status planned, active, or closed; the **planned → active** transition is a **manual activation**, and a **single-active invariant** holds (at most one active cycle at any time across the team-wide instance). The single-active invariant is enforced by a **global partial-unique index** on the active-cycle status (database-level guarantee), in addition to the handler-level activation check. If no planned cycle exists when rollover targets the next cycle, US-05.AS-06 prompts the user to create one first.

### Edge Cases

- **EC-04 — Deleting an active cycle**: The system prevents this; the cycle must be closed first.
- **EC-10 — "Backlog" naming distinction**: The term "backlog" is used in two different contexts: (1) task status "backlog" refers to a task's workflow state on the Kanban board (first column, meaning the task has not yet been triaged into active work); (2) cycle backlog refers to tasks not assigned to any cycle. These are independent dimensions — a task can have status "todo" while being in the cycle backlog, or have status "backlog" while assigned to a cycle.
- **EC-12 — Archiving a project with cycle-assigned tasks**: When archiving a project whose tasks are assigned to an active cycle, the tasks remain in the cycle but are hidden from project-based views. They remain visible in the Cycle view and can be moved to another project or unassigned.

## Requirements *(mandatory)*

### Functional Requirements (slice-specific)

- **FR-015**: System MUST support cycles (sprints) with a default duration of 2 weeks, configurable in application settings.
- **FR-016**: Each task MUST belong to at most one cycle (or no cycle, meaning backlog).
- **FR-017**: The active cycle MUST be visible in the sidebar.
- **FR-018**: When a cycle is closed with incomplete tasks, the system MUST provide rollover options: move all to next cycle, move all to backlog (remove cycle assignment), keep all in the closed cycle with a "carried over" flag, or handle individually (per-task choice among the same three options).
- **FR-019**: System MUST prevent deletion of an active cycle; the cycle must be closed first.
- **FR-020**: System MUST allow the user to create, edit (name, start/end dates), close, and delete cycles. Active cycles cannot be deleted (must be closed first, per FR-019). Planned and closed cycles may be deleted only if they have no assigned tasks.
- **FR-024**: The Project List view MUST display a project's tasks as a flat list, groupable by cycle, status, or priority.
- **FR-026**: The Cycle view MUST display: percentage of tasks completed, days remaining, and breakdown by status.

> Scope note: FR-024 is owned here, where its by-cycle grouping dimension is completed; the
> group-by status and group-by priority dimensions shipped in slice 010.

> Scope note (shortcut sets): FR-028 (navigation shortcuts, incl. `G C`) and FR-029 (list
> shortcuts, incl. `#`) are **[DEFERRED]** per constitution v5.0.0 (US-18/OOS-20) and are NOT
> realized by this slice — the June draft's claim that this slice "completes" those sets is
> withdrawn. The cycle operations they would have triggered are delivered behind visible
> affordances (FR-103/FR-108/FR-017).

### Cross-cutting Requirements (realized in this slice)

UI-First Operability (per Constitution Principle I; verbatim from product-vision.md):
- **FR-103**: Every operation the product offers MUST have a visible affordance on the surface where it applies — directly, or via an explicit overflow/context menu ("⋯") on the item it targets. No functionality may exist only behind a keyboard shortcut.
- **FR-108**: Each task row MUST expose quick actions on hover/focus (complete, edit, overflow) and a "⋯" menu containing every operation available on that task; all row actions MUST have keyboard-focus-triggered equivalents (FR-046) and correct hit targets/stacking so pointer clicks always land (Principle I).
- **FR-110**: Every empty list state MUST present a short explanatory hint plus the relevant action button; onboarding wizards and first-run modal tours remain prohibited (Principle IV).

> FR-031 (single-key suppression in text inputs), claimed by the June draft of this spec, is
> **[DORMANT]** per product-vision — no single-key shortcuts exist after slice 019 (FR-111); it
> re-activates only with the future accelerator slice (OOS-20) and is NOT realized here.

Accessibility (per Constitution Principle II):
- **FR-042**: Every focusable element MUST have a visible focus indicator.
- **FR-043**: All interactive elements MUST have correct ARIA roles and labels for screen reader compatibility.
- **FR-044**: Text contrast ratio MUST be at least 4.5:1 (3:1 for large text).
- **FR-045**: Custom keyboard shortcuts MUST NOT collide with native assistive-technology bindings.
- **FR-046**: No content may be accessible only via hover — all tooltips and popovers MUST have a keyboard/focus-triggered equivalent.
- **FR-047**: Animations MUST respect the `prefers-reduced-motion` user preference; when reduced motion is active, transitions MUST be instant or under 100ms.
- **FR-101**: Server-initiated updates and toasts MUST be conveyed to assistive technology via an appropriate ARIA live region without stealing focus, and confirmation/command-palette dialogs MUST follow the dialog focus contract (set initial focus, trap focus, dismiss on Esc, return focus to the invoker on close).

Security (per Constitution Principle XII):
- **FR-099**: User-authored content MUST be output-encoded/sanitized so raw HTML injection is impossible, and a Content-Security-Policy plus standard security response headers MUST be present in production.

Error Handling & Data Integrity (per Constitution Principle VII):
- **FR-049**: All errors MUST be presented to the user with a clear message and an actionable recovery suggestion. No operation may fail silently.
- **FR-050**: Errors MUST be logged with structured context (severity level, operation context, and error details) for debugging purposes.
- **FR-051**: Before any data migration, the system MUST automatically create a backup of user data. The user MUST be able to restore from this backup.

### Key Entities

This slice introduces and owns the Cycle entity:

- **ENT-03 — Cycle**: A time-boxed period (default 2 weeks) for organizing work. Has a start date, end date, and status (active/closed/planned). Contains zero or more tasks. Only one cycle can be active at a time. Cycles are team-wide (a single shared instance, ASM-10), with explicit create/activate/close/delete authorization rather than per-user ownership. A task not assigned to any cycle is considered "in the cycle backlog" (distinct from the task status "backlog" — cycle backlog refers to cycle assignment, not workflow state). Tasks remaining in a closed cycle may carry a "carried over" flag indicating they were not completed during that cycle's timeframe.

> Entity touchpoint: this slice populates the `cycle reference` attribute of **ENT-01 — Task** (owned by slice 002, reserved there as a nullable, forward-compatible column) when a task is assigned to a cycle (US-05.AS-02). The "carried over" flag in ENT-03 corresponds to the keep-in-closed-cycle rollover option in FR-018.

## Success Criteria *(mandatory)*

### Measurable Outcomes

This slice introduces no new slice-specific success criteria. The measurable performance and quality outcomes established in earlier slices continue to apply to the cycle assignment, metrics, and rollover interactions delivered here.

## Constitution Compliance

This slice is evaluated against constitution v5.1.0. Cross-cutting principles realized here:

- **I. UI-First Operability**: every cycle operation is reachable through a visible affordance — cycle assignment via the task "⋯" menu's cycle action (FR-103/FR-108, joining the „Priorytet…"/„Etykiety…" menu precedent), navigation to the current cycle via the sidebar's active-cycle entry (FR-017), and the rollover review and lifecycle operations (create/edit/activate/close/delete) via explicit dialogs and controls on the Cycle view. No cycle functionality exists only behind a keyboard shortcut (the `#`/`G C` bindings are deferred with the shortcut system, OOS-20). Standard editing keys (FR-030) and full WCAG keyboard operability apply to every new control.
- **II. Accessibility (WCAG 2.1 AA)**: FR-042 (focus indicator), FR-043 (ARIA roles/labels), FR-044 (contrast ≥ 4.5:1), FR-045 (no AT-binding collisions), FR-046 (no hover-only content), FR-047 (prefers-reduced-motion). Per FR-101, server-initiated updates that this slice produces — a team-wide cycle being created/activated/closed, the active-cycle change reflected in the sidebar (FR-017), and any cycle-related toast — MUST be announced via an ARIA live region (polite, coalesced) WITHOUT stealing focus; and the cycle selector and the rollover/deletion confirmation dialogs MUST follow the dialog focus contract (set initial focus, trap focus, dismiss on Esc, return focus to the invoker on close). The cycle selector, metrics view, and rollover dialog are operable and labeled for screen readers.
- **III. Instant Response**: cycle assignment, the metrics render, and rollover confirmation paint an optimistic result within ~16ms of the triggering interaction while the C# API reconciles asynchronously (server mutations p95 < 200ms, a MUST); skeleton screens are permitted for the network-bound metrics and cycle fetch (Principle IV). Because cycles are **team-wide**, a cycle lifecycle change (create/activate/close) and the active-cycle sidebar update DO fan out to other members over SignalR; real-time reconciliation under last-write-wins applies — an inbound remote patch MUST yield to a pending local optimistic mutation until that mutation's server-ack resolves, then reconcile, and MUST NOT clobber an in-flight local edit. Cycle assignment on a shared-project task likewise reconciles per this rule. The receiving-client fan-out budget is p95 < 1000ms (commit-to-paint), and these server-initiated paints are announced per Principle II / FR-101.
- **V. Connected, Server-Authoritative**: cycles, assignments, and rollover are persisted server-side in PostgreSQL through the C# API, which owns all writes and is the single source of truth; the Next.js client renders these mutations optimistically while the server reconciles. The only permitted external runtime dependency is the Google OAuth authentication provider (Principle IX); no cycle data leaves the team's own API and database.
- **VI. Type Safety End-to-End**: the Cycle entity and its status enum (active/closed/planned) are typed end-to-end, with EF Core code-first migrations as the schema source of truth for the Cycle entity and the Task cycle-reference; runtime validation is applied at the trust boundaries — Zod on the web client for user input and API responses, FluentValidation (or data annotations) at the C# API boundary. The cycle-reference on Task is a validated, nullable relation.
- **VII. Data Integrity & Resilience**: FR-049 (error + recovery — e.g. the "create a new cycle first" prompt of US-05.AS-06 and the active-cycle deletion guard of US-05.AS-07 / EC-04 / FR-019), FR-050 (structured logging), FR-051 (auto-backup hook ahead of the schema change that adds the Cycle entity and the Task cycle reference). The deletion guards (FR-019/FR-020) actively protect data by refusing to remove an active cycle or a non-empty planned/closed cycle.
- **VIII. Test-First**: each owned acceptance scenario above (US-05.AS-01..AS-07), plus EC-04, EC-10, and EC-12, is independently testable (Red-Green-Refactor). Integration tests cover the authorization of every cycle command/query handler — an unauthenticated or non-admitted request to any cycle command/query handler MUST be denied (deny-by-default), and a task-to-cycle assignment by a caller lacking write access to the task (ownership for personal/unprojected tasks, current membership + sufficient role for shared-project tasks) MUST be denied.
- **IX. Authentication & Authorization**: every cycle operation is authenticated and authorized deny-by-default at the API/handler layer, with authorization **dispatched by resource visibility** — NOT a Tier A/B conjunction. Cycles are **team-wide** (a single shared instance, ASM-10): they are not per-user resources, so cycle reads (the cycle selector, the metrics view, the FR-017 sidebar active cycle) are shared across admitted users and are not scoped per-user. The lifecycle operations — **create, activate, close, delete** — are explicit, deny-by-default authorized operations enforced at the handler layer. Task-to-cycle **assignment** is dispatched by the **task's** visibility: a personal/unprojected task authorizes on ownership (`createdBy`/`ownerId`, queries scoped to the caller); a shared-project task authorizes on current `ProjectMembership` + role (`createdBy`/assignee are provenance only and confer no standalone access; on leave/remove/unshare a user loses ALL access to that project's data — FR-065–FR-068). Insufficient role on a shared-project task's assignment MUST be denied (FR-067). The cycle command and query handlers ENFORCE these checks (handler-level), not merely reference them; the rollover/deletion guards run as team-wide authorized operations. Sign-in itself is admission-gated (allowlist / Google Workspace `hd`, FR-087) and the deny-by-default gate covers unauthenticated requests to any cycle route.
- **X. Time & Timezone**: cycle dates are date-relative computation and MUST be handled per FR-092. Cycle `start date` / `end date` are stored in UTC (`timestamptz`); the "days remaining" metric (FR-026 / US-05.AS-03), the cycle boundary determination (which cycle is current/next, cycle ordering by start date), and the end-of-cycle close/rollover trigger MUST all be evaluated against the single instance reference timezone, **`Europe/Warsaw`**, applied identically on client and server, so a cycle boundary is the same fact everywhere. DST transitions across a 2-week cycle MUST be handled by the timezone library, never by fixed-offset arithmetic. Per-user timezones are out of scope (ASM-12, OOS-19).
- **XI. Privacy & Personal Data**: cycles are team-wide and hold no per-user personal data (no Google PII; a cycle has only a name, start/end dates, and status), so this slice introduces no new erasure-cascade obligation. The cycle entity is not part of the per-account deletion cascade (FR-085); it persists as shared team data independent of any single user's account, consistent with the stated data-retention stance (FR-086).
- **XII. Security by Default**: the cycle name is user-authored content and MUST be output-encoded/sanitized so raw HTML injection is impossible (FR-099); the cycle selector, metrics view, and rollover/deletion dialogs render it through the same safe-output path. This slice introduces no new secrets; any added configuration value (e.g. the configurable cycle duration, FR-015) is non-secret application settings. Standard security response headers and CSP (FR-099) apply to the cycle views as to all UI.

**Known compliance gap (deferred, accepted at slicing time):** Principle VII requires destructive and bulk operations to be undoable for ≥ 30 seconds (FR-040). This slice owns the bulk "move all to next cycle" operation (US-05.AS-05) and the move-all-to-backlog rollover, but the 30-second undo for that bulk cycle move is the responsibility of slice 014 (undo), whose canonical scenario US-09.AS-05 restores all moved tasks to their original cycle assignments. No undo is added in this slice, per the slice-011 definition; slice 014 retrofits the undo window onto these rollover paths.

## Assumptions

This slice introduces no new slice-specific assumptions. The MVP-wide assumptions established in product-vision.md continue to apply.

## Out of Scope

This slice confirms the full MVP out-of-scope boundary (OOS-01..OOS-20 from product-vision.md):

- **OOS-01**: [PROMOTED to in-scope — see US-11, US-12] Multi-user collaboration, sharing, permissions
- **OOS-02**: Cross-device sync, cloud storage
- **OOS-03**: Mobile application, PWA
- **OOS-04**: AI features (auto-categorization, summaries, suggestions)
- **OOS-05**: External integrations (calendar, Slack, GitHub, email)
- **OOS-06**: [PARTIALLY promoted] In-app notifications are now in scope (US-16); push/device notifications and reminders remain out of scope.
- **OOS-07**: File attachments on tasks
- **OOS-08**: Subtasks (task nesting)
- **OOS-09**: Custom views, saved filters
- **OOS-10**: Custom theming beyond dark/light mode
- **OOS-11**: Automations (if X then Y)
- **OOS-12**: Plugin or extension system
- **OOS-13**: Email notifications
- **OOS-14**: Push/device notifications and reminders
- **OOS-15**: Presence indicators and activity/audit feed
- **OOS-16**: Anonymous/guest access and public share links
- **OOS-17**: Organizations / multi-tenancy beyond the single team, and non-Google SSO / additional identity providers
- **OOS-18**: Pending / pre-account invitations (invites are by email resolved against existing signed-in Users only)
- **OOS-19**: Per-user timezones (the instance uses a single reference timezone, ASM-12)
- **OOS-20**: The custom keyboard shortcut system — single-key commands, chords, and the shortcuts help overlay (US-08, FR-027..029/031/033, SC-001) — deferred per constitution v5.0.0 to a future opt-in accelerator slice layered on top of complete UI operability (US-18). Standard editing keys and WCAG keyboard operability are NOT out of scope.

Additionally deferred to later slices within the MVP (not part of slice 011): the 30-second undo window for cycle rollover bulk moves (US-09.AS-05) is owned by slice 014 (undo); recurring tasks (012); command palette & search (013); data export/import (015), which serializes the Cycle entity introduced here; and dark/light theming (018).

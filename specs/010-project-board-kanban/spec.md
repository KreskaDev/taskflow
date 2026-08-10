# Feature Specification: Project Board (Kanban)

**Feature Branch**: `010-project-board-kanban`

**Created**: 2026-06-13

**Updated**: 2026-08-10 — aligned to constitution v5.1.0 (Principle I: UI-First Operability) and
the product-vision UI-first reinterpretation clause; slice 019 (ui-design-system) executes BEFORE
this slice and removed the single-key shortcut system (FR-111, OOS-20), so every keyboard-triggered
step below is reinterpreted as a visible-UI-control trigger with unchanged behavior.

**Status**: Draft

**Input**: Slice 010 of the TaskFlow MVP. Source of truth: `.specify/memory/product-vision.md`. Goal: a project Kanban board with status columns (Backlog, Todo, In Progress, Done) and a groupable project list view, so a member can manage a single project's workflow visually. This builds on the projects introduced in slice 004 (project-management) and the task/priority handling delivered in slice 005 (daily-planning), and is access-scoped via the sharing model from slice 007 (project-sharing-membership).

## Provenance

This slice realizes the following items from product-vision.md:

Slice-specific:
- US-03 (Project Kanban Workflow) — full: AS-01, AS-02, AS-03, AS-04, AS-05, AS-06, AS-07. NOTE: US-03.AS-07's group-by control is split across slices — this slice OWNS group-by **status** and **priority**; group-by **by cycle** is deferred to slice 011 (cycles), which owns cycle assignment.
- FR-024 (Project List view: flat list, groupable by cycle, status, or priority)
- FR-025 (Project Board view: Kanban columns mapped to statuses; cancelled tasks hidden)
- EC-11 (cancelled tasks not displayed on the Board)

Cross-cutting (realized in this slice):
- FR-103 (every operation reachable through a visible affordance — UI-first operability; per the
  slicing rule it is realized in every slice that adds an operation)
- FR-108 (task rows expose quick actions + a complete "⋯" menu; the project List rows reuse the
  slice-019 catalog, and Board cards get the same affordance contract)
- FR-110 (empty states present a hint + action; applies to the empty Board columns and List groups)
- FR-042 (visible focus indicator)
- FR-043 (ARIA roles/labels)
- FR-044 (text contrast ≥ 4.5:1)
- FR-045 (no collision with assistive-technology bindings)
- FR-046 (no hover-only content)
- FR-047 (prefers-reduced-motion)
- FR-101 (ARIA-live for server-initiated updates/toasts + dialog focus contract)
- FR-099 (output-sanitize user-authored content rendered on cards; CSP/security headers)
- FR-049 (error message + recovery action)
- FR-050 (structured error logging)
- FR-051 (auto-backup before migration — infrastructure in place)

Access control (realized in this slice — verbatim from product-vision.md):
- **FR-065**: Authorization MUST be dispatched by the containing resource's visibility (not a conjunction of tiers): personal/unprojected data authorizes on ownership (`createdBy`/`ownerId`) with queries scoped to the caller; shared-project entities authorize on current `ProjectMembership` + role. Every query MUST be scoped accordingly (per-user isolation).
- **FR-066**: Access to a shared project's data MUST require current membership in that project. `createdBy` and assignee are provenance only and confer NO standalone access; on leave/remove/unshare a user MUST lose ALL access to that project's data regardless of authorship or assignment.
- **FR-067**: Each operation on a shared-project resource MUST require sufficient role (viewer=read, editor=write, owner=manage); insufficient role MUST be denied.
- **FR-068**: Authorization MUST be deny-by-default and enforced at the API/handler layer for every read and write.

  This slice surfaces a project's Board and List, so authorization is **dispatched by the containing project's visibility** (not a conjunction of tiers). Its command and query handlers ENFORCE FR-065..FR-068 at the handler level — they do not merely reference them. For a **personal** project, the Board/List query authorizes on ownership (`ownerId`) with queries scoped to the caller (FR-065); for a **shared** project it requires current `ProjectMembership` (FR-066). The task `createdBy` and assignee surfaced on cards are provenance only and grant no access; on leave/remove/unshare the user loses ALL access to that project's Board/List (FR-066). Viewing the Board/List and regrouping the List (group-by status/priority) are reads available to any current member (viewer+); moving a task between columns is a write that requires editor or owner role (FR-067). All reads and writes are denied by default unless the policy admits them (FR-068).

MVP boundary confirmed:
- OOS-01..OOS-19 (full MVP out-of-scope confirmation)

Entity touchpoint(s):
- ENT-01 (Task) — this slice reads and updates the `status` attribute (the Task entity is owned by slice 002, task-capture); no new attribute is introduced
- ENT-02 (Project) — the organizational container whose tasks are displayed on the Board and List views (the Project entity is owned by slice 004, project-management); its `ownerId` and `visibility` gate who may see the Board/List

Depends on:
- Slice 004 (project-management) — provides the Project entity, one-level nesting, archive, and move-to-project, on which the project list and per-project views rely
- Slice 005 (daily-planning) — provides priorities and the full task editor, supporting the "group by priority" control on the List view and the per-task selection model reused on the Board
- Slice 007 (project-sharing-membership) — provides project visibility (personal/shared), the ProjectMembership set, and roles, which scope who may view the Board/List and who may move tasks (editor/owner)

Keyboard-trigger deferral (product-vision UI-first reinterpretation clause, constitution v5.0.0+):
- The `G P` navigation chord (US-03.AS-01) and the arrow-move keys (US-03.AS-04, AS-05, AS-06) are
  members of FR-028/FR-029, which are **[DEFERRED]** with the custom shortcut system (OOS-20) — no
  slice currently owns delivering them. This slice owns the Board-move acceptance scenarios'
  BEHAVIOR and the column-to-status mapping driving them (FR-025); the triggers are visible UI
  controls per FR-103 (drag-and-drop between columns and the card's "⋯" menu move actions; the
  project list opens from the sidebar, FR-109). If the accelerator slice ever lands FR-028/FR-029,
  those bindings layer ON TOP of the affordances shipped here.

## User Scenarios & Testing *(mandatory)*

### User Story 3 - Project Kanban Workflow (Priority: P2)

User navigates to a specific project and views tasks on a Kanban board with columns for Backlog, Todo, In Progress, and Done. They move tasks between columns through visible controls — dragging cards or the card's "⋯" menu — and manage the project workflow visually. (Product-vision narrative retains the arrow-key phrasing for the future accelerator slice; triggers here follow the UI-first reinterpretation clause.)

**Why this priority**: Project-level organization is essential for users managing work beyond simple daily lists, but depends on core task and project entities being functional first.

**Independent Test**: Can be tested by creating a project, adding tasks to it with different statuses, opening the project Board view, and moving tasks between columns via drag-and-drop and the card menu.

> Scope note: the keyboard triggers named in the acceptance scenarios (`G P`, arrow-move) belong to FR-028/FR-029, **[DEFERRED]** with the shortcut system per OOS-20 — the scenarios are read through the product-vision UI-first reinterpretation clause: same behavior, visible-UI triggers (sidebar project list per FR-109; column moves via drag-and-drop and the card "⋯" menu per FR-103). This slice owns the Board-move acceptance scenarios and the column-to-status mapping (FR-025) that those moves drive. Cancelled tasks are hidden from the Board (EC-11 / FR-025) and remain reachable via the List view — and, once slice 013 lands, via search and the command palette.

> Scope note (US-03.AS-07 group-by split): the List view's group-by control (FR-024) offers grouping by cycle, status, or priority. This slice OWNS grouping by **status** and **priority** (both available from the Task entity owned by slice 002 and priorities from slice 005). Grouping **by cycle** depends on cycle assignment and is DEFERRED to slice 011 (cycles), which owns the Cycle entity and task-to-cycle assignment; until slice 011 lands, the by-cycle option is not offered.

**Acceptance Scenarios** (owned by this slice; triggers read through the UI-first
reinterpretation clause — original keyboard phrasing retained in product-vision.md for the
future accelerator slice, OOS-20):

1. **(US-03.AS-01) Given** user is on any view, **When** they open the projects list via the sidebar's visible projects entry (FR-109; the original `G P` trigger is deferred per OOS-20), **Then** a project list appears for selection.
2. **(US-03.AS-02) Given** the project list is open, **When** user selects a project, **Then** the project view opens in the last-used mode (List or Board).
3. **(US-03.AS-03) Given** the project Board view is open, **When** the view renders, **Then** tasks are displayed in columns: Backlog, Todo, In Progress, Done.
4. **(US-03.AS-04) Given** a task card is on the Board view, **When** user moves it one column to the right via a visible control — dragging the card to the adjacent column or the card's "⋯" menu move action (the original arrow-key trigger is deferred per OOS-20), **Then** the task moves one column to the right (e.g., Todo to In Progress) and its status updates accordingly.
5. **(US-03.AS-05) Given** a task is in the Done column, **When** user invokes move-right, **Then** nothing happens (Done is the last column; the "⋯" menu does not offer a further-right move).
6. **(US-03.AS-06) Given** a task card is on the Board view, **When** user moves it one column to the left (drag or "⋯" menu), **Then** the task moves one column to the left.
7. **(US-03.AS-07) Given** the project List view is open, **When** the view renders, **Then** tasks are displayed as a flat list, groupable by cycle, status, or priority via a group-by control.

### Edge Cases

- **EC-11 — Cancelled tasks on Board view**: Tasks with status "cancelled" are not displayed on the Kanban Board. They remain accessible via the List view, search, and command palette.

## Requirements *(mandatory)*

### Functional Requirements (slice-specific)

- **FR-024**: The Project List view MUST display a project's tasks as a flat list, groupable by cycle, status, or priority.
- **FR-025**: The Project Board view MUST display a project's tasks in a Kanban layout with columns mapping directly to statuses: Backlog (backlog), Todo (todo), In Progress (in_progress), Done (done). Tasks with status "cancelled" MUST be hidden from the Board view.

### Cross-cutting Requirements (realized in this slice)

UI-First Operability (per Constitution Principle I; verbatim from product-vision.md):
- **FR-103**: Every operation the product offers MUST have a visible affordance on the surface where it applies — directly, or via an explicit overflow/context menu ("⋯") on the item it targets. No functionality may exist only behind a keyboard shortcut.
- **FR-108**: Each task row MUST expose quick actions on hover/focus (complete, edit, overflow) and a "⋯" menu containing every operation available on that task; all row actions MUST have keyboard-focus-triggered equivalents (FR-046) and correct hit targets/stacking so pointer clicks always land (Principle I).
- **FR-110**: Every empty list state MUST present a short explanatory hint plus the relevant action button; onboarding wizards and first-run modal tours remain prohibited (Principle IV).

> FR-031 (single-key suppression in text inputs), listed in the original draft of this spec, is
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

This slice introduces no new entity. It reads and updates the `status` attribute of **ENT-01 — Task** (owned by slice 002, task-capture) as tasks move between Board columns, and displays the tasks belonging to **ENT-02 — Project** (owned by slice 004, project-management). For reference, the full definitions from product-vision.md:

- **ENT-01 — Task**: The core work item. Has a title (required), description, priority (P0-P3), status (backlog/todo/in_progress/done/cancelled), due date, labels, project reference, cycle reference, recurrence rule, createdBy (the User who created it), assignees (zero or more Users; only on shared-project tasks), and system timestamps (created_at, updated_at, completed_at). New tasks default to "backlog" status. A recurring task has a linked recurrence rule that generates successor instances.
- **ENT-02 — Project**: An organizational container for tasks. Has a name, color, icon, optional parent project reference, archived flag, ownerId (the owning User), and visibility (personal or shared). Supports one level of nesting. Contains zero or more tasks. Shared projects have a membership set.

## Success Criteria *(mandatory)*

### Measurable Outcomes

This slice introduces no new slice-specific success criteria. The measurable outcomes that apply here — SC-003 (optimistic result painted immediately, then reconciled or rolled back asynchronously by the server) and SC-004 (no third-party runtime data services — only its own API and PostgreSQL) — are owned by slice 002 (task-capture); how they are realized by this slice's Board and List interactions is described under Constitution Compliance below.

## Constitution Compliance

This slice is evaluated against constitution v5.1.0. Cross-cutting principles realized here:

- **I. UI-First Operability**: every Board and List operation has a visible affordance (FR-103) — the project list opens from the sidebar's projects section (FR-109; US-03.AS-01), a project is selected by clicking/activating its entry (US-03.AS-02), the List/Board mode switch is a visible control, the group-by control is a visible select (US-03.AS-07), and a card moves between columns by drag-and-drop or via its "⋯" menu move actions (US-03.AS-04..06). No operation exists only behind a keyboard shortcut; standard keyboard operability (Tab/Enter/Esc, arrow navigation within composite widgets, focus management) remains required via Principle II.
- **II. Accessibility (WCAG 2.1 AA)**: FR-042 (focus indicator on the focused card, the mode switch, and the group-by control), FR-043 (ARIA roles/labels for columns, cards, and the list — the Board is a composite widget with arrow navigation as WCAG operability, not a shortcut system), FR-044 (contrast ≥ 4.5:1, so column/status is never conveyed by color alone), FR-045 (no custom shortcuts are introduced, so no AT-binding collisions arise), FR-046 (no hover-only content — card quick actions have focus-triggered equivalents per FR-108), FR-047 (prefers-reduced-motion for column-move transitions). **FR-101**: status changes surfaced by this slice (a failed/rolled-back move's toast) use the established persistent `role="status"` region without stealing focus; announcing ANOTHER member's remote column move arrives with the real-time transport (slice 016) — this slice's obligation is that the Board/List does not preclude that announcement (the transfer-note mechanism established in slice 019, D11). Any dialog this slice ships follows the catalog Dialog's focus contract.
- **III. Instant Response**: SC-003 (owned by slice 002, task-capture) — selecting a card, moving it between columns, and switching the List group-by all paint their optimistic result within one animation frame while the C# API reconciles or rolls back the status change asynchronously (server-confirmed mutations within a p95 < 200ms budget), on the established optimistic mutation-factory pattern (snapshot/rollback/invalidate). The real-time transport is slice 016: when it lands, an inbound remote patch resolves under last-write-wins but MUST yield to a pending local optimistic move until that move's server-ack resolves, then reconcile — this slice's cache/mutation design MUST NOT preclude that (transfer note per D11).
- **IV. Minimalist UI**: the Board surfaces the four workflow columns and hides cancelled tasks (FR-025 / EC-11), and the List exposes grouping on demand through a single group-by control (FR-024), keeping density without clutter. Skeleton screens are permitted for the initial network-bound load of a project's tasks; they MUST NOT mask a column move whose optimistic result could be shown instead.
- **V. Connected, Server-Authoritative**: SC-004 (owned by slice 002, task-capture) — both views read and write task status through the app's own C# API and PostgreSQL database, the system of record, with no third-party runtime data service (the sole permitted external runtime dependency is Google OAuth, for sign-in only).
- **VI. Type Safety End-to-End**: the column-to-status mapping (FR-025) is expressed over the typed status enum (backlog/todo/in_progress/done/cancelled) from the schema; a right/left move resolves to a valid adjacent status, with the Done boundary (US-03.AS-05) enforced as a typed no-op.
- **VII. Data Integrity & Resilience**: FR-049 (any failed status update surfaces a clear, recoverable message), FR-050 (structured logging of such failures), FR-051 (the auto-backup hook stays in place ahead of any schema change).
- **VIII. Test-First**: each owned acceptance scenario above, plus EC-11, is independently testable (Red-Green-Refactor); integration tests cover the Board/List command and query handlers through the real database, including authorization (a non-member request, or a viewer attempting a column move, MUST be denied).
- **IX. Authentication & Authorization**: this slice's Board and List handlers authorize **deny-by-default, dispatched by the containing project's visibility** (not a conjunction of tiers), at the API/handler layer (FR-068). For a **personal** project the policy authorizes on ownership (`ownerId`) with queries scoped to the caller (FR-065), so its Board/List is visible only to its owner. For a **shared** project the policy requires current `ProjectMembership` (FR-066), so the Board/List is visible only to current members, and applies role sufficiency (FR-067): viewing the Board/List and regrouping the List (group-by) require viewer+; moving a task between columns is a write that requires editor or owner. The `createdBy` and assignee fields surfaced on cards are **provenance only** and confer no standalone access; on leave/remove/unshare the user loses ALL access to that project's Board/List regardless of authorship or assignment (FR-066). These checks live in the application-layer authorization policy (backed by ProjectMembership), not in ad hoc UI code, and each handler ships with both an allow and a deny test (SC-016 / Principle VIII).
- **X. Time & Timezone**: this slice owns no date-relative computation (Today/Upcoming membership, cycle boundaries, recurrence rollover, and natural-language resolution are owned by other slices). Any due date surfaced on a Board card or List row is stored in UTC and rendered against the single instance reference timezone `Europe/Warsaw`, applied identically on client and server (FR-092), with date-only vs date-time distinguished by the `has_time` flag — consistent display, no per-slice time logic introduced here.
- **XI. Privacy & Personal Data**: the Board/List surfaces personal identifiers (`createdBy`, assignee display names/avatars) on cards. This slice introduces no new personal-data store and owns no erasure path — account deletion and the erasure cascade (FR-085/FR-086, US-17) are owned by the privacy slice (data-export-import / account management). When a member leaves, is removed, or a project is unshared, they lose all access to this Board/List (FR-066) so no residual personal data is exposed to a non-member here.
- **XII. Security by Default**: task titles and markdown descriptions rendered on Board cards and List rows are untrusted, user-authored content and MUST be output-encoded/sanitized to a constrained safe subset so raw HTML injection is impossible, behind the production Content-Security-Policy and security response headers (FR-099). The secrets clause of Principle XII (session key, OAuth secret, DB/broker credentials) is infrastructure-level and not exercised by this slice's view handlers.

**Former compliance gap — resolved by constitution v5.0.0:** the original draft recorded a Keyboard-First gap (the arrow-move keys' canonical requirement FR-029 living in a later slice). Principle I is now UI-First Operability and FR-028/FR-029 are **[DEFERRED]** with the whole shortcut system (OOS-20), so no keyboard-trigger gap exists: every operation this slice ships is fully operable through visible affordances (FR-103), and WCAG keyboard operability (Principle II) covers focus/arrow navigation within the Board as a composite widget. No FR-040 undo gap arises here: moving a task between columns is a reversible status change (move it back), not one of FR-040's destructive/irreversible actions.

## Assumptions

This slice introduces no new assumptions. The assumptions owned by earlier slices continue to apply unchanged — in particular ASM-01 and ASM-02 from slice 001 (accounts-and-auth), and the project-model assumptions established in slice 004 (project-management). For reference, the full text from product-vision.md:

- **ASM-01 — Multi-user team**: The application serves a small collaborating team (~10). Each user authenticates (Google) and has personal data plus access to shared projects.
- **ASM-02 — Web platform**: The MVP targets modern desktop browsers. Native mobile apps, PWA/offline operation, and cross-device sync are explicitly out of scope.

## Out of Scope

This slice confirms the full MVP out-of-scope boundary (OOS-01..OOS-20 from product-vision.md):

- **OOS-01**: [PROMOTED to in-scope in v3.0.0 — see US-11, US-12] Multi-user collaboration, sharing, permissions
- **OOS-02**: Cross-device sync, cloud storage
- **OOS-03**: Mobile application, PWA
- **OOS-04**: AI features (auto-categorization, summaries, suggestions)
- **OOS-05**: External integrations (calendar, Slack, GitHub, email)
- **OOS-06**: [PARTIALLY promoted in v3.0.0] In-app notifications are now in scope (US-16); push/device notifications and reminders remain out of scope.
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

Note: multi-user collaboration, sharing, and in-app notifications are IN scope for the MVP (US-11/US-12/US-16); the OOS-01 and OOS-06 markers above are retained verbatim as historical promotion notes, not as current out-of-scope assertions.

Also out of scope for this slice specifically (deferred to later slices): the keyboard triggers named by US-03's scenarios (`G P`, arrow-move — members of FR-028/FR-029) are deferred with the shortcut system per OOS-20; grouping the List view **by cycle** (one option of FR-024's group-by control, exercised via US-03.AS-07) depends on cycle assignment, which is owned by slice 011 (cycles) — this slice ships group-by status and priority only; the command palette and search paths through which cancelled tasks remain reachable (EC-11) are owned by slice 013 (command-palette-search); the 30-second undo window for destructive actions (FR-040) is owned by slice 014 (undo); the account-deletion / erasure path (FR-085/FR-086, US-17) is owned by the account-management/data slice; the real-time fan-out of another member's Board/List changes (SignalR) is owned by slice 016 (real-time-collaboration) — this slice records transfer notes (D11 mechanism) instead of realizing them.

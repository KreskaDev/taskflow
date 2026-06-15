# Feature Specification: Project Board (Kanban)

**Feature Branch**: `010-project-board-kanban`

**Created**: 2026-06-13

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
- FR-031 (suppress single-key shortcuts in text inputs)
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

Exercised-but-not-owned (mechanics exercised here; canonical keyboard-shortcut requirement lives in a later slice):
- The arrow-move keys that move a selected task between Board columns (US-03.AS-04, AS-05, AS-06) are members of FR-029 (list shortcuts: arrows move, etc.), which is owned by slice 011 (cycles). This slice owns the Board-move acceptance scenarios but not the FR-029 shortcut requirement; the column-to-status mapping driving those moves is owned here via FR-025.

## User Scenarios & Testing *(mandatory)*

### User Story 3 - Project Kanban Workflow (Priority: P2)

User navigates to a specific project and views tasks on a Kanban board with columns for Backlog, Todo, In Progress, and Done. They move tasks between columns using keyboard arrows and manage the project workflow visually.

**Why this priority**: Project-level organization is essential for users managing work beyond simple daily lists, but depends on core task and project entities being functional first.

**Independent Test**: Can be tested by creating a project, adding tasks to it with different statuses, opening the project Board view, and moving tasks between columns using arrow keys.

> Scope note: the arrow keys that move a task between columns (AS-04, AS-05, AS-06) are members of FR-029 (list shortcuts), owned by slice 011 (cycles); this slice owns the Board-move acceptance scenarios and the column-to-status mapping (FR-025) that those moves drive. Cancelled tasks are hidden from the Board (EC-11 / FR-025) and remain reachable via the List view, search, and command palette.

> Scope note (US-03.AS-07 group-by split): the List view's group-by control (FR-024) offers grouping by cycle, status, or priority. This slice OWNS grouping by **status** and **priority** (both available from the Task entity owned by slice 002 and priorities from slice 005). Grouping **by cycle** depends on cycle assignment and is DEFERRED to slice 011 (cycles), which owns the Cycle entity and task-to-cycle assignment; until slice 011 lands, the by-cycle option is not offered.

**Acceptance Scenarios** (owned by this slice):

1. **(US-03.AS-01) Given** user is on any view, **When** they press `G P`, **Then** a project list appears for selection.
2. **(US-03.AS-02) Given** the project list is open, **When** user selects a project, **Then** the project view opens in the last-used mode (List or Board).
3. **(US-03.AS-03) Given** the project Board view is open, **When** the view renders, **Then** tasks are displayed in columns: Backlog, Todo, In Progress, Done.
4. **(US-03.AS-04) Given** a task is selected on the Board view, **When** user presses right arrow, **Then** the task moves one column to the right (e.g., Todo to In Progress) and its status updates accordingly.
5. **(US-03.AS-05) Given** a task is in the Done column, **When** user presses right arrow, **Then** nothing happens (Done is the last column).
6. **(US-03.AS-06) Given** a task is selected on the Board view, **When** user presses left arrow, **Then** the task moves one column to the left.
7. **(US-03.AS-07) Given** the project List view is open, **When** the view renders, **Then** tasks are displayed as a flat list, groupable by cycle, status, or priority via a group-by control.

### Edge Cases

- **EC-11 — Cancelled tasks on Board view**: Tasks with status "cancelled" are not displayed on the Kanban Board. They remain accessible via the List view, search, and command palette.

## Requirements *(mandatory)*

### Functional Requirements (slice-specific)

- **FR-024**: The Project List view MUST display a project's tasks as a flat list, groupable by cycle, status, or priority.
- **FR-025**: The Project Board view MUST display a project's tasks in a Kanban layout with columns mapping directly to statuses: Backlog (backlog), Todo (todo), In Progress (in_progress), Done (done). Tasks with status "cancelled" MUST be hidden from the Board view.

### Cross-cutting Requirements (realized in this slice)

Accessibility (per Constitution Principle II):
- **FR-031**: Single-key shortcuts MUST be suppressed when a text input is focused; only modifier-based shortcuts remain active during text input.
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

This slice is evaluated against constitution v4.0.0. Cross-cutting principles realized here:

- **I. Keyboard-First**: the project list is opened with `G P` (US-03.AS-01), a project is selected from it (US-03.AS-02), and tasks are moved between Board columns with the arrow keys (US-03.AS-04, AS-05, AS-06) — every Board and List interaction is keyboard-driven, with no required mouse action.
- **II. Accessibility (WCAG 2.1 AA)**: FR-042 (focus indicator on the selected card and the group-by control), FR-043 (ARIA roles/labels for columns, cards, and the list), FR-044 (contrast ≥ 4.5:1, so column/status is never conveyed by color alone), FR-045 (no AT-binding collisions for the arrow-move keys), FR-046 (no hover-only content), FR-047 (prefers-reduced-motion for column-move transitions). FR-031 keeps single-key shortcuts inert while a text input (e.g., the editor or search) is focused. **FR-101 is load-bearing here**: the Board/List of a shared project is a server-pushed surface, so when another member moves a task between columns the resulting card relocation MUST be announced to assistive technology via an ARIA live region (polite, coalesced under concurrent fan-out) WITHOUT stealing focus from the current selection; and the `G P` project-picker dialog (US-03.AS-01/AS-02) MUST follow the dialog focus contract — set initial focus into the picker, trap focus, dismiss on Esc, and return focus to the invoker on close.
- **III. Instant Response**: SC-003 (owned by slice 002, task-capture) — selecting a card, moving it between columns, and switching the List group-by all paint their optimistic result within one animation frame while the C# API reconciles or rolls back the status change asynchronously (server-confirmed mutations within a p95 < 200ms budget). Because the Board/List of a shared project is a shared view, it also receives server-initiated updates over SignalR when another member moves a task: an inbound remote patch resolves under last-write-wins but MUST yield to a pending local optimistic move until that move's server-ack resolves, then reconcile — a remote update never clobbers an in-flight local column move.
- **IV. Minimalist UI**: the Board surfaces the four workflow columns and hides cancelled tasks (FR-025 / EC-11), and the List exposes grouping on demand through a single group-by control (FR-024), keeping density without clutter. Skeleton screens are permitted for the initial network-bound load of a project's tasks; they MUST NOT mask a column move whose optimistic result could be shown instead.
- **V. Connected, Server-Authoritative**: SC-004 (owned by slice 002, task-capture) — both views read and write task status through the app's own C# API and PostgreSQL database, the system of record, with no third-party runtime data service (the sole permitted external runtime dependency is Google OAuth, for sign-in only).
- **VI. Type Safety End-to-End**: the column-to-status mapping (FR-025) is expressed over the typed status enum (backlog/todo/in_progress/done/cancelled) from the schema; a right/left move resolves to a valid adjacent status, with the Done boundary (US-03.AS-05) enforced as a typed no-op.
- **VII. Data Integrity & Resilience**: FR-049 (any failed status update surfaces a clear, recoverable message), FR-050 (structured logging of such failures), FR-051 (the auto-backup hook stays in place ahead of any schema change).
- **VIII. Test-First**: each owned acceptance scenario above, plus EC-11, is independently testable (Red-Green-Refactor); integration tests cover the Board/List command and query handlers through the real database, including authorization (a non-member request, or a viewer attempting a column move, MUST be denied).
- **IX. Authentication & Authorization**: this slice's Board and List handlers authorize **deny-by-default, dispatched by the containing project's visibility** (not a conjunction of tiers), at the API/handler layer (FR-068). For a **personal** project the policy authorizes on ownership (`ownerId`) with queries scoped to the caller (FR-065), so its Board/List is visible only to its owner. For a **shared** project the policy requires current `ProjectMembership` (FR-066), so the Board/List is visible only to current members, and applies role sufficiency (FR-067): viewing the Board/List and regrouping the List (group-by) require viewer+; moving a task between columns is a write that requires editor or owner. The `createdBy` and assignee fields surfaced on cards are **provenance only** and confer no standalone access; on leave/remove/unshare the user loses ALL access to that project's Board/List regardless of authorship or assignment (FR-066). These checks live in the application-layer authorization policy (backed by ProjectMembership), not in ad hoc UI code, and each handler ships with both an allow and a deny test (SC-016 / Principle VIII).
- **X. Time & Timezone**: this slice owns no date-relative computation (Today/Upcoming membership, cycle boundaries, recurrence rollover, and natural-language resolution are owned by other slices). Any due date surfaced on a Board card or List row is stored in UTC and rendered against the single instance reference timezone `Europe/Warsaw`, applied identically on client and server (FR-092), with date-only vs date-time distinguished by the `has_time` flag — consistent display, no per-slice time logic introduced here.
- **XI. Privacy & Personal Data**: the Board/List surfaces personal identifiers (`createdBy`, assignee display names/avatars) on cards. This slice introduces no new personal-data store and owns no erasure path — account deletion and the erasure cascade (FR-085/FR-086, US-17) are owned by the privacy slice (data-export-import / account management). When a member leaves, is removed, or a project is unshared, they lose all access to this Board/List (FR-066) so no residual personal data is exposed to a non-member here.
- **XII. Security by Default**: task titles and markdown descriptions rendered on Board cards and List rows are untrusted, user-authored content and MUST be output-encoded/sanitized to a constrained safe subset so raw HTML injection is impossible, behind the production Content-Security-Policy and security response headers (FR-099). The secrets clause of Principle XII (session key, OAuth secret, DB/broker credentials) is infrastructure-level and not exercised by this slice's view handlers.

**Known compliance gap (deferred, accepted at slicing time):** Principle I (Keyboard-First) is exercised here through the arrow-move keys, but the canonical shortcut requirement for those keys — FR-029 (list shortcuts: arrows move, `E` edit, `Space` toggle done, `1-4` priority, etc.) — is owned by slice 011 (cycles). This slice owns the Board-move acceptance scenarios (US-03.AS-04, AS-05, AS-06) and the column-to-status mapping that drives them (FR-025), but does not own FR-029; the shortcut requirement is delivered in slice 011. No FR-040 undo gap arises here: moving a task between columns is a reversible status change (move it back), not one of FR-040's destructive/irreversible actions.

## Assumptions

This slice introduces no new assumptions. The assumptions owned by earlier slices continue to apply unchanged — in particular ASM-01 and ASM-02 from slice 001 (accounts-and-auth), and the project-model assumptions established in slice 004 (project-management). For reference, the full text from product-vision.md:

- **ASM-01 — Multi-user team**: The application serves a small collaborating team (~10). Each user authenticates (Google) and has personal data plus access to shared projects.
- **ASM-02 — Web platform**: The MVP targets modern desktop browsers. Native mobile apps, PWA/offline operation, and cross-device sync are explicitly out of scope.

## Out of Scope

This slice confirms the full MVP out-of-scope boundary (OOS-01..OOS-19 from product-vision.md):

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

Note: multi-user collaboration, sharing, and in-app notifications are IN scope for the MVP (US-11/US-12/US-16); the OOS-01 and OOS-06 markers above are retained verbatim as historical promotion notes, not as current out-of-scope assertions.

Also out of scope for this slice specifically (deferred to later slices): the canonical list-shortcut requirement FR-029, including the arrow-move keys exercised here, is owned by slice 011 (cycles); grouping the List view **by cycle** (one option of FR-024's group-by control, exercised via US-03.AS-07) depends on cycle assignment, which is owned by slice 011 (cycles) — this slice ships group-by status and priority only; the command palette and search paths through which cancelled tasks remain reachable (EC-11) are owned by slice 013 (command-palette-search); the 30-second undo window for destructive actions (FR-040) is owned by slice 014 (undo); the account-deletion / erasure path (FR-085/FR-086, US-17) is owned by the account-management/data slice.

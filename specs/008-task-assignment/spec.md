# Feature Specification: Task Assignment

**Feature Branch**: `008-task-assignment`

**Created**: 2026-06-13

**Status**: Draft

**Input**: Slice 008 of the TaskFlow MVP. Source of truth: `.specify/memory/product-vision.md`. Goal: multiple assignees on shared-project tasks — add and remove assignees, and an "Assigned to me" view listing tasks across shared projects where the current user is an assignee. Assignment is shared-only; personal-project tasks offer no assignment control. Builds on project sharing & membership (slice 007).

## Provenance

This slice realizes the following items from product-vision.md:

Slice-specific (owned):
- US-13 (Task Assignment) — AS-01, AS-02, AS-03, AS-04
- FR-069 (shared-project tasks support multiple assignees who MUST be current members of that project; personal-project tasks offer no assignment)
- FR-070 (editors/owners add/remove assignees; assigning notifies)
- FR-071 ("Assigned to me" view)

Cross-cutting (realized in this slice):
- UI / Accessibility: FR-031, FR-042, FR-043, FR-044, FR-045, FR-046, FR-047, FR-101
- Resilience: FR-049, FR-050, FR-051
- Access-control sub-block: FR-065, FR-066, FR-067, FR-068

MVP boundary confirmed:
- OOS-01..OOS-19 (full MVP out-of-scope confirmation)

Entity touchpoints:
- ENT-01 (Task) — realizes the `assignees` attribute (shared-project tasks only)

Depends on:
- 007 (project-sharing-membership)

## User Scenarios & Testing *(mandatory)*

### User Story 13 - Task Assignment (Priority: P2)

In a shared project, members assign one or more members to a task and filter by "assigned to me"; assignment is shared-only.

**Why this priority**: Assignment turns shared projects into coordinated work — it tells members what is theirs. It depends on sharing and membership being in place first.

**Independent Test**: Can be tested by assigning members to a task in a shared project, changing and removing assignees, opening "Assigned to me", and confirming no assignment control appears on personal-project tasks.

**Acceptance Scenarios** (owned by this slice):

1. **(US-13.AS-01) Given** a task in a shared project, **When** an editor/owner assigns one or more members, **Then** they appear as assignees and each is notified.
2. **(US-13.AS-02) Given** assignees on a task, **When** an editor/owner changes/removes assignees, **Then** the assignee set updates.
3. **(US-13.AS-03) Given** a user with assigned tasks, **When** they open "Assigned to me", **Then** they see tasks across shared projects where they are an assignee.
4. **(US-13.AS-04) Given** a personal (not shared) project, **When** a user views a task, **Then** no assignment control is offered.

> Notification scope: in US-13.AS-01 and FR-070, "each is notified" / "assigning a member MUST notify them" is *exercised* here (adding an assignee emits a `TaskAssigned` domain event — see Key Entities), but the canonical notification acceptance scenarios and delivery (notification center, live toasts, preferences) are owned by slice 017 (notifications), which consumes that event.

**Additional slice behaviors** (per the assignee-membership remediation, B3/008):

5. **Given** a task in a shared project, **When** an editor/owner attempts to assign a user who is NOT a current member of that project, **Then** the assignment is denied at the API/handler layer with a clear, recoverable message (FR-049) and no assignee is added; this deny path MUST have an explicit authorization deny test (per Constitution Principle VIII / SC-016).
6. **Given** an editor/owner who is also a member of the shared project, **When** they assign themselves to a task, **Then** self-assignment is allowed and the assignee set includes them, but the resulting `TaskAssigned` event MUST NOT produce a self-notification (self-suppression via `actorUserId`).
7. **Given** an assignee who subsequently leaves, is removed from, or whose project is unshared, **When** authorization is re-evaluated, **Then** that user loses ALL access to the project's tasks regardless of their prior assignment (assignee is provenance only; dispatch-by-visibility revokes on leave/remove/unshare, per FR-066) and their assignment on those tasks is cleared (per slice 007 FR-059/FR-062/FR-063).

## Requirements *(mandatory)*

### Functional Requirements (slice-specific)

- **FR-069**: Tasks in shared projects MUST support multiple assignees (members of that project); personal-project tasks MUST NOT offer assignment.
- **FR-070**: Editors/owners MUST be able to add/remove assignees; assigning a member MUST notify them.
- **FR-071**: System MUST provide an "Assigned to me" view listing tasks across shared projects where the current user is an assignee.

> Assignee-membership enforcement (B3 / needs-more-info resolution for slice 008): an assignee MUST be a **current member** of the task's shared project. The handler MUST verify membership at assignment time and reject (deny-by-default, FR-068) any attempt to assign a non-member; this is covered by an explicit allow+deny authorization test pair (Constitution Principle VIII, SC-016). Assignee is **provenance only** and confers no standalone access — it is never a tier in an authorization conjunction; dispatch-by-visibility (FR-065/FR-066) decides access, and on leave/remove/unshare the user loses ALL access and their assignment is cleared. Self-assignment is allowed (an editor/owner who is a member may assign themselves); a self-assignment MUST NOT emit a self-notification (self-suppression via `actorUserId`).

> Domain event: adding an assignee (FR-070) emits a `TaskAssigned` domain event carrying the assignee delta (added/removed assignee user ids) and the `actorUserId`. The event is **idempotent** (re-applying the same delta produces no additional effect / at most one notification per genuine change). It is consumed by slice 017 (notifications) to generate the in-app notification for the assigned member (suppressing the actor's own self-assignment); this slice raises the event but does not deliver notifications.

### Cross-cutting Requirements (realized in this slice)

Access Control & Authorization (per Constitution Principle IX):
- **FR-065**: Authorization MUST be dispatched by the containing resource's visibility (not a conjunction of tiers): personal/unprojected data authorizes on ownership (`createdBy`/`ownerId`) with queries scoped to the caller; shared-project entities authorize on current `ProjectMembership` + role. Every query MUST be scoped accordingly (per-user isolation).
- **FR-066**: Access to a shared project's data MUST require current membership in that project. `createdBy` and assignee are provenance only and confer NO standalone access; on leave/remove/unshare a user MUST lose ALL access to that project's data regardless of authorship or assignment.
- **FR-067**: Each operation on a shared-project resource MUST require sufficient role (viewer=read, editor=write, owner=manage); insufficient role MUST be denied.
- **FR-068**: Authorization MUST be deny-by-default and enforced at the API/handler layer for every read and write.

Accessibility (per Constitution Principle II):
- **FR-031**: Single-key shortcuts MUST be suppressed when a text input is focused; only modifier-based shortcuts remain active during text input.
- **FR-042**: Every focusable element MUST have a visible focus indicator.
- **FR-043**: All interactive elements MUST have correct ARIA roles and labels for screen reader compatibility.
- **FR-044**: Text contrast ratio MUST be at least 4.5:1 (3:1 for large text).
- **FR-045**: Custom keyboard shortcuts MUST NOT collide with native assistive-technology bindings.
- **FR-046**: No content may be accessible only via hover — all tooltips and popovers MUST have a keyboard/focus-triggered equivalent.
- **FR-047**: Animations MUST respect the `prefers-reduced-motion` user preference; when reduced motion is active, transitions MUST be instant or under 100ms.
- **FR-101**: Server-initiated updates and toasts MUST be conveyed to assistive technology via an appropriate ARIA live region without stealing focus, and confirmation/command-palette dialogs MUST follow the dialog focus contract (set initial focus, trap focus, dismiss on Esc, return focus to the invoker on close).

Error Handling & Data Integrity (per Constitution Principle VII):
- **FR-049**: All errors MUST be presented to the user with a clear message and an actionable recovery suggestion. No operation may fail silently.
- **FR-050**: Errors MUST be logged with structured context (severity level, operation context, and error details) for debugging purposes.
- **FR-051**: Before any data migration, the system MUST automatically create a backup of user data. The user MUST be able to restore from this backup.

### Key Entities

- **ENT-01 — Task**: The core work item. Has a title (required), description, priority (P0-P3), status (backlog/todo/in_progress/done/cancelled), due date, labels, project reference, cycle reference, recurrence rule, createdBy (the User who created it), assignees (zero or more Users; only on shared-project tasks), and system timestamps (created_at, updated_at, completed_at). New tasks default to "backlog" status. A recurring task has a linked recurrence rule that generates successor instances.

> Slice scope for ENT-01: this slice realizes the `assignees` attribute — the set of zero or more Users assigned to a task, populated only on shared-project tasks (FR-069). Adding an assignee emits a `TaskAssigned` domain event consumed by slice 017 (notifications). All other Task attributes are owned by their respective slices.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-013**: Authorization is enforced on 100% of data operations (no read/write bypasses the policy; deny cases covered by integration tests).
- **SC-016**: Authorization coverage is mechanically verifiable: every data handler ships with both an allow and a deny test, and a role×operation deny matrix demonstrates that insufficient ownership/membership/role is rejected.
- **SC-003**: Every user action on a task (create, edit, complete, delete, move, reprioritize) paints its optimistic result within 16ms of the triggering keypress; the server reconciles or rolls back asynchronously.
- **SC-007**: Codebase enforces strict type safety with no bypasses, per Constitution Principle VI.
- **SC-012**: Server data operations (single-entity reads/writes) complete within a p95 of 200ms against a representative dataset.

## Constitution Compliance

This slice is evaluated against constitution v4.0.0. Cross-cutting principles realized here:

- **I. Keyboard-First**: assigning/removing assignees and opening the "Assigned to me" view are keyboard-driven; the assignee selector is operable without a mouse.
- **II. Accessibility (WCAG 2.1 AA)**: FR-042 (focus indicator), FR-043 (ARIA roles/labels), FR-044 (contrast ≥ 4.5:1), FR-045 (no AT-binding collisions), FR-046 (no hover-only content — the assignee list and controls have keyboard/focus-triggered equivalents), FR-047 (prefers-reduced-motion). FR-031 keeps single-key shortcuts from hijacking text entry in the assignee picker. **FR-101**: any server-initiated update to assignees (a remote member's assignment change arriving over SignalR) and any resulting toast MUST be announced via an ARIA live region without stealing focus, and the assignee-picker / confirmation dialogs MUST follow the dialog focus contract (initial focus, focus trap, Esc to dismiss, focus returned to the invoker on close).
- **III. Instant Response**: SC-003 (optimistic UI paints the assignee change within 16ms of keypress, with the server reconciling or rolling back asynchronously), SC-012 (server data operations within a p95 of 200ms).
- **V. Connected, Server-Authoritative**: assignee data is persisted server-side through the application's own API, which is the system of record; the client holds no authoritative copy.
- **VI. Type Safety End-to-End**: SC-007; the EF Core / PostgreSQL schema is the source of truth, with C# entity types on the server and TypeScript types on the Next.js client kept in lockstep, and runtime validation at the API request/response boundaries. The error contract surfaced when an assignment is rejected (e.g., non-member assignee) follows the ProblemDetails-based contract (ADR-0009) modeled by the generated client.
- **VII. Data Integrity & Resilience**: FR-049 (error + recovery — including the recoverable message when a non-member assignment is denied), FR-050 (structured logging), FR-051 (auto-backup before migration).
- **VIII. Test-First**: each owned acceptance scenario above is independently testable (Red-Green-Refactor); every assignment handler ships with both an allow test and a deny test (assigning a non-member, and a viewer attempting to assign, MUST be denied — Constitution Principle VIII, SC-016).
- **IX. Authentication & Authorization**: authorization is **dispatched by the containing project's visibility, not a Tier A/B conjunction**. Assignment exists only on shared-project tasks, so it authorizes on **current `ProjectMembership` + role**: FR-065 (dispatch-by-visibility, per-user scoped queries), FR-066 (current membership required; `createdBy`/assignee are **provenance only** and grant no standalone access; leave/remove/unshare revokes ALL access and clears assignments), FR-067 (sufficient-role — only editors/owners assign per FR-070; viewers are read-only and denied), FR-068 (deny-by-default at the API/handler layer for every read and write); SC-013/SC-016 (authorization enforced and mechanically verified on 100% of operations with allow+deny tests). The assignee-must-be-a-current-member rule is enforced at the handler with a deny test; the "Assigned to me" view (FR-071) is scoped to the caller's identity across projects where they currently hold membership (a user who lost membership stops seeing those tasks). Sessions/admission (FR-087/FR-088, Principle IX) gate every request upstream of this slice; this slice adds no new session or admission surface but relies on an authenticated, admitted caller.
- **X. Time & Timezone**: this slice introduces no new date-relative computation; assignment carries no due-date logic. Any timestamps recorded for assignment events MUST be stored in UTC and rendered against the single instance reference timezone `Europe/Warsaw` (FR-092), consistent with the rest of the system.
- **XI. Privacy & Personal Data**: assignee references are personal data tied to a User; on a member's leave/removal/unshare or account deletion (FR-085), assignment references to that user MUST be cleared/nulled or reassigned per the erasure cascade, leaving no residual access via a stale assignment.
- **XII. Security by Default**: assignee selection takes a typed User id (no free-form user content rendered as HTML); any displayed member name/avatar is output-encoded (FR-099). The `TaskAssigned` event payload carries only ids/deltas and the `actorUserId`, no secrets, and is never logged with sensitive context (FR-100).

## Assumptions

- **ASM-01 — Multi-user team**: The application serves a small collaborating team (~10). Each user authenticates (Google) and has personal data plus access to shared projects.
- **ASM-06 — In-app notifications only**: The app provides in-app notifications (assignment, mention, changes); email and push/device notifications and reminders are out of scope.
- **ASM-10 — Small team scale**: Small team (~10 users) on a single shared instance; not organizational multi-tenancy.
- **ASM-12 — Instance reference timezone**: The instance operates against a single reference timezone, `Europe/Warsaw`, for all date-relative computation; per-user timezones are out of scope.
- **ASM-13 — Gated admission**: Account creation is gated (email allowlist or Google Workspace hosted-domain); the instance is not open to any Google account or to public sign-up.

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

Note: multi-user collaboration and in-app notifications are explicitly IN scope (OOS-01 promoted; OOS-06 partially promoted) — they are NOT out of scope.

Additionally, the notification delivery triggered by assignment (notification center, live toasts, per-type preferences) is owned by slice 017 (notifications); this slice raises the `TaskAssigned` domain event (carrying the delta + `actorUserId`, idempotent) but does not deliver notifications. Comments & @mentions on assigned tasks are owned by slice 009 (comments-mentions). Sharing, membership, and roles are owned by slice 007 (project-sharing-membership).

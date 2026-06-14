# Feature Specification: Task Assignment

**Feature Branch**: `008-task-assignment`

**Created**: 2026-06-13

**Status**: Draft

**Input**: Slice 008 of the TaskFlow MVP. Source of truth: `.specify/memory/product-vision.md`. Goal: multiple assignees on shared-project tasks — add and remove assignees, and an "Assigned to me" view listing tasks across shared projects where the current user is an assignee. Assignment is shared-only; personal-project tasks offer no assignment control. Builds on project sharing & membership (slice 007).

## Provenance

This slice realizes the following items from product-vision.md:

Slice-specific (owned):
- US-13 (Task Assignment) — AS-01, AS-02, AS-03, AS-04
- FR-069 (shared-project tasks support multiple assignees; personal-project tasks offer no assignment)
- FR-070 (editors/owners add/remove assignees; assigning notifies)
- FR-071 ("Assigned to me" view)

Cross-cutting (realized in this slice):
- UI / Accessibility: FR-031, FR-042, FR-043, FR-044, FR-045, FR-046, FR-047
- Resilience: FR-049, FR-050, FR-051
- Access-control sub-block: FR-065, FR-066, FR-067, FR-068

MVP boundary confirmed:
- OOS-01..OOS-17 (full MVP out-of-scope confirmation)

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

## Requirements *(mandatory)*

### Functional Requirements (slice-specific)

- **FR-069**: Tasks in shared projects MUST support multiple assignees (members of that project); personal-project tasks MUST NOT offer assignment.
- **FR-070**: Editors/owners MUST be able to add/remove assignees; assigning a member MUST notify them.
- **FR-071**: System MUST provide an "Assigned to me" view listing tasks across shared projects where the current user is an assignee.

> Domain event: adding an assignee (FR-070) emits a `TaskAssigned` domain event. That event is consumed by slice 017 (notifications) to generate the in-app notification for the assigned member; this slice raises the event but does not deliver notifications.

### Cross-cutting Requirements (realized in this slice)

Access Control & Authorization (per Constitution Principle IX):
- **FR-065**: Every query MUST be scoped to data the caller owns or has membership access to (per-user isolation).
- **FR-066**: Access to a shared project's data MUST require membership in that project.
- **FR-067**: Each operation MUST require sufficient role (viewer=read, editor=write, owner=manage); insufficient role MUST be denied.
- **FR-068**: Authorization MUST be deny-by-default and enforced at the API/handler layer for every read and write.

Accessibility (per Constitution Principle II):
- **FR-031**: Single-key shortcuts MUST be suppressed when a text input is focused; only modifier-based shortcuts remain active during text input.
- **FR-042**: Every focusable element MUST have a visible focus indicator.
- **FR-043**: All interactive elements MUST have correct ARIA roles and labels for screen reader compatibility.
- **FR-044**: Text contrast ratio MUST be at least 4.5:1 (3:1 for large text).
- **FR-045**: Custom keyboard shortcuts MUST NOT collide with native assistive-technology bindings.
- **FR-046**: No content may be accessible only via hover — all tooltips and popovers MUST have a keyboard/focus-triggered equivalent.
- **FR-047**: Animations MUST respect the `prefers-reduced-motion` user preference; when reduced motion is active, transitions MUST be instant or under 100ms.

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
- **SC-003**: Every user action on a task (create, edit, complete, delete, move, reprioritize) paints its optimistic result within 16ms of the triggering keypress; the server reconciles or rolls back asynchronously.
- **SC-007**: Codebase enforces strict type safety with no bypasses, per Constitution Principle VI.
- **SC-012**: Server data operations (single-entity reads/writes) complete within a p95 of 200ms against a representative dataset.

## Constitution Compliance

This slice is evaluated against constitution v3.0.0. Cross-cutting principles realized here:

- **I. Keyboard-First**: assigning/removing assignees and opening the "Assigned to me" view are keyboard-driven; the assignee selector is operable without a mouse.
- **II. Accessibility (WCAG 2.1 AA)**: FR-042 (focus indicator), FR-043 (ARIA roles/labels), FR-044 (contrast ≥ 4.5:1), FR-045 (no AT-binding collisions), FR-046 (no hover-only content — the assignee list and controls have keyboard/focus-triggered equivalents), FR-047 (prefers-reduced-motion). FR-031 keeps single-key shortcuts from hijacking text entry in the assignee picker.
- **III. Instant Response**: SC-003 (optimistic UI paints the assignee change within 16ms of keypress, with the server reconciling or rolling back asynchronously), SC-012 (server data operations within a p95 of 200ms).
- **V. Connected, Server-Authoritative**: assignee data is persisted server-side through the application's own API, which is the system of record; the client holds no authoritative copy.
- **VI. Type Safety End-to-End**: SC-007; the EF Core / PostgreSQL schema is the source of truth, with C# entity types on the server and TypeScript types on the Next.js client kept in lockstep, and runtime validation at the API request/response boundaries.
- **VII. Data Integrity & Resilience**: FR-049 (error + recovery), FR-050 (structured logging), FR-051 (auto-backup before migration).
- **VIII. Test-First**: each owned acceptance scenario above is independently testable (Red-Green-Refactor), including authorization deny cases.
- **IX. Authentication & Authorization**: FR-065 (per-user isolation), FR-066 (membership required for shared-project data), FR-067 (sufficient-role checks — only editors/owners assign, per FR-070; viewers are read-only), FR-068 (deny-by-default, enforced at the API/handler layer); SC-013 (authorization enforced on 100% of operations). Assignment is offered only on shared-project tasks (FR-069), and the "Assigned to me" view (FR-071) is scoped to the caller's identity across projects where they hold membership.

## Assumptions

- **ASM-01 — Multi-user team**: The application serves a small collaborating team (~10). Each user authenticates (Google) and has personal data plus access to shared projects.
- **ASM-06 — In-app notifications only**: The app provides in-app notifications (assignment, mention, changes); email and push/device notifications and reminders are out of scope.
- **ASM-10 — Small team scale**: Small team (~10 users) on a single shared instance; not organizational multi-tenancy.

## Out of Scope

This slice confirms the full MVP out-of-scope boundary (OOS-01..OOS-17 from product-vision.md):

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

Additionally, the notification delivery triggered by assignment (notification center, live toasts, per-type preferences) is owned by slice 017 (notifications); this slice raises the `TaskAssigned` domain event but does not deliver notifications. Comments & @mentions on assigned tasks are owned by slice 009 (comments-mentions). Sharing, membership, and roles are owned by slice 007 (project-sharing-membership).

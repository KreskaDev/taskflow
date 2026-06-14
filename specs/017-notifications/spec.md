# Feature Specification: Notifications

**Feature Branch**: `017-notifications`

**Created**: 2026-06-13

**Status**: Draft

**Input**: Slice 017 of the TaskFlow MVP. Source of truth: `.specify/memory/product-vision.md` (amended) and `.specify/memory/constitution.md` (v3.0.0). Goal: an in-app notification center (newest-first, read/unread) with live SignalR toasts when online, mark-read / mark-all-read, and per-type preferences; notifications are generated when a member is assigned, @mentioned, or when a task they are an assignee of changes — consuming the `TaskAssigned` / `UserMentioned` and task-changed domain events emitted by slices 008 and 009. In-app only; email and push/device notifications remain out of scope.

## Provenance

Slice-specific (owned by this slice):
- US-16 (Notifications) — AS-01, AS-02, AS-03, AS-04
- FR-079 (generate in-app notification on assigned / @mentioned / assignee-task changed)
- FR-080 (notification center, newest-first, read/unread state)
- FR-081 (live toast when online)
- FR-082 (mark read / mark all read)
- FR-083 (per-type notification preferences)
- FR-084 (in-app only; email and push/device out of scope)
- ENT-09 (Notification)

Cross-cutting (realized in this slice):
- FR-031 (suppress single-key shortcuts in text inputs)
- FR-042 (visible focus indicator)
- FR-043 (ARIA roles/labels)
- FR-044 (text contrast ≥ 4.5:1)
- FR-045 (no collision with assistive-technology bindings)
- FR-046 (no hover-only content)
- FR-047 (prefers-reduced-motion)
- FR-049 (error message + recovery action)
- FR-050 (structured error logging)
- FR-051 (auto-backup before migration)

Access control (realized in this slice):
- FR-065 (per-user isolation — queries scoped to the caller)
- FR-066 (shared-project access requires membership)
- FR-067 (sufficient-role enforcement: viewer=read, editor=write, owner=manage)
- FR-068 (authorization deny-by-default at the API/handler layer)

MVP boundary confirmed:
- OOS-01..OOS-17 (full MVP out-of-scope confirmation)

Entity touchpoints:
- ENT-09 (Notification) — owned here
- ENT-06 (User) — recipient reference (read-only; owned by slice 001)
- ENT-01 (Task) — source reference for assigned / changed notifications (read-only; owned by slice 002)
- ENT-08 (Comment) — source reference for @mention notifications (read-only; owned by slice 009)

Depends on:
- 008 (task-assignment) — emits `TaskAssigned`; source of assigned and assignee-task-changed triggers
- 009 (comments-mentions) — emits `UserMentioned`; source of mention triggers
- 016 (real-time-collaboration) — SignalR transport for live toast delivery

## User Scenarios & Testing *(mandatory)*

### User Story 16 - Notifications (Priority: P2)

Members get in-app notifications when assigned, @mentioned, or when items they are an assignee of change; a notification center, live toasts, mark-read, per-type preferences; in-app only.

**Why this priority**: Notifications close the loop on collaboration — members learn when something needs their attention. They depend on assignment, mentions, and real-time being present.

**Independent Test**: Can be tested by triggering an assignment and an @mention, verifying in-app notifications and a live toast when online, opening the notification center, marking notifications read, and disabling a notification type.

**Acceptance Scenarios** (owned by this slice):

1. **(US-16.AS-01) Given** a member, **When** they are assigned to a task or @mentioned, **Then** they receive an in-app notification (and a live toast if online).
2. **(US-16.AS-02) Given** the notification center, **When** they open it, **Then** notifications are listed newest-first with read/unread state.
3. **(US-16.AS-03) Given** an unread notification, **When** they mark it read (or mark all read), **Then** its state updates.
4. **(US-16.AS-04) Given** notification preferences, **When** they disable a type, **Then** they stop receiving that type.

## Requirements *(mandatory)*

### Functional Requirements (slice-specific)

- **FR-079**: System MUST generate an in-app notification when a user is assigned to a task, @mentioned, or when a task they are an assignee of changes.
- **FR-080**: System MUST present a notification center listing the user's notifications newest-first with read/unread state.
- **FR-081**: When the user is online, a new notification MUST also surface as a live toast.
- **FR-082**: Users MUST be able to mark a notification read and mark all read.
- **FR-083**: Users MUST be able to set per-type notification preferences (enable/disable a type).
- **FR-084**: Notifications MUST be in-app only; email and push/device notifications are out of scope.

### Cross-cutting Requirements (realized in this slice)

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

Authentication & Authorization (per Constitution Principle IX):
- **FR-065**: Every query MUST be scoped to data the caller owns or has membership access to (per-user isolation).
- **FR-066**: Access to a shared project's data MUST require membership in that project.
- **FR-067**: Each operation MUST require sufficient role (viewer=read, editor=write, owner=manage); insufficient role MUST be denied.
- **FR-068**: Authorization MUST be deny-by-default and enforced at the API/handler layer for every read and write.

### Key Entities

- **ENT-09 — Notification**: Has a recipient User, type (assigned/mentioned/changed), source reference, read flag, and created timestamp.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-014**: A change to a shared item is reflected on other members' open shared views within ~1 second.

## Constitution Compliance

This slice is evaluated against constitution v3.0.0. Cross-cutting principles realized here:

- **I. Keyboard-First**: the notification center and per-type preferences are operable entirely via keyboard; opening, navigating, and marking notifications read require no mouse, and the toast carries a keyboard/focus-triggered affordance (FR-046).
- **II. Accessibility (WCAG 2.1 AA)**: FR-042 (focus indicator), FR-043 (ARIA roles/labels — including a live region so a toast is announced to screen readers), FR-044 (contrast ≥ 4.5:1), FR-045 (no AT-binding collisions), FR-046 (no hover-only content), FR-047 (prefers-reduced-motion governs toast transitions). FR-031 keeps single-key shortcuts from hijacking text entry in the preferences UI.
- **III. Instant Response**: SC-014 — a triggering change fans out so the recipient's live toast surfaces within ~1s over SignalR; mark-read / mark-all-read paint optimistically and reconcile against the server. Toasts are non-blocking and the UI accepts input while one is visible.
- **V. Connected, Server-Authoritative**: notifications are persisted server-side through the app's own API and PostgreSQL; the client holds no authoritative copy. Notifications are generated by server-side domain-event handlers, not synthesized on the client.
- **VI. Type Safety End-to-End**: the EF Core / PostgreSQL Notification schema is the source of truth, with C# entity types on the server and a generated TypeScript client on the Next.js web app kept in lockstep, plus runtime validation at the API request/response boundary.
- **VII. Data Integrity & Resilience**: FR-049 (error + recovery), FR-050 (structured logging), FR-051 (auto-backup before the migration that adds the Notification table and preferences). Notifications are non-destructive records; marking read is reversible state, so the 30-second data undo (FR-040) does not apply.
- **VIII. Test-First**: each owned acceptance scenario above is independently testable (Red-Green-Refactor), including integration tests that a notification is generated only for the intended recipient and that disabling a type suppresses it.
- **IX. Authentication & Authorization**: FR-065 (per-user isolation — a user reads and mutates only their own notifications and preferences), FR-066 (membership required to receive notifications sourced from a shared project), FR-067 (sufficient-role enforcement on the source operations that trigger notifications), FR-068 (deny-by-default at the API/handler layer for every notification read and write).

## Assumptions

- **ASM-01 — Multi-user team**: The application serves a small collaborating team (~10). Each user authenticates (Google) and has personal data plus access to shared projects.
- **ASM-02 — Web platform**: The MVP targets modern desktop browsers. Native mobile apps, PWA/offline operation, and cross-device sync are explicitly out of scope.
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

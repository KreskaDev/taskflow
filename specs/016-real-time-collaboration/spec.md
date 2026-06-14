# Feature Specification: Real-Time Collaboration

**Feature Branch**: `016-real-time-collaboration`

**Created**: 2026-06-13

**Status**: Draft

**Input**: Slice 016 of the TaskFlow MVP. Source of truth: `.specify/memory/product-vision.md`. Goal: live propagation of changes to shared items across members' open shared views via SignalR, with an inbound remote patch yielding to any in-flight local optimistic edit and reconciling under last-write-wins, and reconnect re-syncing the current state of visible shared views — layered on top of project sharing and the optimistic-UI model. No presence.

## Provenance

This slice realizes the following items from product-vision.md:

Slice-specific:
- US-15 (Real-Time Collaboration) — AS-01, AS-02, AS-03
- FR-076 (real-time propagation to open shared views via SignalR within the fan-out budget)
- FR-077 (inbound real-time update must not overwrite an in-flight local optimistic edit; last-write-wins after server-ack)
- FR-078 (reconnect re-syncs visible shared views)
- SC-014 (shared-item change reflected on other members' open shared views within ~1s)
- SC-015 (~10 concurrent users without perceptible degradation)

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

Access control (per Constitution Principle IX — realized in this slice):
- FR-065 (per-user isolation: every query scoped to owned/membership-accessible data)
- FR-066 (access to a shared project's data requires membership)
- FR-067 (sufficient-role enforcement per operation)
- FR-068 (deny-by-default authorization at the API/handler layer for every read and write)

MVP boundary confirmed:
- OOS-01..OOS-17 (full MVP out-of-scope confirmation, including OOS-15 — presence indicators and activity/audit feed)

Entity touchpoints (no new entity introduced in this slice):
- ENT-01 (Task)
- ENT-02 (Project)
- ENT-07 (ProjectMembership)

Depends on:
- 007 (project-sharing-membership) — shared projects, membership, and roles are the precondition for real-time fan-out and its authorization scoping.

## User Scenarios & Testing *(mandatory)*

### User Story 15 - Real-Time Collaboration (Priority: P2)

When a member changes a shared item, other members viewing it see the change live.

**Why this priority**: Live updates make collaboration feel coherent — members see each other's work without manual refresh. It layers on top of sharing and the optimistic-UI model.

**Independent Test**: Can be tested by having two members view the same shared project, changing a task in one client and observing the live update in the other, verifying in-flight edits are not clobbered, and confirming re-sync after a dropped connection.

**Acceptance Scenarios** (owned by this slice):

1. **(US-15.AS-01) Given** two members viewing the same shared project, **When** one changes a task, **Then** the other's view updates live within ~1s without manual refresh.
2. **(US-15.AS-02) Given** a member with a pending local optimistic edit, **When** a remote update for the same item arrives, **Then** it does not clobber the in-flight edit (reconciles after the local server-ack under last-write-wins).
3. **(US-15.AS-03) Given** a dropped connection, **When** connectivity returns, **Then** the client reconnects and re-syncs the current state of visible shared views.

### Edge Cases

- **Concurrent edit to the same item**: When two members edit the same shared item near-simultaneously, the inbound remote patch reconciles under last-write-wins after the local mutation's server-ack; an in-flight local optimistic edit is never clobbered by an inbound update (FR-077, Principle III).
- **Dropped connection during a live session**: When the SignalR connection drops, the client reconnects on connectivity return and re-syncs the current state of the visible shared views rather than replaying missed individual patches (FR-078).
- **Non-member / insufficient role on the channel**: Real-time fan-out reaches only members of the shared project, and only for data their role permits; access is scoped by per-user isolation and membership, deny-by-default (FR-065, FR-066, FR-067, FR-068).

## Requirements *(mandatory)*

### Functional Requirements (slice-specific)

- **FR-076**: Changes to a shared item MUST propagate to other members' open shared views in real time (SignalR) within the fan-out budget.
- **FR-077**: An inbound real-time update MUST NOT overwrite an in-flight local optimistic edit; it reconciles under last-write-wins after the local mutation's server-ack.
- **FR-078**: On reconnect after a dropped connection, the client MUST re-sync the current state of visible shared views.

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

Authorization / Access Control (per Constitution Principle IX):
- **FR-065**: Every query MUST be scoped to data the caller owns or has membership access to (per-user isolation).
- **FR-066**: Access to a shared project's data MUST require membership in that project.
- **FR-067**: Each operation MUST require sufficient role (viewer=read, editor=write, owner=manage); insufficient role MUST be denied.
- **FR-068**: Authorization MUST be deny-by-default and enforced at the API/handler layer for every read and write.

### Key Entities

This slice introduces no new entity; it touches the following existing entities (canonical definitions owned by their originating slices):

- **ENT-01 — Task**: The core work item. Has a title (required), description, priority (P0-P3), status (backlog/todo/in_progress/done/cancelled), due date, labels, project reference, cycle reference, recurrence rule, createdBy (the User who created it), assignees (zero or more Users; only on shared-project tasks), and system timestamps (created_at, updated_at, completed_at). New tasks default to "backlog" status. A recurring task has a linked recurrence rule that generates successor instances.
- **ENT-02 — Project**: An organizational container for tasks. Has a name, color, icon, optional parent project reference, archived flag, ownerId (the owning User), and visibility (personal or shared). Supports one level of nesting. Contains zero or more tasks. Shared projects have a membership set.
- **ENT-07 — ProjectMembership**: Links a User to a shared Project with a role (owner/editor/viewer).

> Slice scope for entities: this slice adds no columns. It reads the existing shared-item state to fan out live updates over SignalR; the membership set (ENT-07) on a shared project (ENT-02) determines who receives an item's (ENT-01) updates and at what role.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-014**: A change to a shared item is reflected on other members' open shared views within ~1 second.
- **SC-015**: The system serves ~10 concurrent users performing typical operations without perceptible degradation.

## Constitution Compliance

This slice is evaluated against constitution v3.0.0. Principles realized here:

- **III. Instant Response**: this slice is the first to exercise the real-time reconciliation clause. FR-076 delivers server-initiated SignalR updates to members of shared views; FR-077 makes an inbound remote patch resolve under last-write-wins but yield to a pending local optimistic mutation until that mutation's server acknowledgement resolves — a remote update never clobbers an in-flight local edit. Animations and the live update path are non-blocking; the UI keeps accepting input while updates arrive. SC-014 (fan-out within ~1s) is the perceived-speed budget.
- **V. Connected, Server-Authoritative**: live updates flow over SignalR from the C# backend, which remains the single source of truth; the client holds no authoritative copy and reconciles to server state on each patch and on reconnect (FR-078). No third-party runtime data service is introduced — SignalR is the app's own real-time channel.
- **VII. Data Integrity & Resilience**: FR-049 (error + recovery, e.g. on a failed reconnect/re-sync), FR-050 (structured logging of connection and reconciliation events), FR-051 (auto-backup before migration — applies to any schema change carried by this slice).
- **IX. Authentication & Authorization**: FR-065, FR-066, FR-067, FR-068 — real-time fan-out is gated by the same deny-by-default authorization as every other read/write. A member receives updates only for shared projects they belong to and only for data their role permits (per-user isolation + membership + role), enforced at the API/handler layer.
- **II. Accessibility (WCAG 2.1 AA)**: FR-042 (focus indicator), FR-043 (ARIA roles/labels), FR-044 (contrast ≥ 4.5:1), FR-045 (no AT-binding collisions), FR-046 (no hover-only content), FR-047 (prefers-reduced-motion — live-update transitions respect reduced motion). FR-031 keeps single-key shortcuts from hijacking text entry.
- **Performance Standards**: the real-time fan-out budget (a change to a shared item propagates to other members' open shared views within ~1s, SC-014) and the concurrency target (~10 concurrent users without perceptible degradation, SC-015) are exercised by this slice.
- **VIII. Test-First**: each owned acceptance scenario above (US-15.AS-01..03) is independently testable (Red-Green-Refactor), including the in-flight-edit reconciliation and reconnect re-sync paths.

## Assumptions

- **ASM-01 — Multi-user team**: The application serves a small collaborating team (~10). Each user authenticates (Google) and has personal data plus access to shared projects.
- **ASM-02 — Web platform**: The MVP targets modern desktop browsers. Native mobile apps, PWA/offline operation, and cross-device sync are explicitly out of scope.
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

> Within-MVP boundary for this slice: in-app notifications (live toasts and the notification center) are delivered in slice 017 (notifications), which builds on this slice's SignalR channel. This slice fans out item changes to open shared views only — it adds no presence indicators or activity feed (OOS-15).

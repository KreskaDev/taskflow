# Feature Specification: Project Sharing, Membership & Roles

**Feature Branch**: `007-project-sharing-membership`

**Created**: 2026-06-13

**Status**: Draft

**Input**: Slice 007 of the TaskFlow MVP. Source of truth: `.specify/memory/product-vision.md`. Goal: turn a personal project into a shared, multi-member project — convert personal↔shared, invite members with owner/editor/viewer roles, change roles, remove members, and let members leave — and establish Tier-B authorization (membership + role checks) as the access foundation that assignment, comments, real-time, and notifications build on.

## Provenance

This slice realizes the following items from product-vision.md:

Slice-specific:
- US-12 (Project Sharing, Membership & Roles) — AS-01, AS-02, AS-03, AS-04, AS-05, AS-06
- FR-057 (project visibility personal/shared; default personal)
- FR-058 (convert personal→shared and revert shared→personal)
- FR-059 (on unshare, non-owner members lose access and assignments cleared)
- FR-060 (invite a user to a shared project and assign a role)
- FR-061 (three per-project roles: owner/editor/viewer)
- FR-062 (change a member's role; remove a member, unassigning them)
- FR-063 (non-owner member can leave; unassigned from tasks)
- FR-064 (membership/role changes are confirmation-gated, not under the 30s undo)
- ENT-07 (ProjectMembership)

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

Access control (Tier-B authorization — originates in this slice):
- FR-066 (access to shared-project data requires membership)
- FR-067 (each operation requires sufficient role: viewer=read, editor=write, owner=manage)
- FR-068 (authorization deny-by-default, enforced at the API/handler layer for every read and write)
- SC-013 (authorization enforced on 100% of data operations; deny cases covered by integration tests)

MVP boundary confirmed:
- OOS-01..OOS-17 (full MVP out-of-scope confirmation)

Entity touchpoints:
- ENT-07 (ProjectMembership) — owned and introduced here
- ENT-02 (Project) — `visibility` (personal/shared) and the membership set exercised here; entity owned by slice 004 (project-management)
- ENT-06 (User) — referenced as member/owner; entity owned by slice 001 (accounts-and-auth)
- ENT-01 (Task) — `assignees` cleared on unshare/remove/leave; entity owned by slice 002 (task-capture), assignment owned by slice 008 (task-assignment)

Depends on:
- 001 (accounts-and-auth) — identity, sessions, deny-by-default Tier-A isolation
- 004 (project-management) — Project entity, ownership, personal visibility default

## User Scenarios & Testing *(mandatory)*

### User Story 12 - Project Sharing, Membership & Roles (Priority: P1)

An owner shares a personal project, invites members with roles (owner/editor/viewer), changes roles, removes members, can unshare (revert to personal); members can leave.

**Why this priority**: Sharing with roles is the core of collaboration — it defines who can access a project and what they may do. Assignment, comments, real-time, and notifications all build on membership.

**Independent Test**: Can be tested by sharing a personal project, inviting a member at a given role, changing the role, removing the member, having a member leave, and unsharing, verifying access and assignments at each step.

**Acceptance Scenarios** (owned by this slice):

1. **(US-12.AS-01) Given** an owner of a personal project, **When** they share it and invite a member, **Then** the project becomes shared and the member gains access at the assigned role.
2. **(US-12.AS-02) Given** an owner, **When** they set a member's role to viewer/editor/owner, **Then** that member's permissions change accordingly.
3. **(US-12.AS-03) Given** a viewer, **When** they attempt to modify a task in the shared project, **Then** the action is denied (read-only).
4. **(US-12.AS-04) Given** an owner, **When** they remove a member (after confirmation), **Then** that member loses access and is unassigned from the project's tasks.
5. **(US-12.AS-05) Given** a non-owner member, **When** they leave a shared project, **Then** they lose access and are unassigned from its tasks.
6. **(US-12.AS-06) Given** an owner, **When** they unshare a shared project (after confirmation), **Then** all other members lose access, their assignments on its tasks are cleared, and the project becomes personal again.

### Edge Cases

- **Confirmation-gated, not undoable**: invite, role change, remove, leave, and unshare each require explicit confirmation before taking effect and are NOT covered by the 30-second data undo (FR-064). There is no undo toast for these actions.
- **Unshare cleanup**: reverting a shared project to personal removes all non-owner members and clears their assignments on that project's tasks (FR-059); the owner is retained and the project's tasks are preserved.
- **Read-only viewers**: a viewer may read the shared project and its tasks but may not modify tasks (US-12.AS-03) or comment; insufficient-role operations are denied at the API/handler layer (FR-067, FR-068).
- **Membership required**: a non-member's read or write against a shared project's data is denied (FR-066) under deny-by-default (FR-068), even when the caller is authenticated.

## Requirements *(mandatory)*

### Functional Requirements (slice-specific)

- **FR-057**: Projects MUST have a visibility of personal (private to owner) or shared; new projects default to personal.
- **FR-058**: An owner MUST be able to convert a personal project to shared and to revert a shared project to personal (unshare).
- **FR-059**: On unshare, all non-owner members MUST lose access and their assignments on that project's tasks MUST be cleared.
- **FR-060**: An owner MUST be able to invite a user to a shared project and assign a role.
- **FR-061**: System MUST support three per-shared-project roles: owner (manage members, share/unshare, delete), editor (change tasks, comment), viewer (read-only, no commenting).
- **FR-062**: An owner MUST be able to change a member's role and remove a member; removal MUST unassign that member from the project's tasks.
- **FR-063**: A non-owner member MUST be able to leave a shared project, losing access and being unassigned from its tasks.
- **FR-064**: Membership and role changes (invite, role change, remove, leave, unshare) MUST require explicit confirmation and MUST NOT be covered by the 30-second data undo (FR-040).

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

Access Control (Tier-B authorization — per Constitution Principle IX; originates in this slice):
- **FR-066**: Access to a shared project's data MUST require membership in that project.
- **FR-067**: Each operation MUST require sufficient role (viewer=read, editor=write, owner=manage); insufficient role MUST be denied.
- **FR-068**: Authorization MUST be deny-by-default and enforced at the API/handler layer for every read and write.

### Key Entities

- **ENT-07 — ProjectMembership**: Links a User to a shared Project with a role (owner/editor/viewer).

> Slice scope for ENT-07: this slice introduces the ProjectMembership link (User ↔ shared Project, with role owner/editor/viewer) as the basis of Tier-B authorization. The membership set is the source the application-layer policy consumes for membership and role checks (FR-066, FR-067). Assignment clearing on unshare/remove/leave touches the Task entity's `assignees` (ENT-01); the assignment feature itself is owned by slice 008 (task-assignment).

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-013**: Authorization is enforced on 100% of data operations (no read/write bypasses the policy; deny cases covered by integration tests).

## Constitution Compliance

This slice is evaluated against constitution v3.0.0. Principles realized here:

- **I. Keyboard-First**: sharing, invite, role change, remove, leave, and unshare are all reachable and operable via keyboard; confirmation dialogs (FR-064) are keyboard-operable and dismissable.
- **II. Accessibility (WCAG 2.1 AA)**: FR-042 (focus indicator), FR-043 (ARIA roles/labels), FR-044 (contrast ≥ 4.5:1), FR-045 (no AT-binding collisions), FR-046 (no hover-only content), FR-047 (prefers-reduced-motion). FR-031 keeps single-key shortcuts from hijacking text entry in the invite/member inputs.
- **V. Connected, Server-Authoritative**: membership, roles, and visibility are persisted server-side and are the system of record for who may access a shared project; the client holds no authoritative copy.
- **VII. Data Integrity & Resilience**: FR-049 (error + recovery), FR-050 (structured logging), FR-051 (auto-backup before migration — the ProjectMembership schema addition is a migration). Per Principle VII, membership and role changes are NOT under the 30-second data undo; they are confirmation-gated instead (FR-064).
- **IX. Authentication & Authorization**: this slice is the origin of Tier-B authorization. FR-066 (membership required for shared-project data), FR-067 (sufficient-role-per-operation: viewer=read, editor=write, owner=manage), FR-068 (deny-by-default, enforced at the API/handler layer for every read and write). SC-013 measures enforcement on 100% of data operations with deny cases covered by integration tests.

## Assumptions

- **ASM-01 — Multi-user team**: The application serves a small collaborating team (~10). Each user authenticates (Google) and has personal data plus access to shared projects.
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

Additionally, the following are owned by other slices and are out of scope here: task assignment in shared projects (008), comments & @mentions (009), real-time collaboration (016), and notifications (017). This slice provides the membership and role foundation those slices depend on.

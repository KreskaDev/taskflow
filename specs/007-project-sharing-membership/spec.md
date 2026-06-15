# Feature Specification: Project Sharing, Membership & Roles

**Feature Branch**: `007-project-sharing-membership`

**Created**: 2026-06-13

**Status**: Draft

**Input**: Slice 007 of the TaskFlow MVP. Source of truth: `.specify/memory/product-vision.md`. Goal: turn a personal project into a shared, multi-member project — convert personal↔shared (reversibly), invite members (by email, resolved against existing signed-in Users) with editor/viewer roles, change roles, transfer ownership, remove members, and let members leave — and establish the shared-project authorization branch (deny-by-default, dispatched by the containing project's visibility: current `ProjectMembership` + role) as the access foundation that assignment, comments, real-time, and notifications build on.

## Provenance

This slice realizes the following items from product-vision.md:

Slice-specific:
- US-12 (Project Sharing, Membership & Roles) — AS-01, AS-02, AS-03, AS-04, AS-05, AS-06
- FR-057 (project visibility personal/shared; default personal)
- FR-058 (convert personal→shared and revert shared→personal — reversible)
- FR-059 (on unshare, non-owner members lose access and assignments cleared)
- FR-060 (invite a user to a shared project and assign a role)
- FR-061 (per-shared-project roles; editor/viewer freely assignable; owner is the immutable `ownerId`; last-owner guard)
- FR-062 (change a member's assignable role; remove a member, unassigning them; ownership moves only via transfer command)
- FR-063 (non-owner member can leave; unassigned from tasks)
- FR-064 (membership/role changes are confirmation-gated, not under the 30s undo)
- FR-094 (ownership transfer command + last-owner guard) — owned and introduced here
- ENT-07 (ProjectMembership) — owned and introduced here

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
- FR-101 (ARIA-live for server-initiated updates/toasts + dialog focus contract)

Access control (shared-project authorization branch — per Constitution Principle IX; originates in this slice):
- FR-065 (authorization dispatched by the containing resource's visibility; shared-project entities authorize on current `ProjectMembership` + role; per-user query scoping)
- FR-066 (access to shared-project data requires current membership; `createdBy`/assignee are provenance only; leave/remove/unshare revokes ALL access)
- FR-067 (each operation requires sufficient role: viewer=read, editor=write, owner=manage)
- FR-068 (authorization deny-by-default, enforced at the API/handler layer for every read and write)
- SC-013 (authorization enforced on 100% of data operations; deny cases covered by integration tests)
- SC-016 (authorization coverage mechanically verifiable: every handler ships allow+deny tests; role×operation deny matrix)

MVP boundary confirmed:
- OOS-01..OOS-19 (full MVP out-of-scope confirmation)

Entity touchpoints:
- ENT-07 (ProjectMembership) — owned and introduced here
- ENT-02 (Project) — `ownerId`, `visibility` (personal/shared), and the membership set exercised here; entity owned by slice 004 (project-management)
- ENT-06 (User) — referenced as member/owner; invitations resolved against existing Users; entity owned by slice 001 (accounts-and-auth)
- ENT-01 (Task) — `assignees` cleared on unshare/remove/leave; entity owned by slice 002 (task-capture), assignment owned by slice 008 (task-assignment)

Depends on:
- 001 (accounts-and-auth) — identity, sessions (Postgres-backed, rotated at OAuth completion, server-side sign-out invalidation), admission control, deny-by-default ownership-scoped isolation
- 004 (project-management) — Project entity, `ownerId`, personal visibility default

## User Scenarios & Testing *(mandatory)*

### User Story 12 - Project Sharing, Membership & Roles (Priority: P1)

An owner shares a personal project, invites members with roles (owner/editor/viewer), changes roles, removes members, can unshare (revert to personal); members can leave.

**Why this priority**: Sharing with roles is the core of collaboration — it defines who can access a project and what they may do. Assignment, comments, real-time, and notifications all build on membership.

**Independent Test**: Can be tested by sharing a personal project, inviting a member at a given role, changing the role, transferring ownership, removing the member, having a member leave, and unsharing, verifying access and assignments at each step — and confirming reversibility back to personal.

**Acceptance Scenarios** (owned by this slice):

1. **(US-12.AS-01) Given** an owner of a personal project, **When** they share it and invite a member, **Then** the project becomes shared and the member gains access at the assigned role.
2. **(US-12.AS-02) Given** an owner, **When** they change a member's assignable role between editor and viewer (owner is reached only via the explicit FR-094 ownership-transfer command, not by promoting a member into an "owner" role), **Then** that member's permissions change accordingly.
3. **(US-12.AS-03) Given** a viewer, **When** they attempt to modify a task in the shared project, **Then** the action is denied (read-only).
4. **(US-12.AS-04) Given** an owner, **When** they remove a member (after confirmation), **Then** that member loses access and is unassigned from the project's tasks.
5. **(US-12.AS-05) Given** a non-owner member, **When** they leave a shared project, **Then** they lose access and are unassigned from its tasks.
6. **(US-12.AS-06) Given** an owner, **When** they unshare a shared project (after confirmation), **Then** all other members lose access, their assignments on its tasks are cleared, and the project becomes personal again.

> Note on AS-02 vocabulary: changing a member's **assignable** role moves between editor and viewer (FR-061). "Owner" is reached only via the explicit ownership **transfer** command (FR-094), not by promoting a member into a freely assignable "owner" role; the transfer reassigns the immutable `ownerId` and demotes the prior owner to editor.

### Edge Cases

- **Confirmation-gated with blast radius, not undoable**: invite, role change, ownership transfer, remove, leave, and unshare each require explicit confirmation before taking effect and are NOT covered by the 30-second data undo (FR-064). The confirmation dialog MUST show its **blast radius** (which members lose access, which assignments are cleared). There is no undo toast for these actions.
- **Reversibility (personal↔shared)**: an owner may share a personal project (FR-058) and later unshare it back to personal; the round-trip is supported. On unshare the project's tasks are preserved and only membership state and non-owner assignments are removed.
- **Unshare / remove cleanup (blast radius)**: reverting a shared project to personal removes all non-owner members and clears their assignments on that project's tasks (FR-059); removing a single member clears that member's assignments. The owner (`ownerId`) is retained and the project's tasks are preserved.
- **Owner is immutable, roles are editor/viewer**: the owner is the project's `ownerId`. Only editor and viewer are freely assignable roles. Ownership changes only through the transfer command (FR-094).
- **Last-owner guard**: the last owner MUST NOT be removable, demotable, or able to leave. Such an attempt is rejected with a clear, recoverable message (FR-049); ownership must be transferred to another member first.
- **Invite by email resolves against existing Users only**: an invitation is addressed by email and resolved against an existing signed-in User. An unknown email is rejected with a clear, recoverable FR-049 message (e.g., "ask them to sign in once first"). Pending / pre-account invitations are out of scope (OOS-18).
- **Read-only viewers**: a viewer may read the shared project and its tasks but may not modify tasks (US-12.AS-03) or comment; insufficient-role operations are denied at the API/handler layer (FR-067, FR-068).
- **Membership required (provenance is not access)**: a non-member's read or write against a shared project's data is denied (FR-066) under deny-by-default (FR-068), even when the caller is authenticated and even if they created or were assigned the entity — `createdBy`/assignee are provenance only and confer no standalone access. On leave/remove/unshare a user loses ALL access to that project's data.
- **Live subscriptions follow membership (forward reference)**: a membership/role change that revokes access means the affected user receives no further live patches and is forced to a no-access re-sync; the real-time enforcement mechanism (SignalR subscription eviction, FR-095) is realized by slice 016. This slice defines membership as the authority that change consumes.

## Requirements *(mandatory)*

### Functional Requirements (slice-specific)

- **FR-057**: Projects MUST have a visibility of personal (private to owner) or shared; new projects default to personal.
- **FR-058**: An owner MUST be able to convert a personal project to shared and to revert a shared project to personal (unshare).
- **FR-059**: On unshare, all non-owner members MUST lose access and their assignments on that project's tasks MUST be cleared.
- **FR-060**: An owner MUST be able to invite a user to a shared project and assign a role.
- **FR-061**: System MUST support per-shared-project roles with the owner capability set (manage members, share/unshare, delete), editor (change tasks, comment), and viewer (read-only, no commenting). Only editor and viewer are freely assignable roles; owner is the immutable `ownerId` and is NOT a freely assignable role (see FR-094). The last owner MUST NOT be removable, demotable, or able to leave (recoverable FR-049 error).
- **FR-062**: An owner MUST be able to change a member's assignable role (editor/viewer) and remove a member; removal MUST unassign that member from the project's tasks. Ownership moves only via an explicit transfer-owner command (FR-094), never by promoting a member to an "owner" role.
- **FR-063**: A non-owner member MUST be able to leave a shared project, losing access and being unassigned from its tasks.
- **FR-064**: Membership and role changes (invite, role change, remove, leave, unshare) MUST require explicit confirmation and MUST NOT be covered by the 30-second data undo (FR-040).
- **FR-094**: System MUST provide an explicit ownership transfer command, guarded so the last owner cannot be removed, demoted, or leave (recoverable FR-049 error).

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

Error Handling & Data Integrity (per Constitution Principle VII):
- **FR-049**: All errors MUST be presented to the user with a clear message and an actionable recovery suggestion. No operation may fail silently.
- **FR-050**: Errors MUST be logged with structured context (severity level, operation context, and error details) for debugging purposes.
- **FR-051**: Before any data migration, the system MUST automatically create a backup of user data. The user MUST be able to restore from this backup.

Access Control (shared-project authorization branch — per Constitution Principle IX; originates in this slice):
- **FR-065**: Authorization MUST be dispatched by the containing resource's visibility (not a conjunction of tiers): personal/unprojected data authorizes on ownership (`createdBy`/`ownerId`) with queries scoped to the caller; shared-project entities authorize on current `ProjectMembership` + role. Every query MUST be scoped accordingly (per-user isolation).
- **FR-066**: Access to a shared project's data MUST require current membership in that project. `createdBy` and assignee are provenance only and confer NO standalone access; on leave/remove/unshare a user MUST lose ALL access to that project's data regardless of authorship or assignment.
- **FR-067**: Each operation on a shared-project resource MUST require sufficient role (viewer=read, editor=write, owner=manage); insufficient role MUST be denied.
- **FR-068**: Authorization MUST be deny-by-default and enforced at the API/handler layer for every read and write.

### Key Entities

- **ENT-07 — ProjectMembership**: Links a User to a shared Project with a role (owner/editor/viewer).
- **ENT-02 — Project** (referenced; owned by slice 004): An organizational container for tasks. Has a name, color, icon, optional parent project reference, archived flag, `ownerId` (the owning User), and visibility (personal or shared). Shared projects have a membership set. This slice exercises `ownerId`, `visibility`, and the membership set.

> Slice scope for ENT-07: this slice introduces the ProjectMembership link (User ↔ shared Project, with role owner/editor/viewer) as the basis of the shared-project authorization branch. The owner role corresponds to the project's immutable `ownerId` (ENT-02); editor and viewer are the freely assignable roles, and ownership moves only via the transfer command (FR-094). The membership set is the source the application-layer policy consumes for membership and role checks (FR-065, FR-066, FR-067). Assignment clearing on unshare/remove/leave touches the Task entity's `assignees` (ENT-01); the assignment feature itself is owned by slice 008 (task-assignment).

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-013**: Authorization is enforced on 100% of data operations (no read/write bypasses the policy; deny cases covered by integration tests).
- **SC-016**: Authorization coverage is mechanically verifiable: every data handler ships with both an allow and a deny test, and a role×operation deny matrix demonstrates that insufficient ownership/membership/role is rejected.

## Constitution Compliance

This slice is evaluated against constitution v4.0.0. Principles realized here:

- **I. Keyboard-First**: sharing, invite, role change, ownership transfer, remove, leave, and unshare are all reachable and operable via keyboard; confirmation dialogs (FR-064) are keyboard-operable and dismissable.
- **II. Accessibility (WCAG 2.1 AA)**: FR-042 (focus indicator), FR-043 (ARIA roles/labels), FR-044 (contrast ≥ 4.5:1), FR-045 (no AT-binding collisions), FR-046 (no hover-only content), FR-047 (prefers-reduced-motion). FR-031 keeps single-key shortcuts from hijacking text entry in the invite/member inputs. FR-101 governs this slice's server-initiated surfaces: any membership/role/access-change toast or status message MUST be announced via an ARIA live region without stealing focus, and the confirmation dialogs (share/unshare/remove/leave/transfer) MUST follow the dialog focus contract (set initial focus, trap focus, dismiss on Esc, return focus to the invoker on close).
- **V. Connected, Server-Authoritative**: membership, roles, the immutable `ownerId`, and visibility are persisted server-side and are the system of record for who may access a shared project; the client holds no authoritative copy.
- **VII. Data Integrity & Resilience**: FR-049 (error + recovery), FR-050 (structured logging), FR-051 (auto-backup before migration — the ProjectMembership schema addition is a migration). Per Principle VII, membership and role changes are NOT under the 30-second data undo; they are confirmation-gated instead (FR-064). This slice's confirmation dialogs MUST additionally state their **blast radius** — which members lose access and which assignments are cleared on unshare/remove.
- **IX. Authentication & Authorization**: this slice is the origin of the shared-project authorization branch. Authorization is **deny-by-default and dispatched by the containing project's visibility** — NOT a conjunction of tiers: shared-project entities authorize on current `ProjectMembership` + role (FR-065), and `createdBy`/assignee are provenance only, conferring no standalone access (FR-066). FR-066 requires current membership for shared-project data and revokes ALL access on leave/remove/unshare; FR-067 requires sufficient role per operation (viewer=read, editor=write, owner=manage); FR-068 enforces deny-by-default at the API/handler layer for every read and write. Owner is the immutable `ownerId`, moved only by the explicit transfer command with a last-owner guard (FR-094); editor/viewer are the only freely assignable roles (FR-061). Membership changes are the authority that live-subscription authorization (FR-095, slice 016) consumes to evict a removed member's real-time subscriptions. SC-013 and SC-016 measure enforcement on 100% of data operations with allow+deny tests and a role×operation deny matrix.
- **X. Time & Timezone**: membership and role records carry UTC (`timestamptz`) timestamps for created/changed events; any date-relative display follows the single instance reference timezone, `Europe/Warsaw` (FR-092, ASM-12). This slice introduces no new date-relative computation.
- **XI. Privacy & Personal Data**: removing a member, a member leaving, and unshare apply the residual-attribution rule — the departing user loses ALL access and their assignments are cleared; `createdBy` provenance on tasks they authored remains but confers no access. Invitations are addressed by email but resolved only against existing Users (no new personal data is created for non-members; pending/pre-account invites are OOS-18).
- **XII. Security by Default**: the invite-by-email input is untrusted user input and MUST be validated/sanitized at the trust boundary; resolution is server-side against existing Users and MUST NOT disclose whether a non-resolving email exists beyond the generic FR-049 recovery message. Authorization decisions never leak data across the membership boundary.

## Assumptions

- **ASM-01 — Multi-user team**: The application serves a small collaborating team (~10). Each user authenticates (Google) and has personal data plus access to shared projects.
- **ASM-10 — Small team scale**: Small team (~10 users) on a single shared instance; not organizational multi-tenancy.
- **ASM-12 — Instance reference timezone**: The instance operates against a single reference timezone, `Europe/Warsaw`, for all date-relative computation; per-user timezones are out of scope.
- **ASM-13 — Gated admission**: Account creation is gated (email allowlist or Google Workspace hosted-domain); the instance is not open to any Google account or to public sign-up. Members are therefore drawn from the admitted, signed-in user set.

## Out of Scope

This slice confirms the full MVP out-of-scope boundary (OOS-01..OOS-19 from product-vision.md):

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

Additionally, the following are owned by other slices and are out of scope here: task assignment in shared projects (008), comments & @mentions (009), real-time collaboration including live-subscription eviction (016, FR-095), and notifications (017). This slice provides the membership and role foundation those slices depend on.

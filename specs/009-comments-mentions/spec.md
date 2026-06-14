# Feature Specification: Comments & @Mentions

**Feature Branch**: `009-comments-mentions`

**Created**: 2026-06-13

**Status**: Draft

**Input**: Slice 009 of the TaskFlow MVP. Source of truth: `.specify/memory/product-vision.md`. Goal: a comment thread on tasks in shared projects, where editors and owners can post comments and @mention project members, and authors can edit and delete their own comments. Viewers can read the thread but cannot comment (read-only). An @mention notifies the mentioned member; mechanically it emits a `UserMentioned` domain event consumed by slice 017 (notifications).

## Provenance

This slice realizes the following items from product-vision.md:

Slice-specific (owned IDs):
- US-14 (Comments & @Mentions) — full: AS-01, AS-02, AS-03, AS-04
- FR-072 (editors/owners post comments on shared tasks; viewers MUST NOT comment)
- FR-073 (comment records author + creation timestamp; supports @mentions of project members)
- FR-074 (@mention notifies the mentioned member)
- FR-075 (author may edit and delete their own comments)
- ENT-08 (Comment)

Cross-cutting (realized in this slice):
- UI accessibility — FR-031, FR-042, FR-043, FR-044, FR-045, FR-046, FR-047
- Resilience — FR-049, FR-050, FR-051
- Access-control sub-block — FR-061 (role definitions; viewer = read-only, no commenting; editor = comment), FR-065 (per-user isolation scoping), FR-066 (shared-project access requires membership), FR-067 (sufficient-role-per-operation), FR-068 (deny-by-default authorization at the API/handler layer)

MVP boundary confirmed:
- OOS-01..OOS-17 (full MVP out-of-scope confirmation)

Entity touchpoints:
- ENT-08 (Comment) — owned and introduced by this slice
- ENT-01 (Task) — referenced (parent task of a comment); not modified here
- ENT-06 (User) — referenced (comment author, @mentioned members); not modified here
- ENT-07 (ProjectMembership) — referenced (membership + role gate for commenting and for @mention candidacy); not modified here
- ENT-09 (Notification) — touched indirectly: the @mention emits a `UserMentioned` domain event consumed by slice 017 (notifications); the notification entity and its delivery are owned by slice 017, not here

Depends on:
- 007 (project-sharing-membership) — shared projects, membership, and roles (owner/editor/viewer) must exist before comments and @mentions can be authorized

## User Scenarios & Testing *(mandatory)*

### User Story 14 - Comments & @Mentions (Priority: P2)

Editors/owners comment on shared tasks and @mention members; viewers can read but not comment.

**Why this priority**: Comments and @mentions are how members discuss work in context. They enrich collaboration but depend on shared projects and roles already existing.

**Independent Test**: Can be tested by posting a comment on a shared task as an editor, @mentioning a member and verifying notification, confirming a viewer has no comment input, and editing/deleting one's own comment.

> Scope note: this slice realizes the comment thread, @mentions, and author edit/delete on tasks in shared projects. The @mention emits a `UserMentioned` domain event; the in-app notification it triggers (notification center, live toast, mark-read, per-type preference) is delivered by slice 017 (notifications). Sharing, membership, and roles are provided by slice 007 (project-sharing-membership).

**Acceptance Scenarios** (owned by this slice):

1. **(US-14.AS-01) Given** an editor/owner on a shared task, **When** they post a comment, **Then** it appears in the task's thread with author and timestamp.
2. **(US-14.AS-02) Given** a comment being composed, **When** the author @mentions a project member, **Then** that member is notified.
3. **(US-14.AS-03) Given** a viewer on a shared task, **When** they view it, **Then** they can read comments but have no comment input (cannot post).
4. **(US-14.AS-04) Given** a comment's author, **When** they edit or delete their own comment, **Then** the thread updates.

### Edge Cases

- A viewer (read-only role) MUST be able to read the comment thread but MUST NOT be presented with a comment input and MUST NOT be able to post, edit, or delete comments (FR-061, FR-067, FR-072).
- Only the author of a comment may edit or delete that comment; another member — regardless of role — MUST NOT edit or delete a comment they did not author (FR-068, FR-075).
- @mention candidates are limited to members of the comment's parent project; a user who is not a member of that shared project MUST NOT be mentionable and access to the thread requires membership (FR-066, FR-073).
- Personal (non-shared) projects have no membership set; commenting applies to tasks in shared projects only (FR-072).

## Requirements *(mandatory)*

### Functional Requirements (slice-specific)

- **FR-072**: Editors and owners MUST be able to post comments on tasks in shared projects; viewers MUST NOT be able to comment.
- **FR-073**: A comment MUST record author and creation timestamp and support @mentions of project members.
- **FR-074**: An @mention MUST notify the mentioned member.
- **FR-075**: A comment's author MUST be able to edit and delete their own comments.

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

Access Control (per Constitution Principle IX):
- **FR-061**: System MUST support three per-shared-project roles: owner (manage members, share/unshare, delete), editor (change tasks, comment), viewer (read-only, no commenting).
- **FR-065**: Every query MUST be scoped to data the caller owns or has membership access to (per-user isolation).
- **FR-066**: Access to a shared project's data MUST require membership in that project.
- **FR-067**: Each operation MUST require sufficient role (viewer=read, editor=write, owner=manage); insufficient role MUST be denied.
- **FR-068**: Authorization MUST be deny-by-default and enforced at the API/handler layer for every read and write.

### Key Entities

- **ENT-08 — Comment**: Has an author User, parent task, body, @mentions, and created/edited timestamps.

> Slice scope for ENT-08: this slice introduces the Comment entity in full — author User reference, parent Task reference, body, the set of @mentioned project members, and created/edited timestamps. The @mention set drives the `UserMentioned` domain event consumed by slice 017 (notifications); the Notification entity (ENT-09) itself is owned by slice 017, not created here.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-013**: Authorization is enforced on 100% of data operations (no read/write bypasses the policy; deny cases covered by integration tests).

## Constitution Compliance

This slice is evaluated against constitution v3.0.0. Cross-cutting principles realized here:

- **I. Keyboard-First**: composing, posting, @mentioning, editing, and deleting a comment are all reachable by keyboard; FR-031 keeps single-key shortcuts from hijacking the comment composer while it is focused.
- **II. Accessibility (WCAG 2.1 AA)**: FR-042 (focus indicator), FR-043 (ARIA roles/labels), FR-044 (contrast ≥ 4.5:1), FR-045 (no AT-binding collisions), FR-046 (no hover-only content — the @mention picker and author edit/delete affordances have keyboard/focus-triggered equivalents), FR-047 (prefers-reduced-motion). FR-031 keeps single-key shortcuts from hijacking text entry in the composer.
- **III. Instant Response**: a posted, edited, or deleted comment paints its optimistic result within one animation frame, with the server reconciling or rolling back asynchronously; the thread is a shared view, so an inbound remote comment reconciles under last-write-wins and MUST NOT clobber a pending local optimistic edit (the live propagation itself is owned by slice 016, real-time-collaboration).
- **V. Connected, Server-Authoritative**: comments are persisted server-side in PostgreSQL through the application's own API, which is the system of record; the client holds no authoritative copy, and the app depends on no third-party runtime data service.
- **VI. Type Safety End-to-End**: the EF Core / PostgreSQL schema for the Comment entity is the source of truth, with C# entity types on the server and TypeScript types generated from the OpenAPI contract on the Next.js client, and runtime validation at the comment-composer input and API request/response boundaries.
- **VII. Data Integrity & Resilience**: FR-049 (error + recovery), FR-050 (structured logging), FR-051 (auto-backup before migration — the Comment table is introduced by a migration in this slice, so the backup/restore hook applies).
- **VIII. Test-First**: each owned acceptance scenario above is independently testable (Red-Green-Refactor); per Principle VIII, integration tests cover authorization — a viewer attempting to post, or a non-author attempting to edit/delete, MUST be denied.
- **IX. Authentication & Authorization**: FR-061 (role definitions — viewer is read-only with no commenting, editor and owner may comment), FR-065 (per-user isolation scoping), FR-066 (access to the shared project's thread requires membership), FR-067 (sufficient role per operation — commenting requires editor or owner), FR-068 (deny-by-default, enforced at the API/handler layer for every read and write). Authorization on commenting and on author-only edit/delete is a first-class, tested concern (SC-013).

## Assumptions

- **ASM-01 — Multi-user team**: The application serves a small collaborating team (~10). Each user authenticates (Google) and has personal data plus access to shared projects.
- **ASM-06 — In-app notifications only**: The app provides in-app notifications (assignment, mention, changes); email and push/device notifications and reminders are out of scope.
- **ASM-10 — Small team scale**: Small team (~10 users) on a single shared instance; not organizational multi-tenancy.
- **ASM-11 — Google identity provider**: Google is the sole identity provider for the MVP.

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

Additionally, the in-app notification the @mention triggers (notification center, live toast, mark-read, per-type preferences) is delivered by slice 017 (notifications); this slice only emits the `UserMentioned` domain event. Live propagation of new comments to other members' open threads is delivered by slice 016 (real-time-collaboration).

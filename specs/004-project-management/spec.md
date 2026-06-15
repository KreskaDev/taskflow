# Feature Specification: Project Management

**Feature Branch**: `004-project-management`

**Created**: 2026-06-13

**Status**: Draft

**Input**: Slice 004 of the TaskFlow MVP. Source of truth: `.specify/memory/product-vision.md`. Goal: organize tasks into projects with one level of nesting (parent/child), color and icon from a preset, archive (hidden from default views but searchable), and the move-to-project action (`M`). This slice also defines the Inbox as tasks not assigned to any project, refining slice 002's flat "all tasks" list.

## Provenance

This slice realizes the following items from product-vision.md:

Slice-specific:
- US-10 (Project Management) — full: AS-01, AS-02, AS-03, AS-04, AS-05, AS-06
- US-08 (Keyboard Navigation & Shortcuts) — subset: AS-05 (`M` move-to-project)
- FR-011 (create projects with name, color preset, icon preset)
- FR-012 (one level of nesting; prevent grandchildren)
- FR-013 (archive projects; hidden from default views, accessible via search)
- FR-014 (delete-with-tasks prompt: cascade / move to Inbox / archive with tasks)
- FR-021 (Inbox view: tasks not assigned to any project, newest first)
- FR-057 (personal half: personal visibility value + default-personal; the shared visibility value is realized in slice 007)
- EC-03 (deleting a project with tasks)
- ENT-02 (Project)
- ASM-04 (preset colors and icons)

Slice-derived lifecycle coverage (no new FR **ID** is minted — product-vision is the sole ID allocator and allocates no standalone edit/unarchive/re-parent FR number; these clauses extend the existing owned FRs and are covered by new US-10 acceptance scenarios AS-07–AS-11 anchored to the FRs below):
- Project edit (name/color/icon/parent) — anchored to FR-011 (the editable fields) and FR-012 (the parent/nesting rule)
- Re-parenting within the 1-level rule — anchored to FR-012
- Deleting/archiving a parent that has child projects (prompt for children: cascade vs orphan-to-top) — anchored to FR-013 (archive) and FR-014 (delete-with-contents prompt)
- Unarchive — anchored to FR-013 (archive lifecycle)

Cross-cutting (realized in this slice):
- FR-031 (suppress single-key shortcuts in text inputs)
- FR-042 (visible focus indicator)
- FR-043 (ARIA roles/labels)
- FR-044 (text contrast ≥ 4.5:1)
- FR-045 (no collision with assistive-technology bindings)
- FR-046 (no hover-only content)
- FR-047 (prefers-reduced-motion)
- FR-101 (ARIA-live for server-initiated updates/toasts + dialog focus contract)
- FR-049 (error message + recovery action)
- FR-050 (structured error logging)
- FR-051 (auto-backup before migration — infrastructure in place)

Access control (realized in this slice, per Constitution Principle IX):
- FR-065 (authorization dispatched by visibility — this slice exercises the personal/unprojected → ownership branch)
- FR-066, FR-067 (membership + role branch — referenced; governs shared projects realized in slice 007)
- FR-068 (deny-by-default, enforced at the API/handler layer for every read and write)

MVP boundary confirmed:
- OOS-01..OOS-19 (full MVP out-of-scope confirmation; OOS-01 PROMOTED, OOS-06 PARTIAL)

Entity touchpoint(s):
- ENT-01 (Task) — owned by slice 002; this slice populates the `project reference` attribute (reserved there as a nullable, forward-compatible column) via FR-021 (Inbox) and US-08.AS-05 (move-to-project)

Depends on:
- Slice 002 (task-capture) — provides the Task entity, the single task list (here redefined as the Inbox), and server-side persistence (PostgreSQL via the C# API)

Exercised-but-not-owned (scenario owned here; its umbrella requirement lives in a later slice):
- `M` move-to-project mechanic — the canonical acceptance scenario US-08.AS-05 is owned by this slice, but `M` is a member of the full list-shortcut requirement FR-029, which is owned by slice 011 (cycles). FR-029 is therefore not included in this slice's Requirements list.

## User Scenarios & Testing *(mandatory)*

### User Story 10 - Project Management (Priority: P2)

User creates, edits, archives, and organizes projects with one level of nesting (parent/child). Each project has a color and icon from a preset. Archived projects disappear from default views but remain searchable.

**Why this priority**: Projects are the organizational backbone for grouping tasks beyond the Inbox.

**Independent Test**: Can be tested by creating parent and child projects, editing a project's name/color/icon/parent, re-parenting within the one-level rule, deleting/archiving a parent and choosing how its children are handled, assigning tasks, archiving a project and verifying it disappears from the sidebar but remains in search, then unarchiving it.

**Acceptance Scenarios** (owned by this slice):

1. **(US-10.AS-01) Given** the user wants to create a project, **When** they use the command palette or dedicated action, **Then** a project creation form appears with fields for name, color (preset), icon (preset), and optional parent project.
2. **(US-10.AS-02) Given** a parent project exists, **When** user creates a child project under it, **Then** the child appears nested under the parent in the sidebar (max 1 level).
3. **(US-10.AS-03) Given** a user tries to create a grandchild project (child of a child), **When** they attempt to set a child project as parent, **Then** the system prevents it and shows a message that only one level of nesting is allowed.
4. **(US-10.AS-04) Given** a project has tasks assigned to it, **When** user chooses to delete the project, **Then** a dialog asks: delete tasks cascading, move tasks to Inbox, or archive the project with its tasks.
5. **(US-10.AS-05) Given** a project is archived, **When** user views the sidebar and default project list, **Then** the archived project is not visible.
6. **(US-10.AS-06) Given** a project is archived, **When** user searches for it in the command palette, **Then** it appears in results and can be unarchived.

**Additional acceptance scenarios** (slice-derived, owned by this slice; cover the project-edit, re-parent, parent-lifecycle, and unarchive behaviors resolved for slice 004 — anchored to FR-011/FR-012/FR-013/FR-014, no new product-vision FR minted):

7. **(US-10.AS-07) Given** an existing project, **When** the user edits its name, color, icon, or parent in the project editor and saves, **Then** the changes are persisted and reflected in the sidebar and project view (the editable fields are those of FR-011 plus the parent reference of FR-012).
8. **(US-10.AS-08) Given** a top-level project and a candidate parent project that is itself top-level (not already a child), **When** the user re-parents the project under that candidate, **Then** the project becomes a child of the candidate, remaining within the one-level nesting rule (FR-012).
9. **(US-10.AS-09) Given** the user attempts to re-parent a project under a target that would create a grandchild (the target is already a child, or the project being moved already has children of its own), **When** they confirm, **Then** the system prevents it and shows the one-level-nesting message (FR-012), and the re-parent is rejected with a clear, recoverable error (FR-049).
10. **(US-10.AS-10) Given** a parent project that has one or more child projects, **When** the user deletes or archives that parent, **Then** a dialog prompts how to handle the children — cascade (delete/archive the children with the parent) or orphan-to-top (promote the children to top-level) — and the chosen disposition is applied (this is distinct from the tasks prompt of FR-014/EC-03; the dialog states its blast radius per Principle VII).
11. **(US-10.AS-11) Given** an archived project, **When** the user unarchives it, **Then** the project is restored to the default views and the sidebar (FR-013); a child whose parent is still archived is restored as a top-level project.

---

### User Story 8 - Keyboard Navigation & Shortcuts (Priority: P1)

User operates on the selected task using keyboard shortcuts only. The shortcut realized in this slice is `M` (move the selected task to a different project), which the project organization introduced here makes meaningful.

**Why this priority**: Keyboard-first is the core principle. Without complete keyboard coverage, the app fails its primary promise.

**Independent Test**: Can be tested by selecting a task, pressing `M`, choosing a target project from the selector, and verifying the task moves to that project (and leaves the Inbox when assigned).

**Acceptance Scenarios** (owned by this slice):

1. **(US-08.AS-05) Given** a task is selected, **When** user presses `M`, **Then** a project selector appears for moving the task to a different project.

> Shortcut coverage: `M` is implemented and its canonical scenario (US-08.AS-05) is owned here, but `M` is a member of the full list-shortcut requirement FR-029, which is owned by slice 011 (cycles). See Provenance.

### Edge Cases

- **EC-03 — Deleting a project with tasks**: User is prompted with three options: cascade delete, move to Inbox, or archive with tasks.
- **Deleting or archiving a parent project with child projects** (slice-derived, parallel to EC-03 but for the project hierarchy rather than tasks): the user is prompted how to handle the children — cascade (delete/archive children with the parent) or orphan-to-top (promote children to top-level). The confirmation dialog states its blast radius (which children are affected) per Principle VII (US-10.AS-10).
- **Re-parenting that would break the one-level rule** (slice-derived): re-parenting a project under a target that is already a child, or moving a project that itself has children under another parent, would create a grandchild; the system rejects it with the one-level-nesting message and a recoverable error (US-10.AS-09, FR-012/FR-049).
- **Unarchiving a child whose parent is still archived** (slice-derived): the child is restored as a top-level project rather than re-nesting under a still-archived parent (US-10.AS-11).

## Requirements *(mandatory)*

### Functional Requirements (slice-specific)

- **FR-011**: System MUST allow creating projects with a name, color (from preset), and icon (from preset).
- **FR-012**: System MUST support one level of project nesting (parent-child) and MUST prevent creation of grandchild projects.
- **FR-013**: System MUST allow archiving projects, which hides them from default views while keeping them accessible via search.
- **FR-014**: When deleting a project that contains tasks, the system MUST prompt the user with three options: cascade delete, move tasks to Inbox, or archive the project with its tasks.
- **FR-021**: The Inbox view MUST show tasks not assigned to any project, sorted by newest first.
- **FR-057** (personal half): Projects MUST have a visibility of personal (private to owner) or shared; new projects default to personal. This slice realizes the personal visibility value and the default-personal behavior; the shared visibility value is realized in slice 007 (project-sharing-membership).

> Scope note: FR-021 redefines slice 002's flat "all tasks" list as the Inbox — tasks with no project assignment. With projects introduced in this slice, a task that has not been moved to any project belongs to the Inbox; assigning a task to a project (via US-08.AS-05) removes it from the Inbox.

> Scope note (lifecycle, no new FR ID): the project edit (name/color/icon/parent), re-parent, parent-with-children delete/archive prompt, and unarchive behaviors (resolved for slice 004 per the remediation ledger, where edit and unarchive each "get an FR") are folded into the existing owned FRs FR-011–FR-014 and carried by acceptance scenarios AS-07–AS-11. The owned FR text remains a 1:1 copy of product-vision.md (the canonical FR-text source); the added lifecycle behavior is normatively specified by the acceptance scenarios — AS-07 (edit name/color/icon/parent on FR-011's fields), AS-08/AS-09 (re-parent within FR-012's one-level rule, rejecting grandchildren), AS-11 (unarchive on FR-013), and AS-10 (parent-with-children delete/archive prompt on FR-014) — rather than by rewriting the owned FRs' normative MUST text. No new FR **ID** is minted: product-vision.md is the sole ID allocator (the sole-allocator rule governs ID numbers, not FR-text), and it allocates no standalone edit/unarchive/re-parent FR number, so these behaviors attach to the already-owned FR-011 (editable fields), FR-012 (one-level rule applied to re-parenting), FR-013 (archive/unarchive), and FR-014 (delete/archive-with-children prompt) rather than to a newly numbered FR.

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

Access control (realized in this slice) (per Constitution Principle IX):

Authorization is **deny-by-default and dispatched by the containing resource's visibility** — NOT a conjunction of tiers. Every project this slice creates carries a required `ownerId` and a `visibility` that defaults to **personal**, so this slice exercises the **personal/unprojected → ownership** dispatch branch: authorization is decided on ownership (`ownerId`/`createdBy`), with all queries scoped to the caller. `createdBy` and assignee are **provenance only** and confer NO standalone access. The shared-project → membership+role branch (and the rule that leave/remove/unshare revokes ALL access to a project's data regardless of authorship or assignment) is part of the same dispatch model but is realized in slice 007 (project-sharing-membership).

- **FR-065**: Authorization MUST be dispatched by the containing resource's visibility (not a conjunction of tiers): personal/unprojected data authorizes on ownership (`createdBy`/`ownerId`) with queries scoped to the caller; shared-project entities authorize on current `ProjectMembership` + role. Every query MUST be scoped accordingly (per-user isolation).
- **FR-066**: Access to a shared project's data MUST require current membership in that project. `createdBy` and assignee are provenance only and confer NO standalone access; on leave/remove/unshare a user MUST lose ALL access to that project's data regardless of authorship or assignment. *(Referenced — governs the shared branch realized in slice 007; the provenance-only rule already constrains this slice's `createdBy`.)*
- **FR-067**: Each operation on a shared-project resource MUST require sufficient role (viewer=read, editor=write, owner=manage); insufficient role MUST be denied. *(Referenced — governs the shared branch realized in slice 007.)*
- **FR-068**: Authorization MUST be deny-by-default and enforced at the API/handler layer for every read and write.

The slice's command and query handlers ENFORCE this at the handler layer (not merely reference it): every project create / edit / re-parent / archive / unarchive / delete command, the move-to-project (`M`) command, and the Inbox and project-list queries are dispatched on personal-visibility ownership, scoped to the authenticated caller, and denied deny-by-default otherwise (FR-068). A user may only read or write their own projects, and `M` may only move a task within the user's own/accessible projects. The membership+role branch (FR-066, FR-067) applies to shared projects and is realized in slice 007 (project-sharing-membership); it is not in this slice's scope.

### Key Entities

- **ENT-02 — Project**: An organizational container for tasks. Has a name, color, icon, optional parent project reference, archived flag, ownerId (the owning User), and visibility (personal or shared). Supports one level of nesting. Contains zero or more tasks. Shared projects have a membership set.

> Scope note (ENT-02 in this slice): this slice realizes the personal-visibility baseline plus ownership — every project carries a required `ownerId` and a `visibility` that defaults to personal. The shared `visibility` value, the project membership set, and the shared half of FR-057 are realized in slice 007 (project-sharing-membership); this slice owns only the personal baseline.

This slice also populates an attribute of an entity owned elsewhere. It does not introduce or own ENT-01; it sets the `project reference` attribute of **ENT-01 — Task** (owned by slice 002), which was reserved there as a nullable, forward-compatible column. For reference, the full Task definition from product-vision.md:

- **ENT-01 — Task**: The core work item. Has a title (required), description, priority (P0-P3), status (backlog/todo/in_progress/done/cancelled), due date, labels, project reference, cycle reference, recurrence rule, createdBy (the User who created it), assignees (zero or more Users; only on shared-project tasks), and system timestamps (created_at, updated_at, completed_at). New tasks default to "backlog" status. A recurring task has a linked recurrence rule that generates successor instances.

## Success Criteria *(mandatory)*

### Measurable Outcomes

This slice introduces no new slice-specific success criteria. The measurable outcomes owned by slice 002 continue to apply: SC-003 (creating a project, moving a task, and archiving each paints its optimistic result within 16ms of the triggering keypress; the server reconciles or rolls back asynchronously) and SC-004 (project operations depend on no third-party runtime data services — only its own API and PostgreSQL database; there are no external SaaS data dependencies at runtime).

## Constitution Compliance

This slice is evaluated against constitution v4.0.0. Cross-cutting principles realized here:

- **I. Keyboard-First**: project organization is keyboard-driven — projects are created via the command palette or a dedicated action (US-10.AS-01), and a selected task is moved to a project with `M` (US-08.AS-05); no mouse interaction is required.
- **II. Accessibility (WCAG 2.1 AA)**: FR-042 (focus indicator), FR-043 (ARIA roles/labels) on the project form, selector, and delete/children-prompt dialogs, FR-044 (contrast ≥ 4.5:1 — preset colors and icons convey meaning together with text, never by color alone), FR-045 (no AT-binding collisions), FR-046 (no hover-only content), FR-047 (prefers-reduced-motion). FR-101 applies the **dialog focus contract** to the project create/edit form, the project selector (`M`), the delete-with-tasks dialog, and the new delete/archive-children prompt (set initial focus, trap focus, dismiss on Esc, return focus to the invoker on close), and routes any server-initiated update or toast (e.g., an optimistic-create reconciliation or a delete-failure notice) through an ARIA live region without stealing focus. FR-031 keeps `M` and other single-key shortcuts from hijacking text entry in the project name field and project selector.
- **III. Instant Response**: the project create, `M` move, archive, and nesting-prevention message paint their optimistic result within 16ms (SC-003, owned by slice 002) while the server reconciles or rolls back asynchronously; server mutations meet a p95<200ms budget. Because projects are server-authoritative and shared in later slices, an inbound real-time update reconciles under last-write-wins and MUST yield to a pending local optimistic mutation until its server-ack resolves; the real-time transport itself (SignalR) is owned by slice 016 (real-time-collaboration). Skeleton screens are permitted for network-bound loads (Principle IV).
- **IV. Minimalist UI**: the project sidebar, create form, selector, and delete dialog stay minimal; skeleton screens are permitted for the network-bound project-list and project-view loads, but MUST NOT mask a mutation whose optimistic result could be shown instead.
- **V. Connected, Server-Authoritative**: PostgreSQL accessed through the C# API is the system of record; project records and the Task `project reference` are persisted server-side in the documented, inspectable relational schema (ASM-08, owned by slice 002), and the app depends on no third-party runtime data service — the only permitted external runtime dependency is Google OAuth, for authentication only (SC-004).
- **VI. Type Safety End-to-End**: the Project type is generated from the schema (source of truth), with runtime validation at the project-form input boundary and at storage deserialization; the one-level-nesting invariant (FR-012) is enforced as a validated rule.
- **VII. Data Integrity & Resilience**: FR-049 (error + recovery — e.g., the grandchild-prevention message of US-10.AS-03), FR-050 (structured logging), FR-051 (auto-backup hook in place ahead of the schema change that adds the Project entity and the Task `project reference`).
- **VIII. Test-First**: each owned acceptance scenario above, plus EC-03, is independently testable (Red-Green-Refactor); integration tests exercise the command/query handlers through the real database including authorization (a request for a project the caller does not own MUST be denied).
- **IX. Authentication & Authorization**: authorization is deny-by-default (FR-068) and enforced at the API/handler layer for every read and write in this slice, and is **dispatched by the containing resource's visibility — NOT a conjunction of tiers** (FR-065). Because every project here is personal-visibility by default, the operations in this slice (create / edit / re-parent / archive / unarchive / delete project, the move-to-project `M` action, and the Inbox and project-list queries) dispatch on the **personal/unprojected → ownership** branch: each is scoped to the caller's identity (`ownerId`/`createdBy`) — a user may only read or write their own projects, and `M` may only move a task within the user's own/accessible projects. `createdBy` is **provenance only** and never a standalone grant. The shared-project → membership+role branch (FR-066, FR-067) and the rule that leave/remove/unshare revokes ALL access to a project's data are part of the same dispatch model but are realized in slice 007 (project-sharing-membership); this slice realizes only the personal-ownership branch. Sessions and admission (FR-087/FR-088 et al.) are owned by slice 001; this slice assumes an authenticated, admitted caller.
- **X. Time & Timezone**: project records carry only system timestamps, stored in UTC per FR-092; this slice performs no date-relative computation (Today/Upcoming, cycle boundaries, recurrence) so the Europe/Warsaw reference-zone rule has no further surface here.
- **XI. Privacy & Personal Data**: the required `ownerId` on every project is the anchor for the account-deletion erasure cascade (owned shared projects transferred or deleted; `createdBy` nulled or reassigned) defined in FR-085 and realized via US-17 / slice 015; this slice introduces no other personal data beyond ownership.
- **XII. Security by Default**: the project **name** is user-authored content and MUST be output-encoded/sanitized on render so raw HTML injection is impossible (FR-099); preset colors and icons are from a constrained server-known set (ASM-04), not free-form input. This slice introduces no secrets.

**Known compliance gap (deferred, accepted at slicing time):** Principle VII requires destructive actions to be undoable for ≥ 30 seconds (FR-040), backed by soft-delete (FR-097). The soft-delete **persistence mechanism is NOT deferred**: per remediation-ledger B8, soft-delete ships from slice 002 day one (a `deleted_at` tombstone, excluded from authz-scoped queries, reaped after the undo window), so project and child-project deletions introduced in this slice (FR-014 / EC-03, the cascade-delete option, and the parent-with-children cascade in US-10.AS-10) are already tombstoned via FR-097 and excluded from this slice's authz-scoped queries. What is deferred is only the user-facing **30-second undo affordance** (FR-040): these destructive operations have no undo *UI* in slice 004, and the 30-second undo affordance over this slice's deletion, re-parent, and move paths is the pure retrofit delivered in slice 014 (undo). No undo affordance is added here, per the slice-004 definition. The delete/archive confirmation dialogs do, however, state their blast radius (affected tasks and child projects) per Principle VII.

## Assumptions

- **ASM-04 — Preset colors and icons**: Project colors and icons are selected from a predefined set, not custom user values.

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

Also out of scope for this slice specifically (deferred to later slices): the full list-shortcut requirement FR-029, of which `M` is a member, is owned by slice 011 (cycles); the command palette and search machinery referenced by US-10.AS-06 (locating and unarchiving an archived project) is owned by slice 013 (command-palette-search); the project Board and groupable List views are owned by slice 010 (project-board-kanban); priorities, Today/Upcoming views, and the full task editor are owned by slice 005 (daily-planning); shared project visibility, project membership and roles, and the shared half of FR-057 are owned by slice 007 (project-sharing-membership). This slice covers project creation, nesting, archive, deletion prompt, the Inbox definition, the personal-visibility/ownership baseline, and move-to-project only.

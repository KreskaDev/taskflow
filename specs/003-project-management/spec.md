# Feature Specification: Project Management

**Feature Branch**: `003-project-management`

**Created**: 2026-06-13

**Status**: Draft

**Input**: Slice 003 of the TaskFlow MVP. Source of truth: `.specify/memory/product-vision.md`. Goal: organize tasks into projects with one level of nesting (parent/child), color and icon from a preset, archive (hidden from default views but searchable), and the move-to-project action (`M`). This slice also defines the Inbox as tasks not assigned to any project, refining slice 001's flat "all tasks" list.

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
- EC-03 (deleting a project with tasks)
- ENT-02 (Project)
- ASM-04 (preset colors and icons)

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
- FR-051 (auto-backup before migration — infrastructure in place)

MVP boundary confirmed:
- OOS-01..OOS-12 (full MVP out-of-scope confirmation)

Entity touchpoint(s):
- ENT-01 (Task) — owned by slice 001; this slice populates the `project reference` attribute (reserved there as a nullable, forward-compatible column) via FR-021 (Inbox) and US-08.AS-05 (move-to-project)

Depends on:
- Slice 001 (task-capture) — provides the Task entity, the single task list (here redefined as the Inbox), and server-side persistence (PostgreSQL via the C# API)

Exercised-but-not-owned (scenario owned here; its umbrella requirement lives in a later slice):
- `M` move-to-project mechanic — the canonical acceptance scenario US-08.AS-05 is owned by this slice, but `M` is a member of the full list-shortcut requirement FR-029, which is owned by slice 007 (cycles). FR-029 is therefore not included in this slice's Requirements list.

## User Scenarios & Testing *(mandatory)*

### User Story 10 - Project Management (Priority: P2)

User creates, edits, archives, and organizes projects with one level of nesting (parent/child). Each project has a color and icon from a preset. Archived projects disappear from default views but remain searchable.

**Why this priority**: Projects are the organizational backbone for grouping tasks beyond the Inbox.

**Independent Test**: Can be tested by creating parent and child projects, assigning tasks, archiving a project, and verifying it disappears from the sidebar but remains in search.

**Acceptance Scenarios** (owned by this slice):

1. **(US-10.AS-01) Given** the user wants to create a project, **When** they use the command palette or dedicated action, **Then** a project creation form appears with fields for name, color (preset), icon (preset), and optional parent project.
2. **(US-10.AS-02) Given** a parent project exists, **When** user creates a child project under it, **Then** the child appears nested under the parent in the sidebar (max 1 level).
3. **(US-10.AS-03) Given** a user tries to create a grandchild project (child of a child), **When** they attempt to set a child project as parent, **Then** the system prevents it and shows a message that only one level of nesting is allowed.
4. **(US-10.AS-04) Given** a project has tasks assigned to it, **When** user chooses to delete the project, **Then** a dialog asks: delete tasks cascading, move tasks to Inbox, or archive the project with its tasks.
5. **(US-10.AS-05) Given** a project is archived, **When** user views the sidebar and default project list, **Then** the archived project is not visible.
6. **(US-10.AS-06) Given** a project is archived, **When** user searches for it in the command palette, **Then** it appears in results and can be unarchived.

---

### User Story 8 - Keyboard Navigation & Shortcuts (Priority: P1)

User operates on the selected task using keyboard shortcuts only. The shortcut realized in this slice is `M` (move the selected task to a different project), which the project organization introduced here makes meaningful.

**Why this priority**: Keyboard-first is the core principle. Without complete keyboard coverage, the app fails its primary promise.

**Independent Test**: Can be tested by selecting a task, pressing `M`, choosing a target project from the selector, and verifying the task moves to that project (and leaves the Inbox when assigned).

**Acceptance Scenarios** (owned by this slice):

1. **(US-08.AS-05) Given** a task is selected, **When** user presses `M`, **Then** a project selector appears for moving the task to a different project.

> Shortcut coverage: `M` is implemented and its canonical scenario (US-08.AS-05) is owned here, but `M` is a member of the full list-shortcut requirement FR-029, which is owned by slice 007 (cycles). See Provenance.

### Edge Cases

- **EC-03 — Deleting a project with tasks**: User is prompted with three options: cascade delete, move to Inbox, or archive with tasks.

## Requirements *(mandatory)*

### Functional Requirements (slice-specific)

- **FR-011**: System MUST allow creating projects with a name, color (from preset), and icon (from preset).
- **FR-012**: System MUST support one level of project nesting (parent-child) and MUST prevent creation of grandchild projects.
- **FR-013**: System MUST allow archiving projects, which hides them from default views while keeping them accessible via search.
- **FR-014**: When deleting a project that contains tasks, the system MUST prompt the user with three options: cascade delete, move tasks to Inbox, or archive the project with its tasks.
- **FR-021**: The Inbox view MUST show tasks not assigned to any project, sorted by newest first.

> Scope note: FR-021 redefines slice 001's flat "all tasks" list as the Inbox — tasks with no project assignment. With projects introduced in this slice, a task that has not been moved to any project belongs to the Inbox; assigning a task to a project (via US-08.AS-05) removes it from the Inbox.

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

### Key Entities

- **ENT-02 — Project**: An organizational container for tasks. Has a name, color, icon, optional parent project reference, and archived flag. Supports one level of nesting. Contains zero or more tasks.

This slice also populates an attribute of an entity owned elsewhere. It does not introduce or own ENT-01; it sets the `project reference` attribute of **ENT-01 — Task** (owned by slice 001), which was reserved there as a nullable, forward-compatible column. For reference, the full Task definition from product-vision.md:

- **ENT-01 — Task**: The core work item. Has a title (required), description, priority (P0-P3), status (backlog/todo/in_progress/done/cancelled), due date, labels, project reference, cycle reference, recurrence rule, and system timestamps (created_at, updated_at, completed_at). New tasks default to "backlog" status. A recurring task has a linked recurrence rule that generates successor instances.

## Success Criteria *(mandatory)*

### Measurable Outcomes

This slice introduces no new slice-specific success criteria. The measurable outcomes owned by slice 001 continue to apply: SC-003 (creating a project, moving a task, and archiving each paints its optimistic result within 16ms of the triggering keypress; the server reconciles or rolls back asynchronously) and SC-004 (project operations depend on no third-party runtime data services — only its own API and PostgreSQL database; there are no external SaaS data dependencies at runtime).

## Constitution Compliance

This slice is evaluated against constitution v2.0.0. Cross-cutting principles realized here:

- **I. Keyboard-First**: project organization is keyboard-driven — projects are created via the command palette or a dedicated action (US-10.AS-01), and a selected task is moved to a project with `M` (US-08.AS-05); no mouse interaction is required.
- **II. Accessibility (WCAG 2.1 AA)**: FR-042 (focus indicator), FR-043 (ARIA roles/labels) on the project form, selector, and delete dialog, FR-044 (contrast ≥ 4.5:1 — preset colors and icons convey meaning together with text, never by color alone), FR-045 (no AT-binding collisions), FR-046 (no hover-only content), FR-047 (prefers-reduced-motion). FR-031 keeps `M` and other single-key shortcuts from hijacking text entry in the project name field and project selector.
- **III. Instant Response**: the project create, `M` move, archive, and nesting-prevention message paint their optimistic result within 16ms (SC-003, owned by slice 001) while the server reconciles or rolls back asynchronously; server mutations meet a p95<200ms budget. Skeleton screens are permitted for network-bound loads (Principle IV).
- **V. Connected, Server-Authoritative**: PostgreSQL accessed through the C# API is the system of record; project records and the Task `project reference` are persisted server-side in the documented, inspectable relational schema (ASM-08, owned by slice 001), and the app depends on no third-party runtime data service (SC-004).
- **VI. Type Safety End-to-End**: the Project type is generated from the schema (source of truth), with runtime validation at the project-form input boundary and at storage deserialization; the one-level-nesting invariant (FR-012) is enforced as a validated rule.
- **VII. Data Integrity & Resilience**: FR-049 (error + recovery — e.g., the grandchild-prevention message of US-10.AS-03), FR-050 (structured logging), FR-051 (auto-backup hook in place ahead of the schema change that adds the Project entity and the Task `project reference`).
- **VIII. Test-First**: each owned acceptance scenario above, plus EC-03, is independently testable (Red-Green-Refactor).

**Known compliance gap (deferred, accepted at slicing time):** Principle VII requires destructive actions to be undoable for ≥ 30 seconds (FR-040). In this slice, project deletion (FR-014 / EC-03) and the cascade-delete option are destructive but have no undo window — the 30-second undo for project deletion and bulk moves is delivered in slice 010 (undo), which retrofits undo onto this slice's deletion and move paths. No undo is added here, per the slice-003 definition.

## Assumptions

- **ASM-04 — Preset colors and icons**: Project colors and icons are selected from a predefined set, not custom user values.

## Out of Scope

This slice confirms the full MVP out-of-scope boundary (OOS-01..OOS-12 from product-vision.md):

- **OOS-01**: Multi-user collaboration, sharing, permissions
- **OOS-02**: Cross-device sync, cloud storage
- **OOS-03**: Mobile application, PWA
- **OOS-04**: AI features (auto-categorization, summaries, suggestions)
- **OOS-05**: External integrations (calendar, Slack, GitHub, email)
- **OOS-06**: Push notifications, reminders
- **OOS-07**: File attachments on tasks
- **OOS-08**: Subtasks (task nesting)
- **OOS-09**: Custom views, saved filters
- **OOS-10**: Custom theming beyond dark/light mode
- **OOS-11**: Automations (if X then Y)
- **OOS-12**: Plugin or extension system

Also out of scope for this slice specifically (deferred to later slices): the full list-shortcut requirement FR-029, of which `M` is a member, is owned by slice 007 (cycles); the command palette and search machinery referenced by US-10.AS-06 (locating and unarchiving an archived project) is owned by slice 009 (command-palette-search); the project Board and groupable List views are owned by slice 006 (project-board-kanban); priorities, Today/Upcoming views, and the full task editor are owned by slice 004 (daily-planning). This slice covers project creation, nesting, archive, deletion prompt, the Inbox definition, and move-to-project only.

# Feature Specification: Cycles

**Feature Branch**: `007-cycles`

**Created**: 2026-06-13

**Status**: Draft

**Input**: Slice 007 of the TaskFlow MVP. Source of truth: `.specify/memory/product-vision.md`. Goal: 2-week cycles (sprints) — assigning tasks to a cycle, the cycle metrics view (% done, days remaining, status breakdown), end-of-cycle rollover of unfinished tasks, and deletion guards that prevent removing an active cycle. This slice also completes the navigation (`G C`) and list-shortcut (`#`) sets begun in earlier slices, and builds on the daily-planning task model (004) and the project Board/List views (006).

## Provenance

This slice realizes the following items from product-vision.md:

Slice-specific:
- US-05 (Cycle Management & Review) — full: AS-01, AS-02, AS-03, AS-04, AS-05, AS-06, AS-07
- FR-015 (cycles with default 2-week duration, configurable in settings)
- FR-016 (each task belongs to at most one cycle, or none = backlog)
- FR-017 (active cycle visible in the sidebar)
- FR-018 (rollover options for incomplete tasks at cycle close)
- FR-019 (prevent deletion of an active cycle)
- FR-020 (create/edit/close/delete cycles; deletion guards)
- FR-026 (Cycle view: % done, days remaining, breakdown by status)
- FR-028 (navigation shortcuts set — completes here with `G C`)
- FR-029 (list shortcuts set — completes here with `#`)
- EC-04 (deleting an active cycle is prevented)
- EC-10 ("backlog" naming distinction: task status vs. cycle backlog)
- EC-12 (archiving a project with cycle-assigned tasks)
- ENT-03 (Cycle)

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
- ENT-01 (Task) — this slice populates the `cycle reference` attribute via cycle assignment (US-05.AS-02); the Task entity is owned by slice 001, which reserved this attribute as a nullable, forward-compatible column for slice 007
- ENT-02 (Project) — read by EC-12 when archiving a project whose tasks are assigned to an active cycle (the Project entity is owned by slice 003); no attribute is introduced here

Depends on:
- Slice 004 (daily-planning) — provides priorities, the full task editor, and the per-task selection/status model that the cycle metrics breakdown and per-task rollover choices reuse
- Slice 006 (project-board-kanban) — provides the Project List view groupable by cycle (FR-024) and the status-column model whose statuses feed the Cycle view's breakdown

Note on completed shortcut sets: the FR-028 navigation set and the FR-029 list-shortcut set are owned and completed in this slice. Their earlier members already shipped via acceptance scenarios in earlier slices (e.g. `G I`/`G T`/`G U` and `↑/↓`/`E`/`Space`/`1-4`/`T`/`L`/`M`/`Del` / arrow-column-moves). US-05's scenarios exercise the final members of each set: `#` (move to cycle, US-05.AS-01) and `G C` (current cycle, US-05.AS-03). This is framing only; no new requirement text is introduced.

## User Scenarios & Testing *(mandatory)*

### User Story 5 - Cycle Management & Review (Priority: P3)

User works with 2-week cycles (sprints). They assign tasks to the current cycle, track progress via metrics (% done, days remaining, status breakdown), and at cycle end, roll unfinished tasks to the next cycle or back to backlog.

**Why this priority**: Cycles add structured time-boxing on top of basic task management. Valuable for disciplined planning but not required for basic daily use.

**Independent Test**: Can be tested by creating a cycle, assigning tasks, completing some, closing the cycle, and verifying rollover behavior.

> Scope note: this slice completes the navigation (FR-028) and list-shortcut (FR-029) sets — the `G C` (current cycle) and `#` (move to cycle) members are exercised by the scenarios below, while the remaining members shipped via earlier slices. The undo of the bulk move-all-to-next-cycle operation (US-09.AS-05) is delivered in slice 010 (undo); see Constitution Compliance.

**Acceptance Scenarios** (owned by this slice):

1. **(US-05.AS-01) Given** the user is on any view with a task selected, **When** they press `#`, **Then** a cycle selector appears listing available cycles and a "backlog" option.
2. **(US-05.AS-02) Given** the cycle selector is open, **When** user selects a cycle, **Then** the task is assigned to that cycle.
3. **(US-05.AS-03) Given** the user navigates to the current cycle view (`G C`), **When** the view renders, **Then** it shows: percentage of tasks done, days remaining in the cycle, and a breakdown of tasks by status.
4. **(US-05.AS-04) Given** a cycle has ended with unfinished tasks, **When** user opens the cycle review, **Then** they see a list of incomplete tasks with options to: move all to next cycle, move all to backlog, or handle individually.
5. **(US-05.AS-05) Given** the cycle review shows incomplete tasks, **When** user selects "move all to next cycle" and confirms, **Then** all incomplete tasks are reassigned to the next cycle in a single operation.
6. **(US-05.AS-06) Given** no next cycle exists when rollover is attempted, **When** user selects "move to next cycle", **Then** the system prompts to create a new cycle first.
7. **(US-05.AS-07) Given** an active cycle exists, **When** user attempts to delete it, **Then** the system prevents deletion and shows a message explaining the cycle must be closed first.

### Edge Cases

- **EC-04 — Deleting an active cycle**: The system prevents this; the cycle must be closed first.
- **EC-10 — "Backlog" naming distinction**: The term "backlog" is used in two different contexts: (1) task status "backlog" refers to a task's workflow state on the Kanban board (first column, meaning the task has not yet been triaged into active work); (2) cycle backlog refers to tasks not assigned to any cycle. These are independent dimensions — a task can have status "todo" while being in the cycle backlog, or have status "backlog" while assigned to a cycle.
- **EC-12 — Archiving a project with cycle-assigned tasks**: When archiving a project whose tasks are assigned to an active cycle, the tasks remain in the cycle but are hidden from project-based views. They remain visible in the Cycle view and can be moved to another project or unassigned.

## Requirements *(mandatory)*

### Functional Requirements (slice-specific)

- **FR-015**: System MUST support cycles (sprints) with a default duration of 2 weeks, configurable in application settings.
- **FR-016**: Each task MUST belong to at most one cycle (or no cycle, meaning backlog).
- **FR-017**: The active cycle MUST be visible in the sidebar.
- **FR-018**: When a cycle is closed with incomplete tasks, the system MUST provide rollover options: move all to next cycle, move all to backlog (remove cycle assignment), keep all in the closed cycle with a "carried over" flag, or handle individually (per-task choice among the same three options).
- **FR-019**: System MUST prevent deletion of an active cycle; the cycle must be closed first.
- **FR-020**: System MUST allow the user to create, edit (name, start/end dates), close, and delete cycles. Active cycles cannot be deleted (must be closed first, per FR-019). Planned and closed cycles may be deleted only if they have no assigned tasks.
- **FR-026**: The Cycle view MUST display: percentage of tasks completed, days remaining, and breakdown by status.
- **FR-028**: System MUST support all specified navigation shortcuts: `G I` (Inbox), `G T` (Today), `G U` (Upcoming), `G P` (Projects), `G C` (Current cycle).
- **FR-029**: System MUST support all specified list shortcuts: arrows (move), `E` (edit), `Space` (toggle done), `1-4` (priority), `T` (date), `L` (label), `M` (move to project), `#` (move to cycle), `Del` (delete).

> Scope note: FR-028 and FR-029 are owned in full here and reach completion in this slice. Their non-cycle members shipped through acceptance scenarios in earlier slices (`G I`/`G T`/`G U` via slices 001/004, `G P` via slice 003; `↑/↓` via slice 001, `E`/`Space`/`1-4`/`T` via slice 004, `L` via slice 005, `M` via slice 003, arrow column-moves via slice 006, `Del` via slice 001). The cycle members — `G C` (US-05.AS-03) and `#` (US-05.AS-01) — are exercised here, completing both sets.

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

This slice introduces and owns the Cycle entity:

- **ENT-03 — Cycle**: A time-boxed period (default 2 weeks) for organizing work. Has a start date, end date, and status (active/closed/planned). Contains zero or more tasks. Only one cycle can be active at a time. A task not assigned to any cycle is considered "in the cycle backlog" (distinct from the task status "backlog" — cycle backlog refers to cycle assignment, not workflow state). Tasks remaining in a closed cycle may carry a "carried over" flag indicating they were not completed during that cycle's timeframe.

> Entity touchpoint: this slice populates the `cycle reference` attribute of **ENT-01 — Task** (owned by slice 001, reserved there as a nullable, forward-compatible column) when a task is assigned to a cycle (US-05.AS-02). The "carried over" flag in ENT-03 corresponds to the keep-in-closed-cycle rollover option in FR-018.

## Success Criteria *(mandatory)*

### Measurable Outcomes

This slice introduces no new slice-specific success criteria. The measurable performance and quality outcomes established in earlier slices continue to apply to the cycle assignment, metrics, and rollover interactions delivered here.

## Constitution Compliance

This slice is evaluated against constitution v2.0.0. Cross-cutting principles realized here:

- **I. Keyboard-First**: cycle assignment (`#`), navigation to the current cycle (`G C`), and the rollover review are all keyboard-driven. This slice completes the navigation (FR-028) and list-shortcut (FR-029) grammar begun in earlier slices, so the full keyboard shortcut set is now consistent and composable across all views.
- **II. Accessibility (WCAG 2.1 AA)**: FR-042 (focus indicator), FR-043 (ARIA roles/labels), FR-044 (contrast ≥ 4.5:1), FR-045 (no AT-binding collisions), FR-046 (no hover-only content), FR-047 (prefers-reduced-motion). FR-031 keeps single-key shortcuts (including `#`) from hijacking text entry; the cycle selector, metrics view, and rollover dialog are operable and labeled for screen readers.
- **III. Instant Response**: cycle assignment, the metrics render, and rollover confirmation paint an optimistic result within ~16ms of the triggering keypress while the C# API reconciles asynchronously (p95 < 200ms); skeleton screens are permitted for the network-bound metrics and cycle fetch.
- **V. Connected, Server-Authoritative**: cycles, assignments, and rollover are persisted server-side in PostgreSQL through the C# API, which owns all writes and is the single source of truth; the Next.js client renders these mutations optimistically while the server reconciles.
- **VI. Type Safety End-to-End**: the Cycle entity and its status enum (active/closed/planned) are typed end-to-end, with EF Core code-first migrations as the schema source of truth for the Cycle entity and the Task cycle-reference; runtime validation is applied at the trust boundaries — Zod on the web client for user input and API responses, FluentValidation (or data annotations) at the C# API boundary. The cycle-reference on Task is a validated, nullable relation.
- **VII. Data Integrity & Resilience**: FR-049 (error + recovery — e.g. the "create a new cycle first" prompt of US-05.AS-06 and the active-cycle deletion guard of US-05.AS-07 / EC-04 / FR-019), FR-050 (structured logging), FR-051 (auto-backup hook ahead of the schema change that adds the Cycle entity and the Task cycle reference). The deletion guards (FR-019/FR-020) actively protect data by refusing to remove an active cycle or a non-empty planned/closed cycle.
- **VIII. Test-First**: each owned acceptance scenario above (US-05.AS-01..AS-07), plus EC-04, EC-10, and EC-12, is independently testable (Red-Green-Refactor).

**Known compliance gap (deferred, accepted at slicing time):** Principle VII requires destructive and bulk operations to be undoable for ≥ 30 seconds (FR-040). This slice owns the bulk "move all to next cycle" operation (US-05.AS-05) and the move-all-to-backlog rollover, but the 30-second undo for that bulk cycle move is the responsibility of slice 010 (undo), whose canonical scenario US-09.AS-05 restores all moved tasks to their original cycle assignments. No undo is added in this slice, per the slice-007 definition; slice 010 retrofits the undo window onto these rollover paths.

## Assumptions

This slice introduces no new slice-specific assumptions. The MVP-wide assumptions established in product-vision.md continue to apply.

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

Additionally deferred to later slices within the MVP (not part of slice 007): the 30-second undo window for cycle rollover bulk moves (US-09.AS-05) is owned by slice 010 (undo); recurring tasks (008); command palette & search (009); data export/import (011), which serializes the Cycle entity introduced here; and dark/light theming (012).

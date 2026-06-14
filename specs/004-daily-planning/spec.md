# Feature Specification: Daily Planning

**Feature Branch**: `004-daily-planning`

**Created**: 2026-06-13

**Status**: Draft

**Input**: Slice 004 of the TaskFlow MVP. Source of truth: `.specify/memory/product-vision.md`. Goal: deliver the mouse-free daily loop — the Today and Upcoming views, task priorities, and a full task editor — so the user can review, triage, reprioritize, reschedule, and complete the day's work entirely from the keyboard. This builds on capture (001), natural-language dates (002), and projects (003).

## Provenance

This slice realizes the following items from product-vision.md:

Slice-specific:
- US-02 (Daily Planning Session) — full: AS-01, AS-02, AS-03, AS-04, AS-05, AS-06, AS-07, AS-08
- US-08 (Keyboard Navigation & Shortcuts) — subset: AS-01 (`G I` Inbox), AS-02 (`G U` Upcoming)
- FR-002 (optional task fields umbrella — trace-anchored here; `description` and `priority` realized in this slice)
- FR-022 (Today view: due-today, grouped by project, sorted by priority)
- FR-023 (Upcoming view: next 7 days, grouped by day)
- FR-030 (task editor shortcuts: `Ctrl+Enter` save, `Esc` cancel)
- SC-001 (complete mouse-free daily workflow)
- SC-008 (every main view passes WCAG 2.1 AA audit)

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
- ENT-01 (Task) — this slice populates the `priority` and `description` attributes (the Task entity is owned by slice 001)

Depends on:
- Slice 002 (natural-language-dates) — provides the Polish date parser consumed by the `T` reschedule scenario (US-02.AS-05); FR-005 is owned there
- Slice 003 (project-management) — provides projects and the Inbox definition consumed by the Today view's project grouping (FR-022) and the `G I` navigation scenario (US-08.AS-01); FR-021 is owned there

Exercised-but-not-owned (mechanics/keys exercised here; canonical ownership lives in other slices):
- The `1`-`4` priority keys and the `T` date key are members of FR-029 (the list-shortcuts requirement). They are exercised by US-02.AS-04 and US-02.AS-05 in this slice, but FR-029 is owned by slice 007 (cycles) — see Constitution Compliance.
- The `/` filter scenario US-08.AS-08 is owned by slice 009 (command-palette-search) and is not realized here.
- The full task editor delivered here (US-02.AS-06, US-02.AS-07, US-02.AS-08) supersedes slice 001's inline title edit (`E`); the canonical Space/E scenarios US-02.AS-03 and US-02.AS-06, whose mechanics were built in slice 001, are owned and counted in this slice.

## User Scenarios & Testing *(mandatory)*

### User Story 2 - Daily Planning Session (Priority: P1)

User opens the "Today" view to see all tasks due today, grouped by project and sorted by priority. Using only keyboard shortcuts, they navigate between tasks, change priorities, edit details, reschedule, and mark tasks as done.

**Why this priority**: Daily planning is the second core loop after capture. Users need a reliable, fast way to review and triage their day.

**Independent Test**: Can be tested by creating several tasks with today's due date across different projects and priorities, navigating to Today view, and performing triage operations entirely via keyboard.

> Scope note: this slice OWNS the canonical Space toggle-done and `E` edit scenarios (US-02.AS-03 and US-02.AS-06), whose mechanics were first built in slice 001. The full task editor delivered here supersedes slice 001's inline title edit. The `T` reschedule scenario (US-02.AS-05) consumes the Polish date parser delivered in slice 002. The `1`-`4` priority keys and the `T` date key are members of FR-029, owned by slice 007 (see Provenance and Constitution Compliance).

**Acceptance Scenarios** (owned by this slice):

1. **(US-02.AS-01) Given** the user is on any view, **When** they press `G T`, **Then** the Today view opens showing only tasks with due date equal to today.
2. **(US-02.AS-02) Given** the Today view is open with tasks from multiple projects, **When** the view renders, **Then** tasks are grouped by project and sorted by priority (P0 first) within each group.
3. **(US-02.AS-03) Given** the Today view is open with a task selected, **When** user presses `Space`, **Then** the task status toggles to "done" and `completed_at` is recorded.
4. **(US-02.AS-04) Given** the Today view is open with a task selected, **When** user presses `1`, **Then** the task priority changes to P0 with immediate visual feedback.
5. **(US-02.AS-05) Given** the Today view is open with a task selected, **When** user presses `T`, **Then** a date picker/input appears; user types "jutro" and presses Enter; the task's due date changes to tomorrow and it disappears from Today view.
6. **(US-02.AS-06) Given** the Today view is open with a task selected, **When** user presses `E`, **Then** a task editor opens with the title field focused, allowing inline editing.
7. **(US-02.AS-07) Given** the task editor is open, **When** user presses `Ctrl+Enter`, **Then** changes are saved and the editor closes.
8. **(US-02.AS-08) Given** the task editor is open, **When** user presses `Esc`, **Then** changes are discarded and the editor closes.

---

### User Story 8 - Keyboard Navigation & Shortcuts (Priority: P1)

User navigates to the Inbox and Upcoming views using global navigation shortcuts. The navigation shortcuts realized in this slice are `G I` (Inbox) and `G U` (Upcoming).

**Why this priority**: Keyboard-first is the core principle. Without complete keyboard coverage, the app fails its primary promise.

**Independent Test**: Can be tested by pressing `G I` from any view and verifying the Inbox opens, and pressing `G U` and verifying the Upcoming view opens showing the next 7 days grouped by day.

> Scope note: this slice owns the `G I` and `G U` navigation scenarios (US-08.AS-01, US-08.AS-02). The earlier US-08 scenarios AS-03, AS-07, and AS-09 are owned by slice 001 (task-capture); the `/` filter scenario US-08.AS-08 is owned by slice 009 (command-palette-search). `G I` resolves to the Inbox definition provided by slice 003.

**Acceptance Scenarios** (owned by this slice):

1. **(US-08.AS-01) Given** the app is open, **When** user presses `G I`, **Then** the Inbox view opens.
2. **(US-08.AS-02) Given** the app is open, **When** user presses `G U`, **Then** the Upcoming view opens showing tasks for the next 7 days grouped by day.

### Edge Cases

No edge cases (EC-NN) from product-vision.md are assigned to this slice.

## Requirements *(mandatory)*

### Functional Requirements (slice-specific)

- **FR-002**: System MUST support optional task fields: description (markdown), priority (P0/P1/P2/P3), due date, labels (multiple), and project assignment.
- **FR-022**: The Today view MUST show all tasks with due date equal to today, grouped by project and sorted by priority within each group.
- **FR-023**: The Upcoming view MUST show tasks for the next 7 days, grouped by day.
- **FR-030**: System MUST support task editor shortcuts: `Ctrl+Enter` (save), `Esc` (cancel).

> Scope note: FR-002 is the optional-fields umbrella and is trace-anchored here. This slice realizes its `description` and `priority` portions (via the full task editor and the `1`-`4` priority keys). The remaining optional fields are realized in their owning slices: `due date` in slice 002 (natural-language-dates), `project assignment` in slice 003 (project-management), and `labels` in slice 005 (labels).

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
- **FR-051**: Before any data migration, the system MUST automatically create a local backup of user data. The user MUST be able to restore from this backup.

### Key Entities

This slice introduces no new entity. It populates the `priority` and `description` attributes of **ENT-01 — Task** (owned by slice 001), which were reserved there as nullable, forward-compatible columns. For reference, the full Task definition from product-vision.md:

- **ENT-01 — Task**: The core work item. Has a title (required), description, priority (P0-P3), status (backlog/todo/in_progress/done/cancelled), due date, labels, project reference, cycle reference, recurrence rule, and system timestamps (created_at, updated_at, completed_at). New tasks default to "backlog" status. A recurring task has a linked recurrence rule that generates successor instances.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: User can perform a complete daily workflow (capture task, review today's tasks, reprioritize, reschedule, mark done) without using the mouse at any point.
- **SC-008**: Every main view passes automated accessibility audit at WCAG 2.1 AA level.

## Constitution Compliance

This slice is evaluated against constitution v1.1.0. Cross-cutting principles realized here:

- **I. Keyboard-First**: the entire daily loop — open Today (`G T`), open Inbox (`G I`), open Upcoming (`G U`), navigate, toggle done (`Space`), set priority (`1`-`4`), reschedule (`T`), edit (`E`), save (`Ctrl+Enter`), cancel (`Esc`) — is keyboard-driven; this realizes SC-001 (complete mouse-free daily workflow).
- **II. Accessibility (WCAG 2.1 AA)**: FR-042 (focus indicator), FR-043 (ARIA roles/labels), FR-044 (contrast ≥ 4.5:1), FR-045 (no AT-binding collisions), FR-046 (no hover-only content), FR-047 (prefers-reduced-motion). FR-031 keeps single-key shortcuts (including `1`-`4`, `E`, `T`) from hijacking text entry in the editor and date input. SC-008 audits the Today and Upcoming views at WCAG 2.1 AA.
- **III. Instant Response**: priority changes produce immediate visual feedback (US-02.AS-04) and reschedules reflect within one frame; the inherited SC-003 (16ms feedback, owned by slice 001) continues to apply.
- **V. Offline-Only, Local-First**: all view rendering, priority changes, edits, and reschedules run on local data with zero network calls (SC-004, owned by slice 001).
- **VI. Type Safety End-to-End**: the `priority` (P0-P3 enum) and `description` (markdown string) fields added to the Task type are generated from the schema and validated at the editor input boundary.
- **VII. Data Integrity & Resilience**: FR-049 (error + recovery) and FR-050 (structured logging) cover edit/reschedule failures; FR-051 keeps the backup hook in place ahead of the schema change that populates `priority` and `description`. The editor's `Esc` (US-02.AS-08) discards in-flight changes without committing partial state.
- **VIII. Test-First**: each owned acceptance scenario above is independently testable (Red-Green-Refactor).

**Known ownership boundary (noted at slicing time):** US-02.AS-04 (`1`-`4` priority) and US-02.AS-05 (`T` date) exercise keys that are members of FR-029 (the consolidated list-shortcuts requirement). FR-029 is owned by slice 007 (cycles), where the full list-shortcut set is requirement-mapped; it is therefore deliberately NOT listed among this slice's Functional Requirements even though the two scenarios that use those keys are owned and delivered here. This mirrors the inverse handling in slice 001, where the `Space`/`E` mechanics were built ahead of their canonical scenarios (now owned here).

## Assumptions

No assumptions (ASM-NN) from product-vision.md are assigned to this slice; the assumptions established in slices 001–003 (single user, desktop, Polish date parsing, no subtasks, no notifications, data format) continue to apply.

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

Additionally deferred to later slices within the MVP (not part of slice 004): labels and the label selector (005), the project Kanban board and groupable project list (006), cycles and the `1`-`4`/`T` list-shortcut requirement FR-029 (007), recurring tasks (008), command palette, fuzzy search, and the `/` view filter US-08.AS-08 (009), undo (010), data export/import (011), and dark/light theming (012).

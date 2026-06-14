# Feature Specification: Natural-Language Dates

**Feature Branch**: `002-natural-language-dates`

**Created**: 2026-06-13

**Status**: Draft

**Input**: Slice 002 of the TaskFlow MVP. Source of truth: `.specify/memory/product-vision.md`. Goal: parse Polish natural-language date expressions during task capture so a typed phrase sets the due date, with a clear, recoverable error when the phrase cannot be understood. This completes the capture journey begun in slice 001.

## Provenance

This slice realizes the following items from product-vision.md:

Slice-specific:
- US-01 (Daily Task Capture) — completes partial: AS-02, AS-03, AS-04, AS-05
- FR-005 (parse Polish natural-language date expressions)
- FR-006 (parser-failure UX: retain previous value + "nie rozpoznano")
- EC-02 (natural-language date parsing failure)
- ASM-03 (Polish language UI for date parsing)

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

Entity touchpoint:
- ENT-01 (Task) — this slice populates the `due_date` attribute (the Task entity is owned by slice 001)

Depends on:
- Slice 001 (task-capture) — provides the capture input, the Task entity, and server-side persistence

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Daily Task Capture (Priority: P1)

User opens the application and immediately captures a new task using only the keyboard. They press `C`, type a task title with an optional natural language date (e.g., "Kupic mleko po 17"), and press Enter. The task lands in the Inbox with the parsed due date. The entire flow completes in under 3 seconds without touching the mouse.

**Why this priority**: Capture is the most frequent action in any task manager. If adding a task is slow or friction-heavy, users abandon the tool. This is the atomic unit of value.

**Independent Test**: Can be fully tested by launching the app, pressing `C`, typing a task with a date phrase, pressing Enter, and verifying the task appears with the correct due date; and by typing an unrecognized phrase and verifying the recoverable error behavior.

> Scope note: plain capture and cancellation (US-01.AS-01, AS-06, AS-07) are delivered in slice 001. This slice adds the natural-language date parsing scenarios below, completing US-01.

**Acceptance Scenarios** (owned by this slice):

1. **(US-01.AS-02) Given** the task creation input is focused, **When** user types "Kupic mleko po 17" and presses Enter, **Then** a task titled "Kupic mleko" is created in Inbox with today's due date at 17:00.
2. **(US-01.AS-03) Given** the task creation input is focused, **When** user types "Raport jutro" and presses Enter, **Then** a task titled "Raport" is created with due date set to tomorrow.
3. **(US-01.AS-04) Given** the task creation input is focused, **When** user types "Meeting piatek" and presses Enter, **Then** a task titled "Meeting" is created with due date set to the next occurring Friday.
4. **(US-01.AS-05) Given** the task creation input is focused, **When** user types "Zakupy za 3 dni" and presses Enter, **Then** a task titled "Zakupy" is created with due date 3 days from now.

### Edge Cases

- **EC-02 — Natural language date parsing failure**: When the date parser cannot interpret the user's input, the date field retains its previous value (or remains empty for new tasks) and a red error message "nie rozpoznano" appears below the field.

## Requirements *(mandatory)*

### Functional Requirements (slice-specific)

- **FR-005**: System MUST parse natural language date expressions in Polish ("jutro", "piatek", "za 3 dni", "po 17", "30.06") and set the due date accordingly.
- **FR-006**: When the natural language date parser cannot interpret input, the system MUST retain the previous date value and display a red error message "nie rozpoznano" below the date field.

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

This slice introduces no new entity. It populates the `due_date` attribute of **ENT-01 — Task** (owned by slice 001), which was reserved there as a nullable, forward-compatible column. For reference, the full Task definition from product-vision.md:

- **ENT-01 — Task**: The core work item. Has a title (required), description, priority (P0-P3), status (backlog/todo/in_progress/done/cancelled), due date, labels, project reference, cycle reference, recurrence rule, and system timestamps (created_at, updated_at, completed_at). New tasks default to "backlog" status. A recurring task has a linked recurrence rule that generates successor instances.

## Success Criteria *(mandatory)*

### Measurable Outcomes

This slice introduces no new slice-specific success criteria. The relevant measurable outcomes are owned by slice 001 and continue to apply: SC-003 (every user action paints its optimistic result within 16ms of the triggering keypress; the server reconciles or rolls back asynchronously — here, the parsed due date is painted optimistically within 16ms while the C# API persists it and the client reconciles) and SC-004 (depends on no third-party runtime data services — only its own API and PostgreSQL database).

## Constitution Compliance

This slice is evaluated against constitution v2.0.0. Cross-cutting principles realized here:

- **I. Keyboard-First**: date entry is part of the keyboard capture flow — the user types the phrase inline and presses Enter; no mouse interaction is required.
- **II. Accessibility (WCAG 2.1 AA)**: the FR-006 error ("nie rozpoznano") is conveyed via an ARIA-live region and meets contrast requirements (FR-044), not by color alone; FR-031, FR-042, FR-043, FR-045, FR-046, FR-047 carry from the capture UI.
- **III. Instant Response**: the parsed due date is painted optimistically within one animation frame (under 16ms of the keypress) while the C# API persists the change and the client reconciles or rolls back asynchronously; server-confirmed mutations target a p95 under 200ms, and skeleton screens are permitted for network-bound loads (SC-003, owned by slice 001).
- **V. Connected, Server-Authoritative**: the Polish natural-language parser interprets the typed phrase client-side as input interpretation, but the resulting `due_date` write is server-authoritative — persisted in PostgreSQL through the C# API, which is the system of record; network connectivity is required for normal operation, and the app depends on no third-party runtime data services (SC-004, owned by slice 001).
- **VI. Type Safety End-to-End**: the parser returns a typed result (a resolved date, or an "unrecognized" outcome that drives FR-006); the date boundary is validated at runtime.
- **VII. Data Integrity & Resilience**: FR-006 is the in-flow recovery for a parse failure (retain prior value, no silent loss), satisfying FR-049; FR-050 logs the failure with context; FR-051 keeps the automatic pre-migration backup (pg_dump or a managed database snapshot) in place ahead of the EF Core schema change that adds `due_date`.
- **VIII. Test-First**: each owned acceptance scenario above, plus EC-02, is independently testable (Red-Green-Refactor).

## Assumptions

- **ASM-03 — Polish language UI for date parsing**: Natural language date input supports Polish expressions as the primary language. Error messages related to date parsing (e.g., "nie rozpoznano") are in Polish. Additional language support may be added later but is not required for MVP.

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

Also out of scope for this slice specifically (deferred to later slices): rescheduling an existing task's date via the `T` shortcut from a list/Today view (US-02.AS-05) is owned by slice 004 (daily-planning); recurrence-rule date computation (US-06 / ASM-09) is owned by slice 008 (recurring-tasks). This slice covers natural-language date parsing only at capture time.

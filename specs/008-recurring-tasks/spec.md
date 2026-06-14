# Feature Specification: Recurring Tasks

**Feature Branch**: `008-recurring-tasks`

**Created**: 2026-06-13

**Status**: Draft

**Input**: Slice 008 of the TaskFlow MVP. Source of truth: `.specify/memory/product-vision.md`. Goal: let a task repeat on a schedule (daily, every N days, specific weekdays, monthly) and automatically generate the next instance when an instance is completed. The next instance is computed from the original due date, not the completion date (ASM-09); early completion defers the spawn to the next on/after-due startup (EC-05 / FR-009).

## Provenance

This slice realizes the following items from product-vision.md:

Slice-specific:
- US-06 (Recurring Tasks) — full: AS-01, AS-02, AS-03, AS-04, AS-05, AS-06
- FR-007 (supported recurrence schedules: daily, every N days, specific weekdays, monthly)
- FR-008 (generate next instance on completion on/after due; carry-forward fields and resets)
- FR-009 (early completion defers spawn to startup/periodic check on or after the original due date)
- FR-010 (cancelling a recurring task stops further instances)
- EC-05 (recurring task early completion — deferred spawn)
- ASM-09 (recurrence computed from original due date, not completion date)

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
- ENT-05 (Recurrence Rule) — introduced and owned by this slice; it populates the `recurrence rule` attribute of ENT-01 (Task), which is owned by slice 001

Depends on:
- Slice 004 (daily-planning) — provides the full task editor (where a recurrence rule is set/edited), the `Space` toggle-done mechanic, and the priority/due-date handling that the carried-forward fields rely on

## User Scenarios & Testing *(mandatory)*

### User Story 6 - Recurring Tasks (Priority: P3)

User creates tasks that repeat on a schedule (daily, every N days, specific weekdays, monthly). When a recurring task instance is completed, the next instance is automatically generated according to the recurrence rule.

**Why this priority**: Recurring tasks are important for habits and routine work but are an enhancement to the core task model.

**Independent Test**: Can be tested by creating a recurring task, completing it, and verifying the next instance is generated with the correct due date.

> Scope note: the next instance is computed from the original due date, not the completion date (ASM-09). When an instance is completed early (before its due date), no new instance is spawned immediately; the deferred instance is generated when the application is opened on or after the original due date, or during periodic checks while the app is running (EC-05 / FR-009). This is framing only — the binding requirements are FR-008, FR-009, ASM-09, and EC-05 below.

**Acceptance Scenarios** (owned by this slice):

1. **(US-06.AS-01) Given** a user is creating or editing a task, **When** they set a recurrence rule (e.g., "every day"), **Then** the task is marked as recurring with a visual indicator.
2. **(US-06.AS-02) Given** a recurring task with rule "every day" and due date today, **When** user marks it as done, **Then** a new task instance is automatically created with due date tomorrow and the same title, description, priority, labels, and project.
3. **(US-06.AS-03) Given** a recurring task with rule "every Monday and Wednesday" and due date Monday, **When** user marks it as done on Monday, **Then** the next instance is created with due date Wednesday.
4. **(US-06.AS-04) Given** a recurring task with due date tomorrow, **When** user marks it as done today (before due date), **Then** no new instance is generated yet (to avoid premature spawning).
5. **(US-06.AS-05) Given** a recurring task with rule "every 3 days" and due date June 10, **When** user marks it as done on June 12, **Then** the next instance is created with due date June 13 (3 days from the original due, not from completion).
6. **(US-06.AS-06) Given** a recurring task, **When** user cancels it (status = cancelled), **Then** no further instances are generated.

### Edge Cases

- **EC-05 — Recurring task early completion**: If a recurring task is completed before its due date, no new instance is spawned immediately. The system generates the deferred instance on the next application startup on or after the due date, or during periodic checks while the app is running.

## Requirements *(mandatory)*

### Functional Requirements (slice-specific)

- **FR-007**: System MUST support recurring tasks with the following schedules: daily, every N days, specific weekdays, monthly.
- **FR-008**: When a recurring task instance is marked as done (on or after its due date), the system MUST automatically generate the next instance with the appropriate due date according to the recurrence rule. The new instance MUST carry forward all fields from the completed instance (title, description, priority, labels, project) except: status (reset to backlog), timestamps (new created_at, no completed_at), and cycle assignment (new instance is unassigned to any cycle — the user assigns it manually).
- **FR-009**: When a recurring task instance is marked as done before its due date, the system MUST NOT generate the next instance immediately. The next instance MUST be generated when the application is opened on or after the original due date (checked at application startup and periodically while running).
- **FR-010**: When a recurring task is cancelled, the system MUST stop generating further instances.

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

This slice introduces and owns **ENT-05 — Recurrence Rule**. It populates the `recurrence rule` attribute of **ENT-01 — Task** (owned by slice 001), which was reserved there as a nullable, forward-compatible column.

- **ENT-05 — Recurrence Rule**: Defines a repeating schedule attached to a task. Specifies frequency type (daily, every N days, specific weekdays, monthly) and parameters. Used to generate the next task instance upon completion.

## Success Criteria *(mandatory)*

### Measurable Outcomes

This slice introduces no new slice-specific success criteria. The measurable outcomes that apply here — SC-003 (optimistic UI: the optimistic result is painted within 16ms of the triggering keypress while the server reconciles or rolls back asynchronously) and SC-004 (no third-party runtime data services — only the application's own API and PostgreSQL database) — are owned by slice 001; how they are realized by this slice's recurrence computation and next-instance generation is described under Constitution Compliance below.

## Constitution Compliance

This slice is evaluated against constitution v2.0.0. Cross-cutting principles realized here:

- **I. Keyboard-First**: setting a recurrence rule in the task editor and completing an instance (`Space`, owned by slice 004) are keyboard-driven; the recurring visual indicator (US-06.AS-01) is discoverable without the mouse.
- **II. Accessibility (WCAG 2.1 AA)**: FR-042 (focus indicator), FR-043 (ARIA roles/labels), FR-044 (contrast ≥ 4.5:1), FR-045 (no AT-binding collisions), FR-046 (no hover-only content — the recurring indicator and any recurrence controls have keyboard/focus-triggered equivalents), FR-047 (prefers-reduced-motion). FR-031 keeps single-key shortcuts from hijacking text entry in the recurrence-rule fields.
- **III. Instant Response (Optimistic UI)**: marking an instance done paints the successor optimistically within one animation frame (SC-003, owned by slice 001), while the C# API confirms or reconciles the generation asynchronously against a p95 < 200ms server budget; rollback is applied if the server rejects the operation. Skeleton/loading states are permitted (Principle IV) where the successor or its reconciled state has not yet been confirmed.
- **V. Connected, Server-Authoritative**: recurrence computation and next-instance generation are performed and persisted through the application's own C# API into PostgreSQL, which is the source of truth; the Next.js client surfaces the successor via optimistic UI and reconciles against the server. The on/after-due check (at startup and periodically) drives generation through that same API. The only data dependency is the application's own API and database — no third-party runtime data services (SC-004, owned by slice 001).
- **VI. Type Safety End-to-End**: the Recurrence Rule (ENT-05) frequency type and parameters are a typed model; the recurrence boundary is validated at runtime (user input in the editor and deserialization from storage).
- **VII. Data Integrity & Resilience**: FR-008's deterministic carry-forward and resets prevent silent data loss across instance generation; FR-049 surfaces any generation error with a recovery suggestion and FR-050 logs it with structured context; FR-051 keeps the backup hook in place ahead of the schema change that adds the recurrence rule.
- **VIII. Test-First**: each owned acceptance scenario above, plus EC-05, is independently testable (Red-Green-Refactor).

## Assumptions

- **ASM-09 — Recurrence based on due date**: Next recurring task instance is calculated from the original due date, not the completion date, ensuring consistent scheduling.

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

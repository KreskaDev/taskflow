# Feature Specification: Natural-Language Dates

**Feature Branch**: `003-natural-language-dates`

**Created**: 2026-06-13

**Status**: Draft

**Input**: Slice 003 of the TaskFlow MVP. Source of truth: `.specify/memory/product-vision.md`. Goal: parse Polish natural-language date expressions during task capture so a typed phrase sets the due date, with a clear, recoverable error when the phrase cannot be understood. This completes the capture journey begun in slice 002.

## Provenance

This slice realizes the following items from product-vision.md:

Slice-specific:
- US-01 (Daily Task Capture) — completes partial: AS-02, AS-03, AS-04, AS-05
- FR-005 (parse Polish natural-language date expressions)
- FR-006 (parser-failure UX: retain previous value + "nie rozpoznano")
- FR-092 (time rule: UTC storage, Europe/Warsaw reference zone, `due_date` `has_time` flag, DST via library) — owned by this slice
- EC-02 (natural-language date parsing failure)
- ASM-03 (Polish language UI for date parsing)
- ASM-12 (instance reference timezone Europe/Warsaw; per-user timezones out of scope) — owned by this slice

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

Access control (realized in this slice) (per Constitution Principle IX):
- FR-065 (authorization dispatched by containing-resource visibility; per-user isolation for personal/unprojected data)
- FR-066 (shared-project access requires current membership; createdBy/assignee are provenance only)
- FR-067 (sufficient-role gate on shared-project operations)
- FR-068 (deny-by-default, enforced at the API/handler layer for every read and write)

MVP boundary confirmed:
- OOS-01..OOS-19 (full MVP out-of-scope confirmation)

Entity touchpoint:
- ENT-01 (Task) — this slice populates the `due_date` attribute (the Task entity is owned by slice 002)

Depends on:
- Slice 002 (task-capture) — provides the capture input, the Task entity, and server-side persistence

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Daily Task Capture (Priority: P1)

User opens the application and immediately captures a new task using only the keyboard. They press `C`, type a task title with an optional natural language date (e.g., "Kupic mleko po 17"), and press Enter. The task lands in the Inbox with the parsed due date. The entire flow completes in under 3 seconds without touching the mouse.

**Why this priority**: Capture is the most frequent action in any task manager. If adding a task is slow or friction-heavy, users abandon the tool. This is the atomic unit of value.

**Independent Test**: Can be fully tested by launching the app, pressing `C`, typing a task with a date phrase, pressing Enter, and verifying the task appears with the correct due date; and by typing an unrecognized phrase and verifying the recoverable error behavior.

> Scope note: plain capture and cancellation (US-01.AS-01, AS-06, AS-07) are delivered in slice 002. This slice adds the natural-language date parsing scenarios below, completing US-01.

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
- **FR-092**: All timestamps MUST be stored in UTC, and every date-relative computation (Today/Upcoming membership, cycle boundaries, recurrence rollover, natural-language date resolution) MUST be evaluated against a single instance reference timezone, `Europe/Warsaw`, applied identically on client and server. A due date MUST distinguish date-only from date-time via a `has_time` flag, and DST transitions MUST be handled by the timezone library, not fixed-offset arithmetic.

#### Parser responsibility split (client interprets, server validates)

The Polish natural-language parser runs **client-side**: it interprets the typed phrase against the `Europe/Warsaw` reference zone (FR-092) so the parsed due date can be painted optimistically within 16ms (SC-003), then resolves it to a concrete instant and sends a **resolved ISO-8601 timestamp + UTC offset** (plus the `has_time` flag) to the API. The server **validates** the resolved value (well-formed instant, plausible range, type-checks at the trust boundary per Principle VI) and persists it in UTC; the server **does NOT re-parse Polish natural-language text**. "Resolution against `Europe/Warsaw`" means a phrase like "po 17" or "jutro" is computed in that zone on the client and the server records the equivalent UTC instant with the `has_time` flag. "Equal to today" (for downstream Today-view membership) means the **same calendar day in `Europe/Warsaw`**, evaluated identically on client and server, with DST handled by the timezone library.

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

Authorization is **dispatched by the containing resource's visibility**, not by a conjunction of tiers. Natural-language date capture writes the `due_date` of a task: for a personal/unprojected task the write authorizes on **ownership** (`createdBy`); for a task in a shared project it authorizes on **current `ProjectMembership` + sufficient role** (editor/owner to write). `createdBy` and assignee are **provenance only** and confer no standalone access; on leave/remove/unshare a user loses ALL access to that project's data regardless of authorship or assignment.

- **FR-065**: Authorization MUST be dispatched by the containing resource's visibility (not a conjunction of tiers): personal/unprojected data authorizes on ownership (`createdBy`/`ownerId`) with queries scoped to the caller; shared-project entities authorize on current `ProjectMembership` + role. Every query MUST be scoped accordingly (per-user isolation).
- **FR-066**: Access to a shared project's data MUST require current membership in that project. `createdBy` and assignee are provenance only and confer NO standalone access; on leave/remove/unshare a user MUST lose ALL access to that project's data regardless of authorship or assignment.
- **FR-067**: Each operation on a shared-project resource MUST require sufficient role (viewer=read, editor=write, owner=manage); insufficient role MUST be denied.
- **FR-068**: Authorization MUST be deny-by-default and enforced at the API/handler layer for every read and write.

The slice's command and query handlers ENFORCE this at the handler layer (deny-by-default authorization on every read and write), not merely reference it: the parse-and-set-due-date command is authorized via dispatch-by-visibility — ownership for the caller's own personal task, membership + sufficient role for a shared-project task — and any query backing the date field is scoped accordingly. The server validates the resolved due date but performs no Polish natural-language parsing.

### Key Entities

This slice introduces no new entity. It populates the `due_date` attribute of **ENT-01 — Task** (owned by slice 002), which was reserved there as a nullable, forward-compatible column. For reference, the full Task definition from product-vision.md:

- **ENT-01 — Task**: The core work item. Has a title (required), description, priority (P0-P3), status (backlog/todo/in_progress/done/cancelled), due date, labels, project reference, cycle reference, recurrence rule, createdBy (the User who created it), assignees (zero or more Users; only on shared-project tasks), and system timestamps (created_at, updated_at, completed_at). New tasks default to "backlog" status. A recurring task has a linked recurrence rule that generates successor instances.

## Success Criteria *(mandatory)*

### Measurable Outcomes

This slice introduces no new slice-specific success criteria. The relevant measurable outcomes are owned by slice 002 and continue to apply: SC-003 (every user action paints its optimistic result within 16ms of the triggering keypress; the server reconciles or rolls back asynchronously — here, the parsed due date is painted optimistically within 16ms while the C# API persists it and the client reconciles) and SC-004 (depends on no third-party runtime data services — only its own API and PostgreSQL database).

## Constitution Compliance

This slice is evaluated against constitution v4.0.0. Cross-cutting principles realized here:

- **I. Keyboard-First**: date entry is part of the keyboard capture flow — the user types the phrase inline and presses Enter; no mouse interaction is required.
- **II. Accessibility (WCAG 2.1 AA)**: the FR-006 error ("nie rozpoznano") is conveyed via an ARIA-live region and meets contrast requirements (FR-044), not by color alone; FR-031, FR-042, FR-043, FR-045, FR-046, FR-047 carry from the capture UI. Per the strengthened status-message and dialog-focus requirements (FR-101): any server-initiated update or toast surfaced in this slice (e.g., a rollback message when the server rejects a resolved due date) MUST be announced via a polite ARIA live region without stealing focus, and any confirmation/picker dialog opened in the capture flow MUST follow the dialog focus contract (set initial focus, trap focus, dismiss on Esc, return focus to the invoker).
- **III. Instant Response**: the parsed due date is painted optimistically within one animation frame (under 16ms of the keypress) while the C# API persists the change and the client reconciles or rolls back asynchronously; server-confirmed mutations MUST complete within a p95 under 200ms for single-entity writes, and skeleton screens are permitted for network-bound loads (SC-003, owned by slice 002). This slice initiates no server-pushed updates, but its optimistic-then-reconcile model is the same one real-time reconciliation builds on: an inbound remote patch yields to a pending local optimistic mutation until that mutation's server-ack resolves, then reconciles under last-write-wins.
- **IV. Minimalist UI**: the date-parse feedback is purposeful and minimal; skeleton screens are permitted for genuine network-bound loads but MUST NOT mask a mutation whose optimistic result (the parsed due date) could be shown immediately.
- **V. Connected, Server-Authoritative**: the Polish natural-language parser interprets the typed phrase **client-side** (input interpretation, for optimistic paint); the client resolves it to a concrete ISO-8601 instant + UTC offset and the resulting `due_date` write is server-authoritative — the server validates the resolved value and persists it in UTC in PostgreSQL through the C# API, which is the system of record. The server does NOT re-parse Polish natural-language text. Network connectivity is required for normal operation, and the app depends on no third-party runtime data services (SC-004, owned by slice 002). The single permitted external runtime dependency is Google OAuth, for authentication only.
- **VI. Type Safety End-to-End**: the parser returns a typed result (a resolved date, or an "unrecognized" outcome that drives FR-006); the resolved due date + `has_time` flag crosses the boundary in the generated typed contract and is validated at runtime (Zod on the web, FluentValidation/data annotations at the API), and the error contract is ProblemDetails-based (ADR-0009).
- **VII. Data Integrity & Resilience**: FR-006 is the in-flow recovery for a parse failure (retain prior value, no silent loss), satisfying FR-049; FR-050 logs the failure with context; FR-051 keeps the automatic pre-migration backup (pg_dump or a managed database snapshot) in place ahead of the EF Core schema change that adds `due_date` and its `has_time` flag.
- **VIII. Test-First**: each owned acceptance scenario above, plus EC-02, is independently testable (Red-Green-Refactor), with integration tests exercising the command/query handlers through the real database including authorization — every data handler MUST ship both an allow test and a deny test (a request without the required ownership/membership/role MUST be denied).
- **IX. Authentication & Authorization**: every request is authenticated and every data operation is authorized **deny-by-default at the API/handler layer**, dispatched by the containing resource's visibility (NOT a Tier A/B conjunction). Setting a task's `due_date` authorizes on **ownership** for the caller's own personal/unprojected task and on **current `ProjectMembership` + sufficient role** (editor/owner) for a shared-project task (FR-065, FR-067); `createdBy`/assignee are provenance only and confer no standalone access, and on leave/remove/unshare a user loses ALL access regardless of authorship (FR-066); queries are scoped accordingly (FR-068). Admission to the instance is gated (allowlist / Google Workspace `hd`) and sessions follow the documented policy, both established in slice 001.
- **X. Time & Timezone**: this slice owns the time rule (FR-092, ASM-12). Timestamps are stored in UTC; the natural-language date resolution and all downstream date-relative computation (Today/Upcoming membership, cycle boundaries, recurrence rollover) are evaluated against the single instance reference timezone `Europe/Warsaw`, applied identically on client and server; the `due_date` carries a `has_time` flag distinguishing date-only from date-time; DST transitions are handled by the timezone library, never by fixed-offset arithmetic. "Equal to today" = same calendar day in `Europe/Warsaw`. Per-user timezones are out of scope (OOS-19).
- **XI. Privacy & Personal Data**: this slice adds no new personal-data store — it writes the `due_date` of an existing Task — so it relies on the account-deletion/erasure cascade and the "retained until account deletion" stance defined at the product level (FR-085/FR-086); a deleted user's tasks (and their due dates) follow that cascade.
- **XII. Security by Default**: the typed phrase is untrusted input; the resolved due date is validated/type-checked at the trust boundary (Principle VI) and no user-authored free text from this flow is rendered as raw HTML. Secrets remain runtime-injected and never logged; the structured parse-failure logging (FR-050) MUST NOT include secrets.

## Assumptions

- **ASM-03 — Polish language UI for date parsing**: Natural language date input supports Polish expressions as the primary language. Error messages related to date parsing (e.g., "nie rozpoznano") are in Polish. Additional language support may be added later but is not required for MVP.
- **ASM-12 — Instance reference timezone**: The instance operates against a single reference timezone, `Europe/Warsaw`, for all date-relative computation; per-user timezones are out of scope.

## Out of Scope

This slice confirms the full MVP out-of-scope boundary (OOS-01..OOS-19 from product-vision.md):

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
- **OOS-18**: Pending / pre-account invitations (invites are by email resolved against existing signed-in Users only)
- **OOS-19**: Per-user timezones (the instance uses a single reference timezone, ASM-12)

Also out of scope for this slice specifically (deferred to later slices): rescheduling an existing task's date via the `T` shortcut from a list/Today view (US-02.AS-05) is owned by slice 005 (daily-planning); recurrence-rule date computation (US-06 / ASM-09) is owned by slice 012 (recurring-tasks). This slice covers natural-language date parsing only at capture time.

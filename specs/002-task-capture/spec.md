# Feature Specification: Task Capture

**Feature Branch**: `002-task-capture`

**Created**: 2026-06-13

**Status**: Draft

**Input**: Slice 002 of the TaskFlow MVP. Source of truth: `.specify/memory/product-vision.md`. Goal: keyboard capture of tasks into a single task list, with core navigation, completion, inline rename, deletion, and help — the atomic unit of value, with the accessibility and resilience foundation in place.

## Provenance

This slice realizes the following items from product-vision.md:

Slice-specific:
- US-01 (Daily Task Capture) — partial: AS-01, AS-06, AS-07
- US-08 (Keyboard Navigation & Shortcuts) — subset: AS-03, AS-07, AS-09
- FR-001 (create task with mandatory title)
- FR-002 (optional task fields, incl. assignees on shared-project tasks — `createdBy` set here; assignees populated by task-assignment, slice 008)
- FR-003 (status enum; default "backlog")
- FR-004 (created_at / updated_at / completed_at timestamps)
- FR-041 (server-side persistence via the app's own API)
- EC-01 (empty Inbox state)
- EC-06 (10,000+ tasks performance / virtualization)
- EC-08 (single-key shortcuts suppressed in text inputs)
- SC-002 (FCP < 1s, TTI < 2.5s from a warm backend)
- SC-003 (16ms optimistic keypress-to-paint; async server reconcile)
- SC-004 (no third-party runtime data services)
- SC-007 (strict type safety)
- SC-010 (60fps client-side virtualized list at 10,000 items)
- SC-011 (< 300MB browser tab memory at 10,000 tasks)
- SC-012 (server data operations p95 < 200ms)
- ENT-01 (Task)
- ASM-01 (multi-user team), ASM-02 (web platform), ASM-05 (no subtasks), ASM-06 (in-app notifications only), ASM-08 (data format)

Cross-cutting (realized in this slice):
- FR-065 (per-user isolation — first realized here; every Task query/command is scoped to the caller as its `createdBy`)
- FR-031 (suppress single-key shortcuts in text inputs)
- FR-042 (visible focus indicator)
- FR-043 (ARIA roles/labels)
- FR-044 (text contrast ≥ 4.5:1)
- FR-045 (no collision with assistive-technology bindings)
- FR-046 (no hover-only content)
- FR-047 (prefers-reduced-motion)
- FR-049 (error message + recovery action)
- FR-050 (structured error logging)
- FR-051 (auto-backup before migration — infrastructure in place, no-op at v1)

MVP boundary confirmed:
- OOS-01..OOS-17 (full MVP out-of-scope confirmation)

Exercised-but-not-owned (mechanics built here; canonical acceptance scenarios live in later slices, counted there):
- `Space` toggle-done mechanic — canonical scenario US-02.AS-03 owned by slice 005 (daily-planning)
- `E` inline-edit mechanic — canonical scenario US-02.AS-06 owned by slice 005 (daily-planning); superseded by the full editor there
- `Del` delete mechanic — canonical scenario US-08.AS-06 owned by slice 014 (undo); here deletion is permanent (see Constitution Compliance)

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Daily Task Capture (Priority: P1)

User opens the application and immediately captures a new task using only the keyboard. They press `C`, type a task title, and press Enter. The task lands in the list. The entire flow completes in under 3 seconds without touching the mouse.

**Why this priority**: Capture is the most frequent action in any task manager. If adding a task is slow or friction-heavy, users abandon the tool. This is the atomic unit of value.

**Independent Test**: Can be fully tested by launching the app, pressing `C`, typing a task title, pressing Enter, and verifying the task appears in the list. Cancellation is tested by pressing Esc instead of Enter.

> Scope note: this slice realizes capture without date parsing. Natural-language date scenarios (US-01.AS-02..AS-05) are delivered in slice 003 (natural-language-dates).

**Acceptance Scenarios** (owned by this slice):

1. **(US-01.AS-01) Given** the app is open on any view, **When** user presses `C`, **Then** a task creation input appears with focus on the title field within 16ms of keypress.
2. **(US-01.AS-06) Given** the task creation input is focused, **When** user types a task title without any date expression and presses Enter, **Then** the task is created with no due date.
3. **(US-01.AS-07) Given** the task creation input is focused, **When** user presses Esc, **Then** creation is cancelled, no task is created, and focus returns to the previous view.

---

### User Story 8 - Keyboard Navigation & Shortcuts (Priority: P1)

User navigates the single task list and operates on the selected task using keyboard shortcuts only. The shortcuts available in this slice are: `C` (create), `↑/↓` (navigate the list), `Space` (toggle done), `E` (edit title inline), `Esc` (cancel), `Del` (delete), and `?` (shortcuts help overlay).

**Why this priority**: Keyboard-first is the core principle. Without complete keyboard coverage, the app fails its primary promise.

**Independent Test**: Can be tested by navigating the list with arrows, opening the help overlay with `?`, and verifying that single-key shortcuts typed inside the capture input are entered as text rather than interpreted as commands.

**Acceptance Scenarios** (owned by this slice):

1. **(US-08.AS-03) Given** any list view is open, **When** user presses up/down arrows, **Then** selection moves between tasks with visible focus indicator.
2. **(US-08.AS-07) Given** the app is open, **When** user presses `?`, **Then** a keyboard shortcuts help overlay appears listing all available shortcuts.
3. **(US-08.AS-09) Given** the user is typing in a text input (task title, search, etc.), **When** they press a shortcut key (e.g., `C`, `E`, `1`), **Then** the character is typed into the input, not interpreted as a shortcut.

> Shortcut coverage: the `Space`, `E`, and `Del` mechanics are implemented in this slice, but their canonical acceptance scenarios are owned by later slices (US-02.AS-03 and US-02.AS-06 → slice 005; US-08.AS-06 → slice 014). See Provenance.

### Edge Cases

- **EC-01 — Empty Inbox**: When the Inbox has no tasks, a helpful empty state is shown with a hint to press `C` to create a task.
- **EC-06 — 10,000+ tasks performance**: List views use virtualization to maintain 60fps scrolling. Search returns results in under 50ms regardless of dataset size.
- **EC-08 — Keyboard shortcuts in text inputs**: When a text input is focused, single-key shortcuts (C, E, 1-4, etc.) are treated as text input, not commands. Only modifier-based shortcuts (Ctrl+K, Ctrl+Enter) remain active.

## Requirements *(mandatory)*

### Functional Requirements (slice-specific)

- **FR-001**: System MUST allow creating a task with a mandatory title field.
- **FR-002**: System MUST support optional task fields: description (markdown), priority (P0/P1/P2/P3), due date, labels (multiple), project assignment, and assignees (shared-project tasks only).

  > Slice scope for FR-002: this slice sets `createdBy` (the creating User, required) on every task. The remaining optional fields (description, priority, due date, labels, project assignment, assignees) are reserved as forward-compatible columns and are populated by their owning slices; assignees are introduced by task-assignment (slice 008) and apply only to shared-project tasks.
- **FR-003**: System MUST track task status as one of: backlog, todo, in_progress, done, cancelled. New tasks MUST default to "backlog" status.
- **FR-004**: System MUST automatically record `created_at`, `updated_at`, and `completed_at` timestamps on tasks.
- **FR-041**: All task data MUST be persisted server-side in PostgreSQL through the application's own API; the client holds no authoritative copy. The application MUST depend on no third-party runtime data service — only its own API and database.

### Cross-cutting Requirements (realized in this slice)

Access control (realized in this slice) (per Constitution Principle IX):
- **FR-065**: Every query MUST be scoped to data the caller owns or has membership access to (per-user isolation).

This slice is Tier A (per-user isolation only): tasks are owned by their creator (`createdBy`) and are never shared, so only FR-065 applies — the membership/role requirements (FR-066, FR-067, FR-068) are not exercised here and are first realized in the Tier B sharing slices. The slice's command and query handlers ENFORCE FR-065 directly (handler-level, deny-by-default): every read and write scopes its data to the authenticated caller as the task's `createdBy`. This is not merely a reference — there is no code path that reads or mutates a task belonging to another user.

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

  > Slice scope for FR-051: the automatic backup created before a data migration is a local backup of user data.

### Key Entities

- **ENT-01 — Task**: The core work item. Has a title (required), description, priority (P0-P3), status (backlog/todo/in_progress/done/cancelled), due date, labels, project reference, cycle reference, recurrence rule, createdBy (the User who created it), assignees (zero or more Users; only on shared-project tasks), and system timestamps (created_at, updated_at, completed_at). New tasks default to "backlog" status. A recurring task has a linked recurrence rule that generates successor instances.

> Slice scope for ENT-01: this slice persists `id`, `title`, `status`, `createdBy`, `created_at`, `completed_at`, and `updated_at`. `createdBy` is required and is the basis of per-user isolation (FR-065) realized here. The remaining attributes (description, priority, due date, labels, project reference, cycle reference, recurrence rule, assignees) are reserved as nullable, forward-compatible columns and are populated by their owning slices (003, 004, 005, 006, 011, 012; assignees by 008). Keeping the full status enum from day one avoids a later enum migration.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-002**: Application reaches first contentful paint in under 1 second and time-to-interactive in under 2.5 seconds on a broadband connection from a warm backend.
- **SC-003**: Every user action on a task (create, edit, complete, delete, move, reprioritize) paints its optimistic result within 16ms of the triggering keypress; the server reconciles or rolls back asynchronously.
- **SC-004**: Application depends on no third-party runtime data services — only its own API and PostgreSQL database; there are no external SaaS data dependencies at runtime.
- **SC-007**: Codebase enforces strict type safety with no bypasses, per Constitution Principle VI.
- **SC-010**: List views maintain smooth scrolling (60fps) with 10,000 items loaded via client-side virtualization.
- **SC-011**: Browser tab memory usage stays below 300 MB with 10,000 tasks loaded.
- **SC-012**: Server data operations (single-entity reads/writes) complete within a p95 of 200ms against a representative dataset.

## Constitution Compliance

This slice is evaluated against constitution v3.0.0. Cross-cutting principles realized here:

- **I. Keyboard-First**: every action in this slice (create, navigate, toggle done, inline rename, delete, help) is keyboard-driven; the `?` overlay (US-08.AS-07) makes shortcuts discoverable.
- **II. Accessibility (WCAG 2.1 AA)**: FR-042 (focus indicator), FR-043 (ARIA roles/labels), FR-044 (contrast ≥ 4.5:1), FR-045 (no AT-binding collisions), FR-046 (no hover-only content), FR-047 (prefers-reduced-motion). FR-031 keeps single-key shortcuts from hijacking text entry.
- **III. Instant Response**: SC-003 (optimistic UI paints the result within 16ms of keypress, with the server reconciling or rolling back asynchronously), SC-012 (server data operations within a p95 of 200ms), SC-010 (60fps client-side virtualized list); skeleton/loading states are permitted while the backend responds (per Principle IV; SC-002 first contentful paint < 1s, time-to-interactive < 2.5s from a warm backend). Real-time reconciliation of server-initiated (SignalR) updates does not apply in this slice — tasks here are per-user, never shared — and is first realized by real-time-collaboration (slice 016).
- **V. Connected, Server-Authoritative**: FR-041 and SC-004 — task data is persisted server-side in PostgreSQL through the application's own API, which is the system of record; the client holds no authoritative copy, and the app depends on no third-party runtime data service. ASM-08 (documented, inspectable PostgreSQL relational schema; export/import keeps data portable).
- **VI. Type Safety End-to-End**: SC-007; the EF Core / PostgreSQL schema is the source of truth, with C# entity types on the server and TypeScript types on the Next.js client kept in lockstep, and runtime validation at the title-input and API request/response boundaries.
- **VII. Data Integrity & Resilience**: FR-049 (error + recovery), FR-050 (structured logging), FR-051 (auto-backup infrastructure — no-op at v1 since there is no prior schema to migrate, but the hook and restore path are in place).
- **VIII. Test-First**: each owned acceptance scenario above is independently testable (Red-Green-Refactor); per Principle VIII, the integration tests for this slice's command/query handlers include authorization — a request for a task that is not the caller's `createdBy` MUST be denied.
- **IX. Authentication & Authorization**: authorization is deny-by-default and enforced at the handler layer for every read and write (FR-068 posture). This slice is Tier A, so it authorizes by per-user isolation alone (FR-065): every task command and query is scoped to the authenticated caller as the task's `createdBy`, and no path reads or mutates another user's task. Membership and role checks (FR-066, FR-067) do not apply here — tasks are personal and never shared — and are first realized in the Tier B sharing slices (project-sharing-membership, slice 007 onward). Authentication itself (Google OAuth sign-in, sessions) is owned by accounts-and-auth (slice 001); this slice consumes the authenticated caller's identity.

**Known compliance gap (deferred, accepted at slicing time):** Principle VII requires destructive actions to be undoable for ≥ 30 seconds (FR-040). In this slice, `Del` performs a permanent delete — the 30-second undo window is delivered in slice 014 (undo), which retrofits undo onto this slice's delete path. No undo is added here, per this slice's definition.

## Assumptions

- **ASM-01 — Multi-user team**: The application serves a small collaborating team (~10). Each user authenticates (Google) and has personal data plus access to shared projects.
- **ASM-02 — Web platform**: The MVP targets modern desktop browsers. Native mobile apps, PWA/offline operation, and cross-device sync are explicitly out of scope.
- **ASM-05 — No subtasks**: Tasks are flat entities. Only projects support hierarchy (one level). Task nesting (subtasks) is explicitly out of scope.
- **ASM-06 — In-app notifications only**: The app provides in-app notifications (assignment, mention, changes); email and push/device notifications and reminders are out of scope.
- **ASM-08 — Data format**: The relational schema in PostgreSQL is documented and inspectable; full export/import (Principle VII) keeps user data portable, consistent with the data-sovereignty principle.

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

Additionally deferred to later slices within the MVP (not part of this slice): natural-language dates (003), projects (004), priorities/Today/Upcoming/full editor (005), labels (006), project sharing & membership (007), task assignment (008), comments & @mentions (009), Kanban board (010), cycles (011), recurring tasks (012), command palette & search (013), undo (014), data export/import (015), real-time collaboration (016), notifications (017), dark/light theming (018). Authentication and sign-in (accounts-and-auth, slice 001) precede this slice and provide the authenticated caller's identity that this slice's per-user isolation relies on.

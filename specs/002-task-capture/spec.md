# Feature Specification: Task Capture

**Feature Branch**: `002-task-capture`

**Created**: 2026-06-13

**Status**: Draft

**Input**: Slice 002 of the TaskFlow MVP. Source of truth: `.specify/memory/product-vision.md`. Goal: keyboard capture of tasks into a single task list, with core navigation, completion, inline rename, deletion (soft-delete from day one), and help — the atomic unit of value, with the accessibility, error-contract, and resilience foundation in place.

## Provenance

This slice realizes the following items from product-vision.md:

Slice-specific:
- US-01 (Daily Task Capture) — partial: AS-01, AS-06, AS-07
- US-08 (Keyboard Navigation & Shortcuts) — subset: AS-03, AS-07, AS-09
- FR-001 (create task with mandatory title)
- FR-002 (optional task fields, incl. assignees on shared-project tasks — `createdBy` set here; assignees populated by task-assignment, slice 008)
- FR-003 (status enum; default "backlog")
- FR-004 (created_at / updated_at / completed_at timestamps)
- FR-102 (manual task reordering via a persisted `position`) — owned/first realized here
- FR-041 (server-side persistence via the app's own API)
- FR-093 (ProblemDetails RFC 9457 error contract) — owned/first realized here
- FR-097 (soft-delete for undoable deletions) — owned/first realized here (shipped from day one)
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
- SC-013 (authorization enforced on 100% of data operations)
- SC-016 (authorization coverage mechanically verifiable — allow+deny test per handler)
- ENT-01 (Task)
- ASM-01 (multi-user team), ASM-02 (web platform), ASM-05 (no subtasks), ASM-06 (in-app notifications only), ASM-08 (data format), ASM-12 (instance reference timezone Europe/Warsaw)

Cross-cutting (realized in this slice):
- FR-065 (dispatch-by-visibility — first realized here; this slice's tasks are personal/unprojected, so authorization dispatches to ownership and every Task query/command is scoped to the caller as its `createdBy`)
- FR-066 (provenance-only + revoke-all-on-leave — confirmed: `createdBy`/assignee confer no standalone access; no shared-project path exists in this slice, so revoke-all-on-leave is not exercised here)
- FR-067 (role-sufficiency on shared-project resources — not exercised here; no shared-project resource in this slice)
- FR-068 (deny-by-default, enforced at the API/handler layer for every read and write)
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
- FR-051 (auto-backup before migration — infrastructure in place, no-op at v1)
- FR-092 (time rule — UTC storage; Europe/Warsaw reference zone; due_date `has_time` flag reserved on the Task model)

MVP boundary confirmed:
- OOS-01..OOS-19 (full MVP out-of-scope confirmation)

Exercised-but-not-owned (mechanics built here; canonical acceptance scenarios live in later slices, counted there):
- `Space` toggle-done mechanic — canonical scenario US-02.AS-03 owned by slice 005 (daily-planning)
- `E` inline-edit mechanic — canonical scenario US-02.AS-06 owned by slice 005 (daily-planning); superseded by the full editor there
- `Del` delete mechanic — canonical scenario US-08.AS-06 owned by slice 014 (undo); here deletion is a soft-delete (`deleted_at` tombstone, reaped after the 30s window) so slice 014 is a pure retrofit, but the user-facing 30-second undo *toast/restore UX* is delivered in slice 014 (see Constitution Compliance)

## Clarifications

### Session 2026-06-18

- Q: Event-sourcing (Marten) vs state-stored persistence for the tasks module? → A: Keep state-stored EF Core / CRUD (ADR-0003 Decision 5) — no event store, no aggregate rewind; consistent with slice 001.
- Q: How are concurrent writes to a task handled (FR-093 lists a 409; the spec also says last-write-wins)? → A: Optimistic concurrency via a day-one `version` token on the Task row; a stale write is rejected with `409` ProblemDetails (the client refetches and reapplies). Concurrent task *edits* are NOT last-write-wins.
- Q: What is the maximum task-title length? → A: 500 characters (empty rejected; > 500 → `validation_failed`), enforced by Zod (client) and FluentValidation (API).
- Q: What is the default task-list ordering, and what sort options exist? → A: Ordered by a persisted `position` (seeded newest-first, consistent with FR-021), with **manual reordering** in scope — newly allocated as **FR-102** in product-vision and realized here. Sorting by `priority` / `due date` is OUT of this slice (those fields land in slices 003/005).
- Q: How is the `Space` toggle-done status transition defined (and `completed_at`)? → A: done ↔ backlog. Completing sets `status = done` + `completed_at = now`; un-completing returns `status = backlog` (the only non-done status reachable here) and clears `completed_at`.
- Q: How is the create-task API made idempotent? → A: The client generates the task's UUIDv7 `id` and sends it; create is insert-if-not-exists by that `id` (idempotent on retry, and serves optimistic UI since the client knows the id immediately).
- Q: For a single-item op on a task the caller doesn't own, is the response 403 or 404? → A: **404 `not_found`** (existence-disclosure guard — never leak that another user's id exists); `403 forbidden` is reserved for the shared-project insufficient-role case (slice 007+). Exception: a caller re-deleting their OWN already-soft-deleted task is the idempotent **204** no-op, not 404.

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

User navigates the single task list and operates on the selected task using keyboard shortcuts only. The shortcuts available in this slice are: `C` (create), `↑/↓` (navigate the list), `Alt+↑/↓` (manually reorder the selected task — FR-102; this exact chord is the **proposed default but PROVISIONAL** — Alt+Arrow has known browser/AT conflicts, e.g. Alt+Left/Right = browser back/forward and some screen-reader Alt+Arrow bindings, so the reorder binding is pending verification against the target browser + screen-reader matrix per FR-045 before `/speckit-tasks` freezes it), `Space` (toggle done), `E` (edit title inline), `Esc` (cancel), `Del` (delete), and `?` (shortcuts help overlay).

**Why this priority**: Keyboard-first is the core principle. Without complete keyboard coverage, the app fails its primary promise.

**Independent Test**: Can be tested by navigating the list with arrows, opening the help overlay with `?`, and verifying that single-key shortcuts typed inside the capture input are entered as text rather than interpreted as commands.

**Acceptance Scenarios** (owned by this slice):

1. **(US-08.AS-03) Given** any list view is open, **When** user presses up/down arrows, **Then** selection moves between tasks with visible focus indicator.
2. **(US-08.AS-07) Given** the app is open, **When** user presses `?`, **Then** a keyboard shortcuts help overlay appears listing all available shortcuts.
3. **(US-08.AS-09) Given** the user is typing in a text input (task title, search, etc.), **When** they press a shortcut key (e.g., `C`, `E`, `1`), **Then** the character is typed into the input, not interpreted as a shortcut.

> Shortcut coverage: the `Space`, `E`, and `Del` mechanics are implemented in this slice, but their canonical acceptance scenarios are owned by later slices (US-02.AS-03 and US-02.AS-06 → slice 005; US-08.AS-06 → slice 014). See Provenance.

### Edge Cases

- **EC-01 — Empty Inbox**: When the Inbox has no tasks, a helpful empty state is shown with a hint to press `C` to create a task.
- **EC-06 — 10,000+ tasks performance**: List views use virtualization to maintain 60fps scrolling. _(The "search returns results in under 50ms" clause is owned by slice 013 (command-palette & search) — there is no search surface in this slice; only the virtualized list performance applies here.)_
- **EC-08 — Keyboard shortcuts in text inputs**: When a text input is focused, single-key shortcuts (C, E, 1-4, etc.) are treated as text input, not commands. Only modifier-based shortcuts (Ctrl+K, Ctrl+Enter) remain active.

## Requirements *(mandatory)*

### Functional Requirements (slice-specific)

- **FR-001**: System MUST allow creating a task with a mandatory title field (non-empty after trimming; maximum **500 characters** — see FR-093 for the over-length rejection). The task `id` is a **client-generated UUIDv7** sent with the create request; the create is idempotent (insert-if-not-exists by `id`), which also lets the optimistic UI know the id immediately (SC-003).
- **FR-002**: System MUST support optional task fields: description (markdown), priority (P0/P1/P2/P3), due date, labels (multiple), project assignment, and assignees (shared-project tasks only).

  > Slice scope for FR-002: this slice sets `createdBy` (the creating User, required) on every task. The remaining optional fields (description, priority, due date, labels, project assignment, assignees) are reserved as forward-compatible columns and are populated by their owning slices; assignees are introduced by task-assignment (slice 008) and apply only to shared-project tasks.
- **FR-003**: System MUST track task status as one of: backlog, todo, in_progress, done, cancelled. New tasks MUST default to "backlog" status. The `Space` toggle-done mechanic transitions **done ↔ backlog** in this slice (the only non-done status reachable here): completing sets `status = done` and stamps `completed_at`; un-completing returns `status = backlog` and clears `completed_at`. (Later slices that introduce `todo`/`in_progress` may refine the un-complete target.)
- **FR-004**: System MUST automatically record `created_at`, `updated_at`, and `completed_at` timestamps on tasks (`completed_at` set on completion and cleared on un-completion, per FR-003).
- **Concurrency (slice rule, clarified 2026-06-18):** Task writes use **optimistic concurrency** via a `version` token on the row. A write carrying a stale `version` MUST be rejected with a `409` ProblemDetails (the client refetches and reapplies); concurrent task *edits* are NOT last-write-wins. This realizes FR-093's `409` failure mode over the FR-041 server-of-record. (The optimistic-*delete* rollback in FR-093/FR-097 is separate.)
- **FR-102** (manual ordering, realized here): The single task list is ordered by a persisted `position` field. `position` seeds to **newest-first** (so the just-captured task appears at the top, consistent with the Inbox default in product-vision FR-021), and the user may **manually reorder** tasks thereafter (keyboard reorder binding, Principle I); the manual `position` is the persisted order. A reorder is a normal optimistic write (SC-003) reconciled under the same optimistic-concurrency `version` rule. _Scope note: sorting by `priority` / `due date` is intentionally OUT for this slice (those fields are populated by slices 003/005); only manual `position` order ships here._
- **FR-041**: All task data MUST be persisted server-side in PostgreSQL through the application's own API; the client holds no authoritative copy. The application MUST depend on no third-party runtime data service — only its own API and database.

Error Contract (owned by this slice, per Constitution Principle VI / ADR-0009):
- **FR-093**: The API error contract MUST be ProblemDetails (RFC 9457) with a field-level validation extension and a stable machine-readable error-code enum, modeled by the generated TypeScript client and Zod.

  > Slice scope for FR-093: this slice establishes the ProblemDetails error contract as the foundation every later slice inherits. Each failure mode in this slice's surface — title validation (empty, or **over 500 characters** per FR-001), **404 (a single-item op on a task that is absent, soft-deleted, OR owned by another user — the existence-disclosure guard returns `not_found`, NOT `403`, so the id space is not an enumeration oracle; `403 forbidden` is reserved for the shared-project insufficient-role case that first exists in slice 007)**, **409 (a stale-`version` optimistic-concurrency conflict — see the Concurrency slice rule)**, 500, and network/offline — MUST map to a ProblemDetails response (or, for network/offline, a client-side equivalent) carrying a stable error-code and, for validation, field-level details; the generated client and Zod model it, and each maps to a clear FR-049 message + recovery action. The optimistic-delete rollback UX is owned here: when a soft-delete is rejected or rolled back by the server, the optimistically-removed row MUST reappear in place with an FR-049 message explaining the failure and the recovery (retry). On a 409, the client refetches the current row and reapplies the user's intent.

Soft-delete (owned by this slice, per Constitution Principle VII / IX):
- **FR-097**: Deletions that are undoable MUST be soft-deletes (a `deleted_at` tombstone), excluded from authorization-scoped queries, and reaped after the 30-second undo window elapses.

  > Slice scope for FR-097: soft-delete ships from day one. `Del` performs a soft-delete (sets `deleted_at`); soft-deleted tasks are excluded from every authorization-scoped read (they never appear in the list or counts), and a server-side reaper hard-deletes them after the 30-second window. This makes slice 014 (undo) a pure retrofit — it adds the user-facing 30-second undo toast and restore action on top of the tombstone, not a new delete path. Optimistic-delete rollback: the row is removed optimistically; if the server rejects the soft-delete the row reappears + an FR-049 message (see FR-093).

### Cross-cutting Requirements (realized in this slice)

Access control (realized in this slice) (per Constitution Principle IX):
- **FR-065**: Authorization MUST be dispatched by the containing resource's visibility (not a conjunction of tiers): personal/unprojected data authorizes on ownership (`createdBy`/`ownerId`) with queries scoped to the caller; shared-project entities authorize on current `ProjectMembership` + role. Every query MUST be scoped accordingly (per-user isolation).
- **FR-066**: Access to a shared project's data MUST require current membership in that project. `createdBy` and assignee are provenance only and confer NO standalone access; on leave/remove/unshare a user MUST lose ALL access to that project's data regardless of authorship or assignment.
- **FR-067**: Each operation on a shared-project resource MUST require sufficient role (viewer=read, editor=write, owner=manage); insufficient role MUST be denied.
- **FR-068**: Authorization MUST be deny-by-default and enforced at the API/handler layer for every read and write.

Authorization in this slice is dispatched by visibility, NOT by a conjunction of tiers. Every task here is personal/unprojected (no project reference yet), so the dispatch resolves to the ownership branch of FR-065: authorization is by `createdBy` and queries are scoped to the caller. `createdBy` is provenance, but in the ownership branch it is also the ownership key — there is no shared-project context in this slice for the provenance-only carve-out (FR-066) or role-sufficiency (FR-067) to bite; those are first exercised by the shared-project slices (project-sharing-membership, slice 007 onward), and the soft-delete tombstone (FR-097) is excluded from these authorization-scoped queries from day one. The slice's command and query handlers ENFORCE this directly (handler-level, deny-by-default per FR-068): every read and write scopes its data to the authenticated caller as the task's `createdBy`, and soft-deleted tasks are excluded. This is not merely a reference — there is no code path that reads or mutates a (non-deleted) task belonging to another user.

Accessibility (per Constitution Principle II):
- **FR-031**: Single-key shortcuts MUST be suppressed when a text input is focused; only modifier-based shortcuts remain active during text input.
- **FR-042**: Every focusable element MUST have a visible focus indicator.
- **FR-043**: All interactive elements MUST have correct ARIA roles and labels for screen reader compatibility.
- **FR-044**: Text contrast ratio MUST be at least 4.5:1 (3:1 for large text).
- **FR-045**: Custom keyboard shortcuts MUST NOT collide with native assistive-technology bindings.

  > Slice scope for FR-045: the bare single-key map (C, ↑/↓, Space, E, Esc, Del, ?) is collision-safe, but the **Alt+↑/↓ reorder chord (FR-102) is PROVISIONAL / TO-BE-VERIFIED**: Alt+Arrow has known conflicts (Alt+Left/Right = browser back/forward; some screen readers bind Alt+Arrow). The exact reorder binding MUST be validated against the target browser + screen-reader matrix (or a known-safe alternative chord chosen) BEFORE `/speckit-tasks` freezes it; `Alt+↑/↓` is the proposed default pending that verification.
- **FR-046**: No content may be accessible only via hover — all tooltips and popovers MUST have a keyboard/focus-triggered equivalent.
- **FR-047**: Animations MUST respect the `prefers-reduced-motion` user preference; when reduced motion is active, transitions MUST be instant or under 100ms.
- **FR-101**: Server-initiated updates and toasts MUST be conveyed to assistive technology via an appropriate ARIA live region without stealing focus, and confirmation/command-palette dialogs MUST follow the dialog focus contract (set initial focus, trap focus, dismiss on Esc, return focus to the invoker on close).

  > Slice scope for FR-101: this slice has no server-pushed real-time updates (tasks are personal, never shared — SignalR fan-out is first realized in slice 016), but it establishes the foundation. The `?` shortcuts-help overlay and the capture input MUST follow the dialog focus contract (initial focus set, focus trapped, Esc dismisses, focus returns to the invoker — already required for US-01.AS-07 and US-08.AS-07), and any client-side status/error toast (e.g. the optimistic-delete rollback message from FR-093) MUST be announced via a polite ARIA live region without stealing focus.

Error Handling & Data Integrity (per Constitution Principle VII):
- **FR-049**: All errors MUST be presented to the user with a clear message and an actionable recovery suggestion. No operation may fail silently.
- **FR-050**: Errors MUST be logged with structured context (severity level, operation context, and error details) for debugging purposes.
- **FR-051**: Before any data migration, the system MUST automatically create a backup of user data. The user MUST be able to restore from this backup.

  > Slice scope for FR-051: the automatic backup created before a data migration is a local backup of user data.

### Key Entities

- **ENT-01 — Task**: The core work item. Has a title (required), description, priority (P0-P3), status (backlog/todo/in_progress/done/cancelled), due date, labels, project reference, cycle reference, recurrence rule, createdBy (the User who created it), assignees (zero or more Users; only on shared-project tasks), and system timestamps (created_at, updated_at, completed_at). New tasks default to "backlog" status. A recurring task has a linked recurrence rule that generates successor instances.

> Slice scope for ENT-01: this slice persists `id`, `title`, `status`, `createdBy`, `created_at`, `completed_at`, `updated_at`, `deleted_at` (the soft-delete tombstone, FR-097, shipped from day one), `position` (the default-order field — see the Ordering slice rule), and `version` (the optimistic-concurrency token — see the Concurrency slice rule). The `id` is a **client-generated UUIDv7** (FR-001) — unlike slice 001's server-generated `UserId` — enabling idempotent create + optimistic UI. `createdBy` is required and is the ownership key that the dispatch-by-visibility authorization (FR-065) resolves to for these personal tasks. The remaining attributes (description, priority, due date with its `has_time` flag per FR-092, labels, project reference, cycle reference, recurrence rule, assignees) are reserved as nullable, forward-compatible columns and are populated by their owning slices (003, 004, 005, 006, 011, 012; assignees by 008). Keeping the full status enum, the `deleted_at` tombstone, and the `position` / `version` columns from day one avoids later enum/soft-delete/ordering/concurrency migrations.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-002**: Application reaches first contentful paint in under 1 second and time-to-interactive in under 2.5 seconds on a broadband connection from a warm backend.
- **SC-003**: Every user action on a task (create, edit, complete, delete, move, reprioritize) paints its optimistic result within 16ms of the triggering keypress; the server reconciles or rolls back asynchronously.
- **SC-004**: Application depends on no third-party runtime data services — only its own API and PostgreSQL database; there are no external SaaS data dependencies at runtime.
- **SC-007**: Codebase enforces strict type safety with no bypasses, per Constitution Principle VI.
- **SC-010**: List views maintain smooth scrolling (60fps) with 10,000 items loaded via client-side virtualization.
- **SC-011**: Browser tab memory usage stays below 300 MB with 10,000 tasks loaded.
- **SC-012**: Server data operations (single-entity reads/writes) complete within a p95 of 200ms against a representative dataset.
- **SC-013**: Authorization is enforced on 100% of data operations (no read/write bypasses the policy; deny cases covered by integration tests).
- **SC-016**: Authorization coverage is mechanically verifiable: every data handler ships with both an allow and a deny test, and a role×operation deny matrix demonstrates that insufficient ownership/membership/role is rejected.

  > Slice scope for SC-013/SC-016: every Task command/query handler in this slice ships with both an allow test (the caller reading/mutating their own `createdBy` task) and a deny test (a different user's task, and a soft-deleted task, are denied/excluded). The role×operation matrix dimension is exercised once shared projects exist (slice 007 onward); here the deny matrix covers the ownership branch.

## Constitution Compliance

This slice is evaluated against constitution v4.0.0. Cross-cutting principles realized here:

- **I. Keyboard-First**: every action in this slice (create, navigate, toggle done, inline rename, delete, help) is keyboard-driven; the `?` overlay (US-08.AS-07) makes shortcuts discoverable.
- **II. Accessibility (WCAG 2.1 AA)**: FR-042 (focus indicator), FR-043 (ARIA roles/labels), FR-044 (contrast ≥ 4.5:1), FR-045 (no AT-binding collisions — the bare single-key map is collision-safe; the **Alt+↑/↓ reorder chord is provisional/to-verify** against the browser + screen-reader matrix before `/speckit-tasks` freezes it, since Alt+Arrow has known browser/AT conflicts), FR-046 (no hover-only content), FR-047 (prefers-reduced-motion). FR-031 keeps single-key shortcuts from hijacking text entry. FR-101: although there are no server-pushed updates in this slice (no shared views), the dialog focus contract applies to the `?` help overlay and the capture input (initial focus, focus trap, Esc to dismiss, focus returned to the invoker on close — see US-01.AS-07, US-08.AS-07), and any client-side status/error toast (e.g. the optimistic-delete rollback message) is announced via a polite ARIA live region without stealing focus.
- **III. Instant Response**: SC-003 (optimistic UI paints the result within 16ms of keypress, with the server reconciling or rolling back asynchronously), SC-012 (server data operations within a p95 of 200ms — a MUST under Principle III), SC-010 (60fps client-side virtualized list); skeleton/loading states are permitted while the backend responds (per Principle IV; SC-002 first contentful paint < 1s, time-to-interactive < 2.5s from a warm backend). The optimistic-delete rollback path (FR-093/FR-097) follows this principle: the row is removed optimistically and, if the server rejects, reappears in place. Concurrent task **edits** are reconciled via optimistic concurrency — a stale `version` is rejected with a 409 and the client refetches and reapplies (see the Concurrency slice rule) — **not** last-write-wins. Real-time reconciliation of server-initiated (SignalR) updates does not apply in this slice — tasks here are personal, never shared — and is first realized by real-time-collaboration (slice 016).
- **V. Connected, Server-Authoritative**: FR-041 and SC-004 — task data is persisted server-side in PostgreSQL through the application's own API, which is the system of record; the client holds no authoritative copy, and the app depends on no third-party runtime data service (Google OAuth, owned by slice 001, is the one permitted external runtime dependency, for sign-in only). ASM-08 (documented, inspectable PostgreSQL relational schema; export/import keeps data portable).
- **VI. Type Safety End-to-End**: SC-007; the EF Core / PostgreSQL schema is the source of truth, with C# entity types on the server and TypeScript types on the Next.js client generated from the OpenAPI contract and kept in lockstep, runtime validation (Zod / FluentValidation) at the title-input and API request/response boundaries, and the ProblemDetails error contract (FR-093, ADR-0009) modeled by the generated client and Zod.
- **VII. Data Integrity & Resilience**: FR-049 (error + recovery, mapped from the ProblemDetails contract FR-093), FR-050 (structured logging; secrets never logged per Principle XII), FR-051 (auto-backup infrastructure — no-op at v1 since there is no prior schema to migrate, but the hook and restore path are in place). Soft-delete (FR-097) ships from day one: `Del` is a soft-delete (`deleted_at` tombstone) excluded from authorization-scoped queries and reaped after 30 seconds, so the deleted data is restorable within the window even though the user-facing undo toast/restore action lands in slice 014.
- **VIII. Test-First**: each owned acceptance scenario above is independently testable (Red-Green-Refactor); per Principle VIII, the integration tests for this slice's command/query handlers include authorization — every handler ships with both an allow test and a deny test, and a request for a task that is not the caller's `createdBy` (or for a soft-deleted task) MUST be denied (supports SC-013/SC-016).
- **IX. Authentication & Authorization**: authorization is deny-by-default and enforced at the API/handler layer for every read and write (FR-068), and is **dispatched by resource visibility, not a conjunction of tiers** (FR-065). Every task in this slice is personal/unprojected, so the dispatch resolves to the ownership branch: each command and query is scoped to the authenticated caller as the task's `createdBy`, and no path reads or mutates another user's task. `createdBy` here is provenance that doubles as the ownership key in the personal branch; the provenance-only carve-out and revoke-all-on-leave (FR-066) and role-sufficiency (FR-067) require a shared-project context and are first exercised in the shared-project slices (project-sharing-membership, slice 007 onward). Soft-deleted tasks (FR-097) are excluded from these authorization-scoped queries. Authentication itself (Google OAuth sign-in with admission gating per FR-087, session policy per FR-088, OAuth hardening per FR-090, BFF→API carrier per FR-091) is owned by accounts-and-auth (slice 001); this slice consumes the authenticated, admitted caller's identity. Live-subscription authorization (FR-095) does not apply (no SignalR here).
- **X. Time & Timezone**: timestamps (`created_at`, `updated_at`, `completed_at`, `deleted_at`) are stored in UTC; date-relative computation against the single instance reference timezone `Europe/Warsaw` (FR-092, ASM-12) and the due-date `has_time` flag are reserved on the model but not yet exercised — no date parsing or Today/Upcoming membership in this slice (first realized in slices 003/005). Per-user timezones are out of scope (OOS-19).
- **XI. Privacy & Personal Data**: this slice stores no Google PII beyond the `createdBy` reference to the User owned by slice 001. It establishes the **DB-level erasure link for tasks now**: `tasks.created_by → users(id)` is **`ON DELETE CASCADE`**, so slice-001 account deletion (which hard-deletes the User row) erases that user's personal tasks with it — complete erasure for personal/never-shared data (decided 2026-06-18; reattribution-to-`UserId.Tombstone` is reserved for *shared/thread-anchoring* content once sharing exists, slice 007+). The broader user-facing erasure feature/coordinator + data-retention stance (FR-085/FR-086, US-17) remain owned by slice 015; the soft-delete tombstone and undo-window data introduced here are covered by the "retained until account deletion" retention stance (FR-086).
- **XII. Security by Default**: the title is user-authored content and MUST be output-encoded/sanitized so raw HTML injection is impossible (FR-099 posture; full comment/markdown sanitization is owned by slice 009). A Content-Security-Policy and standard security response headers MUST be present in production (FR-099). Secrets MUST NOT appear in logs or error context (FR-100), consistent with FR-050 structured logging.

**Resolved (was a deferred gap):** Principle VII requires destructive actions to be undoable for ≥ 30 seconds (FR-040). This slice now ships soft-delete from day one (FR-097): `Del` sets a `deleted_at` tombstone, the row is excluded from authorization-scoped queries, and a reaper hard-deletes it after the 30-second window. The data is therefore restorable within the window from this slice forward; what slice 014 (undo) adds is the user-facing 30-second undo *toast and restore action* (US-08.AS-06) on top of this tombstone — a pure retrofit, not a new delete path.

## Assumptions

- **ASM-01 — Multi-user team**: The application serves a small collaborating team (~10). Each user authenticates (Google) and has personal data plus access to shared projects.
- **ASM-02 — Web platform**: The MVP targets modern desktop browsers. Native mobile apps, PWA/offline operation, and cross-device sync are explicitly out of scope.
- **ASM-05 — No subtasks**: Tasks are flat entities. Only projects support hierarchy (one level). Task nesting (subtasks) is explicitly out of scope.
- **ASM-06 — In-app notifications only**: The app provides in-app notifications (assignment, mention, changes); email and push/device notifications and reminders are out of scope.
- **ASM-08 — Data format**: The relational schema in PostgreSQL is documented and inspectable; full export/import (Principle VII) keeps user data portable, consistent with the data-sovereignty principle.
- **ASM-12 — Instance reference timezone**: The instance operates against a single reference timezone, `Europe/Warsaw`, for all date-relative computation; per-user timezones are out of scope.

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

Note: multi-user collaboration, sharing, and in-app notifications are IN scope for the MVP (OOS-01 promoted; OOS-06 partial) — this slice simply does not exercise them; they are realized in their owning slices. Authorization here dispatches by visibility and resolves to the ownership branch because every task in this slice is personal.

Additionally deferred to later slices within the MVP (not part of this slice): natural-language dates (003), projects (004), priorities/Today/Upcoming/full editor (005), labels (006), project sharing & membership (007), task assignment (008), comments & @mentions (009), Kanban board (010), cycles (011), recurring tasks (012), command palette & search (013), the user-facing 30-second undo toast/restore (014, retrofitting onto this slice's soft-delete), data export/import & account deletion (015), real-time collaboration (016), notifications (017), dark/light theming (018). Authentication and sign-in (accounts-and-auth, slice 001) precede this slice and provide the authenticated, admission-gated caller's identity that this slice's dispatch-by-visibility authorization relies on.

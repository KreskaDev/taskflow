# Feature Specification: Daily Planning

**Feature Branch**: `005-daily-planning`

**Created**: 2026-06-13

**Status**: Draft

**Input**: Slice 005 of the TaskFlow MVP. Source of truth: `.specify/memory/product-vision.md`. Goal: deliver the mouse-free daily loop — the Today and Upcoming views, task priorities, and a full task editor — so the user can review, triage, reprioritize, reschedule, and complete the day's work entirely from the keyboard. This builds on capture (002), natural-language dates (003), and projects (004).

## Provenance

This slice realizes the following items from product-vision.md:

Slice-specific:
- US-02 (Daily Planning Session) — full: AS-01, AS-02, AS-03, AS-04, AS-05, AS-06, AS-07, AS-08
- US-08 (Keyboard Navigation & Shortcuts) — subset: AS-01 (`G I` Inbox), AS-02 (`G U` Upcoming)
- FR-002 (optional task fields umbrella — trace-anchored here; `description` and `priority` realized in this slice)
- FR-022 (Today view: due-today incl. overdue incomplete, grouped by project, sorted by priority)
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
- FR-065 (dispatch-by-visibility authorization — see Access control below)
- FR-066 (membership-required access; createdBy/assignee are provenance only — see Access control below)
- FR-067 (sufficient-role per operation on shared-project resources — see Access control below)
- FR-068 (deny-by-default, handler-level enforcement — see Access control below)
- FR-092 (time/Europe-Warsaw reference zone — Today/Upcoming membership computation)
- FR-101 (ARIA-live for server-initiated updates/toasts + dialog focus contract)

MVP boundary confirmed:
- OOS-01..OOS-19 (full MVP out-of-scope confirmation)

Entity touchpoint(s):
- ENT-01 (Task) — this slice populates the `priority` and `description` attributes (the Task entity is owned by slice 002)

Depends on:
- Slice 003 (natural-language-dates) — provides the Polish date parser consumed by the `T` reschedule scenario (US-02.AS-05); FR-005 is owned there
- Slice 004 (project-management) — provides projects and the Inbox definition consumed by the Today view's project grouping (FR-022) and the `G I` navigation scenario (US-08.AS-01); FR-021 is owned there
- Slice 007 (project-sharing-membership) — provides the `ProjectMembership` aggregate, the `shared` visibility realization, and the `ResolveEffectiveRole`/`RequireRole` dispatch-by-visibility policy. **This resolves research open-question #1 (the BLOCKER) via option (a): slice 007 is sequenced before slice 005**, so the shared-project membership + role arm of the Today/Upcoming dispatch (FR-066/FR-067) and its two SC-016 deny tests (viewer-mutation-deny, non-member-read-deny) are now realizable and are realized in this slice. The membership arm fills the seam research R6/R10 reserved with no query/command reshape.

Exercised-but-not-owned (mechanics/keys exercised here; canonical ownership lives in other slices):
- The `1`-`4` priority keys and the `T` date key are members of FR-029 (the list-shortcuts requirement). They are exercised by US-02.AS-04 and US-02.AS-05 in this slice, but FR-029 is owned by slice 011 (cycles) — see Constitution Compliance.
- The `/` filter scenario US-08.AS-08 is owned by slice 013 (command-palette-search) and is not realized here.
- The full task editor delivered here (US-02.AS-06, US-02.AS-07, US-02.AS-08) supersedes slice 002's inline title edit (`E`); the canonical Space/E scenarios US-02.AS-03 and US-02.AS-06, whose mechanics were built in slice 002, are owned and counted in this slice.

Decomposition audit: Evaluated for split (views vs editor/priorities); kept whole — the editor is exercised inside the views, no clean dependency seam.

## User Scenarios & Testing *(mandatory)*

### User Story 2 - Daily Planning Session (Priority: P1)

User opens the "Today" view to see all tasks due today, grouped by project and sorted by priority. Using only keyboard shortcuts, they navigate between tasks, change priorities, edit details, reschedule, and mark tasks as done.

**Why this priority**: Daily planning is the second core loop after capture. Users need a reliable, fast way to review and triage their day.

**Independent Test**: Can be tested by creating several tasks with today's due date across different projects and priorities, navigating to Today view, and performing triage operations entirely via keyboard.

> Scope note: this slice OWNS the canonical Space toggle-done and `E` edit scenarios (US-02.AS-03 and US-02.AS-06), whose mechanics were first built in slice 002. The full task editor delivered here supersedes slice 002's inline title edit. The `T` reschedule scenario (US-02.AS-05) consumes the Polish date parser delivered in slice 003. The `1`-`4` priority keys and the `T` date key are members of FR-029, owned by slice 011 (see Provenance and Constitution Compliance).

> Today-view membership note: "due date equal to today" (US-02.AS-01) is computed against the instance reference timezone `Europe/Warsaw` (FR-092; "equal to today" = the same calendar day in `Europe/Warsaw`). In addition to tasks due today, the Today view INCLUDES overdue incomplete tasks (due date before today, shown as overdue) so nothing silently falls off the day. Both Today and Upcoming EXCLUDE done and cancelled tasks by default. Within each project group, tasks follow a deterministic order: project group → priority (P0 first) → due time → created. These rules are the slice-005 resolution of the product-vision "needs-more-info" item for this view and refine — without contradicting — the verbatim FR-022/FR-023 and the verbatim acceptance scenarios above.

> Dispatch-by-visibility note: the Today and Upcoming views surface BOTH the caller's personal tasks AND tasks in shared projects the caller is a current member of; this is not a personal-only ("Tier A") view. Each task row is authorized by its containing project's visibility (personal → ownership; shared-project → membership + role), deny-by-default at the handler (see Access control). "Assigned to me" filtering is owned by slice 008 and is NOT realized here.

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

> Scope note: this slice owns the `G I` and `G U` navigation scenarios (US-08.AS-01, US-08.AS-02). The earlier US-08 scenarios AS-03, AS-07, and AS-09 are owned by slice 002 (task-capture); the `/` filter scenario US-08.AS-08 is owned by slice 013 (command-palette-search). `G I` resolves to the Inbox definition provided by slice 004.

**Acceptance Scenarios** (owned by this slice):

1. **(US-08.AS-01) Given** the app is open, **When** user presses `G I`, **Then** the Inbox view opens.
2. **(US-08.AS-02) Given** the app is open, **When** user presses `G U`, **Then** the Upcoming view opens showing tasks for the next 7 days grouped by day.

### Edge Cases

No edge cases (EC-NN) from product-vision.md are assigned to this slice.

## Requirements *(mandatory)*

### Functional Requirements (slice-specific)

- **FR-002**: System MUST support optional task fields: description (markdown), priority (P0/P1/P2/P3), due date, labels (multiple), project assignment, and assignees (shared-project tasks only).
- **FR-022**: The Today view MUST show all tasks with due date equal to today, grouped by project and sorted by priority within each group.
- **FR-023**: The Upcoming view MUST show tasks for the next 7 days, grouped by day.
- **FR-030**: System MUST support task editor shortcuts: `Ctrl+Enter` (save), `Esc` (cancel).

> Scope note: FR-002 is the optional-fields umbrella and is trace-anchored here. This slice realizes its `description` and `priority` portions (via the full task editor and the `1`-`4` priority keys). The remaining optional fields are realized in their owning slices: `due date` in slice 003 (natural-language-dates), `project assignment` in slice 004 (project-management), `labels` in slice 006 (labels), and `assignees` in slice 008 (task-assignment).

### Cross-cutting Requirements (realized in this slice)

Access control (realized in this slice):

Authorization is dispatched by the containing resource's visibility (NOT a conjunction of tiers). Because the Today and Upcoming views surface shared-project data alongside personal data, this slice is not personal-only: each task is authorized by its project's visibility — personal/unprojected data authorizes on ownership (`createdBy`/`ownerId`) with queries scoped to the caller; shared-project tasks authorize on the caller's current `ProjectMembership` + role. `createdBy` and assignee are provenance only and confer no standalone access; on leave/remove/unshare a user loses ALL access to that project's data. The Today and Upcoming query handlers and the priority/edit/reschedule command handlers ENFORCE the following at the handler level (not merely reference it); authorization is deny-by-default.

- **FR-065**: Authorization MUST be dispatched by the containing resource's visibility (not a conjunction of tiers): personal/unprojected data authorizes on ownership (`createdBy`/`ownerId`) with queries scoped to the caller; shared-project entities authorize on current `ProjectMembership` + role. Every query MUST be scoped accordingly (per-user isolation).
- **FR-066**: Access to a shared project's data MUST require current membership in that project. `createdBy` and assignee are provenance only and confer NO standalone access; on leave/remove/unshare a user MUST lose ALL access to that project's data regardless of authorship or assignment.
- **FR-067**: Each operation on a shared-project resource MUST require sufficient role (viewer=read, editor=write, owner=manage); insufficient role MUST be denied. In this slice, a viewer MAY see a shared task in Today/Upcoming but MUST be denied the priority/edit/reschedule/toggle-done mutations (write requires editor or owner).
- **FR-068**: Authorization MUST be deny-by-default and enforced at the API/handler layer for every read and write.

Accessibility (per Constitution Principle II):
- **FR-031**: Single-key shortcuts MUST be suppressed when a text input is focused; only modifier-based shortcuts remain active during text input.
- **FR-042**: Every focusable element MUST have a visible focus indicator.
- **FR-043**: All interactive elements MUST have correct ARIA roles and labels for screen reader compatibility.
- **FR-044**: Text contrast ratio MUST be at least 4.5:1 (3:1 for large text).
- **FR-045**: Custom keyboard shortcuts MUST NOT collide with native assistive-technology bindings.
- **FR-046**: No content may be accessible only via hover — all tooltips and popovers MUST have a keyboard/focus-triggered equivalent.
- **FR-047**: Animations MUST respect the `prefers-reduced-motion` user preference; when reduced motion is active, transitions MUST be instant or under 100ms.
- **FR-101**: Server-initiated updates and toasts MUST be conveyed to assistive technology via an appropriate ARIA live region without stealing focus, and confirmation/command-palette dialogs MUST follow the dialog focus contract (set initial focus, trap focus, dismiss on Esc, return focus to the invoker on close).

Time (per Constitution Principle X):
- **FR-092**: All timestamps MUST be stored in UTC, and every date-relative computation (Today/Upcoming membership, cycle boundaries, recurrence rollover, natural-language date resolution) MUST be evaluated against a single instance reference timezone, `Europe/Warsaw`, applied identically on client and server. A due date MUST distinguish date-only from date-time via a `has_time` flag, and DST transitions MUST be handled by the timezone library, not fixed-offset arithmetic.

Error Handling & Data Integrity (per Constitution Principle VII):
- **FR-049**: All errors MUST be presented to the user with a clear message and an actionable recovery suggestion. No operation may fail silently.
- **FR-050**: Errors MUST be logged with structured context (severity level, operation context, and error details) for debugging purposes.
- **FR-051**: Before any data migration, the system MUST automatically create a backup of user data. The user MUST be able to restore from this backup.

### Key Entities

This slice introduces no new entity. It populates the `priority` and `description` attributes of **ENT-01 — Task** (owned by slice 002), which were reserved there as nullable, forward-compatible columns. For reference, the full Task definition from product-vision.md:

- **ENT-01 — Task**: The core work item. Has a title (required), description, priority (P0-P3), status (backlog/todo/in_progress/done/cancelled), due date, labels, project reference, cycle reference, recurrence rule, createdBy (the User who created it), assignees (zero or more Users; only on shared-project tasks), and system timestamps (created_at, updated_at, completed_at). New tasks default to "backlog" status. A recurring task has a linked recurrence rule that generates successor instances.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: User can perform a complete daily workflow (capture task, review today's tasks, reprioritize, reschedule, mark done) without using the mouse at any point.
- **SC-008**: Every main view passes automated accessibility audit at WCAG 2.1 AA level.

## Constitution Compliance

This slice is evaluated against constitution v4.0.0. Cross-cutting principles realized here:

- **I. Keyboard-First**: the entire daily loop — open Today (`G T`), open Inbox (`G I`), open Upcoming (`G U`), navigate, toggle done (`Space`), set priority (`1`-`4`), reschedule (`T`), edit (`E`), save (`Ctrl+Enter`), cancel (`Esc`) — is keyboard-driven; this realizes SC-001 (complete mouse-free daily workflow).
- **II. Accessibility (WCAG 2.1 AA)**: FR-042 (focus indicator), FR-043 (ARIA roles/labels), FR-044 (contrast ≥ 4.5:1), FR-045 (no AT-binding collisions), FR-046 (no hover-only content), FR-047 (prefers-reduced-motion). FR-031 keeps single-key shortcuts (including `1`-`4`, `E`, `T`) from hijacking text entry in the editor and date input. Per the strengthened Principle II (FR-101): the task editor and date-input dialogs MUST follow the dialog focus contract (set initial focus, trap focus, dismiss on Esc, return focus to the invoker on close — the `Esc`/`Ctrl+Enter` close paths in US-02.AS-07/AS-08 return focus to the originating task row), and any server-initiated update or toast surfaced in Today/Upcoming (e.g. an optimistic-write rollback message, or a live patch when the row belongs to a shared project) MUST be announced via an ARIA live region (polite) without stealing focus. SC-008 audits the Today and Upcoming views at WCAG 2.1 AA.
- **III. Instant Response**: priority changes and reschedules paint their optimistic result within one frame (<16ms) of the keypress (SC-003), then the server reconciles or rolls back (SC-012, server mutation budget p95<200ms); inbound real-time updates reconcile under last-write-wins and MUST NOT clobber a pending local optimistic edit.
- **IV. Minimalist UI**: the Today and Upcoming views surface only what matters for triage; server-rendered skeletons ARE permitted for the initial network-bound view load, but MUST NOT mask a mutation (priority/edit/reschedule) whose optimistic result can be shown immediately.
- **V. Connected, Server-Authoritative**: view rendering and all mutations (priority, edit, reschedule) flow through the C# API with PostgreSQL as the system of record; the app depends on no third-party runtime data service (SC-004, owned by slice 002).
- **VI. Type Safety End-to-End**: the `priority` (P0-P3 enum) and `description` (markdown string) fields added to the Task type are generated from the schema and validated at the editor input boundary.
- **VII. Data Integrity & Resilience**: FR-049 (error + recovery) and FR-050 (structured logging) cover edit/reschedule failures; FR-051 keeps the backup hook in place ahead of the schema change that populates `priority` and `description`. The editor's `Esc` (US-02.AS-08) discards in-flight changes without committing partial state.
- **VIII. Test-First**: each owned acceptance scenario above is independently testable (Red-Green-Refactor). Per the strengthened Principle VIII / Governance, every data handler in this slice (Today query, Upcoming query, priority/edit/reschedule/toggle-done commands) ships with both an allow test and a deny test, including a deny for a non-member reading another project's task and a deny for a viewer attempting a mutation (SC-016).
- **IX. Authentication & Authorization**: this slice's query and command handlers are authorized deny-by-default and enforced at the API/handler layer (FR-068). Authorization is dispatched by the containing resource's visibility (FR-065), NOT a Tier A/Tier B conjunction: the Today and Upcoming views surface the caller's personal tasks AND tasks in shared projects where the caller is a current member, so each task is authorized by its project's visibility — personal/unprojected on ownership (queries scoped to the caller), shared-project on current `ProjectMembership` + role (FR-067; viewer=read, editor/owner=write). `createdBy` and assignee are provenance only and never a standalone grant; leave/remove/unshare revokes ALL access to that project's data (FR-066). No unauthenticated request reaches a handler. (Sessions/admission per Principle IX — FR-052/FR-087/FR-088 — are established in slice 001 and assumed in force here; this slice adds no new session or admission surface.)
- **X. Time & Timezone**: "due date equal to today" and the next-7-days Upcoming window are computed against the single instance reference timezone `Europe/Warsaw`, applied identically on client and server (FR-092). Today includes overdue incomplete tasks and excludes done/cancelled; a due date carries a `has_time` flag distinguishing date-only from date-time (used by the deterministic due-time ordering and the `T` reschedule); DST is handled by the timezone library, not fixed-offset arithmetic.
- **XI. Privacy & Personal Data**: this slice renders and mutates existing task data and introduces no new personal-data sink; the account-deletion/erasure cascade and data-retention stance (FR-085/FR-086) are owned elsewhere. Today/Upcoming results respect membership revocation immediately (a task in a project the caller has left/been removed from MUST NOT appear), consistent with the residual-attribution and revoke-all rules.
- **XII. Security by Default**: the task `description` (markdown) surfaced in the editor and any task title rendered in Today/Upcoming is untrusted user-authored content and MUST be output-encoded/sanitized to a safe subset on render so raw HTML injection is impossible (FR-099); the production CSP and standard security headers apply. No new secrets are introduced by this slice (FR-100).

**Known ownership boundary (noted at slicing time):** US-02.AS-04 (`1`-`4` priority) and US-02.AS-05 (`T` date) exercise keys that are members of FR-029 (the consolidated list-shortcuts requirement). FR-029 is owned by slice 011 (cycles), where the full list-shortcut set is requirement-mapped; it is therefore deliberately NOT listed among this slice's Functional Requirements even though the two scenarios that use those keys are owned and delivered here. This mirrors the inverse handling in slice 002, where the `Space`/`E` mechanics were built ahead of their canonical scenarios (now owned here).

## Assumptions

This slice copies the following assumptions verbatim from product-vision.md as they continue to apply:

- **ASM-01 — Multi-user team**: The application serves a small collaborating team (~10). Each user authenticates (Google) and has personal data plus access to shared projects.
- **ASM-06 — In-app notifications only**: The app provides in-app notifications (assignment, mention, changes); email and push/device notifications and reminders are out of scope.
- **ASM-12 — Instance reference timezone**: The instance operates against a single reference timezone, `Europe/Warsaw`, for all date-relative computation; per-user timezones are out of scope.

The other assumptions established in slices 001–004 (gated admission, web platform, Polish date parsing, no subtasks, data format) also continue to apply.

## Out of Scope

This slice confirms the full MVP out-of-scope boundary (OOS-01..OOS-19 from product-vision.md). Multi-user collaboration and in-app notifications are NOT out of scope (they are in-scope MVP capabilities, OOS-01 PROMOTED and OOS-06 PARTIAL):

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

Additionally deferred to later slices within the MVP (not part of slice 005): labels and the label selector (006), task assignment and the "assigned to me" filter (008), the project Kanban board and groupable project list (010), cycles and the `1`-`4`/`T` list-shortcut requirement FR-029 (011), recurring tasks (012), command palette, fuzzy search, and the `/` view filter US-08.AS-08 (013), undo (014), data export/import (015), and dark/light theming (018).

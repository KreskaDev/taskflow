# Feature Specification: Recurring Tasks

**Feature Branch**: `012-recurring-tasks`

**Created**: 2026-06-13

**Status**: Draft

**Input**: Slice 012 of the TaskFlow MVP. Source of truth: `.specify/memory/product-vision.md`. Goal: let a task repeat on a schedule (daily, every N days, specific weekdays, monthly) and automatically generate the next instance when an instance is completed. Recurrence requires a due date (if a task has no due date, recurrence is unavailable). The next instance is computed from the original due date, not the completion date (ASM-09); early completion defers the spawn to a server-side Wolverine scheduled job that runs on or after the original due date (EC-05 / FR-009). All date-relative computation (recurrence rollover) is evaluated against the single instance reference timezone `Europe/Warsaw` (FR-092).

## Provenance

This slice realizes the following items from product-vision.md:

Slice-specific:
- US-06 (Recurring Tasks) — full: AS-01, AS-02, AS-03, AS-04, AS-05, AS-06
- FR-007 (supported recurrence schedules: daily, every N days, specific weekdays, monthly; monthly is day-of-month with end-of-month clamping; recurrence requires a due date)
- FR-008 (generate next instance on completion on/after due; carry-forward fields AND the recurrence rule + anchor so the chain continues across successive instances; re-validate carried assignees against current membership; resets)
- FR-009 (early completion defers spawn to a server-side Wolverine scheduled job on or after the original due date; idempotent ≤1 successor; undo of completion removes the successor)
- FR-010 (cancelling a recurring task stops further instances)
- EC-05 (recurring task early completion — deferred spawn by server-side scheduled job)
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
- FR-101 (ARIA-live for server-initiated updates/toasts + dialog focus contract)

Time (per Constitution Principle X):
- FR-092 (UTC storage; Europe/Warsaw reference zone for recurrence rollover; due-date has_time flag; DST via library)

Access control — dispatch by visibility (per Constitution Principle IX):
- FR-065 (authorization dispatched by the containing resource's visibility; per-user isolation)
- FR-066 (shared-project access requires current membership; createdBy/assignee are provenance only; leave/remove/unshare revokes ALL access)
- FR-067 (sufficient-role gate on shared-project resources)
- FR-068 (deny-by-default, enforced at the API/handler layer for every read and write)

Authorization in this slice is **dispatched by the containing resource's visibility** (not a conjunction of tiers): a recurring task in personal/unprojected space authorizes on **ownership** (`createdBy`/`ownerId`, queries scoped to the caller); a recurring task in a shared project authorizes on **current `ProjectMembership` + role**. `createdBy` and assignee are **provenance only** and confer no standalone access; on leave/remove/unshare a user loses ALL access to that project's data. This slice's command and query handlers ENFORCE this dispatch at the handler level — the on-completion next-instance generation, the deferred-spawn scheduled job, and any read of recurring tasks are all authorized accordingly, deny-by-default. These FRs are not merely referenced; they are enforced by the handlers themselves.

MVP boundary confirmed:
- OOS-01..OOS-19 (full MVP out-of-scope confirmation)

Entity touchpoint(s):
- ENT-05 (Recurrence Rule) — introduced and owned by this slice; it populates the `recurrence rule` attribute of ENT-01 (Task), which is owned by slice 002 (task-capture)

Depends on:
- Slice 005 (daily-planning) — provides the full task editor (where a recurrence rule is set/edited), the `Space` toggle-done mechanic, and the priority/due-date handling that the carried-forward fields rely on

## Clarifications

### Session 2026-11-08

- Q: What does "undoing the completion removes the successor" mean before slice 014 (Undo) exists — what happens to successor B when the completed instance A is reopened (`Space`: done → backlog), including when B was already modified? → A: Reopening A soft-deletes B unless B is already `done` (then B stays and A simply loses its successor link). Re-completing A generates a fresh successor. Any status change of A away from `done` before the deferred job fires cancels the pending spawn (the job finds A not done and no-ops).
- Q: When an instance is completed long after its due date (e.g. every 3 days, due June 10, completed June 20), does the successor skip the overdue occurrences? → A: No. The successor's due date is always the FIRST occurrence of the rule strictly after the original due date (June 13), even when that date is already in the past — the successor is immediately overdue. Nothing is skipped or shifted to today.
- Q: Where is the recurrence rule set — the full task editor only, or also parsed from the quick-add line / offered as a row-menu item? → A: Only in the task editing surfaces via a "Powtarzanie" field: the full task editor (slice 005 `E` dialog, hosted by the daily views) and the task details drawer („Szczegóły i komentarze", reachable from every list and the Board — the planning code audit found the `E` dialog is not mounted on Inbox/project lists, so the drawer hosts the same shared field). Quick-add and its slice-003 date grammar are unchanged (no recurrence phrases); "creating" in US-06.AS-01 is satisfied by adding the task and editing it. No separate row-menu item.
- Q: What happens when the due date is cleared on a task that has a recurrence rule? → A: Blocked. The editor refuses to save a recurring task without a due date (inline message: the task must keep a due date or the user disables recurrence first), and the server rejects such a write with a validation error (`recurrence_requires_due_date`). The rule is never dropped silently.

## User Scenarios & Testing *(mandatory)*

### User Story 6 - Recurring Tasks (Priority: P3)

User creates tasks that repeat on a schedule (daily, every N days, specific weekdays, monthly). When a recurring task instance is completed, the next instance is automatically generated according to the recurrence rule.

**Why this priority**: Recurring tasks are important for habits and routine work but are an enhancement to the core task model.

**Independent Test**: Can be tested by creating a recurring task, completing it, and verifying the next instance is generated with the correct due date.

> Scope note: the next instance is computed from the original due date, not the completion date (ASM-09), evaluated against the `Europe/Warsaw` reference timezone (FR-092). When an instance is completed early (before its due date), no new instance is spawned immediately; the deferred instance is generated by a server-side Wolverine scheduled job that runs on or after the original due date — not by client startup (EC-05 / FR-009). That job is idempotent (at most one successor per completed instance), and undoing the completion removes the spawned successor. The successor carries forward the recurrence rule and its anchor, so the chain continues — a second successor is generable from the first. Recurrence requires a due date; monthly recurrence is day-of-month with end-of-month clamping (e.g. Jan 31 → Feb 28/29). This is framing only — the binding requirements are FR-008, FR-009, FR-092, ASM-09, and EC-05 below.

**Acceptance Scenarios** (owned by this slice):

1. **(US-06.AS-01) Given** a user is creating or editing a task, **When** they set a recurrence rule (e.g., "every day"), **Then** the task is marked as recurring with a visual indicator. The rule is set ONLY through the "Powtarzanie" field of the task editing surfaces (the slice 005 `E` editor dialog and the task details drawer); quick-add and its date grammar do not parse recurrence phrases, and there is no separate row-menu item.
2. **(US-06.AS-02) Given** a recurring task with rule "every day" and due date today, **When** user marks it as done, **Then** a new task instance is automatically created with due date tomorrow and the same title, description, priority, labels, project, and assignees.
3. **(US-06.AS-03) Given** a recurring task with rule "every Monday and Wednesday" and due date Monday, **When** user marks it as done on Monday, **Then** the next instance is created with due date Wednesday.
4. **(US-06.AS-04) Given** a recurring task with due date tomorrow, **When** user marks it as done today (before due date), **Then** no new instance is generated yet (to avoid premature spawning).
5. **(US-06.AS-05) Given** a recurring task with rule "every 3 days" and due date June 10, **When** user marks it as done on June 12, **Then** the next instance is created with due date June 13 (3 days from the original due, not from completion).
6. **(US-06.AS-06) Given** a recurring task, **When** user cancels it (status = cancelled), **Then** no further instances are generated.

### Edge Cases

- **EC-05 — Recurring task early completion**: If a recurring task is completed before its due date, no new instance is spawned immediately. The deferred instance is generated by a server-side scheduled job (Wolverine) that runs on or after the original due date, not by client startup. The deferred instance keeps the same assignees as the completed instance.
- **Chain continuation (second successor)**: Because the successor carries forward the recurrence rule and its anchor, completing the successor on or after its own due date generates a further successor; the chain MUST continue indefinitely across successive instances (a second successor is generable from the first).
- **Idempotent spawn / undo**: The deferred-spawn scheduled job MUST be idempotent — at most one successor is generated per completed instance even if the job runs repeatedly. "Undoing the completion" is reopening the instance (`Space`: done → backlog, or any status change away from `done`): reopening instance A MUST soft-delete the successor B that A's completion spawned, UNLESS B is already `done` — then B stays and A only loses its successor link (the chain has moved on). Re-completing A MUST generate a fresh successor (the slot is free again). If A is reopened before the deferred job fires, the job MUST find A not `done` and no-op (no spawn).
- **Re-validated assignees**: When the completed instance lived in a shared project, the carried-forward assignees MUST be re-validated against the project's current membership before the successor persists (a former member is dropped); no re-notification is sent for the carry-forward.
- **Recurrence requires a due date**: Recurrence is unavailable for a task with no due date; the rule's rollover anchors on the due date. Clearing the due date of a task that has a rule is BLOCKED: the editor shows an inline validation message (keep a due date, or disable recurrence first) and refuses to save; the server rejects the write with `recurrence_requires_due_date` (422). The rule is never dropped silently.
- **Late completion never skips occurrences**: The successor's due date is the FIRST occurrence of the rule strictly after the completed instance's due date, regardless of when the completion happened (ASM-09). When that occurrence is already in the past (e.g. every 3 days, due June 10, completed June 20 → successor due June 13), the successor is created immediately overdue; occurrences are never skipped or shifted to today.
- **Monthly end-of-month clamping**: Monthly recurrence is by day-of-month; when the target month has fewer days than the anchor day-of-month, the due date clamps to the last day of that month (e.g. Jan 31 → Feb 28, or Feb 29 in a leap year).

## Requirements *(mandatory)*

### Functional Requirements (slice-specific)

- **FR-007**: System MUST support recurring tasks with the following schedules: daily, every N days, specific weekdays, monthly.
- **FR-008**: When a recurring task instance is marked as done (on or after its due date), the system MUST automatically generate the next instance with the appropriate due date according to the recurrence rule. The new instance MUST carry forward all fields from the completed instance (title, description, priority, labels, project, assignees) AND the recurrence rule and its anchor, so the chain continues across successive instances (a second successor MUST be generable from the first), except: status (reset to backlog), timestamps (new created_at, no completed_at), and cycle assignment (new instance is unassigned to any cycle — the user assigns it manually). The new instance keeps the same assignees, but carried-forward assignees MUST be re-validated against current project membership (with no re-notification).
- **FR-009**: When a recurring task instance is marked as done before its due date, the system MUST NOT generate the next instance immediately. The next instance MUST be generated by a server-side scheduled job (Wolverine) that runs on or after the original due date — not by client startup. The job MUST be idempotent (at most one successor per completed instance), and undoing the completion MUST remove the spawned successor — concretely: reopening the completed instance (any status change away from `done`) soft-deletes its spawned successor unless that successor is already `done`, cancels a not-yet-fired deferred spawn, and frees the slot so re-completing the instance generates a fresh successor.
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
- **FR-101**: Server-initiated updates and toasts MUST be conveyed to assistive technology via an appropriate ARIA live region without stealing focus, and confirmation/command-palette dialogs MUST follow the dialog focus contract (set initial focus, trap focus, dismiss on Esc, return focus to the invoker on close).

Error Handling & Data Integrity (per Constitution Principle VII):
- **FR-049**: All errors MUST be presented to the user with a clear message and an actionable recovery suggestion. No operation may fail silently.
- **FR-050**: Errors MUST be logged with structured context (severity level, operation context, and error details) for debugging purposes.
- **FR-051**: Before any data migration, the system MUST automatically create a backup of user data. The user MUST be able to restore from this backup.

Time (per Constitution Principle X):
- **FR-092**: All timestamps MUST be stored in UTC, and every date-relative computation (Today/Upcoming membership, cycle boundaries, recurrence rollover, natural-language date resolution) MUST be evaluated against a single instance reference timezone, `Europe/Warsaw`, applied identically on client and server. A due date MUST distinguish date-only from date-time via a `has_time` flag, and DST transitions MUST be handled by the timezone library, not fixed-offset arithmetic.

Access control — dispatch by visibility (per Constitution Principle IX):
- **FR-065**: Authorization MUST be dispatched by the containing resource's visibility (not a conjunction of tiers): personal/unprojected data authorizes on ownership (`createdBy`/`ownerId`) with queries scoped to the caller; shared-project entities authorize on current `ProjectMembership` + role. Every query MUST be scoped accordingly (per-user isolation).
- **FR-066**: Access to a shared project's data MUST require current membership in that project. `createdBy` and assignee are provenance only and confer NO standalone access; on leave/remove/unshare a user MUST lose ALL access to that project's data regardless of authorship or assignment.
- **FR-067**: Each operation on a shared-project resource MUST require sufficient role (viewer=read, editor=write, owner=manage); insufficient role MUST be denied.
- **FR-068**: Authorization MUST be deny-by-default and enforced at the API/handler layer for every read and write.

### Key Entities

This slice introduces and owns **ENT-05 — Recurrence Rule**. It populates the `recurrence rule` attribute of **ENT-01 — Task** (owned by slice 002, task-capture), which was reserved there as a nullable, forward-compatible column.

- **ENT-05 — Recurrence Rule**: Defines a repeating schedule attached to a task. Specifies frequency type (daily, every N days, specific weekdays, monthly) and parameters. Used to generate the next task instance upon completion.

## Success Criteria *(mandatory)*

### Measurable Outcomes

This slice introduces no new slice-specific success criteria. The measurable outcomes that apply here — SC-003 (optimistic UI: the optimistic result is painted within 16ms of the triggering keypress while the server reconciles or rolls back asynchronously) and SC-004 (no third-party runtime data services — only the application's own API and PostgreSQL database) — are owned by slice 002 (task-capture); how they are realized by this slice's recurrence computation and next-instance generation is described under Constitution Compliance below.

## Constitution Compliance

This slice is evaluated against constitution v5.1.0 (Principle I: UI-First Operability). Cross-cutting principles realized here:

- **I. UI-First Operability**: the recurrence rule is a visible, labelled „Powtarzanie" field in the task editor dialog and in the task details drawer (both reachable by click — „Edytuj" on the daily views, „Szczegóły i komentarze" on every surface); completing, reopening and cancelling an instance use the existing visible checkbox / Board move affordances. No recurrence functionality exists only behind a keyboard shortcut (the shortcut system stays deferred, OOS-20); standard WCAG keyboard operability applies to every new control, and the recurring indicator (US-06.AS-01) is a text chip visible on every row.
- **II. Accessibility (WCAG 2.1 AA)**: FR-042 (focus indicator), FR-043 (ARIA roles/labels), FR-044 (contrast ≥ 4.5:1), FR-045 (no AT-binding collisions), FR-046 (no hover-only content — the recurring indicator and any recurrence controls have keyboard/focus-triggered equivalents), FR-047 (prefers-reduced-motion). FR-031 keeps single-key shortcuts from hijacking text entry in the recurrence-rule fields. FR-101 applies here: when a successor instance arrives on an open shared view over SignalR or surfaces as a live toast, it MUST be announced to assistive technology via a polite ARIA live region without stealing focus, and any recurrence confirmation/editor dialog MUST honor the dialog focus contract (initial focus, focus trap, Esc to dismiss, focus returned to the invoker).
- **III. Instant Response (Optimistic UI)**: marking an instance done paints the completion optimistically within one animation frame (SC-003, owned by slice 002, task-capture); the successor itself is server-generated inside the same status write (p95 < 200ms) and surfaces when the mutation settles and the lists reconcile (invalidate + refetch), announced by a toast — the client never paints a provisional successor, so no duplicate row can appear against the refetch; rollback is applied if the server rejects the operation. A successor surfaced on another member's open shared view arrives over SignalR and reconciles under last-write-wins, yielding to any in-flight local edit (Principle III real-time reconciliation). Skeleton/loading states are permitted (Principle IV) where the successor or its reconciled state has not yet been confirmed.
- **V. Connected, Server-Authoritative**: recurrence computation and next-instance generation are performed and persisted through the application's own C# API into PostgreSQL, which is the source of truth; the Next.js client surfaces the successor via optimistic UI and reconciles against the server. The deferred on/after-due spawn is driven by a server-side Wolverine scheduled job through that same API — not by client startup. The only data dependency is the application's own API and database — no third-party runtime data services (SC-004, owned by slice 002, task-capture); the sole external runtime dependency is Google OAuth for authentication, never for application data.
- **VI. Type Safety End-to-End**: the Recurrence Rule (ENT-05) frequency type and parameters are a typed model; the recurrence boundary is validated at runtime (user input in the editor and deserialization from storage).
- **VII. Data Integrity & Resilience**: FR-008's deterministic carry-forward (including the recurrence rule + anchor and assignees) and resets prevent silent data loss across instance generation; the deferred-spawn job is idempotent (≤1 successor per completed instance) and undoing a completion removes its spawned successor (FR-009), so neither duplicate nor orphan successors accrue. FR-049 surfaces any generation error with a recovery suggestion and FR-050 logs it with structured context; FR-051 keeps the backup hook in place ahead of the schema change that adds the recurrence rule.
- **VIII. Test-First**: each owned acceptance scenario above, plus EC-05 and the chain-continuation/idempotency/assignee-revalidation/clamping edge cases, is independently testable (Red-Green-Refactor). Per Principle VIII and FR-068, every recurrence handler ships with both an allow and a deny test — including that a recurrence operation on a task the caller does not own (personal) or in a shared project where the caller lacks the required membership/role is denied.
- **IX. Authentication & Authorization**: authorization is deny-by-default and enforced at the handler layer for every read and write in this slice. It is **dispatched by the containing resource's visibility** (FR-065–FR-068), not a conjunction of tiers: a recurring task in personal/unprojected space authorizes on **ownership** (`createdBy`/`ownerId`) with queries scoped to the caller; a recurring task in a shared project authorizes on **current `ProjectMembership` + sufficient role** (viewer=read, editor=write, owner=manage). `createdBy` and assignee are **provenance only** and grant no standalone access — on leave/remove/unshare a user loses ALL access to that project's recurring tasks regardless of authorship or assignment. Generating the next instance on completion, running the deferred-spawn scheduled job, and reading recurring tasks are all authorized accordingly. When a recurring task lives in a shared project, the carried-forward assignees MUST be re-validated against the project's current membership before the successor persists (a former member is dropped, with no re-notification).
- **X. Time & Timezone**: per FR-092, all timestamps are stored in UTC and the recurrence rollover — "on or after the due date", the next-due computation (every N days / specific weekdays / monthly day-of-month with end-of-month clamping), and the deferred-spawn job's on/after-due trigger — is evaluated against the single instance reference timezone `Europe/Warsaw`, applied identically on client and server, so a due-date boundary is the same fact everywhere. A due date distinguishes date-only from date-time via the `has_time` flag, and DST transitions are handled by the timezone library, never fixed-offset arithmetic. Recurrence requires a due date to anchor this computation.

## Assumptions

- **ASM-09 — Recurrence based on due date**: Next recurring task instance is calculated from the original due date, not the completion date, ensuring consistent scheduling.

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

# Feature Specification: Undo Destructive Actions

**Feature Branch**: `014-undo`

**Created**: 2026-06-13

**Status**: Draft

**Input**: Slice 014 of the TaskFlow MVP. Source of truth: `.specify/memory/product-vision.md`. Goal: a 30-second undo window for all destructive actions, surfaced via a toast notification, that fully restores the previous state. This slice fulfils the Constitution Principle VII undo guarantee that was deferred from slice 002, retrofitting undo onto the existing delete path (002), project delete (004), and cycle rollover (011).

## Provenance

This slice realizes the following items from product-vision.md:

Slice-specific:
- US-09 (Undo Destructive Actions) — full: AS-01, AS-02, AS-03, AS-04, AS-05
- US-08 (Keyboard Navigation & Shortcuts) — subset: AS-06 (`Del` delete with the 30-second undo toast)
- FR-040 (30-second undo window for all destructive and irreversible actions)
- EC-09 (multiple rapid undo actions — independent entries, independent timers)

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

Access control (realized in this slice):
- FR-064 (membership/role changes are confirmation-gated and NOT covered by the 30-second data undo)
- FR-065 (per-user isolation — every query scoped to data the caller owns or has membership access to)
- FR-066 (membership required for shared-project data)
- FR-067 (sufficient-role enforcement per operation)
- FR-068 (deny-by-default authorization at the API/handler layer)

MVP boundary confirmed:
- OOS-01..OOS-17 (full MVP out-of-scope confirmation)

Depends on:
- Slice 004 (project-management) — provides project deletion (cascade delete / move tasks to Inbox / archive) onto which this slice retrofits the undo window (US-09.AS-04)
- Slice 011 (cycles) — provides cycle rollover and bulk moves between cycles onto which this slice retrofits the undo window (US-09.AS-05)

> Scope note (framing only, not new requirements): this slice closes the Constitution Principle VII undo guarantee that slice 002 documented as a deferred gap. It retrofits undo onto the delete path established in slice 002 (task deletion, US-08.AS-06), onto project delete from slice 004, and onto cycle rollover and bulk moves from slice 011. No new destructive action is introduced here — the undo window wraps actions that already exist in those slices.

## User Scenarios & Testing *(mandatory)*

### User Story 9 - Undo Destructive Actions (Priority: P2)

When user performs a destructive action (delete task, delete project, bulk move), the system provides a 30-second undo window via a toast notification. Pressing undo fully restores the previous state.

**Why this priority**: Undo is essential for a keyboard-first app where rapid actions increase the chance of mistakes.

**Independent Test**: Can be tested by deleting a task, verifying the undo toast appears, pressing undo within 30 seconds, and verifying the task is fully restored.

**Acceptance Scenarios** (owned by this slice):

1. **(US-09.AS-01) Given** a user deletes a task, **When** the deletion executes, **Then** a toast notification appears with an "Undo" action and a 30-second countdown.
2. **(US-09.AS-02) Given** the undo toast is visible, **When** user clicks or keyboard-activates "Undo" within 30 seconds, **Then** the task is fully restored to its previous state including all fields, project assignment, and cycle assignment.
3. **(US-09.AS-03) Given** the undo toast is visible, **When** 30 seconds elapse without user action, **Then** the toast disappears and the deletion becomes permanent.
4. **(US-09.AS-04) Given** a user deletes a project containing tasks, **When** they choose "move tasks to Inbox" and then undo, **Then** the project is restored and all tasks are moved back to the project.
5. **(US-09.AS-05) Given** a user performs a bulk move of tasks between cycles, **When** they undo, **Then** all moved tasks return to their original cycle assignments.

---

### User Story 8 - Keyboard Navigation & Shortcuts Across All Views (Priority: P1)

User navigates the entire application using keyboard shortcuts. Global shortcuts work from any view, navigation shortcuts switch between views, and contextual shortcuts operate on the currently selected item.

**Why this priority**: Keyboard-first is the core principle. Without complete keyboard coverage, the app fails its primary promise.

**Independent Test**: Can be tested by navigating to every view, performing every action, and verifying no operation requires a mouse.

> Scope note: the bulk of US-08 (navigation `G I`/`G U`, arrow navigation, `L`, `M`, `?`, `/`, single-key suppression) is owned by earlier slices (002, 004, 005, 006, 013). This slice owns only US-08.AS-06 — the `Del` delete shortcut paired with the 30-second undo toast — because that is the scenario whose canonical behavior (undo) is delivered here. Slice 002 implemented the `Del` mechanic but performed a permanent delete; this slice completes it.

**Acceptance Scenarios** (owned by this slice):

1. **(US-08.AS-06) Given** a task is selected, **When** user presses `Del`, **Then** the task is deleted with a 30-second undo toast notification.

### Edge Cases

- **EC-09 — Multiple rapid undo actions**: Each destructive action creates its own independent undo entry. Multiple undos can be active simultaneously, each with its own 30-second timer.

## Requirements *(mandatory)*

### Functional Requirements (slice-specific)

- **FR-040**: System MUST provide a 30-second undo window for all destructive and irreversible actions, including but not limited to: task deletion, project deletion, bulk moves, bulk status changes, and cycle rollover operations.

**Undo scope boundary (data vs. membership):** the 30-second snapshot undo (FR-040) covers task and project **data** — deletes, bulk moves, bulk status changes, and cycle rollover. It does **NOT** cover membership, role, or sharing changes. Those are confirmation-gated by **FR-064** and are explicitly **NOT undoable** via this slice's snapshot mechanism. Verbatim, per product-vision FR-064: *Membership and role changes (invite, role change, remove, leave, unshare) MUST require explicit confirmation and MUST NOT be covered by the 30-second data undo (FR-040).*

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

### Access control (realized in this slice)

This is a Tier A **and** Tier B slice: the undo window operates over personal-project task/project data (per-user isolation, Tier A) **and** over shared-project task/project data and cycle moves (membership + role, Tier B). The slice's command and query handlers **ENFORCE** the following at the handler level (deny-by-default) — they do not merely reference them. A caller may only capture or restore a snapshot for data they are authorized to mutate; an undo restore is itself an authorized write subject to the same checks as the original action.

Tier A (per-user isolation):
- **FR-065**: Every query MUST be scoped to data the caller owns or has membership access to (per-user isolation).

Tier B (membership + role):
- **FR-066**: Access to a shared project's data MUST require membership in that project.
- **FR-067**: Each operation MUST require sufficient role (viewer=read, editor=write, owner=manage); insufficient role MUST be denied.
- **FR-068**: Authorization MUST be deny-by-default and enforced at the API/handler layer for every read and write.

Confirmation gate (not undo):
- **FR-064**: Membership and role changes (invite, role change, remove, leave, unshare) MUST require explicit confirmation and MUST NOT be covered by the 30-second data undo (FR-040).

### Key Entities

This slice introduces no new entity and populates no new entity attribute. The undo window operates over existing entities — **ENT-01 (Task)** owned by slice 002, **ENT-02 (Project)** owned by slice 004, and **ENT-03 (Cycle)** owned by slice 011 — by capturing and restoring their prior state on a destructive action. No entity definition is owned or modified here.

For reference, the amended entity definitions this slice's snapshot/restore operates over (re-copied 1:1 from product-vision.md):

- **ENT-01 — Task**: The core work item. Has a title (required), description, priority (P0-P3), status (backlog/todo/in_progress/done/cancelled), due date, labels, project reference, cycle reference, recurrence rule, createdBy (the User who created it), assignees (zero or more Users; only on shared-project tasks), and system timestamps (created_at, updated_at, completed_at). New tasks default to "backlog" status. A recurring task has a linked recurrence rule that generates successor instances.
- **ENT-02 — Project**: An organizational container for tasks. Has a name, color, icon, optional parent project reference, archived flag, ownerId (the owning User), and visibility (personal or shared). Supports one level of nesting. Contains zero or more tasks. Shared projects have a membership set.

A task snapshot captured for undo therefore includes `createdBy` and any `assignees`; a project snapshot includes `ownerId` and `visibility`. Restoring a snapshot re-applies these fields under the same authorization checks as the original write.

## Success Criteria *(mandatory)*

This slice introduces no new slice-specific success criteria. The relevant measurable outcomes are owned by earlier slices and continue to apply: SC-003 (the destructive action paints its optimistic result within 16ms of the triggering keypress — the undo toast appears within one frame — while the server reconciles or rolls back asynchronously, owned by slice 002) and SC-004 (the application depends on no third-party runtime data services — only its own API and PostgreSQL database; undo state persists and restores through the app's own API, owned by slice 002).

## Constitution Compliance

This slice is evaluated against constitution v3.0.0. Cross-cutting principles realized here:

- **I. Keyboard-First**: the destructive action (`Del`, US-08.AS-06) and the undo itself are keyboard-driven — US-09.AS-02 explicitly requires that "Undo" be keyboard-activatable, not mouse-only.
- **II. Accessibility (WCAG 2.1 AA)**: the undo toast and its countdown are conveyed accessibly — FR-043 (ARIA roles/labels, with the countdown announced via an ARIA-live region rather than visual-only), FR-042 (focus indicator on the toast's "Undo" control), FR-044 (contrast ≥ 4.5:1), FR-045 (no AT-binding collisions), FR-046 (no hover-only content — the undo action is reachable by keyboard/focus), FR-047 (prefers-reduced-motion governs the toast and countdown animation). FR-031 keeps single-key shortcuts from hijacking text entry.
- **III. Instant Response**: the destructive action paints its optimistic result — the undo toast — within one animation frame (under 16ms) of the keypress, before the server confirms; the C# API then reconciles or rolls back asynchronously within a p95 server budget of 200ms (the 16ms optimistic paint is SC-003; the 200ms server-mutation budget is SC-012 / Performance Standards — both owned by slice 002). For shared projects, an inbound real-time (SignalR) update reflecting another member's delete or undo reconciles under last-write-wins but MUST yield to a pending local optimistic mutation until its server-ack resolves. Skeleton screens are permitted for network-bound loads but MUST NOT mask the optimistic toast.
- **IV. Minimalist UI**: the undo affordance is a single, purposeful toast — no modal interruption, no decorative animation; skeleton screens are permitted for genuine network-bound loads but MUST NOT stand in for the optimistic toast that can be rendered immediately.
- **V. Connected, Server-Authoritative**: undo is a server-authoritative operation through the C# API — the destructive action optimistically removes the item from the client view while the API performs the delete, and undo restores the prior state via the API, with PostgreSQL as the system of record. The app depends on no third-party runtime data services, only its own API and database (SC-004, owned by slice 002); the single permitted external runtime dependency is Google OAuth, for authentication only, and it is not on the undo path.
- **VI. Type Safety End-to-End**: the captured pre-action snapshot is a typed structure; restoration validates the snapshot at the API/persistence boundary before re-applying it.
- **VII. Data Integrity & Resilience**: **this slice closes the Principle VII undo guarantee that slice 002 documented as a deferred, accepted gap.** Principle VII requires that delete, bulk update, and any irreversible operation on task/project **data** be undoable for a minimum of 30 seconds after execution. FR-040 delivers that 30-second window and retrofits it onto the delete path from slice 002 (task deletion, US-08.AS-06 — previously a permanent delete), onto project deletion from slice 004 (US-09.AS-04), and onto cycle rollover and bulk cycle moves from slice 011 (US-09.AS-05). Per Principle VII and FR-064, membership and role changes (sharing, role assignment, removing a member, leaving, unsharing) are **NOT** covered by this 30-second data undo; they require an explicit confirmation dialog instead and are not undoable through this slice's snapshot mechanism. EC-09 ensures rapid successive actions each get an independent undo entry and timer, so no in-flight undo is lost. FR-049 (recovery surfaced, no silent loss), FR-050 (structured logging of the destructive action and any restore failure), and FR-051 (backup hook in place) round out the resilience guarantees.
- **VIII. Test-First**: each owned acceptance scenario above, plus EC-09, is independently testable (Red-Green-Refactor); integration tests cover the undo/restore command and query handlers through the real database, including authorization (a restore without the required ownership/role MUST be denied).
- **IX. Authentication & Authorization**: this slice authorizes its operations deny-by-default at the API/handler layer. Snapshot capture and restore are scoped to the caller's identity — per-user isolation (FR-065, Tier A) so a caller can only undo deletes of data they own, and for shared-project data, membership + sufficient role (FR-066, FR-067, FR-068, Tier B) so an undo restore is permitted only for a member whose role allows the equivalent write. A restore is treated as a write and re-checked against the same policy as the original action; membership/role changes are out of the undo path and confirmation-gated (FR-064).

**Deferred gap closed:** slice 002's Constitution Compliance recorded a known, accepted Principle VII gap — `Del` performed a permanent delete and the 30-second undo window (FR-040) was deferred to this slice. That gap is closed here. No new Principle VII deferral is introduced by this slice.

## Assumptions

This slice introduces no new assumptions. It operates under the assumptions established by the slices it depends on, re-copied 1:1 from the amended product-vision.md:

- **ASM-01 — Multi-user team**: The application serves a small collaborating team (~10). Each user authenticates (Google) and has personal data plus access to shared projects.
- **ASM-02 — Web platform**: The MVP targets modern desktop browsers. Native mobile apps, PWA/offline operation, and cross-device sync are explicitly out of scope.
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

Also out of scope for this slice specifically: the destructive actions themselves (task delete from slice 002, project delete from slice 004, cycle rollover and bulk moves from slice 011) are owned by their respective slices; this slice owns only the undo window wrapping them.

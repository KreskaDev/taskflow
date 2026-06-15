# Feature Specification: Notifications

**Feature Branch**: `017-notifications`

**Created**: 2026-06-13

**Status**: Draft

**Input**: Slice 017 of the TaskFlow MVP. Source of truth: `.specify/memory/product-vision.md` (amended) and `.specify/memory/constitution.md` (v4.0.0). Goal: an in-app notification center (newest-first, read/unread) with live SignalR toasts when online, mark-read / mark-all-read, and per-type preferences; notifications are generated when a member is assigned, @mentioned, or when a task they are an assignee of changes (the "changed" trigger set is closed to status / due date / assignee / project-move; low-signal edits are excluded; rapid changes are coalesced; self-actions are suppressed via `actorUserId`) — consuming the `TaskAssigned` / `UserMentioned` and task-changed domain events emitted by slices 008 and 009. A notification's source is a live reference, re-authorized on dereference (deny-by-default re-check at read/click time); the payload carries no content beyond what current authorization permits, and a source no longer accessible resolves to "no longer available". In-app only; email and push/device notifications remain out of scope.

## Provenance

Slice-specific (owned by this slice):
- US-16 (Notifications) — AS-01, AS-02, AS-03, AS-04
- FR-079 (generate in-app notification on assigned / @mentioned / assignee-task changed; closed "changed" trigger set; coalescing; self-suppression)
- FR-080 (notification center, newest-first, read/unread state; source re-authorized on dereference)
- FR-081 (live toast when online)
- FR-082 (mark read / mark all read)
- FR-083 (per-type notification preferences)
- FR-084 (in-app only; email and push/device out of scope)
- FR-096 (notification dereference re-authorization — no content beyond current authz; denied → "no longer available") — NEW, owned by this slice
- ENT-09 (Notification — source is a live reference)

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
- FR-051 (auto-backup before migration)

Privacy & Security (per Constitution Principles XI / XII):
- FR-085 (account-deletion erasure cascade — recipient notifications are purged; referenced, owned by slice 015 / US-17)
- FR-086 (data-retention stance — notifications retained until account deletion; referenced)
- FR-099 (content sanitization — user-authored content surfaced in a toast/center is output-sanitized; referenced)

Access control (realized in this slice):
- FR-065 (authorization dispatched by resource visibility; queries scoped to the caller — per-user isolation)
- FR-066 (shared-project access requires current membership; createdBy/assignee are provenance only; leave/remove/unshare revokes ALL access)
- FR-067 (sufficient-role enforcement: viewer=read, editor=write, owner=manage)
- FR-068 (authorization deny-by-default at the API/handler layer)

MVP boundary confirmed:
- OOS-01..OOS-19 (full MVP out-of-scope confirmation)

Entity touchpoints:
- ENT-09 (Notification) — owned here
- ENT-06 (User) — recipient reference (read-only; owned by slice 001)
- ENT-01 (Task) — source reference for assigned / changed notifications (read-only; owned by slice 002)
- ENT-08 (Comment) — source reference for @mention notifications (read-only; owned by slice 009)

Depends on:
- 008 (task-assignment) — emits `TaskAssigned`; source of assigned and assignee-task-changed triggers
- 009 (comments-mentions) — emits `UserMentioned`; source of mention triggers
- 016 (real-time-collaboration) — SignalR transport for live toast delivery

## User Scenarios & Testing *(mandatory)*

### User Story 16 - Notifications (Priority: P2)

Members get in-app notifications when assigned, @mentioned, or when items they are an assignee of change; a notification center, live toasts, mark-read, per-type preferences; in-app only.

**Why this priority**: Notifications close the loop on collaboration — members learn when something needs their attention. They depend on assignment, mentions, and real-time being present.

**Independent Test**: Can be tested by triggering an assignment and an @mention, verifying in-app notifications and a live toast when online, opening the notification center, marking notifications read, and disabling a notification type.

**Acceptance Scenarios** (owned by this slice):

1. **(US-16.AS-01) Given** a member, **When** they are assigned to a task or @mentioned, **Then** they receive an in-app notification (and a live toast if online).
2. **(US-16.AS-02) Given** the notification center, **When** they open it, **Then** notifications are listed newest-first with read/unread state.
3. **(US-16.AS-03) Given** an unread notification, **When** they mark it read (or mark all read), **Then** its state updates.
4. **(US-16.AS-04) Given** notification preferences, **When** they disable a type, **Then** they stop receiving that type.

## Requirements *(mandatory)*

### Functional Requirements (slice-specific)

- **FR-079**: System MUST generate an in-app notification when a user is assigned to a task, @mentioned, or when a task they are an assignee of changes. The "changed" trigger set is closed to: status, due date, assignee, and project-move; low-signal edits (e.g., description typos) MUST NOT notify. Rapid successive changes MUST be coalesced, and a change made by the recipient themselves MUST NOT notify them (self-suppression via `actorUserId`).
- **FR-080**: System MUST present a notification center listing the user's notifications newest-first with read/unread state. Resolving a notification's source MUST re-run deny-by-default authorization at read time (the source is a live reference, see FR-096); a source no longer accessible MUST render as "no longer available".
- **FR-081**: When the user is online, a new notification MUST also surface as a live toast.
- **FR-082**: Users MUST be able to mark a notification read and mark all read.
- **FR-083**: Users MUST be able to set per-type notification preferences (enable/disable a type).
- **FR-084**: Notifications MUST be in-app only; email and push/device notifications are out of scope.
- **FR-096**: Resolving a notification's source MUST re-run deny-by-default authorization; the payload MUST carry no content beyond what current authorization permits, and a denied source MUST surface as "no longer available".

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

Authentication & Authorization (per Constitution Principle IX):
- **FR-065**: Authorization MUST be dispatched by the containing resource's visibility (not a conjunction of tiers): personal/unprojected data authorizes on ownership (`createdBy`/`ownerId`) with queries scoped to the caller; shared-project entities authorize on current `ProjectMembership` + role. Every query MUST be scoped accordingly (per-user isolation).
- **FR-066**: Access to a shared project's data MUST require current membership in that project. `createdBy` and assignee are provenance only and confer NO standalone access; on leave/remove/unshare a user MUST lose ALL access to that project's data regardless of authorship or assignment.
- **FR-067**: Each operation on a shared-project resource MUST require sufficient role (viewer=read, editor=write, owner=manage); insufficient role MUST be denied.
- **FR-068**: Authorization MUST be deny-by-default and enforced at the API/handler layer for every read and write.

Time (per Constitution Principle X):
- **FR-092**: All timestamps MUST be stored in UTC, and every date-relative computation (Today/Upcoming membership, cycle boundaries, recurrence rollover, natural-language date resolution) MUST be evaluated against a single instance reference timezone, `Europe/Warsaw`, applied identically on client and server. A due date MUST distinguish date-only from date-time via a `has_time` flag, and DST transitions MUST be handled by the timezone library, not fixed-offset arithmetic.

Privacy & Account (per Constitution Principle XI):
- **FR-085**: System MUST provide an account-deletion path with a defined erasure cascade: anonymize the user's authored comments to a tombstone identity (not hard-deleting comments that anchor a thread); null or reassign `createdBy`/assignee references on tasks; transfer or delete the user's owned shared projects; and purge the user's recipient notifications.
- **FR-086**: System MUST state an explicit data-retention stance: backups, soft-deleted/undo-window data, comments, and notifications are retained until account deletion.

Security (per Constitution Principle XII):
- **FR-099**: User-authored content MUST be output-encoded/sanitized so raw HTML injection is impossible, and a Content-Security-Policy plus standard security response headers MUST be present in production.

### Key Entities

- **ENT-09 — Notification**: Has a recipient User, type (assigned/mentioned/changed), source reference, read flag, and created timestamp. The source reference is a live reference (re-authorized on dereference, see FR-096), not a content snapshot; if the source is no longer accessible it resolves to "no longer available".

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-014**: A change to a shared item is reflected on other members' open shared views within ~1 second.
- **SC-016**: Authorization coverage is mechanically verifiable: every data handler ships with both an allow and a deny test, and a role×operation deny matrix demonstrates that insufficient ownership/membership/role is rejected.
- **SC-017**: Deleting an account removes or anonymizes all of the user's personal data per the FR-085 cascade (no residual personally attributable data beyond the defined tombstone identity).

## Edge Cases

The following cases are slice-local to notifications (descriptive IDs to avoid colliding with the global EC-01..EC-12 reserved in product-vision.md):

- **EC-N1 — Source no longer accessible at dereference**: When a user opens the notification center or clicks a notification whose source has since become inaccessible (left/removed/unshared, or the source was deleted), the dereference re-runs deny-by-default authorization (FR-096) and the entry renders as "no longer available" rather than leaking any source content. The payload itself carries no content beyond what current authorization permits.
- **EC-N2 — Self-action suppression**: A change to a task (status / due date / assignee / project-move) made by the recipient themselves MUST NOT generate a notification for that recipient; suppression is keyed on `actorUserId` matching the would-be recipient.
- **EC-N3 — Rapid successive changes (coalescing)**: When a task an assignee follows is changed several times in quick succession, the "changed" notifications MUST be coalesced so the recipient is not flooded; a single coalesced notification represents the burst.
- **EC-N4 — Low-signal edit produces no notification**: An edit outside the closed trigger set (e.g., a description typo fix) MUST NOT generate a "changed" notification; only status, due date, assignee, and project-move qualify.
- **EC-N5 — Membership lost mid-flight**: If a recipient loses membership of the source's shared project between generation and dereference, the live SignalR subscription is revoked (FR-095, slice 016) and the already-listed notification resolves to "no longer available" on next dereference; no further live toasts for that source are delivered.
- **EC-N6 — Disabled type at generation time**: When a recipient has disabled a notification type (FR-083), the system MUST NOT generate or deliver notifications of that type to them, including the live toast.
- **EC-N7 — Account deletion purge**: When a user deletes their account, their recipient notifications are purged as part of the FR-085 erasure cascade.

## Retention Stance

Notifications are non-destructive records and are NOT covered by the 30-second data undo (FR-040). Per FR-086 (Constitution Principle XI), notifications are retained until account deletion; on account deletion the user's recipient notifications are purged as part of the FR-085 erasure cascade. Read/unread is reversible state and carries no separate retention window.

## Constitution Compliance

This slice is evaluated against constitution v4.0.0. Cross-cutting principles realized here:

- **I. Keyboard-First**: the notification center and per-type preferences are operable entirely via keyboard; opening, navigating, and marking notifications read require no mouse, and the toast carries a keyboard/focus-triggered affordance (FR-046).
- **II. Accessibility (WCAG 2.1 AA)**: FR-042 (focus indicator), FR-043 (ARIA roles/labels), FR-044 (contrast ≥ 4.5:1), FR-045 (no AT-binding collisions), FR-046 (no hover-only content), FR-047 (prefers-reduced-motion governs toast transitions), and FR-101 — server-initiated toasts are announced via an appropriate ARIA live region (polite by default, coalesced/rate-limited under fan-out) WITHOUT stealing focus, and the notification-center and preferences dialogs follow the dialog focus contract (set initial focus, trap focus, dismiss on Esc, return focus to the invoker on close). FR-031 keeps single-key shortcuts from hijacking text entry in the preferences UI.
- **III. Instant Response**: SC-014 — a triggering change fans out so the recipient's live toast surfaces within ~1s (p95) commit-to-paint over SignalR; mark-read / mark-all-read paint optimistically and reconcile against the server. Toasts are non-blocking and the UI accepts input while one is visible.
- **V. Connected, Server-Authoritative**: notifications are persisted server-side through the app's own API and PostgreSQL; the client holds no authoritative copy. Notifications are generated by server-side domain-event handlers, not synthesized on the client.
- **VI. Type Safety End-to-End**: the EF Core / PostgreSQL Notification schema is the source of truth, with C# entity types on the server and a generated TypeScript client on the Next.js web app kept in lockstep; runtime validation applies at the API request/response boundary, and the error contract is ProblemDetails-based (ADR-0009).
- **VII. Data Integrity & Resilience**: FR-049 (error + recovery), FR-050 (structured logging — never secrets), FR-051 (auto-backup before the migration that adds the Notification table and preferences). Notifications are non-destructive records; marking read is reversible state, so the 30-second data undo (FR-040) does not apply.
- **VIII. Test-First**: each owned acceptance scenario above is independently testable (Red-Green-Refactor); per SC-016 every notification read/write handler ships with both an allow and a deny test (a user cannot read or mutate another user's notifications or receive notifications sourced from a project they are not a current member of), and integration tests cover self-suppression, coalescing, low-signal exclusion, disabled-type suppression, and the "no longer available" dereference path.
- **IX. Authentication & Authorization**: authorization is deny-by-default and dispatched by the source's visibility (FR-065/FR-068) — personal sources authorize on ownership, shared-project sources on current `ProjectMembership` + role (FR-066/FR-067); `createdBy`/assignee are provenance only and never a standalone grant, so loss of membership revokes all access. Notification reads/mutations are scoped to the recipient (a user touches only their own notifications and preferences). FR-096: a notification's source is a live reference re-authorized on dereference; the payload carries no content beyond current authorization and a denied source renders "no longer available". Live subscriptions are authorized too: a membership/role change revokes the affected user's SignalR subscription (FR-095, slice 016), so a removed member receives no further toasts.
- **X. Time & Timezone**: notification `created_at` timestamps are stored in UTC (FR-092); newest-first ordering in the center and the coalescing window are computed against the single instance reference timezone, `Europe/Warsaw`, applied identically on client and server, with DST handled by the timezone library rather than fixed-offset arithmetic.
- **XI. Privacy & Personal Data**: notifications hold Google-derived recipient identity and references to user content; per FR-086 they are retained until account deletion, and per FR-085 a user's recipient notifications are purged as part of the account-deletion erasure cascade (SC-017).
- **XII. Security by Default**: any user-authored content surfaced in a toast or center entry (e.g., a task title or comment snippet) is untrusted and MUST be output-sanitized to a safe subset so raw HTML injection is impossible (FR-099); a Content-Security-Policy and standard security response headers are present in production, and secrets never appear in logs or error context (FR-050).

## Assumptions

- **ASM-01 — Multi-user team**: The application serves a small collaborating team (~10). Each user authenticates (Google) and has personal data plus access to shared projects.
- **ASM-02 — Web platform**: The MVP targets modern desktop browsers. Native mobile apps, PWA/offline operation, and cross-device sync are explicitly out of scope.
- **ASM-06 — In-app notifications only**: The app provides in-app notifications (assignment, mention, changes); email and push/device notifications and reminders are out of scope.
- **ASM-10 — Small team scale**: Small team (~10 users) on a single shared instance; not organizational multi-tenancy.
- **ASM-12 — Instance reference timezone**: The instance operates against a single reference timezone, `Europe/Warsaw`, for all date-relative computation; per-user timezones are out of scope.
- **ASM-13 — Gated admission**: Account creation is gated (email allowlist or Google Workspace hosted-domain); the instance is not open to any Google account or to public sign-up.

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

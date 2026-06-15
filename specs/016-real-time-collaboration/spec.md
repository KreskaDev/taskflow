# Feature Specification: Real-Time Collaboration

**Feature Branch**: `016-real-time-collaboration`

**Created**: 2026-06-13

**Status**: Draft

**Input**: Slice 016 of the TaskFlow MVP. Source of truth: `.specify/memory/product-vision.md`. Goal: live propagation of changes to shared items across members' open shared views via SignalR, with an inbound remote patch yielding to any in-flight local optimistic edit and reconciling under last-write-wins (whole-entity, with an optimistic-concurrency token to detect conflicts), and reconnect re-syncing the current state of visible shared views — layered on top of project sharing and the optimistic-UI model. The SignalR hub is reverse-proxied by Caddy directly to the api container (Next.js cannot proxy WebSocket upgrades). A mid-session membership or role change evicts the affected connection from the project's group (and/or re-checks at fan-out), forcing a 403 re-sync. No presence.

## Provenance

This slice realizes the following items from product-vision.md:

Slice-specific:
- US-15 (Real-Time Collaboration) — AS-01, AS-02, AS-03
- FR-076 (real-time propagation to open shared views via SignalR within the fan-out budget)
- FR-077 (inbound real-time update must not overwrite an in-flight local optimistic edit; last-write-wins after server-ack)
- FR-078 (reconnect re-syncs visible shared views)
- SC-014 (shared-item change reflected on other members' open shared views within ~1s)
- SC-015 (~10 concurrent users without perceptible degradation)

Slice-owned new (per Constitution Principle IX):
- FR-095 (live-subscription authorization: a membership/role change revokes or re-authorizes the affected user's active SignalR subscriptions — a removed member receives no further live patches and is forced to a no-access 403 re-sync)

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

Access control (per Constitution Principle IX — realized in this slice):
- FR-065 (authorization dispatched by the containing resource's visibility — personal→ownership, shared→membership+role; every query scoped per-user)
- FR-066 (access to a shared project's data requires current membership; createdBy/assignee are provenance only and confer no standalone access; leave/remove/unshare revokes ALL access)
- FR-067 (sufficient-role enforcement per operation)
- FR-068 (deny-by-default authorization at the API/handler layer for every read and write)

MVP boundary confirmed:
- OOS-01..OOS-19 (full MVP out-of-scope confirmation, including OOS-15 — presence indicators and activity/audit feed)

Entity touchpoints (no new entity introduced in this slice):
- ENT-01 (Task)
- ENT-02 (Project)
- ENT-07 (ProjectMembership)

Depends on:
- 007 (project-sharing-membership) — shared projects, membership, and roles are the precondition for real-time fan-out and its authorization scoping.

## User Scenarios & Testing *(mandatory)*

### User Story 15 - Real-Time Collaboration (Priority: P2)

When a member changes a shared item, other members viewing it see the change live.

**Why this priority**: Live updates make collaboration feel coherent — members see each other's work without manual refresh. It layers on top of sharing and the optimistic-UI model.

**Independent Test**: Can be tested by having two members view the same shared project, changing a task in one client and observing the live update in the other, verifying in-flight edits are not clobbered, and confirming re-sync after a dropped connection.

**Acceptance Scenarios** (owned by this slice):

1. **(US-15.AS-01) Given** two members viewing the same shared project, **When** one changes a task, **Then** the other's view updates live within ~1s without manual refresh.
2. **(US-15.AS-02) Given** a member with a pending local optimistic edit, **When** a remote update for the same item arrives, **Then** it does not clobber the in-flight edit (reconciles after the local server-ack under last-write-wins).
3. **(US-15.AS-03) Given** a dropped connection, **When** connectivity returns, **Then** the client reconnects and re-syncs the current state of visible shared views.

### Edge Cases

- **Concurrent edit to the same item**: When two members edit the same shared item near-simultaneously, reconciliation is whole-entity last-write-wins carrying an optimistic-concurrency token so the server can detect that a concurrent write intervened; the inbound remote patch reconciles after the local mutation's server-ack, and an in-flight local optimistic edit is never clobbered by an inbound update (FR-077, Principle III). When the local write's token is stale, the overwrite is surfaced rather than applied silently (FR-049).
- **Dropped connection during a live session**: When the SignalR connection drops, the client reconnects on connectivity return and re-syncs the current state of the visible shared views rather than replaying missed individual patches (FR-078).
- **Own change echoed back**: A member's own change MUST NOT be re-applied to the originating client as if it were a remote patch — fan-out echo is suppressed by the originating SignalR connection id, so the actor's optimistic state stands while every other subscribed connection in the group receives the update.
- **Mid-session membership or role change**: When a member is removed, leaves, is unshared, or has their role reduced while subscribed to the project's group, the change MUST immediately revoke or re-authorize that user's active SignalR subscriptions — the connection is evicted from the group (and/or re-checked at fan-out) so a removed/insufficiently-roled member receives no further live patches and is forced to a no-access (403) re-sync (FR-095, Principle IX). A removed member's optimistically applied state is reconciled away on the forced re-sync.
- **Non-member / insufficient role on the channel**: Real-time fan-out reaches only current members of the shared project, and only for data their role permits; access is dispatched by the resource's visibility and scoped by per-user isolation, current membership, and role, deny-by-default — createdBy/assignee confer no standalone access (FR-065, FR-066, FR-067, FR-068).

## Requirements *(mandatory)*

> **Definition — "open / visible shared view"**: a client is considered to have a shared view open/visible when it is currently subscribed to that shared project's SignalR group. Fan-out targets the group; echo-suppression is keyed on the originating connection id so the actor's own connection is not sent its own change back.

> **Transport**: the SignalR hub path is reverse-proxied by Caddy directly to the `api` container (the Next.js BFF cannot proxy the WebSocket upgrade); WS is the SLA-bearing transport (Architecture & Stack, Principle V).

> **Reconciliation model**: reconciliation is whole-entity last-write-wins. Each mutation carries an optimistic-concurrency token so the server can detect that a concurrent write intervened; a stale token surfaces the overwrite (FR-049) rather than applying it silently.

### Functional Requirements (slice-specific)

- **FR-076**: Changes to a shared item MUST propagate to other members' open shared views in real time (SignalR) within the fan-out budget.
- **FR-077**: An inbound real-time update MUST NOT overwrite an in-flight local optimistic edit; it reconciles under last-write-wins after the local mutation's server-ack.
- **FR-078**: On reconnect after a dropped connection, the client MUST re-sync the current state of visible shared views.

### Functional Requirements (slice-owned new)

- **FR-095**: A membership or role change MUST immediately revoke or re-authorize the affected user's active real-time (SignalR) subscriptions — a removed member receives no further live patches and is forced to a no-access (403) re-sync. Enforcement is at the hub/handler layer: the affected connection is evicted from the project's SignalR group and/or re-checked at fan-out, deny-by-default, so the live channel honors exactly the same dispatch-by-visibility authorization as every other read/write (Principle IX).

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

Authorization / Access Control (per Constitution Principle IX — dispatch by visibility, not a Tier A/B conjunction):
- **FR-065**: Authorization MUST be dispatched by the containing resource's visibility (not a conjunction of tiers): personal/unprojected data authorizes on ownership (`createdBy`/`ownerId`) with queries scoped to the caller; shared-project entities authorize on current `ProjectMembership` + role. Every query MUST be scoped accordingly (per-user isolation).
- **FR-066**: Access to a shared project's data MUST require current membership in that project. `createdBy` and assignee are provenance only and confer NO standalone access; on leave/remove/unshare a user MUST lose ALL access to that project's data regardless of authorship or assignment.
- **FR-067**: Each operation on a shared-project resource MUST require sufficient role (viewer=read, editor=write, owner=manage); insufficient role MUST be denied.
- **FR-068**: Authorization MUST be deny-by-default and enforced at the API/handler layer for every read and write.

### Key Entities

This slice introduces no new entity; it touches the following existing entities (canonical definitions owned by their originating slices):

- **ENT-01 — Task**: The core work item. Has a title (required), description, priority (P0-P3), status (backlog/todo/in_progress/done/cancelled), due date, labels, project reference, cycle reference, recurrence rule, createdBy (the User who created it), assignees (zero or more Users; only on shared-project tasks), and system timestamps (created_at, updated_at, completed_at). New tasks default to "backlog" status. A recurring task has a linked recurrence rule that generates successor instances.
- **ENT-02 — Project**: An organizational container for tasks. Has a name, color, icon, optional parent project reference, archived flag, ownerId (the owning User), and visibility (personal or shared). Supports one level of nesting. Contains zero or more tasks. Shared projects have a membership set.
- **ENT-07 — ProjectMembership**: Links a User to a shared Project with a role (owner/editor/viewer).

> Slice scope for entities: this slice adds no columns. It reads the existing shared-item state to fan out live updates over SignalR; the membership set (ENT-07) on a shared project (ENT-02) determines who receives an item's (ENT-01) updates and at what role.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-014**: A change to a shared item is reflected on other members' open shared views within ~1 second.
- **SC-015**: The system serves ~10 concurrent users performing typical operations without perceptible degradation.

## Constitution Compliance

This slice is evaluated against constitution v4.0.0. Principles realized here:

- **III. Instant Response**: this slice is the first to exercise the real-time reconciliation clause. FR-076 delivers server-initiated SignalR updates to members of shared views; FR-077 makes an inbound remote patch resolve under whole-entity last-write-wins (with an optimistic-concurrency token to detect conflicts) but yield to a pending local optimistic mutation until that mutation's server acknowledgement resolves — a remote update never clobbers an in-flight local edit. Echo of the actor's own change is suppressed by originating connection id. Animations and the live update path are non-blocking; the UI keeps accepting input while updates arrive. SC-014 (fan-out p95 within ~1s, commit-to-paint) is the perceived-speed budget.
- **V. Connected, Server-Authoritative**: live updates flow over SignalR from the C# backend, which remains the single source of truth; the client holds no authoritative copy and reconciles to server state on each patch and on reconnect (FR-078). No third-party runtime data service is introduced — SignalR is the app's own real-time channel. Per Architecture & Stack, the hub path is reverse-proxied by Caddy directly to the `api` container (the Next.js BFF cannot proxy WebSocket upgrades); WS is the SLA-bearing transport.
- **VII. Data Integrity & Resilience**: FR-049 (error + recovery, e.g. on a failed reconnect/re-sync, and surfacing an LWW overwrite when an optimistic-concurrency token is stale), FR-050 (structured logging of connection and reconciliation events; never secrets), FR-051 (auto-backup before migration — applies to any schema change carried by this slice).
- **IX. Authentication & Authorization**: authorization is deny-by-default and **dispatched by the containing resource's visibility — NOT a Tier A/B conjunction**: personal/unprojected data authorizes on ownership; shared-project entities authorize on current `ProjectMembership` + role (FR-065, FR-066, FR-067, FR-068). `createdBy`/assignee are **provenance only** and confer no standalone access; leave/remove/unshare revokes ALL access. Real-time fan-out is gated by exactly this policy, enforced at the hub/API/handler layer — a member receives updates only for shared projects they currently belong to and only for data their role permits. **Live subscriptions are authorized too** (FR-095): a membership or role change immediately revokes or re-authorizes the affected user's active SignalR subscriptions (eviction from the project's group and/or re-check at fan-out), forcing a removed member to a no-access (403) re-sync. Sessions/admission established in slices 001/007 remain the precondition: every subscriber is an authenticated, admission-gated user; an unauthenticated or non-member connection is denied.
- **II. Accessibility (WCAG 2.1 AA)**: FR-042 (focus indicator), FR-043 (ARIA roles/labels), FR-044 (contrast ≥ 4.5:1), FR-045 (no AT-binding collisions), FR-046 (no hover-only content), FR-047 (prefers-reduced-motion — live-update transitions respect reduced motion). **FR-101 is central to this slice**: every server-initiated SignalR update applied to a shared view MUST be announced to assistive technology via an appropriate ARIA live region (polite by default, coalesced/rate-limited under concurrent fan-out) without stealing focus, and any confirmation/re-sync dialog (e.g. the forced 403 re-sync) MUST follow the dialog focus contract (set initial focus, trap focus, dismiss on Esc, return focus to the invoker). FR-031 keeps single-key shortcuts from hijacking text entry.
- **X. Time & Timezone**: real-time payloads carry timestamps stored in UTC; any date-relative rendering on the receiving client (e.g. a patched due date landing a task in Today vs Upcoming) MUST be evaluated against the single instance reference timezone `Europe/Warsaw`, applied identically on client and server, with `has_time` preserved and DST handled by the timezone library — so a live patch resolves to the same date boundary fact on every client.
- **XI. Privacy & Personal Data**: live fan-out MUST NOT leak personal data across the membership boundary — a non-member or removed member receives no patches (FR-095, FR-066), and patch payloads carry no content beyond what the recipient's current authorization permits. This slice introduces no new personal-data store; account-deletion/erasure and retention remain governed by their owning slices.
- **XII. Security by Default**: user-authored content carried in a live patch (task markdown, comment bodies, @mention tokens) is untrusted and MUST be output-encoded/sanitized to a safe subset on render so a real-time patch cannot deliver stored XSS; the CSP/security headers and `connect-src`/WebSocket origin allowances MUST permit the hub origin while remaining restrictive; SignalR/hub credentials and signing keys are runtime-injected secrets, never committed, baked, or logged.
- **Performance Standards**: the real-time fan-out budget (p95 under 1000 ms commit-to-paint on the receiving client, SC-014) and the concurrency target (~10 concurrent users within the 200 ms write / 16 ms paint budgets without perceptible degradation, SC-015) are exercised by this slice; the per-user authorization-scoped accessible working set is the anchor for any seeded load.
- **VIII. Test-First**: each owned acceptance scenario above (US-15.AS-01..03) is independently testable (Red-Green-Refactor), including the in-flight-edit reconciliation and reconnect re-sync paths; the live channel ships both an allow test (a member receives a patch) and a deny test (a non-member/removed member/insufficient role does not, and is force-re-synced) per the SC-016 authz coverage mechanism.

## Assumptions

- **ASM-01 — Multi-user team**: The application serves a small collaborating team (~10). Each user authenticates (Google) and has personal data plus access to shared projects.
- **ASM-02 — Web platform**: The MVP targets modern desktop browsers. Native mobile apps, PWA/offline operation, and cross-device sync are explicitly out of scope.
- **ASM-10 — Small team scale**: Small team (~10 users) on a single shared instance; not organizational multi-tenancy.
- **ASM-12 — Instance reference timezone**: The instance operates against a single reference timezone, `Europe/Warsaw`, for all date-relative computation; per-user timezones are out of scope.
- **ASM-13 — Gated admission**: Account creation is gated (email allowlist or Google Workspace hosted-domain); the instance is not open to any Google account or to public sign-up.

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

> Within-MVP boundary for this slice: in-app notifications (live toasts and the notification center) are delivered in slice 017 (notifications), which builds on this slice's SignalR channel. This slice fans out item changes to open shared views only — it adds no presence indicators or activity feed (OOS-15). Multi-user collaboration and in-app notifications are explicitly IN scope (OOS-01 promoted; OOS-06 partial); this slice asserts no out-of-scope status for them.

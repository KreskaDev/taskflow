# Feature Specification: Accounts & Auth

**Feature Branch**: `001-accounts-and-auth`

**Created**: 2026-06-13

**Status**: Draft

**Input**: Slice 001 of the TaskFlow MVP. Source of truth: `.specify/memory/product-vision.md`. Goal: admission-gated Google OAuth sign-in (allowlist / Google Workspace hosted-domain `hd`), sign-out, hardened HttpOnly cookie sessions via the Next.js BFF, an integrity-protected BFF→API identity carrier, profile (name/avatar), account deletion with an erasure cascade, security baseline (CSP/sanitization/secrets), and deny-by-default rejection of unauthenticated requests — the foundational authentication and deny-by-default per-user-isolation layer the collaborative multi-user product stands on.

## Provenance

This slice realizes the following items from product-vision.md:

Slice-specific:
- US-11 (Account & Sign-In) — full: AS-01, AS-02, AS-03, AS-04
- US-17 (Account & Data Management) — account-deletion path with erasure cascade (AS-02, AS-03, AS-04); the export portion (AS-01) is owned by slice 015 (data-export-import)
- FR-052 (Google OAuth sign-in with admission-gated sign-up; first sign-in by an admitted user creates, returning matches, non-admitted rejected)
- FR-053 (HttpOnly cookie sessions via the web BFF; no tokens to client JS)
- FR-054 (sign-out ends the session)
- FR-055 (unauthenticated requests to protected routes denied, deny-by-default, directed to sign-in)
- FR-056 (display signed-in user's Google profile — display name, avatar)
- FR-085 (account deletion & erasure cascade) — owned new
- FR-086 (data-retention stance) — owned new
- FR-087 (admission control: allowlist / Workspace `hd`) — owned new
- FR-088 (session policy: absolute+idle lifetime, rotation at OAuth completion, server-side sign-out invalidation, Postgres-backed store) — owned new
- FR-089 (CSRF + explicit SameSite on BFF mutations) — owned new
- FR-090 (OAuth hardening: state, nonce, PKCE, id_token validation) — owned new
- FR-091 (BFF→API identity carrier integrity: signed short-lived token; API port internal-only) — owned new
- FR-099 (content sanitization & CSP / security headers baseline) — owned new
- FR-100 (secrets handling: runtime-injected, never committed/baked/logged) — owned new
- SC-013 (authorization enforced on 100% of data operations; deny cases covered by integration tests)
- SC-016 (authz coverage foundation: every data handler ships an allow and a deny test; role×operation deny matrix) — owned new
- SC-017 (account deletion removes/anonymizes all personal data per the FR-085 cascade) — owned new
- ENT-06 (User)
- ASM-10 (small team scale), ASM-11 (Google identity provider), ASM-13 (gated admission) — owned new
- ASM-12 (instance reference timezone, Europe/Warsaw) — referenced

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

Access-control (deny-by-default authentication + deny-by-default per-user-isolation foundation established here):
- FR-065 (authorization dispatched by the containing resource's visibility — personal/unprojected on ownership with queries scoped to the caller; shared-project on membership + role — per-user isolation)
- FR-066 (shared-project access requires current membership; `createdBy`/assignee are provenance only and confer no standalone access; leave/remove/unshare revokes ALL access)
- FR-067 (each operation on a shared-project resource requires sufficient role; insufficient role denied)
- FR-068 (authorization deny-by-default, enforced at the API/handler layer for every read and write)

MVP boundary confirmed:
- OOS-01..OOS-19 (full MVP out-of-scope confirmation)

Entity touchpoints:
- ENT-06 (User) — owned and established here

Depends on:
- none (foundational slice)

## Clarifications

### Session 2026-06-17

- Q: After account deletion (irreversible), when the same Google identity signs in again, what happens? → A: A brand-new empty account is created; the old account and its data stay erased (deletion removes the account, not the right to return).
- Q: How is the deleted user's own record handled to satisfy SC-017 (no residual PII)? → A: The User row is hard-deleted (no soft-delete `deleted_at`); its sessions are purged and the Google identity is freed. Later-slice references repoint to the separate, pre-seeded "Deleted User" tombstone via the deletion event.
- Q: Must admission require Google's `email_verified == true`? → A: Yes — reject sign-in if `email_verified` is absent or false, even on an allowlist email match (closes the unverified-email admission bypass).
- Q: When neither `ADMISSION_EMAILS` nor `ADMISSION_HD` is configured at boot, what happens? → A: The system fails fast at startup with a clear configuration error (no open and no silently-locked-out instance).

## User Scenarios & Testing *(mandatory)*

### User Story 11 - Account & Sign-In (Priority: P1)

A team member signs in with Google to reach their tasks and shared projects; sign-in is admission-gated (allowlist / Google Workspace `hd`); can sign out; profile shows Google name/avatar.

**Why this priority**: Authentication is the gate to every collaborative feature — without an identity there are no personal data, no shared projects, and no authorization. This is the foundation the multi-user product stands on.

**Independent Test**: Can be tested by signing in with Google as an admitted visitor, verifying account creation and landing in the workspace, confirming a non-admitted sign-in is rejected, then signing out and confirming protected views are no longer accessible.

**Acceptance Scenarios** (owned by this slice):

1. **(US-11.AS-01) Given** a signed-out admitted visitor, **When** they choose "Sign in with Google" and complete OAuth, **Then** a new account is created or a returning one matched, and they land in their workspace; a non-admitted sign-in (not on the allowlist / outside the Workspace `hd`) is rejected and no account is created.
2. **(US-11.AS-02) Given** a signed-in user, **When** they sign out, **Then** the session ends and protected views are no longer accessible.
3. **(US-11.AS-03) Given** an unauthenticated request to any protected route/endpoint, **When** it is made, **Then** it is denied (deny-by-default) and the user is directed to sign in.
4. **(US-11.AS-04) Given** a signed-in user, **When** they open their profile, **Then** their Google display name and avatar are shown.

### User Story 17 - Account & Data Management (Priority: P2)

A signed-in user can delete their account, which erases or anonymizes their personal data across the system per the defined cascade. Account deletion is irreversible and is distinct from signing out. (The data-export portion of US-17 — exporting one's own/accessible data — is delivered by slice 015; only the deletion/erasure path is owned here.)

**Why this priority**: Holding Google-derived personal data carries a privacy obligation (Constitution Principle XI): a user must be able to have their data removed. This is a trust and compliance requirement, not part of the daily flow, so it ranks below the core loops but above optional enhancements.

**Independent Test**: Can be tested by signing in, then deleting the account and verifying the deleted account is gone — the User record is hard-deleted, the prior session no longer works, and a later sign-in by the same Google identity yields a brand-new empty account rather than the deleted one — and that their personal data has been erased or anonymized (owned shared projects transferred or deleted, authored comments anonymized to a tombstone identity, `createdBy`/assignee references nulled or reassigned, recipient notifications purged). At this slice — where only the User aggregate exists — the deletion path and its cascade contract are established and tested for the User identity; later slices wire their entities into the same cascade.

**Acceptance Scenarios** (owned by this slice):

1. **(US-17.AS-02) Given** a signed-in user, **When** they request account deletion and confirm, **Then** their account is deleted and they can no longer sign in with it.
2. **(US-17.AS-03) Given** a user who deletes their account, **When** the erasure cascade runs, **Then** their owned shared projects are transferred or deleted, their authored comments are anonymized to a tombstone identity, their `createdBy`/assignee references are nulled or reassigned, and their notifications are purged.
3. **(US-17.AS-04) Given** a user who deletes their account, **When** the deletion completes, **Then** comments they authored that anchor a thread remain present but attributed to the anonymized tombstone identity rather than being hard-deleted.

> The account-deletion confirmation is a destructive, irreversible action and MUST follow the dialog focus contract (FR-101) and present a clear blast-radius confirmation. Account deletion is NOT covered by the 30-second data undo; it is a deliberate, confirmed erasure.

### First-Run / Empty State

- **First sign-in, empty workspace (EC-01 family)**: When an admitted user signs in for the first time and a fresh account is created, they land in an empty workspace. The empty state MUST be a helpful, accessible empty state (not a blank screen) that orients the user to their now-isolated personal workspace — consistent with Principle IV (no onboarding wizards, modal interruptions, or tooltips-on-first-run). No other user's data is ever visible (deny-by-default per-user isolation, FR-065/FR-068).

### Edge Cases

- **Non-admitted sign-in**: A Google sign-in by a visitor who is not on the allowlist and is outside the Workspace `hd` is rejected; no account is created; the user is shown a clear, recoverable message (US-11.AS-01, FR-087, FR-049). A sign-in whose id_token `email_verified` claim is not `true` is likewise rejected even if the email matches the allowlist (FR-087).
- **Admission unconfigured**: If neither an email allowlist (`ADMISSION_EMAILS`) nor a Workspace hosted-domain (`ADMISSION_HD`) is configured, the system fails fast at startup with a clear configuration error rather than booting in an open or an all-denying state (FR-087).
- **Unauthenticated access**: Any request to a protected route or endpoint without a valid session is denied (deny-by-default) and the user is directed to sign in (US-11.AS-03, FR-055, FR-068).
- **Deny-by-default per-user isolation**: Authorization is dispatched by the containing resource's visibility. For the data established here (the User's own identity and, going forward, personal/unprojected data), access authorizes on ownership and every query is scoped to the caller; `createdBy` is provenance only and never a standalone grant. Cross-user reads/writes are denied and covered by integration tests (FR-065, FR-066, SC-013, SC-016).
- **Session expiry / sign-out invalidation**: After the absolute lifetime or idle timeout elapses, or after a server-side sign-out, the session is invalid server-side; subsequent requests with the old cookie are denied and directed to sign in (FR-088, FR-055).
- **CSRF / cross-origin mutation**: A state-changing request through the BFF that fails the origin check or lacks a valid anti-CSRF token is rejected (FR-089).
- **Account deletion is irreversible**: The deleted account and its data cannot be recovered and the erasure cascade is not undoable (distinct from FR-040 data undo). If the same Google identity signs in again later, a brand-new empty account is created — the prior account is NOT restored (US-17.AS-02, FR-085).

## Requirements *(mandatory)*

### Functional Requirements (slice-specific)

- **FR-052**: System MUST support sign-in via Google OAuth with admission-gated sign-up (see FR-087): first-time sign-in by an admitted user creates an account, returning sign-in matches the existing one, and non-admitted sign-ins are rejected.
- **FR-053**: Sessions MUST be HttpOnly cookies issued and managed by the web BFF; auth tokens MUST NOT be exposed to client JavaScript.
- **FR-054**: System MUST allow sign-out, ending the session.
- **FR-055**: Unauthenticated requests to protected routes/endpoints MUST be denied (deny-by-default) and directed to sign-in.
- **FR-056**: System MUST display the signed-in user's Google profile (display name, avatar).

### Privacy & Account Requirements (per Constitution Principle XI)

- **FR-085**: System MUST provide an account-deletion path with a defined erasure cascade: anonymize the user's authored comments to a tombstone identity (not hard-deleting comments that anchor a thread); null or reassign `createdBy`/assignee references on tasks; transfer or delete the user's owned shared projects; and purge the user's recipient notifications. The user's own User record MUST be **hard-deleted** (NOT soft-deleted): its sessions are purged and its Google identity (`google_subject_id`, email) is freed, so that a later sign-in by the same Google account creates a brand-new empty account rather than restoring the deleted one. The "Deleted User" tombstone is a separate, pre-seeded record that later-slice references repoint to; the deleted user's own row is removed, not retained.
- **FR-086**: System MUST state an explicit data-retention stance: backups, soft-deleted/undo-window data, comments, and notifications are retained until account deletion.

### Authentication & Admission Requirements (per Constitution Principle IX)

- **FR-087**: System MUST gate account creation by admission control — an explicit email allowlist or a Google Workspace hosted-domain (`hd`); sign-in is NOT open to any Google account, and non-admitted sign-ins MUST be rejected. Admission MUST additionally require the Google id_token `email_verified` claim to be `true`: a sign-in whose `email_verified` is absent or `false` MUST be rejected even when the email matches the allowlist. If neither an allowlist nor a hosted-domain is configured, the system MUST fail to start (fail-closed startup guard) rather than boot in an open or an all-denying state.
- **FR-088**: Sessions MUST follow a documented policy: a server-enforced absolute lifetime and idle timeout, a new session id issued at OAuth completion (fixation defense), server-side invalidation on sign-out, backed by a Postgres-backed session store.
- **FR-089**: The session cookie MUST set an explicit `SameSite` value, and every state-changing request through the BFF MUST be CSRF-protected (origin check or anti-CSRF token).
- **FR-090**: The OAuth flow MUST use `state`, `nonce`, and PKCE and MUST validate the id_token.
- **FR-091**: The BFF→API identity carrier MUST be integrity-protected (a signed, short-lived token over the internal network); the API port MUST NOT be externally reachable.

### Security Requirements (per Constitution Principle XII)

- **FR-099**: User-authored content MUST be output-encoded/sanitized so raw HTML injection is impossible, and a Content-Security-Policy plus standard security response headers MUST be present in production. (No user-authored content is rendered in this slice beyond Google-provided profile fields, which MUST themselves be output-encoded; the CSP and security-header baseline is established here for all later slices.)
- **FR-100**: Secrets (session signing key, OAuth client secret, database/broker credentials, deploy keys) MUST be injected at runtime via environment or a secret store, never committed to the repository or baked into images, and MUST NOT appear in logs or error context.

### Access-control Requirements (deny-by-default per-user-isolation foundation established in this slice)

Authentication & Authorization (per Constitution Principle IX):
- **FR-065**: Authorization MUST be dispatched by the containing resource's visibility (not a conjunction of tiers): personal/unprojected data authorizes on ownership (`createdBy`/`ownerId`) with queries scoped to the caller; shared-project entities authorize on current `ProjectMembership` + role. Every query MUST be scoped accordingly (per-user isolation).
- **FR-066**: Access to a shared project's data MUST require current membership in that project. `createdBy` and assignee are provenance only and confer NO standalone access; on leave/remove/unshare a user MUST lose ALL access to that project's data regardless of authorship or assignment.
- **FR-067**: Each operation on a shared-project resource MUST require sufficient role (viewer=read, editor=write, owner=manage); insufficient role MUST be denied.
- **FR-068**: Authorization MUST be deny-by-default and enforced at the API/handler layer for every read and write.

> Dispatch foundation: this slice establishes the deny-by-default dispatch mechanism and the ownership branch (personal/unprojected → ownership, queries scoped to the caller). The shared-project membership + role branch (FR-066, FR-067) has no shared projects to act on yet — it is realized when project sharing arrives in slice 007 — but is stated here because the single dispatch policy and its allow+deny test discipline (SC-016) are stood up in this slice and every later handler plugs into it.

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

### Key Entities

- **ENT-06 — User**: Identity from Google (subject id, email, display name, avatar).

> Slice scope for ENT-06: this slice establishes the User aggregate — Google subject id, email, display name, and avatar — as the identity that every later slice's ownership and membership (`createdBy`, `ownerId`, assignees, ProjectMembership) references. The account-deletion cascade (FR-085) and the tombstone/anonymized identity it reassigns to are anchored on this aggregate. No other entity is persisted here.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-013**: Authorization is enforced on 100% of data operations (no read/write bypasses the policy; deny cases covered by integration tests).
- **SC-016**: Authorization coverage is mechanically verifiable: every data handler ships with both an allow and a deny test, and a role×operation deny matrix demonstrates that insufficient ownership/membership/role is rejected. (Established here as the foundation: the handlers introduced in this slice — sign-in/admission, profile read, account deletion — each ship an allow and a deny test, standing up the matrix that every later slice extends.)
- **SC-017**: Deleting an account removes or anonymizes all of the user's personal data per the FR-085 cascade (no residual personally attributable data beyond the defined tombstone identity); the user's own User record is hard-deleted, leaving no residual row.

## Constitution Compliance

This slice is evaluated against constitution v4.0.0. Principles realized here:

- **I. Keyboard-First**: the sign-in, sign-out, profile, and account-deletion surfaces are operable entirely via keyboard; FR-031 keeps single-key shortcuts from hijacking text entry on any auth form fields.
- **II. Accessibility (WCAG 2.1 AA)**: FR-042 (focus indicator), FR-043 (ARIA roles/labels), FR-044 (contrast ≥ 4.5:1), FR-045 (no AT-binding collisions), FR-046 (no hover-only content), FR-047 (prefers-reduced-motion) apply to the sign-in screen, profile view, and the empty first-run workspace rendered here. Per the strengthened Principle II, FR-101 applies: any server-initiated update or toast surfaced in this slice (e.g., a sign-in failure or session-expiry notice) MUST be announced via an appropriate ARIA live region without stealing focus, and the account-deletion confirmation dialog MUST follow the dialog focus contract (set initial focus, trap focus, dismiss on Esc, return focus to the invoker on close).
- **IV. Minimalist UI**: the first-run empty workspace is a clean, purposeful empty state with no onboarding wizard, no tooltips-on-first-run, and no modal interruptions.
- **V. Connected, Server-Authoritative**: Google OAuth is the single permitted external runtime dependency, and only for sign-in — never for storing application data. The User identity and session store are persisted server-side in PostgreSQL through the application's own API.
- **VI. Type Safety End-to-End**: the EF Core / PostgreSQL schema is the source of truth for the User entity, with C# entity types on the server and TypeScript types on the Next.js client kept in lockstep via the OpenAPI-generated client, and runtime validation (Zod on the web; FluentValidation / data annotations at the API) at the API request/response and session boundaries.
- **VII. Data Integrity & Resilience**: FR-049 (error + recovery on failed/non-admitted sign-in and session errors), FR-050 (structured logging that never logs secrets — see FR-100), FR-051 (auto-backup infrastructure — a no-op at this slice since this is the first migration and there is no prior schema to migrate, but the backup-before-migration hook and restore path are in place). Account deletion (FR-085) is a deliberate erasure and is correctly excluded from the 30-second data undo.
- **VIII. Test-First**: each owned acceptance scenario above is independently testable (Red-Green-Refactor); integration tests cover authorization, including that a request without the required ownership is denied (SC-013), and every data handler introduced here ships both an allow and a deny test (SC-016).
- **IX. Authentication & Authorization**: this slice establishes the foundation. Authentication is via Google OAuth gated by admission control — an explicit allowlist or Google Workspace hosted-domain `hd`; sign-in is not open to any Google account and non-admitted sign-ins are rejected (FR-052, FR-087). The OAuth flow uses `state`, `nonce`, and PKCE and validates the id_token (FR-090). Sessions are HttpOnly cookies issued and managed by the single-origin Next.js BFF with no tokens exposed to client JavaScript (FR-053), follow a documented policy with a server-enforced absolute lifetime and idle timeout, a new session id issued at OAuth completion, server-side invalidation on sign-out, and a Postgres-backed store (FR-088, FR-054); the cookie sets an explicit `SameSite` value and every state-changing BFF request is CSRF-protected (FR-089); and the BFF→API identity carrier is a signed, short-lived token over the internal network with the API port not externally reachable (FR-091). Authorization is deny-by-default and enforced at the API/handler layer for every read and write (FR-068), dispatched by the containing resource's visibility — personal/unprojected data authorizes on ownership with queries scoped to the caller, while `createdBy`/assignee are provenance only and never a standalone grant (FR-065). This is dispatch-by-visibility, NOT a conjunction of tiers. The shared-project membership + role branch (FR-066, FR-067) and the live-subscription, transfer-owner, and notification-dereference authz rules build on this foundation and are delivered in later slices (sharing in 007, real-time in 016, notifications in 017). The signed-in user's Google profile is shown (FR-056).
- **X. Time & Timezone**: timestamps recorded on the User aggregate and session store (created/last-seen, session absolute/idle deadlines) MUST be stored in UTC; the instance operates against a single reference timezone, `Europe/Warsaw` (ASM-12), with no per-user timezones (OOS-19). No date-relative view computation occurs in this slice, but the storage and reference-zone discipline is established here.
- **XI. Privacy & Personal Data**: this slice holds Google-derived PII (subject id, email, display name, avatar), so the account-deletion / erasure path (FR-085) and an explicit data-retention stance (FR-086) are owned here. The erasure cascade — transfer-or-delete owned shared projects, anonymize authored comments to a tombstone identity, null/reassign `createdBy`/assignee, purge recipient notifications — is defined here and is satisfied for later entities as those slices wire into it; SC-017 asserts no residual personally attributable data beyond the tombstone identity.
- **XII. Security by Default**: a Content-Security-Policy and standard security response headers are established for production, and any rendered content (here, Google profile fields) is output-encoded so raw HTML injection is impossible (FR-099). Secrets — the session signing key, OAuth client secret, database/broker credentials, deploy keys — are injected at runtime and never committed, baked into images, or written to logs or error context (FR-100).

## Assumptions

- **ASM-10 — Small team scale**: Small team (~10 users) on a single shared instance; not organizational multi-tenancy.
- **ASM-11 — Google identity provider**: Google is the sole identity provider for the MVP.
- **ASM-12 — Instance reference timezone**: The instance operates against a single reference timezone, `Europe/Warsaw`, for all date-relative computation; per-user timezones are out of scope.
- **ASM-13 — Gated admission**: Account creation is gated (email allowlist or Google Workspace hosted-domain); the instance is not open to any Google account or to public sign-up.

## Out of Scope

This slice confirms the full MVP out-of-scope boundary (OOS-01..OOS-19 from product-vision.md):

- **OOS-01**: [PROMOTED to in-scope — see US-11, US-12] Multi-user collaboration, sharing, permissions
- **OOS-02**: Cross-device sync, cloud storage
- **OOS-03**: Mobile application, PWA
- **OOS-04**: AI features (auto-categorization, summaries, suggestions)
- **OOS-05**: External integrations (calendar, Slack, GitHub, email)
- **OOS-06**: [PARTIALLY promoted; in-app notifications now in scope (US-16)] In-app notifications are now in scope (US-16); push/device notifications and reminders remain out of scope.
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

Additionally, the collaborative features that build on this authentication and deny-by-default per-user-isolation foundation are delivered in later slices within the MVP (not part of slice 001): project sharing, membership & roles (007), task assignment (008), comments & @mentions (009), real-time collaboration (016), and notifications (017). The shared-project membership + role authorization branch (FR-066, FR-067, FR-061), the ownership-transfer command and last-owner guard (FR-094), live-subscription authorization (FR-095), and notification-dereference re-authorization (FR-096) are owned by those later slices. Multi-user collaboration and in-app notifications are explicitly IN scope for the MVP (US-11/US-12 and US-16); they are not asserted as out of scope here.

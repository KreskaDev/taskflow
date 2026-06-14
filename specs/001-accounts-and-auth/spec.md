# Feature Specification: Accounts & Auth

**Feature Branch**: `001-accounts-and-auth`

**Created**: 2026-06-13

**Status**: Draft

**Input**: Slice 001 of the TaskFlow MVP. Source of truth: `.specify/memory/product-vision.md`. Goal: Google OAuth open sign-up, sign-out, HttpOnly cookie sessions via the Next.js BFF, profile (name/avatar), and deny-by-default rejection of unauthenticated requests — the foundational authentication and per-user-isolation layer the collaborative multi-user product stands on.

## Provenance

This slice realizes the following items from product-vision.md:

Slice-specific:
- US-11 (Account & Sign-In) — full: AS-01, AS-02, AS-03, AS-04
- FR-052 (Google OAuth sign-in with open sign-up; first sign-in creates, returning matches)
- FR-053 (HttpOnly cookie sessions via the web BFF; no tokens to client JS)
- FR-054 (sign-out ends the session)
- FR-055 (unauthenticated requests to protected routes denied, deny-by-default, directed to sign-in)
- FR-056 (display signed-in user's Google profile — display name, avatar)
- SC-013 (authorization enforced on 100% of data operations; deny cases covered by integration tests)
- ENT-06 (User)
- ASM-10 (small team scale), ASM-11 (Google identity provider)

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
- FR-051 (auto-backup before migration)

Access-control (deny-by-default authentication + per-user-isolation foundation established here):
- FR-065 (every query scoped to data the caller owns or has membership access to — per-user isolation)
- FR-068 (authorization deny-by-default, enforced at the API/handler layer for every read and write)

MVP boundary confirmed:
- OOS-01..OOS-17 (full MVP out-of-scope confirmation)

Entity touchpoints:
- ENT-06 (User) — owned and established here

Depends on:
- none (foundational slice)

## User Scenarios & Testing *(mandatory)*

### User Story 11 - Account & Sign-In (Priority: P1)

A team member signs in with Google to reach their tasks and shared projects (open sign-up); can sign out; profile shows Google name/avatar.

**Why this priority**: Authentication is the gate to every collaborative feature — without an identity there are no personal data, no shared projects, and no authorization. This is the foundation the multi-user product stands on.

**Independent Test**: Can be tested by signing in with Google as a new visitor, verifying account creation and landing in the workspace, then signing out and confirming protected views are no longer accessible.

**Acceptance Scenarios** (owned by this slice):

1. **(US-11.AS-01) Given** a signed-out visitor, **When** they choose "Sign in with Google" and complete OAuth, **Then** a new account is created or a returning one matched, and they land in their workspace.
2. **(US-11.AS-02) Given** a signed-in user, **When** they sign out, **Then** the session ends and protected views are no longer accessible.
3. **(US-11.AS-03) Given** an unauthenticated request to any protected route/endpoint, **When** it is made, **Then** it is denied (deny-by-default) and the user is directed to sign in.
4. **(US-11.AS-04) Given** a signed-in user, **When** they open their profile, **Then** their Google display name and avatar are shown.

### Edge Cases

- **Unauthenticated access**: Any request to a protected route or endpoint without a valid session is denied (deny-by-default) and the user is directed to sign in (US-11.AS-03, FR-055, FR-068).
- **Per-user isolation**: Every data query is scoped to the caller's identity; a caller may only read or write data they own (FR-065). Cross-user reads/writes are denied and covered by integration tests (SC-013).

## Requirements *(mandatory)*

### Functional Requirements (slice-specific)

- **FR-052**: System MUST support sign-in via Google OAuth with open sign-up; first-time sign-in creates an account, returning sign-in matches the existing one.
- **FR-053**: Sessions MUST be HttpOnly cookies issued and managed by the web BFF; auth tokens MUST NOT be exposed to client JavaScript.
- **FR-054**: System MUST allow sign-out, ending the session.
- **FR-055**: Unauthenticated requests to protected routes/endpoints MUST be denied (deny-by-default) and directed to sign-in.
- **FR-056**: System MUST display the signed-in user's Google profile (display name, avatar).

### Access-control Requirements (foundation established in this slice)

Authentication & Authorization (per Constitution Principle IX):
- **FR-065**: Every query MUST be scoped to data the caller owns or has membership access to (per-user isolation).
- **FR-068**: Authorization MUST be deny-by-default and enforced at the API/handler layer for every read and write.

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

- **ENT-06 — User**: Identity from Google (subject id, email, display name, avatar).

> Slice scope for ENT-06: this slice establishes the User aggregate — Google subject id, email, display name, and avatar — as the identity that every later slice's ownership and membership (`createdBy`, `ownerId`, assignees, ProjectMembership) references. No other entity is persisted here.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-013**: Authorization is enforced on 100% of data operations (no read/write bypasses the policy; deny cases covered by integration tests).

## Constitution Compliance

This slice is evaluated against constitution v3.0.0. Principles realized here:

- **I. Keyboard-First**: the sign-in, sign-out, and profile surfaces are operable entirely via keyboard; FR-031 keeps single-key shortcuts from hijacking text entry on any auth form fields.
- **II. Accessibility (WCAG 2.1 AA)**: FR-042 (focus indicator), FR-043 (ARIA roles/labels), FR-044 (contrast ≥ 4.5:1), FR-045 (no AT-binding collisions), FR-046 (no hover-only content), FR-047 (prefers-reduced-motion) apply to the sign-in screen and profile view rendered here.
- **V. Connected, Server-Authoritative**: Google OAuth is the single permitted external runtime dependency, and only for sign-in — never for storing application data. The User identity is persisted server-side in PostgreSQL through the application's own API.
- **VI. Type Safety End-to-End**: the EF Core / PostgreSQL schema is the source of truth for the User entity, with C# entity types on the server and TypeScript types on the Next.js client kept in lockstep, and runtime validation at the API request/response and session boundaries.
- **VII. Data Integrity & Resilience**: FR-049 (error + recovery on failed sign-in/session errors), FR-050 (structured logging), FR-051 (auto-backup infrastructure — no-op at this slice since this is the first migration and there is no prior schema to migrate, but the backup-before-migration hook and restore path are in place).
- **VIII. Test-First**: each owned acceptance scenario above is independently testable (Red-Green-Refactor); integration tests cover authorization, including that a request without the required ownership is denied (SC-013).
- **IX. Authentication & Authorization**: this slice establishes the foundation — authentication is via Google OAuth with open sign-up (FR-052), sessions are HttpOnly cookies issued and managed by the single-origin Next.js BFF with no tokens exposed to client JavaScript (FR-053), sign-out ends the session (FR-054), and unauthenticated requests to protected routes are denied and directed to sign-in (FR-055). Authorization is deny-by-default and enforced at the API/handler layer for every read and write (FR-068), with per-user isolation scoping every query to data the caller owns (FR-065). The signed-in user's Google profile is shown (FR-056). The membership + role tier (FR-066, FR-067) builds on this foundation and is delivered in slice 007 (project-sharing-membership).

## Assumptions

- **ASM-10 — Small team scale**: Small team (~10 users) on a single shared instance; not organizational multi-tenancy.
- **ASM-11 — Google identity provider**: Google is the sole identity provider for the MVP.

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

Additionally, the collaborative features that build on this authentication and per-user-isolation foundation are delivered in later slices within the MVP (not part of slice 001): project sharing, membership & roles (007), task assignment (008), comments & @mentions (009), real-time collaboration (016), and notifications (017). The membership + role authorization tier (FR-066, FR-067, FR-061) is owned by slice 007.

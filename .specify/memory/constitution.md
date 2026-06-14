<!--
  Sync Impact Report
  ==================
  Version change: 2.0.0 -> 3.0.0
  Bump rationale: MAJOR — the "Single-User Only" hard scope boundary is
    replaced backward-incompatibly by "Collaborative, Multi-User". The
    product becomes a connected, multi-user (~10) collaborative web app
    with Google OAuth sign-in, project sharing with roles, assignment,
    comments, real-time updates, and in-app notifications. A new
    Authentication & Authorization principle is added; Principles III and
    V are amended; the Caddy basic_auth access gate is removed.

  Modified principles:
    - I. Keyboard-First                    — unchanged
    - II. Accessibility (WCAG 2.1 AA)       — unchanged
    - III. Instant Response                 — amended (real-time reconciliation
      clause for SignalR server-initiated updates)
    - IV. Minimalist UI                     — unchanged
    - V. Connected, Server-Authoritative    — amended (OAuth IdP carve-out:
      authentication-only external runtime dependency)
    - VI. Type Safety End-to-End            — unchanged
    - VII. Data Integrity & Resilience      — amended (membership/role changes
      use a confirmation dialog, not the 30s data undo)
    - VIII. Test-First                      — unchanged

  Added principles:
    - IX. Authentication & Authorization    — NEW

  Replaced sections:
    - Constraints & Scope: "Single-User Only" -> "Collaborative, Multi-User"

  Modified sections:
    - Architecture & Stack — add Identity & Access context, SignalR real-time,
      in-app notifications, application-layer authorization; remove the Caddy
      basic_auth gate (Caddy keeps TLS + proxy); update Operational scope
    - Performance Standards — add ~10 concurrent users + real-time fan-out budget

  Removed: the Caddy basic_auth access-gate rule; all single-user / no-auth /
    "MUST NOT carry owner/org/role" language.

  Templates requiring updates:
    - .specify/templates/plan-template.md  — ⚠ pending: the dynamic "Constitution
      Check" gate re-derives at /plan time; new Principle IX (authz) and the
      multi-user scope will surface there automatically
    - .specify/templates/spec-template.md  — ✅ no change (no constitution refs)
    - .specify/templates/tasks-template.md — ✅ no change (no constitution refs)
    - docs/architecture/adr-0002-deployment.md — ✅ done: basic_auth removed,
      SignalR added (amended 2026-06-14)
    - docs/architecture/adr-0003-domain-model.md — ✅ done: Identity & Access
      context + new entities + authorization (rewritten 2026-06-14)
    - docs/architecture/adr-0004..0008 — ✅ added (identity, authorization,
      sharing, real-time, notifications)

  Follow-up TODOs: none
-->

# TaskFlow Constitution

## Core Principles

### I. Keyboard-First

Every interaction MUST be achievable entirely via keyboard.
Mouse and trackpad support is permitted as a convenience but MUST
never be required. Keyboard shortcuts MUST be:

- Discoverable (help overlay or command palette)
- Consistent across all views
- Composable (modifiers follow a predictable grammar)

Rationale: the core promise is that the user never reaches for the
mouse during daily task management.

### II. Accessibility (WCAG 2.1 AA)

Every interactive element MUST meet WCAG 2.1 AA compliance:

- Visible focus indicator on every focusable element.
- Correct ARIA roles and labels for screen readers (NVDA,
  VoiceOver, JAWS).
- Text contrast ratio MUST be at least 4.5:1 (3:1 for large
  text).
- Custom keyboard shortcuts MUST NOT collide with native
  assistive-technology bindings.
- No content accessible only via hover — all tooltips and
  popovers MUST have a keyboard/focus-triggered equivalent.
- Animations MUST respect `prefers-reduced-motion`; when reduced
  motion is active, transitions MUST be instant or under 100 ms.

Rationale: Keyboard-First without accessibility means "shortcuts
for power users." True keyboard-first means the app is usable by
everyone who cannot or chooses not to use a mouse — including
assistive-technology users.

### III. Instant Response

The interface MUST feel instant despite a network round-trip to the
backend. This is achieved through optimistic UI, not by blocking on the
server:

- Optimistic feedback: a user action MUST paint its optimistic result
  within one animation frame (under 16 ms) of the keypress, before the
  server confirms. The client reconciles or rolls back when the server
  responds.
- Server budget: server-confirmed mutations SHOULD complete within a p95
  of 200 ms (enforced as a hard budget in Performance Standards); failures
  MUST surface a clear, recoverable message (Principle VII).
- Real-time reconciliation: shared views receive server-initiated updates
  over SignalR. An inbound remote patch resolves under last-write-wins, but
  MUST yield to a pending local optimistic mutation until that mutation's
  server acknowledgement resolves, then reconcile. A remote update MUST NOT
  clobber an in-flight local edit.
- Skeleton screens ARE permitted for initial page loads and network-bound
  data fetches (see Principle IV). They MUST NOT be used to mask a
  mutation whose optimistic result could have been shown instead.
- Animations MUST be non-blocking — the UI MUST accept input while an
  animation or in-flight request is pending.

Rationale: perceived speed is the product's primary differentiator. A
network and other collaborators now sit in the path, so the instant
*feel* is delivered by optimistic rendering while the server remains the
source of truth.

### IV. Minimalist UI

The interface MUST be clean, focused, and distraction-free.

- Information density without clutter: show what matters, hide
  what doesn't, surface on demand.
- Purposeful animations only — every transition MUST serve
  orientation or confirmation, never decoration.
- Loading affordances MUST be purposeful: skeleton screens are permitted
  for genuine network-bound loads, but spinners and progress bars MUST
  NOT stand in for content that optimistic UI can render immediately.
- Aesthetic direction: muted palette, clear typography, spatial
  consistency. Inspired by Linear's visual language.
- No onboarding wizards, tooltips-on-first-run, or modal
  interruptions.

Rationale: the tool MUST feel like an extension of the user's
thought process, not a product demanding attention.

### V. Connected, Server-Authoritative

The application is a connected multi-user web app. PostgreSQL,
accessed through the C# backend, is the system of record.

- The Next.js client communicates with the C# API over REST; the API
  owns all writes and is the single source of truth.
- Network connectivity is required for normal operation. Offline-first
  operation and cross-device sync are out of scope for this iteration.
- User data MUST remain under the team's control in a documented,
  inspectable relational schema; export and import (Principle VII) keep
  the data portable.
- No third-party runtime **data** services: the app stores and serves all
  task data through its own API and database, with no external SaaS data
  dependency at runtime. A single external **authentication** provider
  (Google OAuth, see Principle IX) is the one permitted external runtime
  dependency, and only for sign-in — never for storing application data.

Rationale: a server-authoritative model gives a single, consistent source
of truth and a clean place to enforce domain invariants and authorization
(see Architecture & Stack), while keeping data ownership in-house.

### VI. Type Safety End-to-End

Type safety MUST hold across the whole stack, with the contract between
tiers machine-generated, never hand-synced.

- Frontend: TypeScript strict mode project-wide with no opt-outs. `any`
  is forbidden; every `@ts-ignore`/`@ts-expect-error` MUST carry a
  justification comment and a tracking issue.
- Backend: C# with nullable reference types enabled and analyzers treated
  as errors in CI; no suppressions without justification.
- Contract: an OpenAPI specification is the typed contract between client
  and API. The TypeScript client MUST be generated from it, never
  hand-written.
- Schema: EF Core code-first migrations are the source of truth for the
  database schema; data-layer types derive from the model/migrations, not
  the reverse.
- Runtime validation MUST be applied at every trust boundary: Zod (or
  equivalent) on the web for user input and API responses; FluentValidation
  or data annotations at the API boundary for incoming requests and
  deserialization.

Rationale: with a client, an API, and a database, the type system plus
runtime validators at each boundary are the defense against malformed
data crossing a tier.

### VII. Data Integrity & Resilience

User data is sacred. The application MUST protect it proactively:

- **Migrations**: forward-only, versioned EF Core migrations. Every
  migration MUST be tested against a representative data snapshot before
  release.
- **Backup**: an automatic backup (pg_dump or a managed database snapshot)
  MUST be taken before each migration runs, and MUST be restorable.
- **Export/Import**: full data export and import MUST be available at all
  times, in a documented format.
- **Error handling**: errors MUST be logged structurally (level, context,
  stacktrace) and presented to the user with an actionable recovery
  suggestion. No silent failures.
- **Undo for destructive actions**: delete, bulk update, and any
  irreversible operation on task/project **data** MUST be undoable for a
  minimum of 30 seconds after execution. Membership and role changes
  (sharing, role assignment, removing a member) are NOT covered by this
  undo; they MUST instead require an explicit confirmation dialog before
  taking effect.

Rationale: the backend is the sole custodian of the team's data; a lost
or corrupted write has no external recovery path beyond these guarantees.

### VIII. Test-First

Tests MUST be written before implementation (Red-Green-Refactor).

- Every user story MUST have acceptance scenarios that are
  independently testable.
- Backend: xUnit unit tests cover domain logic and aggregate invariants;
  integration tests cover command/query handlers through the real
  database, **including authorization** (a request without the required
  ownership/role MUST be denied).
- Frontend: Vitest covers unit/component logic; Playwright covers user
  journeys end-to-end through the real stack.
- A failing test suite MUST block merging.

Rationale: correctness across a client/API/database stack with multiple
users depends on tests at each tier, on authorization tests, and on
end-to-end journeys that exercise the seams.

### IX. Authentication & Authorization

Every request is authenticated and every data operation is authorized.

- **Authentication**: sign-in is via Google OAuth (open sign-up). Sessions
  are HttpOnly cookies issued and managed by the single-origin Next.js BFF;
  no access tokens are exposed to client JavaScript.
- **Authorization is mandatory and deny-by-default**: every read and write
  MUST be authorized at the API/handler layer. Two tiers apply:
  - *Per-user isolation* — a caller may only read or write data they own
    (their tasks, their personal projects). All queries are scoped to the
    caller's identity.
  - *Membership + role* — for shared projects, access requires membership,
    and the action requires sufficient role: **viewer** (read-only, no
    commenting), **editor** (change tasks, comment), **owner** (manage
    members, share/unshare, delete). Least privilege is enforced per
    operation.
- Authorization MUST live in the application layer (a policy backed by
  project membership), not be scattered ad hoc; aggregates remain focused
  on domain invariants.

Rationale: in a collaborative app, the boundary between users is a
correctness and trust requirement. A missed check is a data leak, so
authorization is a first-class, tested, deny-by-default concern.

## Constraints & Scope

### Collaborative, Multi-User

TaskFlow serves a small team — on the order of ten people — on a single
shared instance. This shapes what features exist and how data is modeled.

- Users have accounts (Google OAuth) and personal, private data by default.
- Data carries ownership and membership: tasks have a `createdBy` and may
  have `assignees`; projects have an `ownerId` and a `visibility`
  (personal or shared); shared projects have a `ProjectMembership` set with
  roles. These fields are REQUIRED — they are the basis of authorization.
- Collaboration is in scope: sharing projects, roles/permissions,
  assignment, comments/@mentions, real-time updates, and in-app
  notifications.
- Out of bounds: organizations / multi-tenancy beyond the single team,
  anonymous or guest access, and public share links. The data model MUST
  NOT add an organization/tenant dimension.

The single-team scope keeps the authorization model tractable (ownership +
per-project membership) without the weight of full multi-tenancy.

## Architecture & Stack

This section codifies ADR-0001 (`docs/architecture/adr-0001-stack.md`) and
the Identity/Access, Authorization, Sharing, Real-time, and Notifications
ADRs (0004–0008). It is normative: deviations require an amendment.

- **Repository**: monorepo. `apps/web` (Next.js + TypeScript) and
  `apps/api` (C# / ASP.NET Core) live alongside `specs/` and `.specify/`.
- **Frontend**: Next.js with TypeScript (strict). Hybrid rendering —
  React Server Components render the initial shell with server-side
  skeletons; interactive areas are client islands using TanStack Query
  with optimistic mutations.
- **API contract**: REST with an OpenAPI specification as the source of
  truth; a typed TypeScript client is generated from it.
- **Backend**: C# / ASP.NET Core using full tactical Domain-Driven Design.
  Two bounded contexts: **Task Management** (aggregate roots Task, Project,
  Cycle; value objects; domain events) and **Identity & Access** (the User
  aggregate and the application-layer authorization policy, which consumes
  each shared project's ProjectMembership set — modeled in the Project
  aggregate per ADR-0003). Domain
  invariants live in the aggregates (e.g. single active cycle, one-level
  project nesting, recurrence/status rules).
- **Authorization**: an application-layer policy backed by ProjectMembership
  enforces per-user isolation and role checks on every command/query
  (Principle IX), deny-by-default.
- **Messaging & dispatch**: Wolverine handles command/query dispatch and
  the transactional outbox, with RabbitMQ as the message transport. Domain
  events (e.g. `TaskAssigned`, `UserMentioned`, `TaskCompleted`,
  `CycleClosed`) drive cross-aggregate effects and notification generation.
- **Real-time**: SignalR pushes live updates to members viewing shared
  projects; reconciliation follows Principle III (last-write-wins, yielding
  to in-flight local edits).
- **Notifications**: an in-app notification center plus live SignalR toasts,
  generated by domain-event handlers (assigned / mentioned / changed).
- **Persistence**: EF Core (code-first migrations) over PostgreSQL for the
  write side; CQRS read projections (query handlers returning DTOs)
  optimized for the view requirements.
- **Validation**: Zod at frontend trust boundaries; FluentValidation /
  data annotations at the API boundary.
- **Deployment**: the app is containerized (multi-stage images for web
  and api) and orchestrated with Docker Compose on a single Hetzner VPS.
  PostgreSQL and RabbitMQ run as containers with persistent volumes and
  MUST NOT be exposed publicly — they are bound to the internal Docker
  network / localhost. The published web port uses the 43xx range (the
  3xxx range is reserved by another application on the host).
- **Edge & access**: a host-installed Caddy terminates TLS and reverse-
  proxies a single origin to the web container; the API is reached via the
  web origin's `/api` path over the internal network (no separate public
  API surface, no CORS). Public access is gated by **application-layer
  Google OAuth + sessions** (Principle IX) — there is no shared-password
  edge gate.
- **CI/CD**: GitHub Actions builds and tests both stacks, publishes images
  to GHCR, and deploys to the VPS (production-only). Each deploy MUST take
  a `pg_dump` backup (Principle VII) and apply EF Core migrations before
  starting services. Backups are stored on a local volume.
- **Operational scope**: real-time (SignalR) and in-app notifications are
  in scope. Email/push notifications, reminders, presence indicators,
  activity/audit feeds, non-Google SSO, multi-node orchestration, offsite
  backups, and a staging environment are out of scope for this iteration
  (YAGNI). See `docs/architecture/adr-0002-deployment.md` and ADR-0004..0008.

## Performance Standards

- **Web cold start**: first contentful paint under 1 s and time-to-
  interactive under 2.5 s on a broadband connection from a warm backend.
- **Perceived input latency**: optimistic UI paints the result of a local
  action under 16 ms (see Principle III).
- **Server mutations**: p95 under 200 ms for single-entity writes against
  a representative dataset.
- **Concurrency**: the system MUST serve ~10 concurrent users without
  perceptible degradation.
- **Real-time fan-out**: a change to a shared item MUST propagate to other
  members' open shared views within ~1 s.
- **Rendering**: list views MUST maintain 60 fps while scrolling through
  10 000 items (client-side virtualization required).
- **Search**: command-palette search MUST return results in under 50 ms
  over a 10 000-item working set (client-side index after load, or a
  server search endpoint meeting the same budget).
- **Client memory**: the browser tab MUST stay below 300 MB with 10 000
  tasks loaded.
- Performance regression tests MUST be part of the CI pipeline once a
  benchmark baseline is established.

## Development Workflow

- **Branching**: one feature branch per spec, named
  `###-feature-name` matching the spec directory.
- **Commits**: atomic, descriptive; one logical change per commit.
- **Code review**: every merge MUST pass constitution compliance
  check (see Governance), including an authorization review for any new
  data operation.
- **CI gates**: for both stacks — lint + type-check (TS strict; C#
  nullable/analyzers-as-errors), OpenAPI client generation in sync, the
  full test suite (xUnit + integration incl. authorization, Vitest +
  Playwright), and (when available) performance benchmarks MUST all pass
  before merge.
- **Release cadence**: ship when ready; no fixed schedule. Each
  release MUST include a changelog entry.
- **YAGNI discipline**: every feature MUST justify its existence
  against the core promise — fast, quiet, keyboard-driven collaborative
  task management. When in doubt, leave it out. Three lines of clear
  code are better than a premature abstraction. Configuration
  surface MUST be minimal — sensible defaults over settings
  screens. No plugin system, scripting API, or extensibility
  hooks unless proven necessary by real usage. Scope creep is
  the primary threat to the product's speed and simplicity —
  saying no is a feature.

## Governance

This constitution is the highest-authority document for TaskFlow
development. All design decisions, code reviews, and feature
proposals MUST be evaluated against this document.

- **Normative scope**: compliance review covers every normative
  statement (MUST, MUST NOT) in this document, regardless of
  which section it appears in — Core Principles, Constraints &
  Scope, Architecture & Stack, Performance Standards, and
  Development Workflow all carry equal binding force.
- **Amendments** require: (1) a written proposal describing the
  change and its rationale, (2) an impact assessment on existing
  code and templates, (3) a version bump following semantic
  versioning (see below).
- **Versioning policy**:
  - MAJOR: principle removed, redefined, or made backward-
    incompatible.
  - MINOR: new principle or section added, or existing guidance
    materially expanded.
  - PATCH: wording clarifications, typo fixes, non-semantic
    refinements.
- **Compliance review**: every PR / code review MUST verify that
  changes do not violate any normative statement. Violations
  MUST be justified in a Complexity Tracking table (see plan
  template) or rejected.
- **Guidance file**: refer to the current plan and spec for
  runtime development guidance.

**Version**: 3.0.0 | **Ratified**: 2026-06-13 | **Last Amended**: 2026-06-14

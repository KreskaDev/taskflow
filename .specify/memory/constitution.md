<!--
  Sync Impact Report
  ==================
  Version change: 3.0.0 -> 4.0.0
  Bump rationale: MAJOR — admission control reverses the v3.0.0 "open
    sign-up" rule (account creation is now gated to an allowlist / Google
    Workspace hosted-domain). Bundled with this MAJOR change: three new
    principles (X Time & Timezone, XI Privacy & Personal Data, XII Security
    by Default) and material strengthening of II, III, VII, IX and the
    Performance Standards, resolving the 2026-06-15 design review's
    cross-cutting blockers.

  Modified principles:
    - I. Keyboard-First                    — unchanged
    - II. Accessibility (WCAG 2.1 AA)       — strengthened (ARIA-live for
      server-initiated updates/toasts; dialog focus contract)
    - III. Instant Response                 — strengthened (measurable
      budgets; server-mutation budget is a MUST)
    - IV. Minimalist UI                     — unchanged
    - V. Connected, Server-Authoritative    — unchanged
    - VI. Type Safety End-to-End            — unchanged (API/error contract
      detail referenced via ADR-0009)
    - VII. Data Integrity & Resilience      — strengthened (restore-tested +
      offsite backups; deploy rollback; undo-under-LWW; blast radius)
    - VIII. Test-First                      — unchanged
    - IX. Authentication & Authorization    — strengthened + backward-
      incompatible (dispatch authz; authorship grant; live-subscription
      authz; session policy; ADMISSION GATE replaces open sign-up)

  Added principles:
    - X. Time & Timezone                    — NEW
    - XI. Privacy & Personal Data           — NEW
    - XII. Security by Default              — NEW

  Modified sections:
    - Architecture & Stack — message transport relaxed (Wolverine local
      queues default; RabbitMQ when justified); SignalR hub proxied by
      Caddy directly to api; ADR-0009 (API & error contract) referenced
    - Performance Standards — measurable concurrency/fan-out/search budgets;
      10k figures anchored to per-user authz-scoped working set
    - Governance — authorization changes need a non-author reviewer; authz
      allow+deny test gate in compliance review

  Templates requiring updates:
    - .specify/templates/plan-template.md  — ⚠ pending: dynamic Constitution
      Check re-derives at /plan time (new X/XI/XII + admission will surface)
    - .specify/templates/spec-template.md  — ✅ no change (no constitution refs)
    - .specify/templates/tasks-template.md — ✅ no change (no constitution refs)

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
- **Status messages**: server-initiated content — incoming real-time
  patches to shared views and live toast notifications — MUST be conveyed
  to assistive technology via an appropriate live region
  (`aria-live`/`role=status`/`role=log`) WITHOUT stealing focus; polite by
  default (assertive only for genuinely urgent direct-to-user cases),
  coalesced/rate-limited so output stays usable under concurrent fan-out.
- **Dialog focus contract**: confirmation and command-palette dialogs MUST
  set initial focus into the dialog, trap focus, dismiss on Esc, and return
  focus to the invoker on close.

Rationale: Keyboard-First without accessibility means "shortcuts
for power users." True keyboard-first means the app is usable by
everyone who cannot or chooses not to use a mouse — including
assistive-technology users. Real-time collaboration adds server-pushed
DOM changes, which AT only perceives via status regions.

### III. Instant Response

The interface MUST feel instant despite a network round-trip to the
backend. This is achieved through optimistic UI, not by blocking on the
server:

- Optimistic feedback: a user action MUST paint its optimistic result
  within one animation frame (under 16 ms) of the keypress, before the
  server confirms. The client reconciles or rolls back when the server
  responds.
- Server budget: server-confirmed mutations MUST complete within a p95 of
  200 ms for single-entity writes against a representative dataset; failures
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
  hand-written. The error contract is a documented, machine-readable schema
  (see ADR-0009) modeled by the generated client.
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

- **Migrations**: forward-only, versioned EF Core migrations following
  expand/contract discipline so a deploy is reversible without data loss.
  Every migration MUST be tested against a representative data snapshot
  before release.
- **Backup**: an automatic backup (pg_dump or a managed database snapshot)
  MUST be taken before each migration; in addition, a scheduled backup
  (independent of deploys, with a defined retention window) MUST be taken
  and at least one copy kept **offsite** so host loss is survivable — or
  the host-loss reading of "restorable" MUST be explicitly waived in
  accepted-risk language. "Restorable" is not assertable until restore is
  **verified**: a CI/deploy step MUST restore a backup into a throwaway
  database and assert integrity.
- **Deploy recovery**: the pipeline MUST define a rollback action on
  failure — redeploy the prior pinned (immutable git-SHA) image; restore the
  pre-migration dump when the schema changed.
- **Export/Import**: full data export and import MUST be available at all
  times, in a documented format, **scoped to data the caller owns/can access**.
- **Error handling**: errors MUST be logged structurally (level, context,
  stacktrace; never secrets) and presented to the user with an actionable
  recovery suggestion. No silent failures.
- **Undo for destructive actions**: delete, bulk update, and any
  irreversible operation on task/project **data** MUST be undoable for a
  minimum of 30 seconds after execution. An undo restore is a normal write
  subject to last-write-wins (Principle III): it fans out over SignalR,
  surfaces when it overwrote a concurrent edit (FR-049), restores to
  Inbox/backlog with a recovery message if the parent was deleted, and may
  be performed only by the original actor (scoped by their current role).
  "Fully restores previous state" holds only in the no-concurrent-edit case.
  Membership and role changes (sharing, role assignment, removing a member,
  unshare) are NOT covered by this undo; they require an explicit
  confirmation dialog that MUST show its **blast radius** (members losing
  access, assignments cleared).

Rationale: the backend is the sole custodian of the team's data; a lost
or corrupted write has no external recovery path beyond these guarantees.

### VIII. Test-First

Tests MUST be written before implementation (Red-Green-Refactor).

- Every user story MUST have acceptance scenarios that are
  independently testable.
- Backend: xUnit unit tests cover domain logic and aggregate invariants;
  integration tests cover command/query handlers through the real
  database, **including authorization** — every data handler MUST have both
  an allow test and a deny test (a request without the required
  ownership/membership/role MUST be denied).
- Frontend: Vitest covers unit/component logic; Playwright covers user
  journeys end-to-end through the real stack.
- A failing test suite MUST block merging.

Rationale: correctness across a client/API/database stack with multiple
users depends on tests at each tier, on authorization tests, and on
end-to-end journeys that exercise the seams.

### IX. Authentication & Authorization

Every request is authenticated and every data operation is authorized.

- **Authentication**: sign-in is via Google OAuth. Sessions are HttpOnly
  cookies issued and managed by the single-origin Next.js BFF; no access
  tokens are exposed to client JavaScript. The OAuth flow MUST use `state`,
  `nonce`, and PKCE and MUST validate the id_token. Sessions MUST have a
  server-enforced absolute lifetime and an idle timeout (both documented),
  a new session id MUST be issued at OAuth completion (fixation defense),
  and sign-out MUST invalidate the session server-side. The session cookie
  MUST set an explicit `SameSite` value, and every state-changing request
  through the BFF MUST be CSRF-protected (origin check or anti-CSRF token).
  The BFF→API identity carrier MUST be integrity-protected (a signed,
  short-lived token over the internal network; the API port is not
  externally reachable).
- **Admission control**: account creation MUST be gated to an explicit
  allowlist or a Google Workspace hosted-domain (`hd`). Sign-in is NOT open
  to any Google account; non-admitted sign-ins MUST be rejected.
- **Authorization is mandatory, deny-by-default, and dispatched by
  resource visibility** (not a conjunction of tiers):
  - *Personal / unprojected data (Inbox)* authorizes on **ownership**
    (`createdBy` / `ownerId`); queries are scoped to the caller.
  - *Shared-project entities* authorize on **current `ProjectMembership` +
    role**: viewer (read-only, no commenting), editor (change tasks,
    comment), owner (manage members, share/unshare, delete). Owner is the
    immutable `ownerId`, moved only by an explicit transfer command, not a
    freely assignable role. `createdBy` and assignee are **provenance only**
    and confer NO standalone access; on leave/remove/unshare a user loses
    ALL access to that project's data regardless of authorship or assignment.
  - *Authorship* is a distinct object-level grant: a comment's author — and
    only the author — may edit or delete it; project role (including owner)
    does not override this, but loss of membership does.
- **Live subscriptions are authorized too**: a membership or role change
  MUST immediately revoke or re-authorize the affected user's active
  real-time (SignalR) subscriptions — a removed member receives no further
  live patches and is forced to a no-access re-sync.
- Authorization MUST live in the application layer (a policy backed by
  project membership), not be scattered ad hoc; aggregates remain focused
  on domain invariants.

Rationale: in a collaborative app, the boundary between users is a
correctness and trust requirement. A missed check is a data leak, so
authorization is a first-class, tested, deny-by-default concern that
governs requests AND live subscriptions.

### X. Time & Timezone

Time is computed against one documented reference, everywhere.

- All timestamps MUST be stored in UTC (`timestamptz`).
- Every date-relative computation — "Today" and "Upcoming" membership,
  cycle boundaries, recurrence rollover ("on or after the due date"), and
  natural-language date resolution — MUST be evaluated against a single
  **instance reference timezone, `Europe/Warsaw`**, applied identically on
  client and server, so a date boundary is the same fact everywhere. A task
  due tomorrow MUST appear in Upcoming, not Today.
- A due date MUST distinguish date-only from date-time (a `has_time` flag).
- DST transitions MUST be handled by the timezone library, never by
  fixed-offset arithmetic. Per-user timezones are out of scope.

Rationale: "Today"/"next 7 days"/recurrence are unbuildable without a
single canonical time reference; a fixed zone is correct given the
Polish-only parser and single-team scope.

### XI. Privacy & Personal Data

TaskFlow holds personal data obtained via Google sign-in and MUST treat
it as a first-class concern.

- An **account-deletion / erasure path** MUST exist, with a defined
  cascade: owned shared projects are transferred or deleted; authored
  comments are anonymized to a tombstone identity (not hard-deleted where
  they anchor a thread); `createdBy`/assignee references are nulled or
  reassigned to the tombstone; the user's notifications are purged.
- The same residual-attribution rule (anonymize vs retain) MUST apply when
  a member leaves, is removed, or a project is unshared.
- A **data-retention stance** MUST be stated explicitly for backups,
  soft-deleted (undo-window) data, comments, and notifications. "Retained
  until account deletion" is an acceptable decision; silence is not.

Rationale: holding Google PII without a deletion/retention stance is the
single unacceptable gap; export (Principle VII) needs its erasure
counterpart.

### XII. Security by Default

- User-authored content (task markdown descriptions, comment bodies,
  @mention tokens) is untrusted and MUST be output-encoded or sanitized to
  a constrained, safe subset on render; raw HTML injection MUST be
  impossible.
- A Content-Security-Policy and standard security response headers MUST be
  present in production.
- Secrets — the session signing key, Google OAuth client secret, database
  and broker credentials, deploy SSH keys — MUST be injected at runtime via
  environment or a secret store, MUST NOT be committed to the repository or
  baked into container images, and MUST NOT appear in logs or error
  context.

Rationale: comments are the first free-form user content (a stored-XSS
surface), and an OAuth + SSH-deploy stack has real secrets — both are
cross-cutting and were previously only implicit.

## Constraints & Scope

### Collaborative, Multi-User

TaskFlow serves a small team — on the order of ten people — on a single
shared instance. This shapes what features exist and how data is modeled.

- Users have accounts (Google OAuth, admission-gated per Principle IX) and
  personal, private data by default.
- Data carries ownership and membership: tasks have a `createdBy` and may
  have `assignees`; projects have an `ownerId` and a `visibility`
  (personal or shared); shared projects have a `ProjectMembership` set with
  roles; cycles are team-wide; labels carry an `ownerId`. These fields are
  REQUIRED — they are the basis of authorization.
- Collaboration is in scope: sharing projects, roles/permissions,
  assignment, comments/@mentions, real-time updates, and in-app
  notifications.
- Out of bounds: organizations / multi-tenancy beyond the single team,
  anonymous or guest access, public share links, and pending/pre-account
  invitations. The data model MUST NOT add an organization/tenant dimension.

The single-team scope keeps the authorization model tractable (ownership +
per-project membership) without the weight of full multi-tenancy.

## Architecture & Stack

This section codifies ADR-0001 (`docs/architecture/adr-0001-stack.md`) and
the Identity/Access, Authorization, Sharing, Real-time, Notifications, and
API/error-contract ADRs (0004–0009). It is normative.

- **Repository**: monorepo. `apps/web` (Next.js + TypeScript) and
  `apps/api` (C# / ASP.NET Core) live alongside `specs/` and `.specify/`.
- **Frontend**: Next.js with TypeScript (strict). Hybrid rendering —
  React Server Components render the initial shell with server-side
  skeletons; interactive areas are client islands using TanStack Query
  with optimistic mutations.
- **API contract**: REST with an OpenAPI specification as the source of
  truth; a typed TypeScript client is generated from it. The error contract
  is ProblemDetails-based (ADR-0009).
- **Backend**: C# / ASP.NET Core using full tactical Domain-Driven Design.
  Two bounded contexts: **Task Management** (aggregate roots Task, Project,
  Cycle; value objects; domain events) and **Identity & Access** (User and
  the application-layer authorization policy, which consumes each shared
  project's ProjectMembership set — modeled in the Project aggregate per
  ADR-0003). Domain invariants live in the aggregates.
- **Authorization**: an application-layer policy enforces dispatch-by-
  visibility, deny-by-default, on every command/query and on live
  subscriptions (Principle IX).
- **Messaging & dispatch**: Wolverine handles command/query dispatch and
  the transactional outbox over **a message transport — Wolverine durable
  Postgres-backed local queues by default; RabbitMQ only when a real
  cross-process consumer exists**. Domain events drive cross-aggregate
  effects and notification generation.
- **Real-time**: SignalR pushes live updates to members viewing shared
  projects; reconciliation follows Principle III. The hub is reverse-proxied
  by **Caddy directly to the api container** (Next.js cannot proxy WebSocket
  upgrades); WS is the SLA-bearing transport.
- **Notifications**: an in-app notification center plus live SignalR toasts,
  generated by domain-event handlers; sources are re-authorized on access.
- **Persistence**: EF Core (code-first migrations) over PostgreSQL for the
  write side; CQRS read projections optimized for the view requirements.
- **Validation**: Zod at frontend trust boundaries; FluentValidation /
  data annotations at the API boundary.
- **Deployment**: containerized (multi-stage images, immutable git-SHA
  tags) on a single Hetzner VPS via Docker Compose with healthchecks;
  PostgreSQL and RabbitMQ (if used) are internal-only. Host-installed Caddy
  terminates TLS and reverse-proxies the single web origin plus the SignalR
  hub path. Public access is gated by application-layer Google OAuth +
  admission control (Principle IX) — no shared-password edge gate. GitHub
  Actions builds/tests, publishes to GHCR, and deploys with backup-before-
  migrate, restore-test, and a rollback path (Principle VII).
- **Operational scope**: real-time (SignalR) and in-app notifications are
  in scope. Email/push notifications, reminders, presence indicators,
  activity/audit feeds, non-Google SSO, per-user timezones, pending
  pre-account invitations, multi-node orchestration, and a staging
  environment are out of scope for this iteration (YAGNI).

## Performance Standards

- **Web cold start**: first contentful paint under 1 s and time-to-
  interactive under 2.5 s on a broadband connection from a warm backend.
- **Perceived input latency**: optimistic UI paints a local action under
  16 ms (Principle III).
- **Server mutations**: p95 under 200 ms for single-entity writes against a
  representative dataset (MUST; consistent with Principle III).
- **Concurrency**: under ~10 concurrent sessions, p95 interaction latency
  MUST stay within the 200 ms write / 16 ms paint budgets.
- **Real-time fan-out**: p95 under 1000 ms measured commit-to-paint on the
  receiving client.
- **Working-set anchor**: all "10,000 tasks" figures (rendering, search,
  client memory) refer to the **per-user authorization-scoped accessible
  working set** across ~10 users; perf-regression seed data MUST reflect
  that shape (overlapping shared-project membership), not a flat list.
- **Rendering**: list views MUST maintain 60 fps while scrolling through
  10 000 items (client-side virtualization required).
- **Search**: command-palette search MUST return results in under 50 ms
  over the access-scoped set, with authorization filtering INSIDE the
  budget; the caller's accessible-project set is resolved cheaply (cached
  per session). A separate budget covers the initial scoped load.
- **Client memory**: the browser tab MUST stay below 300 MB with 10 000
  accessible tasks loaded.
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
  full test suite (xUnit + integration incl. authorization allow+deny,
  Vitest + Playwright), backup restore-test, and (when available)
  performance benchmarks MUST all pass before merge.
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
  which section it appears in.
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
  changes do not violate any normative statement. Changes that alter
  authorization, membership, or roles MUST be reviewed by someone other
  than the author, and every new or changed data handler MUST ship with
  both an allow and a deny authorization test. Violations MUST be justified
  in a Complexity Tracking table (see plan template) or rejected.
- **Guidance file**: refer to the current plan and spec for
  runtime development guidance.

**Version**: 4.0.0 | **Ratified**: 2026-06-13 | **Last Amended**: 2026-06-15

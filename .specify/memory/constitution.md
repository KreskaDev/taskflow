<!--
  Sync Impact Report
  ==================
  Version change: 1.1.0 -> 2.0.0
  Bump rationale: MAJOR — Principle V redefined backward-incompatibly
    (Offline-Only, Local-First -> Connected, Server-Authoritative). The
    product's foundational delivery model changes from an offline local
    desktop app to a connected single-user web app with a C# backend and
    PostgreSQL. Principles III, IV, and VI are materially redefined to
    match; a new Architecture & Stack section is added. Single-User scope,
    Keyboard-First, Accessibility, Data Integrity, and Test-First are
    retained.

  Modified principles:
    - I. Keyboard-First                    — unchanged
    - II. Accessibility (WCAG 2.1 AA)       — unchanged
    - III. Instant Response                 — redefined (optimistic UI +
      networked latency budget; skeletons now permitted)
    - IV. Minimalist UI                     — amended (skeleton-screen
      prohibition removed)
    - VI. Type Safety End-to-End            — redefined (TS web + C# api,
      OpenAPI contract, EF Core migrations as schema source of truth)
    - VII. Data Integrity & Resilience      — adapted (EF Core migrations,
      pg_dump/snapshot backups; undo + export/import retained)

  Replaced principles:
    - V. Offline-Only, Local-First -> V. Connected, Server-Authoritative

  Added sections:
    - Architecture & Stack (codifies ADR-0001 and ADR-0002 —
      including Deployment & Operations: Docker/Compose on a single
      Hetzner VPS, host Caddy + basic_auth single-origin, GHCR + GitHub
      Actions CD, backup-before-migrate, internal-only data services)

  Rewritten sections:
    - Performance Standards (reframed for web client + networked backend)

  Removed sections: N/A

  Templates requiring updates:
    - .specify/templates/plan-template.md  — ⚠ pending: re-evaluate the
      "Constitution Check" gate against the new stack at first /plan
    - .specify/templates/spec-template.md  — ✅ no change (no constitution refs)
    - .specify/templates/tasks-template.md — ✅ no change (no constitution refs)
    - .specify/templates/commands/*.md      — ✅ no files present
    - docs/architecture/adr-0001-stack.md  — ✅ source of these amendments

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
  of 200 ms; failures MUST surface a clear, recoverable message
  (Principle VII).
- Skeleton screens ARE permitted for initial page loads and network-bound
  data fetches (see Principle IV). They MUST NOT be used to mask a
  mutation whose optimistic result could have been shown instead.
- Animations MUST be non-blocking — the UI MUST accept input while an
  animation or in-flight request is pending.

Rationale: perceived speed is the product's primary differentiator. A
network now sits in the path, so the instant *feel* is delivered by
optimistic rendering while the server remains the source of truth.

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

The application is a connected single-user web app. PostgreSQL,
accessed through the C# backend, is the system of record.

- The Next.js client communicates with the C# API over REST; the API
  owns all writes and is the single source of truth.
- Network connectivity is required for normal operation. Offline-first
  operation and cross-device sync are out of scope for this iteration.
- User data MUST remain under the user's control in a documented,
  inspectable relational schema; export and import (Principle VII) keep
  the data portable.
- No third-party runtime data services: the app depends only on its own
  API and database, with no external SaaS dependency at runtime.

Rationale: a server-authoritative model gives a single, consistent source
of truth and a clean place to enforce domain invariants (see Architecture
& Stack), while the single-user scope keeps the surface small.

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
  irreversible operation MUST be undoable for a minimum of 30 seconds
  after execution.

Rationale: the backend is the sole custodian of the user's data; a lost
or corrupted write has no external recovery path beyond these guarantees.

### VIII. Test-First

Tests MUST be written before implementation (Red-Green-Refactor).

- Every user story MUST have acceptance scenarios that are
  independently testable.
- Backend: xUnit unit tests cover domain logic and aggregate invariants;
  integration tests cover command/query handlers through the real
  database.
- Frontend: Vitest covers unit/component logic; Playwright covers user
  journeys end-to-end through the real stack.
- A failing test suite MUST block merging.

Rationale: correctness across a client/API/database stack depends on
tests at each tier and on end-to-end journeys that exercise the seams.

## Constraints & Scope

### Single-User Only

TaskFlow is designed for exactly one person. This is a hard scope
boundary, not a principle to be weighed — it constrains what
features may exist.

- No collaboration features, no permissions model, no sharing,
  no multi-tenancy, and no authentication.
- Features that only make sense in a team context MUST NOT be
  added.
- Data model MUST NOT carry fields (owner, org, role) that exist
  solely to support multi-user scenarios.

The single-user model keeps the codebase small, the data schema
focused, and the UX free of access-control noise.

## Architecture & Stack

This section codifies ADR-0001 (`docs/architecture/adr-0001-stack.md`).
It is normative: deviations require an amendment.

- **Repository**: monorepo. `apps/web` (Next.js + TypeScript) and
  `apps/api` (C# / ASP.NET Core) live alongside `specs/` and `.specify/`.
- **Frontend**: Next.js with TypeScript (strict). Hybrid rendering —
  React Server Components render the initial shell with server-side
  skeletons; interactive areas are client islands using TanStack Query
  with optimistic mutations.
- **API contract**: REST with an OpenAPI specification as the source of
  truth; a typed TypeScript client is generated from it.
- **Backend**: C# / ASP.NET Core using full tactical Domain-Driven Design
  — aggregate roots (Task, Project, Cycle), value objects, domain events,
  repositories, and a CQRS command/query split. Domain invariants live in
  the aggregates (e.g. single active cycle, one-level project nesting,
  recurrence/status rules).
- **Messaging & dispatch**: Wolverine handles command/query dispatch and
  the transactional outbox, with RabbitMQ as the message transport.
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
  API surface, no CORS). Because the app has no in-domain authentication,
  Caddy `basic_auth` MUST gate all public access.
- **CI/CD**: GitHub Actions builds and tests both stacks, publishes images
  to GHCR, and deploys to the VPS (production-only). Each deploy MUST take
  a `pg_dump` backup (Principle VII) and apply EF Core migrations before
  starting services. Backups are stored on a local volume.
- **Operational scope**: realtime push/websockets, multi-node
  orchestration, offsite backups, and a staging environment are out of
  scope for this iteration (YAGNI). See `docs/architecture/adr-0002-deployment.md`.

## Performance Standards

- **Web cold start**: first contentful paint under 1 s and time-to-
  interactive under 2.5 s on a broadband connection from a warm backend.
- **Perceived input latency**: optimistic UI paints the result of a local
  action under 16 ms (see Principle III).
- **Server mutations**: p95 under 200 ms for single-entity writes against
  a representative dataset.
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
  check (see Governance).
- **CI gates**: for both stacks — lint + type-check (TS strict; C#
  nullable/analyzers-as-errors), OpenAPI client generation in sync, the
  full test suite (xUnit + integration, Vitest + Playwright), and (when
  available) performance benchmarks MUST all pass before merge.
- **Release cadence**: ship when ready; no fixed schedule. Each
  release MUST include a changelog entry.
- **YAGNI discipline**: every feature MUST justify its existence
  against the core promise — fast, quiet, keyboard-driven task
  management. When in doubt, leave it out. Three lines of clear
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

**Version**: 2.0.0 | **Ratified**: 2026-06-13 | **Last Amended**: 2026-06-14

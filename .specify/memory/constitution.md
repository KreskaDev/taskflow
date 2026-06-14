<!--
  Sync Impact Report
  ==================
  Version change: 1.0.0 -> 1.1.0
  Bump rationale: MINOR — 3 principles added, 2 demoted to
    non-principle sections. No existing principle redefined or removed
    in a backward-incompatible way.

  Modified principles:
    - I. Keyboard-First           — unchanged (position unchanged)
    - III. Instant Response       — unchanged (renumbered: was II)
    - IV. Minimalist UI           — unchanged (renumbered: was V)
    - V. Offline-Only Local-First — unchanged (renumbered: was III)
    - VIII. Test-First            — unchanged (renumbered: was VI)

  Added principles:
    - II. Accessibility (WCAG 2.1 AA)    — NEW
    - VI. Type Safety End-to-End         — NEW
    - VII. Data Integrity & Resilience   — NEW

  Demoted (moved, not deleted):
    - Single-User Simplicity  — was Principle IV, now in Constraints
      & Scope (content and rationale preserved)
    - Simplicity Over Features (YAGNI) — was Principle VII, now in
      Development Workflow as YAGNI discipline (rationale preserved)

  Added sections:
    - Constraints & Scope (absorbs former Single-User Simplicity)

  Removed sections: N/A

  Templates requiring updates:
    - .specify/templates/plan-template.md  — OK (dynamic constitution ref)
    - .specify/templates/spec-template.md  — OK (no constitution refs)
    - .specify/templates/tasks-template.md — OK (no constitution refs)
    - .specify/templates/commands/*.md     — OK (no files present)

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

Every user action MUST produce visible feedback within one animation
frame (16 ms on a 60 Hz display). Specifically:

- Keypress-to-paint latency MUST stay below 16 ms for local
  operations (create, edit, complete, navigate).
- No loading spinners, skeleton screens, or progress bars for
  local data operations.
- Animations MUST be non-blocking — the UI MUST accept input
  while an animation is in flight.

Rationale: perceived speed is the product's primary differentiator.
Any perceptible lag breaks trust.

### IV. Minimalist UI

The interface MUST be clean, focused, and distraction-free.

- Information density without clutter: show what matters, hide
  what doesn't, surface on demand.
- Purposeful animations only — every transition MUST serve
  orientation or confirmation, never decoration.
- Aesthetic direction: muted palette, clear typography, spatial
  consistency. Inspired by Linear's visual language.
- No onboarding wizards, tooltips-on-first-run, or modal
  interruptions.

Rationale: the tool MUST feel like an extension of the user's
thought process, not a product demanding attention.

### V. Offline-Only, Local-First

The application MUST work fully offline with local storage.

- No account creation, no authentication, no cloud dependency.
- No network calls — not even optional telemetry or update checks
  unless the user explicitly opts in.
- User data MUST remain on the user's machine in a documented,
  human-readable or inspectable format.

Rationale: zero-friction onboarding, absolute privacy, and
independence from external services.

### VI. Type Safety End-to-End

TypeScript strict mode MUST be enabled project-wide with no
opt-outs in `tsconfig.json`.

- `any` is forbidden. Every use of `@ts-ignore` or `@ts-expect-error`
  MUST include a justification comment explaining why and a
  tracking issue for removal.
- Runtime validation (Zod or equivalent) MUST be applied at every
  trust boundary: user input, deserialization from storage,
  JSON/YAML parsing, IPC messages.
- Types for the data layer MUST be generated from the database
  schema (or its migration definitions), never hand-written.
  Source of truth is the schema, not the TypeScript file.

Rationale: in an offline app with no server to catch bad data,
the type system and runtime validators are the last line of
defense against data corruption.

### VII. Data Integrity & Resilience

User data is sacred. The application MUST protect it proactively:

- **Migrations**: forward-only, versioned. Every migration MUST be
  tested against a real data snapshot before release.
- **Backup**: automatic local backup MUST be created before each
  migration runs. The user MUST be able to restore from backup.
- **Export/Import**: full data export and import MUST be available
  at all times, in a documented format.
- **Error handling**: errors MUST be logged structurally (level,
  context, stacktrace) and presented to the user with an
  actionable recovery suggestion. No silent failures.
- **Undo for destructive actions**: delete, bulk update, and any
  irreversible operation MUST be undoable for a minimum of
  30 seconds after execution.

Rationale: without a cloud backend there is no "restore from
server." The app is the sole custodian of the user's data and
MUST behave accordingly.

### VIII. Test-First

Tests MUST be written before implementation (Red-Green-Refactor).

- Every user story MUST have acceptance scenarios that are
  independently testable.
- Unit tests cover pure logic; integration tests cover user
  journeys through the real stack.
- A failing test suite MUST block merging.

Rationale: a keyboard-driven, offline app has no server-side
safety net — correctness depends entirely on the client. Tests
are the only guardrail.

## Constraints & Scope

### Single-User Only

TaskFlow is designed for exactly one person. This is a hard scope
boundary, not a principle to be weighed — it constrains what
features may exist.

- No collaboration features, no permissions model, no sharing,
  no multi-tenancy.
- Features that only make sense in a team context MUST NOT be
  added.
- Data model MUST NOT carry fields (owner, org, role) that exist
  solely to support multi-user scenarios.

The single-user model keeps the codebase small, the data schema
flat, and the UX free of access-control noise.

## Performance Standards

- **Startup time**: cold start to interactive MUST be under 500 ms.
- **Input latency**: keypress-to-paint under 16 ms for all local
  operations (see Principle III).
- **Memory**: resident set size MUST stay below 150 MB with 10 000
  tasks loaded.
- **Storage**: data format MUST allow sub-second open/save for
  files up to 50 MB.
- **Rendering**: list views MUST maintain 60 fps while scrolling
  through 10 000 items (virtualization required).
- Performance regression tests MUST be part of the CI pipeline
  once a benchmark baseline is established.

## Development Workflow

- **Branching**: one feature branch per spec, named
  `###-feature-name` matching the spec directory.
- **Commits**: atomic, descriptive; one logical change per commit.
- **Code review**: every merge MUST pass constitution compliance
  check (see Governance).
- **CI gates**: lint, type-check, test suite, and (when available)
  performance benchmarks MUST all pass before merge.
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
  Scope, Performance Standards, and Development Workflow all
  carry equal binding force.
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

**Version**: 1.1.0 | **Ratified**: 2026-06-13 | **Last Amended**: 2026-06-13

# Feature Specification: Appearance & Theming

**Feature Branch**: `018-appearance-theming`

**Created**: 2026-06-13

**Status**: Draft

**Input**: Slice 018 of the TaskFlow MVP. Source of truth: `.specify/memory/product-vision.md`. Goal: provide dark and light visual modes, switchable from the command palette or application settings, following the operating system's color scheme by default. This is the final MVP slice — a requirement-level appearance increment layered on top of the existing UI, with the accessibility and resilience cross-cutting requirements carried through.

## Provenance

This slice realizes the following items from product-vision.md:

Slice-specific:
- FR-048 (dark/light visual modes; switch via command palette or settings; follow OS color scheme by default)
- ASM-07 (dark/light theme only; custom themes out of scope)

Cross-cutting (realized in this slice):
- FR-031 (suppress single-key shortcuts in text inputs)
- FR-042 (visible focus indicator)
- FR-043 (ARIA roles/labels)
- FR-044 (text contrast ≥ 4.5:1) — both the dark and light themes MUST independently meet this contrast requirement
- FR-045 (no collision with assistive-technology bindings)
- FR-046 (no hover-only content)
- FR-047 (prefers-reduced-motion)
- FR-049 (error message + recovery action)
- FR-050 (structured error logging)
- FR-051 (auto-backup before migration — infrastructure in place)
- FR-065 (per-user isolation — the theme preference is read/written only for the calling user)

MVP boundary confirmed:
- OOS-01..OOS-17 (full MVP out-of-scope confirmation)

Depends on:
- Slice 013 (command-palette-search) — provides the command palette through which the theme switch is exposed (the alternative entry point is application settings)

## User Scenarios & Testing *(mandatory)*

This is a requirement-level slice: it realizes the appearance requirement (FR-048) and the standing cross-cutting requirements, and owns no user-story acceptance scenarios. Product-vision.md assigns no user story or acceptance scenario (US-NN / US-NN.AS-NN) to appearance and theming, so none is reproduced here and none is invented.

> Scope note: the theme switch is reachable via the command palette (delivered in slice 013) or via application settings; the system follows the operating system's color scheme by default. Both the dark and light themes MUST independently meet the FR-044 contrast requirement (see Requirements and Constitution Compliance).

### Edge Cases

This slice introduces no new edge cases. Product-vision.md assigns no edge case (EC-NN) to appearance and theming.

## Requirements *(mandatory)*

### Functional Requirements (slice-specific)

- **FR-048**: System MUST support dark and light visual modes. The user MUST be able to switch between modes via the command palette or application settings. The system SHOULD detect and follow the operating system's color scheme preference by default.

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

Access control (realized in this slice):

This is a Tier A slice — the theme preference is per-user, so authorization rests on per-user isolation; there is no shared resource and therefore no membership or role tier applies.

- **FR-065**: Every query MUST be scoped to data the caller owns or has membership access to (per-user isolation).

The slice's command/query handlers ENFORCE FR-065 at the handler level (deny-by-default): the handler reads and writes the appearance preference only for the authenticated caller's own identity, never another user's. This is handler-level enforcement, not a mere reference.

### Key Entities

This slice introduces no new entity and touches none. Product-vision.md assigns no entity (ENT-NN) to appearance and theming; theme selection is an application-level appearance preference, not a data entity.

## Success Criteria *(mandatory)*

### Measurable Outcomes

This slice introduces no new slice-specific success criteria. Product-vision.md assigns no success criterion (SC-NNN) to appearance and theming. The standing accessibility expectation that both themes must meet the FR-044 contrast ratio is carried as a functional/cross-cutting requirement above and is evaluated under Constitution Compliance (Principle II).

## Constitution Compliance

This slice is evaluated against constitution v3.0.0. Cross-cutting principles realized here:

- **I. Keyboard-First**: the theme switch is reachable entirely via the keyboard — through the command palette (slice 013) or the keyboard-navigable application settings; no mouse interaction is required.
- **II. Accessibility (WCAG 2.1 AA)**: FR-042 (focus indicator), FR-043 (ARIA roles/labels), FR-044 (contrast ≥ 4.5:1), FR-045 (no AT-binding collisions), FR-046 (no hover-only content), FR-047 (prefers-reduced-motion). FR-031 keeps single-key shortcuts from hijacking text entry. Critically for this slice, FR-044 MUST be satisfied independently by both the dark and the light theme, so neither mode regresses contrast; FR-047 ensures the theme transition respects reduced-motion preferences.
- **III. Instant Response**: the theme switch paints its optimistic result within one animation frame (<16ms); the preference is persisted server-side and reconciled within the p95<200ms server budget. Principle III also covers real-time reconciliation of server-initiated SignalR updates to shared views, but the theme preference is per-user and not a shared item, so there is no cross-user fan-out to reconcile here; skeletons are permitted for network-bound loads (see Principle IV). No foreign success-criterion IDs are claimed by this slice.
- **IV. Minimalist UI**: dark/light modes are the entire theming surface — the configuration surface stays minimal (sensible default of following the OS color scheme), consistent with YAGNI discipline and ASM-07 (no custom themes). Skeleton screens are permitted for genuine network-bound loads per Principle IV.
- **V. Connected, Server-Authoritative**: the user's selected theme preference is written through the C# API and persisted in PostgreSQL (the client holds no authoritative copy); the OS color-scheme default is detected client-side from the browser/OS, but the user's explicit choice is server-authoritative — the switch is shown optimistically, then reconciled by the server.
- **VI. Type Safety End-to-End**: the theme selection is a typed enumeration (dark / light / follow-OS) validated at its trust boundary (settings deserialization and the OS color-scheme query).
- **VII. Data Integrity & Resilience**: FR-049 (error + recovery) and FR-050 (structured logging) govern any failure in reading or applying the appearance preference; FR-051 keeps the auto-backup hook in place ahead of any preference-schema change.
- **VIII. Test-First**: the realized cross-cutting requirements above are independently testable (Red-Green-Refactor), including automated contrast checks run against both themes and an authorization test asserting a caller cannot read or write another user's theme preference.
- **IX. Authentication & Authorization**: authorization is deny-by-default and enforced at the API/handler layer. This slice is Tier A: the theme preference is per-user, so the only authorization tier that applies is per-user isolation (FR-065) — the handler reads and writes the appearance preference solely for the authenticated caller's own identity, never another user's. There is no shared resource here, so no membership or role check (Tier B) is involved.

**Known compliance gap (deferred, accepted at slicing time):** none. The slice-018 brief notes no deferral or gap. FR-048's "SHOULD follow the OS color scheme by default" is the requirement's own conformance wording, not a deferred obligation.

## Assumptions

- **ASM-07 — Dark/light theme only**: Visual theming is limited to dark and light modes. Custom themes are out of scope.

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

Also out of scope for this slice specifically: custom themes, accent-color customization, and any theming beyond the dark/light modes are excluded per ASM-07 and OOS-10. This slice covers dark/light appearance and the OS-color-scheme default only.

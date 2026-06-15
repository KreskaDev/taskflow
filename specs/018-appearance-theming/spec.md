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
- FR-101 (ARIA-live for server-initiated updates/toasts + dialog focus contract)
- FR-049 (error message + recovery action)
- FR-050 (structured error logging)
- FR-051 (auto-backup before migration — infrastructure in place)
- FR-065..FR-068 (access control: dispatch by visibility; provenance is not a grant; sufficient-role gate; deny-by-default handler-level enforcement)
- FR-099 (content sanitization & CSP / security headers)
- FR-100 (secrets handling — never committed/baked/logged)

MVP boundary confirmed:
- OOS-01..OOS-19 (full MVP out-of-scope confirmation)

Depends on:
- Slice 001 (accounts-and-auth) — provides the authenticated caller identity and session (Google OAuth, admission-gated per FR-087) required to scope the per-user theme preference; this slice relies on it but does not re-implement authentication.
- Slice 013 (command-palette-search) — provides the command palette through which the theme switch is exposed (the alternative entry point is application settings)

## User Scenarios & Testing *(mandatory)*

This is a requirement-level slice: it realizes the appearance requirement (FR-048) and the standing cross-cutting requirements, and owns no user-story acceptance scenarios. Product-vision.md assigns no user story or acceptance scenario (US-NN / US-NN.AS-NN) to appearance and theming, so none is reproduced here and none is invented.

> Scope note: the theme switch is reachable via the command palette (delivered in slice 013) or via application settings; the system follows the operating system's color scheme by default. Both the dark and light themes MUST independently meet the FR-044 contrast requirement (see Requirements and Constitution Compliance).

### Edge Cases

This slice introduces no new edge cases. Product-vision.md assigns no edge case (EC-NN) to appearance and theming.

## Requirements *(mandatory)*

### Functional Requirements (slice-specific)

- **FR-048**: System MUST support dark and light visual modes. The user MUST be able to switch between modes via the command palette or application settings. The system SHOULD detect and follow the operating system's color scheme preference by default.

Slice-specific elaboration of FR-048 (theme/preferences are per-user — a UserSettings home):
- The selected theme is a per-user preference: it MUST be read and written only for the authenticated caller's own identity (a UserSettings home keyed to the User), and it MUST persist across sessions (it is server-authoritative in PostgreSQL, not merely a client-local value), so a returning user sees their last chosen mode.
- The system MUST avoid a flash of the wrong theme on initial load: the effective theme (the user's persisted explicit choice, or the OS color scheme when "follow-OS" is selected) MUST be resolved and applied before first contentful paint, so the page does not paint in one mode and then visibly switch.

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

Security (per Constitution Principle XII):
- **FR-099**: User-authored content MUST be output-encoded/sanitized so raw HTML injection is impossible, and a Content-Security-Policy plus standard security response headers MUST be present in production.
- **FR-100**: Secrets (session signing key, OAuth client secret, database/broker credentials, deploy keys) MUST be injected at runtime via environment or a secret store, never committed to the repository or baked into images, and MUST NOT appear in logs or error context.

Error Handling & Data Integrity (per Constitution Principle VII):
- **FR-049**: All errors MUST be presented to the user with a clear message and an actionable recovery suggestion. No operation may fail silently.
- **FR-050**: Errors MUST be logged with structured context (severity level, operation context, and error details) for debugging purposes.
- **FR-051**: Before any data migration, the system MUST automatically create a backup of user data. The user MUST be able to restore from this backup.

Access control (realized in this slice — per Constitution Principle IX):

Authorization is dispatched by the containing resource's visibility, not a conjunction of tiers. The theme/appearance preference is per-user personal data (a UserSettings home owned by the calling user); there is no shared project involved, so the dispatch resolves to the ownership path: the preference is authorized on ownership and queries are scoped to the caller. No membership/role path applies because no shared resource is touched. `createdBy`/ownership is the authorization basis here, not mere provenance.

- **FR-065**: Authorization MUST be dispatched by the containing resource's visibility (not a conjunction of tiers): personal/unprojected data authorizes on ownership (`createdBy`/`ownerId`) with queries scoped to the caller; shared-project entities authorize on current `ProjectMembership` + role. Every query MUST be scoped accordingly (per-user isolation).
- **FR-066**: Access to a shared project's data MUST require current membership in that project. `createdBy` and assignee are provenance only and confer NO standalone access; on leave/remove/unshare a user MUST lose ALL access to that project's data regardless of authorship or assignment. (For this slice, the appearance preference is never a shared-project resource, so the user's own ownership is the sole basis of access.)
- **FR-067**: Each operation on a shared-project resource MUST require sufficient role (viewer=read, editor=write, owner=manage); insufficient role MUST be denied. (Not exercised by this slice, as the preference is personal.)
- **FR-068**: Authorization MUST be deny-by-default and enforced at the API/handler layer for every read and write.

The slice's command/query handlers ENFORCE FR-065 and FR-068 at the handler level (deny-by-default): the handler reads and writes the appearance preference only for the authenticated caller's own identity (the caller's UserSettings home), never another user's. This is handler-level enforcement, not a mere reference.

### Key Entities

Product-vision.md assigns no entity (ENT-NN) to appearance and theming; theme selection is an application-level per-user appearance preference, not a vision-level domain entity (ENT-01..ENT-10). It is persisted in a per-user settings home (UserSettings) keyed to the authenticated User (ENT-06), so the chosen theme survives across sessions and is read/written only for its owning user. No shared-project entity, membership, or role is involved.

## Success Criteria *(mandatory)*

### Measurable Outcomes

This slice introduces no new slice-specific success criteria. Product-vision.md assigns no success criterion (SC-NNN) to appearance and theming. The standing accessibility expectation that both themes must meet the FR-044 contrast ratio is carried as a functional/cross-cutting requirement above and is evaluated under Constitution Compliance (Principle II).

## Constitution Compliance

This slice is evaluated against constitution v4.0.0. Cross-cutting principles realized here:

- **I. Keyboard-First**: the theme switch is reachable entirely via the keyboard — through the command palette (slice 013) or the keyboard-navigable application settings; no mouse interaction is required.
- **II. Accessibility (WCAG 2.1 AA)**: FR-042 (focus indicator), FR-043 (ARIA roles/labels), FR-044 (contrast ≥ 4.5:1), FR-045 (no AT-binding collisions), FR-046 (no hover-only content), FR-047 (prefers-reduced-motion). FR-031 keeps single-key shortcuts from hijacking text entry. FR-101 carries the strengthened v4.0.0 status-message and dialog obligations: any server-initiated update or toast surfaced while applying the preference (e.g., an FR-049 recovery toast on a failed persist) MUST be announced via a polite ARIA live region without stealing focus, and the settings/command-palette dialog through which the theme is switched MUST follow the dialog focus contract (initial focus, focus trap, Esc to dismiss, focus returned to the invoker on close). Critically for this slice, FR-044 MUST be satisfied independently by both the dark and the light theme, so neither mode regresses contrast; FR-047 ensures the theme transition respects reduced-motion preferences.
- **III. Instant Response**: the theme switch paints its optimistic result within one animation frame (<16ms); the preference is persisted server-side and reconciled within the p95<200ms server-mutation budget (a MUST in v4.0.0). Principle III also covers real-time reconciliation of server-initiated SignalR updates to shared views, but the theme preference is per-user and not a shared item, so there is no cross-user fan-out to reconcile here; skeletons are permitted for network-bound loads (see Principle IV). No foreign success-criterion IDs are claimed by this slice.
- **IV. Minimalist UI**: dark/light modes are the entire theming surface — the configuration surface stays minimal (sensible default of following the OS color scheme), consistent with YAGNI discipline and ASM-07 (no custom themes). Skeleton screens are permitted for genuine network-bound loads per Principle IV.
- **V. Connected, Server-Authoritative**: the user's selected theme preference is written through the C# API and persisted in PostgreSQL (the client holds no authoritative copy); the OS color-scheme default is detected client-side from the browser/OS, but the user's explicit choice is server-authoritative — the switch is shown optimistically, then reconciled by the server.
- **VI. Type Safety End-to-End**: the theme selection is a typed enumeration (dark / light / follow-OS) validated at its trust boundary (settings deserialization and the OS color-scheme query). Any API failure surfaces through the ProblemDetails-based error contract (FR-093 / ADR-0009) modeled by the generated client.
- **VII. Data Integrity & Resilience**: FR-049 (error + recovery) and FR-050 (structured logging) govern any failure in reading or applying the appearance preference; FR-051 keeps the auto-backup hook in place ahead of any preference-schema change.
- **VIII. Test-First**: the realized cross-cutting requirements above are independently testable (Red-Green-Refactor), including automated contrast checks run against both themes and the v4.0.0 authorization allow+deny test gate — an allow test for the caller reading/writing their own UserSettings preference and a deny test asserting a caller cannot read or write another user's theme preference.
- **IX. Authentication & Authorization**: authorization is deny-by-default and enforced at the API/handler layer, dispatched by the resource's visibility (NOT a Tier A/B conjunction). The appearance preference is per-user personal data (a UserSettings home), so the dispatch resolves to the ownership path (FR-065): the handler reads and writes the preference solely for the authenticated caller's own identity, never another user's. No shared project, `ProjectMembership`, or role is involved here, so the membership/role path (FR-066/FR-067) is not exercised by this slice; `createdBy`/ownership here is the authorization basis, while in general `createdBy`/assignee are provenance only and confer no standalone access (FR-066). Every read and write goes through deny-by-default handler-level enforcement (FR-068). Authentication itself (Google OAuth, admission-gated sign-up per FR-087, session policy FR-088, CSRF/SameSite FR-089, OAuth hardening FR-090, BFF→API carrier integrity FR-091) is delivered in slice 001 and is a precondition for identifying the caller whose settings are read/written; this slice relies on it but does not re-implement it.
- **X. Time & Timezone**: this slice performs no date-relative computation; theme persistence carries only standard UTC `created_at`/`updated_at` timestamps (FR-092). No Today/Upcoming, cycle, recurrence, or natural-language date logic is involved, so the `Europe/Warsaw` reference-zone rule has no slice-specific surface beyond storing timestamps in UTC.
- **XI. Privacy & Personal Data**: the theme preference is per-user personal data and is part of the UserSettings owned by the User (ENT-06). On account deletion it is removed/anonymized with the rest of the user's personal data under the FR-085 erasure cascade, and it falls under the FR-086 data-retention stance (retained until account deletion). This slice introduces no new category of personal data beyond a stored UI preference.
- **XII. Security by Default**: FR-099 (content sanitization + CSP and security response headers in production) and FR-100 (secrets injected at runtime, never committed/baked/logged) apply. The theme preference is a constrained typed enumeration, not free-form user content, so there is no stored-content injection surface here; nonetheless any rendered settings copy is output-encoded and the production CSP/security headers remain in force.

**Known compliance gap (deferred, accepted at slicing time):** none. The slice-018 brief notes no deferral or gap. FR-048's "SHOULD follow the OS color scheme by default" is the requirement's own conformance wording, not a deferred obligation. The system MUST avoid a flash of the wrong theme on initial load (see FR-048 note below) by resolving the persisted/OS-derived theme before first paint.

## Assumptions

- **ASM-07 — Dark/light theme only**: Visual theming is limited to dark and light modes. Custom themes are out of scope.

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

Note: multi-user collaboration, sharing, and in-app notifications are NOT out of scope — they are in scope for the MVP (OOS-01 PROMOTED; OOS-06 PARTIAL, in-app notifications in scope). Only the specific exclusions listed above (push/device notifications, email notifications, non-Google SSO, per-user timezones, pending pre-account invitations, organizations/multi-tenancy, etc.) remain out of scope.

Also out of scope for this slice specifically: custom themes, accent-color customization, and any theming beyond the dark/light modes are excluded per ASM-07 and OOS-10. This slice covers dark/light appearance and the OS-color-scheme default only.

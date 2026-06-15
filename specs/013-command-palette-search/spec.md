# Feature Specification: Command Palette & Search

**Feature Branch**: `013-command-palette-search`

**Created**: 2026-06-13

**Status**: Draft

**Input**: Slice 013 of the TaskFlow MVP. Source of truth: `.specify/memory/product-vision.md`. Goal: a `Ctrl+K` command palette providing fuzzy search across tasks, projects, labels, and actions — each action showing its keyboard shortcut, and selecting any result navigating to the item or executing the action — plus a `/` filter scoped to the current view. Search and palette results are access-scoped: a user sees only the tasks, projects, and labels they own or have membership access to (FR-067). This slice completes the global-shortcut set begun in slice 002 (`Ctrl+K` and `/` join the already-shipped `C` and `?`) and lands the fifth and final primary user journey (search & command), at which point all five journeys pass end-to-end. Builds on the projects from slice 004 and the labels from slice 006, which the palette searches and navigates to.

## Provenance

This slice realizes the following items from product-vision.md:

Slice-specific:
- US-04 (Command Palette & Search) — full: AS-01, AS-02, AS-03, AS-04, AS-05, AS-06, AS-07
- US-08 (Keyboard Navigation & Shortcuts) — subset: AS-08 (`/` filter the current view)
- FR-027 (global shortcuts set — completes here with `Ctrl+K` and `/`)
- FR-032 (fuzzy search across tasks, projects, labels, and actions)
- FR-033 (each palette action displays its keyboard shortcut)
- FR-034 (selecting a result navigates to the item or executes the action)
- SC-006 (all five primary user journeys pass end-to-end — reached at this slice)
- SC-009 (fuzzy search across 10,000+ tasks under 50ms)

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
- FR-051 (auto-backup before migration — infrastructure in place)
- FR-101 (ARIA-live for server-initiated updates/toasts + dialog focus contract for the command palette)

Access control (realized in this slice — dispatch by visibility):
- FR-065 (authorization dispatched by the containing resource's visibility — ownership for personal/unprojected, membership+role for shared-project)
- FR-066 (access to shared-project data requires current membership; createdBy/assignee are provenance only)
- FR-067 (each operation requires sufficient role: viewer=read, editor=write, owner=manage)
- FR-068 (authorization deny-by-default, enforced at the API/handler layer for every read and write)
- SC-013 (authorization enforced on 100% of data operations; deny cases covered by integration tests)

This slice's search/query handlers ENFORCE these checks at the handler level (they do not merely reference them): the command palette's fuzzy-search and the `/` view-filter run through a server search endpoint whose authorization is dispatched by each candidate's visibility — personal/unprojected items authorize on ownership (`createdBy`/`ownerId`) scoped to the caller (FR-065), shared-project items on current `ProjectMembership` plus a sufficient role (FR-066, FR-067), deny-by-default (FR-068). This is not a conjunction of tiers, and `createdBy`/assignee confer no standalone access. Items the caller may not access are never returned as results, and on a leave/remove/unshare access-loss event the caller's result set is invalidated and refetched so a now-inaccessible item can never surface (FR-095).

MVP boundary confirmed:
- OOS-01..OOS-19 (full MVP out-of-scope confirmation)

Depends on:
- Slice 004 (project-management) — provides the projects the palette searches and navigates to (US-04.AS-05), and the command-palette/dedicated-action surface through which "Create Project" is exposed as a searchable action (US-04.AS-04)
- Slice 006 (labels) — provides the labels the palette searches over (FR-032)
- Slice 007 (project-sharing-membership) — establishes the membership/role authorization (FR-066, FR-067, FR-068) that scopes which projects, tasks, and labels appear in this slice's search results

Note on completed shortcut set: the FR-027 global-shortcut set is owned and completed in this slice. Its earlier members already shipped via acceptance scenarios in slice 002 — `C` (create task, US-01.AS-01) and `?` (shortcuts help, US-08.AS-07). US-04 and US-08 here exercise the final two members: `Ctrl+K` (command palette, US-04.AS-01) and `/` (search/filter the current view, US-08.AS-08), completing the set. This is framing only; no new requirement text is introduced.

Note on primary user journeys: SC-006 requires all five primary user journeys — daily capture, planning session, project work, cycle review, and search & command — to pass end-to-end automated tests. The first four shipped through slices 001–007; the fifth (search & command) is delivered by this slice, so SC-006 is reached here. This is milestone framing only; SC-006 text is copied verbatim below.

## User Scenarios & Testing *(mandatory)*

### User Story 4 - Command Palette & Search (Priority: P2)

User presses `Ctrl+K` to open a command palette that provides fuzzy search across tasks, projects, labels, and actions. They can instantly find and navigate to any item or execute any action.

**Why this priority**: The command palette is the central navigation hub for keyboard-first users. It ties all features together and enables power-user speed.

**Independent Test**: Can be tested by creating tasks, projects, and labels with known names, opening the command palette, typing partial matches, and verifying results appear with correct navigation.

> Scope note: this slice completes the global-shortcut set (FR-027) — the `Ctrl+K` (command palette) member is exercised by US-04.AS-01 below and the `/` (search the current view) member by US-08.AS-08, while the earlier `C` and `?` members shipped via slice 002. Switching the visual theme from the command palette (FR-048) is owned by slice 018 (appearance-theming); see Out of Scope.

**Acceptance Scenarios** (owned by this slice):

1. **(US-04.AS-01) Given** the app is open on any view, **When** user presses `Ctrl+K`, **Then** a command palette overlay appears with a focused search input within 16ms.
2. **(US-04.AS-02) Given** the command palette is open, **When** user types "mleko", **Then** matching tasks (by title or description) appear in the results list in under 50ms.
3. **(US-04.AS-03) Given** the command palette shows task results, **When** user selects a task and presses Enter, **Then** the app navigates to that task's location (project or Inbox).
4. **(US-04.AS-04) Given** the command palette is open, **When** user types "create project", **Then** the "Create Project" action appears in results with its keyboard shortcut displayed.
5. **(US-04.AS-05) Given** the command palette is open, **When** user types a project name, **Then** matching projects appear and selecting one navigates to that project's view.
6. **(US-04.AS-06) Given** the command palette is open, **When** user presses Esc, **Then** the palette closes and focus returns to the previous view.
7. **(US-04.AS-07) Given** the app has 10,000+ tasks, **When** user types in the command palette, **Then** results appear in under 50ms.

> Access-scoping note: every result above is access-scoped. Authorization is dispatched by each candidate's visibility — the palette surfaces personal/unprojected tasks, projects, and labels the current user owns (`createdBy`/`ownerId`, FR-065) and shared-project items the user can reach through current `ProjectMembership` at a sufficient role (FR-066, FR-067), enforced deny-by-default at the server search endpoint (FR-068). This is not a Tier-conjunction, and `createdBy`/assignee are provenance only — they grant no standalone access. A task or project the caller cannot access never appears as a result, and selecting a result never navigates the caller into data they lack access to. The matches in US-04.AS-02 and US-04.AS-05 are drawn from the caller's accessible working set, not the whole team's data; the 10,000+ tasks of US-04.AS-07 are that per-user authorization-scoped accessible working set, not a flat global list.

---

### User Story 8 - Keyboard Navigation & Shortcuts (Priority: P1)

User filters the currently open view by pressing `/`, which focuses a search input scoped to that view. This is the global `/` shortcut and the second of the two global-shortcut members completed in this slice (alongside `Ctrl+K`); it narrows what is shown in the current view rather than opening the cross-cutting command palette.

**Why this priority**: Keyboard-first is the core principle. Without complete keyboard coverage, the app fails its primary promise.

**Independent Test**: Can be tested by opening any list view, pressing `/`, verifying a search input receives focus, typing a query, and verifying the current view filters to matching items.

**Acceptance Scenarios** (owned by this slice):

1. **(US-08.AS-08) Given** the app is open, **When** user presses `/`, **Then** a search input is focused for filtering the current view.

> Shortcut coverage: `/` is implemented and its canonical scenario (US-08.AS-08) is owned here. Together with `Ctrl+K` (US-04.AS-01), it completes the global-shortcut requirement FR-027, which is owned by this slice. See Provenance.

### Edge Cases

No edge cases (EC-NN) are assigned to this slice in product-vision.md. The 10,000+ tasks performance characterization (EC-06) is owned by slice 002; the in-palette manifestation of that performance bound is captured here by US-04.AS-07 and SC-009.

## Requirements *(mandatory)*

### Functional Requirements (slice-specific)

- **FR-027**: System MUST support all specified global shortcuts: `C` (create task), `Ctrl+K` (command palette), `/` (search), `?` (shortcuts help).
- **FR-032**: The command palette MUST provide fuzzy search across tasks (title and description), projects, labels, and actions.
- **FR-033**: Each action in the command palette MUST display its assigned keyboard shortcut.
- **FR-034**: Selecting a result in the command palette MUST navigate to the item or execute the action.

> Scope note: FR-027 is owned in full here and reaches completion in this slice. Its earlier members already shipped through acceptance scenarios in slice 002 — `C` (US-01.AS-01) and `?` (US-08.AS-07). This slice's scenarios exercise the final members: `Ctrl+K` (US-04.AS-01) and `/` (US-08.AS-08), completing the set.

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

Access Control (dispatch by visibility — per Constitution Principle IX; enforced in this slice's search/query handlers):
- **FR-065**: Authorization MUST be dispatched by the containing resource's visibility (not a conjunction of tiers): personal/unprojected data authorizes on ownership (`createdBy`/`ownerId`) with queries scoped to the caller; shared-project entities authorize on current `ProjectMembership` + role. Every query MUST be scoped accordingly (per-user isolation).
- **FR-066**: Access to a shared project's data MUST require current membership in that project. `createdBy` and assignee are provenance only and confer NO standalone access; on leave/remove/unshare a user MUST lose ALL access to that project's data regardless of authorship or assignment.
- **FR-067**: Each operation on a shared-project resource MUST require sufficient role (viewer=read, editor=write, owner=manage); insufficient role MUST be denied.
- **FR-068**: Authorization MUST be deny-by-default and enforced at the API/handler layer for every read and write.

These are enforced at the handler level, not merely referenced: the fuzzy-search and `/` view-filter both execute through the server search endpoint, whose authorization is dispatched per candidate's visibility — a caller's palette and filter results contain only personal/unprojected items they own (FR-065) and shared-project items they can reach through current membership (FR-066) at a sufficient role (FR-067), deny-by-default (FR-068). This is not a Tier-A/Tier-B conjunction, and `createdBy`/assignee never act as a standalone grant. Inaccessible tasks, projects, and labels are excluded from results, and an access-loss event (leave/remove/unshare) invalidates and refetches the result set (FR-095) so a now-inaccessible item can never surface.

### Key Entities

This slice introduces no new entity and populates no entity attribute. The command palette reads existing data — tasks (owned by slice 002), projects (owned by slice 004), and labels (owned by slice 006) — to search and navigate over it; no Key Entity (ENT-NN) is assigned to this slice in product-vision.md.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-006**: All five primary user journeys (daily capture, planning session, project work, cycle review, search & command) pass end-to-end automated tests.
- **SC-009**: Fuzzy search across 10,000+ tasks returns results in under 50ms.

## Constitution Compliance

This slice is evaluated against constitution v4.0.0. Cross-cutting principles realized here:

- **I. Keyboard-First**: the palette is the central keyboard-first navigation hub — opened with `Ctrl+K`, driven entirely by typing and Enter/Esc, with no mouse required. This slice completes the global-shortcut grammar (FR-027) begun in slice 002, so the full global shortcut set (`C`, `Ctrl+K`, `/`, `?`) is now consistent and composable across all views, and FR-033 surfaces each action's shortcut, keeping shortcuts discoverable.
- **II. Accessibility (WCAG 2.1 AA)**: FR-042 (focus indicator on the search input and every result), FR-043 (ARIA roles/labels for the palette overlay and its listbox/option semantics), FR-044 (contrast ≥ 4.5:1), FR-045 (no AT-binding collisions — relevant since `Ctrl+K` and `/` join the active set), FR-046 (no hover-only content), FR-047 (prefers-reduced-motion for the overlay transition). FR-031 keeps single-key shortcuts from hijacking text entry inside the palette and the `/` filter input. FR-101 governs the command palette as a dialog: opening it sets initial focus into the search input (US-04.AS-01) and traps focus while open, Esc dismisses it and returns focus to the invoking view (US-04.AS-06); the live, server-fetched result list is announced to assistive technology via a polite ARIA live region without stealing focus as the caller types.
- **III. Instant Response**: US-04.AS-01 (palette open within 16ms) and US-04.AS-02 / US-04.AS-07 / SC-009 (results under 50ms, including at 10,000+ tasks) keep the palette within the instant-response envelope; the keypress-to-paint and virtualization performance criteria themselves are owned by slice 002. Search and navigation are read-only, so this slice initiates no optimistic mutation; the real-time reconciliation clause (server-initiated SignalR updates yielding to in-flight local edits under last-write-wins) is owned by slice 016 (real-time-collaboration) and is not exercised here, though a live remote change to an accessible item will be reflected in subsequently issued search results.
- **V. Connected, Server-Authoritative**: this is a connected multi-user web app. Fuzzy search runs against a server search endpoint exposed by the C# API (not a client-only index), with authorization filtering applied INSIDE the under-50ms budget over the caller's access-scoped working set; the caller's accessible-project set is resolved cheaply (cached per session) so the authorization filter stays within budget. PostgreSQL, accessed through the C# API, remains the source of truth for the tasks, projects, and labels the palette searches over; the client holds no authoritative copy.
- **VI. Type Safety End-to-End**: palette result types (task / project / label / action) are discriminated and typed; the search query boundary is validated at runtime, and action descriptors carry their typed shortcut bindings for FR-033.
- **VII. Data Integrity & Resilience**: search and navigation are read-only over existing data, so no data is mutated here; FR-049 (clear message + recovery) and FR-050 (structured logging) cover any palette/search failure, and FR-051 keeps the backup hook in place. Skeleton screens are permitted for the network-bound initial load of the searchable working set (Principle IV), but MUST NOT mask a result that is already available.
- **VIII. Test-First**: each owned acceptance scenario above (US-04.AS-01..AS-07 and US-08.AS-08) is independently testable (Red-Green-Refactor), including authorization tests that a caller never receives results for tasks, projects, or labels they cannot access (FR-065..FR-068, SC-013). Because the search & command journey lands here as the fifth primary journey, SC-006 (all five journeys pass end-to-end) is reached at this slice.
- **IX. Authentication & Authorization**: this slice authorizes its own operations deny-by-default at the server search endpoint / query-handler layer. Its only operations are reads (fuzzy search, `/` view-filter, result navigation), so authorization reduces to scoping the result set, and that scoping is dispatched by each candidate's visibility — NOT a conjunction of tiers. Personal/unprojected items authorize on ownership (`createdBy`/`ownerId`) with the query scoped to the caller (FR-065); shared-project items authorize on current `ProjectMembership` plus a sufficient role (FR-066, FR-067); all deny-by-default (FR-068). `createdBy` and assignee are provenance only and never grant standalone access, and on leave/remove/unshare the affected caller loses ALL access to that project's items so they stop appearing as results. SC-013 covers enforcement on 100% of these read operations; SC-016 covers the allow+deny test matrix for the search/filter handlers.
- **X. Time & Timezone**: this slice performs no date-relative computation — search and the `/` filter match on text (title, description, project/label name, action), not on Today/Upcoming membership, cycle boundaries, or recurrence. Any due dates shown in results are display-only and rendered against the single instance reference timezone `Europe/Warsaw` (FR-092); the slice introduces no new time rule of its own.
- **XI. Privacy & Personal Data**: search must never become a back-channel to data a caller has lost access to. An access-loss event — a member leaving, being removed, a project being unshared, or an account being deleted — MUST invalidate and refetch the caller's (and, for membership loss, the removed member's) search/filter result set so a now-inaccessible task, project, or label can never surface as a result (consistent with FR-095 live-subscription re-authorization and the FR-066 revoke-all-on-leave rule). The palette holds no separate authoritative cache that could outlive a revocation.
- **XII. Security by Default**: the palette renders untrusted user-authored content — task titles and markdown descriptions (which FR-032 searches) and label names — so every result label and snippet MUST be output-encoded/sanitized to the safe subset on render; raw HTML injection through a crafted task title, description, or label name MUST be impossible (FR-099). Search query input crossing the API boundary is validated (Principle VI); no secret is read or emitted by this slice (FR-100).

No compliance gap is introduced by this slice. The palette's read-only behavior introduces no destructive action, so the Principle VII undo requirement (FR-040, owned by slice 014) does not apply here.

## Assumptions

No assumptions (ASM-NN) are assigned to this slice in product-vision.md. The multi-user, web-platform assumptions that frame the product (ASM-01, ASM-02, ASM-08) — a connected collaborative web app for a small team (~10) targeting modern desktop browsers, where each user authenticates with Google and has personal data plus access to shared projects — are established by slices 001 (accounts-and-auth) and 002 (task-capture) and continue to apply. They are why this slice's search results are access-scoped per user.

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

Also out of scope for this slice specifically (deferred to later slices within the MVP): switching the visual theme via the command palette (FR-048) is owned by slice 018 (appearance-theming) — the palette becomes a surface for that action there, not here; the 30-second undo window for destructive actions (FR-040 / US-09) is owned by slice 014 (undo) and does not apply to this slice's read-only search and navigation. This slice covers the command palette, its fuzzy search across tasks/projects/labels/actions, action-shortcut display, result navigation/execution, and the `/` current-view filter only — all access-scoped to the caller (FR-065..FR-068). Defining the access model itself (membership and roles) is owned by slice 007 (project-sharing-membership); this slice consumes that model to scope results.

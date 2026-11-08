# Feature Specification: Command Palette & Search

**Feature Branch**: `013-command-palette-search`

**Created**: 2026-06-13

**Updated**: 2026-11-08 — aligned to constitution v5.1.0 (Principle I: UI-First Operability) and
the product-vision UI-first reinterpretation clause; slice 019 (ui-design-system) removed the
single-key shortcut system (FR-111, OOS-20) and product-vision marks FR-027 and FR-033 as
[DEFERRED]. The `Ctrl+K` and `/` triggers named in US-04.AS-01 / US-08.AS-08 are therefore
reinterpreted as visible-UI-control triggers with unchanged behavior (owner decision, Clarifications
2026-11-08): the topbar search box opens the command palette; each list view carries a visible
filter input. Slice 004 shipped projects with the „Zarchiwizowane" sidebar listing and deferred the
palette-based archived-project search (US-10.AS-06) to this slice.

**Status**: Draft

**Input**: Slice 013 of the TaskFlow MVP. Source of truth: `.specify/memory/product-vision.md`. Goal: a command palette — opened from the visible topbar search box — providing fuzzy search across tasks, projects, labels, and actions, where selecting any result navigates to the item or executes the action; plus a visible filter input scoped to the current view (the behavior the deferred `/` shortcut named). Shortcut triggers (`Ctrl+K`, `/`) and per-action shortcut labels (FR-033) are deferred with the shortcut system (OOS-20). Search and palette results are access-scoped: a user sees only the tasks, projects, and labels they own or have membership access to (FR-067). This slice lands the fifth and final primary user journey (search & command), at which point all five journeys pass end-to-end. The global-shortcut set (FR-027) stays deferred (OOS-20); this slice delivers the operations those shortcuts would have triggered through visible controls. Builds on the projects from slice 004 and the labels from slice 006, which the palette searches and navigates to.

## Provenance

This slice realizes the following items from product-vision.md:

Slice-specific:
- US-04 (Command Palette & Search) — full: AS-01, AS-02, AS-03, AS-04, AS-05, AS-06, AS-07
- US-08 (Keyboard Navigation & Shortcuts) — subset: AS-08 (`/` filter the current view)
- FR-027 [DEFERRED in product-vision — OOS-20; the `Ctrl+K` / `/` OPERATIONS (open the palette, filter the current view) are realized here through visible controls, the shortcut triggers are not]
- FR-032 (fuzzy search across tasks, projects, labels, and actions)
- FR-033 [DEFERRED with the shortcut system — see US-18/OOS-20; actions are listed and executable, no shortcut label is shown]
- FR-034 (selecting a result navigates to the item or executes the action)
- US-10.AS-06 (owned by slice 004, realized here: an archived project appears in palette results and can be unarchived from the result)
- SC-006 (all five primary user journeys pass end-to-end — reached at this slice)
- SC-009 (fuzzy search across 10,000+ tasks under 50ms)

Cross-cutting (realized in this slice):
- FR-103 (every operation has a visible affordance — the palette opener, the view filter and every palette action are visible controls)
- FR-110 (empty states carry a hint + action — the palette's "no results" state)
- FR-031 [DORMANT while FR-027..029 are deferred — no single-key shortcuts exist; listed for traceability only]
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

Note on the shortcut set: FR-027 is [DEFERRED] in product-vision (constitution v5.0.0+, OOS-20) and slice 019 removed the earlier `C`/`?` bindings (FR-111). This slice realizes the two OPERATIONS the deferred `Ctrl+K` and `/` members named — opening the command palette and filtering the current view — through visible controls per the UI-first reinterpretation clause. No shortcut binding is added; the set is completed, if ever, by the future accelerator slice. This is framing only; no new requirement text is introduced.

Note on primary user journeys: SC-006 requires all five primary user journeys — daily capture, planning session, project work, cycle review, and search & command — to pass end-to-end automated tests. The first four shipped through slices 001–007; the fifth (search & command) is delivered by this slice, so SC-006 is reached here. This is milestone framing only; SC-006 text is copied verbatim below.

## Clarifications

### Session 2026-11-08

- Q: Under constitution v5.1.0 (FR-027/FR-033 deferred, OOS-20) how are the palette (`Ctrl+K`) and the view filter (`/`) triggered? → A: Visible UI only (owner decision). The topbar search box („Szukaj…", today a disabled stub) becomes the palette opener on every view; each list view (Inbox, Dziś, Nadchodzące, Przypisane, project List/Board, `/cycle`) gets a visible „Filtruj…" input in its header. No `Ctrl+K`/`/` bindings and no per-action shortcut labels ship; they return, if ever, with the accelerator slice.
- Q: Does the view filter call the server search endpoint or narrow the already-loaded rows? → A: It narrows the rows the view already holds (client-side, instant, case/diacritic-insensitive substring match on title — those rows are already access-scoped by the view's own query). Only the command palette calls the server search endpoint. The spec's "both run through the server endpoint" sentence is superseded by this answer.
- Q: Which actions does the palette offer? → A: A fixed catalogue: „Nowy task" (opens the global capture dialog), „Nowy projekt" (opens the project form), „Nowy cykl" (opens the cycle form on `/cycle`), and navigation to the primary views (Inbox, Dziś, Nadchodzące, Przypisane, Cykl, Ustawienia). Actions match on their Polish label and on English aliases from product-vision („create project"); each executes exactly what its visible button does.
- Q: Do archived projects appear in results? → A: Yes — with the suffix „(zarchiwizowany)" and an inline „Przywróć" (unarchive) action on the result (US-10.AS-06, owned by slice 004 and realized here); selecting the project itself navigates to its (archived) view.
- Q: Where does selecting a task result navigate? → A: To the task's location — the Inbox for an unprojected task, the owning project's List for a projected one — with the task opened in the details drawer (`?task=<id>`), so the row is selected and in view.

## User Scenarios & Testing *(mandatory)*

### User Story 4 - Command Palette & Search (Priority: P2)

User presses `Ctrl+K` to open a command palette that provides fuzzy search across tasks, projects, labels, and actions. They can instantly find and navigate to any item or execute any action.

**Why this priority**: The command palette is the central navigation hub for keyboard-first users. It ties all features together and enables power-user speed.

**Independent Test**: Can be tested by creating tasks, projects, and labels with known names, opening the command palette, typing partial matches, and verifying results appear with correct navigation.

> Scope note (UI-first reinterpretation): wherever a scenario below names `Ctrl+K` or `/` as the trigger, the trigger is the visible control — the topbar search box („Szukaj…") opens the command palette (US-04.AS-01) and each list view's visible filter input narrows that view (US-08.AS-08) — with the scenario's BEHAVIOR unchanged (Clarifications 2026-11-08). Switching the visual theme from the command palette (FR-048) is owned by slice 018 (appearance-theming); see Out of Scope.

**Acceptance Scenarios** (owned by this slice):

1. **(US-04.AS-01) Given** the app is open on any view, **When** user presses `Ctrl+K`, **Then** a command palette overlay appears with a focused search input within 16ms. *(Reinterpreted trigger: activating the topbar search box — click, or Enter/Space on the focused box — opens the palette; the behavior is unchanged.)*
2. **(US-04.AS-02) Given** the command palette is open, **When** user types "mleko", **Then** matching tasks (by title or description) appear in the results list in under 50ms.
3. **(US-04.AS-03) Given** the command palette shows task results, **When** user selects a task and presses Enter, **Then** the app navigates to that task's location (project or Inbox). *(Clarified: the destination is the Inbox or the owning project's List with the task opened in the details drawer, `?task=<id>`.)*
4. **(US-04.AS-04) Given** the command palette is open, **When** user types "create project", **Then** the "Create Project" action appears in results with its keyboard shortcut displayed. *(FR-033 is deferred: the action appears and executes — opening the same „Nowy projekt" dialog as the sidebar button — without a shortcut label.)*
5. **(US-04.AS-05) Given** the command palette is open, **When** user types a project name, **Then** matching projects appear and selecting one navigates to that project's view.
6. **(US-04.AS-06) Given** the command palette is open, **When** user presses Esc, **Then** the palette closes and focus returns to the previous view.
7. **(US-04.AS-07) Given** the app has 10,000+ tasks, **When** user types in the command palette, **Then** results appear in under 50ms.

> Access-scoping note: every result above is access-scoped. Authorization is dispatched by each candidate's visibility — the palette surfaces personal/unprojected tasks, projects, and labels the current user owns (`createdBy`/`ownerId`, FR-065) and shared-project items the user can reach through current `ProjectMembership` at a sufficient role (FR-066, FR-067), enforced deny-by-default at the server search endpoint (FR-068). This is not a Tier-conjunction, and `createdBy`/assignee are provenance only — they grant no standalone access. A task or project the caller cannot access never appears as a result, and selecting a result never navigates the caller into data they lack access to. The matches in US-04.AS-02 and US-04.AS-05 are drawn from the caller's accessible working set, not the whole team's data; the 10,000+ tasks of US-04.AS-07 are that per-user authorization-scoped accessible working set, not a flat global list.

---

### User Story 8 - Keyboard Navigation & Shortcuts (Priority: P1)

User filters the currently open view through a visible filter input scoped to that view (the operation the deferred `/` shortcut named); it narrows what is shown in the current view rather than opening the cross-cutting command palette.

**Why this priority**: Keyboard-first is the core principle. Without complete keyboard coverage, the app fails its primary promise.

**Independent Test**: Can be tested by opening any list view, pressing `/`, verifying a search input receives focus, typing a query, and verifying the current view filters to matching items.

**Acceptance Scenarios** (owned by this slice):

1. **(US-08.AS-08) Given** the app is open, **When** user presses `/`, **Then** a search input is focused for filtering the current view. *(Reinterpreted trigger: a visible „Filtruj…" input sits in every list view's header; focusing it and typing narrows the view. No `/` binding — FR-027 stays deferred, OOS-20.)*

> Shortcut coverage: the `/` OPERATION is implemented here as the visible view filter; the binding itself is deferred with FR-027 (see Provenance).

### Edge Cases

No edge cases (EC-NN) are assigned to this slice in product-vision.md. The 10,000+ tasks performance characterization (EC-06) is owned by slice 002; the in-palette manifestation of that performance bound is captured here by US-04.AS-07 and SC-009.

## Requirements *(mandatory)*

### Functional Requirements (slice-specific)

- **FR-027**: [DEFERRED] System MUST support all specified global shortcuts: `C` (create task), `Ctrl+K` (command palette), `/` (search), `?` (shortcuts help).
- **FR-032**: The command palette MUST provide fuzzy search across tasks (title and description), projects, labels, and actions.
  > Clarified 2026-11-08: projects include ARCHIVED ones (suffix „(zarchiwizowany)", inline „Przywróć" — US-10.AS-06); actions are the fixed catalogue listed in Clarifications. The view filter (US-08.AS-08) is a client-side narrowing of the view's already-loaded rows, not a palette query.
- **FR-033**: [DEFERRED with the shortcut system — see US-18/OOS-20] Each action in the command palette MUST display its assigned keyboard shortcut.
- **FR-034**: Selecting a result in the command palette MUST navigate to the item or execute the action.

> Scope note: FR-027 and FR-033 are [DEFERRED] in product-vision (verbatim markers above). This slice delivers FR-032 and FR-034 in full and the two FR-027 OPERATIONS (open the palette, filter the current view) through visible controls; no shortcut binding and no shortcut label ship here.

### Cross-cutting Requirements (realized in this slice)

UI-First Operability (per Constitution Principle I; verbatim from product-vision.md):
- **FR-103**: Every operation the product offers MUST have a visible affordance on the surface where it applies — directly, or via an explicit overflow/context menu ("⋯") on the item it targets. No functionality may exist only behind a keyboard shortcut.
- **FR-110**: Every empty list state MUST present a short explanatory hint plus the relevant action button; onboarding wizards and first-run modal tours remain prohibited (Principle IV).

> FR-031 (single-key suppression in text inputs), claimed by the June draft of this spec, is
> **[DORMANT]** per product-vision — no single-key shortcuts exist after slice 019 (FR-111); it
> re-activates only with the future accelerator slice (OOS-20) and is NOT realized here.

Accessibility (per Constitution Principle II):
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

This slice is evaluated against constitution v5.1.0 (Principle I: UI-First Operability). Cross-cutting principles realized here:

- **I. UI-First Operability**: the palette is reachable by sight — the topbar search box („Szukaj…", a visible control present on every view) opens it, every result and action is a visible list item activated by click or Enter, and the view filter is a visible input in each list header (FR-103). Nothing in this slice exists only behind a keyboard shortcut; the `Ctrl+K`/`/` bindings stay deferred with the shortcut system (OOS-20), and standard WCAG keyboard operability (Tab, arrows within the result list, Enter/Esc) applies to every new control.
- **II. Accessibility (WCAG 2.1 AA)**: FR-042 (focus indicator on the search input and every result), FR-043 (ARIA roles/labels for the palette overlay and its listbox/option semantics), FR-044 (contrast ≥ 4.5:1), FR-045 (no AT-binding collisions — relevant since `Ctrl+K` and `/` join the active set), FR-046 (no hover-only content), FR-047 (prefers-reduced-motion for the overlay transition). FR-031 is dormant (no single-key shortcuts exist); typing in the palette and the view filter is ordinary text entry. FR-101 governs the command palette as a dialog: opening it sets initial focus into the search input (US-04.AS-01) and traps focus while open, Esc dismisses it and returns focus to the invoking view (US-04.AS-06); the live, server-fetched result list is announced to assistive technology via a polite ARIA live region without stealing focus as the caller types.
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

Also out of scope for this slice specifically (deferred to later slices within the MVP): the shortcut bindings `Ctrl+K` and `/` and per-action shortcut labels (FR-027/FR-033 — the future accelerator slice, OOS-20); switching the visual theme via the command palette (FR-048) is owned by slice 018 (appearance-theming) — the palette becomes a surface for that action there, not here; the 30-second undo window for destructive actions (FR-040 / US-09) is owned by slice 014 (undo) and does not apply to this slice's read-only search and navigation. This slice covers the command palette, its fuzzy search across tasks/projects/labels/actions, action-shortcut display, result navigation/execution, and the `/` current-view filter only — all access-scoped to the caller (FR-065..FR-068). Defining the access model itself (membership and roles) is owned by slice 007 (project-sharing-membership); this slice consumes that model to scope results.

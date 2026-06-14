# Feature Specification: Command Palette & Search

**Feature Branch**: `009-command-palette-search`

**Created**: 2026-06-13

**Status**: Draft

**Input**: Slice 009 of the TaskFlow MVP. Source of truth: `.specify/memory/product-vision.md`. Goal: a `Ctrl+K` command palette providing fuzzy search across tasks, projects, labels, and actions — each action showing its keyboard shortcut, and selecting any result navigating to the item or executing the action — plus a `/` filter scoped to the current view. This slice completes the global-shortcut set begun in slice 001 (`Ctrl+K` and `/` join the already-shipped `C` and `?`) and lands the fifth and final primary user journey (search & command), at which point all five journeys pass end-to-end. Builds on the projects from slice 003 and the labels from slice 005, which the palette searches and navigates to.

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

MVP boundary confirmed:
- OOS-01..OOS-12 (full MVP out-of-scope confirmation)

Depends on:
- Slice 003 (project-management) — provides the projects the palette searches and navigates to (US-04.AS-05), and the command-palette/dedicated-action surface through which "Create Project" is exposed as a searchable action (US-04.AS-04)
- Slice 005 (labels) — provides the labels the palette searches over (FR-032)

Note on completed shortcut set: the FR-027 global-shortcut set is owned and completed in this slice. Its earlier members already shipped via acceptance scenarios in slice 001 — `C` (create task, US-01.AS-01) and `?` (shortcuts help, US-08.AS-07). US-04 and US-08 here exercise the final two members: `Ctrl+K` (command palette, US-04.AS-01) and `/` (search/filter the current view, US-08.AS-08), completing the set. This is framing only; no new requirement text is introduced.

Note on primary user journeys: SC-006 requires all five primary user journeys — daily capture, planning session, project work, cycle review, and search & command — to pass end-to-end automated tests. The first four shipped through slices 001–007; the fifth (search & command) is delivered by this slice, so SC-006 is reached here. This is milestone framing only; SC-006 text is copied verbatim below.

## User Scenarios & Testing *(mandatory)*

### User Story 4 - Command Palette & Search (Priority: P2)

User presses `Ctrl+K` to open a command palette that provides fuzzy search across tasks, projects, labels, and actions. They can instantly find and navigate to any item or execute any action.

**Why this priority**: The command palette is the central navigation hub for keyboard-first users. It ties all features together and enables power-user speed.

**Independent Test**: Can be tested by creating tasks, projects, and labels with known names, opening the command palette, typing partial matches, and verifying results appear with correct navigation.

> Scope note: this slice completes the global-shortcut set (FR-027) — the `Ctrl+K` (command palette) member is exercised by US-04.AS-01 below and the `/` (search the current view) member by US-08.AS-08, while the earlier `C` and `?` members shipped via slice 001. Switching the visual theme from the command palette (FR-048) is owned by slice 012 (appearance-theming); see Out of Scope.

**Acceptance Scenarios** (owned by this slice):

1. **(US-04.AS-01) Given** the app is open on any view, **When** user presses `Ctrl+K`, **Then** a command palette overlay appears with a focused search input within 16ms.
2. **(US-04.AS-02) Given** the command palette is open, **When** user types "mleko", **Then** matching tasks (by title or description) appear in the results list in under 50ms.
3. **(US-04.AS-03) Given** the command palette shows task results, **When** user selects a task and presses Enter, **Then** the app navigates to that task's location (project or Inbox).
4. **(US-04.AS-04) Given** the command palette is open, **When** user types "create project", **Then** the "Create Project" action appears in results with its keyboard shortcut displayed.
5. **(US-04.AS-05) Given** the command palette is open, **When** user types a project name, **Then** matching projects appear and selecting one navigates to that project's view.
6. **(US-04.AS-06) Given** the command palette is open, **When** user presses Esc, **Then** the palette closes and focus returns to the previous view.
7. **(US-04.AS-07) Given** the app has 10,000+ tasks, **When** user types in the command palette, **Then** results appear in under 50ms.

---

### User Story 8 - Keyboard Navigation & Shortcuts (Priority: P1)

User filters the currently open view by pressing `/`, which focuses a search input scoped to that view. This is the global `/` shortcut and the second of the two global-shortcut members completed in this slice (alongside `Ctrl+K`); it narrows what is shown in the current view rather than opening the cross-cutting command palette.

**Why this priority**: Keyboard-first is the core principle. Without complete keyboard coverage, the app fails its primary promise.

**Independent Test**: Can be tested by opening any list view, pressing `/`, verifying a search input receives focus, typing a query, and verifying the current view filters to matching items.

**Acceptance Scenarios** (owned by this slice):

1. **(US-08.AS-08) Given** the app is open, **When** user presses `/`, **Then** a search input is focused for filtering the current view.

> Shortcut coverage: `/` is implemented and its canonical scenario (US-08.AS-08) is owned here. Together with `Ctrl+K` (US-04.AS-01), it completes the global-shortcut requirement FR-027, which is owned by this slice. See Provenance.

### Edge Cases

No edge cases (EC-NN) are assigned to this slice in product-vision.md. The 10,000+ tasks performance characterization (EC-06) is owned by slice 001; the in-palette manifestation of that performance bound is captured here by US-04.AS-07 and SC-009.

## Requirements *(mandatory)*

### Functional Requirements (slice-specific)

- **FR-027**: System MUST support all specified global shortcuts: `C` (create task), `Ctrl+K` (command palette), `/` (search), `?` (shortcuts help).
- **FR-032**: The command palette MUST provide fuzzy search across tasks (title and description), projects, labels, and actions.
- **FR-033**: Each action in the command palette MUST display its assigned keyboard shortcut.
- **FR-034**: Selecting a result in the command palette MUST navigate to the item or execute the action.

> Scope note: FR-027 is owned in full here and reaches completion in this slice. Its earlier members already shipped through acceptance scenarios in slice 001 — `C` (US-01.AS-01) and `?` (US-08.AS-07). This slice's scenarios exercise the final members: `Ctrl+K` (US-04.AS-01) and `/` (US-08.AS-08), completing the set.

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
- **FR-051**: Before any data migration, the system MUST automatically create a local backup of user data. The user MUST be able to restore from this backup.

### Key Entities

This slice introduces no new entity and populates no entity attribute. The command palette reads existing data — tasks (owned by slice 001), projects (owned by slice 003), and labels (owned by slice 005) — to search and navigate over it; no Key Entity (ENT-NN) is assigned to this slice in product-vision.md.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-006**: All five primary user journeys (daily capture, planning session, project work, cycle review, search & command) pass end-to-end automated tests.
- **SC-009**: Fuzzy search across 10,000+ tasks returns results in under 50ms.

## Constitution Compliance

This slice is evaluated against constitution v1.1.0. Cross-cutting principles realized here:

- **I. Keyboard-First**: the palette is the central keyboard-first navigation hub — opened with `Ctrl+K`, driven entirely by typing and Enter/Esc, with no mouse required. This slice completes the global-shortcut grammar (FR-027) begun in slice 001, so the full global shortcut set (`C`, `Ctrl+K`, `/`, `?`) is now consistent and composable across all views, and FR-033 surfaces each action's shortcut, keeping shortcuts discoverable.
- **II. Accessibility (WCAG 2.1 AA)**: FR-042 (focus indicator on the search input and every result), FR-043 (ARIA roles/labels for the palette overlay and its listbox/option semantics), FR-044 (contrast ≥ 4.5:1), FR-045 (no AT-binding collisions — relevant since `Ctrl+K` and `/` join the active set), FR-046 (no hover-only content), FR-047 (prefers-reduced-motion for the overlay transition). FR-031 keeps single-key shortcuts from hijacking text entry inside the palette and the `/` filter input.
- **III. Instant Response**: US-04.AS-01 (palette open within 16ms) and US-04.AS-02 / US-04.AS-07 / SC-009 (results under 50ms, including at 10,000+ tasks) keep the palette within the instant-response envelope; the keypress-to-paint and virtualization performance criteria themselves are owned by slice 001.
- **V. Offline-Only, Local-First**: fuzzy search runs entirely on local data with zero network calls; the palette indexes only the user's on-device tasks, projects, and labels.
- **VI. Type Safety End-to-End**: palette result types (task / project / label / action) are discriminated and typed; the search query boundary is validated at runtime, and action descriptors carry their typed shortcut bindings for FR-033.
- **VII. Data Integrity & Resilience**: search and navigation are read-only over existing data, so no data is mutated here; FR-049 (clear message + recovery) and FR-050 (structured logging) cover any palette/search failure, and FR-051 keeps the backup hook in place.
- **VIII. Test-First**: each owned acceptance scenario above (US-04.AS-01..AS-07 and US-08.AS-08) is independently testable (Red-Green-Refactor). Because the search & command journey lands here as the fifth primary journey, SC-006 (all five journeys pass end-to-end) is reached at this slice.

No compliance gap is introduced by this slice. The palette's read-only behavior introduces no destructive action, so the Principle VII undo requirement (FR-040, owned by slice 010) does not apply here.

## Assumptions

No assumptions (ASM-NN) are assigned to this slice in product-vision.md. The single-user, desktop, offline assumptions that frame the product (ASM-01, ASM-02, ASM-08) are established by slice 001 and continue to apply.

## Out of Scope

This slice confirms the full MVP out-of-scope boundary (OOS-01..OOS-12 from product-vision.md):

- **OOS-01**: Multi-user collaboration, sharing, permissions
- **OOS-02**: Cross-device sync, cloud storage
- **OOS-03**: Mobile application, PWA
- **OOS-04**: AI features (auto-categorization, summaries, suggestions)
- **OOS-05**: External integrations (calendar, Slack, GitHub, email)
- **OOS-06**: Push notifications, reminders
- **OOS-07**: File attachments on tasks
- **OOS-08**: Subtasks (task nesting)
- **OOS-09**: Custom views, saved filters
- **OOS-10**: Custom theming beyond dark/light mode
- **OOS-11**: Automations (if X then Y)
- **OOS-12**: Plugin or extension system

Also out of scope for this slice specifically (deferred to later slices within the MVP): switching the visual theme via the command palette (FR-048) is owned by slice 012 (appearance-theming) — the palette becomes a surface for that action there, not here; the 30-second undo window for destructive actions (FR-040 / US-09) is owned by slice 010 (undo) and does not apply to this slice's read-only search and navigation. This slice covers the command palette, its fuzzy search across tasks/projects/labels/actions, action-shortcut display, result navigation/execution, and the `/` current-view filter only.

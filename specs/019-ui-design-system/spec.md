# Feature Specification: UI Design System & Full UI Operability

**Feature Branch**: `019-ui-design-system`

**Created**: 2026-08-09

**Status**: Draft

**Input**: Slice 019 of the TaskFlow MVP. Source of truth: `.specify/memory/product-vision.md`.
Goal: the application looks professional in the KreskaDev 4-palette semantic token system
(2 modes × 2 palettes: dark/light × cool blue / warm red, default `dark-cool`) and every
operation is discoverable and performable through visible UI controls alone; the existing
single-key shortcut system is removed. Decision record and design inputs:
`visual-requirements.md` (§A–§J, including the post-mockup verdict §J), `design-brief.md`
(token tables, component contracts — normative annex), `ui-test-plan.md` (UIT-001..UIT-112),
`mockup-inbox.html` (approved direction). Executes BEFORE slice 010 so the board and every
later surface build on the design system.

## Provenance

This slice realizes the following items from product-vision.md:

Slice-specific (owned IDs):
- US-18 (UI-First Operability & Visual Design System) — full: AS-01, AS-02, AS-03, AS-04, AS-05, AS-06
- FR-103 (every operation has a visible affordance; nothing exists only behind a shortcut)
- FR-104 (single set of design tokens, KreskaDev 4-palette system 1:1 with the owner's blog, default `dark-cool`, semantic tokens only, per-palette FR-044 contrast)
- FR-105 (single coherent icon set; no emoji as system iconography; avatars with initials fallback)
- FR-106 (task details in a right-side drawer without leaving the current view)
- FR-107 (visible "add task" affordance: global "+ New task" AND inline add per list)
- FR-108 (row quick actions on hover/focus + "⋯" menu with every operation; keyboard equivalents; correct hit targets/stacking)
- FR-109 (sidebar: all primary views as clickable entries with icons and counts; collapsible)
- FR-110 (every empty state: hint + action button; no onboarding wizards)
- FR-111 (removal of the single-key shortcut system; FR-030 editing keys and FR-042..047/FR-101 operability remain intact)
- SC-018 (first-time user completes the full daily workflow with visible UI only, shortcut system absent)

Partially realized (owned elsewhere or shared):
- FR-102 — the persisted-`position` reorder API already ships (PATCH `/api/tasks/{id}/position`); this slice delivers its VISIBLE reorder affordance (drag handle AND "Move up"/"Move down" in the row "⋯" menu — per clarification 2026-08-09) per the FR-102 parenthetical and FR-103. The keyboard reorder binding stays deferred (OOS-20).
- FR-032/FR-034 (command palette / search) — NOT owned here (slice 013); this slice must not regress the constraint that no operation is reachable ONLY via the palette (FR-103). The topbar ships the mockup's search field as a DISABLED placeholder (visually consistent, excluded from tab order, announced as unavailable); functional, keyboard-focusable search arrives with slice 013 — UIT-022's focusability/functionality assertions transfer there, 019 asserts only the placeholder's presence and disabled semantics.
- FR-048 (mode/palette switching UI + OS preference) — NOT owned here (slice 018 per ASM-07); this slice ships the token architecture and all four palettes; the default is `dark-cool` with no user-facing switcher yet.

New capability (allocated by product-vision amendment, 2026-08-09):
- FR-112 (task duplication — "Duplikuj" in the row "⋯" menu) — added by owner decision (2026-08-09 clarification, confirming the approved mockup and UIT-041); allocated in product-vision.md under the US-18 FR block before `/speckit-plan` ran, per the recorded precondition. Owned by this slice.

Cross-cutting (realized in this slice):
- UI accessibility — FR-042, FR-043, FR-044, FR-045, FR-046, FR-047, FR-101 (this slice makes them verifiable per palette; FR-031 is dormant per the FR-027..031 deferral note — its regression here is that no single-key bindings exist at all)
- Task editor operability — FR-030 (Ctrl+Enter save, Esc cancel — retained; these are editing keys, not shortcuts)
- Resilience — FR-049, FR-050 (every UI state in scope surfaces errors visibly with recovery; structured logging). FR-051 is not triggered: this slice performs no data migration.
- Access control — FR-065, FR-068 apply to any NEW read this slice introduces (e.g., sidebar view counts): scoped to the caller, deny-by-default at the handler layer. No authorization model changes.

MVP boundary confirmed:
- OOS-01..OOS-20 (full MVP out-of-scope confirmation; OOS-20 — the shortcut system — is actively REMOVED by this slice, not merely out of scope)

Entity touchpoints (all referenced, none modified):
- ENT-01 (Task) — rendered in the new list/row/drawer surfaces
- ENT-02 (Project) — sidebar entries, project view, emoji as optional user-chosen project icon (FR-105 carve-out)
- ENT-04 (Label) — chips and label editing surfaces (slice 006 behavior re-skinned)
- ENT-06 (User) — avatars (Google photo, initials fallback) wherever authorship/assignment/mention identity shows
- ENT-07 (ProjectMembership) — assignee pickers and role-gated UI states (viewer sees no comment input)
- ENT-08 (Comment) — thread rendered inside the drawer (slice 009 behavior re-skinned, safeMarkdown regression kept)

Depends on:
- 001–009 (all shipped slices) — this slice redesigns their surfaces and must preserve their behavior; User Story 2 (regression inventory) is the mechanism that proves preservation.

> **ID purity note**: the user stories below are a spec-local decomposition of US-18 into
> independently testable increments. They mint no new product-vision IDs; every acceptance
> scenario keeps its canonical `US-18.AS-xx` anchor where one exists, and scenarios added
> by this spec are numbered locally per story (`S<n>.<k>`). Slice-local ID namespaces:
> `INV-###` (feature inventory), `UIT-###` (test plan).

## Clarifications

### Session 2026-08-09

- Q: The mockup and UIT-022 show a search field in the topbar, but search functionality (FR-032/FR-034) belongs to slice 013 — what should slice 019 ship in the topbar? → A: A disabled placeholder: the search field renders per the mockup but is non-functional and excluded from tab order until slice 013.
- Q: The mockup and UIT-041 list "Duplikuj" (duplicate task) in the row "⋯" menu, but no product-vision FR covers duplication and the shipped app has no such feature — drop it or ship it? → A: Ship it: task duplication is added to slice 019 as a new capability; a product-vision amendment must allocate its FR ID before /speckit-plan.
- Q: S3.7 leaves the reorder affordance as "drag handle and/or move actions in the row ⋯ menu" — which form ships? → A: Both: a drag handle on hover/focus for pointer reordering AND "Move up"/"Move down" items in the ⋯ menu as the keyboard-reachable equivalent.
- Q: What context does the persistent global "+ New task" target when clicked from different views? → A: It inherits the current view's context where one is defined (Inbox → Inbox; project view → that project; Today → due today) and falls back to Inbox on all other surfaces (Upcoming, Assigned, Settings, …).
- Q: What exactly does each sidebar item count represent (new authorization-scoped read, FR-109)? → A: The number of INCOMPLETE tasks the view would list: Inbox = incomplete unprojected; Today = due today + overdue; Upcoming = due in its window; Assigned = incomplete assigned to the caller; project = incomplete tasks in that project.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Token Foundation & Four Palettes (Priority: P1)

Every screen of the application renders on a single set of semantic design tokens carried
1:1 from the owner's KreskaDev system — two modes × two palettes with `dark-cool` as the
default — so the product looks professional, dense, and consistent ("black and blue"), and
the token set stays extractable into a future shared cross-project package.

**Why this priority**: every other story in this slice, and every later slice (board,
palette, notifications, theming), builds on the tokens. Without the foundation, restyling
is per-screen forkery. Constitution v5.1.0 Principle IV makes this system the aesthetic
direction.

**Independent Test**: load the application with no stored preference and verify it renders
entirely in `dark-cool` tokens; force each of the four palette classes and verify key
surfaces re-theme without layout change; run the token contrast matrix and the per-palette
accessibility audit.

**Acceptance Scenarios**:

1. **(US-18.AS-06) Given** any main view, **When** rendered, **Then** the design system applies: the KreskaDev blog-derived token palette (default `dark-cool`), consistent typography/spacing tokens (Geist UI stack), a coherent icon set in the app chrome (emoji only as optional user-chosen project icons), and user avatars (Google photo with initials fallback) — all meeting FR-044 contrast.
2. **(S1.2) Given** a first load with no theme preference stored, **When** the page renders (including before hydration), **Then** the `dark-cool` palette applies with no flash of unstyled or mis-themed content, and native controls/scrollbars match the mode.
3. **(S1.3) Given** any of the four palettes (`dark-cool`, `dark-warm`, `light-cool`, `light-warm`) activated via the composite theme class, **When** any in-scope screen renders, **Then** all surfaces resolve to that palette's tokens — no component keeps hard-coded colors (semantic tokens only, no raw hex values outside the token source file).
4. **(S1.4) Given** the token contrast matrix, **When** it is evaluated separately in each of the four palettes, **Then** every text pairing is ≥4.5:1 and every non-text/control-boundary pairing is ≥3:1 — including hover, selected, and disabled states (known traps enumerated in Edge Cases are explicit matrix rows).
5. **(S1.5) Given** the application chrome, **When** audited, **Then** a single coherent icon set is used throughout (no emoji as system iconography; emoji only as optional user-chosen project icons), and identity is shown as avatars with initials fallback (FR-105).

---

### User Story 2 - Regression Feature Inventory (Priority: P1)

Every feature the application already has (slices 001–009) is enumerated in a testable
form — screen → Given/When/Then → expected outcome — and each entry is covered by an
automated UI test, so the redesign (and every later slice) cannot silently drop an
existing capability. **This is the key user requirement of this slice**
(`visual-requirements.md` §J2.5).

**Why this priority**: the redesign touches every surface at once; without an explicit
regression grid, feature loss would be detected only by users. The inventory is also the
acceptance instrument for every other story in this slice, so it must exist BEFORE
restyling begins.

**Independent Test**: `feature-inventory.md` exists, is derived from an audit of the
running application (not the specs alone), covers all shipped user-visible features of
slices 001–009, and CI proves 100% of entries have a passing covering test.

**Acceptance Scenarios**:

1. **(S2.1) Given** the shipped application (slices 001–009), **When** the inventory audit completes, **Then** `specs/019-ui-design-system/feature-inventory.md` lists every user-visible feature as a stable `INV-###` entry in Given/When/Then form, cross-referenced to the product-vision FR/AS it realizes.
2. **(S2.2) Given** the inventory, **When** coverage is computed, **Then** every `INV-###` entry maps to at least one automated UI test (component, E2E, or a11y level per `ui-test-plan.md` conventions); an uncovered entry fails the slice's exit criterion.
3. **(S2.3) Given** the redesigned UI, **When** the full inventory suite runs in CI, **Then** all entries pass: no feature present before this slice is absent after it — except the shortcut system, whose REMOVAL is itself an inventory entry asserting the bindings are gone (FR-111).
4. **(S2.4) Given** a later slice modifying the UI, **When** its CI runs, **Then** the inventory suite still gates the merge (Constitution VIII — the grid is a living artifact extended by subsequent slices).

---

### User Story 3 - UI-Operable Daily Workflow & Shortcut Removal (Priority: P1)

A first-time user — given no instruction and with the shortcut system removed — performs
the complete daily workflow through visible controls alone: creates a task, edits it, sets
priority/date/labels, moves it to a project, completes it, and comments on a shared task.
Navigation, task creation, row actions, and empty states are all discoverable.

**Why this priority**: this is the substance of constitution Principle I (UI-First
Operability) and the SC-018 gate. It depends on Story 1 for its visual language but is the
behavioral heart of the slice.

**Independent Test**: the SC-018 end-to-end journey passes with only pointer + standard
Tab/Enter/Esc interaction; a binding audit confirms no single-key shortcut is registered.

**Acceptance Scenarios**:

1. **(US-18.AS-01) Given** any view where tasks can exist, **When** the user looks at it, **Then** a visible "add task" affordance is present (global "+ New task" in the app bar AND an inline add within the list) and creates a task in that context. Context resolution for the global button: Inbox → Inbox; project view → that project; Today → new task due today; all other surfaces (Upcoming, Assigned, Settings, …) → Inbox.
2. **(US-18.AS-02) Given** a task row, **When** the user points at or focuses it, **Then** quick actions (complete, edit, overflow "⋯") are visible, and the "⋯" menu exposes every operation available on that task (edit, priority, due date, labels, move, assign, duplicate, comments, delete) — with keyboard-focus equivalents (FR-046).
3. **(US-18.AS-04) Given** the sidebar, **When** the user reads it, **Then** all primary views (Inbox, Today, Upcoming, Assigned, projects) are clickable entries with icons and item counts (count = incomplete tasks the view would list; Today includes overdue), and the sidebar is collapsible.
4. **(US-18.AS-05) Given** any empty list, **When** it renders, **Then** it shows a short hint plus the relevant action button (no onboarding wizards — Principle IV).
5. **(S3.5) Given** the application after this slice, **When** the user presses any former shortcut key (C/E/M/L/T/A/1–4, G-chords, `?`, Delete) while a list has focus, **Then** nothing happens — no action fires, no dead binding errors — and the shortcuts help overlay no longer exists (FR-111); standard editing keys (Ctrl+Enter save, Esc cancel — FR-030) and WCAG operability (Tab order, arrows within composite widgets) still work.
6. **(S3.6) Given** a first-time user with no instruction, **When** they attempt the full daily workflow (create → edit → set priority/date/labels → move to project → complete → comment on a shared task), **Then** every step is completable through visible controls alone (SC-018).
7. **(S3.7) Given** a task list, **When** the user wants to reorder tasks, **Then** BOTH reorder affordances exist: a drag handle revealed on row hover/focus for pointer reordering, AND "Move up"/"Move down" items in the row "⋯" menu as the keyboard-reachable non-pointer equivalent (FR-046) — each persisting the order through the existing reorder capability (FR-102 affordance).

---

### User Story 4 - Task Detail Drawer (Priority: P2)

Opening a task slides in a right-side detail panel — without leaving the current view —
where every field is directly editable inline and, for shared-project tasks, the full
comment thread (slice 009) lives: composer, @mention picker, avatars, tombstones.

**Why this priority**: the drawer is the "complete editing surface" leg of FR-103 (the
"⋯" menu covers completeness of actions; the drawer covers completeness of editing). It
depends on Stories 1 and 3 surfaces but is independently testable and shippable after them.

**Acceptance Scenarios**:

1. **(US-18.AS-03) Given** a task, **When** the user opens it, **Then** a right-side detail panel (drawer) presents all fields for direct editing plus the comment thread (shared projects).
2. **(S4.2) Given** the drawer is open, **When** the user interacts with the list behind it, **Then** the list remains interactive (non-modal); Esc closes the drawer and returns focus to the invoker — EXCEPT while a text field inside the drawer is focused, where Esc cancels the field edit (FR-030) without closing the drawer.
3. **(S4.3) Given** a task URL, **When** the user navigates to it directly (deep link) or uses browser back/forward, **Then** the drawer opens on the right task and list state is preserved.
4. **(S4.4) Given** a shared task's thread in the drawer, **When** comments render, **Then** slice-009 behavior is fully preserved: chronological order, author avatar (Google photo → initials fallback), @mention tokens highlighted, soft-deleted comments as tombstones, markdown sanitized (XSS payloads never execute), and a viewer sees the thread but no composer.

---

### User Story 5 - Full-Codebase Sweep: Remaining Screens, States & Cleanup (Priority: P2)

Every remaining UI surface in the codebase — project view, Today/Upcoming, Assigned,
Sign-in, Settings/Profile, and ANY other place that renders UI — is migrated to the
design system and made fully UI-operable; the component catalog defines complete
interaction states (loading, error, optimistic-pending, hover/focus/active/disabled/
selected, long-content overflow, narrow-window behavior); and the migration leaves no
dead code behind (user requirement §J3.6–7: the sweep covers the WHOLE codebase, not a
list of blessed screens).

**Why this priority**: completes the §J2/§J3 gap list across the whole shipped surface.
Depends on Stories 1–3 patterns; each screen is independently verifiable against the
inventory and the component contracts.

**Acceptance Scenarios**:

1. **(S5.1) Given** each in-scope screen (project view, Today, Upcoming, Assigned, Sign-in, Settings/Profile), **When** rendered, **Then** it uses only design-system components and tokens, passes the per-palette accessibility audit, and every operation it offers has a visible affordance (FR-103) — verified against its inventory entries.
2. **(S5.2) Given** a genuine network-bound load, **When** content is pending, **Then** a skeleton renders (never a spinner standing in for optimistically paintable content); optimistic writes paint immediately (Principle III/IV).
3. **(S5.3) Given** a failed operation, **When** the error surfaces, **Then** the user sees a clear message with an actionable recovery in place (FR-049) — no silent failure on any in-scope surface.
4. **(S5.4) Given** long task titles, label names, or project names, **When** they exceed available space, **Then** they truncate with ellipsis and the full value stays reachable without hover-only affordances (FR-046).
5. **(S5.5) Given** a window between 768px and 1024px, **When** the layout renders, **Then** nothing scrolls horizontally and the drawer overlays the list instead of docking beside it; the collapsible sidebar remains operable.
6. **(S5.6) Given** the codebase after the migration, **When** it is audited, **Then** NO UI surface remains on legacy styling (no component bypasses the design system; no raw hex values outside the token source file), and no dead UI code remains: unused components, styles, and hooks — including all shortcut-system remnants (FR-111) — are deleted, not orphaned.

---

### Delivery constraints (user requirements §J3; binding for plan/tasks, no new IDs)

- **Complete sweep (§J3.6)**: the migration covers EVERY place in the codebase that
  renders UI — screens, shared components, one-off widgets, error/empty/loading surfaces.
  "In-scope screens" in Story 5 is a checklist, not a boundary; anything discovered during
  the sweep joins the inventory and is migrated too.
- **Dead code removal (§J3.7)**: code made unreachable by the redesign (legacy styles,
  superseded components, unused hooks, the entire shortcut system) is deleted in the same
  slice — the codebase after 019 contains no unreferenced UI-layer artifacts.
- **Styling architecture (§J3.8)**: UI elements are built as SEPARATE reusable components,
  each with its styling grouped/co-located at the component — there is NO single monolithic
  stylesheet holding all component styles. Deliberate exception (unchanged from FR-104):
  design TOKENS stay in one source file (the extraction-to-shared-package prerequisite),
  and the only global styles beyond it are a minimal base/reset. Components consume
  semantic tokens only.

### Component catalog (binding for plan/tasks; realizes FR-104/FR-105 across stories)

Each component ships with hover/active/focus/disabled states, per-palette contrast, and
its ARIA contract (FR-043/FR-046/FR-101): buttons (primary on `accent-strong`, secondary,
destructive on `danger-strong`, icon buttons ≥32px hit area), inline quick-add input, text
inputs/textareas, date input (Polish NL parse behavior and FR-006 error presentation
unchanged), pickers (priority, project, label, assignee), checkbox (32px padded hit area,
outline on `fg-disabled`), chips (labels, assignees), context/overflow menu (full keyboard
navigation), modal dialog (full focus contract), non-modal drawer, toasts (persistent live
region; undo toast lives the full 30 s window per Constitution VII), skeletons, empty
states, avatars (AA-safe deterministic palette). `design-brief.md` is the normative annex
for tokens, dimensions, and the menu/modal/toast contracts.

### Edge Cases

- **Per-palette contrast traps** (normative rows of the S1.4 matrix, from `design-brief.md`):
  dark `accent-hover` is a lightened TEXT tint — primary-button hover backgrounds go darker
  via `accent-strong-hover`, never `accent-hover` (white label would fall to 2.4–4.0:1);
  light-mode warning uses `#8F7F36` (blog `#A89640` is 2.8:1); selection in dark uses
  translucent accent (blog `accent-soft` is invisible at 1.05–1.08:1 on dark backgrounds);
  checkbox outlines use `fg-disabled` (~4.2:1), not `border-strong` (~2:1, breaks
  WCAG 1.4.11); accent/danger text on hovered elevated surfaces switches to its hover
  variant in parallel, or dark-cool drops below 4.5:1.
- **SSR before hydration**: no palette class present renders the `dark-cool` default
  without a flash of unstyled tokens; `color-scheme` per mode keeps native controls and
  scrollbars consistent.
- **Hover-revealed actions**: the row action bar also appears on keyboard focus and is
  Tab/Shift+Tab traversable in both directions (not `display:none` while unfocused).
- **Menu at viewport edge**: the "⋯" menu on bottom rows repositions to stay inside the
  viewport (dimensions measured after showing).
- **Toast semantics**: toast text is injected into a PERSISTENT `role="status"` live
  region (toggling visibility of pre-filled content is unreliable for screen readers);
  closing a toast never lands focus on a hidden element; informational toasts auto-dismiss
  in 3–5 s; the undo toast persists the full 30 s window with an explicit close.
- **Empty vs. loading**: an empty state (hint + action button) renders only when the view
  is confirmed empty — never as a flash while data is still loading.
- **Viewer role**: a viewer on a shared task sees the comment thread but no composer, and
  no assignment/edit affordances they cannot use — the slice-007/009 authorization surface
  is restyled but behaviorally unchanged.
- **Reduced motion**: `prefers-reduced-motion: reduce` makes all transitions instant or
  <100ms (FR-047), including drawer and menu open/close.
- **Typing former shortcut characters in inputs**: characters type normally (trivially
  true once no bindings exist — asserted as an inventory regression entry).

## Requirements *(mandatory)*

### Functional Requirements (slice-specific, verbatim from product-vision.md)

- **FR-103**: Every operation the product offers MUST have a visible affordance on the surface where it applies — directly, or via an explicit overflow/context menu ("⋯") on the item it targets. No functionality may exist only behind a keyboard shortcut. *(Stories 3, 4, 5)*
- **FR-104**: The UI MUST be built on a single set of design tokens (color, typography, spacing, radii, elevation) applied consistently across all views, with token names and values taken 1:1 from the owner's KreskaDev token system (2 modes × 2 palettes; see `specs/019-ui-design-system/design-brief.md`) so the set stays extractable into a future shared package. The default theme is `dark-cool` ("black and blue", accent `#5290BD`) at Linear-inspired density (13px base list typography); components use semantic tokens only (no raw hexes), and every token combination MUST satisfy FR-044 contrast in each palette. (Slice 019 ships the token architecture with all four palettes; the mode/palette switcher and preference persistence arrive with the theming story — ASM-07/OOS-10.) *(Story 1)*
- **FR-105**: Application chrome (navigation, actions, statuses) MUST use a single coherent icon set; emoji MUST NOT serve as system iconography (they remain permitted solely as user-chosen project icons). Users MUST be represented by avatars — Google photo with an initials fallback — wherever authorship, assignment, or mention identity is shown. *(Story 1)*
- **FR-106**: Task details (all editable fields plus, for shared-project tasks, the comment thread) MUST open in a right-side detail panel (drawer) without leaving the current view. *(Story 4)*
- **FR-107**: A visible "add task" affordance MUST be present on every view where tasks can exist: a persistent global "+ New task" action in the app bar AND an inline add within each task list (Inbox, project, Today), creating the task in that view's context. *(Story 3)* — Clarified 2026-08-09: the global action inherits the current view's context (Inbox → Inbox; project → that project; Today → due today) and falls back to Inbox on surfaces without a creation context (Upcoming, Assigned, Settings).
- **FR-108**: Each task row MUST expose quick actions on hover/focus (complete, edit, overflow) and a "⋯" menu containing every operation available on that task; all row actions MUST have keyboard-focus-triggered equivalents (FR-046) and correct hit targets/stacking so pointer clicks always land (Principle I). *(Story 3)*
- **FR-109**: The sidebar MUST present all primary views (Inbox, Today, Upcoming, Assigned, projects) as clickable entries with icons and item counts, and MUST be collapsible. *(Story 3)* — Clarified 2026-08-09: each count is the number of INCOMPLETE tasks the view would list (Inbox = incomplete unprojected; Today = due today + overdue; Upcoming = due within its window; Assigned = incomplete tasks assigned to the caller; project = incomplete tasks in that project), computed by an authorization-scoped read (FR-065/FR-068).
- **FR-110**: Every empty list state MUST present a short explanatory hint plus the relevant action button; onboarding wizards and first-run modal tours remain prohibited (Principle IV). *(Stories 3, 5)*
- **FR-111**: The existing single-key shortcut system (global/list/navigation bindings and the shortcuts help overlay) MUST be removed from the application as part of realizing US-18; standard editing keys (FR-030) and WCAG operability (FR-042..047, FR-101) MUST remain intact. *(Stories 2, 3)*

### Slice-added Requirement (allocated 2026-08-09)

- **FR-112**: The row "⋯" menu MUST offer "Duplikuj", creating a new task in the same context (same list/project) copying the user-editable fields — title, description, priority, due date, labels, and assignees where the caller's membership still permits assignment — but NOT completion state and NOT comments. The duplicate appears optimistically (Principle III) and is subject to the same authorization scoping as task creation (FR-065/FR-068). *(Story 3; UIT-041)*

### Cross-cutting Requirements (realized in this slice)

Accessibility (per Constitution Principle II):
- **FR-042**: Every focusable element MUST have a visible focus indicator.
- **FR-043**: All interactive elements MUST have correct ARIA roles and labels for screen reader compatibility.
- **FR-044**: Text contrast ratio MUST be at least 4.5:1 (3:1 for large text).
- **FR-045**: Custom keyboard shortcuts MUST NOT collide with native assistive-technology bindings. (Trivially satisfied while no custom shortcuts exist; retained as a guard for slice 018+/future accelerator.)
- **FR-046**: No content may be accessible only via hover — all tooltips and popovers MUST have a keyboard/focus-triggered equivalent.
- **FR-047**: Animations MUST respect the `prefers-reduced-motion` user preference; when reduced motion is active, transitions MUST be instant or under 100ms.
- **FR-101**: Server-initiated updates and toasts MUST be conveyed to assistive technology via an appropriate ARIA live region without stealing focus, and confirmation/command-palette dialogs MUST follow the dialog focus contract (set initial focus, trap focus, dismiss on Esc, return focus to the invoker on close).

Task editor operability:
- **FR-030**: System MUST support task editor shortcuts: `Ctrl+Enter` (save), `Esc` (cancel).

Error Handling (per Constitution Principle VII):
- **FR-049**: All errors MUST be presented to the user with a clear message and an actionable recovery suggestion. No operation may fail silently.
- **FR-050**: Errors MUST be logged with structured context (severity level, operation context, and error details) for debugging purposes.

Access Control (per Constitution Principle IX — applies to any new read this slice adds, e.g., sidebar counts):
- **FR-065**: Authorization MUST be dispatched by the containing resource's visibility (not a conjunction of tiers): personal/unprojected data authorizes on ownership (`createdBy`/`ownerId`) with queries scoped to the caller; shared-project entities authorize on current `ProjectMembership` + role. Every query MUST be scoped accordingly (per-user isolation).
- **FR-068**: Authorization MUST be deny-by-default and enforced at the API/handler layer for every read and write.

### Key Entities

No entity is introduced or modified by this slice. ENT-01/02/04/06/07/08 are rendered by
the redesigned surfaces with behavior unchanged (see Entity touchpoints in Provenance).

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-018** (owned): A first-time user, given no instruction, completes the full daily workflow (create a task, edit it, set priority/date/labels, move it to a project, complete it, and comment on a shared task) using only visible UI controls — verified end-to-end with the shortcut system absent. *(Story 3)*
- **Regression gate** (this slice's exit bar, §J2.5): 100% of `feature-inventory.md` entries have a covering automated test, and the full inventory passes against the redesigned UI in CI. *(Story 2)*
- **SC-008** (referenced): Every main view passes automated accessibility audit at WCAG 2.1 AA level — extended here to run per palette (×4). *(Stories 1, 5)*
- **SC-002 / SC-003** (referenced): FCP <1 s, TTI <2.5 s; optimistic paint within 16 ms — the redesign MUST NOT regress these budgets (UIT-110/111 guard them).
- **SC-014** (referenced, deferred): live fan-out within ~1 s is not verifiable this slice — the real-time transport arrives with slice 016. UIT-045/082/112 transfer to slice 016 via the inventory's transfer notes (D11); this slice's obligation is only that redesigned surfaces do not preclude their later passage.
- **Sweep & cleanup gate** (§J3): an automated post-migration audit finds zero raw hex values outside the token source file, zero surfaces styled outside the design system, and zero unreferenced UI-layer modules (dead code, including shortcut-system remnants). *(Story 5)*

**Test plan**: `ui-test-plan.md` (UIT-001..UIT-112) enumerates the design-system tests
across levels [C]/[E]/[A]/[V]/[P]; the seed `tests/mockup.spec.ts` is rewritten against
the real application during implementation. Exit criterion: all [C]/[E]/[A] green in CI;
[V] screenshot baseline approved per palette; [P] after benchmark baseline.

## Assumptions

- The approved mockup (`mockup-inbox.html`) is the visual reference for Inbox; other
  screens follow its patterns without requiring further mockups (user decision §J1 —
  remaining gaps are specified here, not re-mocked).
- `design-brief.md` is normative for token names/values, the documented app-extension
  tokens, and the resolved light-mode mapping (elevated surfaces `#FFFFFF` + border; blog
  `bg-secondary` serves as `bg-deep` chrome background).
- Mode/palette switching UI and preference persistence are slice 018 (ASM-07/FR-048);
  this slice hard-defaults to `dark-cool` with all four palettes shipped and testable via
  the composite class on `<html>`.
- Drawer behavior at ≤1024px = overlay (resolves the open point in UIT-100); full mobile
  remains OOS-03.
- Settings surface currently amounts to profile + sign-out; it is restyled and remains the
  future home for theme (018) and cycle-length (011) controls.
- UI copy language stays as shipped (Polish user-facing strings per ASM-03 precedent);
  the design system does not introduce a localization layer.
- The `[P]` performance suite runs after a benchmark baseline is established
  (`ui-test-plan.md` exit criterion); it gates regressions, not initial merge of the
  token architecture.
- Fonts (Geist, Instrument Serif, JetBrains Mono) are self-hosted; no external font CDN
  at runtime (Constitution: CSP / no third-party runtime dependencies).

## Out of Scope (slice-level)

- The shared cross-project token package ("common") — token architecture is kept
  extraction-ready (single source file, semantic-only consumption) but no package is
  built now (YAGNI; long-term goal recorded in `design-brief.md`).
- Mode/palette switcher UI + persistence (slice 018), Kanban board (010), command palette
  (013), undo-window mechanics beyond restyling existing toasts (014), real-time
  transport changes (016), notifications center (017).
- The custom keyboard shortcut accelerator system (OOS-20) — removed here, returns (if
  ever) as a future opt-in slice.
- Full MVP boundary: OOS-01..OOS-20 confirmed.

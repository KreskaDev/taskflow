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
(token tables, component contracts), `ui-test-plan.md` (UIT-001..UIT-112),
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
- FR-102 — the persisted-`position` reorder API already ships (PATCH `/api/tasks/{id}/position`); this slice delivers its VISIBLE reorder affordance (drag handle and/or move actions in the row menu) per the FR-102 parenthetical and FR-103. The keyboard reorder binding stays deferred (OOS-20).
- FR-032/FR-034 (command palette) — NOT owned here (slice 013); this slice must not regress the constraint that no operation is reachable ONLY via the palette (FR-103).
- FR-048 (mode/palette switching UI + OS preference) — NOT owned here (slice 018 per ASM-07); this slice ships the token architecture and all four palettes; the default is `dark-cool` with no user-facing switcher yet.

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
- 001–009 (all shipped slices) — this slice redesigns their surfaces and must preserve their behavior; the regression inventory (below) is the mechanism that proves preservation.

## User Scenarios & Testing *(mandatory)*

### User Story 18 - UI-First Operability & Visual Design System (Priority: P1)

The application looks professional (a dark, dense, Linear-inspired visual system) and every
operation is discoverable and performable through visible UI controls alone — buttons, menus,
inline inputs, and panels. A first-time user needs no knowledge of keyboard shortcuts.

**Why this priority**: constitution v5.0.0 Principle I (UI-First Operability). The shipped slices
built a functional baseline operable mainly through memorized shortcuts; this story makes the UI
self-sufficient and establishes the design system every later surface (board, palette,
notifications, theming) builds on. Decision record: `specs/019-ui-design-system/visual-requirements.md`.

**Independent Test**: With shortcuts removed, a first-time user performs the complete daily
workflow (create a task, edit it, set priority/date/labels, move it to a project, complete it,
comment on a shared task) using only visible controls; a visual audit confirms the design tokens
(dark theme, accent, typography, iconography) applied on every main view.

> Scope note (aesthetic direction): per constitution v5.1.0 Principle IV, "dark
> Linear-inspired" is realized concretely as the KreskaDev 4-palette token system
> (`design-brief.md` is normative for token names, values, and the documented app
> extensions). Density and operability remain Linear-inspired; visual identity is the
> owner's blog system, carried 1:1 for future extraction into a shared package.

**Acceptance Scenarios** (owned by this slice):

1. **(US-18.AS-01) Given** any view where tasks can exist, **When** the user looks at it, **Then** a visible "add task" affordance is present (global "+ New task" in the app bar AND an inline add within the list) and creates a task in that context.
2. **(US-18.AS-02) Given** a task row, **When** the user points at or focuses it, **Then** quick actions (complete, edit, overflow "⋯") are visible, and the "⋯" menu exposes every operation available on that task (edit, priority, due date, labels, move, assign, comments, delete) — with keyboard-focus equivalents (FR-046).
3. **(US-18.AS-03) Given** a task, **When** the user opens it, **Then** a right-side detail panel (drawer) presents all fields for direct editing plus the comment thread (shared projects).
4. **(US-18.AS-04) Given** the sidebar, **When** the user reads it, **Then** all primary views (Inbox, Today, Upcoming, Assigned, projects) are clickable entries with icons and item counts, and the sidebar is collapsible.
5. **(US-18.AS-05) Given** any empty list, **When** it renders, **Then** it shows a short hint plus the relevant action button (no onboarding wizards — Principle IV).
6. **(US-18.AS-06) Given** any main view, **When** rendered, **Then** the design system applies: the KreskaDev blog-derived token palette (default `dark-cool`), consistent typography/spacing tokens (Geist UI stack), a coherent icon set in the app chrome (emoji only as optional user-chosen project icons), and user avatars (Google photo with initials fallback) — all meeting FR-044 contrast.

### Slice scope details (no new product-vision IDs; binding for plan/tasks)

The post-mockup verdict (`visual-requirements.md` §J) fixes the delivery scope of this
slice beyond the literal AS list. All items below are realizations of the owned FRs cited
in parentheses — they mint no new requirement IDs.

**1. Screens in scope** (order per §H1; every screen restyled on tokens AND made fully
UI-operable):
- Workspace/Inbox (reference implementation — matches the approved mockup)
- Project view (list mode; board arrives in slice 010 already on tokens)
- Today / Upcoming
- Assigned to me
- Task detail drawer (on every screen that lists tasks)
- Sign-in and Settings/Profile (Settings today = profile + sign-out; theme controls arrive in slice 018)

**2. Component catalog** (FR-104/FR-105; each component ships with hover/active/focus/
disabled states, per-palette contrast, and its ARIA contract per FR-043/FR-046/FR-101):
buttons (primary `accent-strong`, secondary, destructive `danger-strong`, icon buttons ≥32px
hit area), inline quick-add input, text inputs and textareas, date input (with the Polish
NL parse behavior and FR-006 error presentation unchanged), pickers (priority, project,
label, assignee), checkbox (32px padded hit area, outline on `fg-disabled`), chips
(labels, assignees), context/overflow menu, modal dialog (native `<dialog>`, full focus
contract), non-modal drawer, toasts (persistent live region; undo toast lives the full
30 s window per Constitution VII), skeletons (genuine network-bound loads only),
empty states, avatars (AA-safe deterministic palette).

**3. Interaction states** (FR-049/FR-108/Principle III/IV): loading (skeleton only where
optimistic UI cannot paint), error (visible message + recovery per FR-049), optimistic
pending, hover/focus/active/disabled/selected, long-content overflow (truncation with a
focus-reachable full-value affordance — FR-046), drag-and-drop reorder (FR-102 affordance)
with a non-pointer equivalent (move actions in the "⋯" menu), narrow-window behavior
(≥768px sensible, no horizontal scroll; drawer becomes an overlay ≤1024px).

**4. Full drawer** (FR-106): all task fields editable inline (title, description, status,
priority, due date, labels, assignees where shared), the slice-009 comment thread
(composer, @mention picker, tombstones, sanitization regression), monospace task
identifier, deep-linkable URL, non-modal contract (list stays interactive; Esc closes
except while a text field is focused; focus returns to the invoker).

**5. Shortcut-system removal** (FR-111): all single-key global/list/navigation bindings
and the shortcuts help overlay are deleted from the codebase; pressing former shortcut
keys (C/E/M/L/T/1–4/G-chords/?) in a list does nothing; FR-030 editor keys and standard
WCAG operability (Tab order, arrows within composite widgets, Esc, Enter/Space) remain.

### Regression inventory — testable feature list (key user requirement, §J2.5)

The spec's verification backbone. **Deliverable**: `specs/019-ui-design-system/feature-inventory.md`
— an enumeration of EVERY user-visible feature shipped by slices 001–009, each entry in a
directly testable form (screen → Given/When/Then → expected outcome) with a stable
slice-local ID (`INV-###`), cross-referenced to the product-vision FR/AS it realizes and
to the UIT test that covers it.

- The inventory MUST be produced before restyling begins (plan phase task), by auditing the
  shipped application surface (slices 001–009), not the specs alone.
- Every inventory entry MUST map to at least one automated UI test (component, E2E, or
  a11y level per `ui-test-plan.md` conventions); entries with no covering test fail the
  slice's exit criterion.
- The redesigned UI MUST pass the full inventory: no feature present before this slice may
  be absent after it (except the shortcut system, whose removal is itself an inventory
  entry asserting the bindings are gone — FR-111).
- The inventory is a living regression grid: subsequent slices extend it and CI keeps it
  green (Constitution VIII — failing suite blocks merge).

**Test plan**: `ui-test-plan.md` (UIT-001..UIT-112) enumerates the design-system-specific
tests across levels [C]/[E]/[A]/[V]/[P]; the seed `tests/mockup.spec.ts` is rewritten
against the real application during implementation. Exit criterion: all [C]/[E]/[A] green
in CI; [V] baseline approved per palette; [P] after benchmark baseline.

### Edge Cases

- **Per-palette contrast**: every token pair is verified separately in each of the 4
  palettes, INCLUDING hover and selected states. Known traps (design-brief, normative):
  dark `accent-hover` is a lightened TEXT tint — primary-button hover backgrounds go
  darker via `accent-strong-hover`, never `accent-hover` (white label would fall to
  2.4–4.0:1); light-mode warning uses `#8F7F36` (blog `#A89640` is 2.8:1); selection in
  dark uses translucent accent (blog `accent-soft` is invisible at 1.05–1.08:1 on dark);
  checkbox outlines use `fg-disabled` (~4.2:1), not `border-strong` (~2:1, breaks
  WCAG 1.4.11); accent/danger text on hovered elevated surfaces switches to its hover
  variant in parallel or dark-cool drops below 4.5:1.
- **SSR before hydration**: no palette class on `<html>` renders the `dark-cool` default
  without a flash of unstyled tokens; `color-scheme` is set per mode so native controls
  and scrollbars match.
- **Hover-revealed actions**: the row action bar must also appear on keyboard focus and be
  Tab/Shift+Tab traversable in both directions (not `display:none` while unfocused) —
  FR-046/FR-108.
- **Menu at viewport edge**: the "⋯" menu on bottom rows repositions to stay inside the
  viewport (dimensions measured after showing).
- **Toast semantics**: toast text is injected into a PERSISTENT `role="status"` live region
  (toggling `display` with pre-filled content is unreliable for screen readers); closing a
  toast never lands focus on a `display:none` element; informational toasts auto-dismiss
  in 3–5 s; the undo toast persists the full 30 s window with an explicit close.
- **Drawer non-modality**: Esc in a drawer text field does NOT close the drawer (it cancels
  the field edit per FR-030); Esc elsewhere closes and returns focus to the invoker; the
  underlying list stays interactive throughout.
- **Empty vs. loading**: an empty state (hint + action button) renders only when the view
  is confirmed empty; a genuine network-bound load shows a skeleton, never a spinner in
  place of optimistically paintable content.
- **Former shortcuts are inert**: typing C/E/1–4 etc. while a list has focus inserts
  nothing and triggers nothing (no hidden dead bindings, no console errors); typing them
  inside inputs types the character (trivially, since no bindings exist).
- **Viewer role**: a viewer on a shared task sees the comment thread but no composer, and
  sees no assignment/edit affordances they cannot use — disabled-with-reason or absent per
  the slice-007/009 authorization surface, restyled but behaviorally unchanged.
- **Reduced motion**: `prefers-reduced-motion: reduce` makes all transitions instant or
  <100ms (FR-047), including drawer and menu open/close.
- **Long content**: long task titles, label names, and project names truncate with
  ellipsis; the full value stays reachable without hover-only affordances (FR-046).
- **Narrow window**: down to 768px nothing scrolls horizontally; ≤1024px the drawer
  overlays the list instead of docking beside it.

## Requirements *(mandatory)*

### Functional Requirements (slice-specific, verbatim from product-vision.md)

- **FR-103**: Every operation the product offers MUST have a visible affordance on the surface where it applies — directly, or via an explicit overflow/context menu ("⋯") on the item it targets. No functionality may exist only behind a keyboard shortcut.
- **FR-104**: The UI MUST be built on a single set of design tokens (color, typography, spacing, radii, elevation) applied consistently across all views, with token names and values taken 1:1 from the owner's KreskaDev token system (2 modes × 2 palettes; see `specs/019-ui-design-system/design-brief.md`) so the set stays extractable into a future shared package. The default theme is `dark-cool` ("black and blue", accent `#5290BD`) at Linear-inspired density (13px base list typography); components use semantic tokens only (no raw hexes), and every token combination MUST satisfy FR-044 contrast in each palette. (Slice 019 ships the token architecture with all four palettes; the mode/palette switcher and preference persistence arrive with the theming story — ASM-07/OOS-10.)
- **FR-105**: Application chrome (navigation, actions, statuses) MUST use a single coherent icon set; emoji MUST NOT serve as system iconography (they remain permitted solely as user-chosen project icons). Users MUST be represented by avatars — Google photo with an initials fallback — wherever authorship, assignment, or mention identity is shown.
- **FR-106**: Task details (all editable fields plus, for shared-project tasks, the comment thread) MUST open in a right-side detail panel (drawer) without leaving the current view.
- **FR-107**: A visible "add task" affordance MUST be present on every view where tasks can exist: a persistent global "+ New task" action in the app bar AND an inline add within each task list (Inbox, project, Today), creating the task in that view's context.
- **FR-108**: Each task row MUST expose quick actions on hover/focus (complete, edit, overflow) and a "⋯" menu containing every operation available on that task; all row actions MUST have keyboard-focus-triggered equivalents (FR-046) and correct hit targets/stacking so pointer clicks always land (Principle I).
- **FR-109**: The sidebar MUST present all primary views (Inbox, Today, Upcoming, Assigned, projects) as clickable entries with icons and item counts, and MUST be collapsible.
- **FR-110**: Every empty list state MUST present a short explanatory hint plus the relevant action button; onboarding wizards and first-run modal tours remain prohibited (Principle IV).
- **FR-111**: The existing single-key shortcut system (global/list/navigation bindings and the shortcuts help overlay) MUST be removed from the application as part of realizing US-18; standard editing keys (FR-030) and WCAG operability (FR-042..047, FR-101) MUST remain intact.

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

- **SC-018** (owned): A first-time user, given no instruction, completes the full daily workflow (create a task, edit it, set priority/date/labels, move it to a project, complete it, and comment on a shared task) using only visible UI controls — verified end-to-end with the shortcut system absent.
- **Regression gate** (this slice's exit bar, per §J2.5): 100% of `feature-inventory.md` entries have a covering automated test, and the full inventory passes against the redesigned UI in CI.
- **SC-008** (referenced): Every main view passes automated accessibility audit at WCAG 2.1 AA level — extended here to run per palette (×4).
- **SC-002 / SC-003** (referenced): FCP <1 s, TTI <2.5 s; optimistic paint within 16 ms — the redesign MUST NOT regress these budgets (UIT-110/111 guard them).
- **SC-014** (referenced): live fan-out within ~1 s remains intact on redesigned surfaces (UIT-045/082/112).

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

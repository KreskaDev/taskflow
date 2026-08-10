# Research: UI Design System & Full UI Operability (slice 019)

**Date**: 2026-08-09 | **Spec**: `specs/019-ui-design-system/spec.md` | **Plan**: `plan.md`

Inputs: `design-brief.md` (normative annex), `visual-requirements.md` §A–§J,
`ui-test-plan.md` (UIT-001..112), a full survey of the current `apps/web` UI layer
(2026-08-09, working tree at `cc81cca`), and the constitution v5.1.0.

## Current-state facts the plan builds on (from the codebase survey)

- **Styling today**: one global stylesheet (`apps/web/src/app/globals.css`, 484 lines,
  BEM-ish `tf-` classes) with only 6 CSS custom properties (light-only indigo accent
  `#4f46e5`). **~95 of ~140 referenced class names have no CSS rule at all** (whole
  sidebar, app-shell grid, comments, members, project form, label chips, mention picker,
  shortcuts overlay, `tf-input`, `tf-icon-button`, and a `tf-visually-hidden` class that
  is used but undefined — a live a11y bug). All raw hexes live in the stylesheet; zero in
  TSX. No Tailwind, no CSS Modules, no CSS-in-JS.
- **No fonts loaded** (system stack; `apps/web/public/` is empty), **no icon library**
  (icons are literal emoji/Unicode glyphs in JSX), **no avatar component** (one raw
  `<img>` on Settings), **no dropdown/menu/popover/tooltip/skeleton primitives**.
- **Shortcut system**: `apps/web/src/hooks/useGlobalShortcuts.ts` (single document
  keydown listener; C/E/M/L/T/A/1–4, G-chords, `?`, Space, Delete, ↑/↓, Alt+↑/↓ reorder
  chord), call sites in `(app)/page.tsx` and `DailyView.tsx`, overlay
  `ShortcutsHelp.tsx`, contract test `tests/unit/shortcuts.test.ts`; plus
  `aria-keyshortcuts` attributes and "Press C…" empty-state copy. 5 of 10 E2E specs
  drive the UI by bare keys.
- **Detail surface today is a modal** (`TaskDetailPanel.tsx` via the `Dialog` primitive),
  mounted only on the project screen for shared projects, showing comments only — not a
  drawer, not field editing, not reachable from Inbox/Today/Upcoming/Assigned.
- **Reorder data layer is complete** (`fractional-indexing`, `lib/position.ts`,
  `PATCH /api/tasks/{id}/position`) — only the Alt+chord drives it; no visible affordance.
- **No counts endpoint**; sidebar shows no counts. **No duplicate endpoint.**
- **Virtualized Inbox list** (`@tanstack/react-virtual`, absolutely-positioned
  `translateY` rows — a documented stacking-context trap that once swallowed dialog
  clicks); `DailyView` is non-virtualized.
- **Testing**: Vitest (27 unit specs) + Playwright (10 E2E specs, self-booting
  PG/API/fake-IdP stack), **no axe**, **no visual regression**, **Playwright not run in
  CI** (web-quality runs lint/typecheck/vitest only).
- **Not yet shipped anywhere** (relevant to over-eager test-plan rows): SignalR/live
  fan-out (slice 016), undo window (014), command palette (013), board (010). UI copy is
  mixed Polish/English.

---

## D1. Token architecture: single `tokens.css` with composite palette classes

- **Decision**: All design tokens live in ONE new file
  `apps/web/src/app/tokens.css` as CSS custom properties: `:root` carries the
  `dark-cool` values (SSR default), and `html.dark-cool`, `html.dark-warm`,
  `html.light-cool`, `html.light-warm` each (re)declare the full set. Token names carried
  1:1 from the blog (`--color-bg-primary`, `--color-accent`, …) plus the documented
  TaskFlow extensions (`--color-bg-deep`, `--color-accent-strong[-hover]`,
  `--color-danger-strong[-hover]`, `--color-warning`, `--color-selection`,
  `--color-fg-disabled`, `--color-bg-hover/active`, `--avatar-*`) and app-scale tokens
  (spacing 4px rhythm, radii 6/8px, z-index scale, type scale, motion durations).
  `color-scheme: dark`/`light` is set per mode class (and on `:root` for the default).
  The root layout server-renders `<html className="dark-cool">` — hard default, no
  switcher (slice 018), no FOUC because the class is in the SSR payload and `:root`
  duplicates the default set.
- **Rationale**: FR-104 + §J3.8 demand a single extractable token source consumed only
  semantically; CSS custom properties on a composite class are exactly the blog's
  mechanism (ADR-039/041), cost zero dependencies, work with SSR before hydration
  (S1.2/UIT-001), and make the four palettes testable by toggling one class (S1.3/UIT-003).
- **Alternatives considered**: Tailwind `@theme` (blog-compatible but adds a build
  dependency and rewires every component's styling idiom — rejected, YAGNI);
  JS-object tokens with a generator (extra indirection, breaks "extraction = move one
  file" — rejected); keeping tokens in `globals.css` (conflates tokens with base styles;
  the token file must stay extraction-pure — rejected).

## D2. Component styling: CSS Modules co-located per component

- **Decision**: Every design-system component ships as its own `.tsx` with a co-located
  `ComponentName.module.css` consuming only semantic tokens. `globals.css` shrinks to a
  minimal base/reset (box-sizing, body defaults, `:focus-visible` ring, `::selection`,
  `.sr-only`, reduced-motion guard) + `@import` of `tokens.css`. The existing 484-line
  monolith is dismantled; the `tf-` global-class system is deleted with it.
- **Rationale**: §J3.8 explicitly forbids one stylesheet holding all component styles and
  requires co-location. CSS Modules are built into Next.js (zero new deps), keep CSP
  `style-src 'self'` intact (no runtime CSS-in-JS), are statically analyzable for the
  dead-code audit, and the hex-audit stays trivial (grep all `.css` except `tokens.css`).
- **Alternatives considered**: CSS-in-JS (runtime cost, CSP `unsafe-inline` pressure,
  React 19 streaming caveats — rejected); Tailwind utilities (styling co-located but
  token consumption via generated utilities adds a compiler and fights the 1:1 token-name
  requirement — rejected); keeping global BEM but split into per-component files
  (satisfies the letter, loses scoping guarantees; Modules give the same effort with real
  isolation — rejected).

## D3. Fonts: `next/font/google` self-hosted at build time

- **Decision**: Load **Geist** (UI sans), **Instrument Serif** (brand wordmark only) and
  **JetBrains Mono** (task identifiers, inline code) via `next/font/google` in the root
  layout, exposed as CSS variables (`--font-sans`, `--font-display`, `--font-mono`)
  referenced from `tokens.css`. `next/font` downloads at build time and self-hosts the
  woff2 with the app — **no runtime CDN request**, satisfying the constitution/CSP
  (`font-src 'self'` already configured in `next.config.ts`).
- **Rationale**: matches the blog's stack (design-brief), zero font files to vendor
  manually, automatic subsetting + `size-adjust` fallback metrics (guards FCP budget
  SC-002).
- **Alternatives considered**: vendoring woff2 in `public/` + `@font-face`
  (`next/font/local`) — equivalent CSP-wise but manual subsetting/maintenance; chosen
  fallback if a build-time Google fetch is unacceptable in CI (documented, not needed
  now). Keeping the system stack — rejected, FR-104 mandates the Geist stack.

## D4. Icons: `lucide-react`

- **Decision**: Add `lucide-react`; all app-chrome iconography uses it (14–16px dense
  rows, 18–20px headers, consistent 1.5–2 stroke). Emoji remain ONLY as user-chosen
  project icons (`ProjectForm` preset picker and sidebar `iconGlyph()` mapping stay,
  restyled). Every icon-only button gets `aria-label` and a ≥32px hit area; decorative
  icons get `aria-hidden`.
- **Rationale**: FR-105 + constitution IV name Lucide; `lucide-react` is tree-shakeable
  (only imported icons are bundled), TS-typed, no runtime fetch.
- **Alternatives considered**: Heroicons/Phosphor (fine libraries, but the constitution
  pins Lucide — rejected); inline SVG sprite hand-rolled (icon-set drift risk, no gain —
  rejected).

## D5. Shortcut removal: delete the hook wholesale; rebuild list keys as composite-widget operability

- **Decision**: Delete `useGlobalShortcuts.ts`, `ShortcutsHelp.tsx`, both call sites'
  handler wiring, `aria-keyshortcuts` attributes, `tf-kbd`/`tf-shortcuts` classes, the
  "Press C" empty-state copy, and `tests/unit/shortcuts.test.ts`. **This includes the
  `Alt+↑/↓` reorder chord** — the spec (FR-102 note) keeps the keyboard reorder *binding*
  deferred with OOS-20; its keyboard-reachable replacement is the "Move up/down" menu
  items. The list-navigation keys that today live inside the same hook (↑/↓ selection,
  Space toggle, Enter) are NOT shortcuts but composite-widget operability (WCAG,
  FR-042..047): they are re-implemented **inside** the listbox component (roving
  tabindex / `aria-activedescendant`, per UIT-093), not as document-level bindings.
  Editing keys stay untouched where they already live component-locally (Ctrl+Enter
  save, Esc cancel — FR-030; survey confirmed none of them route through the global hook).
  S3.5's binding audit becomes an inventory entry: pressing C/E/M/L/T/A/1–4, G-chords,
  `?` with a focused list does nothing.
- **Rationale**: FR-111 removes the *system* (global/list/navigation bindings + help
  overlay); a single deleted hook file is exactly that system. Keeping arrow-key list
  navigation inside the widget preserves UIT-090..093 without any global listener.
- **Alternatives considered**: keep the hook but empty (dead code — violates §J3.7);
  keep Alt+chords as "not single-key" (spec explicitly defers the keyboard reorder
  binding to OOS-20 — rejected).

## D6. Sidebar counts: one new aggregated endpoint `GET /api/views/counts`

- **Decision**: Add a single authorization-scoped read returning all sidebar counts in
  one response: `{ inbox, today, upcoming, assigned, projects: [{ projectId, count }] }`
  (semantics per the 2026-08-09 clarification: incomplete tasks the view would list;
  Today includes overdue; Europe/Warsaw boundaries per Principle X). Implemented as a
  Wolverine query handler with deny-by-default scoping (ownership for inbox/today/
  upcoming/assigned slices; membership for project counts), allow + deny integration
  tests, exposed in OpenAPI, consumed via a `useViewCounts()` query invalidated by the
  existing task-mutation `onSettled` invalidations (query key joins the view-key family).
- **Rationale**: the survey confirmed no counts source exists and client-side derivation
  would need one `GET /api/projects/{id}/tasks` per project (N requests on every shell
  load — fails the perf posture and duplicates count logic client-side). One endpoint
  keeps the count definition server-authoritative next to the existing view queries and
  gives FR-065/FR-068 a single testable surface.
- **Alternatives considered**: client-side counting from already-fetched view arrays
  (N+1 project fetches, counts wrong unless every view is loaded — rejected); per-view
  `?countOnly` params on five endpoints (five handlers to authorize and test — rejected);
  denormalized counter columns (write-side complexity for a read this cheap — rejected).

## D7. Task duplication: new endpoint `POST /api/tasks/{id}/duplicate`

- **Decision**: Add a Wolverine command endpoint. Request body carries a client-generated
  `newTaskId` (same idempotency idiom as the existing `PUT /api/tasks/{id}` create),
  enabling optimistic insertion with a known id. Server copies title, description,
  priority, due date, labels, and assignees **filtered to current project membership**;
  excludes completion state/status timestamps and comments; the duplicate lands in the
  same context (same project or Inbox) with `position` directly after the source row
  (fractional index between source and its successor). Authorization = same scoping as
  task creation in that context (FR-065/FR-068), allow + deny tests. Row "⋯" menu item
  "Duplikuj" (FR-112, UIT-041) paints optimistically per Principle III.
- **Rationale**: composing duplication client-side (one PUT + five PATCHes) is
  non-atomic, re-implements membership filtering in the client, and produces N requests
  for one user action; a single command is atomic, testable (deny: viewer/non-member),
  and matches the Wolverine handler pattern already in the codebase.
- **Alternatives considered**: client-side composition over existing endpoints (above —
  rejected); server-generated id (breaks optimistic-paint-with-known-id idiom used by
  quick-add — rejected); position at list top per FR-021 newest-first seeding (duplicate
  visually jumps away from its source; adjacency communicates the result — rejected).

## D8. Drawer & deep link: non-modal drawer driven by a `?task=<id>` query param

- **Decision**: Replace the modal `TaskDetailPanel` with a right-side **non-modal**
  drawer component (420–480px; ≤1024px overlay per assumption) rendered by the app
  shell on every task-listing view. Open state is URL-driven: `?task=<taskId>` on the
  current route (Inbox `/`, `/today`, `/upcoming`, `/assigned`, `/projects/[id]`), set
  via `router.push` — giving deep links and back/forward for free while preserving list
  state (S4.3/UIT-054). Esc closes and restores focus to the invoker EXCEPT while a text
  field inside is focused (field-level Esc cancels the edit first — FR-030); no focus
  trap (list stays interactive); full dialog contract remains on true modals only.
  The drawer hosts all field editors (existing pickers re-skinned) + the slice-009
  comment thread for shared-project tasks.
- **Rationale**: FR-106/US-18.AS-03 + S4.2..S4.4. A query param works identically across
  all five listing routes with one implementation, survives refresh, and avoids the
  parallel-route/intercepting-route machinery that would fork each route's layout.
- **Alternatives considered**: Next parallel + intercepting routes (`@drawer` slot) —
  idiomatic but multiplies route files per view and complicates the non-modal focus
  model (rejected for scope); dedicated `/tasks/[id]` page as fallback (violates
  "without leaving the current view" — rejected); local state only (no deep link —
  fails S4.3 — rejected).

## D9. Reorder affordance: `@dnd-kit` drag handle + menu "Move up/down"

- **Decision**: Add `@dnd-kit/core` + `@dnd-kit/sortable`. A drag handle appears in the
  row's hover/focus action zone (pointer path); "Przenieś wyżej/niżej" (Move up/Move
  down) items in the row "⋯" menu are the keyboard-reachable equivalent (clarified
  2026-08-09: BOTH ship). Both paths compute the new fractional index via the existing
  `lib/position.ts` `between()` and the shipped `PATCH /api/tasks/{id}/position` — no new
  ranking logic. dnd-kit's keyboard sensor is NOT relied on for the accessibility story
  (the menu items are); drag is pointer-first. The known virtualization stacking-context
  trap (absolutely-positioned `translateY` rows once painted over the dialog overlay) is
  handled by the token z-index scale + `DragOverlay` rendering in a portal, with an
  explicit regression test.
- **Rationale**: S3.7/FR-102 affordance; dnd-kit is the maintained, dependency-light
  standard for sortable lists and coexists with `@tanstack/react-virtual`; native HTML5
  DnD has poor ghost-image/scroll behavior in virtualized lists.
- **Alternatives considered**: native HTML5 DnD (no auto-scroll, ugly drag image,
  brittle in virtualized lists — rejected); `react-beautiful-dnd` (unmaintained —
  rejected); menu-only reorder (violates the clarified "both" decision — rejected).

## D10. Regression inventory: `feature-inventory.md` + `[INV-###]` test-title tags + CI coverage gate

- **Decision**: `feature-inventory.md` enumerates every user-visible shipped feature
  (slices 001–009) as `INV-###` rows: screen → Given/When/Then → expected outcome →
  realized FR/AS → covering test level [C]/[E]/[A]. Derived from the running app + the
  2026-08-09 codebase survey (which enumerated every route, component, empty/error state
  and binding), then verified against the app. Covering tests carry `[INV-###]` tags in
  their titles; a coverage script (`apps/web/tests/inventory-coverage.test.ts`) parses
  the inventory, greps unit+E2E test sources for each tag, and FAILS on any uncovered
  entry — running in `web-quality` CI so the gate lives on every future merge (S2.4,
  Constitution VIII). The shortcut REMOVAL is itself inventory entries (bindings gone;
  typing former shortcut chars in inputs works).
- **Rationale**: S2.1–S2.4 demand a machine-checkable 100% mapping; title tags keep the
  mapping in one place (the test itself) and survive refactors better than a separate
  mapping table.
- **Alternatives considered**: separate mapping YAML (drifts from tests — rejected);
  coverage-by-convention without a gate (exactly the silent-loss failure mode the story
  exists to prevent — rejected).

## D11. Inventory reality-check: rows for unshipped capabilities transfer out

- **Decision**: The inventory and this slice's test suite cover **shipped** behavior
  only. UIT rows asserting capabilities that do not exist yet transfer to their owning
  slices, mirroring the spec's search-placeholder precedent (UIT-022 → slice 013):
  live fan-out assertions (UIT-045 fan-out clause, UIT-082 fan-out clause, UIT-112) →
  slice 016 (SignalR is not integrated on either side today); the 30 s undo window
  (UIT-082) → slice 014 (the Toast component ships a persistent-with-close variant so
  014 restyles nothing); virtualized 10k perf row (UIT-035) stays in the post-baseline
  [P] suite. UIT-024's "counts match API" and UIT-041's "Duplikuj" become NEW capability
  tests here (FR-109/FR-112). The topbar search field ships as the disabled placeholder
  per the clarification (rendered, excluded from tab order, announced unavailable).
- **Rationale**: S2.3 gates on features "present before this slice"; asserting
  slice-016/014 behavior now would gate 019 on features the codebase has never had.
  The spec's own SC section marks SC-014 as "referenced", and the ui-test-plan predates
  the survey finding that SignalR/undo are unshipped.
- **Alternatives considered**: implementing stub fan-out/undo to satisfy the rows
  (scope creep into slices 014/016 — rejected); silently dropping the rows (they must be
  explicitly transferred with a note in `feature-inventory.md` — rejected).

## D12. Accessibility & contrast verification: axe per palette + token contrast matrix as unit test

- **Decision**: Add `@axe-core/playwright`; an `[A]` spec walks each screen × 4 palettes
  (class swap on `<html>`) asserting zero AA violations (UIT-010). The S1.4 contrast
  matrix is a **Vitest tabular test** that parses `tokens.css` and computes WCAG ratios
  for every normative pairing — including the edge-case trap rows (accent-strong-hover
  under white label text, light warning `#8F7F36`, dark selection translucency composited
  over `bg-primary`, checkbox outline `fg-disabled`, hover-elevated accent/danger text)
  — per palette (text ≥4.5:1, non-text ≥3:1). Keyboard operability tests (UIT-090..093)
  run in Playwright.
- **Rationale**: SC-008 ×4 palettes; computing ratios from the single token source keeps
  the matrix in lockstep with the file the audit is about (a designer edit fails the
  matrix before it ships).
- **Alternatives considered**: manual contrast spreadsheet (drifts — rejected); axe-only
  (axe cannot see token *pairings* that aren't rendered on the audited page state, e.g.
  hover states — the matrix covers those — rejected as sole mechanism).

## D13. Visual regression: Playwright `toHaveScreenshot` per palette

- **Decision**: Use built-in Playwright screenshot assertions for the [V] suite: each
  in-scope screen × 4 palettes (+ 1440/1024/768px for UIT-100), baselines committed to
  the repo, reviewed like code. Animations disabled via `reducedMotion: 'reduce'` +
  `animations: 'disabled'` for determinism. Baseline approval per palette is the [V]
  exit criterion; the suite runs in the new CI E2E job but only gates after the baseline
  is approved.
- **Rationale**: zero new services (Percy/Chromatic are external SaaS — constitution V
  posture), deterministic in the existing chromium-only config.
- **Alternatives considered**: Percy/Chromatic (external service — rejected); Storybook +
  Loki (introduces Storybook for testing only — rejected, YAGNI).

## D14. Dead-code & sweep audit: knip + hex-grep + CI E2E job

- **Decision**: Three automated gates wired into CI:
  (1) **knip** (devDep) for unreferenced files/exports/dependencies in `apps/web` —
  zero findings after the migration (§J3.7; catches orphaned components, hooks, the
  deleted shortcut system's remnants);
  (2) **hex audit** as a unit test: grep `#[0-9A-Fa-f]{3,8}` across `src/**` and all
  CSS except `tokens.css` — zero matches (UIT-004, S5.6; also enforces "no component
  bypasses tokens");
  (3) **CI E2E job**: Playwright currently never runs in CI — a new `web-e2e` job
  (self-booting PG :55432 + API :4311 + fake IdP, as locally) is REQUIRED for the
  S2.3/S2.4 inventory gate and the [E]/[A]/[V] exit criteria to be a merge gate at all.
- **Rationale**: S5.6 and the §J3 sweep gate demand "zero unreferenced UI-layer
  modules" as an *automated* post-migration audit; the survey shows the current CI gap
  would make every E2E-level guarantee advisory.
- **Alternatives considered**: ts-prune + depcheck (two tools ≈ one knip — rejected);
  manual sweep checklist only (not automatable, S5.6 demands an audit — rejected).

## D15. UI copy: normalize to Polish during the sweep

- **Decision**: All user-facing strings migrate to Polish as surfaces are restyled
  (today's copy is mixed Polish/English; the approved mockup and spec examples are
  Polish: "Duplikuj", "+ Nowy task", "Przenieś wyżej/niżej"). No localization layer
  (spec assumption); copy lives inline in components.
- **Rationale**: spec assumption "UI copy language stays as shipped (Polish per ASM-03
  precedent)" — the *shipped precedent* (mockup-approved direction, Polish NL dates,
  newest strings) is Polish; leaving mixed languages fails the "professional" bar the
  slice exists to hit.
- **Alternatives considered**: i18n framework (explicitly out of spec — rejected);
  keep mixed copy (contradicts the design-system intent — rejected).

## New dependencies summary (all dev-time or bundled client libs; no runtime services)

| Package | Purpose | Decision |
|---|---|---|
| `lucide-react` | icon set (FR-105) | D4 |
| `@dnd-kit/core` + `@dnd-kit/sortable` | pointer reorder (S3.7) | D9 |
| `@axe-core/playwright` (dev) | per-palette AA audit (SC-008) | D12 |
| `knip` (dev) | dead-code audit (§J3.7) | D14 |
| `next/font` (built-in) + Google-sourced Geist/Instrument Serif/JetBrains Mono | typography (FR-104) | D3 |

No NEEDS CLARIFICATION remains: all Technical Context unknowns are resolved above.

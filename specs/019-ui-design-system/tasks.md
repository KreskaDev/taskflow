# Tasks: UI Design System & Full UI Operability

**Input**: Design documents from `/specs/019-ui-design-system/`

**Prerequisites**: plan.md, spec.md, research.md (D1–D15), data-model.md, contracts/
(view-counts.md, task-duplicate.md, ui-theme.md), design-brief.md (normative annex),
ui-test-plan.md (UIT-001..112), visual-requirements.md §A–§J, mockup-inbox.html

**Tests**: INCLUDED — the spec mandates test-first (Constitution VIII), a 100%-covered
regression inventory as the exit bar (S2.2/S2.3), allow+deny integration tests for both
new endpoints (Constitution IX), and the [C]/[E]/[A]/[V] suites from ui-test-plan.md.

**Organization**: Tasks are grouped by user story. Execution-order caveat: US2's
inventory audit (T019) runs against the PRE-redesign app and must complete before any
restyling task (spec: the inventory "must exist BEFORE restyling begins").

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies on incomplete tasks)
- **[Story]**: US1–US5 per spec.md; Setup/Foundational/Polish tasks carry no story label
- Every task names exact file paths

## Path Conventions

Web monorepo per plan.md: `apps/web` (Next.js 15 App Router) + `apps/api` (.NET 9).
No new projects; no EF migration this slice.

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: New dependencies and dev tooling (research.md dependency summary)

- [X] T001 Add runtime deps to apps/web/package.json via `pnpm --dir apps/web add lucide-react @dnd-kit/core @dnd-kit/sortable` (D4, D9)
- [X] T002 Add dev deps via `pnpm --dir apps/web add -D @axe-core/playwright knip` (D12, D14 — sequential with T001/T003: all three mutate apps/web/package.json)
- [X] T003 Configure knip for the web UI layer in apps/web/knip.json (entry: app routes, tests; ignore generated apps/web/src/lib/api/generated/) and add `"audit:dead": "knip"` script to apps/web/package.json (§J3.7; CI wiring deferred to T065 so the gate lands once it can pass)

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Token architecture + design-system component catalog that every restyling
story consumes. All additive or component-scoped — the app keeps working throughout.

**⚠️ CRITICAL**: T004–T006 block all catalog components; the catalog blocks US1/US3/US4/US5
implementation. Do NOT start ANY Phase 2 task before the US2 inventory audit (T019) has
captured the pre-redesign app — that includes T004–T006: the fonts/default-theme/base-reset
switch is where the redesign visibly begins. Recommended execution order: Phase 1 → T019 →
Phase 2. Test-first (Constitution VIII): every catalog task T007–T018 authors its failing [C]
component spec FIRST, then implements the component to green (Red-Green-Refactor).

- [X] T004 Create apps/web/src/app/tokens.css — the single token source (contracts/ui-theme.md, D1, design-brief.md): `:root` carries the full `dark-cool` set (SSR fallback), `html.dark-cool` / `html.dark-warm` / `html.light-cool` / `html.light-warm` each redeclare the complete set with names 1:1 from the blog (`--color-bg-primary`, `--color-accent`, …) plus app extensions (`--color-bg-deep`, `--color-accent-strong[-hover]`, `--color-danger-strong[-hover]`, `--color-warning`, `--color-selection`, `--color-fg-disabled`, `--color-bg-hover/active`, `--avatar-*`) and scale tokens (4px spacing rhythm, radii 6/8px, z-index 10/20/40/60/100/1000, type scale 13px base, motion 100–150ms); `color-scheme: dark|light` per mode class and on `:root`; light-mode traps honored (warning `#8F7F36`, elevated `#FFFFFF` + border)
- [X] T005 Rewrite apps/web/src/app/globals.css to `@import './tokens.css'` + minimal base/reset only (box-sizing, body defaults on semantic tokens, `:focus-visible` ring, `::selection` on `--color-selection`, `.sr-only`, `prefers-reduced-motion` guard — D2); migrate all existing usages of the used-but-undefined `tf-visually-hidden` class to `.sr-only` (live a11y bug from the research survey); temporarily retain still-referenced legacy `tf-` rules below a `/* LEGACY — delete in T064 */` marker until each surface migrates — this marker IS the hex-audit justification comment recognized per contracts/ui-theme.md (the legacy block's raw hexes are exempt until T064 deletes both the block and the exemption)
- [X] T006 Update apps/web/src/app/layout.tsx: load Geist, Instrument Serif, JetBrains Mono via `next/font/google` exposing `--font-sans`/`--font-display`/`--font-mono`, and server-render `<html className="dark-cool">` (D1, D3, S1.2 — no FOUC, `font-src 'self'` intact)
- [X] T007 [P] Restyle Button in apps/web/src/components/ui/Button.tsx + NEW Button.module.css: primary on `accent-strong` (hover via `accent-strong-hover`, NEVER `accent-hover` — design-brief trap), secondary, destructive on `danger-strong`; hover/active/focus/disabled states, semantic tokens only; [C] spec apps/web/tests/unit/components/Button.test.tsx (variants + states + disabled semantics)
- [X] T008 [P] Create IconButton in apps/web/src/components/ui/IconButton.tsx + IconButton.module.css: ≥32px hit area, required `aria-label`, Lucide-sized 14–16px glyph slot; [C] spec apps/web/tests/unit/components/IconButton.test.tsx (hit area + aria-label enforcement)
- [X] T009 [P] Create Input, Textarea, and DateInput in apps/web/src/components/ui/ (Input.tsx + Input.module.css, Textarea.tsx + Textarea.module.css, DateInput.tsx + DateInput.module.css): full state set incl. error presentation (FR-006 pattern unchanged), placeholder contrast per matrix; DateInput is the catalog date input from plan.md's target tree — it extracts/wraps the existing Polish NL parse presentation (RescheduleInput behavior + FR-006 error presentation unchanged) for T051/T057 to consume; [C] specs apps/web/tests/unit/components/Input.test.tsx + Textarea.test.tsx + DateInput.test.tsx (states, error presentation, NL parse passthrough)
- [X] T010 [P] Create Checkbox in apps/web/src/components/ui/Checkbox.tsx + Checkbox.module.css: 32px padded hit area, outline on `--color-fg-disabled` (NOT `border-strong` — WCAG 1.4.11 trap), checked/indeterminate/disabled states; [C] spec apps/web/tests/unit/components/Checkbox.test.tsx (hit area, keyboard toggle, states)
- [X] T011 [P] Create Chip in apps/web/src/components/ui/Chip.tsx + Chip.module.css: label and assignee variants, removable affordance with keyboard equivalent (FR-046), truncation behavior; [C] spec apps/web/tests/unit/components/Chip.test.tsx (keyboard removal, truncation)
- [X] T012 [P] Create Menu (context/overflow "⋯") in apps/web/src/components/ui/Menu.tsx + Menu.module.css: `role="menu"`, arrow/Home/End navigation, `aria-expanded` on trigger, Esc + focus return, viewport-edge repositioning measured after show (spec Edge Cases), z-index from token scale (60), destructive-item styling; [C] spec apps/web/tests/unit/components/Menu.test.tsx (full keyboard-navigation + ARIA contract, Esc/focus return)
- [X] T013 [P] Restyle Dialog in apps/web/src/components/ui/Dialog.tsx + NEW Dialog.module.css preserving the full dialog focus contract (initial focus, trap, Esc, return to invoker — FR-101); z-index token 100 (DeleteAccountDialog itself migrates in T060); [C] spec apps/web/tests/unit/components/Dialog.test.tsx (focus contract)
- [X] T014 [P] Restyle Toast in apps/web/src/components/ui/Toast.tsx + NEW Toast.module.css with apps/web/src/components/ui/LiveRegion.tsx as a PERSISTENT `role="status"` region (text injected, not visibility-toggled — spec Edge Cases): informational variant auto-dismiss 4 s (spec tolerance 3–5 s), undo-capable variant persisting the full 30 s window with explicit close (D11, Constitution VII); closing never lands focus on a hidden element; z-index token 1000; [C] spec apps/web/tests/unit/components/Toast.test.tsx (persistent-region semantics, variant timing, close focus)
- [X] T015 [P] Create Skeleton in apps/web/src/components/ui/Skeleton.tsx + Skeleton.module.css: row/block variants, reduced-motion-safe shimmer (S5.2); [C] spec apps/web/tests/unit/components/Skeleton.test.tsx (aria-hidden, reduced-motion)
- [X] T016 [P] Create EmptyState in apps/web/src/components/ui/EmptyState.tsx + EmptyState.module.css: short hint + action button slot, no wizard patterns (FR-110); [C] spec apps/web/tests/unit/components/EmptyState.test.tsx (hint + action rendering)
- [X] T017 [P] Create Avatar in apps/web/src/components/ui/Avatar.tsx + Avatar.module.css: Google photo with initials fallback, AA-safe deterministic background palette from `--avatar-*` tokens, sizes for rows/comments/pickers (FR-105); [C] spec apps/web/tests/unit/components/Avatar.test.tsx (initials fallback, deterministic palette)
- [X] T018 [P] Create focus-triggered Tooltip equivalent in apps/web/src/components/ui/Tooltip.tsx + Tooltip.module.css (no hover-only content — FR-046), used for truncated-value reveal (S5.4); [C] spec apps/web/tests/unit/components/Tooltip.test.tsx (focus-triggered reveal, no hover-only path)

**Checkpoint**: Tokens + catalog primitives exist; contrast matrix (T023) can compute
against tokens.css; restyling stories can consume the catalog

---

## Phase 3: User Story 2 — Regression Feature Inventory (Priority: P1)

**Goal**: Every shipped feature of slices 001–009 enumerated as testable `INV-###`
entries with a CI-enforced 100% coverage gate — the acceptance instrument for the whole
slice, authored BEFORE restyling.

**Independent Test**: `feature-inventory.md` exists, derived from the running app; the
coverage test fails on any `INV-###` without an `[INV-###]`-tagged covering test; the
gate runs in CI (S2.1–S2.4).

**⚠️ Sequencing**: T019 audits the PRE-redesign app — complete it before ALL of
Phase 2 (T004–T018) and everything after. T020–T022 may run in parallel with
Phase 2. The gate goes green only at the end of US5 (T067); until then it is the
red-list driving the work.

- [X] T019 [US2] Audit the RUNNING pre-redesign app (all 7 routes, every component, empty/error/loading state, binding, viewer-role gap — cross-checked against the 2026-08-09 survey in research.md) and author specs/019-ui-design-system/feature-inventory.md: stable `INV-###` rows as screen → Given/When/Then → expected outcome → realized FR/AS → covering-test level [C]/[E]/[A]; include shortcut-REMOVAL entries (bindings dead, typing former shortcut chars in inputs works — S3.5, FR-111) and D11 transfer notes (fan-out UIT-045/082/112 → slice 016, undo window UIT-082 → 014, UIT-035 → post-baseline [P], search focusability UIT-022 → 013)
- [X] T020 [US2] Create the coverage gate in apps/web/tests/unit/inventory-coverage.test.ts: parse feature-inventory.md, scan apps/web/tests/**(unit + e2e) sources for `[INV-###]` title tags, FAIL on any uncovered entry (D10) — runs under the existing `pnpm --dir apps/web test` so `web-quality` CI carries it forever (S2.4)
- [X] T021 [P] [US2] Baseline-tag existing tests: add `[INV-###]` tags to the current Vitest and Playwright spec titles wherever a shipped behavior they already cover maps to an inventory row (apps/web/tests/unit/*, apps/web/tests/e2e/*), recording the pre-redesign coverage floor
- [X] T022 [US2] Add the `web-e2e` job to .github/workflows/ci.yml: `dotnet build apps/api/src/TaskFlow.Api -c Debug` then `pnpm --dir apps/web e2e` (self-boots PG :55432 + API :4311 + fake IdP :4321, as locally) — required for the S2.3/S2.4 inventory gate and all [E]/[A]/[V] exit criteria to gate merges at all (D14; Playwright never runs in CI today)

**Checkpoint**: Inventory authored + coverage gate live (red where redesign work remains);
restyling may begin

---

## Phase 4: User Story 1 — Token Foundation & Four Palettes (Priority: P1)

**Goal**: The app renders on the KreskaDev 4-palette semantic token system (default
`dark-cool`), verified by a per-palette contrast matrix, hex audit, palette-swap tests,
and coherent Lucide/Avatar identity.

**Independent Test**: load with no stored preference → fully `dark-cool`, no FOUC; force
each of the 4 palette classes → surfaces re-theme with no layout change; contrast matrix
and per-palette axe audit pass (matrix green now; the hex audit is green modulo the
marked LEGACY exemption until T064; axe across ALL screens completes as US3–US5 migrate
them).

- [X] T023 [P] [US1] Create the token contrast matrix in apps/web/tests/unit/contrast-matrix.test.ts: parse tokens.css, compute WCAG ratios for every normative pairing per palette (text ≥4.5:1, non-text/control-boundary ≥3:1) including the trap rows — white label on `accent-strong-hover`, light warning `#8F7F36`, dark selection translucency composited over `bg-primary`, checkbox outline `fg-disabled`, accent/danger text on hovered elevated surfaces (S1.4, D12)
- [X] T024 [P] [US1] Create the hex audit in apps/web/tests/unit/hex-audit.test.ts: grep `#[0-9A-Fa-f]{3,8}` across apps/web/src/** and all CSS except tokens.css → zero matches (inline justification-comment exception recognized per contracts/ui-theme.md — until T064 the only exemption is T005's `/* LEGACY */` block in globals.css; T064 deletes it, after which the audit runs unexempted) (UIT-004, S5.6)
- [X] T025 [US1] Replace all emoji/Unicode system iconography with `lucide-react` across existing chrome (apps/web/src/components/layout/Sidebar.tsx nav glyphs, tasks/TaskRow.tsx, tasks/TaskList.tsx, dialog close buttons, toasts): 14–16px in dense rows, 18–20px headers, `aria-hidden` on decorative, `aria-label` via IconButton on icon-only controls; emoji remain ONLY as user-chosen project icons (ProjectForm presets + sidebar `iconGlyph()` restyled, not removed) (FR-105, D4, S1.5)
- [X] T026 [US1] Adopt Avatar everywhere identity shows: apps/web/src/app/(app)/settings/page.tsx (replaces raw `<img>`), components/tasks/CommentItem.tsx, tasks/AssigneePicker.tsx, tasks/MentionPicker.tsx, projects/MembersDialog.tsx (US-18.AS-06, FR-105)
- [X] T027 [P] [US1] Palette E2E in apps/web/tests/e2e/theme.spec.ts: SSR default `dark-cool` with no flash pre-hydration (UIT-001/S1.2), class swap on `<html>` re-themes all four palettes without layout shift (UIT-002/003/S1.3), `color-scheme` follows mode for native controls/scrollbars (UIT-005)
- [X] T028 [P] [US1] Per-palette accessibility suite in apps/web/tests/e2e/axe.spec.ts using `@axe-core/playwright`: walk each in-scope screen × 4 palettes (class swap only, per contracts/ui-theme.md) asserting zero WCAG 2.1 AA violations (UIT-010, SC-008, D12) — screens join the walk as US3–US5 migrate them

**Checkpoint**: Token architecture verified per palette; visual language ready for the
behavioral stories

---

## Phase 5: User Story 3 — UI-Operable Daily Workflow & Shortcut Removal (Priority: P1)

**Goal**: The complete daily workflow is performable through visible controls alone —
topbar + inline add, row quick actions + complete "⋯" menu (incl. new Duplikuj),
sidebar with counts, reorder affordances, empty states — and the single-key shortcut
system is deleted (FR-103, FR-107–112, SC-018).

**Independent Test**: SC-018 E2E journey passes with pointer + Tab/Enter/Esc only;
binding audit confirms no single-key shortcut registered; both new endpoints pass
allow+deny integration tests.

### Backend (tests first — Constitution VIII/IX)

- [X] T029 [P] [US3] Write allow+deny integration tests for `GET /api/views/counts` in apps/api/tests/TaskFlow.IntegrationTests/TaskManagement/ViewCountsTests.cs (TaskManagement namespace → lands in the `tasks-core` CI shard per the complement filters): counts equal incomplete-filtered lengths of each corresponding view listing (seeded caller; overdue task counted in `today`; done/cancelled excluded; Europe/Warsaw boundary case); deny/scoping — second user's tasks/projects never leak, project the caller was removed from disappears, archived projects excluded (contracts/view-counts.md; frozen-clock TimeProvider hook per established pattern)
- [X] T030 [P] [US3] Write allow+deny integration tests for `POST /api/tasks/{id}/duplicate` in apps/api/tests/TaskFlow.IntegrationTests/TaskManagement/DuplicateTaskTests.cs (TaskManagement namespace → `tasks-core` CI shard): owner duplicates Inbox task; editor duplicates shared-project task; ex-member assignee silently dropped; completion state + comments NOT copied; position adjacent to source; idempotent replay returns existing duplicate; 409 on foreign `newTaskId`; 400 on missing/malformed `newTaskId`; deny — non-member → 404-shaped, viewer → create-denied, cross-user Inbox task → denial (contracts/task-duplicate.md)
- [X] T031 [US3] Implement GetViewCounts query handler in apps/api/src/TaskFlow.Application/ (public concrete types for Wolverine codegen): incomplete = status ∉ {done, cancelled} — identical predicate to the view queries; Europe/Warsaw date boundaries via the existing evaluation; ownership scoping for inbox/today/upcoming/assigned, membership for `projects[]`; expose `GET /api/views/counts` in apps/api/src/TaskFlow.Api/Endpoints/ViewCountsEndpoints.cs (deny-by-default, delegate HTTP→bus) and add it to the OpenAPI document-transformer pipeline (data-model.md ViewCounts, D6)
- [X] T032 [US3] Implement DuplicateTask command handler in apps/api/src/TaskFlow.Application/: copy title/description/priority/due date (incl. `has_time`)/labels/project/assignees with assignees re-validated against CURRENT membership (non-members dropped, no notifications), fresh status + timestamps per FR-003 default, fractional position directly AFTER source via existing ranking, client-supplied `newTaskId` idempotency; route `POST /api/tasks/{id}/duplicate` on apps/api/src/TaskFlow.Api/Endpoints/TaskEndpoints.cs + OpenAPI exposure (data-model.md DuplicateTask, D7, FR-112)
- [X] T033 [US3] Regenerate the typed client: boot PG + API per quickstart.md, run `pnpm --dir apps/web gen:api`, commit apps/web/src/lib/api/generated/schema.d.ts; `openapi-sync` CI job stays green (Constitution VI)

### Shortcut removal & shell

- [X] T034 [US3] Delete the shortcut system wholesale (D5, FR-111): apps/web/src/hooks/useGlobalShortcuts.ts, apps/web/src/components/tasks/ShortcutsHelp.tsx, handler wiring in apps/web/src/app/(app)/page.tsx and components/tasks/DailyView.tsx, all `aria-keyshortcuts` attributes, `tf-kbd`/`tf-shortcuts` CSS rules, "Press C…" empty-state copy, and apps/web/tests/unit/shortcuts.test.ts — including the Alt+↑/↓ reorder chord (keyboard reorder binding stays deferred, OOS-20)
- [X] T035 [US3] Re-implement list keyboard operability as composite-widget behavior shared by BOTH listboxes — apps/web/src/components/tasks/TaskList.tsx AND DailyView.tsx (DailyView renders its own `role="listbox"` and called the deleted hook directly) — via a shared roving-tabindex / `aria-activedescendant` piece: ↑/↓ selection, Space toggle, Enter opens (UIT-090..093, D5); no document-level listeners; hover-revealed row actions also appear on keyboard focus and are Tab/Shift+Tab traversable in both directions (not `display:none` — spec Edge Cases)
- [X] T036 [US3] Create Topbar in apps/web/src/components/layout/Topbar.tsx + Topbar.module.css: persistent global "+ Nowy task" resolving context per clarification (Inbox → Inbox, project view → that project, Today → due today, all else → Inbox), and the search field as a DISABLED placeholder (rendered per mockup, excluded from tab order, announced unavailable — slice 013 owns function) (FR-107, US-18.AS-01)
- [X] T037 [US3] Rebuild the app shell in apps/web/src/app/(app)/layout.tsx + a co-located shell CSS Module: grid of Topbar + Sidebar + main + drawer host slot, z-index from token scale, 768–1024px behavior hooks (S5.5 groundwork)
- [X] T038 [US3] Create useViewCounts in apps/web/src/hooks/useViewCounts.ts (query key `['views','counts']` joining the view-key family) and add its invalidation to the existing mutation factories' `onSettled` in apps/web/src/hooks/useTaskMutations.ts (+ comment/membership factories where task-affecting) so counts track optimistic flows (D6, data-model.md)
- [X] T039 [US3] Rebuild Sidebar in apps/web/src/components/layout/Sidebar.tsx + Sidebar.module.css: Inbox/Today/Upcoming/Assigned/projects as clickable entries with Lucide icons + counts from useViewCounts, collapsible with `aria-expanded` (client state, not persisted), project emoji icons retained (FR-109, US-18.AS-04, UIT-023/024)

### Rows, menu, reorder, add, empty states

- [X] T040 [US3] Rebuild TaskRow in apps/web/src/components/tasks/TaskRow.tsx + TaskRow.module.css: hover/focus quick actions (complete via Checkbox, edit, "⋯" trigger), drag-handle zone, correct hit targets ≥32px and stacking over the virtualized `translateY` rows (documented trap), 13px density (FR-108, US-18.AS-02)
- [X] T041 [US3] Implement the complete row "⋯" menu using ui/Menu in TaskRow: edit, priority, due date, labels, move to project, assign, Duplikuj, comments/open details, Przenieś wyżej/Przenieś niżej, delete — every operation available on the task, keyboard-navigable (FR-103/FR-108, US-18.AS-02); the comments/open-details item targets the task detail surface — it opens the drawer once T050–T052 land (US4), and until then routes to the existing detail surface where mounted
- [X] T042 [US3] Create useDuplicateTask in apps/web/src/hooks/useDuplicateTask.ts on the established optimistic factory pattern: `onMutate` inserts copied-field duplicate with client-generated id adjacent to source, `onError` snapshot rollback, `onSettled` invalidates view keys + `['views','counts']`; wire to the menu's "Duplikuj" (FR-112, contracts/task-duplicate.md)
- [X] T043 [US3] Implement drag reorder with `@dnd-kit/core` + `@dnd-kit/sortable` in TaskList/TaskRow: handle appears in the hover/focus action zone, `DragOverlay` rendered in a portal with token z-index (virtualization stacking regression test included), new fractional index via existing apps/web/src/lib/position.ts `between()` → existing `PATCH /api/tasks/{id}/position`; "Przenieś wyżej/niżej" menu items compute the same write as the keyboard-reachable path (S3.7, D9, FR-102 affordance)
- [X] T044 [US3] Restyle inline quick-add in apps/web/src/components/tasks/TaskCapture.tsx + TaskCapture.module.css on ui/Input: present within each task list (Inbox, project, Today), creating in that view's context; Ctrl+Enter/Esc editing keys intact (FR-107, FR-030)
- [X] T045 [US3] Apply EmptyState across Inbox/Today/Upcoming/Assigned/project list surfaces (apps/web/src/app/(app)/page.tsx, components/tasks/DailyView.tsx, TodayView.tsx, UpcomingView.tsx, AssignedView.tsx): short hint + relevant action button, rendered only when confirmed empty (never during load), no shortcut copy (FR-110, US-18.AS-05, spec Edge Cases)

### Story tests

- [ ] T046 [US3] Rewrite the 5 keyboard-driven Playwright specs to UI-driven flows (click affordances instead of bare keys), preserving their behavioral assertions and `[INV-###]` tags: apps/web/tests/e2e/tasks.spec.ts (c/e/?/Space/Delete/Alt+arrow), daily-planning.spec.ts (G-chords/t/1/e), labels.spec.ts (l), task-assignment.spec.ts (a), projects.spec.ts (m); sharing.spec.ts keeps its Escape editing-key press (FR-030, not a shortcut)
- [ ] T047 [P] [US3] Binding-audit E2E in apps/web/tests/e2e/shortcut-removal.spec.ts, `[INV-###]`-tagged: with a focused list, pressing C/E/M/L/T/A/1–4, G-chords, `?` does nothing, and Delete no longer deletes the selected task (A and Delete included per D5 — the shipped hook binds both even though S3.5's list omits them; Delete's keyboard-reachable replacement is the "⋯" menu's delete item from T041) (no action, no errors); shortcuts overlay absent; former shortcut characters type normally in inputs; Ctrl+Enter save / Esc cancel still work (S3.5, FR-111/FR-030)
- [ ] T048 [US3] SC-018 journey E2E in apps/web/tests/e2e/sc-018.spec.ts: fresh user, no instruction — create (global + inline), edit, set priority/date/labels, move to project, complete, comment on shared task — pointer + Tab/Enter/Esc only (S3.6, SC-018)
- [ ] T049 [P] [US3] New-capability E2E in apps/web/tests/e2e/counts-duplicate.spec.ts: sidebar counts render from the endpoint and update after a mutation (UIT-024); "Duplikuj" creates the adjacent copy optimistically (UIT-041); reorder via drag handle AND menu items persists across reload (S3.7); the topbar search placeholder renders per mockup, is excluded from tab order, and is announced unavailable (spec Provenance FR-032/034 carve-out — 019 owns presence + disabled semantics; UIT-022 focusability/functionality assertions transfer to slice 013)

**Checkpoint**: Daily workflow fully UI-operable, shortcuts gone, both endpoints live —
core P1 scope complete

---

## Phase 6: User Story 4 — Task Detail Drawer (Priority: P2)

**Goal**: A non-modal, deep-linkable right-side drawer replaces the comments modal:
every field directly editable inline, slice-009 comment thread preserved (FR-106).

**Independent Test**: open a task from any listing view → drawer with all fields
editable; list behind stays interactive; `?task=<id>` deep link + back/forward restore
state; comment regression (avatars, mentions, tombstones, sanitized markdown, viewer
sees no composer) passes.

- [X] T050 [US4] Create the Drawer primitive in apps/web/src/components/ui/Drawer.tsx + Drawer.module.css: right-side non-modal 420–480px docked, overlay mode ≤1024px, NO focus trap (list stays interactive), Esc closes + returns focus to invoker EXCEPT while a text field inside is focused (field Esc cancels the edit first — FR-030), z-index token 40, reduced-motion-instant transitions (D8, S4.2, FR-047)
- [X] T051 [US4] Create TaskDrawer in apps/web/src/components/tasks/TaskDrawer.tsx + TaskDrawer.module.css: all fields inline-editable — title, description, priority/project/label/assignee pickers re-skinned on catalog components, due date via ui/DateInput (T009 — Polish NL parse behavior unchanged) — over the existing mutation hooks (US-18.AS-03)
- [X] T052 [US4] Wire drawer state to the URL in the (app)/layout.tsx drawer host: `?task=<taskId>` on all five listing routes (`/`, `/today`, `/upcoming`, `/assigned`, `/projects/[id]`) via `router.push` — deep link, back/forward, list state preserved; invalid/inaccessible id renders the drawer's error state with recovery (D8, S4.3, FR-049)
- [X] T053 [US4] Move the comment thread into the drawer for shared-project tasks, restyling apps/web/src/components/tasks/CommentThread.tsx, CommentItem.tsx, CommentComposer.tsx, MentionPicker.tsx, DeleteCommentDialog.tsx to catalog components + CSS Modules: chronological order, Avatar authors, @mention highlight, tombstones, safeMarkdown sanitization, viewer sees thread but no composer (S4.4 — slice-009 behavior byte-for-byte)
- [X] T054 [US4] Delete apps/web/src/components/tasks/TaskDetailPanel.tsx and its modal wiring on the project screen once TaskDrawer replaces it (§J3.7 — no orphaned code)
- [ ] T055 [US4] Drawer E2E in apps/web/tests/e2e/drawer.spec.ts, `[INV-###]`-tagged: non-modal interactivity + Esc-with-field-exception (S4.2), deep link / back-forward / list-state (S4.3, UIT-054), comment-thread regression incl. XSS payload never executing and viewer-role composer absence (S4.4); ALSO rewrite apps/web/tests/e2e/comments.spec.ts against the drawer — its "Komentarze:" button + `role="dialog"` assertions die with T053/T054 — keeping (or moving into drawer.spec.ts) every `[INV-###]` tag it carries so the coverage gate holds

**Checkpoint**: Complete editing surface shipped; comments modal fully replaced

---

## Phase 7: User Story 5 — Full-Codebase Sweep: Remaining Screens, States & Cleanup (Priority: P2)

**Goal**: EVERY remaining UI surface migrates to the design system with complete
interaction states; dead code (incl. the dismantled monolith) is deleted; sweep audits
(knip, hex, visual, inventory) go green (§J3.6–8).

**Independent Test**: each screen renders design-system-only and passes per-palette axe;
knip and the hex audit report zero findings; [V] baselines approved ×4 palettes;
inventory-coverage gate green.

- [ ] T056 [P] [US5] Migrate the project view + projects components to catalog + co-located CSS Modules: apps/web/src/app/(app)/projects/[id]/page.tsx and apps/web/src/components/projects/ProjectForm.tsx, ProjectSelector.tsx, MembersDialog.tsx, InviteMemberForm.tsx, RoleBadge.tsx, ShareProjectDialog.tsx, TransferOwnershipDialog.tsx, RemoveMemberDialog.tsx, LeaveProjectDialog.tsx, ArchiveProjectDialog.tsx, DeleteProjectDialog.tsx — viewer-role affordance gaps preserved (S5.1, spec Edge Cases)
- [ ] T057 [P] [US5] Migrate Today/Upcoming surfaces: apps/web/src/app/(app)/today/page.tsx, upcoming/page.tsx and components/tasks/DailyView.tsx, TodayView.tsx, UpcomingView.tsx, and RescheduleInput.tsx migrated onto ui/DateInput (T009 — Polish NL parse + FR-006 error presentation unchanged) (S5.1)
- [ ] T058 [P] [US5] Migrate the Assigned view: apps/web/src/app/(app)/assigned/page.tsx + components/tasks/AssignedView.tsx (S5.1)
- [ ] T059 [P] [US5] Migrate Sign-in: apps/web/src/app/(auth)/layout.tsx, (auth)/signin/page.tsx + components/auth/SignInButton.tsx — Instrument Serif brand wordmark per design-brief (S5.1)
- [ ] T060 [P] [US5] Migrate Settings/Profile: apps/web/src/app/(app)/settings/page.tsx + components/ui/DeleteAccountDialog.tsx on catalog Dialog/Button (S5.1)
- [ ] T061 [P] [US5] Migrate label surfaces: apps/web/src/components/labels/LabelChips.tsx → ui/Chip, LabelSelector.tsx + CSS Modules; TaskEditor.tsx to catalog inputs (S5.1)
- [ ] T062 [US5] Interaction-states completeness pass across all migrated surfaces: Skeleton ONLY on genuine network-bound loads (never for optimistically paintable content — S5.2), every failure surfaces a clear message + in-place recovery action with structured logging via a shared `logError({ severity, operation, error })` helper — FR-050's severity/context/details shape (S5.3, FR-049/050), optimistic-pending states, long title/label/project truncation with ellipsis + focus-reachable full value via Tooltip (S5.4, FR-046), 768–1024px: no horizontal scroll, drawer overlays, sidebar collapsible (S5.5); this pass also asserts the re-skinned pickers' (priority/project/label/assignee) state + ARIA contracts per the spec's Component catalog
- [ ] T063 [US5] Normalize all user-facing copy to Polish across apps/web/src (D15): audit every surface for mixed English strings, align with mockup vocabulary ("+ Nowy task", "Duplikuj", "Przenieś wyżej/niżej"); no i18n layer
- [ ] T064 [US5] Dead-code deletion (§J3.7): remove the `/* LEGACY */` `tf-` block AND its hex-audit exemption marker from apps/web/src/app/globals.css (monolith fully dismantled; the hex audit T024 now runs with zero exemptions outside tokens.css), delete superseded components/hooks/styles surfaced by `pnpm --dir apps/web audit:dead`, iterate until knip reports zero unreferenced UI-layer modules (S5.6)
- [ ] T065 [US5] Wire knip into the `web-quality` job in .github/workflows/ci.yml (D14 — lands now that it passes)
- [ ] T066 [US5] Visual regression [V] suite in apps/web/tests/e2e/visual.spec.ts: `toHaveScreenshot` per in-scope screen × 4 palettes × 1440/1024/768px widths, `reducedMotion: 'reduce'` + `animations: 'disabled'` for determinism; generate/update baselines in the CI-matching linux environment (`--update-snapshots` inside the Playwright docker image matching the `web-e2e` job — `toHaveScreenshot` baselines are platform-suffixed, so Windows-generated baselines would never match ubuntu CI) and commit them for human per-palette approval (D13, UIT-100)
- [ ] T067 [US5] Close the inventory: add remaining `[INV-###]` tags / covering tests for every entry still uncovered (incl. surfaces discovered during the sweep — §J3.6 joins them to feature-inventory.md), until apps/web/tests/unit/inventory-coverage.test.ts passes with the FULL suite green (S2.2/S2.3); rewrite/retire the seed spec specs/019-ui-design-system/tests/mockup.spec.ts against the real application (spec Test plan), folding its assertions into the tagged suites

**Checkpoint**: Whole codebase on the design system; all audits green; inventory gate
proves zero feature loss

---

## Phase 8: Polish & Cross-Cutting Concerns

**Purpose**: Final validation against quickstart.md and the spec's exit criteria

- [ ] T068 Run all five quickstart.md scenarios end-to-end locally: full Vitest suite (contrast matrix, hex audit, inventory coverage), Playwright suite (E2E + axe ×4 + visual — stop any dev API on :4311 first), `dotnet test` (allow+deny for both endpoints), `pnpm --dir apps/web audit:dead`
- [ ] T069 [P] Verify performance budgets are not regressed per UIT-110/111 against a production build (`next build` + `next start` over the self-booted stack per quickstart.md): FCP/TTI via the browser Performance API in a Playwright probe (median of 5 runs), optimistic paint via a requestAnimationFrame-delta probe asserting the DOM reflects the action within one frame (<16 ms) of the triggering click, on the redesigned Inbox (SC-002/003; results recorded in the PR — the [P] benchmark suite proper stays post-baseline per spec assumption)
- [ ] T070 Final gate review: CI green on web-quality (vitest + knip), NEW web-e2e, openapi-sync, and API jobs; re-verify integration-shard capacity/complement filters in .github/workflows/ci.yml after T029/T030 add ~16 container-per-fact tests to `tasks-core` (Docker start timeouts there = capacity, not flakiness); [V] baselines human-approved per palette; feature-inventory.md transfer notes (D11) accurate; spec Success Criteria checklist walked (SC-018, regression gate, SC-008 ×4, sweep gate)

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: no dependencies — start immediately
- **US2 audit (T019)**: depends only on Setup; MUST complete before ALL of Phase 2 (T004–T018) and every later task — it captures the pre-redesign app, and the redesign visibly begins with T005/T006. Effective execution order: Phase 1 → T019 → Phase 2 → …
- **Foundational (Phase 2)**: T004 → T005/T006 → catalog T007–T018 (all [P] after tokens exist); blocks US1/US3/US4/US5 implementation
- **US2 rest (T020–T022)**: after T019; may run in parallel with Phase 2
- **US1 (Phase 4)**: after Foundational; T023 needs only tokens.css (can start right after T004); T024 also needs T005's LEGACY exemption marker to be green
- **US3 (Phase 5)**: after US1 (visual language) and T019; backend chain T029/T030 → T031/T032 → T033; frontend needs T033 (typed client) for T038/T042; T034 before T035; shell T036/T037 AND counts hook T038 (itself needing T033) before T039; rows T040 → T041 → T042/T043
- **US4 (Phase 6)**: after US3 shell/rows (drawer host in T037, menu in T041); T050 → T051 → T052/T053 → T054 → T055
- **US5 (Phase 7)**: after US1–US4 patterns; screen migrations T056–T061 all [P]; T062/T063 after them; T064 → T065; T067 last (needs everything)
- **Polish (Phase 8)**: after all desired stories

### User Story Dependencies

- **US2 (P1)**: independent of all styling work — the audit precedes it, the gate outlives it
- **US1 (P1)**: depends on Foundational only
- **US3 (P1)**: builds on US1's visual language; backend endpoints have no story dependencies
- **US4 (P2)**: consumes US3's shell + row menu; independently testable once shipped
- **US5 (P2)**: consumes US1–US4 patterns; each screen independently verifiable

### Parallel Opportunities

- Catalog primitives T007–T018 (12 tasks) — all parallel after T004–T006
- T020–T022 (US2 infra) parallel with all of Phase 2
- T023/T024/T027/T028 (US1 test authoring) parallel with T025/T026
- Backend T029+T030 parallel; T031 then T032 sequential (both touch the shared OpenAPI document-transformer registration); entire backend chain parallel with T034–T037 frontend work (until T038/T042 need the client)
- Screen migrations T056–T061 (6 tasks) — fully parallel
- Whole-story parallelism: US2's authoring track and the API track (T029–T033) can proceed while Foundational/US1 styling lands

---

## Parallel Example: Foundational catalog

```bash
# After T004–T006, launch all primitives together:
Task: "Restyle Button in apps/web/src/components/ui/Button.tsx + Button.module.css"
Task: "Create IconButton in apps/web/src/components/ui/IconButton.tsx"
Task: "Create Input/Textarea in apps/web/src/components/ui/"
Task: "Create Checkbox in apps/web/src/components/ui/Checkbox.tsx"
Task: "Create Chip in apps/web/src/components/ui/Chip.tsx"
Task: "Create Menu in apps/web/src/components/ui/Menu.tsx"
Task: "Restyle Dialog in apps/web/src/components/ui/Dialog.tsx"
Task: "Restyle Toast in apps/web/src/components/ui/Toast.tsx"
Task: "Create Skeleton, EmptyState, Avatar, Tooltip in apps/web/src/components/ui/"
```

## Parallel Example: User Story 5 sweep

```bash
# All screen migrations at once:
Task: "Migrate project view + projects/ components"
Task: "Migrate Today/Upcoming surfaces"
Task: "Migrate Assigned view"
Task: "Migrate Sign-in"
Task: "Migrate Settings/Profile"
Task: "Migrate label surfaces"
```

---

## Implementation Strategy

### MVP scope

All three P1 stories together (Phases 1–5) form the minimal shippable increment: US2
alone proves nothing changed, US1 alone changes nothing visible, US3 without US1 restyles
nothing. Recommended MVP stopping point: **end of Phase 5** — tokens verified ×4
palettes, inventory gate live in CI, daily workflow fully UI-operable, shortcuts gone.

### Incremental Delivery

**Branch-CI note**: once T020 (coverage gate) and T022 (`web-e2e`) land, `web-quality`
and `web-e2e` stay RED on the feature branch until T046 (rewritten keyboard specs) and
T067 (inventory closure) — by design: the gate is the red-list driving the work. The
checkpoints below are validation points on the branch, NOT independently mergeable
increments; slice 019 merges to main as one unit once all gates are green.

1. Setup → T019 inventory audit (freezes the pre-redesign feature set)
2. Foundational tokens + catalog (app still fully working — additive)
3. US2 gate + CI jobs → red-list of coverage debt drives all later phases
4. US1 → token architecture verified per palette
5. US3 → behavioral heart: validate SC-018 + binding audit, demo
6. US4 → drawer replaces modal, validate deep links + comment regression, demo
7. US5 → sweep every remaining surface, audits go green, [V] baselines approved
8. Polish → quickstart walk + final gate review

### Format validation

All 70 tasks follow `- [ ] T### [P?] [US#?] description`; every implementation task
(T001–T067) names explicit file path(s); the Polish-phase validation tasks (T068–T070)
name the commands and CI gates they exercise instead. Story labels only on Phases 3–7
tasks; [P] only where files and dependencies are disjoint.

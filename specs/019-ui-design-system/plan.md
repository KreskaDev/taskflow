# Implementation Plan: UI Design System & Full UI Operability

**Branch**: `019-ui-design-system` | **Date**: 2026-08-09 | **Spec**: `specs/019-ui-design-system/spec.md`

**Input**: Feature specification from `/specs/019-ui-design-system/spec.md`; normative
annexes `design-brief.md` (tokens/component contracts), `ui-test-plan.md`
(UIT-001..112), `visual-requirements.md` §A–§J, approved `mockup-inbox.html`.

## Summary

Rebuild the entire shipped UI (slices 001–009: 7 routes, 39 components) on the KreskaDev
4-palette semantic token system (single `tokens.css`, composite class on `<html>`,
default `dark-cool`) with co-located CSS-Module component styling, Geist/Lucide/avatar
identity, and full UI-first operability: topbar with global "+ Nowy task" and a disabled
search placeholder, collapsible sidebar with authorization-scoped counts (new
`GET /api/views/counts`), row hover/focus quick actions + a complete "⋯" menu (incl. new
"Duplikuj" — FR-112, new `POST /api/tasks/{id}/duplicate`), drag-handle + menu reorder
over the existing position API, a non-modal deep-linkable task drawer replacing the
comments modal, and empty/error/loading states everywhere. The single-key shortcut
system is deleted wholesale. A regression feature inventory (`INV-###` ↔ `[INV-###]`
test tags) gates in CI — which gains a `web-e2e` job (Playwright currently never runs in
CI) — alongside axe-per-palette, a token contrast matrix, per-palette screenshots, and a
dead-code/hex sweep audit (knip + grep). Decisions D1–D15: `research.md`.

## Technical Context

**Language/Version**: TypeScript 5.7 (strict, no opt-outs) on Next.js 15 App Router /
React 19; C# on .NET 9 (nullable + analyzers-as-errors)

**Primary Dependencies**: web — TanStack Query v5, `@tanstack/react-virtual`,
`openapi-fetch` + generated client, `fractional-indexing`, zod, date-fns(-tz),
react-markdown + rehype-sanitize; NEW: `lucide-react`, `@dnd-kit/core` +
`@dnd-kit/sortable`, `next/font` (Geist, Instrument Serif, JetBrains Mono — build-time
self-hosted); api — ASP.NET Core, Wolverine 6.11, EF Core/Npgsql

**Storage**: PostgreSQL — unchanged; NO migration this slice (no entity introduced or
modified; two new read/command endpoints over existing tables)

**Testing**: Vitest + Testing Library (component, contrast matrix, hex audit,
inventory-coverage gate), Playwright (E2E via self-booted PG/API/fake-IdP; NEW:
`@axe-core/playwright` per-palette [A] suite, `toHaveScreenshot` per-palette [V] suite),
xUnit + Testcontainers integration incl. allow+deny for both new endpoints; NEW dev
tooling: knip (dead-code audit). NEW CI job: `web-e2e` (Playwright is local-only today —
without it the inventory/S2.4 gate cannot gate merges)

**Target Platform**: web, desktop-first ≥768px (chromium-tested; full mobile OOS-03);
single-origin Next.js BFF → .NET API

**Project Type**: web monorepo — `apps/web` + `apps/api`

**Performance Goals**: constitution budgets guarded, not newly established — FCP <1 s,
TTI <2.5 s (UIT-111), optimistic paint <16 ms (UIT-110), 60 fps virtualized lists,
fan-out budget inapplicable until slice 016; [P] suite runs post-benchmark-baseline

**Constraints**: CSP intact (`style-src 'self'`, `font-src 'self'` — no runtime CDN, no
CSS-in-JS); tokens in ONE source file, components consume semantic tokens only (FR-104,
§J3.8); component-scoped co-located styling, no style monolith; zero raw hex outside
`tokens.css`; zero unreferenced UI-layer modules post-migration (§J3.7); WCAG 2.1 AA
per palette (×4); `Europe/Warsaw` boundaries for count semantics (Principle X); UI copy
normalized to Polish (D15)

**Scale/Scope**: 7 routes + 39 existing components restyled/rebuilt (~95 currently
unstyled class names get real styling for the first time), ~20-component design-system
catalog, 4 palettes × screens for [A]/[V] suites, ~112 UIT rows + full `INV-###`
inventory of slices 001–009, 2 new API endpoints, 1 deleted subsystem (shortcuts:
hook + overlay + 5 keyboard-driven E2E specs rewritten to UI-driven flows)

## Constitution Check

*GATE: evaluated pre-Phase-0 and re-checked post-Phase-1 — PASS (no violations; no
Complexity Tracking entries).*

| Principle | Verdict | How the design complies |
|---|---|---|
| I. UI-First Operability | PASS — this slice *realizes* it | Every operation gets a visible affordance (FR-103): topbar/global add, inline add, row quick actions + complete "⋯" menu, drawer editing, sidebar nav+counts, empty-state actions, reorder handle+menu. Shortcut system deleted (FR-111); hit targets ≥32px; correct stacking via token z-index scale (D5, D8, D9) |
| II. Accessibility (WCAG 2.1 AA) | PASS | axe ×4 palettes (D12), token contrast matrix incl. hover/selected/disabled traps (S1.4), focus rings, ARIA contracts per component catalog, `role="menu"` arrow nav, dialog focus contract on modals, non-modal drawer with Esc/focus-return, persistent `role="status"` toast region, `prefers-reduced-motion` (FR-047), keyboard operability preserved as composite-widget behavior after shortcut removal (D5) |
| III. Instant Response | PASS | Duplicate + all migrated mutations stay on the established optimistic factory pattern (snapshot/rollback/invalidate); skeletons only for genuine network-bound loads (S5.2); drawer/menu non-blocking; optimistic paint budget guarded by UIT-110 |
| IV. Minimalist UI | PASS — aesthetic direction IS this slice | KreskaDev 4-palette token system 1:1, single token file, semantic-only consumption (D1/D2), Geist 13px density, Lucide-only chrome (D4), no onboarding wizards (FR-110), purposeful 100–150ms motion |
| V. Connected, Server-Authoritative | PASS | No new runtime services; fonts self-hosted at build time (D3); screenshots via built-in Playwright, not SaaS (D13); counts computed server-side (D6) |
| VI. Type Safety End-to-End | PASS | Both new endpoints enter the OpenAPI document → regenerated typed client (`gen:api`, `openapi-sync` CI); TS strict maintained; zod at trust boundaries unchanged; no hand-written client code |
| VII. Data Integrity & Resilience | PASS | No migration (FR-051 untriggered); FR-049/050 error surfacing with recovery on every in-scope surface; undo *mechanics* remain slice 014 — Toast ships the persistent-with-close variant so the 30 s window has its component ready (D11); duplicate is a non-destructive create |
| VIII. Test-First | PASS | Inventory written BEFORE restyling (Story 2 priority ordering); every INV row test-covered with a CI coverage gate (D10); allow+deny integration tests for both endpoints; failing suite blocks merge — made real by the new `web-e2e` CI job (D14) |
| IX. Authentication & Authorization | PASS | The only new reads/writes (counts, duplicate) are deny-by-default, dispatched by resource visibility, handler-layer enforced, each with allow AND deny tests (contracts); no authorization model changes; viewer-role UI states preserved (restyle only) |
| X. Time & Timezone | PASS | Count semantics (today+overdue, upcoming window) computed with the same Europe/Warsaw evaluation as the existing view queries (data-model.md) |
| XI. Privacy & Personal Data | PASS | No new personal data; avatars render existing Google photo/initials; no retention change |
| XII. Security by Default | PASS | safeMarkdown/XSS regression kept as inventory entries (S4.4); CSP headers unchanged and verified compatible with next/font + CSS Modules; no secrets touched |

**Post-design re-check (after Phase 1)**: no design artifact introduced a violation —
no new project, no repository/abstraction layers, both endpoints follow the existing
Wolverine handler pattern, no entity/migration, no external runtime dependency.
Complexity Tracking remains empty.

## Project Structure

### Documentation (this feature)

```text
specs/019-ui-design-system/
├── plan.md              # This file
├── research.md          # Phase 0 — decisions D1–D15 (all unknowns resolved)
├── data-model.md        # Phase 1 — ViewCounts read model, DuplicateTask command, UI state
├── quickstart.md        # Phase 1 — validation scenarios per story + audit commands
├── contracts/
│   ├── view-counts.md   # GET /api/views/counts
│   ├── task-duplicate.md# POST /api/tasks/{id}/duplicate
│   └── ui-theme.md      # token file, palette class, consumption rules, fonts
├── feature-inventory.md # Story 2 deliverable (created during implementation, BEFORE restyling)
├── design-brief.md      # normative annex (pre-existing)
├── ui-test-plan.md      # UIT-001..112 (pre-existing)
├── visual-requirements.md, mockup-inbox.html, checklists/, tests/   # pre-existing inputs
└── tasks.md             # Phase 2 (/speckit-tasks — NOT created by /speckit-plan)
```

### Source Code (repository root)

```text
apps/web/
├── src/app/
│   ├── tokens.css               # NEW — the single token source (4 palettes + scales)
│   ├── globals.css              # SHRUNK to base/reset + imports (monolith dismantled)
│   ├── layout.tsx               # <html class="dark-cool">, next/font variables
│   ├── (app)/layout.tsx         # app shell: Topbar (NEW) + Sidebar + main + Drawer host
│   ├── (app)/{page,today,upcoming,assigned,projects/[id],settings}/  # migrated screens
│   └── (auth)/signin/           # migrated
├── src/components/
│   ├── ui/                      # design-system catalog — each Component.tsx + Component.module.css:
│   │                            #   Button, IconButton, Input, Textarea, DateInput, Checkbox,
│   │                            #   Chip, Menu (⋯/context), Dialog, Drawer (NEW), Toast (variants),
│   │                            #   Skeleton (NEW), EmptyState (NEW), Avatar (NEW), Tooltip-equiv
│   ├── layout/                  # Topbar (NEW: global add, disabled search placeholder), Sidebar
│   │                            #   (counts, collapse), shell grid
│   ├── tasks/                   # TaskList/TaskRow (quick actions, drag handle, ⋯ menu),
│   │                            #   TaskDrawer (replaces TaskDetailPanel modal), views, pickers
│   ├── projects/, labels/, auth/  # migrated to catalog components
├── src/hooks/                   # useViewCounts (NEW), useDuplicateTask (NEW), mutation factories;
│                                #   useGlobalShortcuts.ts DELETED with ShortcutsHelp.tsx
└── tests/
    ├── unit/                    # + contrast-matrix, hex-audit, inventory-coverage, component specs
    └── e2e/                     # keyboard-driven specs rewritten UI-driven; + axe.spec (×4 palettes),
                                 #   visual.spec (screenshots ×4 palettes ×3 widths), sc-018 journey

apps/api/src/
├── TaskFlow.Api/Endpoints/      # + ViewCountsEndpoints (GET /api/views/counts),
│                                #   duplicate route on TaskEndpoints (POST /api/tasks/{id}/duplicate)
├── TaskFlow.Application/        # + GetViewCounts query handler, DuplicateTask command handler
└── (Domain/Infrastructure unchanged — no migration)

apps/api/tests/TaskFlow.IntegrationTests/   # + allow/deny suites for both endpoints
.github/workflows/ci.yml                    # + web-e2e job (Playwright in CI); knip in web-quality
```

**Structure Decision**: existing web monorepo (`apps/web` + `apps/api`) — no new
projects. The one structural shift inside `apps/web` is styling architecture (§J3.8):
per-component CSS Modules replace the single global stylesheet, with `tokens.css` as
the sole token source and `globals.css` reduced to base/reset.

## Complexity Tracking

No constitution violations to justify — table intentionally empty.

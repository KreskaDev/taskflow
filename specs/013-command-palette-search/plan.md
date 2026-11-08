# Implementation Plan: Command Palette & Search

**Branch**: `013-command-palette-search` | **Date**: 2026-11-08 | **Spec**: `specs/013-command-palette-search/spec.md`

**Input**: Feature specification from `/specs/013-command-palette-search/spec.md` (refreshed
2026-11-08 to constitution v5.1.0 — visible-UI triggers, FR-027/FR-033 deferred; Clarifications
session same day).

## Summary

Deliver the search & command journey through visible controls: the topbar „Szukaj…" button
opens a **command palette** (catalog Dialog with combobox/listbox semantics) that searches one
new access-scoped endpoint `GET /api/search?q=` (tasks by title/description, projects incl.
archived, the caller's labels — token-AND `ILIKE`, server-ranked, capped) plus a client-side
action catalogue (Nowy task / Nowy projekt / Nowy cykl / Przejdź…). Selecting a task opens its
view with the drawer, a project its view (archived → inline „Przywróć", US-10.AS-06), an action
executes what its visible button does. Every list view gains a visible „Filtruj…" input that
narrows its already-loaded rows (the deferred `/` operation). No shortcut bindings, no
migration, no new entity. Decisions D1–D14: `research.md`.

## Technical Context

**Language/Version**: TypeScript 5.7 (strict) on Next.js 15 App Router / React 19; C# on
.NET 9 (nullable + analyzers-as-errors)

**Primary Dependencies**: web — TanStack Query v5 (`useSearch` keyed by normalized query,
keepPreviousData), slice-019 `ui/` catalog (Dialog, Input, IconButton, EmptyState, LiveRegion)
+ `tokens.css`, `next/navigation` router; api — ASP.NET Core, Wolverine 6.11 (HTTP→bus,
public concrete types), EF Core/Npgsql (`EF.Functions.ILike`, the escaped-pattern precedent)

**Storage**: PostgreSQL — NO migration; reads `tasks`, `projects`, `project_memberships`,
`labels` through the existing repositories (+ one new search method per repository)

**Testing**: xUnit + Testcontainers in NEW namespace `TaskFlow.IntegrationTests.Search` →
dedicated ci shard (D13), incl. the 10k characterization fact; Vitest (`lib/palette.ts`
catalogue matching + navigation targets, `lib/filter.ts` token/diacritics, CommandPalette
semantics, ViewFilterInput, inventory gate); Playwright E2E (`search.spec.ts`; palette joins
axe ×4 and [V])

**Target Platform**: web, desktop-first ≥768px (chromium-tested); single-origin Next.js BFF →
.NET API

**Project Type**: web monorepo — `apps/web` + `apps/api`

**Performance Goals**: palette opens within one frame (local state + dialog); search p95 < 50 ms
server-side over the caller's ≤ 10k-row working set (SC-009 — characterized by an integration
fact with a CI-safe ceiling); view filter is synchronous over loaded rows

**Constraints**: CSP intact; tokens-only styling; Polish copy only; Lucide-only icons; every
new UI behavior needs an `INV-###` row + tagged test (019 gate); NO `Ctrl+K`/`/` bindings
(OOS-20 — `shortcut-removal.spec.ts` must stay green); all user text rendered as text
(FR-099); SignalR revocation stays with slice 016 (D10 covers local invalidation)

**Scale/Scope**: 1 new endpoint + 3 repository search methods + 1 query handler; web: 1 dialog
component + 1 filter component + 2 pure libs + `useSearch` + opener rewrite + 6 filter hosts +
palette actions wiring (3 dialogs/routes); ~22 integration facts, ~15 unit specs, 1 E2E spec +
axe/[V] additions; [V] topbar change re-bakes the 5 base screens

## Constitution Check

*GATE: evaluated pre-Phase-0 and re-checked post-Phase-1 — PASS (no violations; Complexity
Tracking empty). Constitution v5.1.0.*

| Principle | Verdict | How the design complies |
|---|---|---|
| I. UI-First Operability | PASS | Opener is a visible topbar button on every view; every result/action is a visible list item; the view filter is a visible input in each list header; zero shortcut bindings (FR-027/033 deferred, OOS-20) |
| II. Accessibility (WCAG 2.1 AA) | PASS | Dialog focus contract (initial focus, trap, Esc, return); combobox + listbox/option with `aria-activedescendant`; polite LiveRegion result counts; contrast via tokens; no hover-only; reduced-motion untouched; palette joins axe ×4 (empty + populated) |
| III. Instant Response | PASS | Palette open = local state; results via TanStack with keepPreviousData (no flicker); server ILIKE over ≤ 10k scoped rows inside 50 ms (D2, characterized); view filter synchronous; read-only slice — no optimistic mutation except the reused `unarchiveProject` |
| IV. Minimalist UI | PASS | One opener (replaces the placeholder), one dialog, one filter input per view; no new chrome |
| V. Connected, Server-Authoritative | PASS | Search runs on the API over PG; the client holds no index; actions are UI affordances only |
| VI. Type Safety End-to-End | PASS | `SearchResponse` composed of existing typed read models; `q` validated at the boundary (FluentValidation + zod); result kinds discriminated in `lib/palette.ts`; `gen:api`/`openapi-sync` guard the client |
| VII. Data Integrity & Resilience | PASS | Read-only; FR-049 copy + retry on search failure; FR-050 `logError`; unarchive reuses the OCC-guarded mutation; FR-051 posture untouched (no migration) |
| VIII. Test-First | PASS | Allow+deny matrix per contract (incl. former member); ranking/caps/metachar facts; 10k characterization; INV rows precede tests |
| IX. Authentication & Authorization | PASS | Deny-by-default gate; per-candidate visibility dispatch in the handler (owner / current membership, viewer suffices for reads); former member gets nothing; labels per-user; results reuse the same read models the views already expose |
| X. Time & Timezone | PASS | No date logic; due chips render through the existing Warsaw formatter |
| XI. Privacy & Personal Data | PASS | `["search"]` invalidated on access-loss mutations and palette open (D10); no separate cache outlives revocation; no PII beyond what the views show |
| XII. Security by Default | PASS | LIKE metacharacters escaped (no wildcard injection); `q` length-capped; results rendered as text (FR-099); no new secrets/headers |

**Post-design re-check (after Phase 1)**: no artifact introduced a violation — no new
project/dependency/migration; one read endpoint. Complexity Tracking empty.

## Project Structure

### Documentation (this feature)

```text
specs/013-command-palette-search/
├── plan.md                 # This file
├── research.md             # Phase 0 — decisions D1–D14
├── data-model.md           # Phase 1 — SearchResponse, normalization, ranking, scoping, navigation targets
├── quickstart.md           # Phase 1 — validation scenarios + commands
├── contracts/
│   ├── search-api.md       # GET /api/search + test matrix
│   └── ui-palette.md       # opener / palette dialog / view filter / INV + determinism
└── tasks.md                # Phase 2 (/speckit-tasks — NOT created by /speckit-plan)
```

### Source Code (repository root)

```text
apps/web/src/
├── lib/palette.ts                          # NEW — action catalogue, token matching (client), result → href (pure, unit-tested)
├── lib/filter.ts                           # NEW — filterTasks (token-AND, case/diacritic-insensitive) (pure, unit-tested)
├── hooks/useSearch.ts                      # NEW — ["search", q] query (keepPreviousData), invalidation helper
├── hooks/useMembershipMutations.ts         # + invalidate ["search"] on leave/remove/unshare settle (D10)
├── components/search/CommandPalette.tsx    # NEW (+ .module.css) — Dialog + combobox/listbox, groups, empty/error, LiveRegion
├── components/search/ViewFilterInput.tsx   # NEW (+ .module.css)
├── components/layout/Topbar.tsx            # placeholder → opener button; hosts CommandPalette + ProjectForm(create)
├── app/(app)/page.tsx, components/tasks/DailyView.tsx, AssignedView.tsx,
│   app/(app)/projects/[id]/page.tsx, components/cycles/CycleView.tsx   # + ViewFilterInput host + filterTasks before grouping
├── components/cycles/CycleView.tsx         # + `?nowy=1` opens the create dialog (D12)
└── tests/  unit/ (palette, filter, command-palette, view-filter-input) · e2e/search.spec.ts · axe/[V] additions

apps/api/src/
├── TaskFlow.Application/TaskManagement/Queries/SearchTasksProjectsLabels.cs   # NEW — query + validator + handler (D1–D4)
├── TaskFlow.Application/TaskManagement/SearchResponse.cs                     # NEW read model
├── TaskFlow.Application/TaskManagement/ITaskRepository.cs, IProjectRepository.cs, Labels/ILabelRepository.cs  # + Search*Async
├── TaskFlow.Infrastructure/Persistence/TaskRepository.cs, ProjectRepository.cs, LabelRepository.cs            # ILIKE token-AND + ranking
├── TaskFlow.Api/Endpoints/SearchEndpoints.cs                                  # NEW — GET /api/search
└── TaskFlow.Api/OpenApi/TaskFlowDocumentTransformer.cs                        # + operationId `search`

apps/api/tests/TaskFlow.IntegrationTests/Search/   # NEW namespace → ci shard `search` (D13)
.github/workflows/ci.yml                           # + search shard; identity-labels-infra complement
specs/019-ui-design-system/feature-inventory.md    # + slice-013 INV section (gate input)
apps/web/src/lib/api/generated/schema.d.ts         # regenerated (openapi-sync)
```

**Structure Decision**: existing web monorepo, no new projects. Search is a read-side query in
the TaskManagement context reusing the three repositories; the palette and filter are catalog
components under a new `components/search/` folder; all styling co-located CSS Modules on
semantic tokens.

## Complexity Tracking

No constitution violations to justify — table intentionally empty.

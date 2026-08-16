# Implementation Plan: Cycles

**Branch**: `011-cycles` | **Date**: 2026-08-16 | **Spec**: `specs/011-cycles/spec.md`

**Input**: Feature specification from `/specs/011-cycles/spec.md` (refreshed 2026-08-16 to
constitution v5.1.0 / the UI-first reinterpretation clause; Clarifications session same day).

## Summary

Introduce the team-wide **Cycle** aggregate (ENT-03: planned/active/closed, manual transitions,
single-active invariant via partial unique index) with its lifecycle API, the `/cycle`
management view (metrics per FR-026 + switcher + create/edit/activate/close/delete), the
sidebar active-cycle entry (FR-017), task↔cycle assignment through the shared "⋯" menu
(„Cykl…" → CyclePicker; `PATCH /api/tasks/{id}/cycle` on the task's own visibility), the
close-review rollover (one transactional command: next/backlog/keep + per-task overrides;
carried-over flag on tasks), the by-cycle grouping that completes FR-024 („Bez cyklu" last,
EC-10 naming), and the per-user default-duration preference (FR-015) on /settings. One EF
migration (`cycles` + FK/flag/preference columns). Decisions D1–D18: `research.md`.

## Technical Context

**Language/Version**: TypeScript 5.7 (strict) on Next.js 15 App Router / React 19; C# on
.NET 9 (nullable + analyzers-as-errors)

**Primary Dependencies**: web — TanStack Query v5 (established optimistic factories in
`useTaskMutations.ts`), slice-019 `ui/` catalog + `tokens.css`, `lib/timezone.ts`
(date-fns-tz, Europe/Warsaw) for days-remaining; api — ASP.NET Core, Wolverine 6.11
(HTTP→bus, public concrete types), EF Core/Npgsql

**Storage**: PostgreSQL — ONE migration `AddCycles`: `cycles` table (+ partial unique index
`WHERE status='active'`, ordering index), `tasks.cycle_id` FK (RESTRICT) — the column itself
has existed since slice 002 — `tasks.carried_over`, `users.cycle_default_duration_days`

**Testing**: Vitest (cycle group builder, D5 ordering, Warsaw/DST days-remaining, menu/picker
builders, inventory gate rows), Playwright E2E (cycles journey; `/cycle` joins axe ×4 and [V]
with relative-date seeding + stylePath-hidden absolute dates), xUnit + Testcontainers in NEW
namespace `TaskFlow.IntegrationTests.Cycles` → dedicated ci shard (complement update, D15)

**Target Platform**: web, desktop-first ≥768px (chromium-tested); single-origin Next.js BFF →
.NET API

**Project Type**: web monorepo — `apps/web` + `apps/api`

**Performance Goals**: constitution budgets guarded — assignment/lifecycle ops paint
optimistically within one frame; server mutations p95 < 200 ms; metrics computed in the list
query at ASM-10 scale (no stored aggregates, no N+1: one grouped count query)

**Constraints**: CSP intact; tokens-only styling; Polish copy only; Lucide-only icons; every
new UI behavior needs an `INV-###` row + tagged test (019 gate); Europe/Warsaw for ALL cycle
date logic (FR-092 — days remaining, boundaries, overdue) via the existing timezone helpers;
no custom keyboard shortcuts (OOS-20); list surfaces keep the post-010 grid/row/gridcell
remediation (no widget role around focusable row controls); SignalR fan-out DEFERRED to slice
016 (D14 — refetch-based propagation now)

**Scale/Scope**: 1 new route (`/cycle`) + sidebar entry + settings field; ~8 new web modules
(`lib/cycles.ts`, `useCycles`, CycleView + dialogs, CyclePicker, menu wiring, group-builder
extension); 1 new aggregate + 9 endpoints (8 cycle/preference + 1 task PATCH); ~25–30 new
integration facts, ~12–15 unit specs, 1 new E2E spec + axe/[V] additions; TaskResponse +
flattened rows widened (`cycleId`, `carriedOver`)

## Constitution Check

*GATE: evaluated pre-Phase-0 and re-checked post-Phase-1 — PASS (no violations; Complexity
Tracking empty). Constitution v5.1.0.*

| Principle | Verdict | How the design complies |
|---|---|---|
| I. UI-First Operability | PASS | Every operation visible: sidebar „Cykl" entry (FR-017), `/cycle` lifecycle buttons/dialogs, „Cykl…" in the shared "⋯" menu, group-by „Cykl" toggle, settings field. `#`/`G C` stay deferred (OOS-20); nothing is shortcut-only |
| II. Accessibility (WCAG 2.1 AA) | PASS | Catalog dialogs with the 019 focus contract; metrics as labelled TEXT (never color-only); carried-over as a text chip; overdue as a text badge; grid-pattern rows on the cycle task list; `/cycle` joins axe ×4; LiveRegion announces lifecycle results incl. rollover counts (FR-101) |
| III. Instant Response | PASS | `setTaskCycle` + lifecycle mutations ride the established optimistic factory (snapshot/rollback/409-reapply-once); close is one command with counts in the response; skeletons only on network-bound cycle fetch; remote fan-out transfers to 016 (D14) |
| IV. Minimalist UI | PASS | One management surface (`/cycle`, per Clarifications); no new chrome beyond the sidebar entry, one menu item, one group-by toggle, one settings field |
| V. Connected, Server-Authoritative | PASS | Cycles/assignments/rollover live in own PG via own API; the D8 preference is server-side account data; group-by value reuses the existing device-local localStorage key (presentation state only) |
| VI. Type Safety End-to-End | PASS | CycleStatus enum + CycleId typed end-to-end; EF migration is schema truth; new operationIds + error codes through the document transformer → `gen:api` → `openapi-sync`; Task.CycleId stays raw `Guid?` (D2 — the slice-005 value-converted-nullable-FK trap avoided); Zod/FluentValidation at the boundaries |
| VII. Data Integrity & Resilience | PASS | Close+rollover atomic (one tx, D4); OCC everywhere; delete guards handler-level with FK RESTRICT backstop (D1); FR-049 recovery copy per error code (incl. `no_next_cycle` prompt); FR-050 structured logs; FR-051 backup posture unchanged ahead of the migration; 30-s undo for the bulk move explicitly deferred to slice 014 (spec known-gap) |
| VIII. Test-First | PASS | Every AS/EC lands as tagged tests; INV rows before tests (019 gate); allow+deny matrices per contract; red repro discipline per docs/bugfix-workflow.md |
| IX. Authentication & Authorization | PASS | Deny-by-default gate on all routes; lifecycle/list = explicit team-wide authorized ops (no per-user scoping — spec IX); `SetTaskCycle` dispatches on the TASK's visibility (owner / membership+role; viewer 403, non-member 404); cycle task rows caller-filtered (D10); overrides on invisible tasks rejected |
| X. Time & Timezone | PASS | All cycle date logic in Europe/Warsaw via existing helpers (FR-092): days remaining, overdue marking, D18 pre-fills; DST across a 2-week span unit-tested; dates stored UTC timestamptz |
| XI. Privacy & Personal Data | PASS | Cycle holds no PII (name/dates/status); NOT in the per-account deletion cascade (FR-085/FR-086 stance per spec XI); the D8 preference column is account data deleted with the user row |
| XII. Security by Default | PASS | Cycle name output-sanitized everywhere it renders (FR-099 — sidebar, switcher, groups, picker, dialogs); no new secrets; CSP/headers untouched |

**Post-design re-check (after Phase 1)**: no artifact introduced a violation — one aggregate,
no new external dependency, no new project, preference stored on the existing user row.
Complexity Tracking empty.

## Project Structure

### Documentation (this feature)

```text
specs/011-cycles/
├── plan.md              # This file
├── research.md          # Phase 0 — decisions D1–D18
├── data-model.md        # Phase 1 — Cycle aggregate, lifecycle, rollover matrix, widenings
├── quickstart.md        # Phase 1 — validation scenarios + commands
├── contracts/
│   ├── cycles-api.md    # lifecycle/list/metrics/preferences API + test matrix
│   ├── task-cycle.md    # PATCH /api/tasks/{id}/cycle + response widening
│   └── ui-cycle.md      # sidebar / Cycle view / picker / grouping / settings UI contract
└── tasks.md             # Phase 2 (/speckit-tasks — NOT created by /speckit-plan)
```

### Source Code (repository root)

```text
apps/web/src/
├── app/(app)/cycle/page.tsx            # NEW — Cycle view route (switcher+metrics+list+lifecycle)
├── app/(app)/settings/…                # + default-duration field (D8)
├── lib/cycles.ts                       # NEW — ordering (D5), days-remaining (Warsaw), group
│                                       #        builder extension, picker options (pure, unit-tested)
├── hooks/
│   ├── useCycles.ts                    # NEW — ["cycles"] list+metrics, ["cycle-tasks", id]
│   └── useTaskMutations.ts             # + setTaskCycle (optimistic factory)
├── components/cycles/                  # NEW — CycleView, CycleMetrics, CycleFormDialog,
│   │                                   #        CloseCycleDialog (review), CyclePicker
├── components/layout/Sidebar.tsx       # + „Cykl" entry (FR-017, overdue badge)
├── components/tasks/TaskRow.tsx        # TaskRowActions.onOpenCycle + „Cykl…" in buildMenuItems
├── components/tasks/GroupedTaskList.tsx# GroupByControl + „Cykl" option
└── tests/  unit/ (cycles lib, picker, menu) · e2e/cycles.spec.ts · axe/[V] additions

apps/api/src/
├── TaskFlow.Domain/TaskManagement/Cycle.cs, CycleId.cs, CycleStatus.cs   # NEW aggregate (D1)
├── TaskFlow.Domain/TaskManagement/Task.cs          # SetCycle(...), CarriedOver + close transitions
├── TaskFlow.Application/TaskManagement/Cycles/     # NEW — commands/queries + ICycleRepository
├── TaskFlow.Application/TaskManagement/SetTaskCycle.cs                    # task-side assignment
├── TaskFlow.Application/IdentityAccess/…           # SetUserPreferences + profile widening (D8)
├── TaskFlow.Infrastructure/Persistence/            # CycleRepository, mappings, AddCycles migration
└── TaskFlow.Api/Endpoints/CycleEndpoints.cs        # NEW; TaskEndpoints + UserEndpoints widened
    TaskFlow.Api/OpenApi/TaskFlowDocumentTransformer.cs   # new operationIds + error codes (D16)

apps/api/tests/TaskFlow.IntegrationTests/Cycles/    # NEW namespace → dedicated ci shard (D15)
.github/workflows/ci.yml                            # + cycles shard; identity-labels-infra complement
specs/019-ui-design-system/feature-inventory.md     # + slice-011 INV section (gate input)
apps/web/src/lib/api/generated/schema.d.ts          # regenerated (openapi-sync)
```

**Structure Decision**: existing web monorepo, no new projects. Cycle joins the TaskManagement
bounded context (same DbContext); the Cycle view is a standard App-Router route on the 019
catalog; all styling co-located CSS Modules on semantic tokens.

## Complexity Tracking

No constitution violations to justify — table intentionally empty.

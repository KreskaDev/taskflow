# Implementation Plan: Project Board (Kanban)

**Branch**: `010-project-board-kanban` | **Date**: 2026-08-10 | **Spec**: `specs/010-project-board-kanban/spec.md`

**Input**: Feature specification from `/specs/010-project-board-kanban/spec.md` (updated
2026-08-10 to constitution v5.1.0 / the product-vision UI-first reinterpretation clause).

## Summary

Add the two project-view projections from US-03 on top of the slice-019 design system: a
**Kanban Board** (four status columns — Backlog/Todo/In Progress/Done — cancelled hidden,
EC-11) and a **groupable List** (group-by status/priority; by-cycle waits for slice 011), with
a per-project last-used mode (List default) persisted client-side. Column moves are visible-UI
operations (drag-and-drop via dnd-kit + card "⋯" menu move actions) issuing ONE status write
over the **widened existing** `PATCH /api/tasks/{id}/status` (command renamed
`SetTaskStatus`; new domain transition preserving the `CompletedAt`-iff-Done invariant).
Prerequisite correctness repair: the project task listing (and its three sibling call sites:
counts, delete-cascade, duplicate-neighbour) becomes **project-scoped instead of
owner-scoped**, closing the FR-066 hole where member-authored tasks were invisible on shared
projects. No new entity, no migration, no new endpoint. Decisions D1–D14: `research.md`.

## Technical Context

**Language/Version**: TypeScript 5.7 (strict) on Next.js 15 App Router / React 19; C# on
.NET 9 (nullable + analyzers-as-errors)

**Primary Dependencies**: web — TanStack Query v5 (established optimistic mutation factories
in `useTaskMutations.ts`), `@dnd-kit/core` (Board cross-column drag; PointerSensor 4px +
KeyboardSensor), `@tanstack/react-virtual` (List), slice-019 `ui/` catalog + `tokens.css`;
api — ASP.NET Core, Wolverine 6.11 (HTTP→bus delegation, public concrete types),
EF Core/Npgsql

**Storage**: PostgreSQL — unchanged; **no migration** (all five `tasks.status` values storable
since slice 002; `completed_at`, `position`, `deleted_at` already in place)

**Testing**: Vitest (board mapping/group builders/persistence hook/menu builder + the 019
inventory-coverage gate — new INV rows), Playwright E2E (board journey UI-driven; board view
joins the axe ×4-palette suite; [V] additions ride 019's pending baseline generation), xUnit +
Testcontainers integration (allow+deny for the widened status write and the scoping repair —
class names chosen to land in the `tasks-core` shard without ci.yml edits, research D12)

**Target Platform**: web, desktop-first ≥768px (chromium-tested); single-origin Next.js BFF →
.NET API

**Project Type**: web monorepo — `apps/web` + `apps/api`

**Performance Goals**: constitution budgets guarded, not newly established — optimistic column
move paints within one frame (SC-003 posture; single PATCH, snapshot/rollback factory);
server-confirmed mutations p95 < 200 ms; board renders from the one existing project query
(no N+1, no new read model)

**Constraints**: CSP intact; tokens-only styling (zero raw hex outside `tokens.css` —
hex-audit has no exemptions); Polish copy only (019 D15); Lucide-only chrome icons; every new
UI behavior needs an `INV-###` row + `[INV-###]`-tagged test or the inventory gate blocks
merge; `Europe/Warsaw` untouched (no date-relative logic owned here); no custom keyboard
shortcuts (OOS-20) — composite-widget arrow navigation + dnd-kit KeyboardSensor are WCAG
operability

**Scale/Scope**: 1 route reworked (`projects/[id]`) + mode switch; ~6 new web modules
(`lib/board.ts`, Board view components ×3–4, persistence hook, group-by control) reusing the
19-component catalog; 1 widened endpoint + 1 repository scoping repair (4 call sites); ~12–16
new integration facts, ~8–10 unit specs, 1 new E2E spec + axe/inventory additions

## Constitution Check

*GATE: evaluated pre-Phase-0 and re-checked post-Phase-1 — PASS (no violations; Complexity
Tracking empty). Constitution v5.1.0.*

| Principle | Verdict | How the design complies |
|---|---|---|
| I. UI-First Operability | PASS | Every operation has a visible affordance: sidebar project entries (AS-01), header mode switch, drag handles + full card "⋯" menu incl. move-left/right (omitted at boundaries — AS-05), group-by control. Nothing is shortcut-only (OOS-20); dnd keyboard path + menu path both non-pointer (D7, D10) |
| II. Accessibility (WCAG 2.1 AA) | PASS | Columns as labelled listboxes with counts; arrow nav via existing `listboxKeys`; Polish dnd announcements; grouped List reuses DailyView's proven `role="group"` pattern; status never color-only; reduced-motion honored; board joins axe ×4 palettes; FR-101 via existing LiveRegion for failed-move toasts; remote-move announcements transfer to 016 (D11 note) |
| III. Instant Response | PASS | Column move = one optimistic PATCH on the established factory (snapshot/rollback/settle + 409 reapply-once); no skeleton masks a move (skeletons only on initial project load) |
| IV. Minimalist UI | PASS | Two projections of one dataset behind one switch; cancelled hidden on Board; grouping on demand; no new chrome beyond the switch + group-by |
| V. Connected, Server-Authoritative | PASS | Task data reads/writes only via own API+PG; view mode/group-by are device-local presentation state (localStorage, D8) — not system-of-record data |
| VI. Type Safety End-to-End | PASS | Widened status flows through the OpenAPI doc → regenerated typed client (`gen:api`, `openapi-sync`); column↔status mapping is one typed module (`lib/board.ts`); no new error codes so `ERROR_UX` union unchanged |
| VII. Data Integrity & Resilience | PASS | No migration (FR-051 untriggered); failed moves roll back with toast+retry (FR-049) and structured logs (FR-050); D4 repair FIXES an existing integrity bug (delete-cascade orphaning member tasks); OCC preserved on the widened write |
| VIII. Test-First | PASS | Every owned AS + EC-11 lands as tagged tests; INV rows written before restyling/behavior tests (gate enforces both directions); allow+deny integration tests per contract |
| IX. Authentication & Authorization | PASS | No authz-model change: reads keep 404-first visibility dispatch + viewer+; the move write keeps `LoadWritableTaskAsync(Editor)` (viewer 403, non-member 404); D4 makes the FR-066 promise actually hold for member-authored tasks; each repair ships allow+deny tests |
| X. Time & Timezone | PASS | No date-relative computation owned; due chips render exactly as the List does today |
| XI. Privacy & Personal Data | PASS | Cards surface existing provenance identifiers only; membership loss removes all Board/List access (FR-066, tested) |
| XII. Security by Default | PASS | Card content renders through the same sanitized text paths as rows (FR-099); CSP/headers untouched; no secrets touched |

**Post-design re-check (after Phase 1)**: no artifact introduced a violation — no new project,
no repository/abstraction layer beyond the corrected repo method signature, no entity, no
external dependency, localStorage holds only presentation state. Complexity Tracking empty.

## Project Structure

### Documentation (this feature)

```text
specs/010-project-board-kanban/
├── plan.md              # This file
├── research.md          # Phase 0 — decisions D1–D14
├── data-model.md        # Phase 1 — SetStatus transition, column mapping, client state
├── quickstart.md        # Phase 1 — validation scenarios + audit commands
├── contracts/
│   ├── task-status.md   # widened PATCH /api/tasks/{id}/status
│   ├── project-tasks.md # project-scoped GET /api/projects/{id}/tasks (+ 3 sibling repairs)
│   └── ui-board-list.md # Board/List UI contract (copy, ARIA, DnD, persistence, INV plan)
└── tasks.md             # Phase 2 (/speckit-tasks — NOT created by /speckit-plan)
```

### Source Code (repository root)

```text
apps/web/src/
├── app/(app)/projects/[id]/page.tsx   # mode switch host; renders List (existing) | Board (new)
├── lib/board.ts                       # NEW — BOARD_COLUMNS, buildBoardColumns, adjacentStatus,
│                                      #        buildProjectGroups (pure, unit-tested)
├── hooks/
│   ├── usePersistedProjectView.ts     # NEW — localStorage view-mode + group-by per project
│   └── useTaskMutations.ts            # setTaskStatus wrapper generalized over list key
├── components/tasks/
│   ├── BoardView.tsx (+ .module.css)  # NEW — DndContext + columns grid
│   ├── BoardColumn.tsx (+ .module.css)# NEW — labelled listbox column + EmptyState
│   ├── BoardCard.tsx (+ .module.css)  # NEW — card on catalog components; reuses buildMenuItems
│   ├── TaskRow.tsx                    # TaskRowActions + buildMenuItems gain moveLeft/moveRight
│   └── GroupedTaskList.tsx            # NEW or DailyView-generalized grouped listbox for FR-024
└── tests/  unit/ (board, groups, persistence, menu) · e2e/board.spec.ts · axe/visual additions

apps/api/src/
├── TaskFlow.Domain/TaskManagement/Task.cs          # SetStatus(TaskStatus, DateTime); Mark* removed
├── TaskFlow.Application/TaskManagement/Commands/   # SetTaskDone.cs → SetTaskStatus.cs (rename+widen)
├── TaskFlow.Application/TaskManagement/Queries/GetProjectTasks.cs  # project-scoped listing (D4)
├── TaskFlow.Application/TaskManagement/{GetViewCounts,DeleteProject,DuplicateTask}.cs  # D4 siblings
├── TaskFlow.Infrastructure/Persistence/TaskRepository.cs  # ListByProjectAsync project-scoped
└── TaskFlow.Api/OpenApi/TaskFlowDocumentTransformer.cs    # operationId setTaskStatus

apps/api/tests/TaskFlow.IntegrationTests/TaskManagement/
├── SetTaskStatusBoardTests.cs         # NEW — allow+deny+transitions (tasks-core shard by name)
└── ProjectScopedTaskListingTests.cs   # NEW — D4 repairs incl. counts/cascade/duplicate

specs/019-ui-design-system/feature-inventory.md    # + slice-010 INV section (gate input)
apps/web/src/lib/api/generated/schema.d.ts         # regenerated (openapi-sync)
```

**Structure Decision**: existing web monorepo, no new projects. The Board is a sibling
projection next to the existing List inside the one project route; all styling co-located CSS
Modules on semantic tokens (019 architecture).

## Complexity Tracking

No constitution violations to justify — table intentionally empty.

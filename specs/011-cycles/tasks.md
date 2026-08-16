# Tasks: Cycles

**Input**: Design documents from `/specs/011-cycles/`
**Prerequisites**: plan.md, spec.md (+ Clarifications 2026-08-16), research.md (D1–D18), data-model.md, contracts/
**Tests**: INCLUDED — the spec mandates test-first (Constitution VIII); allow+deny matrices per contract; red repro before green per docs/bugfix-workflow.md
**Organization**: This slice realizes a single user story — **US-05 Cycle Management & Review** — so the story phase carries the whole web surface; the foundational phase carries the server side (aggregate, migration, API).

## Format: `[ID] [P?] [Story] Description`

## Path Conventions

Web monorepo per plan.md: `apps/web` (Next.js 15) + `apps/api` (.NET 9). Feature docs in `specs/011-cycles/`.

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: No new dependencies; the regression-inventory rows that gate every UI behavior.

- [ ] T001 [P] Append the slice-011 section to specs/019-ui-design-system/feature-inventory.md: new `INV-###` rows (continuing the numbering) for every behavior in contracts/ui-cycle.md — sidebar „Cykl" entry (active name / bare label / „po terminie" badge), `/cycle` switcher + default selection, FR-026 metrics strip (%, days remaining incl. „0 dni (po terminie)", per-status text breakdown, team-wide numbers), caller-visible task rows + „przeniesione" chip + EC-12 archived-project rows, lifecycle affordances (create pre-fills per D18/D8; edit; activate + single-active refusal; close review with three bulk options + per-task overrides + AS-06 prompt + counts toast; delete omitted on active + non-empty refusal), „Cykl…" menu item + CyclePicker (all cycles + „Bez cyklu", current checked), by-cycle grouping („Bez cyklu" LAST, D5 order, persisted `"cycle"` value), settings duration field roundtrip — each row: screen → Given/When/Then → outcome → realized FR/AS → covering level [C]/[E]/[A]/[V] (the inventory gate goes red now, green at slice exit)

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: The entire server side — Cycle aggregate + migration, lifecycle commands with guards, close+rollover transaction, task assignment, metrics/list queries, preference, OpenAPI + typed client, dedicated CI shard.

**⚠️ CRITICAL**: Blocks all of Phase 3 — every web surface consumes these endpoints.

- [ ] T002 [P] Author failing integration tests in apps/api/tests/TaskFlow.IntegrationTests/Cycles/CycleLifecycleTests.cs (NEW namespace `TaskFlow.IntegrationTests.Cycles` — D15) covering contracts/cycles-api.md lifecycle: idempotent create + validation (name required/≤200, both dates, start<end 422; OVERLAP allowed 200), edit in every status + OCC 409, activate happy path / from-active 422 `cycle_not_planned` / second-active 409 `cycle_active_conflict` (plus a direct-DB race fact proving `ix_cycles_single_active` — D3), delete guards (active → 422 `cycle_active_delete_forbidden`; non-empty planned/closed → 422 `cycle_not_empty`; empty planned/closed → 200 hard delete), unauthenticated 401 on every route
- [ ] T003 [P] Author failing integration tests in apps/api/tests/TaskFlow.IntegrationTests/Cycles/CloseCycleRolloverTests.cs covering the D4/D10 close matrix: close from planned/closed → 422 `cycle_not_active`; `rollover:"next"` reassigns ALL incomplete tasks (incl. another user's task the caller cannot see) to the next planned cycle by (StartDate, CreatedAt, Id) and returns counts; `"backlog"` clears assignments; `"keep"` flags `carried_over=true` on incomplete tasks only (done/cancelled untouched); `overrides` apply per-task with the bulk default covering the rest; override on an invisible task → 404 posture, nothing partial; `"next"` with no planned cycle → 422 `no_next_cycle`; transactionality (a failing override rolls back the status flip); close with zero incomplete tasks = pure close
- [ ] T004 [P] Author failing integration tests in apps/api/tests/TaskFlow.IntegrationTests/Cycles/SetTaskCycleTests.cs + CycleReadTests.cs covering contracts/task-cycle.md and the reads: SetTaskCycle owner-personal 200 / other-user 404 / editor 200 / viewer 403 / non-member 404 / former member 404 (FR-066) / unknown cycle 422 / closed cycle 200 (Clarifications) / null clears + `carried_over` cleared on every write / stale version 409; `GET /api/cycles` team-wide metrics counts include OTHER users' tasks, exclude soft-deleted, D5 ordering incl. tiebreaker; `GET /api/cycles/{id}/tasks` caller-visibility filtering (personal-of-other excluded, shared-member included, former member excluded) + archived-project tasks INCLUDED (EC-12); `PATCH /api/users/me/preferences` roundtrip + 1..90 range 422 (D8)
- [ ] T005 Create the Cycle aggregate in apps/api/src/TaskFlow.Domain/TaskManagement/{Cycle.cs, CycleId.cs, CycleStatus.cs} (D1, data-model.md): strongly-typed `CycleId`, `Name` (trimmed 1..200), `StartDate`/`EndDate` UTC with `start < end` invariant, `CycleStatus` Planned/Active/Closed, OCC `Version`, `Activate()` (Planned-only), `Close()` (Active-only), `Rename`/`Reschedule` legal in any status; author the failing domain unit tests in apps/api/tests/TaskFlow.UnitTests/Domain/TaskManagement/CycleTests.cs first
- [ ] T006 Extend Task in apps/api/src/TaskFlow.Domain/TaskManagement/Task.cs: `CarriedOver` (bool, default false) + `SetCycle(Guid? cycleId)` (sets CycleId, ALWAYS clears CarriedOver, `Touch()` + version bump) + internal rollover transitions used by CloseCycle (`RollToCycle(Guid next)`, `RollToBacklog()`, `MarkCarriedOver()` — no version-churn beyond one bump each, data-model.md rollover matrix); `CycleId` STAYS raw `Guid?` (D2 — the slice-005 value-converted-nullable-FK trap); failing unit tests first in apps/api/tests/TaskFlow.UnitTests/Domain/TaskManagement/TaskTests.cs
- [ ] T007 Persistence in apps/api/src/TaskFlow.Infrastructure/Persistence/: `CycleRepository` + `ICycleRepository` (in TaskFlow.Application/TaskManagement/Cycles/), EF mapping for `cycles` (text status conversion like TaskStatus), and ONE migration `AddCycles`: `cycles` table, `ix_cycles_single_active` UNIQUE `((1)) WHERE status='active'` (`HasFilter`), ordering index `(start_date, created_at, id)`, FK `tasks.cycle_id → cycles(id) ON DELETE RESTRICT`, `tasks.carried_over boolean NOT NULL DEFAULT false`, `users.cycle_default_duration_days int NOT NULL DEFAULT 14` (D1/D3/D7/D8; translate `DbUpdateException` for the new aggregate per the slice-006 lesson)
- [ ] T008 Cycle commands in apps/api/src/TaskFlow.Application/TaskManagement/Cycles/ (Wolverine conventions: public concrete types, HTTP→bus delegation): `CreateCycle` (idempotent PUT), `EditCycle`, `ActivateCycle` (handler single-active check + friendly `cycle_active_conflict`; the partial index wins races), `DeleteCycle` (guards → `cycle_active_delete_forbidden`/`cycle_not_empty`), all team-wide authorized (authenticated+admitted only — spec IX) with OCC; FluentValidation per contracts/cycles-api.md
- [ ] T009 `CloseCycle` in apps/api/src/TaskFlow.Application/TaskManagement/Cycles/CloseCycle.cs (D4/D5/D10): Active-only; resolves next planned by (StartDate, CreatedAt, Id) when needed (`no_next_cycle` otherwise); applies bulk default to EVERY incomplete task (team-wide) + caller-visible `overrides` (invisible override → 404 posture, whole command rejected); flags/clears `carried_over` per the data-model matrix; ONE transaction with the status flip; response carries `{rolledToNext, rolledToBacklog, kept}`
- [ ] T010 Queries + task assignment: `GetCycles` (all cycles + computed team-wide metrics in one grouped count query — D6, no N+1) and `GetCycleTasks` (caller-visibility filter per FR-065 dispatch + EC-12 archived-project inclusion) in apps/api/src/TaskFlow.Application/TaskManagement/Cycles/; `SetTaskCycle` in apps/api/src/TaskFlow.Application/TaskManagement/SetTaskCycle.cs on the `LoadWritableTaskAsync(Editor)` guard (SetPriority pattern; unknown cycle 422); widen `TaskResponse` AND the flattened Today/Upcoming/Assigned row shapes with `cycleId` + `carriedOver` (D16 — the slice-006 flattening gap must NOT recur); `SetUserPreferences` + widened `GET /api/users/me` in apps/api/src/TaskFlow.Application/IdentityAccess/ (D8)
- [ ] T011 Endpoints + OpenAPI: new apps/api/src/TaskFlow.Api/Endpoints/CycleEndpoints.cs (PUT/PATCH×3/DELETE/GET×2 per contracts/cycles-api.md), `PATCH /api/tasks/{id}/cycle` in TaskEndpoints.cs, `PATCH /api/users/me/preferences` in UserEndpoints.cs — all behind the deny-by-default authenticated gate; extend apps/api/src/TaskFlow.Api/OpenApi/TaskFlowDocumentTransformer.cs with the nine operationIds (path strings EXACTLY matching routes — silent no-op on typo) + new error codes `no_next_cycle`, `cycle_not_planned`, `cycle_active_conflict`, `cycle_not_active`, `cycle_not_empty`, `cycle_active_delete_forbidden` (D16)
- [ ] T012 Add the dedicated CI shard in .github/workflows/ci.yml (D15): new matrix entry `cycles` with filter `FullyQualifiedName~IntegrationTests.Cycles`, and append `&FullyQualifiedName!~IntegrationTests.Cycles` to the `identity-labels-infra` complement — every class still lands in exactly one shard
- [ ] T013 Run `dotnet test` in apps/api — T002–T006 suites green (red→green), all prior suites untouched; boot PG+API per quickstart.md, run `pnpm --dir apps/web gen:api`, commit the regenerated apps/web/src/lib/api/generated/schema.d.ts (`openapi-sync` guards it); map the six new error codes into the web `ERROR_UX` union with Polish copy in apps/web/src/lib/api/errors.ts (or the established location)

**Checkpoint**: Server owns cycles end-to-end — lifecycle with guards, atomic rollover, team-wide metrics, visibility-scoped rows, widened task responses, typed client regenerated.

## Phase 3: User Story 5 — Cycle Management & Review (Priority: P3) 🎯 MVP

**Goal**: A member assigns tasks to cycles from the shared "⋯" menu, navigates via the sidebar to the `/cycle` management surface (metrics, switcher, lifecycle), closes a finished cycle through the review with rollover, and groups the project List by cycle.

**Independent Test**: quickstart.md manual walk steps 1–8 (create→activate→assign→metrics→close/rollover→AS-06 prompt→delete guards→settings pre-fill).

**Test-first (Constitution VIII)**: every unit-testable task authors its failing spec first; the E2E journey lands with the surfaces it drives.

- [ ] T014 [P] [US5] Create apps/web/src/lib/cycles.ts (pure — unit-tested first in apps/web/tests/unit/cycles.test.ts): `orderCycles` (StartDate, CreatedAt, Id — D5), `daysRemaining(endDate, now)` in Europe/Warsaw via apps/web/src/lib/timezone.ts incl. the overdue → 0 case and a DST-crossing 2-week fact (FR-092), `daysRemainingLabel` with Polish pluralization („1 dzień" / „2 dni" / „0 dni (po terminie)"), `cycleStatusLabel` (aktywny/planowany/zamknięty), `buildCycleGroups(tasks, cycles)` for the by-cycle grouping — groups in D5 order, labels = cycle names, **„Bez cyklu" LAST** (EC-10), empty groups omitted; `buildCyclePickerOptions(cycles, currentCycleId)` (all cycles + „Bez cyklu", current checked)
- [ ] T015 [P] [US5] Create apps/web/src/hooks/useCycles.ts: `["cycles"]` list+metrics query and `["cycle-tasks", id]` query; lifecycle mutations (create/edit/activate/close/delete) on the established optimistic/settle pattern with FR-049 toasts + FR-101 LiveRegion announcements (close announces the returned counts) and `logError` (FR-050); extend apps/web/src/hooks/useTaskMutations.ts with `setTaskCycle(taskId, cycleId)` (optimistic factory, snapshot/rollback/409-reapply-once, invalidates view keys + `["cycles"]` + `["cycle-tasks"]`); failing specs first in apps/web/tests/unit/use-cycles.test.ts + use-task-mutations.test.ts
- [ ] T016 [US5] Menu + picker (US-05.AS-01/02): `TaskRowActions.onOpenCycle` + „Cykl…" item in `buildMenuItems` (between „Etykiety…" and „Przenieś do projektu…") in apps/web/src/components/tasks/TaskRow.tsx; new apps/web/src/components/cycles/CyclePicker.tsx (+.module.css) on the PriorityPicker dialog pattern (019 focus contract; options via `buildCyclePickerOptions`; selection fires `setTaskCycle` and closes); wire `onOpenCycle` on EVERY surface that wires labels today: Inbox page, DailyView, project List/Board pages; failing unit specs first (task-row menu item + picker in apps/web/tests/unit/)
- [ ] T017 [P] [US5] Sidebar entry (FR-017) in apps/web/src/components/layout/Sidebar.tsx: „Cykl" entry between the daily views and PROJEKTY navigating to `/cycle`; shows the ACTIVE cycle's sanitized name when one exists, bare „Cykl" otherwise; text badge „po terminie" when the active cycle's end date is past in Warsaw (never color-only — FR-044); failing unit spec first in apps/web/tests/unit/sidebar.test.tsx (or the established sidebar spec file)
- [ ] T018 [US5] The `/cycle` view (US-05.AS-03, FR-020/FR-026, contracts/ui-cycle.md): apps/web/src/app/(app)/cycle/page.tsx + apps/web/src/components/cycles/{CycleView,CycleMetrics,CycleFormDialog}.tsx (+.module.css, tokens-only): switcher in D5 order with status suffixes (default: active → next planned → FR-110 empty state with „Nowy cykl"), metrics strip (% ukończone, „N dni pozostało"/„0 dni (po terminie)", per-status labelled text counts), caller-visible task rows (grid-pattern row catalog; „przeniesione" chip on `carriedOver`), lifecycle buttons (create always; edit; activate on planned; close on active; „Usuń" VISIBLE also on active — invoking it shows the AS-07 refusal „Cyklu nie można usunąć — najpierw go zamknij" (client guard + server 422 backstop); non-empty planned/closed refusal via FR-049 copy), create/edit dialog with D18 pre-fills (name „Cykl N", start=today Warsaw, end=start+preference) + inline `start<end` validation, overdue banner „Cykl dobiegł końca — zamknij go"; skeletons on the network-bound fetch only
- [ ] T019 [US5] The close review (US-05.AS-04/05/06): apps/web/src/components/cycles/CloseCycleDialog.tsx (+.module.css): lists caller-visible INCOMPLETE tasks, bulk choice (Przenieś wszystkie do następnego cyklu / do backlogu / Zostaw w zamkniętym cyklu „przeniesione"), optional per-task overrides („obsłuż pojedynczo"), footer noting the bulk choice also covers other users' tasks, atomic confirm via `closeCycle` mutation, `no_next_cycle` → „Najpierw utwórz nowy cykl" prompt with a „Nowy cykl" action, result toast + polite announcement with counts; failing unit spec first in apps/web/tests/unit/close-cycle-dialog.test.tsx
- [ ] T020 [P] [US5] By-cycle grouping (FR-024 completion, US-03.AS-07 deferred dimension): `GroupByControl` in apps/web/src/components/tasks/GroupedTaskList.tsx gains the „Cykl" toggle (persisted value `"cycle"` in the existing localStorage key; `usePersistedProjectView` accepts it); project page passes `buildCycleGroups` output when selected; update apps/web/tests/unit/grouped-task-list.test.tsx + use-persisted-project-view.test.ts (failing first)
- [ ] T021 [P] [US5] Settings field (FR-015/D8) in apps/web/src/app/(app)/settings/: labelled number input „Domyślna długość cyklu (dni)" (1..90) persisted via the preferences mutation; polite save announcement; failing unit spec first
- [ ] T022 [US5] Author the E2E journey in apps/web/tests/e2e/cycles.spec.ts (`[INV-###]` tags, UI-driven, unique user per test × attempt): sidebar „Cykl" → empty state → create (pre-filled name/dates) → second cycle → activate first (sidebar shows name) → activate second refused (single-active copy) → task „⋯" → „Cykl…" → assign (AS-01/02; one PATCH on the network) → `/cycle` metrics move as a task completes (AS-03) → project List „Grupuj: Cykl" shows the cycle group + „Bez cyklu" last → close with „move all to next" (AS-04/05; counts toast; tasks land in cycle 2) → close cycle 2 with rollover next and no planned cycle → AS-06 prompt → delete guards (active: „Usuń" invoked → refusal message „najpierw go zamknij" per AS-07; non-empty closed: refusal copy; empty planned: deletes) (AS-07/EC-04) → settings duration changes the create pre-fill; plus an overdue-banner fact (seed end date in the past via the API helper) and the EC-12 fact: archive a project (API helper) whose task sits in the cycle → the row STAYS visible on `/cycle`
- [ ] T023 [P] [US5] Add `/cycle` (seeded: cycles + tasks + picker and close dialog OPEN states) to the axe ×4-palette walk in apps/web/tests/e2e/axe.spec.ts — zero WCAG 2.1 AA violations
- [ ] T024 [P] [US5] Add [V] cycle screens to apps/web/tests/e2e/visual.spec.ts (linux-gated as today): `/cycle` empty + seeded × 4 palettes × 3 widths with RELATIVE date seeding (start=today−7, end=today+7 Warsaw → constant „7 dni pozostało") and the absolute date-range text hidden via apps/web/tests/e2e/visual.hide-dev-overlay.css (contract ui-cycle.md determinism note); baselines generate in T027

**Checkpoint**: US-05 fully functional behind visible affordances; inventory gate still red only if any row lacks its tagged test.

## Phase 4: Polish & Cross-Cutting Concerns

- [ ] T025 Run `pnpm --dir apps/web test` and drive the inventory-coverage gate green — every slice-011 `INV-###` row has an `[INV-###]`-tagged covering test and vice versa; reconcile as needed
- [ ] T026 [P] `pnpm --dir apps/web audit:dead` (zero dead modules) + hex audit clean (tokens-only in all new .module.css) + `pnpm --dir apps/web lint` + `typecheck`
- [ ] T027 Regenerate [V] baselines via apps/web/tests/e2e/update-visual-baselines.ps1 (docker; prereqs per the runbook: Debug API build, port 55432 free) — new cycle baselines committed for human approval per palette; existing baselines must NOT drift
- [ ] T028 Run the full suites per quickstart.md: web unit, `dotnet test` (verify the new `cycles` shard filter partitions correctly — D15), full `pnpm --dir apps/web e2e` (incl. axe + tasks/daily/board regression), `gen:api` diff clean
- [ ] T029 Walk quickstart.md manual steps 1–8 against the interactive stack and record the outcome in this file

## Dependencies & Execution Order

### Phase Dependencies

Setup (T001) → Foundational T002–T013 (server; blocks everything web) → US5 T014–T024 → Polish T025–T029.

### Within-Phase Notes

- T002–T004 (red integration suites) land BEFORE T005–T011 turn them green; T012 anytime before T013's full `dotnet test`; T013 last in phase 2.
- T014/T015 unblock T016–T021; T018 needs T015+T014; T019 needs T018's dialogs host; T022 needs T016–T021; T023/T024 after T018.
- T027 after every pixel-affecting task; T028/T029 last.

### Parallel Opportunities

```text
# Phase 2 kickoff (after T001):
T002 ‖ T003 ‖ T004        # three red integration suites, separate files
# After T007 (schema in place): T008 ‖ T010's response widening
# Phase 3 kickoff (after T013):
T014 ‖ T015 ‖ T017 ‖ T021
```

## Implementation Strategy

### MVP = the single story

The slice IS US-05; the foundational phase has no user-visible value alone — the first demoable increment is T018 (view + lifecycle) with T016 (assignment).

### Suggested local commit boundaries

`test(011)` red suites (T002–T004) → `feat(011)` domain+migration (T005–T007) → `feat(011)` commands/queries/endpoints (T008–T011) + `chore(011)` shard (T012) → `test(011)` green + gen:api (T013) → `feat(011)` web lib/hooks (T014–T015) → `feat(011)` menu+picker+sidebar (T016–T017) → `feat(011)` cycle view+review (T018–T019) → `feat(011)` grouping+settings (T020–T021) → `test(011)` E2E/axe/[V] (T022–T024) → polish commits (T025–T029).

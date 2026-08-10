# Tasks: Project Board (Kanban)

**Input**: Design documents from `/specs/010-project-board-kanban/`

**Prerequisites**: plan.md, spec.md, research.md (D1–D14), data-model.md, contracts/
(task-status.md, project-tasks.md, ui-board-list.md), quickstart.md

**Tests**: INCLUDED — the spec mandates test-first (Constitution VIII), allow+deny
integration tests for the widened status write and the D4 scoping repair (Constitution IX),
and every new user-visible behavior joins the slice-019 regression inventory
(`INV-###` rows + `[INV-###]`-tagged tests, research D11) before its covering test lands.

**Organization**: This slice realizes a single user story — **US-03 Project Kanban
Workflow (P2)** — so there is one story phase. The backend correctness repair (D4) and the
widened status write (D1–D3) are foundational: the Board is unbuildable on the
owner-scoped query, so they precede all UI work.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies on incomplete tasks)
- **[Story]**: US3 per spec.md; Setup/Foundational/Polish tasks carry no story label
- Every task names exact file paths

## Path Conventions

Web monorepo per plan.md: `apps/web` (Next.js 15 App Router) + `apps/api` (.NET 9).
No new projects; **no EF migration** this slice (data-model.md).

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Dependency verification + the regression-inventory rows that gate every
UI behavior of this slice (D11: rows BEFORE covering tests — they are the red-list
driving the work, exactly as in 019).

- [X] T001 Verify `@dnd-kit/core` and `@tanstack/react-virtual` are present in apps/web/package.json (both added by earlier slices); add via `pnpm --dir apps/web add` ONLY if missing — no other new dependency this slice (plan Technical Context)
- [X] T002 [P] Append the slice-010 section to specs/019-ui-design-system/feature-inventory.md: new `INV-###` rows (continuing the existing numbering) for every behavior in contracts/ui-board-list.md — mode switch renders + last-used mode persists per project (default Lista), four Board columns render in order with counts, cancelled task absent from Board, drag move issues one status PATCH, card "⋯" menu move-left/right with boundary omission (Done no right / Backlog no left), group-by control with status grouping (kolumny + „Anulowane" last) and priority grouping (P0→P3 + „Bez priorytetu"), group-by persists per project, viewer sees read-only Board (no drag, no move items), failed move rolls back with toast — each row: screen → Given/When/Then → expected outcome → realized FR/AS → covering-test level [C]/[E]/[A] (D11; the inventory-coverage gate goes red now and green at slice exit)

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: The server side of the slice — the D4 project-scoping repair (FR-066) and
the widened `SetTaskStatus` write (D1–D3) — plus the OpenAPI/client sync. Test-first:
T003/T004 are authored and observed RED before T005–T009 turn them green.

**⚠️ CRITICAL**: Blocks all of Phase 3 — the Board renders member-authored tasks and
issues the widened status write, neither of which exists until this phase completes.

- [X] T003 [P] Author failing integration tests in apps/api/tests/TaskFlow.IntegrationTests/TaskManagement/SetTaskStatusBoardTests.cs (namespace `TaskFlow.IntegrationTests.TaskManagement`; class name avoids all `tasks-core` exclusion substrings so it lands in the `tasks-core` shard with no ci.yml edit — D12) covering contracts/task-status.md: allow editor `todo→in_progress` (200, status+version persisted, `completedAt` null), allow `done→in_progress` clears `completedAt`, allow `in_progress→done` stamps `completedAt`, allow same-status idempotent no-op (200, version unchanged), allow owner sets `cancelled` (EC-11 seeding basis), deny viewer 403 `forbidden` with DB unchanged, deny non-member 404 `not_found`, 409 on stale version, 422 on `"doing"`
- [X] T004 [P] Author failing integration tests in apps/api/tests/TaskFlow.IntegrationTests/TaskManagement/ProjectScopedTaskListingTests.cs (same namespace/shard rules — D12) covering contracts/project-tasks.md D4 repairs with member-authored tasks: `GET /api/projects/{id}/tasks` returns a member-authored task to the owner AND to another member (and includes `cancelled` tasks); `GetViewCounts` sidebar count includes member-authored tasks; `DeleteProject` cascade disposes member-authored tasks (no orphans); `DuplicateTask` duplicate lands adjacent in the full project ordering; deny: non-member listing 404
- [X] T005 Add `Task.SetStatus(TaskStatus target, DateTime utcNow)` to apps/api/src/TaskFlow.Domain/TaskManagement/Task.cs and remove `MarkDone`/`MarkBacklog` (their only PRODUCTION call sites are the old handler switch arms — D2): same-status → idempotent no-op (no `Touch()`, no version bump); any→Done stamps `CompletedAt = utcNow`; Done→other clears `CompletedAt`; other→other leaves it null; every non-no-op calls `Touch()` (invariant: `CompletedAt` set iff `Status == Done`, data-model.md transition table). Removing the old methods breaks TEST call sites too — migrate them to `SetStatus` in the same task: apps/api/tests/TaskFlow.UnitTests/Domain/TaskManagement/TaskTests.cs (~6 calls; rewrite the transition tests as a table-driven suite over the data-model.md transition table), apps/api/tests/TaskFlow.IntegrationTests/Infrastructure/SharingTestBase.cs:99, and the seeding call in apps/api/tests/TaskFlow.IntegrationTests/TaskManagement/SetTaskDoneTests.cs:75 — edit test BODIES only, NEVER the class names `SetTaskDoneTests`/`SetTaskDoneSharedAuthzTests` (load-bearing in ci.yml shard filters — D12)
- [X] T006 Rename apps/api/src/TaskFlow.Application/TaskManagement/Commands/SetTaskDone.cs → SetTaskStatus.cs: command/handler/validator renamed `SetTaskDone` → `SetTaskStatus`; validator accepts `Status ∈ {backlog, todo, in_progress, done, cancelled}` (422 otherwise — D3) and `Version ≥ 0`; handler switch replaced by one `task.SetStatus(...)` call; authorization unchanged — `TaskAccessGuards.LoadWritableTaskAsync(Id, EffectiveRole.Editor, …)` then in-handler OCC `task.Version != command.Version → VersionConflictException` (contracts/task-status.md); wire DTO `SetTaskStatusRequest` in apps/api/src/TaskFlow.Application/TaskManagement/SetTaskStatusRequest.cs unchanged in name, widened in accepted values if it carries validation
- [X] T007 [P] Make the project listing project-scoped in apps/api/src/TaskFlow.Infrastructure/Persistence/TaskRepository.cs + apps/api/src/TaskFlow.Application/TaskManagement/ITaskRepository.cs: `ListByProjectAsync(projectId, ct)` drops the `ownerId`/`created_by` filter — `project_id = @id AND deleted_at IS NULL`, ordered by `position` (COLLATE "C" byte order) (D4, contracts/project-tasks.md)
- [X] T008 Repair the four consumers of the owner-scoped assumption (D4): apps/api/src/TaskFlow.Application/TaskManagement/Queries/GetProjectTasks.cs (listing), apps/api/src/TaskFlow.Application/TaskManagement/Queries/GetViewCounts.cs (shared-project count), apps/api/src/TaskFlow.Application/TaskManagement/DeleteProject.cs (cascade covers member-authored tasks), apps/api/src/TaskFlow.Application/TaskManagement/DuplicateTask.cs (position neighbour over the full project ordering) — query-handler authorization untouched (404-first `FindReadableAsync`, shared-only `RequireRole(Viewer)`)
- [X] T009 Update the OpenAPI transformer in apps/api/src/TaskFlow.Api/OpenApi/TaskFlowDocumentTransformer.cs: operationId `setTaskDone` → `setTaskStatus` for `PATCH /api/tasks/{id}/status` — verify the `SetOperation` path string EXACTLY matches the route (it silently no-ops on a typo — D13); no new error codes, so the web `ERROR_UX` union stays untouched
- [X] T010 Run `dotnet test` in apps/api — T003/T004 green, existing `SetTaskDoneTests`/`SetTaskDoneSharedAuthzTests` green UNCHANGED (their names are load-bearing in CI shard filters — D12); then boot PG+API per quickstart.md, run `pnpm --dir apps/web gen:api`, and commit the regenerated apps/web/src/lib/api/generated/schema.d.ts (CI `openapi-sync` diffs it — D13)

**Checkpoint**: Server accepts all five statuses through one command, shared-project
listings are project-scoped, typed client is in sync — UI work can begin

---

## Phase 3: User Story 3 — Project Kanban Workflow (Priority: P2) 🎯 MVP

**Goal**: A member opens a project from the sidebar, lands in the last-used mode
(List default), sees the four-column Board (cancelled hidden — EC-11), moves cards
between columns by drag-and-drop or the card "⋯" menu (boundary moves omitted —
AS-05), and groups the List by status or priority (AS-07; by-cycle waits for slice 011).

**Independent Test**: Create a project, add tasks with different statuses (incl. one
`cancelled` via API), open the project Board view, and move tasks between columns via
drag-and-drop and the card menu; regroup the List; reload to confirm mode/group-by
persistence (spec Independent Test + quickstart Scenarios 1–3).

**Test-first (Constitution VIII)**: every unit-testable task below authors its failing
`[INV-###]`-tagged spec FIRST, then implements to green (Red-Green-Refactor).

- [X] T011 [P] [US3] Create apps/web/src/lib/board.ts (pure module — data-model.md): `BOARD_COLUMNS` = backlog/„Backlog", todo/„Do zrobienia", in_progress/„W toku", done/„Zrobione" (cancelled NEVER a column); `buildBoardColumns(tasks)` → 4 columns filtered of `cancelled`, within-column order = `position` rank; `adjacentStatus(status, direction)` → neighbour or `null` at boundary; `buildProjectGroups(tasks, groupBy)` for `none|status|priority` — status groups in column order + „Anulowane" last when non-empty, priority groups P0→P3 then „Bez priorytetu" (reuse `priorityRank()` from apps/web/src/lib/dailyViews.ts), within-group order = flat order, empty groups omitted; failing spec first in apps/web/tests/unit/board.test.ts
- [X] T012 [P] [US3] Create apps/web/src/hooks/usePersistedProjectView.ts (D8): view mode per project over `localStorage["taskflow.project-view.<projectId>"]` (`list`|`board`, default `list`) and group-by over `localStorage["taskflow.project-groupby.<projectId>"]` (`none`|`status`|`priority`, default `none`); hydration-safe — state initializes to the default, an effect swaps to the stored value after mount (no SSR mismatch); invalid stored values fall back to defaults; failing spec first in apps/web/tests/unit/use-persisted-project-view.test.ts
- [X] T013 [US3] Extend the shared action architecture in apps/web/src/components/tasks/TaskRow.tsx (D7): `TaskRowActions` + `buildMenuItems` gain optional `moveLeft`/`moveRight` actions („Przenieś w lewo" / „Przenieś w prawo", Lucide `ChevronLeft`/`ChevronRight`), **omitted** (not disabled) when the callback is absent — the caller maps presence via `adjacentStatus` (T011) so Done offers no right move and Backlog no left (AS-05); the full standard action set stays intact; update apps/web/tests/unit/task-row.test.tsx with the new menu items + boundary-omission cases (failing first)
- [X] T014 [P] [US3] Generalize the status mutation in apps/web/src/hooks/useTaskMutations.ts: the `setTaskStatus` wrapper accepts all five statuses and works over the project list cache key (`["projects", id, "tasks"]`) on the established optimistic factory (snapshot/rollback/settle + 409 refetch-and-reapply-once); optimistic update also routes through `applyTaskToViewCaches` and calls `invalidateViewCounts()` after settle (data-model.md cache interactions; verify counts semantics vs `GetViewCounts` while implementing); failed move surfaces the established toast + retry (FR-049) and `logError` structured context (FR-050); update apps/web/tests/unit/use-task-mutations.test.ts (failing first)
- [X] T015 [US3] Implement the groupable List (FR-024, D9): apps/web/src/components/tasks/GroupedTaskList.tsx — NEW or a generalization of the DailyView grouped-listbox pattern (one `role="listbox"`, `role="group"` + `aria-label` per group, flat index across groups, virtualization preserved or consciously bypassed exactly as apps/web/src/components/tasks/DailyView.tsx does today) rendering `buildProjectGroups` output; plus the visible group-by control „Grupuj: Brak | Status | Priorytet" (by-cycle NOT offered — slice 011); failing spec first in apps/web/tests/unit/grouped-task-list.test.tsx
- [X] T016 [US3] Create apps/web/src/components/tasks/BoardCard.tsx + BoardCard.module.css: card on catalog components only — sanitized title text (FR-099 posture: no raw-HTML render path), optional due chip (renders exactly as the List does — no new date logic), priority chip, label chips, assignee avatars; hover/focus quick-action zone + full „⋯" menu via the shared `buildMenuItems` incl. move actions (T013); visible focus indicator (FR-042); tokens-only styling (hex-audit has no exemptions); failing spec first in apps/web/tests/unit/board-card.test.tsx
- [X] T017 [US3] Create apps/web/src/components/tasks/BoardColumn.tsx + BoardColumn.module.css: heading with Polish label + live count; `role="listbox"` with `aria-label="<label>, N zadań"` (D10); arrow navigation within the column reusing apps/web/src/lib/listboxKeys.ts; Tab moves between columns; empty column renders the catalog `EmptyState` (hint + action — FR-110); status conveyed by heading text, never color alone (FR-044); failing spec first in apps/web/tests/unit/board-column.test.tsx
- [X] T018 [US3] Create apps/web/src/components/tasks/BoardView.tsx + BoardView.module.css: `DndContext` with PointerSensor (4px activation) + KeyboardSensor, Polish `announcements` on dnd-kit's live region (D10); columns grid from `buildBoardColumns`; drop on another column → ONE optimistic `PATCH /status` via T014 (status only, `position` untouched — D6); `DragOverlay` in a portal at `var(--z-menu)`; drop animation disabled under `prefers-reduced-motion` (FR-047); viewer role → read-only board (no drag activation, no move menu items — server still enforces 403); failing spec first in apps/web/tests/unit/board-view.test.tsx
- [X] T019 [US3] Rework apps/web/src/app/(app)/projects/[id]/page.tsx: visible segmented mode switch **„Lista" | „Tablica"** (Lucide `List`/`LayoutGrid`, keyboard-operable per segmented-control ARIA pattern or two `aria-pressed` toggle buttons — contracts/ui-board-list.md) in the project view header; renders existing List (with T015 grouping + group-by control) or new BoardView per `usePersistedProjectView` (T012 — last-used mode, default Lista; AS-02); skeletons only on initial project load, never masking a move (Constitution III/IV); pickers already mounted on the page stay available to the card menu (D7)
- [X] T020 [US3] Author the E2E board journey in apps/web/tests/e2e/board.spec.ts (`[INV-###]` tags; UI-driven per 019 posture): the journey ENTERS the project by clicking its visible sidebar entry (explicit AS-01 assertion — project reachable from the sidebar, not by direct URL); four columns render with counts (AS-03); drag `todo→in_progress` paints optimistically and Network shows ONE `PATCH /api/tasks/{id}/status` (AS-04); menu „Przenieś w prawo"/„w lewo" moves + item absent in Zrobione/Backlog (AS-05/06); mode persists across reload per project, different project defaults to Lista (AS-02); group-by status shows „Anulowane" group with an API-seeded cancelled task (via the `apiAs` helper) while Tablica shows it NOWHERE (EC-11, AS-07); priority grouping P0→P3 + „Bez priorytetu"; viewer sees read-only board (no drag, no move items); done-move check state coherent in List and Board (quickstart Scenarios 1–3)
- [X] T021 [P] [US3] Add the Board view to the per-palette accessibility walk in apps/web/tests/e2e/axe.spec.ts: board screen × 4 palettes, zero WCAG 2.1 AA violations (plan [A] suite)
- [X] T022 [P] [US3] Add Board and grouped-List screens to the `SCREENS` list in apps/web/tests/e2e/visual.spec.ts as [V] specs NOW (the spec `test.skip`s on `process.platform !== "linux"`, so this is inert on Windows): baselines generate ONLY when 019's pending baseline generation (019 T066) runs — one shared regeneration covers both slices, and the `web-e2e` CI job stays red-blocked on T066 exactly as it is today for 019 (conscious acceptance; quickstart exit criteria; do not run the baseline update script here)

**Checkpoint**: US-03 fully functional — Board + groupable List, persistence, authz-scoped;
all owned AS + EC-11 covered by tagged tests

---

## Phase 4: Polish & Cross-Cutting Concerns

**Purpose**: Gates green, no dead code, full-suite verification, manual validation.

- [ ] T023 Run `pnpm --dir apps/web test` and drive the inventory-coverage gate green: every slice-010 `INV-###` row from T002 has an `[INV-###]`-tagged covering test and every tag has a row (typo guard) — reconcile rows/tags as needed (D11)
- [ ] T024 [P] Run `pnpm --dir apps/web exec knip` (zero dead modules) and confirm the hex audit passes with no new exemptions (tokens-only styling in all new .module.css)
- [ ] T025 Run the full suites per quickstart.md: `pnpm --dir apps/web test`, `dotnet build apps/api/src/TaskFlow.Api -c Debug` + `pnpm --dir apps/web e2e`, `dotnet test` in apps/api — all green; re-check the `tasks-core` shard capacity after the new container-per-fact tests (D12: budget ~12–16 new facts; verify the complement filters still partition correctly)
- [ ] T026 Walk quickstart.md Scenarios 1–4 against the interactive stack (`node apps/web/dev-run.mjs`): Board rendering + moves, last-used mode, groupable List + cancelled, authorization matrix (viewer read-only + forged PATCH 403, non-member 404, editor full rights, D4 owner-sees-member-task repair incl. sidebar count)

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — start immediately
- **Foundational (Phase 2)**: After Setup; T003/T004 (RED) before T005–T009 (GREEN); T010 last — **BLOCKS Phase 3** (Board needs the project-scoped listing + widened write + regenerated client)
- **US3 (Phase 3)**: After Phase 2. Within the phase: T011/T012/T014 independent starters; T013 needs T011; T015 needs T011; T016 needs T013; T017 needs T016; T018 needs T014+T016+T017; T019 needs T012+T015+T018; T020 needs T019; T021/T022 need T019
- **Polish (Phase 4)**: After Phase 3; T023 needs T020–T022 landed; T024 parallel with T023; T025 after T023/T024; T026 last

### Within-Phase Notes

- T002's INV rows intentionally precede their covering tests (D11) — the coverage gate is red from T002 until T023; do not "fix" it early by deleting rows
- Existing `SetTaskDoneTests`/`SetTaskDoneSharedAuthzTests` are never renamed (D12 — shard-filter load-bearing)

### Parallel Opportunities

- Phase 1: T001 ∥ T002
- Phase 2: T003 ∥ T004 (both RED first); then T005→T006 ∥ T007→T008; T009 after T006
- Phase 3: T011 ∥ T012 ∥ T014 as soon as Phase 2 completes; T021 ∥ T022 once T019 lands
- Phase 4: T023 ∥ T024

## Parallel Example: Phase 3 kickoff

```bash
# After T010 (checkpoint green), launch together:
Task: "T011 lib/board.ts + board.test.ts (pure mapping/group builders)"
Task: "T012 usePersistedProjectView.ts + use-persisted-project-view.test.ts"
Task: "T014 useTaskMutations.ts setTaskStatus generalization + spec update"
```

---

## Implementation Strategy

### MVP = the single story

US-03 is this slice's only story; the MVP increment is Phases 1–3 complete with
Phase 4 gates green. There is no smaller shippable cut: the Foundational repair (D4)
alone changes shared-project semantics and MUST NOT ship without its allow+deny tests
(they land in the same phase).

### Incremental checkpoints

1. Phase 2 checkpoint — server contract complete, client regenerated: the app still
   behaves identically (List unchanged, done-toggle works through the widened command);
   safe local commit point
2. Phase 3 checkpoint — full US-03 behavior; E2E + axe green
3. Phase 4 — inventory gate green, knip/hex clean, full suites + quickstart walk;
   slice exit per quickstart.md Exit criteria

### Suggested local commit boundaries

Conventional commits per phase (or logical task group): after T002 (inventory rows),
T010 (api + regenerated client), T019 (UI wired), T022 (test suites), T026 (validation).
Push/merge only on explicit user approval.

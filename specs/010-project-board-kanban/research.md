# Research: Project Board (Kanban) — slice 010

**Spec**: `spec.md` (updated 2026-08-10, constitution v5.1.0) | **Phase 0 output**

All unknowns resolved. Facts below were verified against the codebase on branch
`010-project-board-kanban` (stacked on 019 @ `657ba64`); file references are current.

## D1 — Status write path: widen the EXISTING `PATCH /api/tasks/{id}/status`

**Decision**: Reuse the existing route (one status write in the system), widening it to the
full enum. Rename the Application command `SetTaskDone` → `SetTaskStatus` (handler, validator
with it) and the operationId `setTaskDone` → `setTaskStatus`.

**Rationale**: The endpoint, wire DTO (`SetTaskStatusRequest { status, version }`), OCC
guard, editor-role dispatch (`TaskAccessGuards.LoadWritableTaskAsync(…, EffectiveRole.Editor)`)
and ProblemDetails mapping already exist and match FR-067 exactly — only the accepted value
set and the domain transition are missing. The TS client is path-based (openapi-fetch), so the
operationId rename does not ripple into `apps/web`. Existing integration-test class names
(`SetTaskDoneTests`, `SetTaskDoneSharedAuthzTests`) are load-bearing in the CI shard filters
and are NOT renamed (D12).

**Alternatives considered**: a new `POST /api/tasks/{id}/move-column` endpoint — rejected:
duplicates authz/OCC plumbing, splits the status write in two, and the column→status mapping
is a UI concern (FR-025) that the server should not own twice.

## D2 — Domain transition: one `Task.SetStatus(TaskStatus, DateTime)`

**Decision**: Add a single transition method preserving the invariant **`CompletedAt` is set
iff `Status == Done`** (stamp on entering Done, clear on leaving), calling `Touch()`
(UpdatedAt + Version++). `MarkDone`/`MarkBacklog` are removed; their only call sites are the
two arms of the old handler switch.

**Rationale**: Five statuses × pairwise moves make per-pair methods (MarkTodo, MarkInProgress…)
noise; the invariant is the only rule (`Task.cs:84` documents it). A same-status call is a
no-op that still bumps Version? — NO: same-status is treated as idempotent no-op (no Touch),
matching the existing `MarkDone` guard (`if (Status == TaskStatus.Done) return;`).

**Alternatives**: keep MarkDone/MarkBacklog and add three more Mark* — rejected (surface area,
identical invariant logic in five places).

## D3 — Accepted wire statuses: all five; no "cancel" UI affordance this slice

**Decision**: The validator accepts `backlog | todo | in_progress | done | cancelled`. The
Board UI offers only the four column statuses; no UI control sets `cancelled` in this slice.

**Rationale**: FR-003 (slice 002) declares all five storable; the server capability follows the
entity contract, and EC-11 (cancelled hidden on Board, visible in List) becomes end-to-end
testable by seeding a cancelled task through the real API (E2E `apiAs` helper) instead of a DB
poke. No FR assigns a user-facing "cancel task" operation to this slice, so none is added
(FR-103 governs operations the product offers — the product does not offer cancel yet).

**Alternatives**: four statuses only — rejected: EC-11 would be untestable over the real API
and slice 012 (recurring: FR-010 stops generation on cancelled) would re-open the contract.

## D4 — Fix the owner-scoped project listing (prerequisite correctness repair)

**Decision**: Replace the owner-scoped `TaskRepository.ListByProjectAsync(projectId, ownerId)`
with a project-scoped listing (`project_id == id AND deleted_at IS NULL`), and repair ALL four
consumers of the owner-scoped assumption: `GetProjectTasks` (`GetProjectTasks.cs:77-79`),
`GetViewCounts` (`GetViewCounts.cs:120`), `DeleteProject` (`DeleteProject.cs:152`),
`DuplicateTask` (`DuplicateTask.cs:178`). Each fix ships with a member-authored-task test.

**Rationale**: On a shared project, tasks authored by non-owner members are invisible to these
paths (the in-code comment deferred this to slice 008, which never closed it). This violates
FR-066 verbatim ("Access to a shared project's data MUST require current membership… " — and
conversely membership grants access to the project's data, not the owner's slice of it). A
Kanban board over a shared project is unbuildable on the broken query; `DeleteProject` is a
data-integrity bug (cascade misses member-authored tasks and orphans them). This is a repair
of slices 007/008's stated model, not new scope — no new requirement is invented.

**Alternatives**: fix only `GetProjectTasks` — rejected: leaves the delete-cascade integrity
bug and undercounted sidebar counts inconsistent with the now-correct Board/List.

## D5 — Board read: reuse `GET /api/projects/{id}/tasks`; group client-side; no new index

**Decision**: No new read endpoint and no server-side board read model. The existing project
tasks query (after D4) serves both List and Board; the client groups by `status` into columns.
Cancelled is filtered out **client-side on the Board only** (EC-11); the List continues to show
cancelled tasks. No new DB index.

**Rationale**: `TaskResponse` already carries `status`, `position`, `priority`, labels,
assignees — everything a card needs. One cache key (`["projects", id, "tasks"]`) keeps the
optimistic factory pattern and the future slice-016 reconciliation single-sourced. EC-11 is a
Board *display* rule while the List must keep cancelled reachable — same data, two projections,
so filtering server-side would force two endpoints. At ~10-user scale with the existing
`project_id` FK index, a `(project_id, status, position)` index is premature (YAGNI; revisit in
slice 016 if fan-out changes read patterns).

## D6 — Drag semantics: cross-column drag changes STATUS ONLY

**Decision**: Dropping a card on another column issues one `PATCH /status` (D1). The card's
`position` is untouched; within a column, cards render in the same order as the List (position
rank). Intra-column drag-reorder is NOT offered on the Board this slice (reorder lives in the
List via the established handle/menu, FR-102 — already shipped).

**Rationale**: US-03.AS-04..06 require column moves only. A drop that also rewrites `position`
would need two round-trips with two version bumps over the two existing single-field endpoints
(`/status`, `/position`) — a compound-write design (new endpoint or transactional pairing) that
no acceptance scenario demands. Status-only keeps optimistic rollback single-recipe.

**Alternatives**: combined status+position write — deferred; if a later slice needs "drop
between two cards", it adds a compound command then.

## D7 — Board card affordances: reuse the TaskRow action architecture

**Decision**: `BoardCard` reuses the `TaskRowActions` interface + `buildMenuItems` builder
(`TaskRow.tsx:38-120`), extended with two optional actions: `moveLeft` / `moveRight`
("Przenieś w lewo" / "Przenieś w prawo"), omitted (not disabled) at the boundary columns
(AS-05: Done offers no further-right move). The full standard action set (toggle, edit,
priority, due, labels, move-to-project, assign, duplicate, details, delete) stays available on
the card menu — the pickers are already mounted on the project page.

**Rationale**: FR-108 requires the complete "⋯" menu on the item surface; reusing the builder
keeps one source of truth for menu order/copy and the inventory tests that cover it.

## D8 — Mode switch + "last-used mode": per-project `localStorage`, default List

**Decision**: A visible List | Board segmented switch in the project view header. Persistence:
new `usePersistedProjectView(projectId)` hook over `localStorage`
(key `taskflow.project-view.<projectId>`, values `list` | `board`; the group-by choice D9 is
stored alongside as `taskflow.project-groupby.<projectId>`). Default on first visit: `list`
(continuity with the shipped view). Hydration-safe: state initializes to the default and an
effect swaps to the stored value after mount (brief default flash accepted; no SSR mismatch).

**Rationale**: US-03.AS-02 requires "last-used mode". `apps/web` has ZERO existing preference
persistence (verified — the only storage API hit in src is the proxy session cookie), so this
slice establishes the mechanism. A server-side preference store would be a new entity + API for
a presentation concern — Principle V governs *data*, not view state; constitution's
server-authoritative clause is about the system of record, and view mode is device-local UX
(same posture as the slice-018 theming plan). localStorage is the minimal mechanism.

**Alternatives**: cookie (adds SSR read but couples the BFF to UI prefs), server profile
(new entity/migration for no data requirement) — both rejected.

## D9 — List group-by: client-side, reusing the dailyViews/DailyView pattern

**Decision**: Group-by control on the List view: `Grupuj: brak | status | priorytet`
(by-cycle NOT offered until slice 011 — spec scope note). Grouping is client-side over the
same query: a pure `buildProjectGroups(tasks, groupBy)` module (unit-tested like
`dailyViews.ts`), rendered with the `DailyView` grouped-listbox pattern (`role="group"` blocks
inside the listbox, flat index across groups). Within-group order preserves the flat list's
position order. Status group order: Backlog, Todo, In Progress, Done, Cancelled; priority
order: P0→P3 then "Bez priorytetu" (reuse `priorityRank()`, `dailyViews.ts:41-54`). Empty
groups are omitted (flat data view, not a board).

**Rationale**: FR-024 is a read-side projection; `buildTodayGroups`/`buildUpcomingGroups` are
the direct precedent, and reusing the grouped-listbox ARIA structure keeps the a11y contract
already proven by axe in 019.

## D10 — Board accessibility contract

**Decision**: The Board is a composite widget: each column is a `role="listbox"` with
`aria-label` "«column name», N zadań" (count included), cards are options; arrow navigation
within a column reuses `listboxKeys.ts`; Tab moves between columns (and the rest of the page).
Drag-and-drop uses dnd-kit sensors (PointerSensor with the 4px activation constraint +
KeyboardSensor) with **Polish `announcements`** wired to dnd-kit's own live region; the menu
move actions (D7) are the guaranteed non-drag path (FR-103/FR-046). Status is conveyed by
column heading text, never color alone (FR-044). Reduced motion: drop animation disabled under
`prefers-reduced-motion` (FR-047). A failed/rolled-back move announces via the existing
persistent `LiveRegion` toast path (FR-101); remote-member move announcements transfer to
slice 016 (D11-transfer note, per spec).

**Rationale**: dnd-kit KeyboardSensor lift/move/drop is WCAG operability inside a composite
widget, not a custom shortcut system (constitution Principle I explicitly keeps composite-widget
arrow behavior in scope).

## D11 — Regression inventory: new INV rows in the slice-019 inventory (the living gate)

**Decision**: Every new user-visible behavior (mode switch + persistence, four columns render,
drag move, menu move-left/right + boundary omission, group-by control + both groupings,
cancelled hidden on Board / visible in List, viewer sees no move affordances) gets an
`INV-###` row appended to `specs/019-ui-design-system/feature-inventory.md` (new section for
slice 010) BEFORE the covering tests land, each tagged `[INV-###]` in the test title.

**Rationale**: The inventory-coverage gate (`inventory-coverage.test.ts`) fails `web-quality`
on any tag without a declared row (typo guard) and any row without a tag — this is the
regression mechanism 019 established for exactly this purpose; new features must join it
(handoff + D10/019).

## D12 — Test placement vs CI shards; container budget

**Decision**: New integration test classes live in
`tests/TaskFlow.IntegrationTests/TaskManagement/` (namespace
`TaskFlow.IntegrationTests.TaskManagement`) with names avoiding ALL 24 excluded substrings of
the `tasks-core` filter (verified list in ci.yml) so they land in `tasks-core` with no ci.yml
edit: `SetTaskStatusBoardTests` (widened transitions + allow/deny), and
`ProjectScopedTaskListingTests` (D4 repairs: member-authored visibility in list/counts/
duplicate/delete-cascade). Existing `SetTaskDoneTests` / `SetTaskDoneSharedAuthzTests` keep
their names and shards. New-fact budget kept lean (~12–16 facts; container-per-fact — shard
capacity re-check is already a T070 gate on 019 and repeats here at slice exit).

**Alternatives**: a new `IntegrationTests.Board` namespace — rejected: falls into the
`identity-labels-infra` complement shard by accident.

## D13 — OpenAPI/client pipeline obligations (mechanical, recorded once)

**Decision/checklist**: transformer entry updated for the renamed operationId (`SetOperation`
for `/api/tasks/{id}/status` — beware: `SetOperation` silently no-ops on a path typo); no new
error codes (403/404/409/422 all exist → `ERROR_UX` map untouched); after API changes run
`gen:api` against a locally booted stack and COMMIT `schema.d.ts` (CI `openapi-sync` diffs it).
`TaskResponse` shape is unchanged (status already a string field), so the web type surface is
stable.

## D14 — Copy: Polish-only, tokens-only, Lucide-only

**Decision**: All new UI copy in Polish per D15/019 ("Lista", "Board" — product noun stays
"Board" as in product vision? NO: visible copy uses "Tablica" for the Board mode label,
"Lista" for list; column headers: "Backlog", "Do zrobienia", "W toku", "Zrobione" — set in one
module next to the status mapping); zero raw hex outside `tokens.css` (hex-audit runs with no
exemptions); icons from Lucide only (e.g. `LayoutGrid`/`List` for the mode switch,
`ChevronLeft`/`ChevronRight` for menu moves). Column headers use the same vocabulary as any
existing status copy in the app (verify during implementation; the status→label map is a
single exported const so tests and UI share it).

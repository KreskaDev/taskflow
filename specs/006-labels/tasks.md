---
description: "Task list for Labels (slice 006)"
---

# Tasks: Labels

**Input**: Design documents from `/specs/006-labels/`
**Prerequisites**: plan.md, spec.md, research.md (R1–R12), data-model.md, contracts/openapi.yaml, quickstart.md (all present)

**Tests**: REQUIRED. Constitution v4.0.0 Principle VIII (Test-First) governs; Principle IX + the governance gate
require **every new/changed data handler to ship an ALLOW and a DENY test**. Test-First is Red-Green-Refactor:
**the test task has a LOWER id than the implementation it covers — write it, watch it fail (RED), then implement.**
For new-type unit tests RED is the **compile failure** (report honestly).

**Organization**: This slice realizes **ENT-04 (Label)** — a **per-user** tag (Tier A, `ownerId`) — and its
**many-to-many** relationship with tasks, plus the **`L` label selector** (US-08.AS-04). It ships **full label CRUD**
(create/list/update=rename+recolor/delete) **and** the per-user apply/remove on a task (`SetTaskLabels`). It ships
**ONE EF migration** (`AddLabels`, two tables: `labels` + `task_labels`) — so **FR-051 backup-before-migration is
LIVE** (the slice-004/007/008 posture; the CI `backup.sh → ef database update → restore-test.sh` gate applies —
confirm, don't re-wire). It reuses the slice-005 `TaskAccessGuards` write path and the slice-004 preset-token
convention **unchanged**, raises **no** domain event (R8), and modifies **no** slice-007 handler (R5). Single user
story (US-08.AS-04, P1).

**Load-bearing design facts** (do NOT mirror slice-008 blindly):
- Labels are **per-user**; `TaskResponse.labels` is **caller-scoped** (each member sees only their own labels on a
  shared task). `task_labels` is a **STANDALONE M:N relation** (NOT `OwnsMany` under `Task`).
- `SetTaskLabels` is **versionless** and **never bumps `Task.Version`**; it is **two-sided** (task write-access AND
  caller owns every label).
- **No clear-on-project-move, no membership-loss cleanup** (FK cascades + double-gating, R5).
- The optimistic `setTaskLabels` `onSettled` does a **labels-ONLY merge**, NOT a whole-object writeback (R11).

**No new error code** (R9): viewer → 403 `forbidden`; not-owned/absent label or task → 404 `not_found`; non-owned
label id / duplicate name / non-preset color / malformed set → 422 `validation_failed`; no session → 401
`unauthenticated`. **No 409** (label ops are versionless).

## Format: `[ID] [P?] [Story] Description`
- **[P]**: parallelizable (different files, no dependency on an incomplete task)
- **[Story]**: `[US1]` for the user-story phase (Setup/Foundational/Polish carry no story label)

## Path Conventions
Monorepo: backend `apps/api/src/<Project>/…`, backend tests `apps/api/tests/<Project>/…`, web `apps/web/src/…`, web tests `apps/web/tests/…`.

---

## Phase 1: Setup (preconditions)

- [ ] T001 [P] Confirm slice-006 preconditions (verify in code, do not infer): there is **NO** `Label` entity/table/column today (`apps/api/src/TaskFlow.Domain/TaskManagement/` has no `Label*.cs`) → a migration is genuinely required; the slice-005 `TaskAccessGuards.LoadWritableTaskAsync(id, EffectiveRole.Editor, …)` exists (the task-side dispatch this slice reuses); the slice-004 preset-token convention exists (`Project.Color`/`Project.Icon` stored as validated token strings); the strongly-typed-id pattern (`TaskId.cs` — `ValueGeneratedNever`, client-generated) is the template for `LabelId`; the standalone-join mapping (`ProjectMembershipConfiguration.cs`) + the composite-value-converted-key mapping (`task_assignees` in `TaskConfiguration.cs`) are the two precedents for `task_labels`; the error codes `forbidden`/`not_found`/`validation_failed`/`unauthenticated` already exist in `TaskFlowDocumentTransformer.cs` + `apps/web/src/lib/api/client.ts` (so reuse leaves `ERROR_UX` exhaustive, R9); the shared `apps/web/src/components/ui/Dialog.tsx`, `TaskRow.tsx`, and `useGlobalShortcuts.ts` (the `E`/`M`/`A` contextual keys) exist to be reused. **Enumerate the `TaskResponse.From` call-sites** to be touched in T015 (verify against the tree): the 9 command handlers (`CreateTask` ×2, `RenameTask`, `EditTask`, `MoveTaskToProject`, `RescheduleDueDate`, `SetPriority`, `Commands/SetTaskDone`, `Commands/ReorderTask`, `SetTaskAssignees`), the 4 list queries (`GetMyTasks`, `GetProjectTasks`, `GetUpcomingTasks`, `GetAssignedToMe`), and **the flattened `TodayTaskResponse`** (`Queries/TodayResponse.cs` — the one the required-param defense does NOT auto-cover). There is **no** single-task `GET /api/tasks/{id}`.
- [ ] T002 [P] Confirm **FR-051 is LIVE** this slice (Principle VII): the CI deploy job already runs `scripts/backup.sh → dotnet ef database update → scripts/restore-test.sh` — this slice adds **one** migration (`AddLabels`) and **confirms** the gate fires; it does **not** re-wire it.

---

## Phase 2: Foundational (the Label aggregate, the relation, migration, repos, the caller-scoped TaskResponse.labels)

**Purpose**: the `Label` aggregate + the `task_labels` relation + its migration (FR-051 LIVE) + the label/task-label
repositories + the **caller-scoped `TaskResponse.labels`** projection on every read path (incl. the flattened Today
DTO) — prerequisites for the US1 verticals. **Note (no event, R8)**: Program.cs message wiring is UNCHANGED — do not
add a `PublishMessage`/queue/handler.

- [ ] T003 [P] **(write first — RED)** Create `apps/api/tests/TaskFlow.UnitTests/Domain/TaskManagement/LabelTests.cs` — `Label.Create(id, owner, name, color?, utcNow)` and `Label.Edit(name, color?, utcNow)`: `name` trimmed-non-empty + ≤50 (over-long throws); **`NameNormalized == name.Trim().ToLowerInvariant()`** set on both create and edit; `color` recorded as-is (preset membership is validated upstream, not in the aggregate); `Edit` re-stamps `UpdatedAt`; **no `Version` field** on the aggregate. Plus `LabelId` round-trip (`New()`/`From(Guid)`). (covers T004/T005.)
- [ ] T004 [P] Create `apps/api/src/TaskFlow.Domain/TaskManagement/LabelId.cs` — `readonly record struct LabelId(Guid Value)` with `New()` (UUIDv7) + `From(Guid)`, mirroring `TaskId` (client-generated, `ValueGeneratedNever`).
- [ ] T005 Create `apps/api/src/TaskFlow.Domain/TaskManagement/Label.cs` — `AggregateRoot<LabelId>` with `{ OwnerId, Name, NameNormalized, Color?, CreatedAt, UpdatedAt }`, the `Create`/`Edit` behaviors (data-model §1), and the EF materialization ctor. **No `Version`** (single-owner; no concurrent-editor conflict). RED via T003.
- [ ] T006 [P] Create `apps/api/src/TaskFlow.Domain/TaskManagement/TaskLabel.cs` — the join entity `{ TaskId, LabelId }` (a pure relation row; no behavior, no surrogate id — composite key set in the mapping, data-model §1).
- [ ] T007 Create `apps/api/src/TaskFlow.Infrastructure/Persistence/Configurations/LabelConfiguration.cs` — table `labels`; `Id`/`OwnerId` value-converted via `HasConversion`, `Id` `ValueGeneratedNever`; `Name`/`NameNormalized` `varchar(50)` required, `Color` `varchar` nullable, `CreatedAt`/`UpdatedAt` `timestamp with time zone`; **plain unique index** `ux_labels_owner_name` on `(owner_id, name_normalized)` (NOT a functional `lower(name)` index — EF can't model that, R7); index `ix_labels_owner_id`; FK `owner_id → users(id)` `OnDelete(Cascade)` (data-model §3).
- [ ] T008 Create `apps/api/src/TaskFlow.Infrastructure/Persistence/Configurations/TaskLabelConfiguration.cs` — table `task_labels`; composite key `HasKey(e => new { e.TaskId, e.LabelId })` with per-property `HasConversion`; FK `task_id → tasks(id)` and FK `label_id → labels(id)` **both `OnDelete(Cascade)`**; **no navigation property**; index `ix_task_labels_label_id` (the composite PK's `task_id` prefix serves the per-task lookup, so no standalone `task_id` index). Add `DbSet<Label>` to `AppDbContext.cs` (the join maps via its configuration). (data-model §3.)
- [ ] T009 Create the EF migration **`AddLabels`** (`dotnet ef migrations add AddLabels --project apps/api/src/TaskFlow.Infrastructure --startup-project apps/api/src/TaskFlow.Api`) — **`labels` created FIRST, then `task_labels`** (its `label_id` FK needs `labels` to exist); the `tasks` table is unchanged; **no `migrationBuilder.Sql`** (the unique index is plain/EF-generated). **Exactly ONE** new file under `apps/api/src/TaskFlow.Infrastructure/Persistence/Migrations/`; **read the generated SQL** to verify the `(owner_id, name_normalized)` unique index + the three cascade FKs (FR-051 LIVE, data-model §6). Depends T007,T008.
- [ ] T010 **(write first — RED — the fail-fast EF mapping spike)** Create `apps/api/tests/TaskFlow.IntegrationTests/Labels/LabelMappingTests.cs` (Testcontainers) — prove the two risky pieces BEFORE the big-bang `From` rewrite (T015): **(a)** a **standalone entity with a composite value-converted `(TaskId, LabelId)` key** persists and round-trips (insert a `TaskLabel`, read it back); **(b)** `ITaskLabelRepository.ListLabelIdsForTasksAsync(taskIds, owner)` — the `task_labels ⋈ labels WHERE owner_id = @caller AND task_id = ANY(@ids)` projection over value-converted ids — **TRANSLATES** (executes, returns the right per-owner ids) with no Npgsql value-converted-id-in-collection error. (Both ids are non-nullable, so the slice-005 nullable-FK trap should not apply — this test confirms it.) RED = compile failure (the repo method does not exist yet) → green via T011. Requires the tables/migration (T007–T009). (covers the mapping + the repo join in T011.)
- [ ] T011 Create `apps/api/src/TaskFlow.Application/TaskManagement/Labels/ILabelRepository.cs` + `apps/api/src/TaskFlow.Infrastructure/Persistence/LabelRepository.cs` (`AddAsync`, `UpdateAsync`, `DeleteAsync`, `FindOwnedAsync(id, owner)`, `ListForOwnerAsync(owner)`, `ListIdsForOwnerAsync(owner)`, `ExistsByNormalizedNameForOwnerAsync(owner, nameNormalized, excludingId?)`), and `apps/api/src/TaskFlow.Application/TaskManagement/Labels/ITaskLabelRepository.cs` + `apps/api/src/TaskFlow.Infrastructure/Persistence/TaskLabelRepository.cs` (`SetForOwnerAsync(taskId, owner, labelIds)` — the per-user set-replace delta; `ListLabelIdsForTaskAsync(taskId, owner)`; `ListLabelIdsForTasksAsync(taskIds, owner)` — the batched caller-scoped join). Register in DI. GREEN's the T010 spike.
- [ ] T012 [P] Modify `apps/api/src/TaskFlow.Application/TaskManagement/TaskResponse.cs` — add the **required** `labels` (`IReadOnlyList<Guid>`, always present, empty when none) and change `From` to **`From(Task task, IReadOnlyList<Guid> callerLabelIds)`** — a **required** parameter (R6: compile-breaks every call site, the type-safe anti-silent-empty defense).
- [ ] T013 [P] Modify `apps/api/src/TaskFlow.Application/TaskManagement/Queries/TodayResponse.cs` — `TodayTaskResponse` gains its own `labels` field, and `TodayTaskResponse.From` takes `callerLabelIds` and surfaces it (the flattened `allOf TaskResponse + isOverdue` DTO the required param does NOT auto-cover, R6). Depends T012.
- [ ] T014 [P] [US1] **(write first — RED)** Create `apps/api/tests/TaskFlow.IntegrationTests/Labels/TaskLabelsReadTests.cs` (`SharingTestBase`) — seed labels + applications, then assert: a **normal read path carries the caller's labels** on the **Inbox** (`GetMyTasks`), the **project list** (`GetProjectTasks`), **Upcoming** (`GetUpcomingTasks`), the **Today flattened DTO** (`GetTodayTasks`), and **"Assigned to me"** (`GetAssignedToMe`) — the §9/R6 named regression guard against a silent empty array; **caller-scoping** — member `O`'s labels on a shared task are **absent** from `C`'s read; and **labels survive a project move** (apply labels, `MoveTaskToProject`/to-Inbox, labels still present — R5 no-clear-on-move). Requires the projection wiring → covers T015. RED before T015.
- [ ] T015 Modify **every `TaskResponse.From` call site** to supply caller-scoped label ids — do this as **ONE atomic commit** that restores the green build after the T012 signature change. Command handlers (single-task overload `ListLabelIdsForTaskAsync(task.Id, caller)`): `CreateTask` (both calls), `RenameTask`, `EditTask`, `MoveTaskToProject`, `RescheduleDueDate`, `SetPriority`, `Commands/SetTaskDone`, `Commands/ReorderTask`, `SetTaskAssignees`. List/query handlers (the **batched** `ListLabelIdsForTasksAsync(taskIds, caller)` then map): `GetMyTasks`, `GetProjectTasks`, `GetUpcomingTasks`, `GetAssignedToMe`, and `GetTodayTasks` (→ `TodayTaskResponse.From`). (`SetTaskLabels` in US1 supplies its just-committed set directly.) Depends T011,T012,T013; RED via T014.

**Checkpoint**: the Label aggregate + relation + migration + repos exist; every existing task read carries the caller's (empty, for now) labels; the build is green and the full existing suite still passes.

---

## Phase 3: User Story 1 — Labels & the `L` selector (Priority: P1) 🎯

**Goal**: a user creates/renames/recolors/deletes their own labels (Tier A), and on a selected task presses `L` to
add/remove their labels entirely via keyboard; on a shared task each member manages only their own labels.

**Independent Test**: create a label, press `L` on a task, toggle it on/off (and type-to-create a new one) — it
persists and shows as a chip; another member's labels never appear on the caller's read; a viewer cannot tag a shared
task; renaming/deleting a label reflects across its tasks.

### Backend — Label CRUD (tests FIRST → impl), each ALLOW + DENY

- [ ] T016 [P] [US1] **(write first — RED)** Create `apps/api/tests/TaskFlow.IntegrationTests/Labels/CreateLabelTests.cs` (Testcontainers) — **ALLOW**: a caller `PUT`s a label → an owner-scoped row is written; a **retried `PUT` of the same id is idempotent** (returns the existing label, no error, no duplicate row). **DENY**: **no session → exactly 401 `unauthenticated` + the RFC 9457 envelope + NO row written** (a body-only handler with no auth weave is a silent 200 — assert the row count, per the slice-001 lesson); a **duplicate** `(owner, normalized name)` → **422** `validation_failed` (field `name`); a **non-preset `color`** → 422. (covers T017.)
- [ ] T017 [US1] Create `apps/api/src/TaskFlow.Application/TaskManagement/Labels/CreateLabel.cs` (request DTO `{ name, color? }` + command [id from path] + `CreateLabelValidator` [name non-empty + ≤50, color null-or-preset] + handler) — `ownerId = caller`; if the id already exists, **return the existing label** (idempotent upsert, mirrors `CreateTask`); else a normalized-name pre-check (`ExistsByNormalizedNameForOwnerAsync`) → 422, then insert. `PUT /api/labels/{id}` (R3). RED via T016.
- [ ] T018 [P] [US1] **(write first — RED)** Create `apps/api/tests/TaskFlow.IntegrationTests/Labels/ListLabelsTests.cs` — **ALLOW**: returns the caller's labels ordered by name; **DENY/isolation**: another user's labels are **absent** (per-user scope); no session → 401. (covers T019.)
- [ ] T019 [US1] Create `apps/api/src/TaskFlow.Application/TaskManagement/Labels/ListLabels.cs` + `apps/api/src/TaskFlow.Application/TaskManagement/Labels/LabelResponse.cs` (`{ id, name, color? }`, no `ownerId`) — `owner_id = caller`, ordered by `name`. `GET /api/labels` (R3/R6). RED via T018.
- [ ] T020 [P] [US1] **(write first — RED)** Create `apps/api/tests/TaskFlow.IntegrationTests/Labels/UpdateLabelTests.cs` — **ALLOW**: rename **and** recolor (whole-object); `NameNormalized` updated. **DENY**: a label **not owned / absent** → **404** (uniform existence-hide); a **duplicate** normalized name (another of the caller's labels) → 422; no session → 401. (covers T021.)
- [ ] T021 [US1] Create `apps/api/src/TaskFlow.Application/TaskManagement/Labels/UpdateLabel.cs` (request `{ name, color? }` + `UpdateLabelValidator` + handler) — `FindOwnedAsync(id, caller)` else 404; dup normalized-name pre-check (excluding self) → 422; `label.Edit(name, color, utcNow)`. `PATCH /api/labels/{id}` (R3). RED via T020.
- [ ] T022 [P] [US1] **(write first — RED)** Create `apps/api/tests/TaskFlow.IntegrationTests/Labels/DeleteLabelTests.cs` — **ALLOW**: the row is gone **and** its `task_labels` applications are **cascade-removed** (seed a label applied to a task, delete it, assert both gone). **DENY**: a label not owned / absent → 404; no session → 401. (covers T023.)
- [ ] T023 [US1] Create `apps/api/src/TaskFlow.Application/TaskManagement/Labels/DeleteLabel.cs` — `FindOwnedAsync(id, caller)` else 404; **hard delete** (FK cascade clears applications; labels are not in the FR-040/097 undo scope). `DELETE /api/labels/{id}` → 204 (R3). RED via T022.

### Backend — SetTaskLabels (tests FIRST → impl), two-sided authz

- [ ] T024 [P] [US1] **(write first — RED)** Create `apps/api/tests/TaskFlow.IntegrationTests/Labels/SetTaskLabelsTests.cs` (`SharingTestBase`) — **ALLOW** on a **personal** task (owner applies own labels) and on a **shared** task (editor/owner applies own labels). **SC-016 DENY matrix**: a **viewer** member → **403** `forbidden`; a **non-member** → **404**; a **personal task not owned** by the caller → **404**; a `labelId` **not owned** by the caller (or absent) → **422** `validation_failed` (field `labelIds`, no row changed). **Per-user isolation**: `C`'s set-replace does **NOT** touch member `O`'s labels on the same shared task. **Idempotent** no-op (same set → no change). **Versionless**: the request has no `version`; the task's `Version` is **unchanged** after the call. (covers T025.)
- [ ] T025 [US1] Create `apps/api/src/TaskFlow.Application/TaskManagement/Labels/SetTaskLabels.cs` (request DTO `{ labelIds }` + `SetTaskLabelsValidator` [well-formed uuids, no duplicates, sane cap] + handler) — handler: `TaskAccessGuards.LoadWritableTaskAsync(id, EffectiveRole.Editor, …)` (personal-foreign→404, non-member→404, viewer→403) → every `labelId` ∈ `ILabelRepository.ListIdsForOwnerAsync(caller)` else **422** (uniform, no existence leak) → `ITaskLabelRepository.SetForOwnerAsync(taskId, caller, labelIds)` (per-user delta) → return `TaskResponse.From(task, callerLabelIds: labelIds)`. **No `version`; `Task.Version` untouched** (R2). `PATCH /api/tasks/{id}/labels`. RED via T024.

### Endpoints + the gen:api join (gates ALL web work)

- [ ] T026 [US1] Create `apps/api/src/TaskFlow.Api/Endpoints/LabelEndpoints.cs` — `GET /api/labels` (→ `ListLabels`), `PUT /api/labels/{id}` (→ `CreateLabel`), `PATCH /api/labels/{id}` (→ `UpdateLabel`), `DELETE /api/labels/{id}` (→ `DeleteLabel`); and modify `apps/api/src/TaskFlow.Api/Endpoints/TaskEndpoints.cs` — add `PATCH /api/tasks/{id}/labels` (→ `SetTaskLabels`). All public types; thin adapters delegating via `IMessageBus.InvokeAsync` so the deny-by-default `AuthorizationMiddleware` weaves (the silent-200 backstop — confirmed by T016's 401+no-row assertion). Depends T017,T019,T021,T023,T025.
- [ ] T027 [US1] Stamp the operationIds + responses in `apps/api/src/TaskFlow.Api/OpenApi/TaskFlowDocumentTransformer.cs` — `createLabel` (PUT; 401,422), `listLabels` (GET; 401), `updateLabel` (PATCH; 401,404,422), `deleteLabel` (DELETE; 401,404), `setTaskLabels` (PATCH; 401,403,404,422) — **no `ErrorCodes` change** (R9). Then `cd apps/web && pnpm gen:api` (API on `localhost:4311`) → `schema.d.ts` gains the 5 ops + `LabelResponse` + `TaskResponse.labels`; `pnpm typecheck` green; matches `contracts/openapi.yaml`; commit the regen. **The one gen:api gate** — blocks every web task. Depends T026.

### Web — tests FIRST → impl

- [ ] T028 [P] [US1] **(write first — RED)** Create `apps/web/tests/unit/label-validation.test.ts` — the label-name/color schema + the label-set schema (uuid items, no duplicates) over the generated types (covers T029).
- [ ] T029 [US1] Create `apps/web/src/lib/validation/label.ts` — the Zod schemas at the trust boundary (Constitution VI). Depends T027; RED via T028.
- [ ] T030 [P] [US1] **(write first — RED)** Extend `apps/web/tests/unit/task-view-mutations.test.ts` — the optimistic `setTaskLabels` recipe: `onMutate` patches the task's `labels` across the caches from the current row, `onError` rolls back, **`onSettled` does a labels-ONLY merge from `data.labels` (NOT a whole-object `applyTaskToViewCaches` writeback)**; **the clobber-guard pin: a `setTaskLabels` settle does NOT revert a concurrent in-flight title/priority edit on the same task** (R11); the body carries `{ labelIds }` with **no `version`**. Plus the optimistic `createLabel` roster insert (`['labels']`). (covers T031.)
- [ ] T031 [US1] Modify `apps/web/src/hooks/useTaskMutations.ts` — add the optimistic `setTaskLabels` recipe (**labels-only merge** in `onMutate` + `onSettled`; invalidate via `settleViewCaches` for the rest; never pass the server row through `applyTaskToViewCaches`, R11) — and create `apps/web/src/hooks/useLabels.ts` (`['labels']` roster query + optimistic `createLabel`/`updateLabel`/`deleteLabel` mutations). Depends T027; RED via T030.
- [ ] T032 [P] [US1] **(write first — RED)** Create `apps/web/tests/unit/label-selector.test.ts` — the selector assembly (keyboard toggle an option, `aria-checked`, type-to-create commits an optimistic `createLabel` + applies it) and chip rendering (the label **name** present; color rendered as a decorative dot/background, **never the sole carrier of meaning**) (covers T033).
- [ ] T033 [US1] Create `apps/web/src/components/labels/LabelSelector.tsx` (the shared `Dialog` focus contract — initial focus, trap, Esc, return focus to the invoking task row, FR-101; the caller's labels as keyboard-operable toggles; a type-to-create input with FR-031 single-key suppression; commits via `setTaskLabels`) and modify `apps/web/src/components/tasks/TaskRow.tsx` — render **label chips** (name from the `['labels']` roster, React-escaped, with the preset color as a decorative dot — FR-044/FR-099) + the selector affordance. Depends T031; RED via T032.
- [ ] T034 [US1] Modify `apps/web/src/hooks/useGlobalShortcuts.ts` — bind the **`L`** contextual key (→ `onLabel`, opens the selector on the selected task) on the existing contextual layer (alongside `E`/`M`/`A`); suppressed in text inputs (FR-031); FR-045 (no AT-binding collision — same class as `E`/`M`/`A`). **Test-first within the task**: add the `L` case to `apps/web/tests/unit/shortcuts.test.ts` (RED) before the binding (GREEN). Depends T033.
- [ ] T035 [US1] **(write first — RED, then GREEN)** Create `apps/web/tests/e2e/labels.spec.ts` (Playwright) — **US-08.AS-04**: select a task, press `L`, **add** a label and **remove** it entirely via keyboard; **type-to-create** a new label and apply it; verify persistence (reopen the selector / reload); plus the **SC-008 WCAG audit** on the selector. Depends T034.

**Checkpoint**: US1 — label CRUD + the `L` add/remove/create flow are whole and keyboard-driven; per-user scoping holds on shared tasks; no `Task.Version` churn.

---

## Phase 4: Polish & Cross-Cutting

- [ ] T036 [P] Accessibility pass (Principle II / SC-008): the selector follows the dialog focus contract (initial focus, trap, Esc, return focus, FR-101); **a visible focus indicator** on the options/create-input/chip affordance (FR-042); options keyboard-toggleable with `aria-checked` (FR-043); chips carry the label **name** (never color-alone, FR-044 ≥4.5:1 via a curated preset palette); the new single-key `L` does **not** collide with native AT bindings (FR-045); no hover-only affordances (FR-046); `prefers-reduced-motion` (FR-047); a label change / rollback routes through the polite ARIA live region without stealing focus (FR-101). The selector passes the WCAG 2.1 AA audit (SC-008).
- [ ] T037 [P] Instant response (SC-003): `setTaskLabels` and `createLabel` paint optimistically <16 ms; the label settle is a **labels-only merge** (leaves `Task.version` and other fields untouched); rollback on error.
- [ ] T038 [P] Security/privacy (Principles XI/XII): the label `name` is React-escaped on render (FR-099); `color` is a **preset token** (not raw hex/CSS); `LabelResponse` carries **no `ownerId`** and `TaskResponse.labels` is caller-scoped ids only; account-deletion erasure of a user's labels + applications is automatic via `labels.owner_id → users(id) ON DELETE CASCADE` (FR-085) — exercised by a test; structured logging carries ids/codes only (FR-050).
- [ ] T039 Confirm CI gates: `pnpm gen:api` clean (the 5 ops + `TaskResponse.labels`); the `ERROR_UX satisfies Record<ErrorCode, ErrorUx>` map stays exhaustive with **NO change** (no new errorCode, R9); TS strict + C# nullable/analyzers-as-errors; **every NEW data handler** (`CreateLabel`, `ListLabels`, `UpdateLabel`, `DeleteLabel`, `SetTaskLabels`) has an **allow + a deny** test (Principle VIII/IX); confirm **Program.cs message wiring is UNCHANGED** (no label event, R8).
- [ ] T040 Confirm **FR-051 LIVE** (Principle VII): **exactly ONE** new migration (`AddLabels`, two tables) under `Persistence/Migrations/` in the diff; **no `migrationBuilder.Sql`** (the `(owner_id, name_normalized)` unique index is EF-generated); the CI `backup.sh → ef database update → restore-test.sh` gate covers it.
- [ ] T041 Run `specs/006-labels/quickstart.md` validation scenarios end-to-end (the `L` add/remove/create flow; label CRUD; per-user scoping on a shared task; labels-survive-move; the deny matrix; the Today/Inbox/project/Upcoming/Assigned read-path carries-labels; migration/FR-051).

---

## Dependencies & Execution Order

### Phase dependencies
- **Setup (P1)**: T001 ‖ T002 — confirmations, start immediately.
- **Foundational (P2)**: depends on Setup. T003→T004/T005 (aggregate); T006 ‖; T007/T008→T009 (mapping→migration); **T010 spike (RED) → T011 (repos)** (mapping + join translation proven before the rewrite); T012→T013 (read-model delta incl. the flattened Today DTO); T014→T015 (the atomic `From`-rewrite restores green). **Blocks US1**.
- **US1 (P3)**: depends on Foundational. The 5 handler verticals (each test→impl, allow+deny) → endpoints → **T027 gen:api (the one gate)** → web (validation → mutations → selector/chips → `L` key → E2E).
- **Polish (P4)**: depends on US1.

### Test-First ordering (Red-Green) — strict lower-id within each phase
- Foundational: T003→T004/T005; T010 spike (RED) → T011 repos (both before T015); T014→T015 (the read-path carries-labels + caller-scoping + survive-move test precedes the projection rewrite).
- US1 backend: T016→T017, T018→T019, T020→T021, T022→T023, T024→T025 (each allow+deny test precedes its impl).
- US1 web: T028→T029, T030→T031 (incl. the clobber-guard pin), T032→T033; T034 binds `L` (test-first within); T035 is the E2E (RED then GREEN).

### Critical path
T005 → T007/T008 → T009 (migration) → **T010 spike (RED) → T011 repos (mapping/join proven)** → T012/T013 → **T015 (atomic From-rewrite, green build)** → (T017,T019,T021,T023,T025) → **T026 endpoints** → **T027 gen:api (the single gate)** → (web T028..T035) → Polish.

### Migration & contract gates
- **ONE** migration (`AddLabels`, T009); **FR-051 LIVE** (T040) — the backup/restore-test gate covers it; no `migrationBuilder.Sql`.
- **One** `gen:api` regen (T027), CI-diff-gated; `ERROR_UX` unchanged, **no new errorCode** (T039).

---

## Implementation Strategy

### MVP
Foundational (the `Label` aggregate + the `task_labels` relation + migration + repos + the caller-scoped
`TaskResponse.labels` projection — **prove the EF mapping/join first (T010 spike → T011 repos), then do the atomic `From`-rewrite, T015**)
→ US1 backend (label CRUD + `SetTaskLabels`, each allow+deny) → **T027 gen:api** → US1 web (the `L` selector + chips +
the `L` key + optimistic labels-only recipe) → **STOP & VALIDATE** (quickstart) → Polish. The backend CRUD + apply is
a demoable increment (create/list/apply via the API); the web completes the `L` loop.

### Notes
- **Test-First is mandatory** (Constitution VIII): the test task has a lower id than the impl. **Every** new data handler ships an **allow + a deny** test (Principle IX + governance gate). `CreateLabel`'s deny is **exactly 401 + the RFC 9457 envelope + NO row written** (the body-only silent-200 guard, slice-001 lesson).
- **Prove the EF mapping before the big-bang** (T010 spike → T011 repos): a standalone entity with a composite value-converted key + the caller-scoped batched join must round-trip/translate **before** the required-`From`-parameter change (T015) red-builds all ~14 call-sites. Do T015 as one atomic commit that restores green.
- **The flattened `TodayTaskResponse` is a first-class affected path** (T013/T014) — the required-`From`-parameter does NOT auto-cover it; it gets its own `labels` field + a dedicated carries-labels assertion.
- **Per-user, versionless, two-sided** (R2/R4): `SetTaskLabels` never bumps `Task.Version`, touches only caller-owned rows, and requires task write-access AND caller-owned labels (non-owned → 422; viewer → 403).
- **The optimistic settle is a labels-ONLY merge** (R11) — never a whole-object writeback (the versionless response carries possibly-stale non-label fields with an unchanged version); pinned by the T030 clobber-guard vitest.
- **No domain event** (R8) — Program.cs message wiring is unchanged. **No cross-slice cleanup / no clear-on-move** (R5) — FK cascades + double-gating; a `labels-survive-move` test (T014) pins the no-clear-on-move decision.
- **FR-051 is LIVE** (R10) — exactly one migration (`AddLabels`, two tables), no `migrationBuilder.Sql`; the backup/restore-test gate covers it (T002/T040). The `owner_id` FK `ON DELETE CASCADE` makes account-deletion erasure automatic (Constitution XI).
- **No new error code** (R9): 401/403/404/422 are all existing codes; **no 409** (label ops are versionless); `ERROR_UX` stays exhaustive with no change.

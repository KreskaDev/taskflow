---
description: "Task list for Task Assignment (slice 008)"
---

# Tasks: Task Assignment

**Input**: Design documents from `/specs/008-task-assignment/`
**Prerequisites**: plan.md, spec.md, research.md (R1–R11), data-model.md, contracts/openapi.yaml, quickstart.md (all present)

**Tests**: REQUIRED. Constitution v4.0.0 Principle VIII (Test-First) governs; Principle IX + the governance gate
require **every new/changed data handler to ship an ALLOW and a DENY test**. Test-First is Red-Green-Refactor:
**the test task has a LOWER id than the implementation it covers — write it, watch it fail (RED), then implement.**
For new-type unit tests RED is the **compile failure** (report honestly).

**Organization**: This slice realizes the **`assignees`** attribute of ENT-01 for **shared-project tasks only**
(FR-069). It ships **ONE EF migration** (`AddTaskAssignees`, a join table) — so **FR-051 backup-before-migration is
LIVE** (the slice-004/007 posture; the CI `backup.sh → ef database update → restore-test.sh` gate applies — confirm,
don't re-wire). It reuses the slice-007 `ProjectMembership` + `ResolveEffectiveRole`/`RequireRole` policy and the
slice-005 `TaskAccessGuards` dispatch-by-visibility write path unchanged, and **extends** the slice-004/007
membership-removal + project-delete/move flows to clear assignments. Single user story (US-13, P2).

**No new error code** (R8): viewer → 403 `forbidden`; non-member / personal-task / foreign → 404 `not_found`;
non-member assignee / malformed set → 422 `validation_failed`; stale token → 409 `version_conflict`.

## Format: `[ID] [P?] [Story] Description`
- **[P]**: parallelizable (different files, no dependency on an incomplete task)
- **[Story]**: `[US1]` for the user-story phase (Setup/Foundational/Polish carry no story label)

## Path Conventions
Monorepo: backend `apps/api/src/<Project>/…`, backend tests `apps/api/tests/<Project>/…`, web `apps/web/src/…`, web tests `apps/web/tests/…`.

---

## Phase 1: Setup (preconditions)

- [x] T001 [P] Confirm slice-008 preconditions (verify in code, do not infer): the `Task` aggregate has **NO** assignees field today (`apps/api/src/TaskFlow.Domain/TaskManagement/Task.cs`) → a migration is genuinely required; the slice-007 `ProjectMembership` + `IProjectMembershipRepository` (`ListByProjectAsync`/`ListProjectIdsForUserAsync`/`RemoveAllForProjectAsync`) + `ResolveEffectiveRole`/`RequireRole` + `MembershipGuards` exist; the slice-005 `TaskAccessGuards.LoadWritableTaskAsync` exists; the slice-004 `MoveTaskToProject` + `Task.MoveToProject` + `DeleteProject` (the `move_to_inbox` disposition via `ProjectRepository.MoveProjectTasksToInboxAsync`) and the slice-007 `UnshareProject`/`RemoveMember`/`LeaveProject` exist (the flows this slice extends); the error codes `forbidden`/`not_found`/`validation_failed`/`version_conflict` already exist in `TaskFlowDocumentTransformer.cs` + `apps/web/src/lib/api/client.ts` (so reuse leaves `ERROR_UX` exhaustive, R8); the slice-007 `MembersResponse` roster, the shared `apps/web/src/components/ui/Dialog.tsx`, and the slice-005 `DailyView` exist to be reused by the web surface.
- [x] T002 [P] Confirm **FR-051 is LIVE** this slice (Principle VII): the CI deploy job already runs `scripts/backup.sh → dotnet ef database update → scripts/restore-test.sh` — this slice adds **one** migration and **confirms** the gate fires; it does **not** re-wire it. (The failure mode to avoid is shipping the migration without the backup/restore-test job covering it.)

---

## Phase 2: Foundational (the relation, migration, aggregate behavior, event, read-model delta)

**Purpose**: the `task_assignees` relation + its migration (FR-051 LIVE) + the `Task` assignee behaviors + the
`TaskAssigned` event + the `TaskResponse.assignees` delta + eager-load — prerequisites for the US1 verticals.

- [x] T003 [P] **(write first — RED)** Extend `apps/api/tests/TaskFlow.UnitTests/Domain/TaskManagement/TaskTests.cs` — `Task.SetAssignees(desired, actor, utcNow)`: set/replace/clear; computes the **delta** (added/removed); a **no-op** set (same members) does **NOT** bump `Version` and raises **no** `TaskAssigned` (idempotency, R3); a real change bumps `Version` + raises **one** `TaskAssigned` carrying the right `{added, removed, actorUserId}`; and **`MoveToProject` CLEARS the assignee set on a real project change** (R5) — moving to a different project or to null/Inbox empties `Assignees` (no event). (covers T004.)
- [x] T004 Add the **`Assignees`** collection (`IReadOnlyCollection<UserId>`) + **`SetAssignees`** behavior to `apps/api/src/TaskFlow.Domain/TaskManagement/Task.cs`, and modify **`MoveToProject`** to clear `Assignees` when the project actually changes (data-model §1). RED via T003.
- [x] T005 [P] Create `apps/api/src/TaskFlow.Domain/TaskManagement/TaskAssigned.cs` — the domain event `{ TaskId, ProjectId, IReadOnlyCollection<UserId> AddedAssigneeIds, RemovedAssigneeIds, UserId ActorUserId }` (data-model §2). Public concrete type (Wolverine codegen).
- [x] T006 [P] Modify `apps/api/src/TaskFlow.Application/TaskManagement/TaskResponse.cs` — add the **required** `assignees` (`Guid[]`, always present, empty for personal/unassigned) + the `From(Task)` projection (R7).
- [x] T007 Map the `Assignees` collection → **`task_assignees`** in `apps/api/src/TaskFlow.Infrastructure/Persistence/Configurations/TaskConfiguration.cs` — composite PK `(task_id, user_id)`, FK `task_id → tasks(id)` and FK `user_id → users(id)` BOTH `ON DELETE CASCADE` (data-model §3). Depends T004.
- [x] T008 Create the EF migration **`AddTaskAssignees`** (`dotnet ef migrations add AddTaskAssignees`) — the join table only; the `tasks` table is unchanged. **Exactly ONE** new file under `apps/api/src/TaskFlow.Infrastructure/Persistence/Migrations/` (FR-051 LIVE, data-model §6). Depends T007.
- [x] T008a [P] **(write first — RED)** Create `apps/api/tests/TaskFlow.IntegrationTests/TaskManagement/TaskAssigneesEagerLoadTests.cs` (`SharingTestBase`) — seed a shared-project task with assignees, then assert it carries its `assignees` through a **normal (non-assigned) read path** (the project task list AND the slice-005 Today view AND the Inbox list), proving the `Include(t => t.Assignees)` is present on every read path and `TaskResponse.assignees` is populated (the data-model §9 / R7 named regression guard against a silent empty array). Requires the table → depends T007/T008; covers the eager-load in T009 (and the T006 projection). RED before T009.
- [x] T009 Modify `apps/api/src/TaskFlow.Application/TaskManagement/ITaskRepository.cs` + `apps/api/src/TaskFlow.Infrastructure/Persistence/TaskRepository.cs` — **`Include(t => t.Assignees)` on EVERY task read path** (`FindOwnedAsync`/`FindByIdAsync`/`ListOwnedAsync`/`ListByProjectAsync`/`ListDueInRangeReadableAsync`); add `ClearAssigneesForProjectAsync(projectId)` + `ClearAssigneesForUserInProjectAsync(projectId, userId)` (set-based `ExecuteDelete`); add the assigned-to-me list method (R6/R7, data-model §3/§5). Depends T004; RED via T008a.

---

## Phase 3: User Story 1 — Task Assignment (Priority: P2) 🎯

**Goal**: editors/owners assign members to a shared-project task (picker), change/remove assignees, see "Assigned to
me", and get **no** assignment control on personal tasks. Assigning raises `TaskAssigned` (delta + actor, for slice 017).

**Independent Test**: in a shared project, assign members → they appear + `TaskAssigned` raised; change/remove → set
updates; open "Assigned to me" → assigned shared tasks (incl. the owner's self-assigned); a personal task → no control.

### Backend — SetTaskAssignees (tests FIRST → impl → endpoint)

- [x] T010 [P] [US1] **(write first — RED)** Create `apps/api/tests/TaskFlow.IntegrationTests/TaskManagement/SetTaskAssigneesTests.cs` (Testcontainers, `SharingTestBase`) — **ALLOW**: an editor (and the owner) assigns current members; **self-assignment** allowed. **SC-016 DENY matrix**: a **viewer** member → **403** `forbidden`; a **non-member** → **404**; assignment on a **personal/Inbox** task → **404** (no surface); a **non-member assignee** id → **422** `validation_failed` (no assignee added); stale `version` → **409**. **Event**: a real change raises one `TaskAssigned` with the right `{added, removed, actorUserId}` (assert via `host.TrackActivity()…Sent.MessagesOf<TaskAssigned>()`); a **no-op** set raises **none**. (covers T011.)
- [x] T011 [US1] Create `apps/api/src/TaskFlow.Application/TaskManagement/SetTaskAssignees.cs` (request DTO `{ assigneeIds, version }` + command + `SetTaskAssigneesValidator` [well-formed uuids, no duplicates, sane cap] + handler) and `SetTaskAssigneesRequest.cs`. Handler: `TaskAccessGuards.LoadWritableTaskAsync(id, EffectiveRole.Editor, …)` → shared-only (personal → 404) → `version` (409) → every `assigneeId` a current member (`ListByProjectAsync` ∪ owner anchor, else 422) → `Task.SetAssignees(validated, actor: caller, utcNow)` → drain `TaskAssigned` → persist. `PATCH /api/tasks/{id}/assignees` (R2/R4). Depends T004,T005,T009; RED via T010.

### Backend — GetAssignedToMe (tests FIRST → owned-shared seam → query)

- [x] T012 [P] [US1] **(write first — RED)** Create `apps/api/tests/TaskFlow.IntegrationTests/TaskManagement/GetAssignedToMeTests.cs` (`SharingTestBase`) — **ALLOW**: a member-assignee sees their assigned shared tasks (grouped by project); the **owner** who self-assigns **sees it** (the owned-shared union, R6); done/cancelled excluded. **DENY**: a non-member / non-assignee does **not** see a task; a user who **lost membership** stops seeing it (membership gates; assignee is provenance only). (covers T013/T014.)
- [x] T013 [US1] Add `ListOwnedSharedProjectIdsAsync(UserId owner)` to `apps/api/src/TaskFlow.Application/TaskManagement/IProjectRepository.cs` + `apps/api/src/TaskFlow.Infrastructure/Persistence/ProjectRepository.cs` — owner-scoped, `Visibility == shared`, non-deleted (the owned-shared union for the assigned read, R6). RED via T012.
- [x] T014 [US1] Create `apps/api/src/TaskFlow.Application/TaskManagement/Queries/GetAssignedToMe.cs` + the `AssignedResponse`/`AssignedGroup` read models in `apps/api/src/TaskFlow.Application/TaskManagement/AssignedResponse.cs` — readable shared set = `ListProjectIdsForUserAsync(caller)` ∪ `ListOwnedSharedProjectIdsAsync(caller)`; tasks where `task_assignees.user_id = caller AND project_id ∈ that set`, non-deleted, not done/cancelled; group by project; slice-005 R5 order. `GET /api/tasks/assigned` (R6). Depends T009,T013; RED via T012.

### Backend — event wiring + endpoints

- [x] T015 [US1] Create `apps/api/src/TaskFlow.Application/TaskManagement/TaskAssignedHandler.cs` — a **no-op** handler (routes the publish; slice 017 replaces) mirroring `MembershipEventHandlers`; wire `apps/api/src/TaskFlow.Api/Program.cs`: `opts.PublishMessage<TaskAssigned>().ToLocalQueue("task-assignment-events")` + add `&& chain.MessageType != typeof(TaskAssigned)` to the `AuthorizationMiddleware` exclusion (off-queue, no HttpContext). Depends T005.
- [x] T016 [US1] Modify `apps/api/src/TaskFlow.Api/Endpoints/TaskEndpoints.cs` — add `PATCH /api/tasks/{id}/assignees` (→ `SetTaskAssignees`) and `GET /api/tasks/assigned` (→ `GetAssignedToMe`; register the literal `/assigned` so it WINS over `/{id}` templates). Depends T011,T014.

### Cleanup touches (tests FIRST → impl)

- [x] T017 [P] [US1] **(write first — RED)** Create `apps/api/tests/TaskFlow.IntegrationTests/TaskManagement/AssignmentCleanupTests.cs` (`SharingTestBase`) — **unshare** clears ALL the project's `task_assignees`; **remove-member** / **leave** clears that user's assignee rows across the project; **DeleteProject + `move_to_inbox`** clears the project's assignees (the bulk-move path bypasses the aggregate); **EditTask** and **MoveTaskToProject** moving a task off its project clear that task's assignees (R5). (covers T018/T019.)
- [x] T018 [US1] Modify `apps/api/src/TaskFlow.Application/TaskManagement/{UnshareProject,RemoveMember,LeaveProject}.cs` + `DeleteProject.cs` (the `move_to_inbox` branch) to clear assignments in the same transaction (R5): unshare/delete-to-inbox → `ClearAssigneesForProjectAsync`; remove/leave → `ClearAssigneesForUserInProjectAsync`. Depends T009; RED via T017.
- [x] T019 [US1] Confirm `EditTask`/`MoveTaskToProject` clear assignees on move via `Task.MoveToProject` (T004) — add the **move-clears-assignees** assertions to `EditTaskTests.cs` / `MoveTaskToProjectTests.cs` (no handler change beyond T004; the aggregate-routed paths inherit the clear). RED via T017 (shared file additions).

### gen:api join — the single backend→web serialization point (gates ALL web work)

- [x] T020 [US1] Stamp the operationIds (`setTaskAssignees`, `getAssignedToMe`) + their 401/403/404/409/422 (assignees) / 401 (assigned) responses in `apps/api/src/TaskFlow.Api/OpenApi/TaskFlowDocumentTransformer.cs` (**no `ErrorCodes` change**, R8), then `cd apps/web && pnpm gen:api` (API on `localhost:4311`) → `schema.d.ts` gains the 2 ops + `AssignedResponse` + `TaskResponse.assignees`; `pnpm typecheck` green; matches `contracts/openapi.yaml`; commit. **The one gen:api gate** — blocks every web task. Depends T016.

### Web — tests FIRST → impl

- [x] T021 [P] [US1] **(write first — RED)** Extend `apps/web/tests/unit/task-validation.test.ts` — the assignee-set Zod schema (uuid items, no duplicates) in `apps/web/src/lib/validation/task.ts` (covers T022).
- [x] T022 [US1] Modify `apps/web/src/lib/validation/task.ts` — add the assignee-set schema over the generated types. Depends T020; RED via T021.
- [x] T023 [P] [US1] **(write first — RED)** Extend `apps/web/tests/unit/task-view-mutations.test.ts` — the optimistic `setTaskAssignees` surface (`onMutate` patches the task's `assignees` across the caches, `onError` rolls back, `onSettled` writes the server row back; body carries `{assigneeIds, version}`) (covers T024).
- [x] T024 [US1] Modify `apps/web/src/hooks/useTaskMutations.ts` — add the optimistic `setTaskAssignees` recipe (slice-005 snapshot → patch → rollback → settle-writeback shape; also patch `['tasks','assigned']`). Depends T020; RED via T023.
- [x] T025 [P] [US1] **(write first — RED)** Create `apps/web/tests/unit/assigned.test.ts` — the "Assigned to me" group assembly from the read model (grouped by project; R5 order preserved) (covers T026/T027).
- [x] T026 [US1] Create `apps/web/src/hooks/useAssignedTasks.ts` (`['tasks','assigned']` → `getAssignedToMe`). Depends T020; RED via T025.
- [x] T027 [US1] Create `apps/web/src/components/tasks/AssigneePicker.tsx` (the `Dialog` focus contract; the project roster as keyboard-operable checkboxes; FR-031 suppression in the filter; commits via `setTaskAssignees`) and `apps/web/src/components/tasks/AssignedView.tsx` (the grouped "Assigned to me" view, `DailyView`-style). Depends T024,T026; RED via T025.
- [x] T028 [US1] Modify `apps/web/src/components/tasks/TaskRow.tsx` — render **assignee chips** (member names from the slice-007 roster, React-escaped, never avatar-color-alone — FR-044/FR-099) + the picker affordance **only on shared-project tasks** (FR-069/AS-04). Depends T027.
- [x] T029 [US1] Modify `apps/web/src/hooks/useGlobalShortcuts.ts` — bind the `G A` ("Assigned to me") navigation chord on the existing `G`-chord layer. **Test-first within the task**: add the `G A` case to `tests/unit/shortcuts.test.ts` (RED) before the binding (GREEN). Depends T020.
- [x] T030 [US1] Create the route `apps/web/src/app/(app)/assigned/page.tsx` (mounts `AssignedView`, served by `G A`). Depends T027,T029.
- [x] T031 [US1] **(write first — RED, then GREEN)** Create `apps/web/tests/e2e/task-assignment.spec.ts` (Playwright) — **US-13 AS-01..AS-04**: assign members in a shared project (AS-01); change/remove (AS-02); "Assigned to me" lists them (AS-03, incl. owner self-assign); a personal task offers **no** assignment control (AS-04); plus the **SC-008 WCAG audit** on the picker + the assigned view. Depends T030.

**Checkpoint**: US1 — assign/change/remove, "Assigned to me", no-control-on-personal are whole and keyboard-driven; `TaskAssigned` raised for slice 017.

---

## Phase 4: Polish & Cross-Cutting

- [x] T032 [P] Accessibility pass (Principle II / SC-008): the assignee picker follows the dialog focus contract (initial focus, trap, Esc, return focus, FR-101); assignee chips carry the member **name** (never avatar-color alone, FR-044 ≥4.5:1); no hover-only affordances (FR-046); `prefers-reduced-motion` (FR-047); single-key suppression in the picker filter (FR-031); a remote assignee change / rollback routes through the polite ARIA live region (FR-101). The picker + assigned view pass the WCAG 2.1 AA audit (SC-008).
- [x] T033 [P] Instant response (SC-003): `setTaskAssignees` paints optimistically <16 ms (snapshot → patch → rollback → settle-writeback); the server reconciles/rolls back.
- [x] T034 [P] Security/logging (Principle XII): `TaskResponse.assignees` carries **ids only** (names from the roster, React-escaped, FR-099); the `TaskAssigned` payload carries only ids/deltas + `actorUserId`, no secrets, never logged with sensitive context (FR-050/FR-100).
- [x] T035 Confirm CI gates: `pnpm gen:api` clean (the 2 ops + `TaskResponse.assignees`); the `ERROR_UX satisfies Record<ErrorCode, ErrorUx>` map stays exhaustive with **NO change** (no new errorCode, R8); TS strict + C# nullable/analyzers-as-errors; **every NEW/CHANGED data handler** (`SetTaskAssignees`, `GetAssignedToMe`, and the changed `UnshareProject`/`RemoveMember`/`LeaveProject`/`DeleteProject`/`EditTask`/`MoveTaskToProject`) has an **allow + a deny** (or cleanup-asserted) test (Principle VIII/IX); the `TaskAssigned` event asserted.
- [x] T036 Confirm **FR-051 LIVE** (Principle VII): **exactly ONE** new migration (`AddTaskAssignees`) under `Persistence/Migrations/` in the diff; the CI `backup.sh → ef database update → restore-test.sh` gate covers it; account-deletion erasure is exercised (the `user_id` FK `ON DELETE CASCADE` clears assignee rows).
- [x] T037 Run `specs/008-task-assignment/quickstart.md` validation scenarios end-to-end (assign/change/remove, owner-self-assign, "Assigned to me", personal-no-control, the deny matrix, the cleanup + move-clears, the eager-load normal-view).

---

## Dependencies & Execution Order

### Phase dependencies
- **Setup (P1)**: T001 ‖ T002 — confirmations, start immediately.
- **Foundational (P2)**: depends on Setup. T003→T004 (behaviors); T005/T006 ‖; T007→T008 (mapping→migration); T009 (repo). **Blocks US1**.
- **US1 (P3)**: depends on Foundational. The command + query verticals → event wiring → endpoints → cleanup → **T020 gen:api (the one gate)** → web. Both data handlers ship allow+deny; the cleanup handlers ship cleanup assertions.
- **Polish (P4)**: depends on US1.

### Test-First ordering (Red-Green) — strict lower-id within each phase
- Foundational: T003→T004; T008a→T009 (the eager-load normal-view test precedes the `Include` impl).
- US1 backend: T010→T011, T012→{T013,T014}, T017→{T018,T019} (each test precedes its impl).
- US1 web: T021→T022, T023→T024, T025→{T026,T027}; T031 is the E2E (RED then GREEN).

### Critical path
T004/T005/T006 → T007 → T008 (migration) → T009 → (T011, T014, T015) → **T016 endpoints** → **T020 gen:api (the single gate)** → (web T021..T031) → Polish.

### Migration & contract gates
- **ONE** migration (`AddTaskAssignees`, T008); **FR-051 LIVE** (T036) — the backup/restore-test gate covers it.
- **One** `gen:api` regen (T020), CI-diff-gated; `ERROR_UX` unchanged, **no new errorCode** (T035).

---

## Implementation Strategy

### MVP
Foundational (the relation + migration + `Task.SetAssignees`/`MoveToProject`-clear + `TaskAssigned` + the `TaskResponse`
delta + eager-load) → US1 backend (`SetTaskAssignees` + `GetAssignedToMe` + event wiring + endpoints + the cleanup
touches) → **T020 gen:api** → US1 web (picker + assigned view + chips + `G A` + route) → **STOP & VALIDATE** (quickstart)
→ Polish. US1 backend alone is a demoable increment (assign + "Assigned to me" via the API); the web completes the loop.

### Notes
- **Test-First is mandatory** (Constitution VIII): the test task has a lower id than the impl. **Every** new/changed data handler ships an **allow + a deny** (or cleanup-asserted) test (Principle IX + governance gate).
- **FR-051 is LIVE** this slice (R9) — exactly one migration (`AddTaskAssignees`); the backup/restore-test gate covers it (T002/T036). The `user_id` FK `ON DELETE CASCADE` makes the account-deletion erasure cascade automatic.
- **Assignee is provenance only** (FR-066): membership/ownership dispatches access; the assigned read scopes to `(memberships ∪ owned-shared) ∩ assignee`; leave/remove/unshare clears assignments AND revokes access; task project-move (incl. the `DeleteProject` `move_to_inbox` bulk path) clears assignees.
- **`TaskResponse.assignees` is eager-loaded on EVERY task read path** (T009) — a missing `Include` silently emits `[]`.
- **The `TaskAssigned` event** (R3) carries the delta + `actorUserId`, is idempotent (only on a real change), durable-queue routed with a **no-op handler** this slice; slice 017 consumes it + self-suppresses via `actorUserId`.
- **No new error code** (R8): 403 / 404 / 422 / 409 are all existing codes; `ERROR_UX` stays exhaustive with no change.

---
description: "Task list for Project Management (slice 004)"
---

# Tasks: Project Management

**Input**: Design documents from `/specs/004-project-management/`
**Prerequisites**: plan.md, spec.md, research.md (R1–R16), data-model.md, contracts/openapi.yaml, quickstart.md (all present)

**Tests**: REQUIRED. Constitution v4.0.0 Principle VIII (Test-First) governs, and Principle IX + the governance gate
require **every new data handler to ship an allow AND a deny test**. Test-First is Red-Green-Refactor: **the test task
has a LOWER id than the implementation it covers — write it, watch it fail (RED), then implement (GREEN).**

**Organization**: This slice is the first since slice 002 to add an entity (`Project`, ENT-02), a new aggregate, and an
EF migration (`AddProjects` → **FR-051 backup-before-migration is LIVE**). The work splits into a **Foundational** layer
(the Project aggregate + migration + repository + read model — blocking both stories) and two stories:
- **US1 — Project Management (US-10, P2)**: create / edit / re-parent / archive / unarchive / delete (with task + child
  dispositions), the sidebar, the project form, and the delete dialog.
- **US2 — Move to Project & Inbox (US-08.AS-05, P1 mechanic)**: the `M` move-to-project action, the Inbox redefinition
  (`GET /api/tasks` narrowed to `project_id IS NULL`), and a project's task list.

This slice realizes the **personal/ownership** authorization branch only (FR-065/FR-068); shared visibility + membership
(FR-066/FR-067) are slice 007. **No new error code** (R12): nesting/preset → 422, foreign → 404, stale → 409.

## Format: `[ID] [P?] [Story] Description`
- **[P]**: Can run in parallel (different files, no dependency on an incomplete task)
- **[Story]**: `[US1]`/`[US2]` for story-phase tasks (Setup/Foundational/Polish carry no story label)

## Path Conventions
Monorepo (per plan.md): backend `apps/api/src/<Project>/…`, backend tests `apps/api/tests/<Project>/…`,
web `apps/web/src/…`, web tests `apps/web/tests/…`.

---

## Phase 1: Setup (Preconditions + frozen preset surface)

- [x] T001 [P] Confirm slice-004 preconditions: `tasks.project_id` (`uuid` NULL) column exists from slice-002 `AddTasks` with **NO FK yet**; the `Task` aggregate provides the client-id + idempotent-insert, `version`/`version_conflict`, and `deleted_at` soft-delete patterns to mirror; the error codes `validation_failed`/`not_found`/`version_conflict` (and `last_owner`) already exist in `TaskFlowDocumentTransformer.cs` + `apps/web/src/lib/api/client.ts` (so reuse leaves `ERROR_UX` exhaustive, R12); `apps/web/src/app/(app)/layout.tsx` has header+main and **no sidebar** today.
- [x] T002 [P] **[FROZEN PRESET SURFACE — ASM-04]** Freeze the closed color + icon preset sets (R10) — the surface the validators (T013/T015) and `projectSchema` tests (T023) assert verbatim. Define the **authoritative** set on the API side (a constant/enum used by the FluentValidation membership rule) and mirror it in `apps/web/src/lib/projectPresets.ts`. Colors/icons are a constrained server-known set, never free-form (Principle XII).

---

## Phase 2: Foundational (blocking prerequisites for US1 + US2)

**Purpose**: the `Project` aggregate, its persistence + migration, repository, and read model — required by every command/query in both stories.

- [x] T003 [P] Create `apps/api/src/TaskFlow.Domain/TaskManagement/ProjectId.cs` — strongly-typed UUIDv7 id with EF value conversion (mirror `TaskId`).
- [x] T004 **(write first — RED)** Create `apps/api/tests/TaskFlow.UnitTests/Domain/TaskManagement/ProjectTests.cs` — `Create(...)` sets name/color/icon/parent, coerces `OwnerId`, `Visibility=personal`, `Version=0`; `Edit(...)` updates the mutable fields together and bumps `Version`; the **one-level nesting** invariant (both failure shapes: parent-is-already-a-child, project-has-children); `Archive`/`Unarchive` (incl. **orphan-on-still-archived-parent**, R9); `SoftDelete` is idempotent (no re-stamp/version-bump if already tombstoned) (covers T005).
- [x] T005 Create `apps/api/src/TaskFlow.Domain/TaskManagement/Project.cs` — aggregate root: `Create`/`Edit`/`Archive`/`Unarchive`/`SoftDelete`; holds the archive-vs-tombstone distinction (R2) and the nesting invariant surface (the cross-row check itself runs in the handler with the repo, R3) (depends on T003; RED via T004).
- [x] T006 [P] Create `apps/api/src/TaskFlow.Application/TaskManagement/ProjectResponse.cs` — read model `{ id, name, color, icon, parentId, visibility, archivedAt, version, createdAt, updatedAt }`; **hides `ownerId`/`deletedAt`**; `From(Project)` projection.
- [x] T007 [P] Create `apps/api/src/TaskFlow.Application/TaskManagement/IProjectRepository.cs` — `FindOwnedAsync` / `FindOwnedIncludingDeletedAsync` / `ListOwnedAsync(includeArchived)` / `ListChildrenAsync(parentId, owner)` / `Add` / `SaveChangesAsync` (mirror `ITaskRepository`).
- [x] T008 Create `apps/api/src/TaskFlow.Infrastructure/Persistence/Configurations/ProjectConfiguration.cs` — `projects` table, all columns (R1 types/nullability), `ix_projects_owner_id` partial `WHERE deleted_at IS NULL`, `owner_id→users(id)` CASCADE, self-ref `parent_id→projects(id)` SET NULL; **and** modify `TaskConfiguration.cs` to add the `project_id→projects(id)` FK (ON DELETE SET NULL — column pre-exists) (depends on T005).
- [x] T009 Implement `apps/api/src/TaskFlow.Infrastructure/Persistence/ProjectRepository.cs` — `IProjectRepository` over EF; translate `DbUpdateConcurrencyException → VersionConflictException` and unique-violation at `SaveChangesAsync` (mirror `TaskRepository`) (depends on T007, T008).
- [x] T010 Generate the EF migration: `dotnet ef migrations add AddProjects` → creates `projects` (+ index + FKs) and adds the `tasks.project_id` FK; **review** it is forward-only/expand-contract (purely additive) and that **no other table is touched** (depends on T008).
- [x] T011 Verify **FR-051** is satisfied for `AddProjects` (the first migration since slice 002): the automatic pre-migration backup + the CI restore-test gate (Constitution VII) MUST run against it — confirm the gate exists and is green; **wire it if absent** (in-scope per plan Complexity Tracking) (depends on T010).

---

## Phase 3: User Story 1 — Project Management (Priority: P2) 🎯

**Goal**: Create projects (name + preset color/icon, optional parent), edit/re-parent within the one-level rule, archive (hidden but reachable to unarchive), and delete with task + child dispositions — all keyboard-driven, owner-scoped, optimistic.

**Independent Test**: Create parent + child; reject a grandchild; edit name/color/icon/parent; re-parent (allow + reject); archive → gone from sidebar; open the Archived disclosure → unarchive (child of still-archived parent returns top-level); delete a project with tasks → choose cascade / move-to-Inbox / archive-with-tasks; delete a parent with children → choose cascade / orphan-to-top. A foreign project id → 404.

### Backend — tests FIRST (RED), then the command verticals

- [x] T012 [P] [US1] **(write first — RED)** Create `apps/api/tests/TaskFlow.IntegrationTests/TaskManagement/CreateProjectTests.cs` (Testcontainers-Postgres) — create round-trip; **idempotent replay** (same id → unchanged); name/color/icon-preset/self-parent → **422**; **foreign parent → 404** (before nesting); parent-is-a-child → **422**; **ALLOW + DENY** (a foreign owner gets 404) (covers T013).
- [x] T013 [US1] Create `apps/api/src/TaskFlow.Application/TaskManagement/CreateProject.cs` (request DTO + command + `CreateProjectValidator` for the command-local checks + idempotent handler coercing `OwnerId` to the caller) **and** add `Create` to `apps/api/src/TaskFlow.Api/Endpoints/ProjectEndpoints.cs` (new file, `PUT /api/projects/{id}`); the **404-before-422** parent resolution + nesting check live in the handler (R3) (depends on T005, T006, T009; RED via T012).
- [x] T014 [P] [US1] **(write first — RED)** Create `apps/api/tests/TaskFlow.IntegrationTests/TaskManagement/EditProjectTests.cs` — edit name/color/icon; **re-parent allow** (AS-08); **re-parent reject** (target is a child, or project has children → 422, AS-09); foreign target → 404; stale `version` → 409; **whole-object replace** (omitted required `parentId` → 422, never a silent un-parent); ALLOW + DENY (covers T015).
- [x] T015 [US1] Create `apps/api/src/TaskFlow.Application/TaskManagement/EditProject.cs` + `ProjectEndpoints.Edit` (`PATCH /api/projects/{id}`) — whole-object replace of name/color/icon/parentId (R4), nesting guard in the handler, `version` concurrency (RED via T014).
- [x] T016 [P] [US1] **(write first — RED)** Create `apps/api/tests/TaskFlow.IntegrationTests/TaskManagement/ArchiveProjectTests.cs` — archive hides from default list but stays in the archived listing; **child disposition** on a parent (cascade vs orphan-to-top, AS-10); unarchive restores; **orphan-on-still-archived-parent** (R9); `version`; ALLOW + DENY (covers T017).
- [x] T017 [US1] Create `apps/api/src/TaskFlow.Application/TaskManagement/ArchiveProject.cs` + `UnarchiveProject.cs` + `ProjectEndpoints` (`PATCH /api/projects/{id}/archive` + `/unarchive`) — archive sets `archived_at`; unarchive clears it and nulls `parentId` if the parent is still archived/deleted (R9); cascade follows the parent's fate (R5) (depends on T005, T009; RED via T016).
- [x] T018 [P] [US1] **(write first — RED)** Create `apps/api/tests/TaskFlow.IntegrationTests/TaskManagement/DeleteProjectTests.cs` — **three task dispositions** (cascade / move_to_inbox / archive_with_tasks); **two child dispositions** (cascade / orphan_to_top); `archive_with_tasks` archives the subtree (no tombstone); tombstone excluded from queries; `version` → 409 (NOT idempotent); ALLOW + DENY (covers T019).
- [x] T019 [US1] Create `apps/api/src/TaskFlow.Application/TaskManagement/DeleteProject.cs` + `ProjectEndpoints.Delete` (`DELETE /api/projects/{id}` with `version`/`taskDisposition`/`childDisposition` query params) — dispositions applied in-transaction **before** the soft-delete tombstone (R5) (depends on T005, T009; RED via T018).
- [x] T020 [P] [US1] **(write first — RED)** Create `apps/api/tests/TaskFlow.IntegrationTests/TaskManagement/ProjectQueriesTests.cs` — `GetMyProjects` returns active (default) vs archived (`?archived=true`, R8), owner-scoped, tombstone-excluded; ALLOW + DENY (covers T021).
- [x] T021 [US1] Create `apps/api/src/TaskFlow.Application/TaskManagement/Queries/GetMyProjects.cs` + `ProjectEndpoints.List` (`GET /api/projects?archived=`) (depends on T006, T009; RED via T020).
- [x] T022 [US1] Stamp the new project operationIds + auto-insert their 401/404/409/422 responses in `apps/api/src/TaskFlow.Api/OpenApi/TaskFlowDocumentTransformer.cs` (**no `ErrorCodes` change** — R12), then regenerate the typed client: `cd apps/web && pnpm gen:api` (API on `localhost:4311`) → `schema.d.ts` gains the project ops + `ProjectResponse`; `pnpm typecheck` green; confirm it matches `contracts/openapi.yaml`; commit (depends on T013, T015, T017, T019, T021).

### Web — tests FIRST (RED), then impl

- [x] T023 [P] [US1] **(write first — RED)** Create `apps/web/tests/unit/project-validation.test.ts` — `projectSchema`: name bounds, color/icon **preset enum**, `parentId` optional on create / required on edit; the disposition enums (covers T024).
- [x] T024 [US1] Create `apps/web/src/lib/validation/project.ts` — `createProjectSchema` + `editProjectSchema` (required `parentId`) over the `projectPresets` enums; export inferred types (depends on T002, T022; RED via T023).
- [x] T025 [P] [US1] **(write first — RED)** Create `apps/web/tests/unit/use-project-mutations.test.ts` — optimistic create/edit/archive/unarchive/delete on the `['projects']` key: `onMutate` patches the tree, `onError` rolls back, the request body/params carry the right fields (covers T026).
- [x] T026 [US1] Create `apps/web/src/hooks/useProjects.ts` (query) + `apps/web/src/hooks/useProjectMutations.ts` (the five optimistic mutations; client-side one-level-nesting prevention message from the loaded tree, R15) (depends on T022, T024; RED via T025).
- [x] T027 [P] [US1] **(write first — RED)** Create `apps/web/tests/unit/sidebar.test.ts` — one-level **tree assembly** from the flat list by `parentId` (R16); archived excluded from the default tree; the Inbox entry renders (covers T028).
- [x] T028 [US1] Create `apps/web/src/components/layout/Sidebar.tsx` (Inbox + one-level tree + minimal "Archived" disclosure, R8) and mount it in `apps/web/src/app/(app)/layout.tsx` (depends on T026; RED via T027).
- [x] T029 [US1] Create `apps/web/src/components/projects/ProjectForm.tsx` — create/edit form: name input + preset color/icon pickers (color never the sole signal, FR-044) + parent selector; dialog focus contract (FR-101); single-key suppression in the name input (FR-031) (depends on T024, T026).
- [x] T030 [US1] Create `apps/web/src/components/projects/DeleteProjectDialog.tsx` — three-way task disposition + child disposition; **states blast radius** (affected task + child counts, Principle VII); dialog focus contract (depends on T026).

**Checkpoint**: US1 — projects can be created, nested, edited, re-parented, archived/unarchived, and deleted with dispositions, fully keyboard-driven and owner-scoped.

---

## Phase 4: User Story 2 — Move to Project & Inbox (Priority: P1 mechanic) 🎯

**Goal**: Press `M` on the selected task → a project selector moves it to a project (or back to the Inbox); `GET /api/tasks` is the Inbox (unprojected tasks); a project view lists its tasks.

**Independent Test**: Select a task, press `M`, choose a project → it moves and leaves the Inbox; press `M` → "Inbox" → it returns; the default list shows only unprojected tasks (existing tasks still appear); open a project → its tasks only.

- [x] T031 [P] [US2] **(write first — RED)** Extend `apps/api/tests/TaskFlow.UnitTests/Domain/TaskManagement/TaskTests.cs` — `MoveToProject(projectId?, utcNow)` sets `ProjectId` (and `null` clears it to Inbox) and bumps `Version` (covers T032).
- [x] T032 [US2] Add `MoveToProject(Guid? projectId, DateTime utcNow)` to `apps/api/src/TaskFlow.Domain/TaskManagement/Task.cs` (RED via T031).
- [x] T033 [P] [US2] **(write first — RED)** Create `apps/api/tests/TaskFlow.IntegrationTests/TaskManagement/MoveTaskToProjectTests.cs` — move to an owned project; move to Inbox (`null`); **both-ownership DENY** (foreign task OR foreign target project → 404); stale `version` → 409; ALLOW (covers T034).
- [x] T034 [US2] Create `apps/api/src/TaskFlow.Application/TaskManagement/MoveTaskToProject.cs` (command/validator/handler — checks ownership of BOTH the task and the target project, R7) + `TaskEndpoints` `PATCH /api/tasks/{id}/project` (depends on T032, T009; RED via T033).
- [x] T035 [P] [US2] **(write first — RED)** Create `apps/api/tests/TaskFlow.IntegrationTests/TaskManagement/InboxAndProjectTasksTests.cs` — `GET /api/tasks` returns only `project_id IS NULL` (Inbox, FR-021/R6), pre-existing tasks still appear; `GET /api/projects/{id}/tasks` returns that project's tasks (owner+project scoped, 404 if foreign); ALLOW + DENY (covers T036).
- [x] T036 [US2] Narrow `apps/api/src/TaskFlow.Application/TaskManagement/Queries/GetMyTasks.cs` with `AND project_id IS NULL` (preserve `ORDER BY position, id`, R6) **and** create `Queries/GetProjectTasks.cs` + `ProjectEndpoints` `GET /api/projects/{id}/tasks` (depends on T009; RED via T035).
- [x] T037 [US2] Modify `TaskResponse.cs` to add nullable `ProjectId` (+ `From` projection, R16); stamp the `moveTaskToProject` + `listProjectTasks` operationIds in `TaskFlowDocumentTransformer.cs`; `pnpm gen:api` → `TaskResponse.projectId` + the two ops; `pnpm typecheck` green; commit (depends on T034, T036).
- [x] T038 [P] [US2] **(write first — RED)** Extend `apps/web/tests/unit/use-task-mutations.test.ts` — the move-to-project optimistic surface (moves a task between the `['tasks']`/project caches; `null` → Inbox; rollback on error) (covers T039).
- [x] T039 [US2] Modify `apps/web/src/hooks/useTaskMutations.ts` — add the move-to-project optimistic mutation carrying `{ projectId, version }` (depends on T037; RED via T038).
- [x] T040 [US2] Create `apps/web/src/components/projects/ProjectSelector.tsx` — the `M` project selector (lists owned projects + Inbox; dialog focus contract, FR-101) (depends on T026, T037).
- [x] T041 [US2] Modify `apps/web/src/components/tasks/TaskRow.tsx` (a project chip when projected; open the selector for the selected task) and `apps/web/src/hooks/useGlobalShortcuts.ts` (bind **`M`** to open the selector for the selected task; suppressed in inputs FR-031) (depends on T039, T040).
- [x] T042 [US2] **(write first — RED, then GREEN)** Create `apps/web/tests/e2e/projects.spec.ts` (Playwright) — AS-01..AS-05, AS-07..AS-11, EC-03, the `M` move (incl. move-to-Inbox), and the Inbox narrowing (AS-06's palette search is slice 013) (depends on T028, T029, T030, T041).

**Checkpoint**: US2 — the `M` mechanic and the Inbox/project task lists are whole; the project journey is end-to-end.

---

## Phase 5: Polish & Cross-Cutting Concerns

- [x] T043 [P] Accessibility pass (Principle II): the project form, `M` selector, delete dialog, and child-disposition prompt follow the **dialog focus contract** (FR-101); preset **color is accompanied by icon + text**, contrast ≥ 4.5:1 (FR-044); no hover-only affordances (FR-046); transitions instant/<100 ms under `prefers-reduced-motion` (FR-047); no AT-binding collision for `M` (FR-045); single-key suppression in the name input (FR-031).
- [x] T044 [P] Confirm SC-003: project create, the `M` move, and archive paint optimistically **< 16 ms**; the one-level-nesting prevention message is computed client-side (no round-trip, R15).
- [x] T045 [P] Security/logging (Principle XII): the project `name` is React-escaped on render (FR-099, no `dangerouslySetInnerHTML`); colors/icons are preset-constrained (R10); FR-050 structured rejection logs carry `ErrorCode`/`Method`/`Path` only — never the name or owner.
- [x] T046 Confirm CI gates green: `pnpm gen:api` clean (project ops + `TaskResponse.projectId` present); the `ERROR_UX satisfies Record<ErrorCode, ErrorUx>` map stays exhaustive with **NO change** (no new errorCode, R12); TS strict + C# nullable/analyzers-as-errors; **every** new handler has an allow + a deny test (Principle VIII/IX).
- [x] T047 Confirm **exactly one** migration (`AddProjects`) in the slice diff (no unexpected migration); confirm the **FR-051** backup-before-migrate + restore-test gate executed green (the first migration since slice 002).
- [x] T048 Run `specs/004-project-management/quickstart.md` validation scenarios end-to-end (lifecycle, dispositions, Inbox/move, authorization, and server-validation tables — all rows).

---

## Phase 6: App-shell wiring completion (owned scenarios — decomposition gap)

**Purpose**: The original T028–T030 built the project components (edit `ProjectForm`, `DeleteProjectDialog`) and T041 the `M` move, but never tasked the **sidebar triggers** that open the edit/delete dialogs, nor a **project-tasks view**. Without them, the owned acceptance scenarios AS-04/EC-03, AS-07/08/09, AS-10, and the move-to-Inbox round-trip have no UI path (they shipped as `test.fixme` living specs). This phase wires the existing components into the app shell.

- [x] T049 [US1] Wire a keyboard-reachable **edit** affordance into `apps/web/src/components/layout/Sidebar.tsx` that opens `ProjectForm` in **edit** mode (dialog "Edit project") seeded with the project — rename/recolor/re-parent + Save persists and reflects in the tree (AS-07/AS-08); the parent picker excludes illegal targets and the form surfaces the inline one-level-nesting message + disables Save when a re-parent would create a grandchild (AS-09, FR-012/FR-049/R15).
- [x] T050 [US1] Wire keyboard-reachable **archive** + **delete** affordances into `Sidebar.tsx`: archive calls the archive mutation; delete opens `DeleteProjectDialog` (dialog "Delete project") with the three task-disposition radios (move-to-Inbox / archive-with-tasks / cascade) and, for a parent, the two child-disposition radios (orphan-to-top / cascade), stating blast radius (AS-04/EC-03/AS-10, Principle VII).
- [x] T051 [US2] Add the project-tasks view `apps/web/src/app/(app)/projects/[id]/page.tsx` — lists that project's tasks via the generated `listProjectTasks` op; each `TaskRow` exposes the "Move … to another project" chip → `ProjectSelector` → choosing "Inbox" moves it back (projectId=null), completing the move-to-Inbox round-trip (R6/R7/R16).
- [x] T052 Convert the 6 `test.fixme` scenarios in `apps/web/tests/e2e/projects.spec.ts` (AS-04/EC-03, AS-07/08/09, AS-10, move-to-Inbox round-trip) to **live, passing** assertions against the now-wired DOM — do NOT weaken assertions; align selectors to the real component DOM where needed.

**Checkpoint**: every owned US-10/US-08 acceptance scenario is delivered end-to-end; the full `projects.spec.ts` is live (no `fixme`).

---

## Dependencies & Execution Order

### Phase dependencies
- **Setup (P1)**: T001 ‖ T002 — start immediately (T002 freezes the preset surface the validators/tests assert).
- **Foundational (P2)**: depends on Setup. **Blocks both stories** — the `Project` aggregate, migration, repository, and read model.
- **US1 (P3)**: depends on Foundational. The project-management journey.
- **US2 (P4)**: depends on Foundational (the `projects` table + FK + repository); independent of US1's UI but shares `gen:api`.
- **Polish (P5)**: depends on US1 + US2.

### Test-First ordering (Red-Green) — strict lower-id within each phase
- Foundational: T004 (ProjectTests) precedes T005 (`Project`).
- US1: T012→T013, T014→T015, T016→T017, T018→T019, T020→T021 (each integration test precedes its command); T023→T024, T025→T026, T027→T028 (web).
- US2: T031→T032, T033→T034, T035→T036, T038→T039; T042 is the E2E (RED then GREEN).

### Critical path
T002 → T003/T004 → T005 → T008 → T009/T010 → T011 → (US1 commands T012..T021) → **T022 gen:api** → (US1 web T023..T030) ‖ (US2 backend T031..T036 → **T037 gen:api** → US2 web T038..T041) → T042 E2E → Polish.
The two `gen:api` joins (T022, T037) are the backend→client serialization points.

### Parallel opportunities
- **Setup**: T001 ‖ T002.
- **Foundational**: T003 ‖ T004; then T006 ‖ T007 after T005.
- **US1 backend**: the RED test files T012 ‖ T014 ‖ T016 ‖ T018 ‖ T020 (different files); each impl follows its own test. The five command verticals touch different files but share `ProjectEndpoints.cs` — serialize the endpoint edits.
- **US1 web**: T023 ‖ T025 ‖ T027 (test files); T029 ‖ T030 after T026.
- **US2**: T031/T033/T035 RED files in parallel; web T040 ‖ (T038→T039).
- **Polish**: T043 ‖ T044 ‖ T045.

### Migration & contract gates
- **One** migration only (`AddProjects`, T010); a second new file under `Persistence/Migrations/` is a defect (T047).
- **FR-051 is live** (T011/T047) — the backup + restore-test gate must run against `AddProjects`.
- Two `gen:api` regens (T022, T037), each CI-diff-gated; `ERROR_UX` unchanged (T046).

---

## Implementation Strategy

### MVP
Foundational (the `Project` aggregate + migration) → US1 (project management) → **STOP & VALIDATE** the lifecycle (quickstart A/B) → US2 (`M` move + Inbox) → validate C → Polish. US1 alone is a demoable increment (projects exist, nest, archive, delete); US2 makes tasks movable into them.

### Notes
- **Test-First is mandatory** (Constitution VIII): the test task has a lower id than the impl it covers — RED, then GREEN. **Every** new data handler ships an **allow AND a deny** test (Principle IX + governance gate).
- **404 before 422** (R3/R13): resolve the parent as owned (foreign → 404, no existence leak) before the nesting check (owned-but-illegal → 422). Same precedence on create/edit/move.
- **Whole-object edit** (R4): `EditProjectRequest.parentId` is required — a name-only edit must re-send the current parent, never silently un-parent.
- **Archive ≠ delete** (R2): `archived_at` is reversible state; `deleted_at` is the terminal tombstone. The 30-second undo **UI** is slice 014 (the tombstone persistence ships here).
- **Inbox narrowing** (R6): `GET /api/tasks` gains only `AND project_id IS NULL`, keeping `ORDER BY position, id` — backward-compatible (all pre-004 tasks stay in the Inbox).
- **No new error code** (R12); **no NodaTime** (no date-relative computation this slice).

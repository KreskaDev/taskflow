---
description: "Task list for Daily Planning (slice 005)"
---

# Tasks: Daily Planning

**Input**: Design documents from `/specs/005-daily-planning/`
**Prerequisites**: plan.md, spec.md, research.md (R1–R13 + the BLOCKER), data-model.md, contracts/openapi.yaml, quickstart.md (all present)

**Tests**: REQUIRED. Constitution v4.0.0 Principle VIII (Test-First) governs, and Principle IX + the governance gate
require **every new data handler to ship an allow AND a deny test**. Test-First is Red-Green-Refactor: **the test task
has a LOWER id than the implementation it covers — write it, watch it fail (RED), then implement (GREEN).**

**Organization**: This slice adds **no entity and no EF migration** — it activates the two reserved Task columns
(`priority`, `description`, already mapped `string?` from the slice-002 `AddTasks` migration; research R2/R3) and
introduces **NodaTime server-side for Warsaw day-boundary computation ONLY** (no column remap, no `Npgsql.NodaTime`
plugin → unchanged model snapshot → **no migration**; research R1/R9). **FR-051 backup-before-migration is therefore a
named NO-OP this slice** — the slice-003 posture, NOT slice-004's LIVE posture. The work splits into a **Foundational**
layer (the `WarsawDayBounds` NodaTime seam + the three new `Task` aggregate behaviors + the `TaskResponse` delta —
prerequisites for the verticals) and two stories:
- **US1 — Daily Planning Session (US-02, P1)** 🎯: the **Today** + **Upcoming** views, the `1`-`4` priority triage, the
  `T` reschedule, the `Space` toggle-done, and the `E` task editor (`Ctrl+Enter` save / `Esc` discard). Owns the
  `G T` (Today) navigation chord (US-02.AS-01). The 5 new backend verticals + the single `gen:api` join live here.
- **US2 — Keyboard Navigation (US-08 subset, P1)**: the `G I` (Inbox) and `G U` (Upcoming) navigation chords.
  **Both stories are P1; US2 is ordered AFTER US1 by dependency** — `G U` navigates to the Upcoming view and `G I` to
  the slice-004 Inbox; US2 is web-only nav wiring over views US1 builds (it is NOT independent of US1).

**⚠ BLOCKER RESOLVED (research open-question #1 → option (a)):** slice 007 (project-sharing-membership) is now
sequenced **before** slice 005 and is in the tree (the `ProjectMembership` aggregate, the `shared` visibility, and the
`ResolveEffectiveRole`/`RequireRole` dispatch-by-visibility policy). The previously-BLOCKED **shared-project membership
+ role** arm (FR-066/FR-067) and its **two SC-016 deny tests** (viewer-mutation-deny, non-member-read-deny) are
therefore **realized in this slice**. The membership arm fills the seam research R6/R10 reserved: the read queries
additionally surface tasks in shared projects the caller is a current member of, and the write commands dispatch on the
containing project's visibility (personal → ownership; shared → `RequireRole(Editor)`: viewer → **403**, non-member →
**404**, editor/owner → allow). This authorization is realized via a small `TaskAccessGuards` helper mirroring the
slice-007 `MembershipGuards`, a non-owner-scoped `ITaskRepository.FindByIdAsync`, and a today/upcoming range-list repo
method scoped `created_by = caller OR project_id ∈ ListProjectIdsForUserAsync(caller)`. The personal/Inbox path is
**provably additive** — identical to slice 002/004 (foreign → 404, owner → allow), proven by the unchanged existing
per-handler suites.

**⚠ DEVIATION from the original plan (T029/T039), per spec L127 (FR-067 verbatim: "a viewer … MUST be denied the
priority/edit/reschedule/toggle-done mutations; write requires editor or owner"):** the reused slice-002 `SetTaskDone`
(the `Space` toggle-done) is now a **CHANGED data handler** — it dispatches by visibility like the three new commands so
an editor member MAY complete a shared task and a viewer is denied **403** (not the owner-only 404). It therefore ships
its **own** allow+deny shared-project tests and is in the **non-author authorization reviewer's scope** (Constitution
IX). The personal path is unchanged (the slice-002 `SetTaskDoneTests` run as the additive-regression proof). The
original "unchanged/inherited, not re-tested" wording in T029/T039 was written while this arm was BLOCKED and is now
superseded; the spec wins over tasks.md (Constitution governance).

**No new error code** (R11): bad priority / bad reschedule instant / over-long description → 422; foreign id / non-member
→ 404; insufficient-role member → 403 (the pre-provisioned `forbidden` code, slice 007); stale token → 409.

## Format: `[ID] [P?] [Story] Description`
- **[P]**: Can run in parallel (different files, no dependency on an incomplete task)
- **[Story]**: `[US1]`/`[US2]` for story-phase tasks (Setup/Foundational/Polish carry no story label)

## Path Conventions
Monorepo (per plan.md): backend `apps/api/src/<Project>/…`, backend tests `apps/api/tests/<Project>/…`,
web `apps/web/src/…`, web tests `apps/web/tests/…`.

---

## Phase 1: Setup (Preconditions + NodaTime adoption)

- [x] T001 [P] Confirm slice-005 preconditions (verify in code, do not infer): `tasks.priority` + `tasks.description` exist as **nullable text** mapped in `apps/api/src/TaskFlow.Infrastructure/Persistence/Configurations/TaskConfiguration.cs` (`HasColumnName("priority")` / `HasColumnName("description")`) and the `Task` aggregate already declares `Priority`/`Description` (private setters, "Reserved (slice 005)") — so activating them is **zero-migration** (R2/R3); `tasks.due_date` is mapped `HasColumnType("timestamp with time zone")` over `DateTime?` with **no NodaTime** (R1); the slice-003 `CreateTaskValidator` due-date rule (pairing invariant + UTC-kind + plausible range) exists to be reused (R4); the slice-004 `MoveTaskToProject` command + `Task.MoveToProject` exist to be reused by `EditTask` (R4); the slice-004 Inbox (`GET /api/tasks` narrowed to `project_id IS NULL`) + its route resolve `G I` (R8); the error codes `validation_failed`/`not_found`/`version_conflict` already exist in `apps/api/src/TaskFlow.Api/OpenApi/TaskFlowDocumentTransformer.cs` + `apps/web/src/lib/api/client.ts` (so reuse leaves `ERROR_UX` exhaustive, R11); `apps/web/src/lib/timezone.ts` (the client Warsaw boundary mirror) and `apps/web/src/lib/dates.ts` (the slice-003 Polish parser) exist to be reused (R7/R8).
- [x] T002 [P] **[NodaTime adoption — boundary computation ONLY]** Add the **`NodaTime`** package reference to `apps/api/src/TaskFlow.Application/TaskFlow.Application.csproj` (research R1). Do **NOT** add `Npgsql.NodaTime`; do **NOT** remap any `timestamptz` column from `DateTime` to `Instant` (that would change the model snapshot → force a migration → flip FR-051 to LIVE — R1-a/R9). NodaTime types are confined to the `WarsawDayBounds` seam (T004/T005). After this task, `dotnet build` restores the package; the EF model snapshot is **unchanged** (no migration generated).

---

## Phase 2: Foundational (blocking prerequisites for the verticals)

**Purpose**: the `WarsawDayBounds` NodaTime seam (the one place the server's Warsaw day boundary lives) + the three new
`Task` aggregate behaviors + the `TaskResponse` read-model delta — required by the Today/Upcoming queries and the
priority/reschedule/edit commands in US1. **No migration** (data-model §7).

- [x] T003 [P] **(write first — RED)** Create `apps/api/tests/TaskFlow.UnitTests/Time/WarsawDayBoundsTests.cs` — against an injected/frozen UTC "now": `StartOfTodayUtc` / `StartOfTomorrowUtc` / `StartOfDayPlusUtc(n)` return the correct UTC instants at the start of the Warsaw calendar day; `WarsawLocalDate(utc)` returns the **Warsaw** `LocalDate` (NOT the truncated UTC date) for an instant near the UTC-midnight seam (the off-by-one this slice exists to prevent, R1/R3); a **DST-boundary** case (a "now" at the late-Mar / late-Oct Warsaw transition) computes the boundary by the **tzdb library**, never fixed-offset arithmetic (FR-092, R13) (covers T004).
- [x] T004 Create `apps/api/src/TaskFlow.Application/Time/WarsawDayBounds.cs` — the ONE server seam for the Warsaw calendar-day boundary (NodaTime): `StartOfTodayUtc` / `StartOfTomorrowUtc` / `StartOfDayPlusUtc(int days)` / `WarsawLocalDate(DateTime utcInstant)`. Implemented `DateTimeZoneProviders.Tzdb["Europe/Warsaw"]`, `LocalDate` → `ZonedDateTime` (start-of-day) → `Instant` → `DateTime` (`DateTimeKind.Utc`); DST by tzdb (R1/data-model §2). Mirrors `apps/web/src/lib/timezone.ts` (FR-092). Depends on T002; RED via T003.
- [x] T005 [P] **(write first — RED)** Extend `apps/api/tests/TaskFlow.UnitTests/Domain/TaskManagement/TaskTests.cs` — `SetPriority(priority?, utcNow)` set (`"P0".."P3"`) / clear (`null`) / **closed-set guard** (out-of-set → throws) + bumps `Version`; `Reschedule(dueDate?, dueHasTime?, utcNow)` sets the due pair / clears (both null) + bumps `Version` (reuse the slice-003 due-date round-trip shape); `EditTask(title, description?, priority?, dueDate?, dueHasTime?, projectId?, utcNow)` **whole-object replace** — reuses `NormalizeTitle` + the closed-set guard + the pairing invariant + `MoveToProject` semantics internally, one `Touch` (covers T006).
- [x] T006 Add `SetPriority`, `Reschedule`, and `EditTask` behaviors to `apps/api/src/TaskFlow.Domain/TaskManagement/Task.cs` (data-model §1) — each sets its field(s) and `Touch`es (`UpdatedAt` + `Version++`); `EditTask` reuses `MoveToProject` internally for the project field (no duplicate move logic, R4). RED via T005.
- [x] T007 [P] Modify `apps/api/src/TaskFlow.Application/TaskManagement/TaskResponse.cs` — add nullable **`priority`** (`"P0".."P3" | null`) and **`description`** (`string | null`) + the `From(Task)` projection lines (R2/R3/R10); both nullable, **NOT** added to `required[]`; continue to hide `created_by`/`deleted_at`. (`isOverdue` is NOT added here — it lives only on the Today read model, §10.)
- [x] T007a **(membership-arm seam — BLOCKER resolved)** Add the dispatch-by-visibility persistence + authorization seams the US1 verticals build on (no migration): (1) `ITaskRepository.FindByIdAsync(TaskId, ct)` — a **non-owner-scoped, non-deleted** single-row load (+ its `TaskRepository` impl) used by the write dispatch to load a task that may live in a shared project the caller does not own; (2) `ITaskRepository.ListDueInRangeReadableAsync(UserId caller, IReadOnlyCollection<ProjectId> sharedProjectIds, DateTime? lowerInclusiveUtc, DateTime upperExclusiveUtc, ct)` — the Today/Upcoming fetch, filtered `due_date IS NOT NULL AND due_date < @upper AND (@lower IS NULL OR due_date >= @lower) AND status NOT IN ('done','cancelled') AND deleted_at IS NULL AND (created_by = @caller OR project_id = ANY(@sharedProjectIds))`; (3) `apps/api/src/TaskFlow.Application/TaskManagement/TaskAccessGuards.cs` — `LoadWritableTaskAsync(taskId, requiredRole, …)` mirroring `MembershipGuards`: load via `FindByIdAsync` → 404 if null; if `task.ProjectId is null` → personal/Inbox → `task.CreatedBy == caller` else 404; else `projects.FindReadableAsync(task.ProjectId, caller)` → 404 if null, and on a `shared` project `members.ListByProjectAsync` + `authorization.RequireRole(project, memberships, requiredRole)` (viewer<Editor → 403, non-member → 404). Reused by T009/T011/T013/T044. RED is the compile failure of the consumers.

---

## Phase 3: User Story 1 — Daily Planning Session (Priority: P1) 🎯

**Goal**: The mouse-free daily loop. Open Today (`G T`); see tasks due today (incl. overdue, flagged) grouped by
project, priority-sorted; toggle done (`Space`); set priority (`1`-`4`); reschedule (`T` → Polish phrase, task leaves
Today); open the editor (`E`), save (`Ctrl+Enter`) or discard (`Esc`). Upcoming (`G U`'s view) is built here; the `G U`
chord itself is US2.

**Independent Test**: Seed tasks across projects/priorities with today's, yesterday's (overdue), and tomorrow's due
dates. `G T` → only due-today + overdue (flagged), grouped by project, priority-sorted (NULL last, date-only as
start-of-day), done/cancelled/no-due excluded. `Space` → done, row leaves Today. `1` → P0, re-sorts. `T` "jutro" Enter
→ moves to tomorrow, disappears from Today. `E` → editor (title focused); `Ctrl+Enter` saves; `Esc` discards. A foreign
task id → 404.

### Backend — tests FIRST (RED), then the command + query verticals

- [x] T008 [P] [US1] **(write first — RED)** Create `apps/api/tests/TaskFlow.IntegrationTests/TaskManagement/SetPriorityTests.cs` (Testcontainers-Postgres, `SharingTestBase`) — set (`"P0".."P3"`) / clear (`null`); **out-of-set → 422** `validation_failed`; stale `version` → 409 `version_conflict`; **personal ALLOW + DENY** (another user's personal task → 404, existence not disclosed); **shared-project arm**: an **editor** member ALLOW (mutates the owner's shared task), a **viewer** member → **403** `forbidden` (SC-016 viewer-mutation-deny), a **non-member** → **404** (covers T009).
- [x] T009 [US1] Create `apps/api/src/TaskFlow.Application/TaskManagement/SetPriority.cs` — request DTO `{ priority, version }` + command + `SetPriorityValidator` (closed set `{P0,P1,P2,P3}|null`) + handler. Authorizes via `TaskAccessGuards.LoadWritableTaskAsync(id, EffectiveRole.Editor, …)` (T007a) — dispatch-by-visibility: personal → ownership-404, shared → `RequireRole(Editor)` (viewer→403, non-member→404). Version check then `Task.SetPriority`. `PATCH /api/tasks/{id}/priority` (R2/R4). Depends on T006, T007a; RED via T008.
- [x] T010 [P] [US1] **(write first — RED)** Create `apps/api/tests/TaskFlow.IntegrationTests/TaskManagement/RescheduleDueDateTests.cs` (`SharingTestBase`) — reschedule to a resolved UTC instant + `dueHasTime` / clear (both null); **pairing invariant** (one set, one null → 422); **non-`Z` kind → 422 NOT 500**; implausible range → 422 (the slice-003 rules reused); stale `version` → 409; **personal ALLOW + DENY** (404); **shared-project arm**: editor ALLOW, viewer → 403, non-member → 404 (covers T011).
- [x] T011 [US1] Create `apps/api/src/TaskFlow.Application/TaskManagement/RescheduleDueDate.cs` — request DTO `{ dueDate, dueHasTime, version }` + command + `RescheduleDueDateValidator` (reuses the slice-003 due-pairing/UTC-kind/range rule) + handler authorizing via `TaskAccessGuards.LoadWritableTaskAsync(id, EffectiveRole.Editor, …)`. `PATCH /api/tasks/{id}/due-date` (R4). Depends on T006, T007a; RED via T010.
- [x] T012 [P] [US1] **(write first — RED)** Create `apps/api/tests/TaskFlow.IntegrationTests/TaskManagement/EditTaskTests.cs` (`SharingTestBase`) — **whole-object replace** round-trip (title + description + priority + due pair + project saved together); **omitted required key → 422** (`title`/`description`/`priority`/`dueDate`/`dueHasTime`/`projectId`/`version`, never a silent null — the slice-004 R4 discipline); out-of-set priority / bad due pair / over-long description (>8000) → 422; `projectId` referencing a **foreign/absent project → 404** (the move-to-project ownership check, applied **only when the target changes**); stale `version` → 409; **personal ALLOW + DENY** (foreign task → 404); **shared-project arm**: editor ALLOW with `projectId` **unchanged** (= the current shared project; the target check is SKIPPED so a legitimate editor edit is not 404'd — see T013), viewer → 403, non-member → 404 (covers T013).
- [x] T013 [US1] Create `apps/api/src/TaskFlow.Application/TaskManagement/EditTask.cs` — request DTO `{ title, description, priority, dueDate, dueHasTime, projectId, version }` (all required keys, nullable values except `title`) + command + `EditTaskValidator` (closed-set priority + the reused due rule + description length ≤ 8000) + handler. Authorizes the task via `TaskAccessGuards.LoadWritableTaskAsync(id, EffectiveRole.Editor, …)`; then **only when `command.ProjectId != task.ProjectId`** (an actual move) resolve the target project as OWNED (`FindOwnedAsync(target, caller)` → foreign/absent → 404) — an **unchanged** `projectId` is NOT a move and skips the target check, so an editor editing a shared task they don't own is not spuriously 404'd (the changed-target case keeps the owner-scoped check: an editor may move only into the Inbox / their own projects). Whole-object replace via `Task.EditTask`/`MoveToProject`. `PATCH /api/tasks/{id}/edit` (R4). Depends on T006, T007a; RED via T012.
- [x] T014 [P] [US1] **(write first — RED)** Create `apps/api/tests/TaskFlow.IntegrationTests/TaskManagement/GetTodayTasksTests.cs` (`SharingTestBase`) — **due-today** (Warsaw) + **overdue-incomplete** included (flagged `isOverdue`); done/cancelled excluded; no-due excluded; **group by project** (Inbox/unprojected = `projectId:null` group); the **R5 order** (priority P0 first, **NULL last**, **date-only as start-of-day**, then createdAt, then id); a **same-Warsaw-day** case (`2026-06-27T21:30:00Z` lands in Today on 2026-06-27, not the UTC day); a **DST-boundary** case (server tier, R13); **personal ALLOW + DENY** — a collection GET, so the personal deny is pure owner-isolation: a foreign owner's personal task is **absent** from the caller's Today; **shared-project read arm**: a **member** (viewer+) of a shared project SEES that project's due-today task in their Today (a distinct `projectId` group) — the **viewer-member-read-ALLOW**; a **non-member** does **NOT** (the SC-016 **non-member-read-deny** = absence, R10) (covers T015).
- [x] T015 [US1] Create `apps/api/src/TaskFlow.Application/TaskManagement/Queries/GetTodayTasks.cs` + the `TodayResponse`/`TodayTaskResponse`/`TodayGroup` read models in `apps/api/src/TaskFlow.Application/TaskManagement/TodayResponse.cs` — zone-aware via `WarsawDayBounds`, **dispatch-by-visibility read arm**: resolve the caller's shared-project ids via `IProjectMembershipRepository.ListProjectIdsForUserAsync(caller)`, then `ITaskRepository.ListDueInRangeReadableAsync(caller, sharedIds, lower:null, upper:@startOfTomorrowUtc, …)` (T007a) — `created_by = caller OR project_id ∈ sharedIds`, a plain UTC range, status not done/cancelled, `deleted_at IS NULL`; derive `isOverdue = due_date < @startOfTodayUtc`; group by project (null = Inbox), groups deterministically ordered (null-first then projectId asc); R5 order within each group, server-side. Empty shared-list degrades cleanly to owner-only. `GET /api/tasks/today` (R5/R6). Depends on T004, T007, T007a; RED via T014.
- [x] T016 [P] [US1] **(write first — RED)** Create `apps/api/tests/TaskFlow.IntegrationTests/TaskManagement/GetUpcomingTasksTests.cs` (`SharingTestBase`) — **tomorrow-in-Upcoming-NOT-Today** (Constitution X partition); the **7-day window** `[start of tomorrow-Warsaw, start of (today+8)-Warsaw)` (day-8 excluded); done/cancelled/no-due excluded; **group by Warsaw `LocalDate`** (`YYYY-MM-DD`, via `WarsawLocalDate` — NOT the truncated UTC date), groups ascending by date; the R5 order within each day; a **DST-boundary** case (server tier); **personal ALLOW + DENY**; **shared-project read arm**: a member sees a shared project's upcoming task, a non-member does not (covers T017).
- [x] T017 [US1] Create `apps/api/src/TaskFlow.Application/TaskManagement/Queries/GetUpcomingTasks.cs` + the `UpcomingResponse`/`UpcomingGroup` read models in `apps/api/src/TaskFlow.Application/TaskManagement/UpcomingResponse.cs` — zone-aware via `WarsawDayBounds`, **dispatch-by-visibility read arm** (same as T015): resolve shared-project ids via `ListProjectIdsForUserAsync(caller)`, then `ListDueInRangeReadableAsync(caller, sharedIds, lower:@startOfTomorrowUtc, upper:@startOfDayPlus8Utc, …)`; group by `WarsawLocalDate`; R5 order server-side. `GET /api/tasks/upcoming` (R5/R6). Depends on T004, T007, T007a; RED via T016.
- [x] T018 [US1] Modify `apps/api/src/TaskFlow.Api/Endpoints/TaskEndpoints.cs` — add `GET /api/tasks/today`, `GET /api/tasks/upcoming`, `PATCH /api/tasks/{id}/priority`, `PATCH /api/tasks/{id}/due-date`, `PATCH /api/tasks/{id}/edit`, dispatching the queries/commands. **Register the literal `/today` + `/upcoming` routes so they WIN over the `/{id}` template** (or constrain `{id}` to a uuid) — data-model §6 routing note. Depends on T009, T011, T013, T015, T017.

### gen:api join — the SINGLE backend→web serialization point (gates ALL web work)

- [x] T019 [US1] Stamp the new operationIds (`getTodayTasks`, `getUpcomingTasks`, `setTaskPriority`, `rescheduleTaskDueDate`, `editTask`) + auto-insert their 401/404/409/422 responses in `apps/api/src/TaskFlow.Api/OpenApi/TaskFlowDocumentTransformer.cs` (**no `ErrorCodes` change** — no new errorCode, R11), then regenerate the typed client: `cd apps/web && pnpm gen:api` (API on `localhost:4311`) → `apps/web/src/lib/api/generated/schema.d.ts` gains the 5 new ops + `TodayResponse`/`UpcomingResponse` + `TaskResponse.priority`/`TaskResponse.description`; `pnpm typecheck` green; confirm it matches `contracts/openapi.yaml`; commit. **This is the one gen:api gate — it blocks every US1 web task AND every US2 nav-web task** (the nav hooks need the generated types). Depends on T018.

### Web — tests FIRST (RED), then impl

- [x] T020 [P] [US1] **(write first — RED)** Extend `apps/web/tests/unit/task-validation.test.ts` — the **priority enum** (`P0`..`P3`|null) and the **edit-form schema** (description length ≤ 8000, the due-pairing rule) in `apps/web/src/lib/validation/task.ts` (covers T021).
- [x] T021 [US1] Modify `apps/web/src/lib/validation/task.ts` — add the priority Zod enum + the `editTaskSchema` (description bound, due pairing) over the generated types; export the inferred types (R2/R3). Depends on T019; RED via T020.
- [x] T022 [P] [US1] **(write first — RED)** Extend `apps/web/tests/unit/task-mutations.test.ts` — the **setPriority / rescheduleDueDate / editTask** optimistic surfaces on `['tasks','today']` / `['tasks','upcoming']` / `['tasks']`: `onMutate` patches, `onError` rolls back, the body/params carry the right fields; **the client view-membership recompute** (frozen Warsaw clock) — a reschedule-to-tomorrow removes the row from Today and adds it to Upcoming; **`Space` toggle-done removes it from both** (R7); a **DST-boundary** case proving client/server parity (the same fact the server asserts in T014/T016, R13) (covers T023).
- [x] T023 [US1] Modify `apps/web/src/hooks/useTaskMutations.ts` — add the `setPriority` / `rescheduleDueDate` / `editTask` optimistic mutations + extend the reused `setTaskDone` (`Space`) path with the **client view-membership recompute** using `apps/web/src/lib/timezone.ts` (the same Warsaw boundary the server uses, FR-092) to move rows across the today/upcoming/inbox keys (R7). Depends on T019, T021; RED via T022.
- [x] T024 [P] [US1] **(write first — RED)** Create `apps/web/tests/unit/today-upcoming.test.ts` — group/order **assembly from the read model**: Today groups by project with `isOverdue` rendered; Upcoming groups by Warsaw `date` ascending; within a group the R5 order (NULL-priority-last, date-only-as-start-of-day) is preserved as the server sent it (covers T025/T026).
- [x] T025 [P] [US1] Create the query hooks `apps/web/src/hooks/useTodayTasks.ts` (`['tasks','today']` → `getTodayTasks`) and `apps/web/src/hooks/useUpcomingTasks.ts` (`['tasks','upcoming']` → `getUpcomingTasks`). Depends on T019; RED via T024.
- [x] T026 [US1] Create `apps/web/src/components/tasks/TodayView.tsx` (project-grouped, priority-sorted, renders the **overdue** flag) and `apps/web/src/components/tasks/UpcomingView.tsx` (day-grouped, next-7-days). Depends on T025; RED via T024.
- [x] T027 [US1] Create `apps/web/src/components/tasks/RescheduleInput.tsx` — the `T` date input: parses the Polish phrase via `apps/web/src/lib/dates.ts` (slice-003), resolves the instant, calls `rescheduleDueDate`; **dialog focus contract** (initial focus, trap, Esc-dismiss, return focus to the originating row, FR-101); single-key suppression while focused (FR-031). Depends on T023.
- [x] T028 [US1] Create `apps/web/src/components/tasks/TaskEditor.tsx` — the `E` editor: title (focused on open) / description / priority / due / project; **dialog focus contract** (FR-101); `Ctrl+Enter` → `editTask` atomic save; `Esc` → discard-all (no request sent), return focus to the row (AS-06/07/08); single-key suppression in the fields (FR-031). Depends on T021, T023.
- [x] T029 [US1] Modify `apps/web/src/components/tasks/TaskRow.tsx` — render the **priority badge** (a P0–P3 label/text always accompanies any color, never color-alone, FR-044) + the **overdue / due** labels; wire `1`-`4` / `T` / `E` / `Space` on the **selected** row (open `RescheduleInput` / `TaskEditor`; `setPriority` / `setTaskDone`). The `Space` toggle-done reuses the slice-002 `SetTaskDone` command — its handler is now **membership-aware** (T044, the BLOCKER-resolved deviation): the personal path is unchanged, the shared path requires editor/owner. Web wiring is unchanged; a viewer's optimistic toggle on a shared task rolls back on the server **403**. Depends on T023, T027, T028.
- [x] T030 [US1] Modify `apps/web/src/hooks/useGlobalShortcuts.ts` — bind the `G T` (Today) navigation chord and the `1`-`4` / `T` / `E` list verbs (single-key suppression in inputs, FR-031, R8). (The `G I` / `G U` chords are US2/T035.) Depends on T029.
- [x] T031 [US1] Create the Today route `apps/web/src/app/(app)/today/page.tsx` (mounts `TodayView`, served by `G T`) and the Upcoming route `apps/web/src/app/(app)/upcoming/page.tsx` (mounts `UpcomingView`; the `G U` chord is wired in US2). Depends on T026, T030.
- [x] T032 [US1] **(write first — RED, then GREEN)** Create `apps/web/tests/e2e/daily-planning.spec.ts` (Playwright) — **US-02 AS-01..AS-08**: `G T` opens Today (AS-01); group-by-project + priority-sort incl. NULL-last + date-only-as-start-of-day (AS-02); `Space` toggle-done + row leaves Today (AS-03, the reused command exercised from Today); `1`→P0 + re-sort (AS-04); `T` "jutro" Enter → moves to tomorrow + disappears from Today (AS-05); `E` opens editor title-focused (AS-06); `Ctrl+Enter` saves (AS-07); `Esc` discards (AS-08); plus the **SC-008 WCAG 2.1 AA audit** on the Today view. Depends on T031.

**Checkpoint**: US1 — the Today/Upcoming views, priority triage, reschedule, toggle-done, and the editor are whole and keyboard-driven; `G T` opens Today.

---

## Phase 4: User Story 2 — Keyboard Navigation (US-08 subset, Priority: P1)

**Goal**: Press `G I` → the Inbox view opens; press `G U` → the Upcoming view opens (next 7 days, grouped by day).

**Builds on US1 (NOT independent)**: both stories are P1, but `G U` navigates to the Upcoming view + `useUpcomingTasks`
hook built in US1, and `G I` resolves to the slice-004 Inbox; US2 is the nav-chord wiring over US1's views.

**Independent Test**: From any view, `G I` → the Inbox (the slice-004 `project_id IS NULL` list) opens; `G U` → Upcoming
opens showing the next 7 days grouped by day.

- [x] T033 [P] [US2] **(write first — RED, then GREEN)** Extend `apps/web/tests/e2e/daily-planning.spec.ts` with the **US-08 AS-01/AS-02** assertions — `G I` opens the Inbox (the slice-004 Inbox route) and `G U` opens the Upcoming view (next 7 days, grouped by day); plus the **SC-008 WCAG 2.1 AA audit** on the Upcoming view (covers T034/T035). Depends on T032.
- [x] T034 [US2] Confirm/wire the **Inbox route** target for `G I` — the slice-004 Inbox (`GET /api/tasks` narrowed to `project_id IS NULL`); add the route entry if the slice-004 Inbox view is not already at a stable path (R8). Depends on T032.
- [x] T035 [US2] Modify `apps/web/src/hooks/useGlobalShortcuts.ts` — bind the `G I` (Inbox, → T034 route) and `G U` (Upcoming, → `apps/web/src/app/(app)/upcoming/page.tsx`) navigation chords on the existing `G`-chord layer (R8). Depends on T031, T034; RED via T033.

**Checkpoint**: US2 — `G I` / `G U` navigate to the Inbox / Upcoming views; the full `G T` / `G I` / `G U` nav set is mouse-free.

---

## Phase 5: Polish & Cross-Cutting Concerns

- [x] T036 [P] Accessibility pass (Principle II / SC-008): the task editor + the `T` reschedule input follow the **dialog focus contract** (initial focus, trap, Esc-dismiss, return focus to the originating row, FR-101); **priority is never the sole signal** (a P0–P3 label/text accompanies any color, contrast ≥ 4.5:1, FR-044); no hover-only affordances (FR-046); transitions instant/<100 ms under `prefers-reduced-motion` (FR-047); no AT-binding collision for `1`-`4`/`T`/`E`/`G _` (FR-045); single-key suppression in the editor fields + the `T` input (FR-031); an optimistic-write rollback routes through the **polite** ARIA live region without stealing focus (FR-101). Confirm the Today + Upcoming views pass the automated WCAG 2.1 AA audit (SC-008).
- [x] T037 [P] Confirm SC-003 / Instant Response: `setPriority`, `rescheduleDueDate`, `setTaskDone`, and `editTask` paint optimistically **< 16 ms**; a mutation that changes **view membership** (reschedule-to-tomorrow, toggle-done) recomputes membership **client-side** via `apps/web/src/lib/timezone.ts` — the **same** Warsaw boundary the server uses (FR-092) — so the optimistic patch equals the authoritative result (R7); the server reconciles/rolls back without clobbering a pending local edit.
- [x] T038 [P] Security/logging (Principle XII): the task `description` (markdown **source**) and `title` rendered in Today/Upcoming/the editor are **React-escaped on render** (FR-099, no `dangerouslySetInnerHTML`) — **no markdown renderer this slice** (R3/R12), so the description renders as escaped raw text; FR-050 structured rejection logs carry `ErrorCode`/`Method`/`Path` only — never the title/description/owner.
- [x] T039 Confirm CI gates green: `pnpm gen:api` clean (the 5 new ops + `TaskResponse.priority`/`.description` present, matches `contracts/openapi.yaml`); the `ERROR_UX satisfies Record<ErrorCode, ErrorUx>` map stays exhaustive with **NO change** (no new errorCode — the 403 reuses the slice-007 `forbidden` code, R11); TS strict + C# nullable/analyzers-as-errors; **every NEW data handler** (`GetTodayTasks`, `GetUpcomingTasks`, `SetPriority`, `RescheduleDueDate`, `EditTask`) has a **personal allow + deny AND a shared-project allow (editor) + deny (viewer 403 / non-member 404)** test (Principle VIII/IX). The reused `SetTaskDone` toggle-done command (spec L127/L172) is now a **CHANGED data handler** (T044) — membership-aware — so it **also** ships its own shared-project allow+deny here (NOT inherited-only); the personal path is regression-proven by the unchanged slice-002 `SetTaskDoneTests`. All five mutating handlers + the two read queries are in the **non-author authorization reviewer's scope** (Constitution IX, authz change).
- [x] T040 Confirm **FR-051 is a NAMED NO-OP** this slice (Principle VII): assert **NO new file appears under `apps/api/src/TaskFlow.Infrastructure/Persistence/Migrations/`** in this slice's diff (the failure mode is an accidental migration flipping FR-051 to LIVE — R9); confirm `Npgsql.NodaTime` was **not** added and no `timestamptz` column was remapped to `Instant` (the model snapshot is unchanged); the pre-migration backup hook stays in place for the next migrating slice.
- [x] T041 Run `specs/005-daily-planning/quickstart.md` validation scenarios end-to-end (A Today/priority, B reschedule/editor, C Upcoming/nav, D zone-aware/DST-boundary on **both** tiers, the server-validation table, the ownership authorization allow+deny rows).

---

## Phase 6: Shared-project membership arm — UNBLOCKED (research open-question #1 → option (a))

**Purpose**: the SC-016 shared-project deny tests the spec mandates (spec L127/L172) — now **realizable** because slice
007 is sequenced before slice 005 (the `ProjectMembership` substrate is in the tree). The membership arm itself is
implemented **inside the US1 verticals** (T007a + T009/T011/T013/T015/T017 + T044) — this phase is the **consolidated
SC-016 matrix** that proves it, written **Test-First as part of the per-handler RED steps** (so each test still precedes
its impl). The dispatch denies exactly like slice 007: non-member → **404** (existence not disclosed across the
membership boundary), insufficient-role member (viewer attempting a write) → **403** `forbidden`.

- [x] T042 [US1] SC-016 **viewer-mutation-deny** — a **viewer** member of a shared project attempting `SetPriority` / `RescheduleDueDate` / `EditTask` / toggle-done (`SetTaskDone`) on that project's task MUST be denied **403** `forbidden` (FR-067; write requires editor/owner). Realized as the shared-project DENY cases folded into the per-handler RED suites (T008/T010/T012 + T044's `SetTaskDoneTests` additions); the matched **editor ALLOW** cases prove the dispatch is a real role gate, not a blanket 404. (Cross-checked against the slice-007 `RoleOperationDenyMatrixTests` posture.)
- [x] T043 [US1] SC-016 **non-member-read-deny** — a **non-member** reading another project's task via Today/Upcoming MUST NOT see it (FR-066; on leave/remove/unshare ALL access is lost — the residual-attribution revoke-all rule). Realized as the shared-project read DENY cases in T014/T016 (the task is **absent** from a non-member's Today/Upcoming), paired with the **viewer-member-read-ALLOW** (a current member sees it). A collection read, so the deny is absence, not a 404 body.
- [x] T044 [US1] **(CHANGED handler — the BLOCKER-resolved deviation, spec L127)** Extend `apps/api/src/TaskFlow.Application/TaskManagement/Commands/SetTaskDone.cs` to dispatch by visibility via `TaskAccessGuards.LoadWritableTaskAsync(id, EffectiveRole.Editor, …)` (replacing the owner-only `FindOwnedAsync`): personal path unchanged (foreign → 404, owner → allow); shared path requires editor/owner (viewer → 403, non-member → 404). **(write first — RED)** extend `apps/api/tests/TaskFlow.IntegrationTests/TaskManagement/SetTaskDoneTests.cs` with the shared-project editor-ALLOW + viewer-403 + non-member-404 cases; the **existing personal cases run unchanged as the additive-regression proof**. Depends on T007a; RED before the handler edit.

---

## Dependencies & Execution Order

### Phase dependencies
- **Setup (P1)**: T001 ‖ T002 — start immediately (T002 adds NodaTime for the `WarsawDayBounds` seam).
- **Foundational (P2)**: depends on Setup. **Blocks the US1 verticals** — the `WarsawDayBounds` seam (queries), the three `Task` behaviors (commands), the `TaskResponse` delta (both).
- **US1 (P3)**: depends on Foundational. The full daily loop + the single `gen:api` join (T019).
- **US2 (P4)**: depends on **US1** (NOT just Foundational) — `G U` needs the Upcoming view + `useUpcomingTasks` (T026/T025/T031) and the gen:api types (T019); `G I` needs the slice-004 Inbox route (T034). Ordered after US1 by dependency, not by priority (both are P1).
- **Polish (P5)**: depends on US1 + US2.
- **Membership arm (P6, UNBLOCKED)**: realized inside US1 — T007a is a Foundational prerequisite for the membership dispatch; the SC-016 matrix (T042/T043) is folded into the per-handler RED suites; T044 (SetTaskDone) depends on T007a.

### Test-First ordering (Red-Green) — strict lower-id within each phase
- Foundational: T003→T004 (`WarsawDayBounds`); T005→T006 (`Task` behaviors); T007a (membership seam — RED is the consumers' compile failure).
- US1 backend: T008→T009, T010→T011, T012→T013, T014→T015, T016→T017 (each test precedes its impl); the shared-project allow/deny cases (T042/T043) live in those same RED test files so they precede the membership-arm impl. T044: its `SetTaskDoneTests` shared cases precede the handler edit.
- US1 web: T020→T021, T022→T023, T024→{T025,T026}; T032 is the E2E (RED then GREEN).
- US2: T033 (E2E RED) → {T034, T035} GREEN.

### Critical path
T002 → T003/T004 ‖ T005/T006 → T007 → (US1 backend T008..T017) → **T018 endpoints** → **T019 gen:api (the single gate)** → (US1 web T020..T032) → (US2 web T033..T035) → Polish.
**T019 is the one backend→client serialization point** — every web task (US1 AND US2) waits on it.

### Parallel opportunities
- **Setup**: T001 ‖ T002.
- **Foundational**: the two test files T003 ‖ T005 (different files); then T004 ‖ T006; T007 is independent (different file) and can run alongside.
- **US1 backend**: the RED test files T008 ‖ T010 ‖ T012 ‖ T014 ‖ T016 (different files); each impl follows its own test. The 5 impls (T009/T011/T013/T015/T017) touch different files but **all share `TaskEndpoints.cs` (T018) — serialize T018** after them.
- **US1 web**: T020 ‖ T022 ‖ T024 (test files); after T019, the hooks T025 ‖ the validation T021 ‖ the mutations T023; T027/T028 after T023; T029 after both.
- **Polish**: T036 ‖ T037 ‖ T038.

### Migration & contract gates
- **ZERO** migrations this slice (T040); **any** new file under `Persistence/Migrations/` is a defect (the FR-051-no-op failure mode).
- **FR-051 is a named NO-OP** (T040) — no schema change to back up before; the backup hook stays in place for the next migrating slice.
- **One** `gen:api` regen (T019), CI-diff-gated; `ERROR_UX` unchanged, no new errorCode (T039).

---

## Implementation Strategy

### MVP
Foundational (the `WarsawDayBounds` NodaTime seam + the three `Task` behaviors + the `TaskResponse` delta) → US1 (the
5 backend verticals → **T019 gen:api** → the Today/Upcoming views + the priority/reschedule/edit/toggle-done surfaces +
the editor) → **STOP & VALIDATE** the daily loop (quickstart A/B/D) → US2 (`G I` / `G U` nav) → validate C → Polish.
US1 alone is a demoable increment (the Today/Upcoming views + the full editor work; `G T` opens Today); US2 completes
the nav set.

### Notes
- **Test-First is mandatory** (Constitution VIII): the test task has a lower id than the impl it covers — RED, then GREEN. **Every** new data handler ships an **allow AND a deny** test (Principle IX + governance gate); deny = another user's personal task → **404** (existence not disclosed).
- **The two SC-016 shared-project deny tests are REALIZED and green** (T042/T043, BLOCKER resolved — slice 007 sequenced first): viewer-mutation-deny → **403** and non-member-read-deny → **absent** from Today/Upcoming, with the editor-member-allow alongside.
- **NodaTime is boundary-computation ONLY** (R1): confined to `WarsawDayBounds`; **no `Npgsql.NodaTime`, no column remap, no migration** (T002/T040). The SQL filters a **plain UTC `due_date` range** (zone-free); the zone math collapses to UTC instants in the seam.
- **The Upcoming group key is the Warsaw `LocalDate`** (`WarsawLocalDate`), **never** the truncated UTC date (the off-by-one this slice exists to prevent, R1/R3).
- **DST-boundary on BOTH tiers** (R13): one server integration assertion (T014/T016) + one client Vitest assertion (T022) prove the FR-092 identical-client/server rule.
- **Whole-object edit** (R4): every `EditTaskRequest` field is a required key — an omitted field is a **400** (binding-layer rejection of a missing System.Text.Json `required` member, the app-wide behavior shared with slice-004 `EditProjectRequest`), never a silent null. The load-bearing invariant is "rejected + row untouched".
- **Routing** (data-model §6): register the literal `/today` + `/upcoming` routes so they win over `/{id}` (T018).
- **No new error code** (R11): 422 / 404 / 409 are all existing codes; `ERROR_UX` stays exhaustive with no change (T039).
- **AS-03 `Space` toggle-done is now membership-aware** (T044, the BLOCKER-resolved deviation per spec L127/FR-067): `SetTaskDone` dispatches by visibility like the new write commands (editor MAY complete a shared task; viewer → 403; non-member → 404). The personal path is unchanged and regression-proven by the slice-002 `SetTaskDoneTests`; the shared arm has its own allow+deny (`SetTaskDoneSharedAuthzTests`). It also gets web wiring (T029), the optimistic membership-removal (T023), and E2E coverage from the Today context (T032).

---
description: "Task list for Task Capture (slice 002)"
---

# Tasks: Task Capture

**Input**: Design documents from `/specs/002-task-capture/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/openapi.yaml (all present)

**Tests**: REQUIRED for this slice. Constitution v4.0.0 Principle VIII (Test-First) and SC-013/SC-016
mandate an **allow AND a deny** integration test per data handler, a domain unit-test suite for the
aggregate, the optimistic-mutation recipe unit test, and the E2E acceptance scenarios. Test tasks are
first-class and follow Red-Green-Refactor: **in the user-story phases the test tasks have LOWER ids than
the implementation they cover — write them, watch them fail (RED), then implement (GREEN).** Foundational
tests are grouped after the unit they cover for readability but are tagged "(write first — RED)".

**Organization**: Tasks are grouped by user story. This slice has two P1 stories:
- **US1 — Daily Task Capture** (`C` → type → Enter; Esc cancels) → the MVP increment.
- **US8 — Keyboard Navigation & Shortcuts** (↑/↓ navigate, `?` help, single-key suppression) plus the
  *operate-on-selected* mechanics that ship here but whose canonical scenarios are owned by later
  slices (`Space` toggle, `E` rename, `Del` soft-delete, `Alt+↑/↓` reorder).

This slice is **additive over slice 001** — it reuses the Wolverine handler pattern, `AppDbContext` +
migrations, the BFF→API carrier + proxy, the OpenAPI transformer pipeline, the ProblemDetails contract,
and the web `Dialog`/`LiveRegion`/`Toast` primitives. It adds NOTHING to the global authorization wiring.

## Format: `[ID] [P?] [Story] Description`
- **[P]**: Can run in parallel (different files, no dependency on an incomplete task)
- **[Story]**: `[US1]` or `[US8]` for story-phase tasks (Setup/Foundational/Polish carry no story label)

## Path Conventions
Monorepo (per plan.md): backend `apps/api/src/<Project>/…`, backend tests `apps/api/tests/<Project>/…`,
web `apps/web/src/…`, web tests `apps/web/tests/…`.

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Pull the additive dependencies and resolve the one open accessibility decision before any code freezes the shortcut map.

- [x] T001 [P] Add web deps to `apps/web/package.json` and lockfile: `@tanstack/react-virtual` (10k-row windowing), `uuid` v11 (exposes `v7()`), `fractional-indexing` (`generateKeyBetween`) — `cd apps/web && pnpm add @tanstack/react-virtual uuid fractional-indexing`
- [x] T002 [P] Add `WolverineFx.FluentValidation` to `apps/api/src/TaskFlow.Api/TaskFlow.Api.csproj` — `cd apps/api/src/TaskFlow.Api && dotnet add package WolverineFx.FluentValidation`
- [x] T003 **[BLOCKING DECISION — RESOLVED]** Verified the `Alt+↑/↓` reorder chord (FR-045/FR-102) against the target browser + screen-reader matrix. **FROZEN: `Alt+↑/↓` (`Alt+ArrowUp`/`Alt+ArrowDown`) — SAFE, confirmed as the binding** (the back/forward conflict is `Alt+Left/Right` horizontal only; the vertical pair is free, and the W3C ARIA APG rearrangeable-listbox example uses this exact chord). Recorded with evidence + load-bearing mitigations in `specs/002-task-capture/research.md` **R18**. Mitigations T054/T058 MUST honor: `preventDefault()` on the listbox keydown (Safari page-scroll), chord active only on listbox focus (R11 gate), keep a plain `role=listbox` (not combobox), expose `aria-keyshortcuts`. T054/T056/T058 are now UNBLOCKED.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: The `Task` aggregate, its persistence + migration, the error-contract additions, the FluentValidation/Wolverine wiring, and the shared web primitives. **No user story can begin until this phase is complete.**

**⚠️ CRITICAL**: This is the shared backbone for BOTH stories.

### Domain (TaskManagement bounded context)

- [x] T004 [P] Create strongly-typed id `TaskId` in `apps/api/src/TaskFlow.Domain/TaskManagement/TaskId.cs` (mirror `UserId`, but **no server `New()`** — `From(Guid)` only; the id is client-supplied)
- [x] T005 [P] Create `TaskStatus` enum (`backlog|todo|in_progress|done|cancelled`) in `apps/api/src/TaskFlow.Domain/TaskManagement/TaskStatus.cs`
- [x] T006 Create the `Task : AggregateRoot<TaskId>` aggregate in `apps/api/src/TaskFlow.Domain/TaskManagement/Task.cs` — private EF ctor + private ctor + `static Create(TaskId, UserId createdBy, string title, string position, DateTime utcNow)`; behavior methods `Rename`/`MarkDone`/`MarkBacklog`/`Reorder`/`SoftDelete` (each stamps `updatedAt` + bumps `version`); invariants (default `backlog`; `completedAt` iff `done`; immutable `createdBy`; idempotent `SoftDelete`; title trimmed-non-empty ≤500 `ArgumentException` backstop); the 7 reserved nullable fields as plain auto-props (depends on T004, T005)
- [x] T007 [P] Create the scheduled reaper message `ReapDeletedTask(TaskId, DateTime DeletedAtInstant)` in `apps/api/src/TaskFlow.Domain/TaskManagement/Events/ReapDeletedTask.cs`
- [x] T008 [P] **(write first — RED)** Domain unit tests in `apps/api/tests/TaskFlow.UnitTests/Domain/TaskManagement/TaskTests.cs` — title bounds (empty/>500 throw), default `backlog`, `MarkDone`/`MarkBacklog` stamp/clear `completedAt`, immutable `createdBy`, every mutator bumps `version` + stamps `updatedAt`, `SoftDelete` idempotent (covers T006)

### Application seams

- [x] T009 [P] Create `NotFoundException` (→ 404 `not_found`) in `apps/api/src/TaskFlow.Application/Errors/NotFoundException.cs`
- [x] T010 [P] Create `VersionConflictException` (→ 409 `version_conflict`) in `apps/api/src/TaskFlow.Application/Errors/VersionConflictException.cs`
- [x] T011 [P] Create `ITaskRepository` persistence seam in `apps/api/src/TaskFlow.Application/TaskManagement/ITaskRepository.cs` (mirror `IUserRepository`; include both a generic owner-scoped + non-deleted load AND a tombstone-inclusive owner-scoped load for DELETE)

### Infrastructure (persistence + migration)

- [x] T012 Create `TaskConfiguration : IEntityTypeConfiguration<Task>` in `apps/api/src/TaskFlow.Infrastructure/Persistence/Configurations/TaskConfiguration.cs` — `ToTable("tasks")`; `id` HasConversion + `ValueGeneratedNever()`; `status` `HasConversion<string>()` default `'backlog'`; `created_by` `UserId` conversion + FK `users(id)` `.OnDelete(DeleteBehavior.Cascade)`; `version` `.IsConcurrencyToken()`; `position` `.UseCollation("C")`; 4 temporal cols `timestamptz`/`DateTimeKind.Utc`; 7 reserved nullable cols; `builder.Ignore(t => t.DomainEvents)`; partial composite index `ix_tasks_created_by_position` on `(created_by, position)` `.HasFilter("deleted_at IS NULL")` with `position` under `COLLATE "C"` (depends on T006)
- [x] T013 Modify `apps/api/src/TaskFlow.Infrastructure/Persistence/AppDbContext.cs` — add `public DbSet<Task> Tasks => Set<Task>();` (configurations auto-picked via existing `ApplyConfigurationsFromAssembly`) (depends on T006)
- [x] T014 Create `TaskRepository : ITaskRepository` in `apps/api/src/TaskFlow.Infrastructure/Persistence/TaskRepository.cs` (mirror `UserRepository`; implement both load shapes from T011) (depends on T011, T013)
- [x] T015 Generate the EF Core migration `AddTasks` → `apps/api/src/TaskFlow.Infrastructure/Persistence/Migrations/*_AddTasks.cs` (`dotnet ef migrations add AddTasks --project src/TaskFlow.Infrastructure --startup-project src/TaskFlow.Api`). **Review checklist (MUST verify):** the `position` **column** carries `COLLATE "C"` (the serving index inherits the column's collation — no separate `COLLATE` token on the index DDL is needed or emitted; verified that `ORDER BY position` resolves byte-ordinal and the index serves it); NO unique constraint on `position`; `version` is the concurrency token; partial index filter is `deleted_at IS NULL`; the `created_by → users(id)` FK is `ON DELETE CASCADE` (NOT default RESTRICT) (depends on T012, T013)
- [x] T016 **(write first — RED)** Regression test `apps/api/tests/TaskFlow.IntegrationTests/TaskManagement/AccountDeletionCascadeTests.cs` (or extend slice-001 `DeleteAccountTests`) — deleting an account that owns tasks succeeds AND the tasks are gone (FK `ON DELETE CASCADE` = complete erasure, Constitution XI) (depends on T015)

### API wiring (shared across both stories)

- [x] T017 Modify `apps/api/src/TaskFlow.Api/Middleware/ProblemDetailsMiddleware.cs` — map `NotFoundException → 404 not_found` and `VersionConflictException → 409 version_conflict` (today it maps only 401/403/422/500) (depends on T009, T010)
- [x] T018 Modify `apps/api/src/TaskFlow.Api/Program.cs` — `AddScoped<ITaskRepository, TaskRepository>()`; `opts.UseFluentValidation()` (NEW — not wired in slice 001); `opts.PublishMessage<ReapDeletedTask>().ToLocalQueue("task-reaper")` (DurabilityMode.Solo already set); add `ReapDeletedTask` to the `AuthorizationMiddleware` exclusion predicate alongside `AccountDeletionRequested` (depends on T007, T014)
- [x] T019 Modify `apps/api/src/TaskFlow.Api/OpenApi/TaskFlowDocumentTransformer.cs` — add `version_conflict` to `ErrorCodes[]` and document the task operations' `404`/`409`/`422` responses (NO `403` for any task op this slice). **Do NOT hand-add the `TaskResponse`/request-body component schemas** — the built-in .NET generator auto-emits typed DTO schemas from the `TaskEndpoints` signatures (verified: slice-001 `UserProfile`/`EnsureUser` appear in the generated schema without any transformer entry); hand-building them would overwrite the accurate auto-emitted shape and silently drift from the real C# serialization (Constitution VI). The transformer only restores exception-driven elements not derivable from signatures (ProblemDetails, operationIds, the 401/404/409/422 responses). NOTE: the per-operation response edits no-op until the endpoints exist (T033/T051) — `SetOperation` only enriches paths already in the document (depends on T017)

### Shared web primitives

- [x] T020 [P] Create `apps/web/src/lib/id.ts` — UUIDv7 minter wrapping `uuid` `v7()` (MUST emit v7; never `crypto.randomUUID()`)
- [x] T021 [P] **(write first — RED)** `apps/web/tests/unit/id.test.ts` — asserts the minter output is a valid v7 UUID (version nibble = 7) (covers T020)
- [x] T022 [P] Create `apps/web/src/lib/position.ts` — wraps `fractional-indexing` `generateKeyBetween` with the pinned alphabet; `between(left, right)` helper (both nullable)
- [x] T023 [P] **(write first — RED)** `apps/web/tests/unit/position.test.ts` — `between(null,null)`, `between(null, head)` (prepend), `between(a,b)` monotonic under byte-order (covers T022)
- [x] T024 [P] Create the **client-side Zod title schema** in `apps/web/src/lib/validation/task.ts` — title trimmed-non-empty, ≤ 500 chars (FR-001/FR-093, Constitution VI "Zod at every trust boundary"); consumed by the capture (T039) and rename (T058) composers, mirroring the server `FluentValidation` rule
- [x] T025 [P] **(write first — RED)** `apps/web/tests/unit/task-validation.test.ts` — empty-after-trim rejected, 500 accepted, 501 rejected (covers T024)
- [x] T026 Modify `apps/web/src/lib/api/client.ts` — extend the `ERROR_UX` map with a `version_conflict` entry (FR-049 message + recovery)
- [x] T027 **(EXECUTED IN US1, immediately after T034 — NOT during Foundational; listed here only because it pairs with T026 on the same file)** Enforce `ERROR_UX` exhaustiveness **at compile time**: type the map in `apps/web/src/lib/api/client.ts` as `satisfies Record<ErrorCode, ErrorUx>`, deriving `ErrorCode` from the generated union (`type ErrorCode = NonNullable<components["schemas"]["ProblemDetails"]["errorCode"]>`), so `tsc` errors if any code is unmapped. A runtime Vitest test cannot iterate a type-level union, so the sound enforcement is the compiler (the existing TS-strict CI type-check, not a bespoke gate) — this IS the canonical exhaustiveness mechanism for this slice (plan.md/data-model.md/quickstart.md/research.md describe it). **Why deferred to US1:** `version_conflict` only enters the *generated* union after the first `pnpm gen:api` (T034); applying this retype during Foundational would leave `tsc` RED until T034. Foundational therefore ends with T026's entry under the existing `Record<string, ErrorUx>` typing (fully green); this `satisfies` retype lands once T034 has run. **Fallout:** switching from the current `Record<string, ErrorUx>` drops the string index signature, so the existing `ERROR_UX[errorCode]` lookup in `mapError` needs a typed accessor (cast `errorCode` to `ErrorCode`, or keep a `FALLBACK` helper). (depends on T026, T034)
- [x] T028 Modify `apps/web/src/components/ui/Toast.tsx` — add a small queue + auto-dismiss + coalescing layer, polite + no focus steal (FR-101). **Use a SINGLE announcer**: route toast text through Toast's own `role=status`/`aria-live=polite` region OR the shared `LiveRegion` — not both (double-announcement hazard). Pick one and document it in the component.

**Checkpoint**: Aggregate + schema + migration + error contract + web primitives ready (the `version_conflict` `ERROR_UX` entry is added under its existing `Record<string, ErrorUx>` typing and is fully green; the compile-time `satisfies Record<ErrorCode, ErrorUx>` retype — T027 — is deferred to US1, applied right after T034's first `pnpm gen:api`). User stories can begin.

---

## Phase 3: User Story 1 — Daily Task Capture (Priority: P1) 🎯 MVP

**Goal**: Press `C`, type a title, Enter → the task is created (client-minted UUIDv7, newest-first) and paints optimistically at the top of the list; Esc cancels with no task created.

**Independent Test**: Launch the app, press `C`, type a title, press Enter → the task appears at the top of the list; press `C` then Esc → no task created and focus returns to the list (US-01.AS-01/06/07, EC-01).

### Tests for User Story 1 (write FIRST — RED — ensure they FAIL)

- [x] T029 [P] [US1] `apps/api/tests/TaskFlow.IntegrationTests/TaskManagement/CreateTaskTests.cs` — allow (own create), foreign-id → 404, idempotent replay — **PUT the same id+owner with a DIFFERENT title and assert the stored title is UNCHANGED and `version` did NOT bump** (proves insert-if-not-exists is NOT a replace; a same-title replay would pass a blind upsert and miss the bug), client UUIDv7 id round-trips, empty/>500 title → 422, **malformed/empty `position` rank → 422**, no-JWT → 401 (reuse `IntegrationTestBase` + `TestJwtHelper`) (covers T031/T033)
- [x] T030 [P] [US1] `apps/api/tests/TaskFlow.IntegrationTests/TaskManagement/GetMyTasksTests.cs` — allow (only own tasks, ordered by position,id), deny/exclude (another user's tasks absent), soft-deleted excluded, no-JWT → 401 (covers T032/T033)

### Implementation for User Story 1 (backend → contract → web)

- [x] T031 [P] [US1] Create `CreateTask` command + static handler + `CreateTaskValidator` in `apps/api/src/TaskFlow.Application/TaskManagement/Commands/CreateTask.cs` — `CreateTaskValidator` validates BOTH `title` (trimmed-non-empty, ≤500) AND `position` (non-empty + valid fractional-indexing rank format) → `422 validation_failed`. **Extract the rank-format check as a shared `PositionRank` validator/helper in the `TaskManagement` application namespace** so create (here) and reorder (T048) share one rule; handler is insert-if-not-exists by client id: no row → insert; own row → idempotent replay (return existing UNCHANGED); foreign/tombstoned id → `NotFoundException` (404); PK unique-violation re-resolved via find-then-decide. Also define the lean `TaskResponse` read-model DTO (incl. `version` to round-trip the optimistic token; excludes `deleted_at` + reserved cols) in the `TaskManagement` application namespace, shared by create + list (depends on T014, T018; RED via T029)
- [x] T032 [US1] Create `GetMyTasks` query + handler in `apps/api/src/TaskFlow.Application/TaskManagement/Queries/GetMyTasks.cs` — `WHERE created_by = caller AND deleted_at IS NULL ORDER BY position, id`; projects the lean `TaskResponse` read model (defined in T031) only (no `deleted_at`, no reserved cols) (depends on T014, **T031** for the shared `TaskResponse` DTO; RED via T030)
- [x] T033 [US1] Create `apps/api/src/TaskFlow.Api/Endpoints/TaskEndpoints.cs` with the two capture/list endpoints — `PUT /api/tasks/{id}` (`createTask`) and `GET /api/tasks` (`listTasks`), thin Wolverine.Http adapters delegating to `IMessageBus` (depends on T031, T032)
- [x] T034 [US1] Regenerate the typed client — `cd apps/web && pnpm gen:api` (API running) → updates `apps/web/src/lib/api/generated/schema.d.ts` with `createTask`/`listTasks` + the auto-emitted `TaskResponse` schema + `version_conflict` in the `errorCode` union; commit the regenerated file (depends on T033)
- [x] T035 [P] [US1] Create `apps/web/src/hooks/useTasks.ts` — TanStack Query `GET /api/tasks` on the single `['tasks']` key (depends on T034)
- [x] T036 [P] [US1] **(write first — RED)** `apps/web/tests/unit/use-task-mutations.test.ts` — Vitest unit test of the optimistic **create** recipe ONLY: `onMutate` snapshots `['tasks']` + paints the new row optimistically at the top; `onError` rolls back to the snapshot in place; `onSettled` invalidates. **Scoped to create so the US1 vitest suite is fully GREEN at the US1 checkpoint** (Constitution VIII — a failing suite blocks merge, and US1 is declared independently shippable). The rename/toggle/reorder/delete + once-only `409 version_conflict` intent-reapply recipe assertions are authored in US8 at the start of T057 (write-first — RED — then implement), NOT here (covers T037)
- [x] T037 [US1] Create `apps/web/src/hooks/useTaskMutations.ts` (create only this story) — optimistic create via `onMutate`/`onError`/`onSettled` on `['tasks']`; client mints id (`lib/id.ts`) + newest-first rank (`lib/position.ts` `between(null, head)`); paints row at top then reconciles/rolls back (depends on T034, T020, T022; RED via T036)
- [x] T038 [US1] Create `apps/web/src/components/tasks/TaskList.tsx` + `apps/web/src/components/tasks/TaskRow.tsx` — `role=listbox` (tabIndex=0) + `role=option` rows with stable ids and accessible names (FR-043), `@tanstack/react-virtual` windowing; render-only baseline (selection/operate added in US8) (depends on T035)
- [x] T039 [US1] Create `apps/web/src/components/tasks/TaskCapture.tsx` — global `C` → `Dialog.tsx` focus contract (with an accessible dialog name, FR-043); **synchronous mount + `.focus()` ≤16 ms** (no network/lazy-import on the `C` path); Enter creates (validates via the T024 Zod schema, then calls T037), Esc cancels + restores focus to the invoker (US-01.AS-01/06/07) (depends on T037, T024)
- [x] T040 [US1] Modify `apps/web/src/app/(app)/page.tsx` — replace the placeholder with the live `TaskList` + `TaskCapture`; accessible empty-Inbox state hinting `C` (EC-01); remove the stale slice-001 `T044` comment in this file while here (depends on T038, T039)
- [x] T041 [US1] Create `apps/web/tests/e2e/tasks.spec.ts` — capture opens focused, Enter creates and the row paints at the top (newest-first), Esc cancels with no task + focus restored (US-01.AS-01/06/07, EC-01) (depends on T040)

**Checkpoint**: US1 is independently functional — a user can capture tasks and see them listed newest-first. This is a shippable MVP.

---

## Phase 4: User Story 8 — Keyboard Navigation & Shortcuts (Priority: P1)

**Goal**: Navigate the list with ↑/↓ (visible indicator on `aria-selected`), open the `?` help overlay, and have single keys typed in text inputs treated as text. Also ships the operate-on-selected mechanics (`Space` toggle, `E` rename, `Del` soft-delete + rollback, `Alt+↑/↓` reorder) with their backend handlers, even though their canonical acceptance scenarios are owned by later slices.

**Independent Test**: With tasks present, press ↑/↓ to move the selection (visible indicator); press `?` to open then Esc to dismiss the help overlay; focus the capture/rename input and press `C`/`E`/`1` → characters are typed, not interpreted (US-08.AS-03/07/09). Operate mechanics verified per the quickstart table.

### Tests for User Story 8 (write FIRST — RED — ensure they FAIL)

- [x] T042 [P] [US8] `apps/api/tests/TaskFlow.IntegrationTests/TaskManagement/RenameTaskTests.cs` — allow, deny (other user → 404), stale-version → 409, empty/>500 → 422 (covers T046)
- [x] T043 [P] [US8] `apps/api/tests/TaskFlow.IntegrationTests/TaskManagement/SetTaskDoneTests.cs` — allow, deny → 404, stale-version → 409, `completedAt` stamp on done + clear on backlog, idempotent under repeated desired-state (covers T047)
- [x] T044 [P] [US8] `apps/api/tests/TaskFlow.IntegrationTests/TaskManagement/ReorderTaskTests.cs` — allow, deny → 404, stale-version → 409, equal-rank tie-break by `id`, malformed rank → 422 (covers T048)
- [x] T045 [P] [US8] `apps/api/tests/TaskFlow.IntegrationTests/TaskManagement/DeleteTaskTests.cs` — allow (soft-delete sets `deleted_at`, excluded from list), deny (foreign/absent → 404), idempotent second-delete of own tombstone → 204, reaper hard-deletes after the window, reaper no-ops on a restored/cleared tombstone (covers T049/T050)

### Implementation for User Story 8 (backend → contract → web)

- [x] T046 [P] [US8] Create `RenameTask` command + handler + `RenameTaskValidator` in `apps/api/src/TaskFlow.Application/TaskManagement/Commands/RenameTask.cs` — owner-scoped + non-deleted load (→404), version compare (→409), title ≤500/non-empty (→422), stamps `updatedAt` (depends on T014, T018; RED via T042)
- [x] T047 [P] [US8] Create `SetTaskDone` command + handler in `apps/api/src/TaskFlow.Application/TaskManagement/Commands/SetTaskDone.cs` — desired-state `done|backlog` (idempotent, not a blind flip); `done` stamps `completedAt`, `backlog` clears it; version compare (→409) (depends on T014, T018; RED via T043)
- [x] T048 [P] [US8] Create `ReorderTask` command + handler in `apps/api/src/TaskFlow.Application/TaskManagement/Commands/ReorderTask.cs` — server validates rank **format** (alphabet + parseable) only via the shared `PositionRank` validator from T031 (never generates ranks); sole writer under the version guard (→409) (depends on T014, T018, T031 for the shared `PositionRank` validator; RED via T044)
- [x] T049 [US8] Create `DeleteTask` command + handler in `apps/api/src/TaskFlow.Application/TaskManagement/Commands/DeleteTask.cs` — **version-free**; **tombstone-inclusive** owner-scoped load (distinguishes own-tombstone → idempotent 204 no-op from foreign/absent → 404); sets `deleted_at`, bumps `version`, and publishes scheduled `ReapDeletedTask` (+30 s) in the same outbox transaction (depends on T014, T018; RED via T045)
- [x] T050 [US8] Create `ReapDeletedTaskHandler` in `apps/api/src/TaskFlow.Application/TaskManagement/Commands/ReapDeletedTaskHandler.cs` — idempotent + restore-aware: hard-delete ONLY if the row still exists AND `deleted_at` is non-null AND unchanged from the scheduled instant; otherwise no-op (a slice-014 restore wins the race). **No allow/deny test** — the reaper is queue infrastructure with no `HttpContext`/caller, so it is intentionally outside Constitution VIII's "data handler" allow+deny rule (covered instead by the reaper-hard-delete + restore-skip cases in T045) (depends on T049; RED via T045)
- [x] T051 [US8] Extend `apps/api/src/TaskFlow.Api/Endpoints/TaskEndpoints.cs` (created in T033) — add `PATCH /api/tasks/{id}/title` (`renameTask`), `PATCH /api/tasks/{id}/status` (`setTaskDone`), `PATCH /api/tasks/{id}/position` (`reorderTask`), `DELETE /api/tasks/{id}` (`deleteTask`). **Also completes the deferred T019 transformer work for ALL SIX task ops in one pass**: assign the friendly operationIds (`createTask`/`listTasks`/`renameTask`/`setTaskDone`/`reorderTask`/`deleteTask`) + documented per-op 404/409/422 responses via `SetOperation`, so the generated client and checked-in `contracts/openapi.yaml` agree (US1 currently emits auto operationIds `PUT_api_tasks_id`/`GET_api_tasks` — functionally fine for the path-based client, reconciled here) (depends on T033, T046, T047, T048, T049)
- [x] T052 [US8] Regenerate the typed client — `cd apps/web && pnpm gen:api` → `schema.d.ts` now carries all six operations (`renameTask`/`setTaskDone`/`reorderTask`/`deleteTask`); commit the regenerated file; confirm it matches the checked-in `contracts/openapi.yaml` surface (depends on T051)
- [x] T053 [P] [US8] **(write first — RED)** `apps/web/tests/unit/shortcuts.test.ts` — single-key suppressed while a text input is focused; modifier chords still pass (covers T054)
- [x] T054 [US8] Create `apps/web/src/hooks/useGlobalShortcuts.ts` — app-shell keydown gate: if `document.activeElement` ∈ {input, textarea, contenteditable, role=textbox} only modifier chords pass (FR-031/EC-08/AS-09); dispatches `C`/↑/↓/Space/`E`/`Del`/`?`/`Alt+↑/↓` (frozen binding from T003) (depends on T003; RED via T053)
- [x] T055 [US8] Extend `apps/web/src/components/tasks/TaskList.tsx`/`TaskRow.tsx` (created in T038) — ↑/↓ update `selectedIndex` + `scrollToIndex`; `aria-activedescendant` on the stable container; **force-include the selected index in the rendered window** so it stays mounted/addressable when scrolled out by wheel/scrollbar (SC-010/SC-011); selection indicator styled on `aria-selected`, not `:focus` (FR-042) (depends on T038, T054)
- [x] T056 [US8] Create `apps/web/src/components/tasks/ShortcutsHelp.tsx` — `?` overlay using the `Dialog.tsx` focus contract; static table of all slice-002 shortcuts (uses the frozen reorder binding from T003) (depends on T003, T054)
- [x] T057 [US8] **(write the recipe test FIRST — RED — then implement)** First extend `apps/web/tests/unit/use-task-mutations.test.ts` (created in T036) with the rename/toggle/reorder/delete + once-only `409 version_conflict` intent-reapply assertions and watch them FAIL; then extend `apps/web/src/hooks/useTaskMutations.ts` (created in T037) — optimistic rename/toggle/reorder/delete on `['tasks']` (snapshot full ordering so a rolled-back delete reappears in place); on `409 version_conflict` refetch + reapply intent ONCE (rename re-stamps typed title; toggle no-ops if server already reflects intent; reorder recomputes `between()` from fresh neighbors) (depends on T037, T052, T026)
- [x] T058 [US8] Wire operate mechanics into `TaskRow.tsx`/`TaskList.tsx` — `Space` toggle, `E` inline rename (validated via the T024 Zod schema; ≤500/non-empty, 422 surfaced), `Del` soft-delete with optimistic removal + rollback-in-place + FR-049 toast, `Alt+↑/↓` reorder (frozen chord from T003) — all via T057 (depends on T055, T057, T024)
- [x] T059 [US8] Extend `apps/web/tests/e2e/tasks.spec.ts` (created in T041) — ↑/↓ navigation with visible indicator (AS-03), `?` overlay open/Esc-dismiss + focus return (AS-07), single-key suppression in inputs (AS-09), Space toggle, `E` rename, `Del` soft-delete + rollback-in-place, `Alt+↑/↓` reorder survives refetch, virtualization-focus (scroll selection out by wheel/scrollbar → selected row stays mounted/`aria-activedescendant` resolves AND ↑/↓ still works) (depends on T041, T058)

**Checkpoint**: Full keyboard surface complete — navigate, help, and operate on tasks entirely by keyboard.

---

## Phase 5: Polish & Cross-Cutting Concerns

**Purpose**: Verify the measurable success criteria and cross-cutting principles across both stories.

- [x] T060 [P] Accessibility pass: correct ARIA roles + accessible names/labels on the listbox, rows, capture dialog, and `?` overlay (FR-043); visible focus/selection indicator on `aria-selected` (FR-042); text contrast ≥ 4.5:1 (FR-044); `prefers-reduced-motion` → transitions instant/<100 ms (FR-047); no hover-only affordances (FR-046)
- [x] T061 [P] Performance: seed a per-user 10k-task dataset and verify **first contentful paint < 1 s + time-to-interactive < 2.5 s from a warm backend (SC-002)**, 60 fps virtualized scroll (SC-010), browser tab < 300 MB (SC-011), and `C`-to-paint < 16 ms (SC-003). _(Seed shape is a flat per-user list — no shared-project overlap exists until slice 007; the constitution working-set anchor is satisfied for this slice's ownership-only scope per plan.)_
- [x] T062 Verify server single-entity writes p95 < 200 ms against a representative dataset (SC-012); confirm the single `ix_tasks_created_by_position` partial index serves the list query as one range scan
- [x] T063 Confirm the CI regen-and-diff gate is green (`pnpm gen:api` clean) and `version_conflict` is present in the generated `errorCode` union; confirm the `ERROR_UX` `satisfies Record<ErrorCode, ErrorUx>` typing compiles (T027); confirm SC-007 (TS strict + C# nullable/analyzers-as-errors CI gates green) and SC-004 (no NEW third-party runtime data dependency — the T001 deps are client/build-time only; data still flows solely through the API + Postgres)
- [x] T064 [P] Verify FR-050 structured logging carries only `ErrorCode`/`Method`/`Path` (never the carrier or task title) and FR-099 output-encoding (title rendered escaped; no `dangerouslySetInnerHTML`)
- [x] T065 [P] Verify the FR-051 backup-before-migrate hook fires before the `AddTasks` migration (Constitution VII MUST). For a brand-new `tasks` table it is effectively a no-op, but the hook MUST run and the restore path stay intact — assert the hook executed in the deploy/migration step
- [x] T066 Run the `specs/002-task-capture/quickstart.md` validation scenarios end-to-end (all rows in the validation table)

---

## Dependencies & Execution Order

### Phase dependencies
- **Setup (P1)**: no dependencies — start immediately (T003 is a blocking *decision* for the reorder wiring only).
- **Foundational (P2)**: depends on Setup — **blocks both user stories**.
- **US1 (P3)**: depends on Foundational. The MVP.
- **US8 (P4)**: depends on Foundational; **builds on US1's list** (T055/T057/T058/T059 extend `TaskEndpoints`/`TaskList`/`TaskRow`/`useTaskMutations`/`tasks.spec.ts` created in US1). Run US1 → US8.
- **Polish (P5)**: depends on US1 + US8.

### Test-First ordering (Red-Green)
- The **lower-id-than-the-impl-it-covers** rule is enforced in the user-story phases (US1/US8). The Foundational phase groups each test immediately AFTER the unit it covers for readability, so its test ids are *higher* (T008→T006, T016→T015, T021→T020, T023→T022, T025→T024) — but each is tagged **"(write first — RED)"** and MUST still be authored and made to fail before its target is implemented. Numbering ≠ authoring order in Foundational; the RED tag governs. (T027 is NOT a RED test — it is the compile-time exhaustiveness retype, deferred for *execution* to US1 after T034; it is listed under Foundational only for file cohesion with T026.)
- US1: **T029/T030 (handler tests) precede T031/T032/T033 (impl)**; **T036 (create-recipe test) precedes T037 (impl)** — strict lower-id. T036 is scoped to the create recipe so the US1 suite is fully green at the checkpoint.
- US8: **T042–T045 (handler tests) precede T046–T051 (impl)**; **T053 precedes T054** — strict lower-id. T057 extends `use-task-mutations.test.ts` with the rename/toggle/reorder/delete + 409-reapply assertions **as a write-first RED step inside the task** (test authored and watched fail before the hook is extended).

### Critical-path within Foundational
T004/T005 → T006 → T012 → (T013) → T014 ; T009/T010 → T017 → T019 ; T007 + T014 → T018 ; T012/T013 → T015 → T016.

### Story independence note
US1 is independently shippable. US8 is a *superset experience* that deliberately reuses US1's list components, so it is sequenced after US1 rather than developed in true isolation (both are P1; capture precedes operate).

### Parallel opportunities
- **Setup**: T001 ∥ T002 (T003 in parallel as a decision).
- **Foundational**: T004 ∥ T005 ∥ T007 ∥ T008 (domain) ; T009 ∥ T010 ∥ T011 (app seams) ; T020/T021 ∥ T022/T023 ∥ T024/T025 (web primitives, independent files). T026 (Foundational) and T027 (deferred to US1, after T034) both touch `client.ts` — sequential, not parallel; T027 depends on T026 + T034.
- **US1**: T029 ∥ T030 (test files) ; then T031 → T032 (T032 reuses T031's `TaskResponse` DTO, so it follows T031, not parallel) ; T035 ∥ T036 parallel-capable after T034.
- **US8**: T042 ∥ T043 ∥ T044 ∥ T045 (test files) ; then T046 ∥ T047 ∥ T048 (different command files; T049/T050 share the reaper publish).

---

## Parallel Example: User Story 1

```bash
# 1. Write the failing handler tests FIRST (parallel — different files):
Task: "CreateTaskTests.cs — allow, foreign→404, idempotent replay, uuidv7, position→422, no-JWT 401"  # T029
Task: "GetMyTasksTests.cs — allow, other-user excluded, soft-deleted excluded, no-JWT 401"            # T030

# 2. Then the handlers (T031 first — it defines the shared TaskResponse DTO that T032 reuses):
Task: "CreateTask.cs command + handler + CreateTaskValidator + TaskResponse DTO (insert-if-not-exists)" # T031
Task: "GetMyTasks.cs query + handler (ownership-scoped, soft-delete-excluding)"                         # T032 (after T031)
```

---

## Implementation Strategy

### MVP first (US1 only)
1. Phase 1 Setup → 2. Phase 2 Foundational (CRITICAL — blocks everything) → 3. Phase 3 US1 → **STOP & VALIDATE** capture works end-to-end → demo. This is the atomic unit of value.

### Incremental delivery
Setup + Foundational → US1 (capture + list, MVP, demo) → US8 (full keyboard nav + operate, demo) → Polish (perf/a11y/contract gates).

### Notes
- Test-First is mandatory (Constitution VIII + SC-013/SC-016): in the story phases the test tasks have lower ids than the implementation they cover — write the test, watch it fail (RED), then implement (GREEN). Every handler ships an **allow + a deny** test.
- One `['tasks']` query key throughout; snapshot the full ordering in optimistic mutations so a rolled-back delete reappears in place.
- Title validation is enforced at BOTH trust boundaries: client Zod (T024) and server FluentValidation (T031/T046) — they must stay in lockstep (non-empty after trim, ≤ 500).
- `COLLATE "C"` must be on the `position` column **and** its index (T012/T015) or list ordering drifts silently.
- The `Alt+↑/↓` reorder chord is FROZEN (T003 resolved; see research.md R18) — implement it per R18's mitigations (preventDefault on listbox keydown, listbox-focus-only, plain role=listbox, aria-keyshortcuts).
- Reaper (`ReapDeletedTask`) is server-authoritative infrastructure on the durable queue — never a REST endpoint; it must be in the auth-exclusion predicate (T018) or it dead-letters.

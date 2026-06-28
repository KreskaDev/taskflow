# Data Model: Task Assignment (slice 008)

**Input**: `spec.md`, `research.md` (R1–R11), constitution v4.0.0, and the slice-002..007 substrate (the `tasks` table, the slice-007 `ProjectMembership` + policy seams, the slice-005 `TaskAccessGuards` write path).

This slice **realizes the `assignees` attribute of ENT-01 (Task)** for **shared-project tasks only** (FR-069). It adds **one new persisted relation** (`task_assignees`) — so it ships **one EF migration** and **FR-051 is LIVE** (R1/R9; the slice-004/007 posture, NOT the slice-005 no-op). It adds the `SetTaskAssignees` command, the `TaskAssigned` domain event, the "Assigned to me" query, the `TaskResponse.assignees` delta, and the assignment-cleanup hook in the slice-007 membership-removal flows.

---

## 1. Entities

### ENT-01 — Task (owned by slice 002) — `assignees` activated (shared-only)

The Task aggregate gains an **assignee set** — zero or more `UserId`, each a **current member** of the task's shared project (FR-069). Modeled as a child collection of the `Task` aggregate (one-aggregate-per-transaction, ADR-0003), persisted in a new join table; assignment changes ride the task's optimistic `version`.

| Field | Type | Change this slice | Constraints / validation |
|---|---|---|---|
| `Assignees` | `IReadOnlyCollection<UserId>` | **NEW** — read/written by `SetTaskAssignees` (R2). Persisted in `task_assignees`. | A **set** (no duplicates — composite PK). Each id MUST be a current `ProjectMembership` of the task's project, verified at the handler (R4); a non-member → 422. Empty on personal-project tasks (assignment never applies). |

**New aggregate behavior** (mutates the set, raises `TaskAssigned` on a real delta, `Touch`es only on a real change):
- `SetAssignees(IReadOnlyCollection<UserId> desired, UserId actor, DateTime utcNow)` — **whole-set replace** (R2). Computes `added = desired − current`, `removed = current − desired`. If both empty → **no-op** (no `Touch`, no event — idempotent, R3). Else applies the set, `Touch`es (`UpdatedAt` + `Version++`), and raises **one** `TaskAssigned { taskId, projectId, addedAssigneeIds, removedAssigneeIds, actorUserId = actor }`. The **member-validity** and **shared-only** checks are enforced UPSTREAM by the handler (R4) — the aggregate records the validated set. (`AddAssignee`/`RemoveAssignee` may exist as private/internal helpers; the public behavior is the set replace.)

**Modified behavior — `MoveToProject` now clears assignees on a real project change (R5).** Assignment is scoped to the task's project's membership (FR-069), so moving a task to a different project — or to the Inbox (`projectId = null`) — **clears the assignee set** (a structural cleanup, no `TaskAssigned` event). This covers BOTH move paths: `MoveTaskToProject` (the `M` action) and `EditTask`'s `projectId` field (slice 005 — which moves to the Inbox with no ownership check, so any editor could otherwise strand assignees on a now-personal task). Without this, a personal/Inbox task could carry assignees, violating FR-069 and the `TaskResponse.assignees`-empty-for-personal contract. (`MoveToProject` is slice-004 code modified here — a cross-slice touch.)

**Reused, unchanged:** all other Task behaviors (slice 002–005). Assignment does not touch status/priority/due.

### ENT — `TaskAssignee` (the join row; not a standalone aggregate)

A pure relation row, owned by the `Task` aggregate. No behavior, no version of its own (the Task `version` guards it).

| Column | Type | Notes |
|---|---|---|
| `task_id` | `uuid` | FK → `tasks(id)` **ON DELETE CASCADE** (task hard-delete / reaper cleans up). Part of PK. |
| `user_id` | `uuid` | FK → `users(id)` **ON DELETE CASCADE** (account-deletion erasure cascade, FR-085 — automatic). Part of PK. |

**PK** = `(task_id, user_id)` — enforces set-uniqueness (a user is assigned at most once per task). No surrogate key, no timestamps (the assignment instant is not a slice-008 requirement; the `TaskAssigned` event carries the temporal signal for slice 017).

---

## 2. Value objects / new types

- **No new value object.** Assignees are a set of the existing `UserId`. The join row is a relation, not a value object.
- **`TaskAssigned` domain event** (R3) — `{ TaskId, ProjectId, IReadOnlyCollection<UserId> AddedAssigneeIds, IReadOnlyCollection<UserId> RemovedAssigneeIds, UserId ActorUserId }`. Raised by `Task.SetAssignees` on a real delta; drained from the aggregate's `DomainEvents` and published via `IMessageContext` (the slice-007 `DomainEventDispatch` pattern). Idempotent (only on a real change). Consumed by slice 017 (this slice ships a **no-op handler** so the publish is routed, R3).

---

## 3. EF mapping (the boundary)

- **`task_assignees`** mapped as the `Task.Assignees` collection (EF `OwnsMany`/skip-navigation or an explicit join entity over `UserId`/`TaskId` value-converted to `uuid`, mirroring the slice-007 `ProjectMembership` id conversions). Composite key `(task_id, user_id)`. Both FKs `ON DELETE CASCADE`.
- **No change to the `tasks` table columns** — assignees live entirely in the join table. The EF model snapshot gains the one new table → exactly one migration (`AddTaskAssignees`).
- The collection loads with the task (the aggregate boundary). **Every** task-loading repository method MUST `Include(t => t.Assignees)` — the Inbox list, the project task list, the single-row loads, AND the slice-005 Today/Upcoming (`ListDueInRangeReadableAsync`) — because `TaskResponse.assignees` is a required field (R7); a missing `Include` silently emits an empty array. The "Assigned to me" read (R6) and the `TaskResponse.assignees` projection (R7) read it.
- **Domain-event drain**: `Task` raises `TaskAssigned` via the `AggregateRoot<T>.DomainEvents` collection; the existing `DomainEventDispatch` helper is `Project`-typed, so the `SetTaskAssignees` handler uses a generalized overload / inline drain (publish each event via `IMessageContext`, then `ClearDomainEvents`) — the same publish pattern, applied to the Task aggregate.

---

## 4. Command surface (R2/R4)

| Command | Trigger | Endpoint | Body | Notes |
|---|---|---|---|---|
| `SetTaskAssignees` (NEW) | the assignee picker (AS-01/AS-02) | `PATCH /api/tasks/{id}/assignees` | `{ assigneeIds: uuid[], version }` | **Whole-set replace** (R2). Authorized via `TaskAccessGuards.LoadWritableTaskAsync(EffectiveRole.Editor)` PLUS: shared-only (personal/Inbox → **404**, R4); every `assigneeId` a current member (non-member → **422**); self-assignment allowed. Computes the delta, raises `TaskAssigned`. Stale `version` → 409. |

**Validation (`SetTaskAssigneesValidator`)** — boundary checks only: `assigneeIds` well-formed uuids, no duplicates (the set), bounded size (e.g. ≤ the project's member count / a sane cap). The **member-validity** check is a cross-row handler check (needs the project's membership set), not a command-local rule.

**Authorization decision path** (handler):
1. `TaskAccessGuards.LoadWritableTaskAsync(id, EffectiveRole.Editor, …)` → personal foreign → 404; shared viewer → 403; non-member → 404; editor/owner → task.
2. **Shared-only**: if the task's project is null/personal → **404** (no assignment surface — the slice-007 `/members`-exists-only-on-shared posture).
3. `version` compare → 409 if stale.
4. Load the project's membership set (`ListByProjectAsync` ∪ owner anchor); every `assigneeId` MUST be a member → else **422** `validation_failed` (field error on `assigneeIds`).
5. `Task.SetAssignees(validatedSet, actor: caller, utcNow)` → delta + event; persist (the `version`-conflict backstop at `SaveChangesAsync` → 409).

---

## 5. Read model & query (R6/R7)

### `TaskResponse` delta
- Gains **`assignees: uuid[]`** — the assignee user ids (R7). **Always present** (empty array for personal/unassigned tasks); a **required** array field. Ids only (no names/avatars — the roster supplies those, Constitution XI/XII). No `created_by`/`deleted_at` leak (the read-model rule).

### `GetAssignedToMe` — `GET /api/tasks/assigned`
- Caller-scoped (R6): the caller's tasks in **shared** projects where the caller is **both** a current member-of-the-project **and** an assignee (`task_assignees.user_id = caller`), excluding done/cancelled, `deleted_at IS NULL`. Grouped by project, ordered by the slice-005 R5 task order within each group. Membership gates access (assignee is provenance only) — a user who lost membership sees nothing.
- **"member-of-the-project" = membership row OR owner**: the readable shared-project set = `ListProjectIdsForUserAsync(caller)` ∪ `{ caller's shared projects WHERE owner = caller }`. The owner has no membership row, so without the owned-shared union an owner who self-assigns (allowed, spec scenario 6) would never see the task — a write/read asymmetry. A `ProjectRepository` owned-shared-ids seam supplies the second set.
- **`AssignedResponse`** (nested-group envelope, mirroring the slice-005 Today read model): `{ groups: [ { projectId, tasks: [TaskResponse] } ] }`.

---

## 6. Migration Plan — **ONE migration (`AddTaskAssignees`); FR-051 LIVE**

- **`AddTaskAssignees`** (the only migration): create `task_assignees (task_id uuid, user_id uuid, PK(task_id,user_id), FK task_id→tasks(id) CASCADE, FK user_id→users(id) CASCADE)`. Additive (new table only; `tasks` unchanged). The EF model snapshot gains exactly this table.
- **FR-051 is LIVE** (R9): the CI deploy job's `scripts/backup.sh → dotnet ef database update → scripts/restore-test.sh` gate covers this migration. This slice **confirms** the gate fires (it is already wired from slice 001/004/007); it does not re-wire it. Verify **exactly one** new file under `Persistence/Migrations/` in the diff.

---

## 7. Assignment cleanup on membership loss (R5)

The slice-007 membership-removal handlers gain an assignment-clear step, transactional with the membership mutation:

| Flow (slice-007 handler) | Added cleanup |
|---|---|
| `UnshareProject` (already `RemoveAllForProjectAsync`) | `ITaskRepository.ClearAssigneesForProjectAsync(projectId)` — delete ALL `task_assignees` for the project's tasks (the project reverts to personal; assignment no longer applies). |
| `RemoveMember` / `LeaveProject` | `ITaskRepository.ClearAssigneesForUserInProjectAsync(projectId, userId)` — delete that user's `task_assignees` rows across the project's tasks. |
| Account deletion (FR-085) | **Automatic** via the `user_id` FK `ON DELETE CASCADE` — no handler change. |
| **Task moves project / to Inbox** (`MoveTaskToProject`, `EditTask` projectId change) | **`Task.MoveToProject` clears the assignee set** on a real project change (§1) — assignment is project-scoped (FR-069); a personal/Inbox task carries no assignees. Structural, no event. Tested on both move paths. |
| **Delete shared project with `move_to_inbox`** (`DeleteProject`) | The bulk `MoveProjectTasksToInboxAsync` (`ExecuteUpdate`) bypasses the aggregate, so `DeleteProject`'s `move_to_inbox` branch ALSO calls `ClearAssigneesForProjectAsync(projectId)` in the same transaction. (`cascade` disposition needs no clear — tasks are soft-deleted; the `task_id` FK cascade clears rows at reap.) |

These are set-based `ExecuteDelete` bulk maintenance ops (like the slice-004 `MoveProjectTasksToInboxAsync`/`OrphanChildrenAsync`), run in the same per-message transaction as the membership change — no window where access is revoked but the assignment lingers.

---

## 8. State transitions

**No new lifecycle.** Assignees are a set attribute, not a state machine. `SetAssignees` is a set mutation (bumps `version` only on a real delta). The `TaskAssigned` event is a side-signal, not a state. The cleanup (§7) is a derived consequence of membership change.

---

## 9. Authorization scoping (deny-by-default — R4)

- **Branch**: shared-project membership + role only (assignment exists only on shared tasks). Deny-by-default at the handler (FR-068), dispatched by visibility (FR-065).
- **Write** (`SetTaskAssignees`): `RequireRole(Editor)` (viewer → 403, non-member → 404); personal task → 404; non-member assignee → 422. `createdBy`/assignee are **provenance only** (FR-066).
- **Read** (`GetAssignedToMe`): scoped `task_assignees.user_id = caller AND project_id ∈ (ListProjectIdsForUserAsync(caller) ∪ caller's owned-shared project ids)` — membership-OR-ownership gates (the owner has no membership row, §5); a stale assignee row after membership loss leaks nothing.
- **Read-model leak rule**: `TaskResponse` exposes assignee **ids** only; no `created_by`/`deleted_at`.
- **Test coverage (Constitution VIII + governance gate)**: `SetTaskAssignees` ships allow + the SC-016 deny matrix (viewer-403, non-member-404, personal-404, non-member-assignee-422); `GetAssignedToMe` ships allow + deny (non-member/non-assignee absent; membership-loss hides) **plus the owner-self-assign-sees-it case** (the owned-shared union); the slice-007 cleanup handlers ship an assignment-cleared assertion; `EditTask`/`MoveTaskToProject` ship a **move-clears-assignees** assertion; and a normal (non-assigned) read path (e.g. Today or the project list) asserts a shared task **carries its assignees** (the eager-load).

---

## 10. What is unchanged

- The `tasks` table columns (assignees live in `task_assignees`); the slice-007 `ProjectMembership` + policy + `MembershipGuards`/`TaskAccessGuards`; the slice-005 Today/Upcoming; the `version`/`version_conflict` machinery; the BFF/auth/`ICurrentUser` resolution; the error contract — **no new errorCode** (R8); the slice-001 CSP/security headers; **no new secrets** (FR-100).

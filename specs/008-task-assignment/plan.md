# Implementation Plan: Task Assignment

**Branch**: `008-task-assignment` | **Date**: 2026-06-28 | **Spec**: `specs/008-task-assignment/spec.md`

**Input**: Feature specification from `specs/008-task-assignment/spec.md`

## Summary

Delivers **task assignment on shared-project tasks** (US-13): multiple **assignees** (members of the task's project) added/removed via a keyboard-operable picker, an **"Assigned to me"** view listing the caller's assigned tasks across shared projects, and **no assignment control on personal-project tasks** (FR-069/AS-04). Assigning raises a `TaskAssigned` domain event (delta + actor, idempotent) consumed by slice 017 (notifications); this slice raises it but delivers no notifications (FR-070/spec L52). Assignment is **authorized by dispatch-by-visibility** — shared-only, editor/owner to write, the assignee **must be a current member**, and assignee is **provenance only** (membership decides access; leave/remove/unshare revokes access AND clears assignments).

This is the **first slice to add a persisted relation to the `Task` aggregate** — the `task_assignees` join table (R1) — so unlike slice 005 it ships **one real EF migration** (`AddTaskAssignees`) and **FR-051 (backup-before-migration) is LIVE** (the slice-004/007 posture; the CI `backup → migrate → restore-test` gate applies, already wired). It reuses, unchanged, the slice-007 `ProjectMembership` + `ResolveEffectiveRole`/`RequireRole` policy and the slice-005 `TaskAccessGuards` dispatch-by-visibility write path, and extends the slice-007 membership-removal handlers (unshare/remove/leave) with an assignment-clear step.

## Technical Context

**Language/Version**: TypeScript (strict) / Next.js 15 (App Router), React 19; C# 13 / .NET 9 / ASP.NET Core.

**Primary Dependencies**:
- Backend (NEW this slice): **none** — reuses EF Core + Npgsql (the `task_assignees` join table + its migration), Wolverine + Wolverine.Http (the `SetTaskAssignees` command + the `GetAssignedToMe` query + the `TaskAssigned` event on a durable local queue), `WolverineFx.FluentValidation` (the assignee-set validator), the slice-007 membership policy + repo, and the slice-005 `TaskAccessGuards`. **No new error code** (R8).
- Frontend (NEW this slice): **none** — reuses TanStack Query v5 (the optimistic `setTaskAssignees` recipe on the task caches + the `['tasks','assigned']` key), the slice-007 roster (`MembersResponse`) for member names, the shared `Dialog` (the assignee picker's FR-101 focus contract), the slice-005 `DailyView`-style grouped list for "Assigned to me", and the `useGlobalShortcuts` chord layer (a `G A` nav chord).

**Storage**: PostgreSQL 17. **ONE migration this slice** (`AddTaskAssignees`): a new `task_assignees (task_id, user_id)` join table, composite PK, both FKs `ON DELETE CASCADE` (R1). Additive — the `tasks` table is unchanged. **FR-051 is LIVE** (R9).

**Testing**: xUnit (`Task.SetAssignees` set semantics + delta + idempotent no-op + `TaskAssigned` raised); Testcontainers integration (`SetTaskAssignees` allow + the SC-016 deny matrix: viewer-403 / non-member-404 / personal-404 / non-member-assignee-422 / stale-409; the `TaskAssigned` event via `host.TrackActivity()…Sent.MessagesOf<TaskAssigned>()`; `GetAssignedToMe` allow + deny; the slice-007 cleanup handlers' assignment-cleared assertion); Vitest (the optimistic `setTaskAssignees` surface + the picker + the assigned-view assembly); Playwright (US-13 AS-01..AS-04 + the SC-008 a11y audit).

**Target Platform**: Linux containers (Docker Compose) on Hetzner VPS; host Caddy. No infra change.

**Project Type**: Web application (monorepo: Next.js frontend + ASP.NET Core API).

**Performance Goals**: the assignee change paints optimistically <16 ms (SC-003); server single-entity writes p95 <200 ms (SC-012); the "Assigned to me" read joins `task_assignees` ∩ the caller's shared memberships over the per-user authz-scoped working set.

**Constraints**: assignment is **shared-only** (FR-069) and **editor/owner-only** (FR-067/FR-070); the assignee **must be a current member** (FR-069/B3, deny-tested); assignee is **provenance only** (FR-066) — membership dispatches access and leave/remove/unshare clears assignments (R5); the `TaskAssigned` event is **idempotent** and carries `actorUserId` for slice-017 self-suppression (R3); no new error code (R8); exactly one migration, FR-051 LIVE (R9).

**Scale/Scope**: ~10 users; per-user accessible working set up to 10,000 tasks. Adds one relation, one command, one query, one domain event, the `TaskResponse.assignees` delta, the assignment-cleanup hook in three slice-007 handlers, and the picker + assigned-view web surface.

## Constitution Check

*GATE: Re-evaluated against v4.0.0 for slice 008. PASS. This slice exercises the **shared-project membership + role** authorization branch (Principle IX) for a new write (assignment) + a new read ("Assigned to me"), ships **one migration with FR-051 LIVE** (Principle VII), and raises a domain event for slice 017 (Principle V). No open questions — the B3 assignee-membership rule is resolved in the spec and realized here.*

| # | Principle | Status | How Addressed |
|---|---|---|---|
| I | Keyboard-First | PASS | The assignee picker is keyboard-operable (focus contract, FR-031 suppression); "Assigned to me" opens via a `G A` nav chord on the existing `useGlobalShortcuts` layer. |
| II | Accessibility | PASS | The picker + confirmation dialogs follow the FR-101 dialog focus contract (initial focus, trap, Esc, return focus); assignee chips show member **names** (never avatar-color alone, FR-044 ≥4.5:1); no hover-only affordances (FR-046); `prefers-reduced-motion` honored (FR-047); single-key suppression in the picker's text filter (FR-031); a remote assignee change / toast routes through the polite ARIA live region without stealing focus (FR-101). SC-008 audits the picker + the assigned view. |
| III | Instant Response | PASS | `setTaskAssignees` paints optimistically <16 ms (SC-003) via the slice-005 `onMutate`/`onError`/`onSettled` recipe (snapshot → patch → rollback → settle-writeback); server writes p95 <200 ms (SC-012). |
| IV | Minimalist UI | PASS | The picker surfaces only the project's members; the assigned view is a focused grouped list. No assignment chrome on personal tasks (FR-069/AS-04). |
| V | Connected, Server-Authoritative | PASS | Assignees persist server-side (the system of record); the client holds no authoritative copy. The `TaskAssigned` event flows through the durable local queue (outbox-backed) for slice 017. |
| VI | Type Safety | PASS | `TaskResponse.assignees` + `SetTaskAssigneesRequest` + `AssignedResponse` are generated from the OpenAPI contract (`pnpm gen:api`, CI-diff-gated). Runtime validation both tiers (Zod assignee-set + FluentValidation). No new `errorCode` → `ERROR_UX` stays exhaustive unchanged (R8). |
| VII | Data Integrity | PASS (**FR-051 LIVE**) | **One migration** (`AddTaskAssignees`, R9). **FR-051 is LIVE** — the CI `backup.sh → ef database update → restore-test.sh` gate covers it (already wired; this slice confirms, not re-wires). FR-049 recoverable errors (non-member assignee → field-level 422; rollback message on optimistic rejection); FR-050 structured logging (ids/codes only). The `user_id` FK `ON DELETE CASCADE` makes the account-deletion erasure cascade automatic (Principle XI). |
| VIII | Test-First | PASS | Red-Green across tiers. Every new/changed data handler ships an **allow** AND a **deny** test; the SC-016 deny matrix (viewer-403, non-member-404, personal-404, non-member-assignee-422) is explicit; the `TaskAssigned` event and the leave/remove/unshare assignment-clear are asserted. |
| IX | Authn/Authz | PASS | Deny-by-default (FR-068), **dispatched by the containing project's visibility** (FR-065) — assignment exists only on shared tasks, so it authorizes on current `ProjectMembership` + role (FR-067: editor/owner write; viewer denied 403). Assignee **must be a current member** (FR-069); assignee + `createdBy` are **provenance only** (FR-066) — leave/remove/unshare revokes ALL access and clears assignments (R5). "Assigned to me" (FR-071) is caller-scoped across current memberships. SC-013/SC-016 (allow+deny per handler). Sessions/admission are slice 001 — assumed in force. |
| X | Time & Timezone | PASS | No new date-relative computation; assignment carries no due-date logic. Any timestamp stays UTC / `Europe/Warsaw` (FR-092) — consistent with the system. |
| XI | Privacy | PASS | Assignee references are personal data; the `user_id` FK `ON DELETE CASCADE` clears them on account deletion (FR-085 erasure cascade); leave/remove/unshare clears them (R5) — no residual access via a stale assignment. `TaskResponse.assignees` carries ids only (names via the roster). |
| XII | Security | PASS | Assignee selection takes a typed `UserId` (no free-form HTML); member names rendered in chips are React-escaped (FR-099). The `TaskAssigned` payload carries only ids/deltas + `actorUserId`, no secrets, never logged with sensitive context (FR-100). Slice-001 CSP/headers reused; no new secrets. |

## Project Structure

### Documentation (this feature)

```text
specs/008-task-assignment/
├── plan.md              # This file
├── research.md          # Phase 0: R1–R11
├── data-model.md        # Phase 1: the task_assignees relation + SetTaskAssignees + TaskAssigned + the assigned read + the migration (LIVE)
├── quickstart.md        # Phase 1: validation guide (assign/change/remove, assigned-to-me, deny matrix, cleanup, migration/FR-051)
├── contracts/
│   └── openapi.yaml     # Phase 1: setTaskAssignees + getAssignedToMe + TaskResponse.assignees delta
└── tasks.md             # Phase 2 (/speckit-tasks — not yet created)
```

### Source Code (repository root)

**(NEW)** net-new; **(MODIFY)** surgical change. **ONE DB migration this slice** (`AddTaskAssignees`).

```text
apps/
├── api/
│   ├── src/
│   │   ├── TaskFlow.Domain/TaskManagement/
│   │   │   ├── Task.cs                          # (MODIFY) add the Assignees collection + SetAssignees(desired, actor, utcNow)
│   │   │   │                                    #   — whole-set replace, delta, raise TaskAssigned on a real change, Touch.
│   │   │   │                                    #   ALSO: MoveToProject clears the assignee set on a real project change (R5).
│   │   │   └── TaskAssigned.cs                  # (NEW) domain event { taskId, projectId, added[], removed[], actorUserId }
│   │   ├── TaskFlow.Application/TaskManagement/
│   │   │   ├── SetTaskAssignees.cs              # (NEW) request DTO + command + validator + handler (shared-only + member-validity dispatch)
│   │   │   ├── SetTaskAssigneesRequest.cs       # (NEW) { assigneeIds: uuid[], version }
│   │   │   ├── TaskResponse.cs                  # (MODIFY) add required `assignees: Guid[]` + the From(...) projection
│   │   │   ├── AssignedResponse.cs              # (NEW) { groups: [ { projectId, tasks: [TaskResponse] } ] }
│   │   │   ├── ITaskRepository.cs               # (MODIFY) + ClearAssigneesForProjectAsync / ClearAssigneesForUserInProjectAsync / the assigned-to-me list
│   │   │   ├── TaskAssignedHandler.cs           # (NEW) no-op handler (routes the publish; slice 017 replaces) — mirrors MembershipEventHandlers
│   │   │   ├── UnshareProject.cs / RemoveMember.cs / LeaveProject.cs  # (MODIFY) + assignment-clear step (R5)
│   │   │   ├── DeleteProject.cs                 # (MODIFY) move_to_inbox branch also ClearAssigneesForProjectAsync (bulk move bypasses the aggregate, R5)
│   │   │   ├── MoveTaskToProject.cs / EditTask.cs  # (MODIFY) Task.MoveToProject clears assignees on a real project change (R5)
│   │   │   ├── IProjectRepository.cs            # (MODIFY) + ListOwnedSharedProjectIdsAsync(owner) — the owned-shared union for GetAssignedToMe (R6)
│   │   │   └── Queries/GetAssignedToMe.cs       # (NEW) caller-scoped read = (memberships ∪ owned-shared) ∩ assignee (R6)
│   │   ├── TaskFlow.Infrastructure/Persistence/
│   │   │   ├── Configurations/TaskConfiguration.cs   # (MODIFY) map the Assignees collection → task_assignees
│   │   │   ├── TaskRepository.cs                # (MODIFY) clear-assignees + assigned-to-me methods; Include(Assignees) on EVERY task read path
│   │   │   ├── ProjectRepository.cs             # (MODIFY) implement ListOwnedSharedProjectIdsAsync (owned-shared union, R6)
│   │   │   └── Migrations/…_AddTaskAssignees.cs # (NEW) the ONE migration (FR-051 LIVE)
│   │   └── TaskFlow.Api/
│   │       ├── Endpoints/TaskEndpoints.cs       # (MODIFY) + PATCH /api/tasks/{id}/assignees + GET /api/tasks/assigned
│   │       ├── OpenApi/TaskFlowDocumentTransformer.cs  # (MODIFY) stamp setTaskAssignees/getAssignedToMe ops + responses (no ErrorCodes edit)
│   │       └── Program.cs                       # (MODIFY) PublishMessage<TaskAssigned>().ToLocalQueue + AuthorizationMiddleware exclusion
│   └── tests/
│       ├── TaskFlow.UnitTests/Domain/TaskManagement/TaskTests.cs        # (MODIFY) SetAssignees set/delta/idempotent/event
│       └── TaskFlow.IntegrationTests/TaskManagement/
│           ├── SetTaskAssigneesTests.cs         # (NEW) allow + SC-016 deny matrix + TaskAssigned event assertion
│           ├── GetAssignedToMeTests.cs          # (NEW) allow + deny (non-member/non-assignee/membership-loss)
│           └── AssignmentCleanupTests.cs        # (NEW) unshare/remove/leave clear assignments
└── web/
    ├── src/
    │   ├── lib/validation/task.ts               # (MODIFY) the assignee-set Zod schema
    │   ├── lib/api/generated/schema.d.ts        # (REGEN) pnpm gen:api — the 2 ops + TaskResponse.assignees
    │   ├── hooks/useTaskMutations.ts            # (MODIFY) the optimistic setTaskAssignees recipe
    │   ├── hooks/useAssignedTasks.ts            # (NEW) query hook (['tasks','assigned'])
    │   ├── components/tasks/AssigneePicker.tsx  # (NEW) the keyboard picker (Dialog, roster checkboxes)
    │   ├── components/tasks/AssignedView.tsx    # (NEW) the "Assigned to me" grouped view
    │   ├── components/tasks/TaskRow.tsx         # (MODIFY) assignee chips (names from roster) + the picker affordance on shared tasks
    │   ├── hooks/useGlobalShortcuts.ts          # (MODIFY) the G A nav chord
    │   └── app/(app)/assigned/page.tsx          # (NEW) the "Assigned to me" route
    └── tests/
        ├── unit/{task-validation,task-view-mutations,assigned}.test.ts  # (MODIFY/NEW) assignee-set + optimistic + assembly
        └── e2e/task-assignment.spec.ts          # (NEW) US-13 AS-01..AS-04 + a11y audit
```

**Structure Decision**: reuses the slice-005/007 vertical-slice conventions. The backend adds one relation (+ its migration), one command, one query, one event (+ no-op handler), the `TaskResponse` delta, and the cleanup hook into three slice-007 handlers. The web adds the picker + the assigned view + a query hook + the optimistic recipe, reusing the slice-007 roster and the shared `Dialog`. The slice-007 membership policy and the slice-005 `TaskAccessGuards` are reused unchanged.

## Key Design Decisions

Summaries of `research.md` (R1–R11):
- **`task_assignees` join table + one migration; FR-051 LIVE** (R1/R9) — the first persisted relation on the Task aggregate; `ON DELETE CASCADE` on both FKs makes account-deletion erasure automatic.
- **Whole-set `SetTaskAssignees`** (R2) — one optimistic round-trip; the handler computes the delta; a no-op set bumps nothing and emits nothing (idempotency).
- **`TaskAssigned` event** (R3) — delta + `actorUserId`, idempotent, durable-queue routed with a no-op handler; slice 017 consumes it and self-suppresses via `actorUserId`.
- **Authorization** (R4) — dispatch-by-visibility (shared-only, editor/owner), assignee-must-be-a-current-member (422 deny test), assignee provenance-only.
- **Cleanup on leave/remove/unshare** (R5) — clear `task_assignees` in the slice-007 handlers' transaction; account deletion via the FK cascade.
- **"Assigned to me"** (R6) — caller ∩ current-membership ∩ assignee; membership gates.
- **No new error code** (R8).

## Complexity Tracking

| Item | Status this slice | Resolution & where it lands |
|------|-------------------|------------------------------|
| **FR-051 backup-before-migration** (Constitution VII MUST) | **LIVE** — this slice ships one EF migration (`AddTaskAssignees`, the join table). | No re-wiring: the CI deploy job's `scripts/backup.sh → dotnet ef database update → scripts/restore-test.sh` gate (live since slice 001/004/007) covers it. Verify **exactly one** new file under `Persistence/Migrations/` in the diff and that the backup/restore-test job runs. |
| **Cross-slice touch: modifying the slice-007 membership-removal handlers** (UnshareProject/RemoveMember/LeaveProject) | **Required** by FR-066/spec scenario 7 — assignment must not outlive membership. | Additive assignment-clear step inside the existing handlers' transaction (R5); the slice-007 membership allow/deny is inherited, the new assertion is "assignment cleared." A non-author authorization reviewer audits the change (Principle IX). |
| **`TaskAssigned` consumed only in slice 017** | This slice raises the event with a **no-op handler** (routes the publish; an unrouted publish is silently dropped). | Slice 017 replaces/augments the handler to deliver notifications + self-suppress via `actorUserId`. The event shape (delta + actor, idempotent) is frozen here. |
| **Self-suppression of the actor's own assignment notification** | The event carries `actorUserId`; **suppression is the consumer's (slice 017) job** — no observable effect this slice. | Documented in R3 + the event payload; this slice has no notification surface to suppress. |
| **Owner self-assign visibility in "Assigned to me"** (review finding) | The owner has **no `ProjectMembership` row**, so a membership-only scope would hide an owner's self-assigned tasks. | `GetAssignedToMe` scopes by `ListProjectIdsForUserAsync(caller)` **∪ owned-shared project ids** (R6); a `ProjectRepository` owned-shared-ids seam supplies the union. Tested by an owner-self-assign-sees-it case. |
| **Stale assignees on task project-move** (review finding) | Moving a shared task to the Inbox/another project (via `EditTask` — any editor, no ownership check — or `MoveTaskToProject`) would otherwise leave assignee rows on a now-personal task (violates FR-069). | `Task.MoveToProject` **clears the assignee set on a real project change** (R5/§7) — one place covering both aggregate-routed move paths; structural (no event); tested on both. |
| **Bulk move bypasses the aggregate** (re-review finding) | `DeleteProject` + `move_to_inbox` uses `MoveProjectTasksToInboxAsync` (bulk `ExecuteUpdate`), so `Task.MoveToProject`'s clear never fires → strands assignees on now-Inbox tasks. | `DeleteProject`'s `move_to_inbox` branch ALSO calls `ClearAssigneesForProjectAsync(projectId)` (R5/§7), in the same transaction; the `cascade` disposition needs none (FK cascade at reap). Tested. |
| **`assignees` eager-load on all read paths** (review finding) | `TaskResponse.assignees` is required, so every task read must `Include` the collection or it silently emits `[]`. | `Include(t => t.Assignees)` added to every task-loading repo method (Inbox/project/single/Today/Upcoming/assigned); tested by a normal-view-carries-assignees assertion. |

# Quickstart & Validation Guide: Task Assignment (slice 008)

Validates the **US-13 Task Assignment** journey (AS-01..AS-04) end-to-end against the real stack: assign/change/remove assignees on a **shared-project** task, the **"Assigned to me"** view, and **no assignment control on personal tasks**. References `spec.md`, `data-model.md`, `research.md`, and `contracts/openapi.yaml`.

> This is a run/validation guide — not implementation. Bodies, validators, and full suites belong in `tasks.md`.

## Prerequisites

- The slice-002..007 stack boots locally (see the local-run & E2E runbook): PostgreSQL + the C# API on `:4311` (for `gen:api`), the Next.js web app, the fake-IdP/BFF.
- **ONE migration this slice** (`AddTaskAssignees`, research R9): `task_assignees (task_id, user_id)` join table. **FR-051 is LIVE** — confirm the CI deploy job's `scripts/backup.sh → dotnet ef database update → scripts/restore-test.sh` gate covers it, and that **exactly one** new file appears under `TaskFlow.Infrastructure/Persistence/Migrations/` in this slice's diff.
- Typed client regenerated: `cd apps/web && pnpm gen:api` → `schema.d.ts` gains `setTaskAssignees`/`getAssignedToMe` + `TaskResponse.assignees`; `pnpm typecheck` green.
- Reference identity: an editor/owner `U` of a **shared** project `P` with members `{O, U, V(viewer)}` and a stranger `X` (non-member).

## Setup

```bash
# Backend (from repo root)
dotnet build
dotnet test                       # xUnit (Task.SetAssignees) + Testcontainers integration
                                  #   (SetTaskAssignees allow + SC-016 deny matrix + TaskAssigned event;
                                  #    GetAssignedToMe allow/deny; leave/remove/unshare cleanup)
# Web (from apps/web)
pnpm install
pnpm gen:api                      # API must be on :4311
pnpm test                         # Vitest (optimistic setTaskAssignees + picker + assigned assembly)
pnpm e2e                          # Playwright — AS-01..AS-04 + a11y audit
```

## Validation scenarios

### A. Assign / change / remove (US-13 — AS-01/AS-02)

| # | Action | Expected |
|---|---|---|
| AS-01 | As editor/owner, open a shared-project task's assignee picker, select members, confirm | The selected members appear as assignees; a `TaskAssigned` event is raised carrying the **added** delta + `actorUserId` (consumed by slice 017; no notification delivered here) |
| AS-02 | Change the selection (add some, remove some), confirm | The assignee set updates to the new full set; `TaskAssigned` carries the precise added/removed delta; re-confirming the SAME set is an idempotent no-op (no event, no version bump) |
| self-assign | Assign yourself (you are a member) | Allowed; you appear in the set; the event carries you in `added` with `actorUserId = you` (slice 017 suppresses the self-notification) |

### B. "Assigned to me" (US-13 — AS-03)

| # | Action | Expected |
|---|---|---|
| AS-03 | Open "Assigned to me" (`G A`) | Lists tasks across **shared** projects where you are a **current member (or owner) AND an assignee**, grouped by project; done/cancelled excluded |
| owner-self-assign | As the project **owner**, self-assign a task, open "Assigned to me" | The task **appears** — the read unions the owner's owned-shared projects with membership-row projects (the owner has no membership row, R6) |
| membership-loss | Leave/lose membership of `P`, reopen | Those tasks disappear — membership gates access (the assignee row is provenance only); the assignment rows are also cleared (R5) |
| move-clears | Assign members, then move the task to the Inbox / another project (`M` or the editor) | The assignees are **cleared** (assignment is project-scoped, FR-069); a personal/Inbox task never carries assignees |
| normal-view | View an assigned shared task in Today / the project list (not the assigned view) | The assignee chips are present — `assignees` is eager-loaded on **every** task read path (R7) |

### C. Personal tasks (US-13 — AS-04)

| # | Action | Expected |
|---|---|---|
| AS-04 | View a personal (not shared) task | **No assignment control** is offered; `PATCH /api/tasks/{id}/assignees` on a personal task → **404** (the surface does not exist) |

## Server-validation & authorization table (the SC-016 deny matrix)

| Input | Expected |
|---|---|
| Editor/owner assigns current members of the shared project | **200** — assignees set; `TaskAssigned` raised (allow) |
| A **viewer** member attempts to assign | **403 forbidden** (insufficient role, FR-067) |
| A **non-member** attempts to assign | **404 not_found** (existence not disclosed) |
| Assignment on a **personal/Inbox** task | **404 not_found** (no assignment surface, FR-069) |
| An `assigneeId` who is **NOT a current member** of the project | **422 validation_failed** (field error on `assigneeIds`, FR-049 recoverable; no assignee added) |
| Duplicate / malformed `assigneeIds` | **422 validation_failed** |
| Stale `version` | **409 version_conflict** |
| `GET /api/tasks/assigned` as a non-member / non-assignee | The task is **absent** (caller-scoped ∩ current membership ∩ assignee) |
| Any request without a valid session | **401 unauthenticated** |

## Cross-cutting checks

- **A11y (Principle II / SC-008)**: the assignee picker follows the dialog focus contract (initial focus, trap, Esc, return focus, FR-101); assignee chips show member **names** (output-encoded, never avatar-color alone, FR-044); no hover-only affordances (FR-046); single-key suppression in the picker filter (FR-031); `prefers-reduced-motion` honored (FR-047). The picker + the assigned view pass the WCAG 2.1 AA audit (SC-008).
- **Instant response (SC-003)**: `setTaskAssignees` paints optimistically <16 ms (snapshot → patch → rollback → settle-writeback); the server reconciles/rolls back, the rollback announced via the polite live region.
- **Authorization (Constitution IX / SC-013/SC-016)**: every new/changed handler ships an allow + a deny test; the deny matrix above is mechanically covered; the leave/remove/unshare handlers assert the assignment is cleared.
- **Privacy (Constitution XI)**: account deletion clears assignee rows via the `user_id` FK `ON DELETE CASCADE`; `TaskResponse.assignees` carries ids only.
- **Migration (Principle VII)**: confirm **exactly one** migration file (`AddTaskAssignees`) in the diff and that **FR-051's backup/restore-test gate** runs (LIVE this slice).
- **Event (Principle V)**: `TaskAssigned` is raised on a real delta (asserted via `host.TrackActivity()…Sent.MessagesOf<TaskAssigned>()`), idempotent, carries the delta + `actorUserId`; a no-op set raises none.

## Definition of done

- Every scenario passes (manual + automated).
- `SetTaskAssignees` ships allow + the full SC-016 deny matrix; `GetAssignedToMe` ships allow + deny; the three slice-007 cleanup handlers assert assignment-cleared.
- `pnpm gen:api` clean; `pnpm typecheck`/`lint` green; `dotnet test` green; **exactly one** migration (`AddTaskAssignees`) + **FR-051 LIVE** confirmed; `ERROR_UX` unchanged (no new errorCode).
- Assign/change/remove + "Assigned to me" + the personal-task no-control path all operate mouse-free (SC-001-style); the picker + assigned view pass the WCAG 2.1 AA audit (SC-008).

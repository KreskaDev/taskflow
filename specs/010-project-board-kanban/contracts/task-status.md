# Contract: PATCH /api/tasks/{id}/status (widened — slice 010)

Existing route, widened value set; command renamed `SetTaskDone` → `SetTaskStatus`
(operationId `setTaskDone` → `setTaskStatus`). Wire DTO name unchanged
(`SetTaskStatusRequest`). See `research.md` D1–D3.

## Request

```
PATCH /api/tasks/{id}/status
Authorization: Bearer <carrier>          (BFF-minted HS256; sub = TaskFlow user id)
Content-Type: application/json

{ "status": "backlog" | "todo" | "in_progress" | "done" | "cancelled",
  "version": <int — last-seen task version> }
```

## Semantics

- Desired-state write (not a toggle). Same-status request: 200, no-op, version unchanged.
- `status = done` stamps `completedAt`; leaving `done` clears it (invariant: set iff done).
- Board column moves map client-side (FR-025): a drop on / menu-move to a column sends that
  column's status. `cancelled` is never offered by the Board UI (no column, EC-11); it is an
  accepted API value (FR-003 storability; enables EC-11 E2E seeding via the real API).

## Responses

| Code | When | ProblemDetails `errorCode` |
|---|---|---|
| 200 | applied (or idempotent same-status) — returns `TaskResponse` | — |
| 401 | no/invalid carrier | `unauthenticated` |
| 403 | shared project, caller is **viewer** | `forbidden` |
| 404 | task absent/soft-deleted; personal task of another user; shared project without current membership (existence undisclosed) | `not_found` |
| 409 | `version` stale | `version_conflict` |
| 422 | status outside the five values; negative version | `validation_failed` |

## Authorization (FR-065..FR-068 — enforced in handler)

`TaskAccessGuards.LoadWritableTaskAsync(id, EffectiveRole.Editor, …)`:
unprojected task → ownership (`createdBy`), foreign → 404; projected task → readable project
(404-first), then role ≥ editor (viewer → 403). Deny-by-default; no change to the guard.

## Tests (contract-driven)

- Integration `SetTaskStatusBoardTests` (lands in `tasks-core` shard by name — research D12):
  - allow: editor moves shared task `todo → in_progress` (200; status + version persisted;
    `completedAt` untouched/null),
  - allow: `done → in_progress` clears `completedAt`,
  - allow: `in_progress → done` stamps `completedAt`,
  - allow: same-status idempotent no-op,
  - allow: owner sets `cancelled` (200) — basis for EC-11 seeding,
  - deny: viewer → 403 `forbidden`, DB unchanged,
  - deny: non-member → 404 `not_found`,
  - 409 on stale version; 422 on `"doing"`.
- Existing `SetTaskDoneTests` / `SetTaskDoneSharedAuthzTests` stay green unchanged (done/backlog
  arms behave identically).

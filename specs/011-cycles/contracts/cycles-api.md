# Contract: Cycle lifecycle & metrics API (slice 011)

All routes sit behind the deny-by-default authenticated gate (Constitution IX). Cycle
lifecycle/read operations are **team-wide**: any authenticated, admitted user may invoke them
(no ownership, no membership/role surface on Cycle — spec IX). OCC via `version` everywhere;
errors are problem+json with `errorCode` (mapped into the web `ERROR_UX` union).

## `PUT /api/cycles/{id}` — create (idempotent)

Body: `{ name: string (1..200 trimmed), startDate: ISO, endDate: ISO }`
- 200 `CycleResponse` (status `planned`, version 0). Re-PUT of the same id: idempotent success.
- 422 `validation_failed`: empty/oversized name, missing dates, `startDate >= endDate`.
- Overlap with other cycles is NOT validated (Clarifications).

## `PATCH /api/cycles/{id}` — edit

Body: `{ name, startDate, endDate, version }` — allowed in every status; `start < end`
re-validated; 409 `version_conflict` on stale version; 404 unknown id.

## `PATCH /api/cycles/{id}/activate`

Body: `{ version }`.
- Legal only from `planned` → else 422 `cycle_not_planned`.
- Single-active guard: another active cycle → 409 `cycle_active_conflict` (handler check + the
  `ix_cycles_single_active` partial unique index as the race-proof backstop, D3).

## `PATCH /api/cycles/{id}/close`

Body: `{ rollover?: "next" | "backlog" | "keep", overrides?: [{ taskId, choice }], version }`
- Legal only from `active` → else 422 `cycle_not_active`.
- Applies the rollover matrix of `data-model.md` to every incomplete task in ONE transaction
  with the status flip. `rollover` defaults to `keep`; with zero incomplete tasks it is a
  pure close. (The UI ALWAYS sends an explicit choice when incomplete tasks exist — the
  default exists for the pure-close path, not as a silent decision.)
- `rollover: "next"` with no planned cycle → 422 `no_next_cycle` (US-05.AS-06 prompt).
- `overrides` entries referencing tasks the caller cannot see → 404 posture for that call
  (whole command rejected; nothing partial).
- Response: `CycleResponse` (closed) + `{ rolledToNext, rolledToBacklog, kept }` counts for the
  confirmation/announcement copy.

## `DELETE /api/cycles/{id}?version=`

- `active` → 422 `cycle_active_delete_forbidden` (EC-04/FR-019; message: close first).
- `planned`/`closed` with ≥1 assigned task → 422 `cycle_not_empty` (FR-020).
- Otherwise hard delete; FK `tasks.cycle_id → cycles(id) ON DELETE RESTRICT` is the backstop.

## `GET /api/cycles`

- Returns ALL cycles ordered by `(startDate, createdAt, id)` (D5), each with computed
  team-wide metrics: `{ total, done, breakdown{backlog,todo,in_progress,done,cancelled} }`
  over non-deleted tasks (D6). No pagination (ASM-10 scale).

## `GET /api/cycles/{id}/tasks`

- 404 unknown cycle. Rows: the cycle's non-deleted tasks FILTERED to caller visibility
  (own personal tasks + current-membership project tasks — FR-065 dispatch, D10), ordered by
  `position`. Serves the Cycle view list and the close review (client filters incomplete).
- EC-12: tasks of an ARCHIVED project remain in these rows (visible in the Cycle view even
  though hidden from project-based views).

## `PATCH /api/users/me/preferences`

Body: `{ cycleDefaultDurationDays: int 1..90 }` → 200 widened profile; 422 out of range.
`GET /api/users/me` response gains `cycleDefaultDurationDays` (default 14).

## OpenAPI

Document-transformer entries (operationIds): `createCycle`, `editCycle`, `activateCycle`,
`closeCycle`, `deleteCycle`, `listCycles`, `getCycleTasks`, `setUserPreferences` (+
`setTaskCycle`, see task-cycle.md). New error codes registered with Polish `ERROR_UX` copy:
`no_next_cycle`, `cycle_not_planned`, `cycle_active_conflict`, `cycle_not_active`,
`cycle_not_empty`, `cycle_active_delete_forbidden`. `openapi-sync` guards the checked-in doc;
`pnpm gen:api` regenerates the typed client.

## Integration-test matrix (allow + deny; namespace `TaskFlow.IntegrationTests.Cycles` — D15)

- create/edit validation (name, dates, start<end; overlap ALLOWED), idempotent re-PUT
- activate: happy path; second-active 409 (incl. a direct-DB race proof of the partial index);
  from closed/active → 422
- close: each rollover mode incl. overrides + invisible-task default application; counts in
  response; `no_next_cycle`; from planned/closed → 422; transactionality (failure rolls all back)
- delete: active forbidden; non-empty forbidden; empty planned/closed OK
- list/metrics: team-wide counts include OTHER users' tasks; ordering incl. tiebreaker
- cycle tasks: visibility filtering (personal-of-other excluded, shared-member included,
  former member excluded — FR-066), archived-project tasks included (EC-12)
- deny-by-default: unauthenticated 401 on every route
- preferences: roundtrip + range validation

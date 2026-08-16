# Contract: Task cycle assignment (slice 011)

## `PATCH /api/tasks/{id}/cycle` — `SetTaskCycle`

Body: `{ cycleId: uuid | null, version }` (`null` = back to the cycle backlog, FR-016).

- **Authorization = the TASK's visibility** (FR-065 dispatch, spec IX): personal/unprojected
  task → owner only (404 posture for others); shared-project task → current membership with
  editor/owner role (viewer 403, non-member 404) — exactly the `SetPriority`/`SetTaskStatus`
  guard (`LoadWritableTaskAsync(Editor)` pattern). The CYCLE needs no additional authorization
  (team-wide).
- `cycleId` must reference an existing cycle → else 422 `validation_failed`. ANY status is
  assignable — active, planned, and **closed** (Clarifications; assigning into a closed cycle
  is allowed and its metrics reflect current assignments).
- Effect: `task.CycleId := cycleId`, `task.CarriedOver := false` (always cleared on manual
  reassignment — the flag is exclusively rollover-written, D7), version bump (OCC 409 on
  stale), `updated_at` touched.
- Response: full `TaskResponse` (now carrying `cycleId` + `carriedOver`).

## Response widening

`TaskResponse` AND the flattened Today/Upcoming/Assigned row shapes gain
`cycleId: uuid | null` + `carriedOver: boolean` (the slice-006 flattening gap must not recur —
every surface that renders the „⋯" menu needs the current assignment for the picker's
checkmark).

## Client mutation

`useTaskMutations.setTaskCycle(taskId, cycleId)` on the established optimistic factory
(snapshot/rollback/settle, 409 reapply-once), invalidating the active view list key +
`["cycles"]` + `["cycle-tasks", *]` on settle. Optimistic paint: the picker closes and the
row's state updates within one frame (SC-003 posture); failure rolls back with the FR-049
toast + LiveRegion announcement (FR-101) and structured `logError` (FR-050).

## Integration-test matrix (in `TaskFlow.IntegrationTests.Cycles` — D15)

- owner assigns personal task → 200; other user → 404 (personal isolation)
- shared-project task: editor 200, viewer 403, non-member 404, former member 404 (FR-066)
- assign to planned/active/closed cycle → all 200; unknown cycle → 422
- null clears assignment; carried_over cleared on every write (seed a carried task first)
- OCC: stale version 409
- unauthenticated 401

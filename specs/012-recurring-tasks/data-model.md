# Data Model — Slice 012 (Recurring Tasks)

Phase-1 design. Decisions referenced as D1–D17 (research.md).

## ENT-05 — Recurrence Rule (owned here; a Task attribute, not a table)

Stored in the slice-002 reserved column `tasks.recurrence_rule jsonb NULL` (D1) through a typed
Domain value object `RecurrenceRule` + EF value converter (System.Text.Json, camelCase).

```text
RecurrenceRule (closed union — `type` discriminates)
├── daily          {}                                  → next = due + 1 day
├── every_n_days   { interval: 2..365 }                → next = due + interval days
├── weekdays       { weekdays: [1..7]+ }  ISO Mon=1…Sun=7, unique, ascending, non-empty
│                                                      → next = first listed weekday STRICTLY after due's Warsaw date
└── monthly        { dayOfMonth: 1..31 }  the ANCHOR    → next = next month, day = min(dayOfMonth, daysInMonth)
```

Invariants (Domain, D1/D9):
- Deserialization rejects unknown `type`/out-of-range members (no silent coercion).
- `RecurrenceRule != null ⇒ DueDate != null` — checked by `SetRecurrence`, `EditTask`,
  `Reschedule`; violation → `RecurrenceRequiresDueDateException` → `422 recurrence_requires_due_date`.
- `NextDueAfter(dueUtc, hasTime)` is pure (no clock): date-only → Warsaw-midnight instant of
  the new date; timed → same Warsaw wall-clock on the new date (`InZoneLeniently`, DST by
  NodaTime). Never skips occurrences (Clarifications Q2).

## Touched: Task (ENT-01, owned by slice 002)

```text
tasks
├── recurrence_rule   jsonb NULL           (exists) now typed RecurrenceRule? — D1
├── spawned_from_id   uuid NULL            NEW — FK → tasks(id) ON DELETE SET NULL (raw Guid?, D2)
└── (unchanged)       status, completed_at, due_date, due_has_time, cycle_id, carried_over, …

Indexes (NEW):
- ux_tasks_spawned_from_live: UNIQUE (spawned_from_id) WHERE deleted_at IS NULL   ← ≤1 LIVE successor per predecessor (FR-009)
```

Domain additions on `Task`:
- `RecurrenceRule? RecurrenceRule` (typed), `Guid? SpawnedFromId`.
- `SetRecurrence(RecurrenceRule? rule, DateTime utcNow)` — guard D9, `Touch()`.
- `EditTask(..., RecurrenceRule? rule, ...)` — the whole-object replace now carries the rule (D11); guard D9.
- `Reschedule(null, null, …)` — guard D9 (clearing the due of a recurring task is refused).
- `static Task SpawnSuccessor(Task predecessor, TaskId id, string position, DateTime utcNow)` —
  the FR-008 carry-forward (D5): copies title/description/priority/project/rule/dueHasTime,
  sets `DueDate = rule.NextDueAfter(...)`, `Status = Backlog`, `CompletedAt = null`,
  `CycleId = null`, `CarriedOver = false`, `SpawnedFromId = predecessor.Id`; assignees are
  applied afterwards by the Application helper (membership re-validation needs repositories).
- `DetachSuccessorLink(DateTime utcNow)` — `SpawnedFromId := null` (D6, done-successor case).

## Lifecycle of an instance pair (A = completed instance, B = successor)

```text
                 SetTaskStatus(A → done)
                 ┌─ today(Warsaw) ≥ dueDay(A) ──► spawn B NOW (same tx)              (FR-008, AS-02/03/05)
A: recurring ────┤
   with due      └─ today < dueDay(A) ──────────► schedule SpawnRecurrenceSuccessor   (FR-009, AS-04/EC-05)
                                                  at start-of-dueDay(Warsaw)
                                                      │ fires:
                                                      ├─ A still done @ same completedAt, no live B → spawn B
                                                      └─ else (reopened / cancelled / rule removed / B exists) → no-op

SetTaskStatus(A: done → anything else)  = "undo"   (D6)
   ├─ live B with status ≠ done → soft-delete B (+ReapDeletedTask 30s)
   └─ live B with status = done → B.spawned_from_id := NULL (B stays)
   → A's slot is free; re-completing A spawns B′

SetTaskStatus(→ cancelled) from non-done: nothing spawns (FR-010, AS-06)
Rule removed (editor): recurrence_rule := NULL; existing B untouched
```

- Completion is detected on an ACTUAL transition only (same-status request = no-op, no spawn).
- Chain continuation: B carries the rule + anchor, so completing B repeats the diagram.
- A successor is always created in the predecessor's project, even if that project is archived
  (no special case; the row is reachable through the project's visibility like any task).

## Successor field matrix (FR-008 / D5)

| field | B |
|---|---|
| id | server uuid v7 |
| createdBy | A.createdBy |
| title / description / priority / projectId | copied |
| dueDate | `rule.NextDueAfter(A.dueDate, A.dueHasTime)` |
| dueHasTime | copied |
| recurrenceRule | copied verbatim (anchor preserved) |
| status / completedAt | backlog / null |
| createdAt / updatedAt / version | now / now / 0 |
| position | adjacent after A (`PositionRank.BetweenAfter`) |
| cycleId / carriedOver | null / false |
| spawnedFromId | A.id |
| assignees | A's ∩ (current members ∪ owner) of A.projectId; none for personal tasks; no `TaskAssigned` event |
| labels (`task_labels`) | every owner's rows for A copied to B |
| comments | not copied |

## Scheduled message

`SpawnRecurrenceSuccessor(TaskId TaskId, DateTime CompletedAtInstant) : DomainEvent` (Domain
`Events/`), routed to the durable local queue `recurrence-spawn`, exempt from
`AuthorizationMiddleware` (no HttpContext — the reaper exemption pattern). Delivery:
`DeliveryOptions { ScheduledTime = <start of A's due day in Europe/Warsaw, as UTC> }`.

## API response shapes (D11/D12/D16)

- `TaskResponse` (+ flattened Today/Upcoming/Assigned rows): `+ recurrenceRule: RecurrenceRule | null`.
- `EditTaskRequest`: `+ recurrenceRule: RecurrenceRule | null` (required, nullable).
- `RecurrenceRule` schema: `{ type: "daily"|"every_n_days"|"weekdays"|"monthly", interval?, weekdays?, dayOfMonth? }`.
- New ProblemDetails `errorCode`: `recurrence_requires_due_date`.
- No new endpoints; no new operationIds.

## Client state

- No new query keys. `toggleDone`/`setTaskStatus`/`editTask` keep their optimistic factories;
  settle invalidates the active view keys (successor appears on refetch — D13).
- No localStorage additions. The editor/drawer hold the rule draft in component state only.

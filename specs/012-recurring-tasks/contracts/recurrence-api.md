# Contract: Recurrence API (slice 012)

No new routes. Three existing operations change behavior; one scheduled message is added.
All routes stay behind the deny-by-default authenticated gate (FR-068). Decisions: research.md.

## `RecurrenceRule` schema (D1)

```json
{ "type": "daily" }
{ "type": "every_n_days", "interval": 3 }          // 2..365
{ "type": "weekdays", "weekdays": [1, 3] }          // ISO 1=Mon … 7=Sun; non-empty, unique, ascending
{ "type": "monthly", "dayOfMonth": 31 }            // 1..31 — the anchor day
```

Validation (FluentValidation on the request, Domain on deserialization): any other `type`, a
member not belonging to the type, or an out-of-range value → `422 validation_failed`.

## `PATCH /api/tasks/{id}/edit` — `EditTask` (widened, D11)

Body: `{ title, description, priority, dueDate, dueHasTime, projectId, recurrenceRule, version }`
— `recurrenceRule` is REQUIRED (nullable). Semantics unchanged otherwise (whole-object
replace, `LoadWritableTaskAsync(Editor)` dispatch, OCC 409).

- `recurrenceRule != null && dueDate == null` → `422 recurrence_requires_due_date`
  (Clarifications Q4; Domain guard D9). The rule is NEVER dropped silently.
- `recurrenceRule` set/changed on a task with a due date → stored; response carries it.
- `recurrenceRule: null` removes the rule; existing successors are untouched (D8).
- Response: `TaskResponse` (+ `recurrenceRule`).

## `PATCH /api/tasks/{id}/due-date` — `RescheduleDueDate` (guarded, D9)

Clearing the due (`dueDate: null`) of a task with a rule → `422 recurrence_requires_due_date`.
Rescheduling to another date is allowed and simply moves the anchor (the next occurrence is
computed from the NEW due when the task is completed).

## `PATCH /api/tasks/{id}/status` — `SetTaskStatus` (spawn + undo, D4/D6)

Unchanged body `{ status, version }`; unchanged authorization. New side effects, all inside
the command's transaction:

| transition | task has rule + due | effect |
|---|---|---|
| → `done` (actual) | yes, `today(Warsaw) ≥ dueDay` | successor inserted now (field matrix: data-model.md) |
| → `done` (actual) | yes, `today < dueDay` | `SpawnRecurrenceSuccessor` scheduled for start-of-dueDay (Warsaw); nothing inserted |
| → `done` (actual) | no rule or no due | nothing |
| `done` → anything (actual) | has a LIVE successor | successor `status ≠ done` → soft-deleted (+`ReapDeletedTask` 30s); `status = done` → its `spawnedFromId` cleared |
| same status | — | idempotent no-op; never spawns, never deletes |

Response stays the predecessor's `TaskResponse`. The client learns about the successor via
invalidation/refetch (D13).

## Scheduled message — `SpawnRecurrenceSuccessor(TaskId, CompletedAtInstant)` (D7)

Durable local queue `recurrence-spawn`; handler exempt from `AuthorizationMiddleware` (no
HttpContext). Decision path, in order — every "no-op" is silent and idempotent:

1. task absent or soft-deleted → no-op
2. `Status != done` OR `CompletedAt` ≠ `CompletedAtInstant` (µs) → no-op (reopened / cancelled / re-completed later)
3. no rule or no due → no-op; due day now in the FUTURE (the done instance was rescheduled — the anchor moved) → re-publish the message for the new due day's start and stop (EC-05: never spawn early)
4. a live successor exists → no-op
5. build + insert (D5); unique-index violation (`ux_tasks_spawned_from_live`) → swallowed no-op

Authorization posture: the successor inherits the predecessor's owner/project (FR-065
visibility unchanged); carried assignees are re-validated against CURRENT membership (FR-066)
and dropped silently; no `TaskAssigned` notification.

## Fact table (shared by the Domain unit tests and `lib/recurrence.ts` unit tests — D3)

| rule | anchor due (Warsaw) | next |
|---|---|---|
| daily | 2026-06-10 | 2026-06-11 |
| every_n_days/3 | 2026-06-10 | 2026-06-13 (AS-05 — regardless of completion date) |
| weekdays [1,3] | Mon 2026-06-08 | Wed 2026-06-10 (AS-03) |
| weekdays [1,3] | Wed 2026-06-10 | Mon 2026-06-15 (wraps) |
| weekdays [5] | Fri 2026-06-12 | Fri 2026-06-19 (single day = weekly) |
| weekdays [1,3] | Tue 2026-06-09 (not a listed day) | Wed 2026-06-10 |
| monthly/31 | 2026-01-31 | 2026-02-28 (clamp) |
| monthly/31 | 2028-01-31 | 2028-02-29 (leap clamp) |
| monthly/31 | 2026-02-28 (clamped instance, anchor 31) | 2026-03-31 (anchor restored) |
| monthly/15 | 2026-12-15 | 2027-01-15 (year wrap) |
| daily, timed 09:00 | 2026-03-28 09:00 CET | 2026-03-29 09:00 CEST (DST: wall-clock kept) |
| every_n_days/7, timed 09:00 | 2026-10-24 09:00 CEST | 2026-10-31 09:00 CET |
| every_n_days/2, date-only | 2026-03-28 (midnight CET = 23:00Z 03-27) | 2026-03-30 midnight CEST (= 22:00Z 03-29) |

## Error codes (D16)

| code | HTTP | Polish copy (web `ERROR_UX`) |
|---|---|---|
| `recurrence_requires_due_date` | 422 | „Zadanie cykliczne musi mieć termin — wyłącz powtarzanie albo ustaw termin." |

## Integration-test matrix (namespace `TaskFlow.IntegrationTests.Recurrence` — D14)

EditTask / Reschedule:
- set each of the four rule types on a due-dated task → 200, echoed in the response; invalid
  shapes (unknown type, interval 1/366, empty/duplicate weekdays, dayOfMonth 0/32) → 422
- rule on a task without due → 422 `recurrence_requires_due_date`; clearing due via edit or
  reschedule while a rule exists → 422; `recurrenceRule: null` clears
- authz: personal foreign → 404; shared editor 200 / viewer 403 / non-member 404 (FR-066/067)
- OCC stale version → 409; unauthenticated → 401

SetTaskStatus spawn (frozen clock):
- due today, complete → successor exists with next due per the fact table, status backlog,
  copied title/description/priority/project/rule, `cycleId` null, `carriedOver` false,
  position adjacent after A; response is A
- due yesterday (late) → successor due = anchor + rule (may be in the past; AS-05)
- due tomorrow → NO successor; exactly ONE `SpawnRecurrenceSuccessor` envelope `Scheduled`
- same-status retry (`done` → `done`) → still exactly one successor / one envelope
- chain: complete B on its due day → C spawned (second successor generable)
- shared project: assignees carried; a former member is dropped; no `TaskAssigned` envelope
- labels: two users' label rows on A both appear on B
- cancelled from backlog → nothing spawned (FR-010)

Undo:
- reopen A (done → backlog) → B soft-deleted (deleted_at set, reaper scheduled); re-complete A
  → B′ spawned (partial unique index permits)
- B already done when A reopens → B stays, `spawned_from_id` null; re-complete A → B″ spawned
- A done → cancelled → same as reopen (leaves done)

Deferred job (direct `SendAsync`, tracked):
- fires on the due day: successor created once; second delivery → no-op (count stays 1)
- A reopened before firing → no-op; A re-completed (new `completedAt`) → stale message no-op,
  the new message spawns
- rule removed before firing → no-op
- A soft-deleted before firing → no-op

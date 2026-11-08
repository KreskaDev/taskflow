# Research — Slice 012 (Recurring Tasks)

Phase-0 decisions. Spec-level ambiguities were resolved in the Clarifications session
(spec.md, 2026-11-08) and are referenced, not re-decided. Code facts verified on branch
`012-recurring-tasks` (post-011 main).

## D1 — Rule storage: the reserved `tasks.recurrence_rule` jsonb column, typed model

**Decision**: persist ENT-05 in the column slice 002 reserved (`tasks.recurrence_rule jsonb
NULL`, already mapped as `Task.RecurrenceRule: string?`). Replace the raw string with a typed
`RecurrenceRule` value object (Domain) serialized through an EF value converter
(System.Text.Json, camelCase). Closed wire/storage shape:

```text
{ "type": "daily" }
{ "type": "every_n_days", "interval": 2..365 }
{ "type": "weekdays",     "weekdays": [1..7] }     # ISO weekday numbers, ≥1, unique, ascending
{ "type": "monthly",      "dayOfMonth": 1..31 }    # the ANCHOR day (survives clamping)
```

Deserialization is validated (unknown `type`, out-of-range values → the row is rejected as a
domain error, never silently coerced — Constitution VI). No new table.

**Rationale**: ENT-05 is a per-task attribute with ~4 variants; a separate table buys
nothing at ASM-10 scale and the column already exists (no FR-051-relevant destructive
migration for the rule itself). Keeping `dayOfMonth` IN the rule is what makes the
"rule + anchor" carry-forward (FR-008) correct: Jan 31 → Feb 28 → **Mar 31** needs the
original 31 remembered; the due date alone loses it after one clamp.

**Alternatives**: RFC 5545 RRULE strings — rejected (grammar far wider than FR-007, needs a
parser on both sides, un-validatable at the boundary). `daily` folded into
`every_n_days/1` — rejected for ENT-05 fidelity (four named frequency types), though the
computation treats it as interval 1.

## D2 — Predecessor link on the successor: `tasks.spawned_from_id` + partial unique index

**Decision**: ONE migration `AddRecurrenceLink` adds `tasks.spawned_from_id uuid NULL`
(FK → `tasks(id)` ON DELETE SET NULL) and the partial unique index
`ux_tasks_spawned_from_live ON tasks(spawned_from_id) WHERE deleted_at IS NULL`.
`Task.SpawnedFromId` stays a raw `Guid?` (the slice-005/011 value-converted-nullable-FK
trap — D2 of slice 011).

**Rationale**: FR-009 idempotency ("≤1 successor per completed instance") becomes a DB
invariant instead of a handler promise — the deferred job and an interleaved immediate spawn
cannot both insert. The link is also what "undo removes the successor" navigates (D6). The
partial predicate is essential: after an undo soft-deletes B, re-completing A must be able to
insert B′ (Clarifications Q1).

**Alternatives**: `successor_id` on the predecessor — rejected: writing A on B's insert
doubles the OCC surface and still needs a uniqueness story; an application-level check only —
rejected (races between the scheduled job and a re-completion).

## D3 — Occurrence computation lives in the Domain (NodaTime, Europe/Warsaw), mirrored by a pure web module

**Decision**: `RecurrenceRule.NextDueAfter(DateTime dueUtc, bool hasTime)` in the Domain
computes the FIRST occurrence strictly after the anchor due date, in `Europe/Warsaw` via
NodaTime (`DateTimeZoneProviders.Tzdb`, already referenced by `WarsawDayBounds`):

| type | next |
|---|---|
| `daily` | due + 1 day |
| `every_n_days` | due + `interval` days |
| `weekdays` | the first ISO weekday in `weekdays` strictly after due's Warsaw date (wraps to next week) |
| `monthly` | next calendar month, day = min(`dayOfMonth`, days in that month) (end-of-month clamp) |

Date-only due (`has_time=false`): the result is the Warsaw-midnight instant of the new date
(the stored convention). Timed due: the SAME Warsaw wall-clock time on the new date
(`LocalDateTime.InZoneLeniently` — DST handled by the library, never offset arithmetic;
FR-092). The web mirror `lib/recurrence.ts` (`nextOccurrence`, date-fns-tz on
`lib/timezone.ts`) exists ONLY for the editor's preview text („następne: 12.06.2026") and
unit facts; the SERVER value is authoritative (Principle V). Both sides are unit-tested
against the same fact table (contract `recurrence-api.md` §Fact table), including a
DST-crossing week and the leap-year clamp.

**Rationale**: "computed from the original due date" (ASM-09) + "never skip" (Clarifications
Q2) make this a pure function of (rule, due) — no clock input — so it belongs in the Domain
and is trivially unit-testable; the Warsaw zone is the only calendar in the product.

## D4 — Trigger: the existing `SetTaskStatus` transition into `done`

**Decision**: no new endpoint for completion. Inside `SetTaskStatusHandler`, after an ACTUAL
transition into `Done` (the same-status no-op never spawns — idempotent under optimistic
retry), if `task.RecurrenceRule != null && task.DueDate != null`:

- `dueDay = WarsawLocalDate(DueDate)`, `today = WarsawLocalDate(timeProvider.GetUtcNow())`
  (day granularity for BOTH date-only and timed dues — "on or after its due date").
- `today >= dueDay` → spawn the successor **in the same transaction** (D5).
- `today < dueDay` → publish `SpawnRecurrenceSuccessor(TaskId, CompletedAtInstant)` with
  `DeliveryOptions.ScheduledTime = start of dueDay in Warsaw (UTC)` to a durable local queue
  `recurrence-spawn` (the `ReapDeletedTask` substrate: outbox-enrolled, survives restarts,
  Solo durability). Nothing is spawned now (US-06.AS-04 / EC-05).

The handler switches from `DateTime.UtcNow` to the injected `TimeProvider` so the boundary is
freezable in integration tests (`FixedTimeProvider` via `ConfigureTestServices`).

**Rationale**: completion is already one command with one authorization dispatch; piggy-backing
keeps "done + successor" atomic (Constitution VII) and needs no client change beyond
invalidation. The scheduled message is the only server-side deferred mechanism the stack has
and the spec names Wolverine explicitly.

**Alternatives**: a periodic cron sweep over `done` recurring tasks — rejected (polling, needs
its own idempotency bookkeeping, not the spec's "scheduled job"); client-triggered spawn on
startup — forbidden by EC-05.

## D5 — Successor assembly (FR-008 carry-forward) reuses the DuplicateTask precedent

**Decision**: a shared Application helper `RecurrenceSuccessor.BuildAsync(predecessor, now, …)`
used by both the immediate path (D4) and the deferred job (D7):

| field | successor value |
|---|---|
| id | new uuid v7 (server-generated — no client id exists for a server spawn) |
| createdBy | = predecessor's `CreatedBy` (ownership = the personal-space visibility key, FR-065) |
| title, description, priority, projectId | copied |
| dueDate / dueHasTime | `rule.NextDueAfter(due, hasTime)` (D3) / copied flag |
| recurrenceRule | copied verbatim (incl. `dayOfMonth` anchor) |
| status | `backlog`; `completedAt` null; fresh `createdAt`/`updatedAt`; version 0 |
| position | directly after the predecessor (`PositionRank.BetweenAfter`, the DuplicateTask D7 adjacency — extracted into a shared `AdjacentPosition` helper so both commands call one implementation) |
| cycleId / carriedOver | `null` / `false` (FR-008: unassigned to any cycle) |
| spawnedFromId | predecessor id (D2) |
| assignees | copied, RE-VALIDATED against current membership ∪ project owner (shared project only); dropped members vanish silently; `ClearDomainEvents()` so no `TaskAssigned` fires (no re-notification) |
| labels | ALL owners' `task_labels` rows copied (`ITaskLabelRepository.CopyAllAsync(from, to)` — NEW) — the per-user relation means each member's own labels follow; DuplicateTask copies only the caller's because it HAS a caller; a spawn has none |
| comments | not copied (not in FR-008's list) |

**Rationale**: FR-008's list is explicit; everything else resets. Server-generated ids are
fine here because idempotency is carried by D2, not by a client-supplied id.

## D6 — Undo = reopen (Clarifications Q1)

**Decision**: in `SetTaskStatusHandler`, on an ACTUAL transition OUT of `done` for a task that
has a live successor (`spawned_from_id = task.Id AND deleted_at IS NULL`):

- successor `status != done` → soft-delete it exactly like `DeleteTask` does (tombstone +
  version bump + scheduled `ReapDeletedTask` +30s — the slice-014 restore seam stays
  uniform);
- successor `status == done` → leave it, set its `spawned_from_id := NULL` (A "loses the
  link"; the chain has moved on).

Either way A's slot is free, so re-completing A spawns a fresh successor (D2's partial index
permits it). A pending scheduled `SpawnRecurrenceSuccessor` is NOT cancelled (Wolverine offers
no cheap cancel) — the job no-ops instead (D7).

**Rationale**: verbatim the clarified answer; soft-delete over hard-delete keeps data
recoverable and reuses the tested reaper path.

## D7 — The deferred job is idempotent by re-checking state, not by cancellation

**Decision**: `SpawnRecurrenceSuccessorHandler` (durable queue, NO HttpContext → exempt from
`AuthorizationMiddleware` exactly like `ReapDeletedTask`/`ReapDeletedComment`):

1. load A including deleted; absent / soft-deleted → no-op;
2. `A.Status != Done` or `!SameInstant(A.CompletedAt, message.CompletedAtInstant)` (µs
   resolution, the reaper's comparison) → no-op (reopened, cancelled, or re-completed later —
   a later re-completion carries its OWN message);
3. `A.RecurrenceRule == null || A.DueDate == null` → no-op (rule removed meanwhile);
4. a live successor already exists → no-op;
5. otherwise build (D5) + insert; a `DbUpdateException` on `ux_tasks_spawned_from_live` →
   swallowed as an idempotent no-op (the race backstop).

Authorization posture (spec IX): the job acts in the PREDECESSOR's context — the successor
inherits A's owner/project (visibility unchanged), and the only identity-sensitive step is
the assignee re-validation against CURRENT membership (D5). Documented in the contract; the
exemption is the established no-caller pattern.

## D8 — FR-010 cancel & rule removal

**Decision**: nothing new to build for cancel: only the `done` transition spawns (D4), so
`cancelled` never does; a cancel AFTER an early completion flips status away from `done` →
the scheduled job no-ops (D7 step 2). A cancelled instance that already has a successor keeps
it (cancel ≠ undo; Clarifications Q1 defines undo as leaving `done`, and cancel does leave
`done`, so the D6 rule applies: the successor is soft-deleted unless already done — the same
code path, no special case). Removing the rule (editor „Nie powtarza się") simply nulls
`recurrence_rule`; existing successors are untouched.

## D9 — Due-date guard (Clarifications Q4) enforced in the Domain

**Decision**: `Task` gains the invariant "RecurrenceRule != null ⇒ DueDate != null", checked
in `SetRecurrence(rule)`, `EditTask(...)` and `Reschedule(...)`: violating it throws
`RecurrenceRequiresDueDateException` (Domain) → mapped by `ProblemDetailsMiddleware` to
`422 recurrence_requires_due_date`. Covers every write path (editor whole-object save, the
`T`/drawer reschedule-to-null) without per-handler duplication. The editor blocks client-side
first (inline message) — the server code is the backstop.

## D10 — Where the rule is edited: a shared `RecurrenceField` in the editor AND the details drawer

**Decision**: one component `components/tasks/RecurrenceField.tsx` (select „Nie powtarza
się / Codziennie / Co N dni / W wybrane dni tygodnia / Co miesiąc" + the per-type inputs +
„następne: <date>" preview) rendered in BOTH `TaskEditor` (the slice-005 `E` dialog, hosted
by the daily views) and `TaskDrawer` (the „Szczegóły" panel reachable from EVERY list and
the Board). Disabled with the hint „Ustaw najpierw termin" when the task has no due date.

**Rationale**: code audit — the `E` editor dialog is mounted ONLY by `DailyView`
(Inbox/project „Edytuj" is the inline rename), while the drawer is the universal details
surface and already edits priority/due/description. Clarifications Q3 excludes quick-add and
the row menu; the drawer is the same "task editor" family, so AS-01's "creating or editing"
is reachable from every surface without a menu item. The Clarifications bullet is amended to
name both hosts.

## D11 — Transport: `EditTask` whole-object replace gains `recurrenceRule`; no new endpoint

**Decision**: `EditTaskRequest`/`EditTask` gain `required RecurrenceRuleDto? RecurrenceRule`
(validated by FluentValidation per D1's ranges). Every existing `editTask` caller (editor
save, drawer description commit) MUST pass the current rule through — the generated TS type
makes the field required so `typecheck` catches an omission (the slice-006 "flattening gap"
lesson applied at the request side). The drawer's own save of the field also goes through
`editTask` (whole-object).

**Rationale**: the rule is part of the editor's atomic save (`Ctrl+Enter`); a second request
would break atomicity. Zero new routes → the OpenAPI delta is schema-only.

**Alternatives**: `PATCH /api/tasks/{id}/recurrence` (SetPriority pattern) — rejected: two
writes per editor save, and the drawer would still need the whole-object path for the guard.

## D12 — Response widening & indicator

**Decision**: `TaskResponse` AND the flattened Today/Upcoming/Assigned rows gain
`recurrenceRule: RecurrenceRuleDto | null` (the 006 flattening gap must not recur — the daily
views render the indicator too). `TaskRow` (every list) and `BoardCard` render a text chip
with the Lucide `Repeat` icon + `recurrenceLabel(rule)` („codziennie" / „co 3 dni" /
„pn, śr" / „co miesiąc, 31."), never icon-only (FR-046/FR-044). `spawnedFromId` is NOT
exposed (no UI need).

## D13 — Client reconciliation of the spawn (SC-003 reading)

**Decision**: the done toggle keeps its optimistic paint (slice 002/005). The successor is
server-generated and surfaces on the mutation's settle: `toggleDone`/`setTaskStatus`
invalidate the view list keys (already the case) and, when the completed task carried a rule
and was due today-or-earlier, show a toast „Utworzono kolejne wystąpienie" + polite LiveRegion
announcement (FR-101). No provisional successor row is painted (a client-invented id would
duplicate against the refetch). The spec's Constitution-III sentence is amended to this
reading. Remote fan-out stays deferred to slice 016 (011 D14 posture).

## D14 — Integration-test shard

**Decision**: new namespace `TaskFlow.IntegrationTests.Recurrence` → new ci matrix shard
`recurrence` (`FullyQualifiedName~IntegrationTests.Recurrence`); the `identity-labels-infra`
complement gains `&FullyQualifiedName!~IntegrationTests.Recurrence`. Class names avoid the
substrings the `tasks-*` filters key on (they are namespace-gated anyway).

## D15 — Frozen clock + deferred-job testing

**Decision**: the suites override `TimeProvider` with `FixedTimeProvider` (ConfigureTestServices
hook) to pin "today" vs the due day; scheduled envelopes are asserted via the
`wolverine_*_envelopes WHERE status='Scheduled'` count (the comments-reaper helper, generalized
to a message type); the job itself is exercised directly via
`host.TrackActivity().ExecuteAndWaitAsync(ctx => ctx.SendAsync(new SpawnRecurrenceSuccessor(…)))`
with the clock advanced (the `ReapDeletedCommentHandlerTests` pattern).

## D16 — OpenAPI & error code

**Decision**: extend `TaskFlowDocumentTransformer` with the new error code
`recurrence_requires_due_date` (→ web `ERROR_UX` copy „Zadanie cykliczne musi mieć termin —
wyłącz powtarzanie albo ustaw termin."); the `RecurrenceRule` schema flows from the DTO. No
new operationIds. `gen:api` regenerates; `openapi-sync` guards.

## D17 — Migration & FR-051

**Decision**: one additive migration (`AddRecurrenceLink`: column + FK + partial unique
index; `recurrence_rule` already exists). The FR-051 auto-backup posture is unchanged from
slices 006–011 (deploy pipeline snapshots before `migrate`).

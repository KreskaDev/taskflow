# Research — Slice 011 (Cycles)

Phase-0 decisions. Every "NEEDS CLARIFICATION" from the technical context is resolved here;
spec-level ambiguities were resolved in the Clarifications session (spec.md, 2026-08-16) and are
referenced, not re-decided.

## D1 — Cycle aggregate & table

**Decision**: New `Cycle` aggregate (DDD pattern of `Label`/`Project`): strongly-typed `CycleId`,
fields `Name` (required, trimmed, ≤200), `StartDate`/`EndDate` (`timestamptz` UTC), `Status`
(`CycleStatus` enum: `Planned`/`Active`/`Closed`, stored as text like `TaskStatus`), `Version`
(OCC int, same convention as Task/Project), `CreatedAt`/`UpdatedAt`. Table `cycles`; migration
also adds the FK `tasks.cycle_id → cycles(id)` **ON DELETE RESTRICT** and
`tasks.carried_over boolean NOT NULL DEFAULT false` (D7).

**Rationale**: mirrors every prior aggregate; RESTRICT backs the FR-020 guard (only empty
planned/closed cycles are deletable) at the DB level — the handler check gives the friendly
error, the FK makes the invariant unbreakable.

**Alternatives**: `ON DELETE SET NULL` (Todoist-style detach) — rejected: FR-020 says deletion
of a non-empty cycle is refused, not softened.

## D2 — Task.CycleId stays a raw `Guid?`

**Decision**: keep `Task.CycleId` as the already-mapped nullable `Guid?` (slice-002 reserved
column); do NOT convert it to a value-converted `CycleId` type.

**Rationale**: the slice-005 lesson (EF/Npgsql cannot translate `Contains`/`IN` over a
value-converted NULLABLE FK — runtime 500). Grouping and metrics queries will filter/aggregate
over `CycleId`; a raw Guid keeps every LINQ shape translatable. The `Cycle` aggregate itself
uses a strongly-typed `CycleId` PK (non-nullable — safe).

## D3 — Single-active invariant enforcement

**Decision**: partial unique index `CREATE UNIQUE INDEX ix_cycles_single_active ON cycles((1))
WHERE status = 'active'` (EF: `HasFilter`) + handler-level check in `ActivateCycle` returning
the friendly conflict error. Activation is a dedicated command (`PATCH /api/cycles/{id}/activate`)
valid only from `Planned`.

**Rationale**: spec mandates the DB-level guarantee explicitly (spec lifecycle note); the
handler check produces the recoverable FR-049 message, the index wins races.

**Alternatives**: advisory locks / serializable transactions — heavier, not needed.

## D4 — Close + rollover is ONE transactional command

**Decision**: `PATCH /api/cycles/{id}/close` carries the rollover decision:
`{ rollover: "next" | "backlog" | "keep", overrides?: [{taskId, choice}], version }`.
"Handle individually" (US-05.AS-04) = a bulk default (`rollover`) plus per-task `overrides`
for the tasks the closer resolved individually; the server applies the default to every
remaining incomplete task (including tasks the caller cannot see — D10). The whole close
(status flip + all task moves + carried-over flags) commits in one transaction. Close with zero
incomplete tasks needs no rollover field (defaults to `keep`, a no-op).

**Rationale**: the review IS the close flow (Clarifications); one command keeps it atomic
(Constitution VII) and gives slice 014 a single operation to retrofit undo onto.

**Alternatives**: separate close then rollover ops — rejected by the clarified UX; risks a
half-closed state.

## D5 — "Next cycle" resolution & ordering tiebreaker

**Decision**: next = the planned cycle with the lowest `(StartDate, CreatedAt, Id)` strictly
by that composite ordering (spec: next PLANNED by start date; tiebreaker CreatedAt then Id —
deterministic per the date-overlap clarification). Resolved server-side inside `CloseCycle`;
if none exists and `rollover: "next"` was requested, the command fails with a dedicated
`no_next_cycle` error code → the UI shows the US-05.AS-06 "create a cycle first" prompt.

## D6 — Metrics are computed, not stored

**Decision**: no stored aggregates. `GET /api/cycles` returns every cycle with computed
per-cycle counts (`total`, `done`, per-status breakdown); "days remaining" is computed
client-side from `endDate` against **today in Europe/Warsaw** using the same date-fns-tz
reference-zone helpers the due-date rendering already uses (`lib/timezone.ts`), so client and
server agree on the boundary (FR-092). Counts aggregate over ALL tasks in the cycle
(team-wide numbers); soft-deleted tasks excluded.

**Rationale**: ~10-user scale (ASM-10) makes computed aggregates trivially cheap; stored
counters would add invariants for zero gain. Client-side days-remaining avoids a clock query
and re-renders correctly at midnight.

## D7 — "Carried over" flag lives on the task

**Decision**: `tasks.carried_over` (bool, default false). Set to `true` by the `keep` rollover
choice for each incomplete task left in the closing cycle; cleared whenever the task's cycle
assignment subsequently changes (any `SetTaskCycle` write, including to backlog). Never set by
manual assignment into a closed cycle (Clarifications).

**Rationale**: ENT-03 ties the flag to tasks "remaining in a closed cycle"; a task-level bool
is the smallest faithful model and serializes naturally in TaskResponse for the UI badge.

## D8 — FR-015 default-duration preference storage

**Decision**: server-side per-user column `users.cycle_default_duration_days`
(`int NOT NULL DEFAULT 14`), exposed via the existing user/profile surface: widened
`GET /api/users/me` response + new `PATCH /api/users/me/preferences
{ cycleDefaultDurationDays }` (1..90 validation). The /settings page gains a labelled number
field ("Domyślna długość cyklu (dni)"). The create-cycle form pre-fills
`end = start + duration`.

**Rationale**: the clarified answer is a per-USER setting on /settings; a server-side column
survives devices (localStorage would silently make it per-device) and the slice already ships a
migration, so the added column is free. Each cycle's real duration stays free per the same
clarification (dates are per-cycle).

**Alternatives**: localStorage (the D8/slice-010 presentation-state pattern) — rejected:
this is an account preference, not view state; team-wide setting — rejected (no admin surface,
OOS-17).

## D9 — API surface (all under the existing deny-by-default gate)

| Endpoint | Command/Query | Notes |
|---|---|---|
| `PUT /api/cycles/{id}` | `CreateCycle` | idempotent create (client uuid v7, repo convention); name+dates required, start<end |
| `PATCH /api/cycles/{id}` | `EditCycle` | name/startDate/endDate + version (OCC); allowed in every status |
| `PATCH /api/cycles/{id}/activate` | `ActivateCycle` | Planned→Active; single-active guard (D3) |
| `PATCH /api/cycles/{id}/close` | `CloseCycle` | Active→Closed + transactional rollover (D4) |
| `DELETE /api/cycles/{id}?version=` | `DeleteCycle` | Planned/Closed only + zero assigned tasks (FR-019/FR-020, EC-04); hard delete |
| `GET /api/cycles` | `GetCycles` | all cycles + computed counts (D6), ordered by (StartDate, CreatedAt, Id) |
| `GET /api/cycles/{id}/tasks` | `GetCycleTasks` | rows filtered to caller-visible tasks (D10); serves the Cycle view list + close review |
| `PATCH /api/tasks/{id}/cycle` | `SetTaskCycle` | `{cycleId: uuid\|null, version}`; task-visibility authz (SetPriority pattern); clears `carried_over` |
| `PATCH /api/users/me/preferences` | `SetUserPreferences` | D8 |

Cycle lifecycle commands authorize as **team-wide operations**: any authenticated, admitted
user may invoke them (spec: no per-user ownership, no role surface on Cycle). Wolverine
conventions per the slice-001 pattern (HTTP→bus delegation, public concrete types,
IncludeAssembly).

## D10 — Team-wide metrics vs caller-scoped rows

**Decision**: aggregate NUMBERS are team-wide (all tasks in the cycle); task ROWS
(`GET /api/cycles/{id}/tasks`, the close review list) are filtered by the caller's task
visibility (own personal tasks + tasks of projects they're members of — the FR-065 dispatch).
The close command's bulk default applies server-side to ALL incomplete tasks, visible or not
(it is a team-wide operation); `overrides` may only reference tasks the caller can see
(others → 404-posture rejection of the override entry).

**Rationale**: exactly what spec IX prescribes ("metrics reflect the cycle's assigned tasks
with per-task authorization still governing any individual task's detail"); the visible-rows
rule reuses the assigned-view precedent (slice 008) for cross-user task lists.

## D11 — By-cycle grouping of the project List (FR-024 completion)

**Decision**: `GroupByControl` gains the fourth option „Cykl"; `buildProjectGroups(tasks,
"cycle", cycles)` orders groups by cycle `(startDate, createdAt, id)` with the no-cycle group
labelled **„Bez cyklu"** LAST. Group label = cycle name. Empty groups omitted (010 rule).

**Rationale**: „Bez cyklu" (not „Backlog") dodges the EC-10 naming collision with the task
status „Backlog" group of the status grouping. Ordering mirrors D5 so every surface agrees.

## D12 — Cycle selector & menu affordance

**Decision**: new menu item „Cykl…" in `buildMenuItems` (wired via `TaskRowActions.onOpenCycle`,
after „Etykiety…", before „Przenieś do projektu…"), opening a `CyclePicker` dialog modelled on
`PriorityPicker`: lists **all** cycles (Clarifications) ordered per D5 with status suffix
(„(aktywny)" / „(planowany)" / „(zamknięty)"), plus „Bez cyklu" to clear; current assignment
checked. Board cards inherit the item automatically through the shared `buildMenuItems`.

## D13 — Cycle view route & sidebar

**Decision**: new route `/cycle` (single management surface per Clarifications): header with a
cycle switcher (defaults to the active cycle, else the next planned, else empty state per
FR-110 with „Nowy cykl" action), the FR-026 metrics strip (% done, days remaining, per-status
breakdown as labelled text — FR-044/FR-044 posture), the caller-visible task rows (TaskList
reuse, read-only positions), and the lifecycle actions (create/edit/activate/close/delete)
as visible buttons/dialogs. Sidebar gains a „Cykl" entry between the daily views and PROJEKTY:
shows the active cycle's name (FR-017); with no active cycle it reads „Cykl" and still
navigates to `/cycle`. Overdue active cycle (endDate past, still active) renders a „po
terminie" badge on the sidebar entry and the view header, and the view surfaces the close
prompt (Clarifications close semantics); "days remaining" shows `0 dni (po terminie)`.

## D14 — Fan-out defers to slice 016

**Decision**: no SignalR in this slice (infrastructure is owned by slice 016,
real-time-collaboration). Cycle lifecycle changes propagate to other members via normal query
refetch (React Query staleness/refocus), and the spec's fan-out/reconciliation language
transfers to 016 exactly like slice-010's remote-move announcements did (010 D11 note).
Local mutations announce via the existing polite LiveRegion (FR-101).

## D15 — Integration-test sharding

**Decision**: new namespace `TaskFlow.IntegrationTests.Cycles` + a dedicated 6th ci shard
`cycles` (`FullyQualifiedName~IntegrationTests.Cycles`), and the `identity-labels-infra`
complement gains `&FullyQualifiedName!~IntegrationTests.Cycles`.

**Rationale**: the cycles suite lands ~15–20 Testcontainers facts; dropping them into
`identity-labels-infra` risks the documented runner-capacity failure mode (MSB4166). The
complement edit keeps the "every class lands in exactly one shard" invariant.

## D16 — OpenAPI & typed client

**Decision**: extend `TaskFlowDocumentTransformer` with the new operations (operationIds:
`createCycle`, `editCycle`, `activateCycle`, `closeCycle`, `deleteCycle`, `listCycles`,
`getCycleTasks`, `setTaskCycle`, `setUserPreferences`) and new error codes
(`no_next_cycle`, `cycle_not_planned`, `cycle_active_conflict`, `cycle_not_active`,
`cycle_not_empty`, `cycle_active_delete_forbidden` → mapped into the web `ERROR_UX` union with
Polish copy). `TaskResponse` (and the flattened Today/Upcoming/Assigned row responses — the
slice-006 flattening gap must NOT recur) gain `cycleId` + `carriedOver`. `pnpm gen:api`
regenerates; `openapi-sync` guards drift.

## D17 — Migration & FR-051

**Decision**: one EF migration (`AddCycles`): `cycles` table + partial unique index (D3) +
`tasks.carried_over` + FK on `tasks.cycle_id` + `users.cycle_default_duration_days`. The
FR-051 auto-backup posture is unchanged from slices 006/008/009: the deploy pipeline snapshots
the DB before `migrate` (infrastructure in place; nothing new to build here).

## D18 — Create-form defaults

**Decision**: create dialog pre-fills name „Cykl N” (N = existing cycle count + 1, editable),
start = today (Europe/Warsaw), end = start + the D8 preference. Dates are date-only in the UI
(stored as the Warsaw-midnight convention already used for date-only due dates); `start < end`
is the only cross-field rule (Clarifications).

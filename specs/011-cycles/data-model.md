# Data Model — Slice 011 (Cycles)

Phase-1 design. Decisions referenced as D1–D18 (research.md).

## New aggregate: Cycle (ENT-03)

```text
cycles
├── id            uuid PK (CycleId, strongly typed — D1)
├── name          text NOT NULL (trimmed, 1..200; user-authored → sanitized output, FR-099)
├── start_date    timestamptz NOT NULL   (UTC; Warsaw-midnight date-only convention, D18)
├── end_date      timestamptz NOT NULL   (UTC; invariant: start_date < end_date — the ONLY
│                                         cross-field rule; overlap between cycles allowed)
├── status        text NOT NULL ('planned' | 'active' | 'closed')  — CycleStatus enum
├── version       int NOT NULL DEFAULT 0 (OCC, same convention as Task/Project)
├── created_at    timestamptz NOT NULL
└── updated_at    timestamptz NOT NULL

Indexes:
- PK
- ix_cycles_single_active: UNIQUE ((1)) WHERE status = 'active'   ← single-active invariant (D3)
- ix_cycles_ordering: (start_date, created_at, id)                ← D5 ordering support
```

### Lifecycle (state machine)

```text
            ActivateCycle                CloseCycle (+rollover, one tx — D4)
  planned ────────────────► active ─────────────────────────────► closed
     │        (D3 guard:       │  (manual only; end_date passing        │
     │      at most one        │   only marks it "overdue" in UI)       │
     ▼        active)          ▼                                        ▼
  DeleteCycle allowed      DeleteCycle FORBIDDEN               DeleteCycle allowed
  iff 0 assigned tasks     (EC-04 / FR-019)                    iff 0 assigned tasks
```

- No automatic transitions — server never moves a cycle by clock (Clarifications).
- `EditCycle` (name/dates) is legal in every status; `start < end` re-validated.
- Delete is a hard delete, guarded by handler (friendly `cycle_not_empty` /
  `cycle_active_delete_forbidden`) and by the FK RESTRICT backstop (D1).

## Touched: Task (ENT-01, owned by slice 002)

- `cycle_id uuid NULL` — already mapped (`Task.CycleId: Guid?`, D2). This slice adds the FK
  `→ cycles(id) ON DELETE RESTRICT` and starts writing it via `SetTaskCycle`.
- `carried_over boolean NOT NULL DEFAULT false` — NEW (D7). Set by the `keep` rollover for
  incomplete tasks in the closing cycle; cleared by ANY subsequent `SetTaskCycle` write;
  never set by manual assignment.
- Task domain gains `SetCycle(Guid? cycleId)` (bumps version, clears `CarriedOver`) and the
  close-flow internal transition (assign-to-next / clear / flag-carried).

## Touched: User (ENT-04, owned by slice 001)

- `cycle_default_duration_days int NOT NULL DEFAULT 14` — NEW (D8). Range 1..90 validated at
  the API boundary; only feeds the create-form pre-fill.

## Close/rollover semantics (D4, D10)

Input: `rollover ∈ {next, backlog, keep}` + optional `overrides[{taskId, choice}]`.

For every task in the closing cycle with `status ∉ {done, cancelled}` ("incomplete"):

| choice | effect on task |
|---|---|
| `next` | `cycle_id := <next planned cycle (D5)>`, `carried_over := false` |
| `backlog` | `cycle_id := NULL`, `carried_over := false` |
| `keep` | `cycle_id` unchanged (stays in the closed cycle), `carried_over := true` |

- Completed (`done`/`cancelled`) tasks are untouched — they remain the closed cycle's record.
- `rollover: next` requires a next planned cycle to exist, else `no_next_cycle` (US-05.AS-06).
- `overrides` may target only caller-visible tasks; the bulk default applies to the rest.
- Whole operation + the `active → closed` flip commit in ONE transaction; OCC on the cycle
  version gates the close itself.

## Metrics (computed — D6)

Per cycle (team-wide aggregates over non-deleted tasks with `cycle_id = c.id`):
`total`, `done` (status=done), `breakdown{backlog,todo,in_progress,done,cancelled}`,
`percentDone = done / max(1, total − cancelled)` (0 when the denominator is empty — cancelled
tasks are not outstanding work, so they do not deflate progress; the breakdown still shows
them). "Days remaining" is client-computed from
`end_date` vs today in Europe/Warsaw (FR-092); an overdue active cycle renders
`0 dni (po terminie)` (D13).

## API response shapes (D9, D16)

- `CycleResponse`: `{ id, name, startDate, endDate, status, version, createdAt, metrics:
  { total, done, breakdown } }` — from `GET /api/cycles` (ordered per D5; `createdAt` is
  exposed so the client-side D5 tiebreaker has its data — the server list is already in D5
  order and client sorting is defensive only).
- `TaskResponse` (+ flattened Today/Upcoming/Assigned rows): `+ cycleId: uuid|null`,
  `+ carriedOver: boolean`.
- `GET /api/cycles/{id}/tasks`: caller-visible `TaskResponse[]` (D10).
- `GET /api/users/me`: `+ cycleDefaultDurationDays`.

## Client state

- React Query keys: `["cycles"]` (list+metrics), `["cycle-tasks", cycleId]`; task mutations on
  cycle assignment invalidate both plus the view list keys (established onSettled pattern).
- No new localStorage keys (the D8 preference is server-side; view/group-by persistence keys
  from slice 010 are unchanged — group-by gains the `"cycle"` value in the existing
  `taskflow.project-groupby.<projectId>` key).

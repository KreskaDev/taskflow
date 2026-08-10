# Data Model: Project Board (Kanban) — slice 010

**Spec**: `spec.md` | **Research**: `research.md` | Phase 1 output

## No migration

This slice introduces **no entity, no attribute, no migration** (FR-051 untriggered). It
operates entirely on existing storage:

- `tasks.status` — `text NOT NULL DEFAULT 'backlog'` (EF `HasConversion<string>()` over the
  domain enum `TaskStatus { Backlog, Todo, InProgress, Done, Cancelled }`,
  `TaskConfiguration.cs:162-180`); all five values storable since slice 002.
- `tasks.completed_at` — `timestamptz NULL`; invariant: **set iff `status = 'done'`**.
- `tasks.position` — `text NOT NULL COLLATE "C"` (base-62 fractional index) — read-only for
  this slice (D6: Board moves change status only).
- `tasks.deleted_at` — soft-delete tombstone; every read keeps filtering `IS NULL`.

## Domain change (behavior, not schema)

### `Task.SetStatus(TaskStatus target, DateTime utcNow)` (replaces `MarkDone`/`MarkBacklog`)

| Transition | Effect |
|---|---|
| any → same status | idempotent no-op (no `Touch()`, no version bump) |
| any → `Done` | `Status = Done`; `CompletedAt = utcNow`; `Touch()` |
| `Done` → any other | `Status = target`; `CompletedAt = null`; `Touch()` |
| any other → any other | `Status = target`; `CompletedAt` stays `null`; `Touch()` |

No transition is forbidden at the domain level (the five statuses form a flat set, not a state
machine — product vision defines no illegal move; the Done-boundary rule of US-03.AS-05 is a
UI mapping rule, not a domain rule).

### Command (renamed): `SetTaskStatus`

```
SetTaskStatus { Id: TaskId (route), Status: string (body), Version: int (body) }
```
- Validator: `Status ∈ { backlog, todo, in_progress, done, cancelled }` (422 otherwise;
  D3), `Version ≥ 0`.
- Authorization: `TaskAccessGuards.LoadWritableTaskAsync(Id, EffectiveRole.Editor, …)` —
  personal task: owner only (404 on foreign); shared-project task: current membership with
  editor/owner (403 viewer, 404 non-member). Unchanged from the existing handler (FR-065..068).
- OCC: in-handler `task.Version != command.Version → VersionConflictException` (409) after the
  access guard; EF concurrency-token backstop unchanged.

## Read model

**Unchanged wire shape.** `GET /api/projects/{id}/tasks` keeps returning `TaskResponse[]`
(`status` already present). Server-side change is scoping only (D4):

- `TaskRepository.ListByProjectAsync(projectId, ct)` — project-scoped (`project_id = @id AND
  deleted_at IS NULL`), ordered by `position` (COLLATE "C" byte order), **no `created_by`
  filter**. Same repair applied to the counts/delete-cascade/duplicate-neighbor call sites.
- Authorization of the query handler is untouched: 404-first `FindReadableAsync`, then
  shared-only `RequireRole(Viewer)`.

## Column ↔ status mapping (FR-025 — client-side, single source)

`apps/web/src/lib/board.ts` (new, pure, unit-tested):

```
BOARD_COLUMNS = [
  { status: "backlog",     label: "Backlog" },
  { status: "todo",        label: "Do zrobienia" },
  { status: "in_progress", label: "W toku" },
  { status: "done",        label: "Zrobione" },
]            // "cancelled" is NEVER a column (EC-11)
```

- `buildBoardColumns(tasks)` → 4 columns, each `{ status, label, tasks }`; input filtered by
  `status !== "cancelled"`; within-column order = `position` rank (the List's flat order).
- `adjacentStatus(status, direction)` → the neighbouring column's status or `null` at a
  boundary (drives both menu move actions and their omission — US-03.AS-05).
- `buildProjectGroups(tasks, groupBy)` (List view, FR-024): `groupBy ∈ none | status |
  priority`; status groups in column order **plus "Anulowane" last when non-empty**; priority
  groups P0→P3 then "Bez priorytetu" (`priorityRank`); within-group order = flat order; empty
  groups omitted.

## Client state (no server persistence)

| State | Home | Persistence |
|---|---|---|
| view mode per project (`list` \| `board`) | `usePersistedProjectView(projectId)` | `localStorage["taskflow.project-view.<id>"]`; default `list` (D8) |
| group-by per project (`none` \| `status` \| `priority`) | same hook family | `localStorage["taskflow.project-groupby.<id>"]`; default `none` |
| board card focus/selection | component state (per column listbox) | none |
| in-flight optimistic move | TanStack Query cache (`["projects", id, "tasks"]`) | standard factory snapshot/rollback/settle; 409 → refetch-and-reapply-once recipe |

Cache interactions: the status mutation updates the project list cache optimistically and lets
`applyTaskToViewCaches` handle the cross-view caches (Today/Upcoming/Assigned already render
status-dependent affordances); `invalidateViewCounts()` after settle (counts may exclude
done/cancelled — verify against `GetViewCounts` semantics during implementation).

## Slice-016 transfer notes (D11 mechanism)

- Remote member's column move: announce via live region politely, coalesced — NOT realized
  here; the Board renders purely from the query cache so a future SignalR patch that writes the
  cache is sufficient.
- A remote patch must yield to a pending local optimistic move — the factory's context
  (mutation in flight per task id) is the hook point; no design here precludes it.

# Contract: GET /api/projects/{id}/tasks (scoping repair — slice 010)

Existing route (`operationId listProjectTasks`), unchanged wire shape (`TaskResponse[]`).
Server-side semantics repaired per `research.md` D4: the listing becomes **project-scoped**,
not owner-scoped.

## Semantics (after repair)

- Returns ALL live tasks of the project (`project_id = {id} AND deleted_at IS NULL`), ordered
  by `position` (COLLATE "C" byte order) — regardless of which member authored them
  (`createdBy` is provenance, FR-066).
- Includes tasks of every status, **including `cancelled`** — the Board hides cancelled
  client-side (EC-11); the List keeps them reachable.
- Labels stay caller-scoped (per-user relation; unchanged).

## Authorization (unchanged; FR-065/066/068)

404-first `FindReadableAsync(projectId, caller)`; personal project → owner only; shared →
current membership, `RequireRole(Viewer)`. Regrouping/mode-switching are pure client
projections of this read (viewer+ per spec).

## Repaired consumers of the same defect (one repository change, four call sites)

| Call site | Symptom before repair | Test |
|---|---|---|
| `GetProjectTasks` | member-authored tasks missing from List/Board | member-authored task visible to owner AND to another member |
| `GetViewCounts` | shared-project sidebar count undercounts | count includes member-authored tasks |
| `DeleteProject` (cascade) | member-authored tasks orphaned on delete | cascade removes/moves member-authored tasks per existing disposition rules |
| `DuplicateTask` (position neighbour) | duplicate lands mid-list vs member tasks | duplicate is adjacent in the full project ordering |

Integration class `ProjectScopedTaskListingTests` (name avoids all `tasks-core` exclusion
substrings — research D12).

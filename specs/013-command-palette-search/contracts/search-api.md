# Contract: Search API (slice 013)

One new read endpoint behind the deny-by-default authenticated gate (FR-068). Decisions: research.md.

## `GET /api/search?q=<text>` — `SearchTasksProjectsLabels` (operationId `search`)

Query: `q` — required string, trimmed length 0..200. Longer → `422 validation_failed`.
Unauthenticated → `401`.

Response `200 SearchResponse`:

```json
{
  "tasks":    [ TaskResponse, ... ],     // ≤ 20
  "projects": [ ProjectResponse, ... ],  // ≤ 10, archived included
  "labels":   [ LabelResponse, ... ]     // ≤ 10
}
```

Semantics (D2/D3/D4):
- tokens = whitespace-split `q` (≤ 5 tokens used, each ≤ 100 chars; `%`/`_`/`\` escaped); a row
  matches when EVERY token matches case-insensitively (`ILIKE`) somewhere in the row's text —
  tasks: title OR description; projects/labels: name. Empty `q` → all three arrays empty.
- ranking within a bucket: exact → prefix → word-start → substring; tasks open-before-done,
  then `updatedAt` desc; projects archived last, then name; labels by name.
- **Authorization = the caller's visibility, dispatched per candidate** (FR-065–068):
  - tasks: `created_by = caller` OR `project_id` in the caller's CURRENT memberships
    (`ListProjectIdsForUserAsync`); soft-deleted excluded. Viewer role suffices (read).
  - projects: owned by the caller ∪ current memberships; both archive states.
  - labels: owned by the caller only (per-user relation).
  - a former member (membership row deleted) gets NONE of that project's tasks/projects;
    assignee/creator status confers nothing (provenance only).
- No new error code. Response rows are the existing read models (no widening).

## Integration-test matrix (namespace `TaskFlow.IntegrationTests.Search` — D13)

Matching:
- `q=mleko` finds „Kup mleko" (title) and a task whose DESCRIPTION contains „mleko"; case-insensitive („MLEKO")
- token-AND: `q=kup mleko` finds „Kup mleko"; `q=kup ser` does not
- projects by name, labels by name; empty/whitespace `q` → three empty arrays; `q` of 201 chars → 422
- LIKE metacharacters in `q` (`%`, `_`) are literal (no wildcard injection)
- ranking: exact before prefix before substring (three tasks „Mleko", „Mleko sojowe", „Kup mleko" for `q=mleko`); done task after open task; archived project after live one
- caps: 25 matching tasks → 20 returned

Access (allow + deny — SC-013/SC-016):
- personal task of ANOTHER user never returned; own personal task returned
- shared project: member (viewer) sees the project and its tasks (incl. the owner's tasks); non-member sees neither
- former member (membership row deleted) sees neither the project nor its tasks even if they created/are assigned to a task
- archived shared project still returned to its members (with `archivedAt`), and its tasks too
- labels: another user's label with a matching name is never returned
- unauthenticated → 401

Performance characterization (SC-009, one fact, informational threshold):
- seed 10 000 personal tasks for one user (direct DB insert) + a few shared ones; `q=mleko`
  served in < 50 ms server-side on the Testcontainers PG (asserted via the response timing with a
  generous CI ceiling of 250 ms; the 50 ms figure is logged, not asserted, to keep CI stable).

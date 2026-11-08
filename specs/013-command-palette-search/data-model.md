# Data Model — Slice 013 (Command Palette & Search)

Phase-1 design. Decisions referenced as D1–D14 (research.md). **No new entity, no migration.**
The slice reads ENT-01 Task (slice 002), Project (004) and Label (006) through existing read
models.

## Read model: `SearchResponse` (D1)

```text
SearchResponse
├── tasks:    TaskResponse[]     ≤ 20, caller-readable (D4), soft-deleted excluded, ranked (D3)
├── projects: ProjectResponse[]  ≤ 10, owned ∪ member projects, archived INCLUDED (D5), ranked
└── labels:   LabelResponse[]    ≤ 10, the caller's own labels, ranked
```

- `TaskResponse` is the slice-002 row shape (incl. `cycleId`, `carriedOver`, `recurrenceRule`
  from later slices) — the palette renders title + location chip; `labels` are caller-scoped
  as everywhere.
- `ProjectResponse.archivedAt != null` drives the „(zarchiwizowany)" suffix + „Przywróć".

## Query normalization (D2)

```text
q (≤ 200 chars) → trim → split /\s+/ → tokens[0..5) (each ≤ 100 chars, LIKE metachars escaped)
match(candidate) = ∀ token: candidateText ILIKE '%' || token || '%'
  taskText    = title ∪ description
  projectText = name
  labelText   = name
empty tokens → every bucket empty (the client renders the action catalogue only)
```

## Ranking key (D3), per bucket

```text
rank = 0 exact (lower(text) = lower(q))
     | 1 prefix (text ILIKE q || '%')
     | 2 word-start (text ILIKE '% ' || q || '%')
     | 3 substring
tasks:    rank, open-before-done/cancelled, updated_at DESC
projects: rank, archived last, name
labels:   rank, name
```

## Access scoping (D4) — enforced in the handler, deny-by-default

```text
memberProjectIds = members.ListProjectIdsForUserAsync(caller)         (resolved once)
tasks    : deleted_at IS NULL AND (created_by = caller OR project_id ∈ memberProjectIds)
projects : owned(caller, both archive states) ∪ ListByIds(memberProjectIds, both archive states)
labels   : owner_id = caller
```

A former member's project ids are absent from `memberProjectIds` → none of that project's
tasks/projects can match (FR-066). `createdBy`/assignee never widen the set (provenance only).

## Client state

- Query key `["search", normalizedQuery]` (D9); `["search"]` family invalidated on leave/
  remove/unshare/account-delete success paths and on palette open (D10).
- Action catalogue: a frozen array in `lib/palette.ts` (D12) — no server state.
- View filter: component state `filter: string` per hosting view (D11); `filterTasks(rows,
  filter)` is pure; nothing persisted.

## Navigation targets (D6/D12)

| result | target |
|---|---|
| task (unprojected) | `/?task=<id>` |
| task (projected) | `/projects/<projectId>?task=<id>` |
| project | `/projects/<id>` (archived too) |
| label | no page of its own — selecting a label result replaces the palette query with the label's name (a second task search); the row says „Szukaj zadań z etykietą" |
| action | executes per the D12 table |

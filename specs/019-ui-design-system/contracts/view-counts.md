# Contract: Sidebar View Counts

**Endpoint**: `GET /api/views/counts`
**Realizes**: FR-109 (counts), FR-065/FR-068 (scoping) | **Read model**: `../data-model.md` → ViewCounts
**Style**: matches existing Wolverine attribute-routed endpoints; exposed in the OpenAPI
document (document transformer pipeline) so `pnpm gen:api` regenerates the typed client;
`openapi-sync` CI job must stay green.

## Request

- Method/path: `GET /api/views/counts`
- Auth: authenticated caller (BFF-proxied, signed internal token) — identical to every
  other read. No parameters.

## Response `200 OK`

```json
{
  "inbox": 3,
  "today": 5,
  "upcoming": 12,
  "assigned": 2,
  "projects": [
    { "projectId": "0d9f…", "count": 7 }
  ]
}
```

- All counts are integers ≥ 0; `projects` contains ONLY projects the caller can
  currently access (membership or personal ownership), excluding archived projects;
  order unspecified (client joins by `projectId`).
- Count semantics per `data-model.md` (incomplete = not done/cancelled; today includes
  overdue; boundaries in Europe/Warsaw).

## Errors

ProblemDetails per ADR-0009 (mapped by the generated client's `mapError`):
- `401` unauthenticated (missing/invalid identity) — standard pipeline behavior.
- No other domain errors: an authenticated caller with zero accessible data receives
  zeros and an empty `projects` array, not an error.

## Tests (Constitution VIII/IX)

- Integration (allow): counts equal the incomplete-filtered lengths of the
  corresponding view queries for a seeded caller (each field exercised, incl. an
  overdue task counted in `today` and excluded-from-count done/cancelled tasks).
- Integration (deny/scoping): a second user's tasks/projects never leak into the
  caller's counts; a project the caller was removed from disappears from `projects`.
- E2E `[E]` (UIT-024): sidebar renders the counts from this endpoint; counts update
  after a mutation (query invalidation).

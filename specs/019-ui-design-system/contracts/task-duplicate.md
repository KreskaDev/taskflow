# Contract: Duplicate Task

**Endpoint**: `POST /api/tasks/{id}/duplicate`
**Realizes**: FR-112 (UIT-041), FR-065/FR-068 | **Command model**: `../data-model.md` → DuplicateTask
**Style**: Wolverine command endpoint; in OpenAPI; typed client regenerated
(`pnpm gen:api`); `openapi-sync` green.

## Request

- Method/path: `POST /api/tasks/{id}/duplicate` where `{id}` = source task id.
- Auth: authenticated caller.
- Body:

```json
{ "newTaskId": "b7e2…" }
```

- `newTaskId` is a client-generated uuid (same idiom as `PUT /api/tasks/{id}` create):
  it makes the command idempotent and lets the UI paint the optimistic duplicate with
  its final id. Replaying with the same `newTaskId` returns the already-created
  duplicate (no second copy).

## Response `200 OK`

The full task response shape of the created duplicate (same schema as existing task
reads) — copied fields per `data-model.md` (title, description, priority, due date,
labels, membership-filtered assignees; fresh status/timestamps; position adjacent to
the source; same project/Inbox context).

## Errors (ProblemDetails, ADR-0009)

- `404` source task not found **or not accessible to the caller** (deny-by-default:
  inaccessible reads as nonexistent, matching existing task endpoints).
- `403`/domain error code if the caller's role in the source's shared project cannot
  create tasks there (viewer) — same code the create path uses.
- `400` validation (missing/malformed `newTaskId`).
- `409` id collision: `newTaskId` already exists but is NOT a duplicate of this source
  (idempotency conflict) — same conflict code family as the create path.

## Optimistic UI (Principle III)

`onMutate`: insert the duplicate (copied fields, client id) into the source view's
cache adjacent to the source row; `onError`: rollback via the existing
snapshot/restore pattern; `onSettled`: invalidate view keys + `['views','counts']`.

## Tests (Constitution VIII/IX)

- Integration (allow): owner duplicates an Inbox task; editor duplicates a shared-
  project task; assignee no longer a member is dropped from the copy; comments and
  completion state not copied; position adjacent to source; idempotent replay.
- Integration (deny): non-member of the source's project → 404-shaped denial; viewer
  role → create-denied; another user's Inbox task → denial.
- E2E `[E]` (UIT-041): "Duplikuj" in the row "⋯" menu creates the adjacent copy
  optimistically.

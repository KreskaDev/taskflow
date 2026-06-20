# Research: Task Capture (002)

This is the design-decisions record for slice 002 — Task Capture. It ADDS to the
slice-001 stack (Next.js 15 BFF + .NET 9 API with EF Core + Npgsql + Wolverine
messaging/outbox, deny-by-default authorization, RFC 9457 ProblemDetails, the
BFF→API HMAC carrier + authenticated proxy, and the OpenAPI document transformer);
it does NOT bootstrap. Every decision below is implementation-ready and consistent
with constitution v4.0.0, ADR-0001/0003/0005/0009, and the spec Clarifications
session of 2026-06-18 (state-stored CRUD, optimistic concurrency via a `version`
token, client-generated UUIDv7 id, idempotent create, done↔backlog toggle,
persisted `position` seeded newest-first, soft-delete + 30s reaper,
ownership-scoped deny-by-default authz).

---

## R1: Task Aggregate Shape & Invariant Placement

**Decision**: Add `Task : AggregateRoot<TaskId>` in `TaskFlow.Domain/TaskManagement/`,
with a strongly-typed `TaskId(Guid)` mirroring slice-001's `UserId` — but WITHOUT a
server-side `New()` factory: the id is client-supplied (UUIDv7) and passed into
`Task.Create`. Properties are all `private set`: `Title` (string), `Status`
(`TaskStatus` enum), `CreatedBy` (UserId, immutable — set in ctor, never reassigned),
`Position` (lexicographic rank string — see R5), `Version` (int concurrency token —
see R4), `CreatedAt`/`UpdatedAt` (UTC), `CompletedAt`/`DeletedAt` (UTC, nullable).
ENT-01 forward-compat fields are mapped as nullable columns but NOT surfaced in this
slice's behavior (`Description`, `Priority`, `DueDate` + `DueHasTime`, `ProjectId`,
`CycleId`, `RecurrenceRule`); labels/assignees are later-slice join tables and are
NOT added now.

Invariants enforced inside the aggregate:
- `Title` trimmed-non-empty and ≤500 (FluentValidation at the boundary AND an
  `ArgumentException` backstop in `Create`/`Rename`, matching `User.Create`'s
  `ThrowIfNullOrWhiteSpace`).
- `Status` defaults to `backlog` on create.
- `MarkDone(utcNow)` → status=done + stamp `CompletedAt`; `MarkBacklog()` →
  status=backlog + clear `CompletedAt` (the done↔backlog toggle, FR-003).
- `SoftDelete(utcNow)` sets `DeletedAt` (idempotent — a second call is a guarded no-op).
- `CreatedBy` is immutable.
- Every mutating behavior method stamps `UpdatedAt` and increments `Version`.

Static factory: `Create(TaskId id, UserId createdBy, string title, string position,
DateTime utcNow)`. Private parameterless/materialization ctor for EF.

**Rationale**: Mirrors the slice-001 `User` aggregate house style exactly (private
ctors incl. EF materialization ctor, static factory with `ThrowIfNullOrWhiteSpace`
guards, behavior methods taking injected `utcNow` for testability, `AddDomainEvent`
from the base). ADR-0003 Decision 5 mandates state-stored EF Core (not
event-sourced), Decision 4 mandates UUIDv7, and the invariant list (status default
backlog, completed_at iff done, immutable createdBy, soft-delete tombstone excluded
from queries) comes verbatim from ADR-0003 'Invariants'. Authorization stays OUT of
the aggregate (ADR-0003 Decision 6, ADR-0005).

**Alternatives considered**: Anemic CRUD entity with public setters — rejected by
ADR-0003 ('Not anemic CRUD — aggregates own behavior and invariants'). A `TaskStatus`
value-object struct vs. a plain C# enum mapped via `HasConversion<string>()` — the
plain enum is sufficient and simpler. The full enum
(`backlog`/`todo`/`in_progress`/`done`/`cancelled`) is stored from day one (only
`backlog`/`done` are reachable in this slice) to avoid a later enum-widening migration
when slices 003/005 introduce the intermediate states.

---

## R2: Idempotent Create via Client-Supplied UUIDv7

**Decision**: `CreateTask` carries the client-generated `Id` (Guid, UUIDv7-shaped),
`Title`, and the client-computed `Position` (see R5). The handler is
insert-if-not-exists keyed on `id`:
- (a) no row → insert, return the created DTO;
- (b) a row exists AND `row.CreatedBy == caller` → treat as an idempotent replay,
  return the existing row UNCHANGED (create is NOT a replace; all edits go through the
  dedicated rename/status/position commands under the version rule);
- (c) a row exists owned by a DIFFERENT user → reject with `404 not_found` (NOT 403),
  so the id space is not an enumeration oracle.

Defense-in-depth: the DB primary key on `id` makes a concurrent double-insert race
surface as a unique-violation, which the handler catches and re-resolves through the
same find-then-decide path (the race collapses to the idempotent-replay branch). A
create that reuses the id of a soft-deleted (tombstoned) row is treated as
not-found-for-the-caller and rejected — the id is spent; recreate uses a fresh id.

The HTTP verb is `PUT /api/tasks/{id}` (insert-if-not-exists by URI; `{id}` binds from
the route, `title`/`position` from the body), returning **200** TaskResponse on both
insert and idempotent-hit (mirroring the slice-001 `Task<T>`→200 pattern, avoiding the
explicit `IResult` ceremony a 201/200 split would require).

**Rationale**: FR-001 and the 2026-06-18 Clarification require insert-if-not-exists by
id, idempotent on retry, serving optimistic UI (the optimistic row already carries its
final id). Returning `404 not_found` for a foreign id mirrors slice-001's posture of
never confirming the existence of resources the caller can't see, and avoids an
enumeration oracle. The PK-as-backstop parallels ADR-0003 Decision 2's 'DB unique
index as a hard backstop'. `not_found` already exists in the error enum, so no enum
change is needed for this path. PUT-to-{id} expresses idempotent upsert-by-URI cleanly
and matches the client-known-id model.

**Alternatives considered**: `POST /api/tasks` with id in body — rejected: PUT-to-{id}
is the natural idempotency fit for a client-generated id. Returning 409 for a duplicate
id — rejected: a same-owner retry must be SUCCESS (idempotent), not a conflict; 409 is
reserved strictly for the stale-`version` case (R4). Returning 403 for a foreign id —
rejected: leaks that the id exists. A 201-on-create/200-on-hit split — rejected for v1:
needs an explicit return type the slice-001 pattern doesn't use, and the client already
knows the id so Location/201 buys nothing. Pure `INSERT ... ON CONFLICT DO NOTHING` at
the SQL layer — viable only as the race backstop; the find-then-decide handler path is
needed anyway to distinguish owner, so catch-unique-violation is the primary and the PK
is the race backstop.

---

## R3: Toggle-Done as a Desired-State Command (Idempotent Under Retry)

**Decision**: The toggle endpoint accepts the DESIRED target state, not a blind
server-side flip: `PATCH /api/tasks/{id}/status` with body `{status:'done'|'backlog',
version}`. The handler calls `MarkDone(utcNow)` (status=done, completed_at=now) when
the target is `done`, and `MarkBacklog()` (status=backlog, completed_at=null) when the
target is `backlog`. The `Task.status` schema exposes the full FR-003 enum
(`backlog`/`todo`/`in_progress`/`done`/`cancelled`) for forward-compat, but the toggle
REQUEST this slice accepts only `backlog`↔`done` targets.

**Rationale**: A stateless server-side toggle is non-idempotent and races under SC-003
optimistic retry (two retries of one keypress cancel out). Sending the desired state
makes the write idempotent and safe to retry. Exposing the full enum now avoids a later
breaking OpenAPI enum widening when slices 003/005 introduce `todo`/`in_progress`.

**Alternatives considered**: A blind `POST /api/tasks/{id}/toggle` that flips
server-side — rejected: non-idempotent, races badly under optimistic retry.
Restricting the schema enum to just `backlog`/`done` now — rejected: would force a
breaking enum widening later.

---

## R4: Optimistic-Concurrency `version` Token & the 409 Path

**Decision**: Use a mapped `int Version` concurrency token on the Task entity (NOT
Postgres `xmin`), configured `.IsConcurrencyToken()` and incremented by the aggregate
on every mutating behavior method. `version` is returned in the Task DTO; every
mutating command that requires concurrency safety (rename, set-status, reorder) carries
the client's last-seen `version` in the request body. The handler loads the row,
asserts ownership, compares versions (`row.Version != command.Version` → throw a new
`VersionConflictException`), applies the change, increments `Version`, and
`SaveChangesAsync`. EF's `DbUpdateConcurrencyException` is the backstop for a lost race
between load and save. Both map to **HTTP 409 with a NEW additive error code
`version_conflict`** — explicitly NOT `conflict_lww`.

The client refetches (invalidate `['tasks']`) and reapplies intent once (see R10).

**DELETE is version-free / idempotent**: `DELETE /api/tasks/{id}` carries NO version
and always succeeds with 204 if the row is the caller's and not already tombstoned; a
caller re-deleting their OWN already-tombstoned row is the idempotent **204** no-op (NOT
404, NOT 409 — the 404 posture applies only to foreign/absent ids; see R17). The
delete-rollback UX concerns 404/network and the optimistic row reappearing in
place, not racing a concurrent edit — so a stale-delete 409 buys nothing and would fork
the uniform version-in-body story. NOTE — this version-free DELETE deliberately
SUPERSEDES the broader 'every mutating command carries version' assumption stated in the
write-side and bff-web research inputs (both implied DELETE echoes `version`). Only the
api-contract input reached this conclusion; it is the adopted one. Downstream authors
MUST NOT wire a `version` param or 409 handling on the delete mutation — soft-delete is
idempotent and recoverable, so a stale-version 409 would only roll back the optimistic
removal for no safety gain.

**Rationale**: An explicit mapped `int` is portable, visible in the OpenAPI DTO (so the
typed client can round-trip it), and trivially testable (allow: matching version
succeeds and bumps it; deny: stale version → 409). It avoids exposing the
Postgres-internal `xmin` (`uint` system column) in the public contract, which would be
a leaky, provider-coupled token. The load-then-compare gives a deterministic 409 even
in the common single-writer case (a client holding a stale optimistic copy), while
`DbUpdateConcurrencyException` covers the true interleaved race.

A DISTINCT `version_conflict` code is required because the existing `conflict_lww`
literally means last-write-wins SUCCEEDED ('your change overwrote theirs') — the
opposite of this slice's clarified semantics (the stale write is REJECTED and the
client must refetch+reapply). `conflict_lww` has a legitimate distinct future home:
FR-040's undo/restore is a normal optimistic write subject to last-write-wins
(slice 014). ADR-0009 blesses additive codes and treats renaming/repurposing a code as
a breaking change.

**Alternatives considered**: Postgres `xmin` via `UseXminAsConcurrencyToken()` —
rejected: couples the wire contract to a Postgres system column, surfaces as an opaque
`uint`, harder to seed/assert in tests. Reusing `conflict_lww` — rejected by the
clarification (edits are NOT LWW) and it steals the code reserved for slice-014 undo. A
`byte[]` rowversion — Npgsql maps this to `xmin` anyway; same leak. `?version=` query
param or ETag/If-Match/412 on DELETE — rejected: forks the version-in-body convention
for marginal value, and a stale delete needs no rollback.

---

## R5: Persisted `position` — Lexicographic Rank, Newest-First, `COLLATE "C"`

**Decision**: Persist order in a single `position` column holding a fractional /
lexicographic rank string (the `fractional-indexing` / LexoRank-style scheme): a short
variable-length ASCII string compared byte-ordinally. The column is `varchar`
`COLLATE "C"` NOT NULL; the rank alphabet is the standard fractional-indexing subset
(digits + lowercase letters). A create, a reorder, and the newest-first seed all reduce
to computing `between(left, right)` (either bound nullable) and writing ONLY the
moved/created row — O(1) writes.

`position` is CLIENT-AUTHORITATIVE on create (mirroring the client-generated UUIDv7 id,
FR-001): the create payload carries the computed `position`. **Ordering direction is
pinned once: ascending rank, top-is-lowest.** Newest-first seed = `between(null, head)`
for a non-empty list (a rank strictly less than the current top), and
`between(null, null)` (a mid-alphabet seed) for the empty-list first task. Reorder
(Alt+↑/↓): moving down past neighbor X = `between(X, Xnext)` (Xnext may be null at the
tail); moving up past neighbor Y = `between(Yprev, Y)` (Yprev may be null at the head).

The server is a format-VALIDATOR only (alphabet + parseable), NOT a generator — it is
the sole writer under the version guard but does not compute ranks. On 409, reapply is
INTENT-based: refetch and recompute `between()` against FRESH neighbor ranks for the
user's intent ('place after X' / 'before Y'), never re-send the stale-derived rank.

**Rationale**: The deciding discriminator is this slice's own per-row `version` token,
not generic 'scales better'. A fractional rank touches exactly ONE row per reorder, so
the version model stays clean: one row's version bumps, a stale write conflicts on that
one row → 409 → refetch+reapply. Integer-with-renumber actively FIGHTS the per-row
token: its exhaustion fallback renumbers N rows, bumping N versions, which under a
per-row token is a conflict storm (every concurrent reader/writer of those rows
spuriously 409s). A rank string has no precision ceiling (it grows a character instead
of renumbering), so that fallback path — and its conflict storm — never exists.
Performance falls out: the client holds neighbor ranks in memory so `between()` is a
microsecond computation with no round-trip (SC-003 16ms); a reorder/create is a
single-row indexed write (SC-012 p95<200ms). Server-as-validator (not generator)
sidesteps the cross-language determinism burden — we never have to prove the TS client
and C# server emit byte-identical `between()` output. FR-102 + FR-021 require a
persisted `position` seeded newest-first.

**`COLLATE "C"` is mandatory on BOTH the column AND every index/ORDER BY over it.** This
is THE classic lexicographic-rank production bug: Postgres' default locale-aware
collation does NOT sort byte-ordinally (it reorders punctuation, may special-case,
ignores spaces), silently breaking fractional-key ordering. The failure is insidious —
ranks compare correctly in TS (code-unit order) so the client paints the right order,
but the server's `ORDER BY position` under default collation returns a DIFFERENT order
and the two drift. EF Core: `.UseCollation("C")` on the property + the same collation on
the index builder, so the migration emits both with `COLLATE "C"`. The rank alphabet
must be pinned IDENTICALLY for the TS generator and the C# format-validator.

**Alternatives considered**: (1) Double-precision float `position` + midpoint
`(A+B)/2` with a per-user renumber fallback — a GENUINE tradeoff for a ~10-user app,
not a strawman, but rejected on (a) FR-102's 'no whole-list renumber at 10k' intent and
(b) the version-conflict-storm its rare fallback still triggers — NOT on scale. (2)
Plain integer + fixed gaps + renumber-on-exhaustion — rejected, same conflict-storm
objection plus a guaranteed eventual renumber cost. (3) Linked-list `prev_id`/`next_id`
— rejected: 'list my tasks in order' becomes a recursive pointer-walk instead of one
indexed `ORDER BY`, hostile to the SC-010 query shape. (4) `bytea` byte-compare —
rejected as less inspectable (ASM-08) with no benefit over an ASCII rank under
`COLLATE "C"`. (5) Server-generated ranks with the client mirroring the algorithm —
rejected: imposes a TS↔C# byte-identical-generator obligation for zero gain. (6)
Value-based reapply (re-send the optimistically computed rank on 409) — rejected:
re-applies a key derived from stale neighbors, can mis-place or re-collide.

**Reserved (NOT implemented this slice)**: a periodic per-user rank rebalance is held
in reserve for the unlikely case of fractional-key length growth from prepend-heavy
newest-first creates; bounded and fine at this scale. It is a monitored property, not a
bug.

---

## R6: Equal-Rank Tie-Break & No Unique Constraint on `position`

**Decision**: Canonical order is `ORDER BY position, id` where `id` is the
client-generated UUIDv7 (time-ordered, deterministic). There is NO unique constraint on
`position` (nor on `(created_by, position)`). Equal-rank collisions — two concurrent
inserts/reorders that legitimately compute the same rank because each client saw the
same neighbors — are resolved by the `id` tie-break and lazily smoothed on the next
reorder of either row.

**Rationale**: A same-rank collision is benign and must resolve deterministically, not
as an error. Tie-break by `id`, not by a 409 and not by a unique constraint: UUIDv7 is
time-ordered, so `ORDER BY position, id` is stable and reproduced identically by client
and server. Canonical-hex UUIDv7 string order matches Postgres `uuid` byte order, so TS
and Postgres agree on the tie-break (the data-model must state this so a reviewer
doesn't store ids in a form that breaks the agreement). A UNIQUE constraint on
`position` would turn a harmless collision into a spurious failure. A 409 is reserved
STRICTLY for a stale `version` on the moved row — do NOT conflate rank-collision with
version-conflict.

**Alternatives considered**: Unique `(created_by, position)` + retry-on-conflict —
rejected: manufactures a conflict where none exists, adds a retry loop, and collides
conceptually with the version-based 409.

---

## R7: Ownership-Scoped List Query & the Serving Index

**Decision**: `GET /api/tasks` (listTasks) returns the caller's FULL non-deleted task
set, scoped in the query itself: `WHERE created_by = currentUser.Id AND deleted_at IS
NULL ORDER BY position, id`. NO server pagination — client-side virtualization handles
10k rows (SC-010/SC-011). The serving index is ONE partial composite index
`ix_tasks_created_by_position` on `(created_by, position)` WHERE `deleted_at IS NULL`,
with `position` under `COLLATE "C"`; optionally `(created_by, position, id)` to make the
`, id` tie-break index-only (a no-measurable-cost choice at 10k). The lean list DTO
(`id`, `title`, `status`, `position`, `version`, `createdAt`, `updatedAt`,
`completedAt`) keeps the single-payload serialization tight.

**Rationale**: `created_by` leads → ownership-scoped (FR-065 personal/ownership
branch), per-user list is a contiguous range. `position` second → ORDER BY satisfied by
the index. `WHERE deleted_at IS NULL` partial → tombstones (FR-097) excluded from the
index itself, matching the authorization-scoped query that must never return them, and
keeping the index small. SC-010/SC-011 commit to client-side virtualization at 10k
items, which presupposes the full set is delivered; pagination would contradict that and
complicate optimistic reconciliation. SC-010's 60fps is a RENDERING concern largely
orthogonal to ordering: the query returns pre-sorted rows and the virtualizer renders
only the visible window; the ordering model's only obligation is keeping this query a
single indexed range scan.

**Alternatives considered**: Cursor/offset pagination — rejected: contradicts the
client-side-virtualization success criteria; flagged instead as a payload-size risk the
data-model must size against SC-011 (<300MB tab memory) and SC-012 (p95<200ms). A
global `(position)` index — rejected (not ownership-scoped; scans across users). A
non-partial index — rejected (indexes tombstones the query never wants and forces a
residual `deleted_at` filter). Default sort by `created_at` — rejected: spec mandates
the persisted `position`.

---

## R8: 30s Soft-Delete Reaper as an Idempotent, Restore-Aware Durable Job

**Decision**: `DeleteTask` performs the soft-delete (sets `DeletedAt`, bumps `Version`,
`SaveChanges`) and then publishes a Wolverine SCHEDULED message
`ReapDeletedTask(TaskId, DeletedAtInstant)` delayed 30s, enrolled in the same
transaction via the outbox (mirroring slice-001's
`PublishMessage<AccountDeletionRequested>().ToLocalQueue(...)` + `AutoApplyTransactions`).
Use scheduled delivery (`DeliveryOptions { ScheduleDelay = TimeSpan.FromSeconds(30) }`).
`ReapDeletedTaskHandler` is idempotent and RESTORE-AWARE: it loads the row and
hard-deletes ONLY if the row still exists AND `DeletedAt` is non-null AND matches the
scheduled instant (i.e. it wasn't restored or re-deleted); if the row is gone or was
un-deleted, it no-ops. The reaper queue/handler is EXEMPTED from `AuthorizationMiddleware`
exactly like `AccountDeletionRequested` (it runs off the durable queue with no
HttpContext, so `ICurrentUser.IsAuthenticated` is false). Register via
`opts.PublishMessage<ReapDeletedTask>().ToLocalQueue("task-reaper")` and add the
message type to the auth-policy exclusion predicate.

**Rationale**: Directly reuses the proven slice-001 outbox + durable-local-queue +
Solo-durability + auth-exemption pattern, so the reaper publish commits atomically with
the soft-delete and survives restart. Restore-aware idempotency
(load-and-check-DeletedAt-still-set) is mandated by the brief ('hard-deletes after the
window — idempotent') and is forward-compatible with slice-014 restore (a restored task
clears `DeletedAt`, so the reaper must skip it; a blind hard-delete would create a
slice-014 retrofit conflict). Wolverine's native scheduled delivery avoids a hand-rolled
timer/cron. The reaper is background infrastructure (a hosted/durable-queue handler),
NOT a REST endpoint — it must not pollute the API surface.

**Alternatives considered**: A periodic sweep job (poll for `deleted_at < now-30s`) —
viable and simpler to reason about under restart, but the brief specifies a per-delete
scheduled durable message that mirrors the existing event-driven pattern; a sweep is
held in reserve as a backstop. A client-side-only 30s timer — rejected: the
tombstone+reaper must be server-authoritative (FR-097, Principle V).

---

## R9: Command/Query Set, Handler Layout & Per-Handler Allow+Deny Tests

**Decision**: Six handlers in
`TaskFlow.Application/TaskManagement/{Commands,Queries}`: `CreateTask`, `RenameTask`,
`SetTaskDone` (set-status), `ReorderTask`, `DeleteTask` (soft-delete), `GetMyTasks`
(listTasks). Each is a `sealed record` command/query + a `static class XHandler` with a
`static Task Handle(...)`, dispatched from thin `[Wolverine*]` endpoints in
`TaskFlow.Api/Endpoints/TaskEndpoints.cs` via `bus.InvokeAsync<T>` (mirroring
`UserEndpoints`). The endpoints class is PUBLIC with the CA1515 `SuppressMessage` like
`UserEndpoints` (Wolverine.Http discovery). Handlers use
`ArgumentNullException.ThrowIfNull` on injected deps, `.ConfigureAwait(false)`, and
inject `DateTime.UtcNow` as `utcNow` into aggregate methods.

REST surface (all under deny-by-default ownership scope, all errors → ProblemDetails,
all via the BFF `/api/proxy`):
- `PUT /api/tasks/{id}` → createTask; body `{title, position}`; 200 (insert or
  idempotent-hit), 401, 404 (foreign id), 422.
- `GET /api/tasks` → listTasks; `TaskResponse[]` ordered `position, id`; 200, 401.
- `PATCH /api/tasks/{id}/title` → renameTask; body `{title, version}`; 200, 401,
  404 (foreign/absent/tombstoned), 409, 422.
- `PATCH /api/tasks/{id}/status` → setTaskDone; body `{status, version}`; 200, 401,
  404 (foreign/absent/tombstoned), 409.
- `PATCH /api/tasks/{id}/position` → reorderTask; body `{position, version}`; 200, 401,
  404 (foreign/absent/tombstoned), 409.
- `DELETE /api/tasks/{id}` → deleteTask; soft-delete; NO version; 204 (idempotent — a
  re-delete of the caller's OWN tombstone is also 204, NOT 404), 401, 404 (foreign/absent).

(No task op emits **403** this slice — see R17; `403 forbidden` is reserved for the
slice-007+ shared-project insufficient-role case.)

Ownership scoping: every **non-DELETE WRITE** handler loads the row by id and must resolve a
foreign / absent / soft-deleted row to **`404 not_found` BEFORE** the OWNERSHIP branch
(`policy.RequireOwnership(task.CreatedBy)`) could throw 403 — either load-scoped-to-not-found
(query already filtered to the caller + non-deleted, so a non-owned/absent row simply isn't
found) or remap the `ForbiddenException` the ownership branch would throw to
`NotFoundException` for this slice. The net effect: **no task op emits 403** (consistent
with R17); `RequireOwnership` is still the policy seam, but its non-owner outcome surfaces as
404 here, with `ForbiddenException`→403 reserved for the slice-007+ shared-project role case.
**DELETE is the deliberate exception to the non-deleted filter: its load is owner-scoped but
TOMBSTONE-INCLUSIVE** (it does NOT apply the generic `deleted_at IS NULL` predicate) — that is
the only way to distinguish the caller's OWN already-soft-deleted task (→ idempotent **204**
no-op) from a foreign/absent id (→ **404**). The generic "filtered to the caller + non-deleted"
load rule therefore governs the non-DELETE writes/reads ONLY; applying that filter to DELETE
would make the own-tombstone unfindable and wrongly return 404 instead of the promised 204.
Foreign/absent on DELETE still → 404 (the tombstone-inclusive query, scoped to the caller,
simply doesn't find them).
`GetMyTasks` is scoped in the query (R7). This is the ONLY authorization path in this slice —
the ownership branch (createdBy = caller); there is NO shared-project branch.

Persistence seam: add `ITaskRepository` (Application/TaskManagement) +
`TaskRepository` (Infrastructure/Persistence) over `AppDbContext`, mirroring
`IUserRepository`/`UserRepository`; register `AddScoped<ITaskRepository,TaskRepository>()`.

Tests: one `*Tests.cs` per handler under
`tests/TaskFlow.IntegrationTests/TaskManagement/`, EACH with at least one Allow (caller
acts on own task) and Deny (a different user's task → 404, a soft-deleted foreign task →
excluded/404, no-JWT → 401), reusing `IntegrationTestBase` + `TestJwtHelper` +
`ApiResponse` helpers as-is. Every mutating handler additionally ships a stale-`version`
→ 409 test; `CreateTask` ships an idempotent-replay test (same id+owner → success; same
id different owner → 404) and a UUIDv7-id test; `ReorderTask` ships an equal-rank
tie-break test. Domain unit tests in
`TaskFlow.UnitTests/Domain/TaskManagement/TaskTests.cs` cover the aggregate invariants
(title bounds, default backlog, done↔backlog + completed_at stamping/clearing,
immutable createdBy, soft-delete sets deleted_at).

**Rationale**: This is the slice-001 Wolverine handler pattern transcribed onto Task
(thin endpoint → bus → Application handler discovered via the existing
`IncludeAssembly(typeof(ICurrentUser).Assembly)`; public concrete injected types;
static handlers). One-command-per-operation makes the deny matrix mechanical and gives
each operation its own validation/concurrency behavior and operationId clarity, exactly
satisfying SC-013/SC-016 and the spec's 'every Task command/query handler ships an allow
AND a deny test'. FR-065's ownership branch is the only authorization path here.
Excluding soft-deleted rows from the authorization-scoped query is the FR-097 day-one
requirement. Authorization at the pipeline level is already guaranteed by the global
`AuthorizationMiddleware.Before` woven on BOTH the message pipeline and the HTTP
endpoint group — slice 002 adds NOTHING to that wiring; it adds per-handler
`RequireOwnership` / query-scoping.

**Alternatives considered**: Instance (non-static) handler classes — slice-001 uses
static handlers; keep consistent. A single multiplexing CRUD/PATCH handler — rejected:
each command needs its own allow+deny surface, validation, and operationId; collapsing
them defeats the deny matrix. Putting ownership checks generically in the pipeline
middleware — rejected: the middleware only enforces authentication (it has no resource
to check); per-resource ownership must be in the handler after the row is loaded,
exactly as ADR-0005/slice-001 do.

---

## R10: BFF — Capture Flow, Optimistic Mutations, Virtualized Keyboard List

**Decision (capture flow, US-01.AS-01/06/07)**: A global keydown listener on the app
shell intercepts bare `C` (when no text input is focused — see R11) and opens a capture
surface that mounts the title input and calls `.focus()` SYNCHRONOUSLY in the same tick
(no network, no async), satisfying the 16ms paint budget (SC-003). The capture surface
REUSES the slice-001 `Dialog.tsx` focus contract (initial focus into the title input,
focus trap, Esc dismisses, focus returns to the invoker). On Enter with a non-empty
trimmed title: the client mints a UUIDv7 id, computes the newest-first `position`
(R5), fires the optimistic create mutation, clears + closes. On Esc: close, create
nothing, restore focus (AS-07). Empty/whitespace-only on Enter is a no-op (mirrors the
API non-empty rule). The composer and its focus call MUST be synchronous on the
keypress; the UUIDv7 minter and virtualization lib MUST NOT be lazy-loaded on the
capture path (any dynamic import/suspense before mount blows the 16ms budget).

**Decision (optimistic mutations, SC-003)**: TanStack Query v5 mutations using the
`onMutate`/`onError`/`onSettled` cancel-snapshot-rollback recipe against a SINGLE list
query key `['tasks']`. `onMutate`: `cancelQueries`, snapshot the FULL ordered list,
apply the change to the cache (create → insert at top; toggle → flip
status+completedAt; rename → replace title; delete → remove row keeping the full
ordering snapshot; reorder → move + recompute `position`) and return the snapshot as
context → paints within 16ms. `onError`: restore the snapshot (a deleted row reappears
in its ORIGINAL position, not at top) and announce the mapped FR-049 message via the
polite live region. `onSettled`: invalidate `['tasks']` to reconcile with server truth.
The client treats server `position` as authoritative on reconcile and only approximates
optimistically (so `onSettled` invalidation doesn't visibly 'jump' rows).

**Decision (409 reapply, per-operation, capped at one retry)**: On `version_conflict`,
invalidate `['tasks']` to pull server truth, then reapply intent ONCE: RENAME →
re-issue with the user's typed title against the refetched row's new version; TOGGLE →
re-evaluate against the refetched status (no-op if the server already reflects the
intent, else retry with the fresh version); REORDER → recompute the target `position`
from the refetched ordering and retry, dropping the move (and announcing via toast) if
the row was concurrently deleted. A second conflict surfaces the FR-049 message and
stops (no livelock).

**Decision (virtualized keyboard list, SC-010/SC-011)**: Use the WAI-ARIA listbox +
`aria-activedescendant` pattern, NOT roving tabindex. The scroll container is
`role=listbox`, `tabIndex=0`, holds DOM focus; each rendered row is `role=option` with
a STABLE `id` (`option-{taskId}`); the container's `aria-activedescendant` points at the
selected row's id. ↑/↓ update a `selectedIndex` and call the virtualizer's
`scrollToIndex()` to keep the selected row mounted. Because `scrollToIndex()` only covers
↑/↓-driven scrolling — NOT a wheel/scrollbar scroll that the user can use to push the
selection out of the rendered window — the virtualizer additionally **force-includes the
selected index in the rendered window at all times** (overscan plus an explicit
always-render-selected entry appended to the visible range), so the selected row stays a
LIVE DOM node regardless of scroll source and `aria-activedescendant` never dangles
against an unmounted option. FR-042's visible indicator is styled
on `aria-selected` (the active option), NOT on `:focus`. Virtualization via
`@tanstack/react-virtual` (windowing) to hit SC-010 (60fps) and SC-011 (<300MB) at 10k
rows. Alt+↑/↓ reorder operates on the list (the list owns it), Space toggles, E inline
rename, Del soft-delete, Esc cancel, ? help.

**Rationale**: `Dialog.tsx` already implements the four FR-101 contract properties
verified in slice 001; reusing it gives AS-07 focus-restore for free and synchronous
focus on mount is the only way to hit 16ms. Client UUIDv7 minting up front is what makes
create idempotent AND lets the optimistic row carry its final id immediately. The
TanStack `onMutate`/`onError`/`onSettled` recipe is the canonical optimistic pattern and
exactly matches SC-003's 'paint optimistic then reconcile/rollback asynchronously'; a
single list key keeps create/delete/reorder cache surgery and invalidation simple for a
personal single list. Whole-list invalidation is the idiomatic TanStack reconcile;
per-operation reapply is required because 'intent' differs (rename re-stamps text, toggle
is idempotent-toward-a-target, reorder is position-relative and invalidatable by
concurrent deletes); the single-retry cap prevents a conflict storm. The
`aria-activedescendant`/listbox pattern is the ONLY pattern that composes virtualization
× keyboard-nav × screen-reader correctly: roving tabindex calls `.focus()` on the row
element, but under virtualization the selected row unmounts when scrolled out, DOM focus
falls back to `<body>`, and arrow-key handling dies. Keeping DOM focus permanently on the
stable container makes selection survive unmount/remount. But stable container focus alone is
not sufficient: `aria-activedescendant` references the selected option BY ID, so that option
must remain a live DOM node — a wheel/scrollbar scroll that pushes the selection out of the
window (beyond what `scrollToIndex()` guards, since that only fires on ↑/↓) would unmount it
and leave `aria-activedescendant` dangling against a node the screen reader can't resolve.
Force-including the selected index in the rendered window (overscan plus an
always-render-selected entry) closes that gap so the active option is always addressable.

**Alternatives considered**: An always-mounted inline composer at the top of the list —
rejected: the spec frames capture as a discrete C-triggered surface following the dialog
contract with focus-restore (AS-07); an always-on input has no invoker to restore to. A
bespoke focus-trap — rejected: `Dialog.tsx` is proven. Per-row query keys — rejected:
create/reorder/delete mutate list membership and ordering, which a per-row cache can't
express cleanly. `useOptimistic`/React 19 form actions — rejected: doesn't cover the
async-reconcile + rollback-with-server-error path TanStack Query gives. Roving tabindex
with per-row `.focus()` — rejected (breaks on unmount — the single biggest
virtualization trap). Rendering all 10k rows without windowing — rejected
(violates SC-010/SC-011). react-window/react-virtuoso — viable but
`@tanstack/react-virtual` aligns with the existing TanStack stack and is
headless/unstyled. Single-row GET-then-merge on 409 — rejected: more cache surgery than
list invalidation for a single personal list. Unbounded auto-retry — rejected (livelock).
Silently discarding the change — rejected (violates FR-049; loses the rename text).

---

## R11: Single-Key Shortcut Suppression Inside Text Inputs (FR-031 / EC-08 / AS-09)

**Decision**: A single global keydown handler (app-shell level) first checks the event
target / `document.activeElement`: if it is an `<input>`, `<textarea>`,
`[contenteditable]`, or any element with `role=textbox`, then ONLY modifier-bearing
combos (Ctrl/Cmd/Alt chords) are dispatched as commands and all bare single keys
(C, E, Space, ?, Del, plain ↑/↓) fall through as text. Outside text inputs, the full
single-key shortcut map is active. The listbox container's own ↑/↓ handlers also defer
when a text input is focused.

**Rationale**: Centralizing the `activeElement` gate in one handler is the only reliable
way to guarantee EC-08 across every shortcut and avoids each component re-deriving 'am I
in a text field'. Checking `document.activeElement` (not just `event.target`) catches
programmatic focus and the inline-rename `E` field — a missed surface would let
Space/Del fire destructively while typing. This directly satisfies AS-09 (C/E/1 typed as
text inside the composer).

**Alternatives considered**: Per-component `stopPropagation` guards — rejected:
error-prone, easy to miss a shortcut. A hotkey library — rejected: adds a dependency for
a ~30-line gate; slice 001 added no such dep.

---

## R12: `?` Help Overlay & Focus-Neutral Announcements

**Decision**: The `?` shortcuts-help overlay REUSES `Dialog.tsx` (FR-101 contract) and
renders a static table of all slice-002 shortcuts (C, ↑/↓, Alt+↑/↓, Space, E, Esc, Del,
?). Toasts/errors (optimistic-delete rollback, `version_conflict`, network) are
announced via the existing `LiveRegion` (`role=status`, `aria-live=polite`,
`aria-atomic`) and the `Toast` component — both already polite and focus-neutral — so
they never steal focus (FR-101). A small client-side toast QUEUE/auto-dismiss layer is
added on top of the slice-001 presentational `Toast` (queueing was explicitly deferred
in 001); under a burst of conflicts/rollbacks the announcements are COALESCED so the
polite live region isn't flooded. `prefers-reduced-motion` (FR-047) gates any open/close
transition to instant/<100ms. The home page (`app/(app)/page.tsx`) replaces the static
'Nothing here yet' placeholder with the live task list + an accessible empty Inbox state
(EC-01) hinting to press `C`.

**Rationale**: Both `Dialog.tsx` and `LiveRegion`/`Toast` already exist and were built
for exactly this; the overlay and announcements are additive composition, not new
primitives. A polite live region is the correct WCAG status-message mechanism for
non-urgent server/optimistic feedback.

**Alternatives considered**: A new modal implementation — rejected (`Dialog.tsx` is
proven). An assertive live region for errors — rejected: the spec says polite, without
stealing focus; assertive interrupts the screen reader. Hover-revealed help — rejected
(FR-046 forbids hover-only content).

---

## R13: Client UUIDv7 Minting & New Web Dependencies

**Decision**: Add two new web dependencies to `apps/web/package.json`: (1)
`@tanstack/react-virtual` for list windowing (SC-010/SC-011); (2) a UUIDv7 source —
`uuid` v11 (exposes `v7()`) or a small dedicated `uuidv7` lib — wrapped behind a
one-line `apps/web/src/lib/id.ts` so the version is auditable; and (3) the
`fractional-indexing` npm lib for `generateKeyBetween(a, b)` (both bounds nullable),
with the alphabet pinned to match the server validator (R5). The id is minted
client-side at create time and sent in the `PUT /api/tasks/{id}` route. CRITICAL: the
chosen function MUST emit a true v7 (time-ordered) UUID — do NOT use
`crypto.randomUUID()` (v4) or an older `uuid` that only has v4; a v4 id silently breaks
the newest-first position seeding intent, the time-ordered idempotency story, and the
`ORDER BY position, id` tie-break (R6).

**Rationale**: Neither library is currently in `apps/web/package.json` (verified).
UUIDv7 is mandated by FR-001/ENT-01 for idempotent create + optimistic UI + newest-first
ordering; v4 would regress all three. Wrapping the minter isolates the dependency and
makes the 'must be v7' contract checkable.

**Alternatives considered**: `crypto.randomUUID()` — rejected (v4, not time-ordered).
Server-generated id — rejected: the spec mandates a client-minted id so the optimistic
row has its final id immediately and create is idempotent on retry.

---

## R14: Data Flow Through the BFF Proxy & the Generated Typed Client

**Decision**: All reads/writes go through the slice-001 BFF proxy (`/api/proxy/...`) via
the existing `openapi-fetch` `apiClient` — NO direct API calls from the browser. The
proxy already validates the session, mints the 60s carrier, and forwards verbatim
(including PUT/PATCH/DELETE — confirmed in `route.ts`); slice-002 endpoints
(`/api/tasks*`) ride the same path with ZERO proxy changes. After the API adds the task
endpoints + the `version_conflict` enum entry, regenerate the typed client
(`pnpm gen:api` → `schema.d.ts`) so Task DTOs and the extended ProblemDetails are typed
end-to-end; CI's regenerate-and-diff gate enforces no drift. Zod schemas validate the
title at the composer boundary (≤500, non-empty trimmed) and parse ProblemDetails error
bodies (including the new code).

**Rationale**: Reusing the proxy preserves deny-by-default, the HMAC carrier, and CSRF
Origin checks with no new auth code, and keeps the browser ignorant of the carrier
(Principle IX/XII). The `openapi-fetch` + generated-types stack gives compile-time
safety for the new endpoints for free once regenerated.

**Alternatives considered**: Direct browser→API calls — rejected (bypasses session
validation, carrier minting, CSRF gate — a security regression). Hand-written fetch
wrappers/types — rejected (violates Principle VI machine-generated contract; the CI diff
would fail).

---

## R15: OpenAPI Contract Mechanism & the `version_conflict` Three-Point Amendment

**Decision**: Follow the AS-BUILT slice-001 pattern, NOT ADR-0009 Decision 6's composed
multi-file vision (which was never implemented). Concretely: (1) a checked-in
`specs/002-task-capture/contracts/openapi.yaml` mirroring the slice-001 file (its own
paths + a `Task`/`TaskResponse` component schema + request DTOs, referencing the shared
ProblemDetails), and (2) extend `TaskFlowDocumentTransformer` to add the `Task` component
schema, the task operationIds (createTask/listTasks/renameTask/setTaskDone/reorderTask/
deleteTask), and documented 404/409/422 responses referencing ProblemDetails on the
task ops (today `SetOperation` attaches only 401) — mirroring the existing
`SetOperation`/`BuildProblemDetailsSchema` style. (No **403** is documented on any task
op this slice — see R17; `403 forbidden` is reserved for the slice-007+ shared-project
role case.)

Add the NEW additive error code `version_conflict` (HTTP 409) — and the new
`VersionConflictException`→409 and `NotFoundException`→404 mappings — in lockstep across
THREE sync points plus the middleware:
1. `ProblemDetailsMiddleware.Map` (`apps/api/.../Middleware/ProblemDetailsMiddleware.cs`)
   — today maps ONLY `UnauthenticatedException`→401, `ForbiddenException`→403,
   `ValidationException`→422, `_`→500; ADD `NotFoundException`→404 `not_found` and
   `VersionConflictException`→409 `version_conflict`.
2. `TaskFlowDocumentTransformer.ErrorCodes[]`
   (`apps/api/.../OpenApi/TaskFlowDocumentTransformer.cs`) — add `version_conflict`.
3. The curated `specs/002-task-capture/contracts/openapi.yaml` errorCode enum (and the
   central ProblemDetails schema).
4. The web `ERROR_UX` map (`apps/web/src/lib/api/client.ts`). The regen-and-diff CI gate
   lands `version_conflict` in the generated `errorCode` union — so any web *reference* to
   it is type-checked — and `ERROR_UX` exhaustiveness over that union is enforced **at
   compile time** by typing the map `satisfies Record<ErrorCode, ErrorUx>` (`ErrorCode`
   derived from the generated `ProblemDetails.errorCode` union), so `tsc` — the existing
   TS-strict CI type-check — fails if any code is unmapped (no bespoke runtime test needed).

**`TaskResponse` schema** (lean — REQUIRED to round-trip the version token, NEVER expose
`deleted_at` or any reserved forward-compat column): `id` (uuid), `title` (string ≤500),
`status` (full FR-003 enum), `position` (string — the lexicographic rank), `version`
(int), `createdAt`, `updatedAt`, `completedAt` (nullable). `createdBy` MAY be omitted
(always the caller). Request DTOs: `CreateTaskRequest{title, position}`,
`RenameTaskRequest{title, version}`, `SetTaskStatusRequest{status, version}`,
`ReorderTaskRequest{position, version}`; no body on DELETE/GET. `version` is REQUIRED in
every mutation request body EXCEPT DELETE (R4).

**Rationale**: ADR-0009 Decision 6 describes an assembled/composed document that does NOT
exist in the repo; the real mechanism is the C# transformer + per-slice checked-in YAML.
Mirroring the as-built pattern keeps the contracts author from designing against an
absent build step and preserves the working CI diff gate. The `version` token MUST appear
in the DTO and round-trip through the typed client; if it's omitted, the client can't
send it and the 409 can never be exercised. Including `version_conflict` here is correct
because slice 002 is the FR-093 owner-slice; ADR-0009 blesses additive codes.

**Alternatives considered**: Implementing the ADR-0009 composition build step now —
rejected (out of scope, large; the transformer + checked-in YAML already satisfy the
one-canonical-document + CI-diff guarantees). Hand-editing the generated client —
rejected (violates Constitution VI / the regenerate-and-diff gate). Reusing
`conflict_lww` for the 409 — rejected (R4: wrong semantics + steals the slice-014 code).

---

## R16: Title Handling — Verbatim Store, Escaped Render, Boundary Validation (FR-099)

**Decision**: The title is plaintext, stored VERBATIM (UTF-8) and rendered ESCAPED
(React default; never `dangerouslySetInnerHTML`). No HTML sanitization this slice
(slice 009 owns that). Validation is non-empty-after-trim + ≤500 chars, enforced at BOTH
boundaries: Zod on the client composer (`trim → min 1 → max 500`) and FluentValidation on
the server. Slice 001 references the FluentValidation package but wires NO validator and
NO Wolverine FluentValidation middleware (only `ProblemDetailsMiddleware` maps a thrown
`ValidationException`→422). Slice 002 therefore adds NEW infrastructure: wire
`WolverineFx.FluentValidation` (`opts.UseFluentValidation()`) and add
`CreateTaskValidator`/`RenameTaskValidator`, so a title violation throws
`ValidationException`→**422 `validation_failed`** with a field-level errors map. The
aggregate keeps an `ArgumentException` backstop (R1).

**Rationale**: FR-099 mandates verbatim store + escaped render; React's default escaping
satisfies output-encoding (Principle XII) with no extra work. FR-093 lists
title-over-length under `validation_failed`, which the existing middleware maps to 422 —
so 422 (not 400) is the intended status for the title rule, confirmed against the
as-built middleware. Wiring `UseFluentValidation` is the minimal new infrastructure to
turn the title rule into a structured 422 rather than an opaque 500 from the aggregate
backstop.

**Alternatives considered**: Validating only in the aggregate (throw `ArgumentException`)
— rejected: it would surface as a 500 without a field-level errors map; FluentValidation
gives the structured 422 FR-093 wants. HTML-sanitizing the title now — rejected (slice
009 owns sanitization; verbatim-store + escaped-render is the correct day-one posture).

---

## R17: Not-Found vs Forbidden Posture (Existence-Disclosure Guard)

**Decision**: For single-resource reads/writes, a foreign / absent / soft-deleted
(tombstoned) id returns **404 `not_found`** — NOT 403 — to avoid an enumeration oracle.
Every WRITE handler must resolve such a row to 404 **BEFORE** `RequireOwnership` could
throw 403 (load-scoped-to-not-found, or remap the ownership branch's `ForbiddenException`
to `NotFoundException`), so this slice emits no 403 (consistent with R9). **Carve-out —
DELETE own-tombstone:** a caller re-deleting their OWN already-soft-deleted task is the
idempotent **204** no-op (per DELETE's version-free idempotency, R4), NOT 404 — the 404
posture applies to foreign/absent ids and to non-DELETE ops. 403 (`forbidden`, via
`RequireOwnership`) is reserved for a future shared-project insufficient-role case
(slice 007+) that does not exist in this slice. The deny tests assert the exact code per
path: another user's LIVE task and a foreign absent/tombstoned id → 404; a re-DELETE of
the caller's own tombstone → 204; no-JWT → 401. `GetMyTasks` simply excludes non-owned
and soft-deleted rows (never leaks them).

**Rationale**: This mirrors slice-001's posture of never confirming the existence of
resources the caller can't see, and avoids an id-enumeration oracle. It is a deliberate
divergence from a naive reading of FR-093 (which lists '403 for a task that is not the
caller's createdBy'); the plan/contracts/spec state explicitly which paths return 404 (and
that 403 is not emitted this slice) so the deny tests assert the right code. Adopted and
encoded in the spec (Clarifications): single-item ops on a foreign/absent/soft-deleted id →
404; a re-DELETE of the caller's own tombstone → 204; 403 reserved for the future
shared-project role case (slice 007+).

**Alternatives considered**: Returning 403 for a foreign id — rejected: leaks that the id
exists. Distinguishing 404 (absent) from 403 (foreign live) on single-item ops — rejected
for this slice: the foreign-live case is also treated as not-found to avoid the oracle,
since there is no shared-resource path where a caller legitimately knows of a resource
they may not act on.

---

## R18: Reorder-Chord Binding — `Alt+↑/↓` FROZEN (FR-045 / FR-102, resolves T003)

**Decision (FROZEN — was the PROVISIONAL T003 blocker):** the manual-reorder shortcut is
**`Alt+↑` / `Alt+↓`** (`Alt+ArrowUp` / `Alt+ArrowDown`), the proposed default — now
**verified SAFE** against the target browser + screen-reader matrix (Chrome, Firefox, Edge,
Safari on Windows + macOS; NVDA, JAWS, VoiceOver). No alternative chord is needed. The
plan's/spec's concern conflated `Alt+Arrow` with `Alt+Left/Right` (browser back/forward);
that binding is **horizontal only** — the **vertical** `Alt+Up/Down` pair is free of it on
every target browser (Safari history is `Cmd+[`/`Cmd+]`, not an Option chord). This binding
is consumed by the shortcut gate (T054), the `?` help overlay (T056), and the reorder wiring
(T058), which are now unblocked.

**Rationale:** the most authoritative precedent possible endorses this exact choice — the
**W3C ARIA Authoring Practices Guide "Scrollable Listbox with Rearrangeable Options" example
uses `Alt+↑`/`Alt+↓` to move options in a `role=listbox` driven by `aria-activedescendant`**
— identical to this slice's widget (R10). **VS Code / Monaco** ships the same chord for "Move
Line Up/Down" (`Option+↑/↓` on macOS) and it works in-browser across the matrix. Screen
readers do not intercept it for a plain listbox: a `role=listbox` forces NVDA/JAWS into
**focus/forms mode**, which passes keystrokes through to the app; NVDA's only `Alt+Up/Down`
defaults are Word/Outlook-scoped (not web); VoiceOver commands are `Ctrl+Option`-prefixed, so
bare `Option+Arrow` is not a VO command. **WCAG 2.1.4 (Character Key Shortcuts) does not apply**
— it governs single-character shortcuts; a modifier chord is exempt — so there is no
remap/disable obligation.

**Load-bearing mitigations (MUST hold — the SAFE verdict is conditional on these; T054/T058
implement them):**
1. The reorder keydown handler MUST call **`preventDefault()`** while the listbox has focus —
   on **Safari**, bare `Option+↑/↓` page-scrolls, so suppressing the native default is
   load-bearing, not hygiene.
2. The chord fires **only while the `role=listbox` (not a text input) has focus** — already
   guaranteed by the R11 `activeElement` gate (T054). This avoids macOS `Option+↑/↓` paragraph
   navigation (text-field-only) entirely.
3. Keep the widget a **plain `role=listbox`**, NOT nested in a `role=combobox` / native
   `<select>` — JAWS binds `Alt+Down`/`Alt+Up` to open/close a combobox, and browsers toggle a
   native `<select>` dropdown on `Alt+↓`. Neither applies to a bare listbox; do not reintroduce
   a combobox role on this list.
4. Expose **`aria-keyshortcuts="Alt+ArrowUp Alt+ArrowDown"`** on the option/listbox (as the APG
   example does) for screen-reader discoverability, complementing the `?` overlay (T056).

**Alternatives considered (NOT adopted — Alt+↑/↓ is APG-canonical):** `Ctrl+Shift+↑/↓` —
collision-free but verbose; a **grab/drop mode** (Space-to-lift, arrows-to-move,
Space/Enter-to-drop, Esc-cancel) — also APG-blessed but adds a mode + indicator and would
collide with this slice's `Space`=toggle-done. Avoid bare `Ctrl+↑/↓` (macOS Mission
Control/Spaces) and `Alt+←/→` (browser back/forward).

**Sources:** W3C ARIA APG rearrangeable-listbox example
(https://www.w3.org/WAI/ARIA/apg/patterns/listbox/examples/listbox-rearrangeable/) and listbox
pattern (https://www.w3.org/WAI/ARIA/apg/patterns/listbox/); NVDA commands quick reference
(https://download.nvaccess.org/releases/2024.4.1/documentation/keyCommands.html); Deque/WebAIM
JAWS shortcuts (https://dequeuniversity.com/screenreaders/jaws-keyboard-shortcuts,
https://webaim.org/resources/shortcuts/jaws); Apple VoiceOver commands
(https://support.apple.com/guide/voiceover/general-commands-cpvokys01/mac) and Safari shortcuts
(https://support.apple.com/guide/safari/keyboard-and-other-shortcuts-cpsh003/mac); VS Code Move
Line Up/Down (https://code.visualstudio.com/docs/getstarted/tips-and-tricks); MDN `<select>`
(https://developer.mozilla.org/en-US/docs/Web/HTML/Element/select).

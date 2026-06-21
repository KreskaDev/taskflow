# Data Model: Task Capture (002)

## Entities

### Task (ENT-01) — Aggregate Root

The core work item, and the first aggregate in the **Task Management** bounded context
(ADR-0003). State-stored via EF Core (ADR-0003 Decision 5 — no event store), authorized
deny-by-default and scoped to the caller (Decision 6). This slice persists the columns
below; the remaining ENT-01 attributes are reserved as nullable, forward-compatible
columns (populated by their owning slices) so no later enum / soft-delete / ordering /
concurrency migration is needed.

| Field | Type | Constraints | Notes |
|---|---|---|---|
| `id` | `uuid` (UUIDv7) | PK | **Client-generated** (FR-001), `ValueGeneratedNever`. Strongly-typed `TaskId(Guid)`; the create is insert-if-not-exists by this id (idempotent retry). Unlike slice-001's server-minted `UserId`, the server never calls a `New()` factory — the id arrives in the create payload. |
| `title` | `text` | NOT NULL | Non-empty after trim, ≤ 500 chars (FR-001). Enforced at the boundary (FluentValidation server, Zod client) **and** an `ArgumentException` backstop in the aggregate factory — **not** as a DB CHECK constraint. Stored verbatim UTF-8; rendered escaped (FR-099). |
| `status` | `text` | NOT NULL, DEFAULT `'backlog'` | Full FR-003 enum stored from day one: `backlog \| todo \| in_progress \| done \| cancelled`. Mapped `HasConversion<string>()`. Only `backlog`/`done` are reachable in this slice (the `Space` toggle). |
| `created_by` | `uuid` | FK → `users(id)` **ON DELETE CASCADE**, NOT NULL | The creating `User` (FR-002). **Immutable** (set in ctor, never reassigned). The ownership key the dispatch-by-visibility authorization (FR-065) resolves to. Mapped as a `UserId` conversion. Provenance that doubles as the ownership key in the personal branch. **`ON DELETE CASCADE`** so that when slice-001 hard-deletes the `User` row, the user's personal tasks are erased with it — for personal/never-shared tasks, cascade-delete IS complete erasure (Constitution XI; reattribution to `UserId.Tombstone` is reserved for SHARED content in slice 007+). |
| `position` | `varchar COLLATE "C"` | NOT NULL | Lexicographic fractional rank string (see **Reorder / `position` strategy**). Sole `ORDER BY` key for the list (ascending, top-is-lowest). Client-authoritative on create; the server validates format and is the sole writer under the version guard. **No** unique constraint. |
| `version` | `integer` | NOT NULL, DEFAULT `0` | Optimistic-concurrency token (`IsConcurrencyToken`), incremented by the aggregate on every mutating behavior method. Echoed in the read model and on every mutation request; a stale value → `409 version_conflict` (see **Optimistic concurrency**). |
| `created_at` | `timestamptz` | NOT NULL | UTC (FR-004, Constitution X). |
| `updated_at` | `timestamptz` | NOT NULL | UTC; stamped by every behavior method (FR-004). |
| `completed_at` | `timestamptz` | NULL | UTC. Set iff `status = done`; cleared on un-complete (FR-003/FR-004). |
| `deleted_at` | `timestamptz` | NULL | UTC soft-delete tombstone (FR-097). Set by `Del`; the row is then excluded from every authorization-scoped query and reaped after 30 s. **Never exposed in the read model.** |

**Reserved forward-compatible columns** (mapped, nullable, **unused this slice** — ENT-01):
these are the **scalar nullables only**, kept so their owning slices need no schema
migration. They are **not** read or written by any slice-002 handler and are **not**
surfaced in the read model.

| Field | Type | Owning slice |
|---|---|---|
| `description` | `text` NULL | 005 (full editor) |
| `priority` | `text` NULL | 005 (P0–P3) |
| `due_date` | `timestamptz` NULL | 003 / 005 |
| `due_has_time` | `boolean` NULL | 003 / 005 — the `DueDate.has_time` flag (ADR-0003 Decision 8) |
| `project_id` | `uuid` NULL | 004 |
| `cycle_id` | `uuid` NULL | 011 |
| `recurrence_rule` | `jsonb` NULL | 012 |

> **Not added now:** `labels` and `assignees` are **join tables** owned by later slices
> (006 labels, 008 assignees), and `RecurrenceRule` is mapped here only as a scalar `jsonb`
> placeholder — **no navigation properties** are introduced in this slice.

**Indexes**:
- `ix_tasks_created_by_position` — composite on `(created_by, position)`, **partial**
  `WHERE deleted_at IS NULL`, with `position` under `COLLATE "C"`. Serves the single hot
  query exactly: `WHERE created_by = @caller AND deleted_at IS NULL ORDER BY position, id`
  as one index range scan (`created_by` leads → ownership-scoped; `position` second →
  `ORDER BY` satisfied; partial → tombstones excluded from the index and kept small).
  Optionally `(created_by, position, id)` to make the `, id` tie-break index-only.
- The PK on `id` is also the **idempotent-create backstop**: a concurrent double-insert of
  the same client id surfaces as a unique-violation that the handler re-resolves through
  the same find-then-decide path.
- **No** unique constraint on `position` (nor on `(created_by, position)`) — see the
  tie-break rule below.

**Domain rules** (enforced inside the aggregate unless noted; mirrors ADR-0003 Invariants):
- `status` defaults to `backlog` on create (FR-003).
- `completed_at` is set **iff** `status = done`: `MarkDone(utcNow)` sets `status = done` +
  stamps `completed_at`; `MarkBacklog()` (un-complete) sets `status = backlog` + clears
  `completed_at` (the done ↔ backlog toggle, FR-003).
- `created_by` is **immutable** (set in the ctor, never reassigned).
- Every mutating method stamps `updated_at` and increments `version`.
- `SoftDelete(utcNow)` sets `deleted_at` and is **idempotent** (a second call on an
  already-tombstoned row is a guarded no-op).
- Title is trimmed-non-empty and ≤ 500 at the boundary (FluentValidation / Zod) **and**
  guarded by an `ArgumentException` backstop in `Create` / `Rename` (mirrors
  `User.Create`'s `ThrowIfNullOrWhiteSpace`).
- Authorization is **not** in the aggregate (ADR-0003 Decision 6 — "does not live in the
  aggregates"); it lives in the application-layer handlers (see **Authorization scoping**).

**Aggregate shape** (mirrors the slice-001 `User` house style — private EF materialization
ctor + private ctor + static factory + `utcNow`-injected behavior methods, on
`AggregateRoot<TaskId>`):
- `static Create(TaskId id, UserId createdBy, string title, string position, DateTime utcNow)`
  — id and position are client-supplied; status defaults to `backlog`; `version` starts at 0.
- `Rename(string title, DateTime utcNow)`, `MarkDone(DateTime utcNow)`,
  `MarkBacklog(DateTime utcNow)`, `Reorder(string position, DateTime utcNow)`,
  `SoftDelete(DateTime utcNow)` — each stamps `updated_at` and bumps `version`.
- `TaskId(Guid Value)` is a strongly-typed id mirroring `UserId`, but **without** a `New()`
  factory used by the server (the id is client-supplied). `From(Guid)` is provided for
  rehydration from the route / database.

### Optimistic concurrency — the `version` token

- `version` is a mapped `int` configured `.IsConcurrencyToken()` and **incremented by the
  aggregate** on every mutating behavior method (not Postgres `xmin` — that would couple the
  wire contract to a provider-internal `uint` system column and be unseedable in tests).
- Every mutating command (rename, toggle-done, reorder) carries the client's last-seen
  `version`. The handler loads the row, asserts ownership, **compares** `row.Version` to the
  command's `version` (mismatch → throws `VersionConflictException`), applies the change,
  increments `version`, and saves. EF's `DbUpdateConcurrencyException` is the **backstop**
  for a true interleaved race between load and save.
- Both map to **HTTP 409** with the error code **`version_conflict`** (a NEW additive code —
  **not** `conflict_lww`, which means a last-write-wins overwrite *succeeded*; this slice
  *rejects* the stale write). The client refetches the row and reapplies the user's intent.
- `version` is **required** in every mutation request body **except DELETE** (soft-delete is
  version-free / idempotent — removing a row whose content changed underneath is safe, so a
  stale-delete 409 buys nothing).

> **Cross-artifact sync:** `version_conflict` (409) must be added in lockstep to the three
> sync points — `ProblemDetailsMiddleware.Map`, `TaskFlowDocumentTransformer.ErrorCodes`,
> and `contracts/openapi.yaml` — or the CI regenerate-and-diff gate fails. A new
> `VersionConflictException → 409` and `NotFoundException → 404` mapping must be added to
> `ProblemDetailsMiddleware.Map` (today it maps only 401/403/422/500). The same regen-and-diff
> gate lands `version_conflict` in the generated web `errorCode` union (so any reference to it
> is type-checked); `ERROR_UX` exhaustiveness over that union is enforced **at compile time** by
> typing the map `satisfies Record<ErrorCode, ErrorUx>` (`ErrorCode` derived from the generated
> `ProblemDetails.errorCode` union), so `tsc` — the existing TS-strict CI type-check — fails if
> any code is unmapped (no bespoke runtime test needed).

### Reorder / `position` strategy (FR-102)

- **Type: a lexicographic fractional rank string** (`varchar COLLATE "C" NOT NULL`), the
  standard `fractional-indexing` / LexoRank scheme — a short variable-length ASCII string
  compared byte-ordinally. A create, a reorder, and the newest-first seed all reduce to
  computing a rank `between(left, right)` (either neighbour may be `null`) and writing
  **only** the one moved/created row (O(1) writes — no whole-list renumber).

  > **Why not `double` precision + midpoint?** Rejected on a **slice-specific** ground, not
  > scaling. Under this slice's per-row `version` token, a `double`'s precision-exhaustion
  > fallback **renumbers N sibling rows**, bumping N versions → a 409 conflict storm for any
  > concurrent reader/writer of those rows. A rank string has **no exhaustion ceiling** (it
  > grows a character instead of renumbering), so that fallback path — and its conflict
  > storm — never exists. The per-row version token is a clarified hard requirement, so it
  > settles the type choice decisively. (Plain-integer-with-gaps is rejected for the same
  > reason; a linked list is rejected because ordered reads would need a recursive CTE
  > instead of one indexed `ORDER BY`.)

- **Collation — the `position` column MUST be `COLLATE "C"`, and every index / `ORDER BY`
  over it MUST resolve under `"C"`.** EF Core: `.UseCollation("C")` on the `position`
  property. **If the column collation is missed, ordering drifts silently**: Postgres' default
  locale-aware collation does not sort byte-ordinally, so the client (TS code-unit order)
  paints the correct order while the server's `ORDER BY position` returns a *different* order,
  and the two drift. **A serving index over a C-collated column needs NO explicit `COLLATE
  "C"` token of its own — a Postgres index inherits the column's collation**, so
  `ix_tasks_created_by_position` on the C-collated `position` is itself C-ordered and the
  planner uses it for `ORDER BY position`. *(Empirically verified on Postgres 17, 2026-06-21:
  with the DB default `en_US.utf8`, `ORDER BY position` over the C-collated column returns
  ASCII byte order and `EXPLAIN` shows an `Index Only Scan` on `ix_tasks_created_by_position`
  — i.e. the index serves the C-ordered query. An explicit index collation would be redundant
  and Npgsql does not cleanly express per-column collation on a composite-index builder.)* The
  migration review verifies the **column** carries `COLLATE "C"`; the index correctness
  follows from that.

- **Alphabet — pinned identically on client and server.** The standard fractional-indexing
  alphabet (digits + lowercase letters) so byte order and intended order coincide exactly
  under `"C"`. The client generates ranks (`generateKeyBetween` from the `fractional-indexing`
  npm package); the server is a **format-validator only** (alphabet + parseable), **not a
  generator** — this avoids any TS ↔ C# byte-identical-output obligation.

- **Ordering direction — pinned once: ascending rank, top-is-lowest.**
  - Newest-first seed (FR-102, consistent with FR-021): a new task prepends, so
    `position = between(null, head.position)` for a non-empty list, and
    `between(null, null)` (a mid-alphabet seed) for the empty-list first task.
  - Manual reorder (`Alt+↑/↓`): moving down past neighbour X = `between(X, Xnext)`
    (`Xnext` may be `null` at the tail); moving up past Y = `between(Yprev, Y)` (`Yprev` may
    be `null` at the head).
  - Canonical query order is **`ORDER BY position, id`** where `id` is the UUIDv7.

- **Equal-rank tie-break by `id` (no unique constraint).** Two concurrent inserts/reorders
  targeting the same neighbour pair can legitimately compute **equal** ranks; that is benign
  and resolves deterministically via the `id` tie-break, lazily smoothed on the next reorder
  of either row. This holds because **canonical-hex UUIDv7 string order matches Postgres
  `uuid` byte order**, so TS and Postgres agree on the tie-break. A unique constraint would
  turn a harmless collision into a spurious failure — so it is omitted. (Do **not** conflate
  a rank collision with a version conflict: a 409 is reserved strictly for a stale `version`
  on the moved row.)

- **Reorder is a normal optimistic write** under the same `version` rule: the row's `version`
  is echoed; a stale version → `409 version_conflict`; on conflict the client refetches and
  reapplies **intent-based** (recompute `between()` against the *fresh* neighbour ranks for
  "place after X" / "before Y" — never re-send the stale-derived rank).

- **Reserved (not implemented this slice):** a periodic rebalance/renumber path. Newest-first
  means every create prepends (`between(null, head)`) — the worst case for fractional-key
  length growth — so head keys lengthen over a long-lived list. Bounded and fine at this
  scale (~10 users, per-user lists); named here as a monitored property so it isn't mistaken
  for a bug later.

### Authorization scoping (deny-by-default, ownership branch)

Authorization is dispatched by visibility (FR-065); every task here is personal/unprojected,
so the dispatch resolves to the **ownership branch** (`created_by = caller`). There is no
shared-project path in this slice (FR-066/FR-067 not exercised).

- **Queries** are scoped in the query itself:
  `WHERE created_by = currentUser.Id AND deleted_at IS NULL ORDER BY position, id`. There is
  no code path that returns a (non-deleted) task belonging to another user, and soft-deleted
  tombstones are excluded from day one (FR-097).
- **Writes** must resolve a foreign / absent / soft-deleted row to **`404 not_found`
  BEFORE** `RequireOwnership` could throw 403 — load-scoped-to-not-found (or remap the
  ownership branch's `ForbiddenException` to `NotFoundException`) — so no task op emits 403
  this slice. **Not-found vs forbidden posture** (avoids an enumeration oracle, mirrors
  slice-001 never confirming the existence of resources the caller can't see):
  - A foreign / absent / soft-deleted (tombstoned) id on a single-item op → **`404
    not_found`** (NOT 403), so the id space is not an oracle.
  - **Carve-out:** a caller re-deleting their OWN already-soft-deleted task is the idempotent
    **`204`** no-op (per DELETE's version-free idempotency), NOT 404 — the 404 posture applies
    to foreign/absent ids and to non-DELETE ops.
  - **DELETE load is owner-scoped but TOMBSTONE-INCLUSIVE** (the deliberate exception to the
    generic `deleted_at IS NULL` filter): the generic owner-scoped + non-deleted load
    (`WHERE created_by = caller AND deleted_at IS NULL → 404 if not found`) governs the
    non-DELETE writes only. DELETE MUST load owner-scoped WITHOUT the `deleted_at IS NULL`
    predicate so it can distinguish an own-tombstone (→ 204 no-op) from a foreign/absent id
    (→ 404); applying the non-deleted filter to DELETE would make the own-tombstone unfindable
    and wrongly return 404 instead of the promised 204. Foreign/absent on DELETE still → 404.
  - **`403 forbidden`** is reserved for a future *owned-but-otherwise-invalid* / shared-project
    insufficient-role case (slice 007+) — not reachable in this slice.
- **Read-model leak rule:** the read model exposes only
  `{ id, title, status, position, version, createdAt, updatedAt, completedAt }`. It **never**
  includes `deleted_at` (soft-deleted rows are never returned) **nor** any reserved
  forward-compatible column.
- Every command/query handler ships **both** an allow test (the caller acts on their own
  task) and a deny test (a different user's task → 404, a soft-deleted task → excluded/404,
  no-JWT → 401), plus a stale-`version` → 409 test on each mutating handler and an
  idempotent-create-replay test on create (SC-013/SC-016).

## State Transitions

### Task Lifecycle (status)

```
[Non-existent] --CreateTask (insert-if-not-exists by client id)--> [backlog]
[backlog]      --CreateTask (same id + owner + title)----------->  [backlog]   (idempotent replay, no change)
[backlog]      --MarkDone (Space)--------------------------------> [done]      (completed_at = now)
[done]         --MarkBacklog (Space)-----------------------------> [backlog]   (completed_at = null)
[backlog|done] --Rename (E)--------------------------------------> (same status, title replaced)
[backlog|done] --Reorder (Alt+↑/↓)-------------------------------> (same status, position changed)
```

(`todo`, `in_progress`, `cancelled` are storable but unreachable in this slice.)

### Task Soft-Delete Lifecycle (tombstone + 30 s reaper)

```
[live]                 --DeleteTask (Del)----------------------> [tombstoned]  (deleted_at = now; excluded from all authz-scoped queries)
[tombstoned]           --DeleteTask (Del, again)---------------> [tombstoned]  (idempotent no-op)
[tombstoned]           --ReapDeletedTask (scheduled +30s)------> [hard-deleted] (row removed)  IFF deleted_at still set & unchanged
[tombstoned]           --(slice 014 Restore — out of scope)----> [live]        (deleted_at cleared; the reaper then skips it)
```

**Soft-delete + reaper mechanics:**
- `DeleteTask` sets `deleted_at`, bumps `version`, saves, **and** publishes a Wolverine
  scheduled message `ReapDeletedTask(TaskId, DeletedAtInstant)` delayed 30 s, enrolled in the
  same transaction via the outbox (mirrors slice-001's `AccountDeletionRequested` +
  `AutoApplyTransactions` pattern; register `opts.PublishMessage<ReapDeletedTask>()
  .ToLocalQueue("task-reaper")`, `DurabilityMode.Solo`).
- The `ReapDeletedTaskHandler` is **idempotent and restore-aware**: it loads the row and
  hard-deletes **only if** the row still exists **and** `deleted_at` is non-null **and**
  unchanged from the scheduled instant. If the row is already gone, or `deleted_at` was
  cleared (a slice-014 restore won the race), it **no-ops**. A blind hard-delete would create
  a slice-014 retrofit conflict — so the restore-aware guard is required forward-compat.
- The reaper handler runs off the durable queue with **no `HttpContext`** (so
  `ICurrentUser.IsAuthenticated` is false); it MUST be added to the `AuthorizationMiddleware`
  exclusion predicate alongside `AccountDeletionRequested`, or it dead-letters.
- The reaper is **infrastructure, not a REST endpoint** — it is off the API surface.
- The user-facing 30-second undo **toast/restore UX is OUT of scope** (slice 014); only the
  `deleted_at` tombstone + reaper ship here, making slice 014 a pure retrofit.

## Migration Plan

### EF Core Migration (.NET API)

A new migration `AddTasks` creates the `tasks` table — the schema source of truth
(Constitution VI). Mirror `UserConfiguration`:

- `TaskConfiguration : IEntityTypeConfiguration<Task>`, `builder.ToTable("tasks")`.
- `id` → `HasConversion(id => id.Value, v => TaskId.From(v))`, `ValueGeneratedNever()`.
- `status` → `HasConversion<string>()`, `HasDefaultValue` `'backlog'`.
- `created_by` → `UserId` conversion; FK to `users(id)` with **`ON DELETE CASCADE`**
  (`.OnDelete(DeleteBehavior.Cascade)` — NOT the EF default `Restrict`), so slice-001's
  hard-delete of the `User` row erases the user's personal tasks atomically. For
  personal/never-shared tasks, cascade-delete IS complete erasure (Constitution XI);
  reattribution to `UserId.Tombstone` is reserved for SHARED content in slice 007+.
- `version` → `.IsConcurrencyToken()`.
- `position` → `.UseCollation("C")`; the composite index built under the **same** collation.
- All four temporal columns → `HasColumnType("timestamp with time zone")`,
  `DateTimeKind.Utc`.
- The seven reserved scalar nullable columns mapped but unused.
- `builder.Ignore(t => t.DomainEvents)` (like `UserConfiguration`).
- `ix_tasks_created_by_position` on `(created_by, position)` **partial**
  `.HasFilter("deleted_at IS NULL")`, `position` under `COLLATE "C"`.
- **Migration review checklist:** both the `position` column **and** its index carry
  `COLLATE "C"`; there is **no** unique constraint on `position`; `version` is the
  concurrency token; the partial index filter is `deleted_at IS NULL`; the
  `created_by → users(id)` FK is **`ON DELETE CASCADE`** (NOT default RESTRICT).

> **Account-deletion cascade:** with the FK at `ON DELETE CASCADE`, slice-001's
> `AccountDeletionRequestedHandler` **stays a deliberate no-op this slice** — the FK performs
> task erasure directly, so no coordinator change is needed here (reattribution to
> `UserId.Tombstone` enters only when SHARED content exists, slice 007+). A regression test
> (`AccountDeletionCascadeTests`, or an extension of slice-001's `DeleteAccountTests`) MUST
> assert that deleting an account that owns tasks succeeds AND the tasks are gone.

Add `public DbSet<Task> Tasks => Set<Task>();` to `AppDbContext` (configurations are picked up
via the existing `ApplyConfigurationsFromAssembly`).

### Seams to add (mirroring slice-001)

- `ITaskRepository` (`TaskFlow.Application/TaskManagement`) + `TaskRepository`
  (`TaskFlow.Infrastructure/Persistence`) over `AppDbContext`, mirroring
  `IUserRepository`/`UserRepository`; register `AddScoped<ITaskRepository, TaskRepository>()`.
- A new `VersionConflictException` (and `NotFoundException` if not already present) mapped in
  `ProblemDetailsMiddleware.Map` → 409 `version_conflict` / 404 `not_found`.
- `FluentValidation` validators (`CreateTaskValidator` / `RenameTaskValidator`: title
  trimmed-non-empty, ≤ 500) + wire `WolverineFx.FluentValidation` (`opts.UseFluentValidation()`,
  **new** — not present in slice 001) so a violation throws `ValidationException` → 422
  `validation_failed` via the existing middleware.

### Backup before migration (FR-051)

The auto-backup hook runs before the migration; at v1 it is effectively a no-op for this
table (no prior `tasks` schema to migrate). The hook and restore path are in place per the
slice-001 foundation.

# Data Model: Project Management (slice 004)

**Input**: `spec.md`, `research.md` (R1–R16), constitution v4.0.0, and the slice-002 schema (the `tasks` table + its seven reserved forward-compatible columns, including `project_id`).

This slice **introduces one new entity** (`Project`, ENT-02) with a new table and one EF migration (`AddProjects`), and **activates one reserved column** on the existing `tasks` table (`project_id`, by adding its FK constraint). It is the first schema change since slice 002.

---

## 1. Entities

### ENT-02 — Project (Aggregate Root) — NEW

An organizational container for tasks, owned by a user, with one level of nesting, a preset color/icon, an archive state, and a personal-visibility baseline.

| Field | Type | Constraints | Notes |
|---|---|---|---|
| `Id` | `ProjectId` (uuid) | PK; client-generated UUIDv7; `ValueGeneratedNever` | Strongly-typed wrapper, mirrors `TaskId` |
| `OwnerId` | `UserId` (uuid) | **REQUIRED**; immutable; FK → `users(id)` **ON DELETE CASCADE** | Authorization anchor (R13); erasure parity with `tasks.created_by` (Constitution XI) |
| `Name` | `string` | non-empty, trimmed, ≤ 200 chars | User-authored; output-encoded on render (FR-099, Principle XII) |
| `Color` | `string` | member of the preset color set (R10, ASM-04) | Validated both tiers; not free-form |
| `Icon` | `string` | member of the preset icon set (R10, ASM-04) | Validated both tiers; not free-form |
| `ParentId` | `ProjectId?` (uuid) | nullable; FK → `projects(id)` **ON DELETE SET NULL** (self-ref); one-level rule (R3) | null = top-level; non-null = child of a **top-level** project |
| `Visibility` | `string` | `personal` (default) \| `shared`; **only `personal` realized this slice** | R11; `shared` value + membership = slice 007 |
| `ArchivedAt` | `DateTime?` | nullable; UTC | R2; null = active, non-null = archived (reversible state) |
| `CreatedAt` | `DateTime` | UTC; set at create | Constitution X |
| `UpdatedAt` | `DateTime` | UTC; stamped by every mutation | |
| `Version` | `int` | starts at 0; `.IsConcurrencyToken()`; bumped by every mutation | Optimistic concurrency → 409 `version_conflict` |
| `DeletedAt` | `DateTime?` | nullable; UTC | R5/R14; soft-delete tombstone, excluded from all authz-scoped queries; reaped after the undo window |

**Table**: `projects`. **Column naming**: snake_case (`owner_id`, `parent_id`, `archived_at`, `deleted_at`, …), matching `tasks`.

**Indexes**:
- `ix_projects_owner_id` — on `(owner_id)`, **partial** `WHERE deleted_at IS NULL`. Serves the sidebar query (owner-scoped, tombstone-excluded) and the archived listing (R8); `archived_at` is filtered in-query, not in the index.
- (Optional, deferred) `(owner_id, parent_id)` — only if tree assembly profiling warrants; not added pre-emptively (YAGNI).

**Foreign keys**:
- `owner_id → users(id)` **ON DELETE CASCADE** — a user's account deletion erases their projects atomically (Constitution XI; same posture as `tasks.created_by`).
- `parent_id → projects(id)` **ON DELETE SET NULL** (self-referential) — defensive backstop under soft-delete; dispositions (R5) reconcile children *before* the reaper hard-deletes.

**Domain rules / invariants** (held by the aggregate + application layer):
- **One-level nesting (FR-012, R3)** — enforced in the command handler (needs repository lookups): a project may set `ParentId` only to a project that is itself top-level (`parent.ParentId IS NULL`), and only if the project being parented has **no children**. Violations → 422 `validation_failed` on `parentId` (R12).
- **Owner coercion (R13)** — `OwnerId` is set from `ICurrentUser.Id`, never from the wire; immutable thereafter.
- **Preset membership (R10)** — `Color`/`Icon` must be in the server-known set.
- **Archive ≠ delete (R2)** — `ArchivedAt` is reversible; `DeletedAt` is the terminal tombstone.
- **Visibility (R11)** — defaults to `personal`; `shared` is not writable this slice.

**Aggregate shape** (factory + behavior methods, mirroring `Task`):
- `Create(ProjectId id, UserId owner, string name, string color, string icon, ProjectId? parentId, DateTime utcNow)` — sets fields, `Visibility = personal`, `Version = 0`; nesting/preset validated upstream.
- `Edit(string name, string color, string icon, ProjectId? parentId, DateTime utcNow)` — updates the mutable fields together (R4); stamps `UpdatedAt`, bumps `Version`. **Whole-object replace**: `parentId` is a **required** wire field (`EditProjectRequest`), so the command always carries the intended post-edit parent (`null` = top-level) — never a merge that could silently un-parent on a name-only edit.
- `Archive(DateTime utcNow)` / `Unarchive(DateTime utcNow)` — set/clear `ArchivedAt` (R2); `Unarchive` also nulls `ParentId` when the parent is still archived/deleted (R9).
- `SoftDelete(DateTime utcNow)` — stamps `DeletedAt`; the aggregate method itself guards against double-tombstoning (a no-op if already tombstoned, like `Task.SoftDelete`). **But the `DeleteProject` *command* is versioned, not idempotent**: unlike the task delete (version-free, replay → 204), it carries a `version` and applies real disposition mutations (R5), so a replay with a stale token → **409 `version_conflict`** (see §2). Only **create** is idempotent (FR-001 parity).

### ENT-01 — Task (owned by slice 002) — `project_id` activated

This slice does **not** own the Task entity; it activates the reserved `project_id` column by adding its FK and beginning to read/write it.

| Field | Type | Change this slice |
|---|---|---|
| `ProjectId` | `Guid?` (uuid) | **FK added** `project_id → projects(id)` **ON DELETE SET NULL** (column already exists from slice-002 `AddTasks`); written by `MoveTaskToProject` (R7); `NULL` = Inbox (R6) |

All other Task fields are unchanged from slices 002/003.

---

## 2. Optimistic concurrency — the `version` token (reused pattern)

`Project.Version` works exactly as `Task.Version`: starts at 0, incremented by every mutating method, configured `.IsConcurrencyToken()`. `EditProject`, `ArchiveProject`, `UnarchiveProject`, `DeleteProject`, and `MoveTaskToProject` carry the caller's last-seen `version`; a stale token → `VersionConflictException` → **409 `version_conflict`** (existing code, no contract change). Idempotent **create** (`CreateProject`) replays unchanged on a same-id retry (FR-001 parity), like `CreateTask`.

---

## 3. Inbox & project task lists (FR-021, R6)

- **Inbox** = `GET /api/tasks`, narrowed to `WHERE created_by = caller AND deleted_at IS NULL AND project_id IS NULL ORDER BY position, id`. **Backward-compatible**: every slice-002/003 task has `project_id = NULL`, so existing data stays in the Inbox; only tasks moved to a project (R7) leave it.
  - **"Newest first" (FR-021) = the existing slice-002 ordering, unchanged.** The Inbox *is* the slice-002 task list narrowed — it reuses the same `ORDER BY position, id` and the same `ix_tasks_created_by_position` index. Capture seeds `position` to a **newest-first** rank (slice 002 FR-102), and `Reorder` lets the user override it; so FR-021's "newest first" is the **default seed**, with the manual fractional rank authoritative thereafter — *not* a separate `created_at DESC` sort (which would discard manual order and break the slice-002 index). This slice adds only the `AND project_id IS NULL` predicate.
- **Project tasks** = `GET /api/projects/{id}/tasks` — `WHERE created_by = caller AND deleted_at IS NULL AND project_id = {id} ORDER BY position, id`, with the project ownership-checked (404 if foreign/absent).

---

## 4. Authorization scoping (deny-by-default, ownership branch — R13)

- **Reads**: every project/inbox/project-task query is scoped `WHERE owner_id = caller` (projects) / `WHERE created_by = caller` (tasks), always `AND deleted_at IS NULL`. Default views also filter `AND archived_at IS NULL` (projects); the archived listing (R8) drops only that last clause.
- **Writes**: `OwnerId`/`CreatedBy` coerced to the caller; a write targeting a foreign/absent/tombstoned project resolves to **404** (existence not disclosed). `M` (R7) checks ownership of **both** the task and the target project.
- **Read-model leak rule**: `ProjectResponse` never exposes `OwnerId` (always the caller) or `DeletedAt`. `TaskResponse` continues to hide `created_by`/`deleted_at`.
- **Test coverage (Constitution VIII + governance gate)**: **every** new handler ships an **allow** and a **deny** integration test through the real DB.

---

## 5. State transitions

### Project archive lifecycle (reversible — R2)
```
            Archive(childDisposition?)              Unarchive  (nulls ParentId if parent still archived — R9)
  active ───────────────────────────────▶ archived ───────────────────────────────▶ active (top-level if orphaned)
   │  (archived_at = null)                  (archived_at set)
```

### Project soft-delete lifecycle (terminal — R5, mirrors Task)
```
            DeleteProject(taskDisposition, childDisposition)        reaper (after undo window)
  live ──────────────────────────────────────────────────▶ tombstoned ──────────────────────▶ hard-deleted
   │   dispositions applied in-txn BEFORE tombstone:          (deleted_at set,                  (row removed;
   │   tasks → cascade | move_to_inbox | archive_with_tasks    excluded from all                 FK SET NULL
   │   children → cascade | orphan_to_top                      authz-scoped queries)             catches stragglers)
```
> The 30-second **undo UI** (FR-040) is deferred to slice 014 (accepted gap, see plan Complexity Tracking); the tombstone **persistence** (FR-097) ships here.

---

## 6. Read models (delta)

- **`ProjectResponse`** (NEW): `{ id, name, color, icon, parentId (nullable), visibility, archivedAt (nullable), version, createdAt, updatedAt }`. Hides `ownerId`, `deletedAt`. The web assembles the one-level tree from the flat list client-side (R16).
- **`TaskResponse`** (delta): gains `projectId` (nullable uuid) + `From(...)` projection. No other change.

---

## 7. Migration Plan (`AddProjects` — R14)

**EF Core migration** (`apps/api/src/TaskFlow.Infrastructure/Persistence/Migrations/`, new `*_AddProjects.cs`):
- `Up`: create `projects` (all §1 columns, types/nullability above); FK `owner_id → users(id)` CASCADE; self-ref FK `parent_id → projects(id)` SET NULL; partial index `ix_projects_owner_id` filtered `deleted_at IS NULL`; add FK `tasks.project_id → projects(id)` SET NULL (column pre-exists — constraint only).
- `Down`: drop the `tasks.project_id` FK, drop `projects` (and its index).
- **Migration-review checklist** (Constitution VII): forward-only / expand-contract (adding a table + a nullable-column FK is purely additive — no rewrite of existing rows); tested against a representative snapshot.

**FR-051 backup-before-migration is LIVE this slice** (R14): this is the first migration since slice 002, so the automatic pre-migration backup **and** the CI restore-test gate (Constitution VII) must actually execute against `AddProjects`. The plan tracks **verifying that gate is wired** rather than assuming it (it was a named no-op in slice 003).

**New application-layer seams**:
- `IProjectRepository` — `FindOwnedAsync` / `FindOwnedIncludingDeletedAsync` / `ListOwnedAsync(includeArchived)` / `ListChildrenAsync(parentId, owner)` / `Add` / `SaveChangesAsync`, mirroring `ITaskRepository` (incl. the `DbUpdateConcurrencyException → VersionConflictException` and unique-violation translation at `SaveChangesAsync`).
- FluentValidation validators for `CreateProject`/`EditProject`/`MoveTaskToProject` (command-local checks; cross-row nesting in the handler, R3).
- `ProjectId` strongly-typed id + EF value conversion, mirroring `TaskId`.

---

## What is unchanged

- The `tasks` table columns (only the `project_id` **FK** is added — no column change); `Task` content/status/position/due-date behavior; the soft-delete reaper mechanism; the error contract (no new code, R12); the BFF proxy, authentication wiring, and `ICurrentUser` resolution; the `version`/`version_conflict` machinery. No NodaTime (this slice does no date-relative computation — Constitution X has no new surface here; project timestamps are plain UTC).

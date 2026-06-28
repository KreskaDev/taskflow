# Data Model: Labels (slice 006)

**Input**: `spec.md`, `research.md` (R1–R12), constitution v4.0.0, and the slice-002..008 substrate (the `tasks` table; the slice-004 preset-token `Project`; the slice-005 `TaskAccessGuards` write path; the slice-007 `ProjectMembership` + policy seams + the `ProjectMembershipConfiguration` standalone-join mapping; the slice-008 `TaskResponse` delta precedent).

This slice **realizes ENT-04 (Label)** as a **per-user aggregate** and its **many-to-many relationship with tasks**. It adds **two new persisted tables** (`labels`, `task_labels`) in **one EF migration** (`AddLabels`) — so **FR-051 is LIVE** (R1/R10; the slice-004/007/008 posture). It adds the `Label` aggregate, the `CreateLabel`/`UpdateLabel`/`DeleteLabel` commands, the `ListLabels` query, the per-user `SetTaskLabels` command, the `LabelResponse` read model, and the **caller-scoped** `TaskResponse.labels` delta. It raises **no** domain events (R8) and adds **no** cleanup hook to the slice-007 flows (R5).

---

## 1. Entities

### ENT-04 — Label (NEW aggregate; per-user / Tier A)

A reusable tag owned by a single User, applied to many tasks (R1). `AggregateRoot<LabelId>`. **No optimistic `Version`** — a label is edited only by its single owner, so there is no concurrent-editor conflict to guard (contrast the Task aggregate); last-write-wins on the owner's own edits is acceptable.

| Field | Type | Change | Constraints / validation |
|---|---|---|---|
| `Id` | `LabelId` (UUIDv7) | **NEW** | **Client-generated** (`ValueGeneratedNever`, mirrors `TaskId`) so the selector creates + paints optimistically <16 ms (SC-003, R3). |
| `OwnerId` | `UserId` | **NEW** | The owning User (FR-065 Tier A). Immutable; set at create, never reassigned. The ownership authorization key. |
| `Name` | `string` | **NEW** | Required, trimmed-non-empty, **≤ 50 chars** (R7). Untrusted content — output-escaped on render (FR-099). The display value (preserves the owner's casing). |
| `NameNormalized` | `string` | **NEW** | The trimmed name **lower-cased in C# (`ToLowerInvariant`)**, set by the aggregate whenever `Name` is set. Backs the **case-insensitive per-owner uniqueness** via a **plain** unique index `(owner_id, name_normalized)` — EF Core 9 cannot model a functional `lower(name)` index (R7), so a normalized column keeps the constraint EF-native and snapshot-clean. Not exposed in `LabelResponse`. |
| `Color` | `string?` | **NEW** | **Optional** (ENT-04). When set, a **closed-set preset token** (the slice-004 `Project.Color` posture — not raw hex/CSS), preset membership validated upstream (R7). Never the sole carrier of meaning (FR-044). |
| `CreatedAt` | `DateTime` (UTC) | **NEW** | Set at create. `timestamptz` (Constitution X). |
| `UpdatedAt` | `DateTime` (UTC) | **NEW** | Stamped at create and on every edit. `timestamptz`. |

**Aggregate behavior**:
- `Create(LabelId id, UserId owner, string name, string? color, DateTime utcNow)` — normalizes/validates `name` (trim, non-empty, ≤50) and sets `NameNormalized = name.Trim().ToLowerInvariant()`; records `color` (preset validated upstream by the command validator); stamps `CreatedAt`/`UpdatedAt`. Uniqueness is a cross-row rule → enforced at the handler (a normalized-name pre-check) + the DB unique index (not in the aggregate).
- `Edit(string name, string? color, DateTime utcNow)` — **whole-object replace** (R3) realizing **rename + recolor**; re-normalizes `name` (and `NameNormalized`), sets `color`, stamps `UpdatedAt`.
- (No delete behavior on the aggregate — delete is a repository hard-delete; FK cascade clears applications.)

### ENT — `TaskLabel` (the join row; NOT a standalone aggregate, NOT owned by Task)

A pure per-user association row linking a task to one of the caller-owner's labels (R2). No behavior, no version. Mapped as a **standalone relation** (the `ProjectMembership` style — no navigation property on `Task` or `Label`).

| Column | Type | Notes |
|---|---|---|
| `task_id` | `uuid` | FK → `tasks(id)` **ON DELETE CASCADE** (task hard-delete / reaper cleans up). Part of PK. |
| `label_id` | `uuid` | FK → `labels(id)` **ON DELETE CASCADE** (label delete cleans up its applications; and — transitively via `labels.owner_id` cascade — account-deletion erasure, FR-085). Part of PK. |

**PK** = `(task_id, label_id)` — enforces set-uniqueness (a label is applied to a task at most once). No surrogate key, no timestamps, no `owner_id` column (ownership is derived through `label_id → labels.owner_id`; the join is partitioned per-user by that derivation, R2).

---

## 2. Value objects / new types

- **`LabelId`** (NEW) — `readonly record struct LabelId(Guid Value)` with `New()`/`From(Guid)`, mirroring `TaskId` (client-generated, `ValueGeneratedNever`).
- **No `Color` value object** — a preset token string (the `Project.Color` posture); preset membership validated by the command validator, not a domain type.
- **No domain event** (R8) — labels are not in the FR-079 notification trigger set; nothing to raise or route.

---

## 3. EF mapping (the boundary)

- **`LabelConfiguration`** (NEW) → table `labels`. `Id`/`OwnerId` value-converted to `uuid` via `HasConversion` (the `ProjectMembership` pattern). `Name` (`varchar(50)`, required), `NameNormalized` (`varchar(50)`, required), `Color` (`varchar`, nullable), `CreatedAt`/`UpdatedAt` (`timestamp with time zone`). `Id` `ValueGeneratedNever`. **Plain unique index** `ux_labels_owner_name` on `(owner_id, name_normalized)` — `HasIndex(l => new { l.OwnerId, l.NameNormalized }).IsUnique()`, the case-insensitive per-owner uniqueness via the normalized column (R7; **not** a functional `lower(name)` index, which EF Core 9 cannot model). **Index** `ix_labels_owner_id` (the roster query + the user-erasure cascade lookup). FK `owner_id → users(id)` via `HasOne<User>().WithMany().HasForeignKey(l => l.OwnerId).OnDelete(Cascade)` — account-erasure parity (Constitution XI).
- **`TaskLabelConfiguration`** (NEW) → table `task_labels`, a **hybrid** of two precedents: the **composite key over value-converted ids** from the slice-008 `task_assignees` block (`HasKey(e => new { e.TaskId, e.LabelId })` with per-property `HasConversion`) and the **standalone-entity, no-navigation-property** shape from `ProjectMembershipConfiguration`. FK `task_id → tasks(id)` and FK `label_id → labels(id)`, **both `OnDelete(Cascade)`**, **no navigation property** on `Task`/`Label`. **Index** `ix_task_labels_label_id` (the reverse lookup + the FK; the composite PK's `task_id` prefix serves the per-task lookup, so no standalone `task_id` index). (A standalone entity with a composite value-converted key is EF-Core/Npgsql-supported — confirmed by a throwaway mapping test.)
- **No change to the `tasks` table columns** — labels live entirely in the two new tables. The EF model snapshot gains exactly these two tables → exactly one migration (`AddLabels`).
- **No `OwnsMany` on `Task`** — `task_labels` is NOT an owned child of the Task aggregate (R2); it does not load with the task and does not ride the task's `version`.

---

## 4. Command surface (R3/R4)

| Command | Trigger | Endpoint | Body | Authorization & notes |
|---|---|---|---|---|
| `CreateLabel` (NEW) | selector "type a new name" / a future manager | `PUT /api/labels/{id}` | `{ name, color? }` (id in path) | Tier A: `ownerId = caller`. **Client-generated id in the path; idempotent-upsert** (a retried `PUT` of the same id returns the existing label, 200 — matching `createTask`/`createProject`; no 409). Duplicate `(owner, normalized name)` → **422** (handler pre-check + unique index backstop). Returns `LabelResponse`. |
| `ListLabels` (NEW) | the selector roster + chip name/color | `GET /api/labels` | — | Tier A: scoped `owner_id = caller`, ordered by `name`. Returns `LabelResponse[]`. |
| `UpdateLabel` (NEW) | rename / recolor | `PATCH /api/labels/{id}` | `{ name, color? }` | Tier A: load label; `OwnerId == caller` else **404** (uniform existence-hide). Whole-object replace (rename+recolor). Duplicate name → **422**. Returns `LabelResponse`. |
| `DeleteLabel` (NEW) | delete | `DELETE /api/labels/{id}` | — | Tier A: load label; `OwnerId == caller` else **404**. **Hard delete**; FK cascade removes `task_labels` rows. Returns **204**. |
| `SetTaskLabels` (NEW) | the `L` selector commit (US-08.AS-04) | `PATCH /api/tasks/{id}/labels` | `{ labelIds }` (**no `version`**) | **Two-sided** (R4): task write-access via `TaskAccessGuards.LoadWritableTaskAsync(Editor)` (personal-foreign→404, non-member→404, viewer→403) **AND** every `labelId` owned by caller (else **422**). Per-user whole-set replace of the caller's labels on the task; other owners' rows untouched. Returns the task's `TaskResponse` (caller-scoped `labels`). |

**Validation (FluentValidation, boundary checks only)**:
- `CreateLabelValidator` / `UpdateLabelValidator`: `name` non-empty + ≤50; `color` null or ∈ preset set (the slice-004 preset-membership check). Uniqueness is a cross-row rule → handler + DB index, not command-local.
- `SetTaskLabelsValidator`: `labelIds` well-formed uuids, **no duplicates** (it is a set), bounded size (a sane cap). The **caller-owns-every-label** check is a cross-row handler check (needs the caller's label set), not command-local.

**`SetTaskLabels` handler decision path**:
1. `TaskAccessGuards.LoadWritableTaskAsync(id, EffectiveRole.Editor, …)` → personal-foreign→404; shared non-member→404; shared viewer→403; else the task (read-only — used only to authorize).
2. Load the caller's label ids (`ILabelRepository.ListIdsForOwnerAsync(caller)` or equivalent). Every `labelId` MUST be in that set → else **422 `validation_failed`** (field `labelIds`), uniformly (no existence leak), no row changed.
3. Compute the caller's current labels on the task (`ITaskLabelRepository.ListLabelIdsForTaskAsync(taskId, caller)`), diff against `labelIds`, **delete removed + insert added** (caller-owned rows only). Idempotent: empty delta → no write.
4. Project `TaskResponse.From(task, callerLabelIds = labelIds)` and return. **`Task.Version` is unchanged** (no aggregate mutation).

---

## 5. Read model & query (R6)

### `LabelResponse` (NEW)
`{ id: uuid, name: string, color?: string }` — the caller's label. Returned by `CreateLabel`/`UpdateLabel` (single) and `ListLabels` (array). No `ownerId` leak (always the caller). Names output-escaped on render (FR-099).

### `TaskResponse.labels` delta
- Gains **`labels: uuid[]`** — the **caller's own** label ids applied to that task (R6). **Always present** (empty array when none); a **required** array field. Ids only (name/color via the roster). Caller-scoped: a co-member's labels on a shared task are **absent** from this caller's read.
- `TaskResponse.From` gains a **required parameter** `IReadOnlyList<Guid> callerLabelIds` — the compiler forces every call site to supply caller-scoped ids (the type-safe defense against a silent empty array; the slice-008 `Include`-omission lesson).

### Batched projection (the cross-cutting touch)
Every task-list query handler, after loading its tasks, calls **one** batched method:
- `ITaskLabelRepository.ListLabelIdsForTasksAsync(IReadOnlyCollection<TaskId> taskIds, UserId owner)` → `Dictionary<TaskId, List<Guid>>` (a `task_labels ⋈ labels WHERE task_id = ANY(@ids) AND owner_id = @caller` projection) — **one query per list**, not per row (perf budget SC-009/SC-012).
- Single-task reads (the command responses) use a one-task overload / `ListLabelIdsForTaskAsync(taskId, owner)`.

**Affected `TaskResponse`-building paths** (verified against the tree — each must supply caller label ids):
- **Command responses** (each returns `TaskResponse` directly → the required `From` parameter compile-breaks them): `CreateTask` (**two** `From` calls — the create + the idempotent-return), `RenameTask`, `EditTask`, `MoveTaskToProject`, `RescheduleDueDate`, `SetPriority`, `SetTaskDone`, `ReorderTask`, `SetTaskAssignees`, and `SetTaskLabels` (which supplies its just-committed set directly).
- **List/query reads** (each `.Select(TaskResponse.From)` → also compile-broken): the Inbox (`GetMyTasks`), the project list (`GetProjectTasks`), the slice-005 **Upcoming** (`GetUpcomingTasks`), and the slice-008 **"Assigned to me"** (`GetAssignedToMe`).
- There is **no** single-task `GET /api/tasks/{id}` (only `listTasks`).

**The flattened `TodayTaskResponse` exception (a genuine gap, R6).** The slice-005 **Today** view (`GetTodayTasks`) returns `TodayTaskResponse` (`allOf TaskResponse + isOverdue`), whose `From` calls `TaskResponse.From(task)` then **hand-copies fields** into a record with no `labels` — so the required-parameter defense does **not** force `labels` onto it. `TodayTaskResponse` MUST therefore be treated as a first-class affected path: add a `labels` field, give `TodayTaskResponse.From` a `callerLabelIds` parameter that surfaces it, and wire the batched projection in `GetTodayTasks`. Pinned by a dedicated **Today-carries-caller-labels** test (§9/R12). (Upcoming has no such wrapper — it returns `TaskResponse` directly.)

**The join translates** (no Npgsql value-converted-id-in-collection trap): the slice-005 trap is specific to a **nullable** value-converted FK; here both `task_id` and `label_id` are **non-nullable**, and non-nullable value-converted `Contains`/`= ANY` already translates in production (`ProjectRepository.ListByIdsAsync`, `UserRepository`). Still, exercise `ListLabelIdsForTasksAsync` directly in a throwaway integration test as cheap insurance before wiring it everywhere.

---

## 6. Migration Plan — **ONE migration (`AddLabels`); FR-051 LIVE**

- **`AddLabels`** (the only migration): `CreateTable labels` FIRST (it carries the `name_normalized` column, the **plain** unique index `ux_labels_owner_name` on `(owner_id, name_normalized)`, `ix_labels_owner_id`, FK `owner_id→users` CASCADE), THEN `CreateTable task_labels` (it FKs `label_id→labels`, so `labels` must exist first) with the composite PK, FK `task_id→tasks` CASCADE, FK `label_id→labels` CASCADE, and `ix_task_labels_label_id`. Additive (two new tables; `tasks` unchanged). The EF model snapshot gains exactly these two tables — **no `migrationBuilder.Sql`** (the normalized column makes the unique index plain and fully EF-generated, so the snapshot round-trips cleanly).
- **FR-051 is LIVE** (R10): the CI deploy job's `scripts/backup.sh → dotnet ef database update → scripts/restore-test.sh` gate (live since slice 001/004/007/008) covers this migration. This slice **confirms** the gate fires; it does not re-wire it. Verify **exactly one** new file under `Persistence/Migrations/` in the diff and read the generated SQL (the `(owner_id, name_normalized)` unique index + the two cascade FKs in particular).

---

## 7. Cleanup & lifecycle — **none beyond the FK cascades (R5)**

| Flow | Mechanism |
|---|---|
| Label deleted | FK `task_labels.label_id → labels(id) CASCADE` removes its applications. Hard delete (labels are not in the FR-040/FR-097 undo scope; slice 014). |
| Task deleted (reaper hard-delete) | FK `task_labels.task_id → tasks(id) CASCADE` removes its rows. |
| Account deletion (FR-085) | **Automatic**: `labels.owner_id → users(id) CASCADE` deletes the user's labels → `task_labels.label_id` cascade removes their applications. No handler. |
| Member leaves / removed / unshare (slice-007 flows) | **No cleanup** (R5). A former member's label rows on now-inaccessible tasks are **double-gated** (no task access AND no label-read access) → invisible, no leak. The slice-007 handlers are **untouched**. |
| Task moves project / to Inbox | **No clear** (R5). Labels are project-independent; a labelled task keeps its labels across a move. (Asserted by a test.) |

---

## 8. State transitions

**No new lifecycle.** A label is a small mutable entity (create → edit* → delete); its application to a task is set membership (`SetTaskLabels` is a per-user set mutation). Neither is a state machine. No event (R8).

---

## 9. Authorization scoping (deny-by-default — R4)

- **Label entity (Tier A, ownership)**: `CreateLabel` → `ownerId = caller`; `ListLabels` → `owner_id = caller`; `UpdateLabel`/`DeleteLabel` → `OwnerId == caller` else **404** (uniform existence-hide — never leak another user's label ids). Deny-by-default at the handler (FR-068).
- **`SetTaskLabels` (two-sided)**: task side dispatched by visibility (`TaskAccessGuards`, FR-065/FR-067 — personal→ownership 404; shared→membership+role: non-member 404, viewer 403, editor/owner write); label side Tier A (every id owned by caller else **422**). The per-user set replace touches **only** caller-owned rows.
- **Read-model leak rule**: `TaskResponse.labels` is caller-scoped ids only — a co-member's labels never appear in this caller's read; `LabelResponse` carries no `ownerId`.
- **Intentional consequence**: a **viewer** on a shared task **cannot** apply even their own labels (the task is read-only, FR-067) — documented, not a gap.
- **Test coverage (Constitution VIII + governance gate)**: every handler ships allow + deny — `CreateLabel` (allow / 401 / dup-422), `ListLabels` (allow / isolation), `UpdateLabel` (allow / not-owned-404 / dup-422), `DeleteLabel` (allow + cascade / not-owned-404), `SetTaskLabels` (personal-allow, shared-allow, viewer-403, non-member-404, personal-foreign-404, non-owned-label-422, per-user-isolation, idempotent no-op); plus a read-path **carries-caller-labels** + **caller-scoping** assertion and a **labels-survive-project-move** assertion.

---

## 10. What is unchanged

- The `tasks` table columns (labels live in the two new tables); the Task aggregate's behavior and **`version`** (label ops never touch it, R2); the slice-007 `ProjectMembership` + policy + `MembershipGuards`/`TaskAccessGuards` (reused for the task side, **unmodified**); the slice-007 membership-removal flows (**no cleanup hook**, R5); the slice-005 Today/Upcoming and the slice-008 "Assigned to me" query shapes (they gain only the caller-label batched-projection call); the `version`/`version_conflict` machinery; the Program.cs message wiring (**no event**, R8); the BFF/auth/`ICurrentUser` resolution; the error contract — **no new errorCode** (R9); the slice-001 CSP/security headers; **no new secrets** (FR-100).

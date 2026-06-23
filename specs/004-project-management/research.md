# Research & Design Decisions: Project Management (slice 004)

**Input**: `spec.md`, `.specify/memory/constitution.md` (v4.0.0), `.specify/memory/product-vision.md`, and the slice-002/003 substrate.

This slice is the first since slice 002 to **add a new entity (`Project`, ENT-02), a new aggregate, new commands/queries, and an EF migration**. It therefore re-activates two cross-cutting concerns that slice 003 explicitly named no-ops: **FR-051 backup-before-migration** (now LIVE — see R14) and the soft-delete reaper for a second aggregate. The format mirrors slice 003: each decision is **Decision / Rationale / Alternatives considered**, cross-referenced from `plan.md` and `data-model.md`.

Reference identity for examples: an authenticated, admitted caller `U`; all data is personal-visibility, owner = `U`.

---

## R1 — Project aggregate (ENT-02): a new table, modeled like `Task`

**Decision**: Introduce a `Project` aggregate root in the **Task Management** bounded context with its own table `projects`, mirroring the `Task` aggregate's proven shape: a client-generated `Id` (UUIDv7, `ValueGeneratedNever`), an immutable `OwnerId` (FK → `users(id)`, the authorization anchor), `Name`/`Color`/`Icon`, a nullable self-referential `ParentId`, a `Visibility` (default `personal`), an `ArchivedAt` (nullable; R2), UTC `CreatedAt`/`UpdatedAt`, a `Version` optimistic-concurrency token (starts at 0, bumped by every mutation), and a `DeletedAt` soft-delete tombstone (R5/R14). A `Create(...)` factory + behavior methods (`Edit`, `Reparent`, `Archive`, `Unarchive`, `SoftDelete`) hold the invariants.

**Rationale**: The `Task` aggregate already encodes every pattern this slice needs — client-supplied id + idempotent insert (FR-001 parity), optimistic concurrency (`version`/`version_conflict`), soft-delete tombstone excluded from authz-scoped queries, owner-FK `ON DELETE CASCADE` for the erasure path (Constitution XI). Reusing the shape keeps the handler/repository/validator conventions identical (the Wolverine pattern in the digest) and the review surface small.

**Alternatives considered**: (a) A separate "Projects" bounded context — rejected; ADR-0001/constitution places Task, Project, Cycle as aggregate roots in the *same* Task Management context. (b) Server-generated id — rejected; breaks the client-authoritative optimistic-create contract (SC-003) used by every other create in the app.

---

## R2 — Archive is reversible *state* (`archived_at`), distinct from the soft-delete *tombstone* (`deleted_at`)

**Decision**: Model archive as a nullable `archived_at` timestamptz (null = active, non-null = archived) — a **reversible lifecycle state** — kept entirely separate from `deleted_at` (the irreversible soft-delete tombstone reaped after the undo window). Archived projects are **live rows** excluded from *default views* by a `WHERE archived_at IS NULL` query filter, but still readable (the unarchive path, R8). Soft-deleted projects are excluded from **all** authz-scoped queries by `WHERE deleted_at IS NULL`.

**Rationale**: Archive (FR-013, hidden-but-searchable, fully reversible) and delete (FR-014, tombstoned, reaped) are different lifecycles with different visibility rules; conflating them onto one column loses the distinction the spec draws between AS-05 (archived → hidden) and EC-03 (deleted → gone). Storing a timestamp (not a bool) matches the `completed_at`/`deleted_at` style already in `TaskResponse` and gives the archive moment for free.

**Alternatives considered**: A single `status` enum (`active|archived|deleted`) — rejected; it couples the reversible/irreversible semantics and complicates the partial-index filters (the reaper only cares about `deleted_at`; default views only care about both).

---

## R3 — One-level nesting (FR-012): an **application-layer** invariant surfaced as 422, two failure shapes

**Decision**: Enforce "at most one level of nesting" in the **application layer** (the command handler), not as a pure FluentValidator and not as a DB constraint, because the check needs repository lookups. Two distinct violations both reject:
1. **Parent-is-already-a-child** — the candidate parent has a non-null `ParentId` (setting it as parent would create a grandchild).
2. **Project-has-children** — the project being (re-)parented itself has ≥1 child (giving it a parent would push its children to depth 2).

A violation throws `ValidationException` → **422 `validation_failed`** with a field-level message on `parentId` (FR-049 recoverable). The FluentValidator still does the cheap, command-local checks (name/color/icon/self-parent).

**Error split — 404 vs 422 (load-bearing, aligns with R13).** Before the nesting check runs, the candidate parent must be *resolved as owned*: a `parentId` that is **absent or owned by another user → 404** (existence not disclosed, the ownership posture). Only once the parent is confirmed caller-owned do the two nesting shapes apply → **422**: (1) the owned parent is itself a child; (2) the project being parented has its own children. Returning 422 for a foreign parent would leak the existence of another user's project (a Principle IX / FR-068 violation), so the ownership 404 strictly precedes the nesting 422. The `create`, `edit`, `archive`, `unarchive`, `delete`, and `move` operations all share this precedence.

**Rationale**: The invariant is inherently cross-row (it reads the candidate parent and the project's children), so it cannot live in a stateless validator or the aggregate alone — it belongs in the handler with the repository, consistent with where the constitution places authorization ("application layer … not scattered ad hoc"). Surfacing it as the existing `validation_failed` keeps the error contract unchanged (R12). The field-level `errors` entry in the ProblemDetails envelope carries the specific one-level-nesting message the client renders (AS-03/AS-09), so reusing `validation_failed` costs no UX fidelity.

**Alternatives considered**: (a) A dedicated `nesting_violation` error code — rejected; widens the `ErrorCode` union and forces an `ERROR_UX` entry for a message the 422 field-errors already carry better (R12). (b) A `CHECK` constraint / trigger — rejected; Postgres cannot express "parent must be top-level" without a subquery trigger, and EF code-first keeps invariants in the model, not hand-written triggers.

---

## R4 — Edit + re-parent are **one** `EditProject` command (AS-07/AS-08/AS-09)

**Decision**: A single `EditProject` command updates the mutable fields together — `Name`, `Color`, `Icon`, `ParentId` — carrying a `version` token (optimistic concurrency). Re-parenting is just supplying a different `ParentId`; it triggers the R3 nesting guard. This contrasts with `Task`, where rename/status/reorder are *separate* PATCH endpoints.

**Rationale**: AS-07 describes one project-editor form that saves name/color/icon/parent atomically; the UX is a form, not four micro-actions. One command = one optimistic mutation = one `version` round-trip, matching the form. The `Task` split exists because its mutations are independent single-key actions (rename inline, `done` toggle, drag-reorder); projects have no such per-field keyboard verbs this slice.

**Whole-object replace, not merge** — `EditProjectRequest` makes **all** mutable fields (incl. `parentId`) **required**. The form always loads and re-sends the project's current `parentId`, so the payload fully describes the post-edit state: `parentId: null` = top-level, `parentId: <uuid>` = that parent. This avoids the footgun of an *optional* `parentId` deserializing to `null` and silently un-parenting a child on a name-only edit — an omitted required field is a 422, not a silent demotion.

**Alternatives considered**: Split `RenameProject` / `RecolorProject` / `ReparentProject` — rejected as premature (YAGNI); no scenario edits a single project field by itself, and four endpoints quadruple the contract + test surface for no behavioral gain.

---

## R5 — Delete dispositions (FR-014/EC-03 + AS-10): explicit choices applied at soft-delete time

**Decision**: `DeleteProject` is a soft-delete (sets `deleted_at`, tombstone reaped after the undo window — R14) that takes **two** caller-supplied dispositions, applied in the same transaction *before* the tombstone:
- **Task disposition** (FR-014/EC-03, when the project has tasks): `cascade` (soft-delete the tasks too) | `move_to_inbox` (set `tasks.project_id = NULL`) | `archive_with_tasks` (archive the project instead of deleting — no tombstone — keeping its tasks).
- **Child disposition** (AS-10, when the project has child projects): `cascade` (soft-delete/archive children with the parent) | `orphan_to_top` (set `children.parent_id = NULL`, promoting them to top-level).

`ArchiveProject` of a parent takes the **child disposition** alone (AS-10 covers archive *or* delete of a parent-with-children). The confirmation dialog states its **blast radius** (count of affected tasks and child projects) per Principle VII.

**`cascade` follows the parent's resolved fate**: when the operation deletes (tombstones) the parent, `childDisposition=cascade` tombstones the children; when the operation *archives* the parent (either `ArchiveProject`, or `DeleteProject` with `taskDisposition=archive_with_tasks`), `cascade` **archives** the children too — the whole subtree shares the parent's terminal-vs-reversible disposition, never a mix.

**Rationale**: The spec is explicit that these are *user-chosen* dispositions presented in a dialog, not a fixed cascade. Applying them at soft-delete time (while the rows are still live) means the reaper later hard-deletes a clean, already-reconciled set; the FK backstops (R14, `ON DELETE SET NULL`) only catch stragglers. `archive_with_tasks` resolves to an archive (no tombstone), which is why it is a delete-dialog option that doesn't actually delete.

**Alternatives considered**: A fixed `ON DELETE CASCADE` from project→task — rejected; it removes the user's choice the spec mandates (move-to-inbox / archive-with-tasks) and would hard-cascade past the undo window.

---

## R6 — Inbox = `GET /api/tasks` narrowed to `project_id IS NULL`; project lists via a new path

**Decision**: Redefine the existing `GetMyTasks` query (and its `GET /api/tasks` endpoint) to return only **unprojected** tasks — `WHERE created_by = caller AND deleted_at IS NULL AND project_id IS NULL ORDER BY position, id` — this *is* the Inbox (FR-021). Add a new query/endpoint `GET /api/projects/{id}/tasks` for a project's task list (owner-scoped to the project). `TaskResponse` gains a nullable `projectId` (R16).

**Rationale**: FR-021 explicitly redefines slice 002's flat "all tasks" list as the Inbox. Every task created in slices 002/003 has `project_id = NULL`, so this narrowing is **backward-compatible for existing data** — those tasks remain in the Inbox exactly as before; only tasks subsequently moved to a project (R7) drop out. Renaming the endpoint would break the slice-002 client/E2E for no gain, so the path stays `GET /api/tasks` with narrowed semantics, documented in the contract.

**FR-021 "newest first" = the existing ordering, not a new sort.** The Inbox keeps slice 002's `ORDER BY position, id` and its `ix_tasks_created_by_position` index. `position` is seeded newest-first at capture (FR-102) and is then user-reorderable, so "newest first" is the default seed with the manual fractional rank authoritative thereafter. The only change is the added `AND project_id IS NULL` predicate — *not* a switch to `created_at DESC` (which would discard manual order and the index).

**Alternatives considered**: (a) `GET /api/tasks?project={id|inbox}` one polymorphic endpoint — rejected; mixes two authz scopes (own-inbox vs own-project) on one operation and complicates the typed client. (b) Keep `GET /api/tasks` as all-tasks and add a separate `GET /api/inbox` — rejected; leaves a now-meaningless "all tasks" list with no view that consumes it, contradicting FR-021's redefinition.

---

## R7 — Move-to-project (`M`, US-08.AS-05): `MoveTaskToProject`, null target = Inbox

**Decision**: A `MoveTaskToProject` command (`PATCH /api/tasks/{id}/project`, body `{ projectId: uuid | null, version }`) sets `tasks.project_id`. `projectId = null` moves the task **to the Inbox**. The handler authorizes that the caller owns the task **and** (when non-null) owns the target project; either failing → 404 (ownership posture). Carries the `version` token (optimistic concurrency, consistent with the other task mutations).

**Rationale**: Moving is a single-field task mutation, so it follows the `Task` per-field PATCH convention (unlike project edit, R4). Null-as-Inbox is the natural inverse of FR-021 — assigning removes from Inbox, clearing returns to it. Authorizing *both* endpoints' ownership prevents a caller from filing their task under someone else's project id.

**Alternatives considered**: Folding the move into a general `EditTask` — rejected; no such command exists (slice 002 split task mutations) and `M` is a discrete keyboard verb.

---

## R8 — Archived projects stay reachable without the command palette (slice 013)

**Decision**: `GET /api/projects` returns **active** (non-archived, non-deleted) projects for the sidebar tree (AS-05: archived hidden from default views). To keep **unarchive (AS-11)** reachable in *this* slice — before the command-palette search machinery arrives in slice 013 — the projects query supports an explicit archived listing (`GET /api/projects?archived=true`, owner-scoped), surfaced behind a minimal, keyboard-reachable "Archived projects" disclosure in the sidebar. The polished palette-search integration of AS-06 remains owned by slice 013.

**Rationale**: AS-06 names slice 013 as the owner of the *locating machinery* (search), but AS-11 (unarchive) is owned **here** and is untestable without *some* surface that lists archived projects. A plain owner-scoped archived listing is the minimal honest surface that satisfies AS-11 without pulling slice-013 search forward. Documented so a reviewer reads the minimal "Archived" disclosure as a deliberate bridge, not scope creep.

**Alternatives considered**: Defer unarchive UI entirely to slice 013 — rejected; AS-11 is explicitly owned by slice 004 and the Independent Test exercises unarchive end-to-end.

---

## R9 — Unarchive (AS-11): a child whose parent is still archived is restored **top-level**

**Decision**: `UnarchiveProject` clears `archived_at`. If the project's `ParentId` references a project that is **still archived (or deleted)**, the unarchive **also nulls `ParentId`**, restoring the child as a top-level project rather than re-nesting it under a hidden parent.

**Rationale**: Spec edge case + AS-11 are explicit. Re-nesting a restored child under a still-hidden parent would make it invisible again (it would inherit the parent's archived exclusion in the tree), defeating the unarchive. Promoting to top-level is the only restoration that actually surfaces the project.

**Alternatives considered**: Block unarchiving a child until its parent is unarchived — rejected; the spec mandates restoration, and a hard block is a worse UX than promotion.

---

## R10 — Preset colors & icons (ASM-04): a constrained, server-known set validated on both tiers

**Decision**: Colors and icons are drawn from a **closed, server-known set** (ASM-04), defined once as a shared constant and enforced at both trust boundaries: Zod `enum` on the web form (Constitution VI) and a FluentValidation `IsInEnum`/membership rule on `CreateProject`/`EditProject` (→ 422 `validation_failed` on a value outside the set). The web mirrors the set in `lib/projectPresets.ts`; the API holds the authoritative list.

**Rationale**: ASM-04 fixes colors/icons to a preset, never free-form — this both serves the minimalist palette (Principle IV/XII: no free-form style injection) and makes the values trivially validatable. Constraining at the API boundary is the load-bearing check (the web set is convenience); a value outside the set is a 422, not a 500.

**Alternatives considered**: Free-form hex/icon strings sanitized on render — rejected; contradicts ASM-04 and widens the XSS/styling surface (Principle XII) for no user value.

---

## R11 — Visibility: `personal` baseline only; `shared` value deferred to slice 007

**Decision**: Every project carries a required `Visibility` column defaulting to `personal`. This slice realizes **only** the `personal` value and the default-personal behavior (FR-057 personal half). The `shared` value, the `ProjectMembership` set, and the membership+role authorization branch (FR-066/FR-067) are **not** modeled here — they are slice 007. The column exists now so slice 007 is an additive value, not a schema change to a hot table.

**Rationale**: The spec's scope note is explicit: slice 004 owns the personal baseline + ownership; slice 007 owns shared. Reserving the column (like slice 002 reserved `project_id`) keeps slice 007 surgical. Authorization this slice dispatches purely on the **ownership** branch (R13).

**Alternatives considered**: Omit the column until slice 007 — rejected; it would force a migration on `projects` in slice 007 and contradicts the constitution's "projects have an `ownerId` and a `visibility`" required-fields statement.

---

## R12 — No new error code: reuse `validation_failed` / `not_found` / `version_conflict`

**Decision**: This slice adds **no** new `ErrorCode`. Nesting violations and out-of-preset color/icon → **422 `validation_failed`** (R3/R10); foreign/absent/tombstoned project ids and unauthorized access → **404 `not_found`** (ownership posture, R13); stale edits → **409 `version_conflict`**. The `ErrorCode` union and the `ERROR_UX satisfies Record<ErrorCode, ErrorUx>` map stay exhaustive **with no change** (the `last_owner` code already present is for slice-007 shared projects and is untouched).

**Rationale**: Mirrors slice 003's deliberate reuse. Every failure shape this slice introduces maps cleanly onto an existing code, and the 422 envelope's field-level `errors` carry the specific human messages (one-level-nesting, bad preset). No new code means no `TaskFlowDocumentTransformer` `ErrorCodes` edit and no `ERROR_UX` growth.

**Alternatives considered**: `nesting_violation` / `project_not_empty` codes — rejected (R3); the distinction the client needs lives in the field-error text, not a new top-level code.

---

## R13 — Authorization: deny-by-default, **ownership** branch, scoped queries, allow+deny tests per handler

**Decision**: Every project command/query and the `M` move dispatch on the **personal/unprojected → ownership** branch (Principle IX, FR-065/FR-068): `OwnerId`/`CreatedBy` is coerced to `ICurrentUser.Id` on writes (never a body field), and every read is scoped `WHERE owner_id = caller AND deleted_at IS NULL`. A request for a project the caller does not own resolves to **404** (existence not disclosed), matching the slice-002 task posture. `M` may only move a task the caller owns into a project the caller owns. Per Constitution VIII + the governance gate, **every** new data handler ships an **allow** and a **deny** integration test through the real DB.

**Rationale**: This is the constitution's central correctness requirement and the slice's only authorization surface (shared/membership is slice 007). The ownership coercion + scoped load is exactly the slice-002 `CreateTask`/`GetMyTasks` pattern; replicating it per handler keeps the policy uniform. 404-not-403 avoids leaking existence of others' projects.

**Alternatives considered**: 403 for foreign ids — rejected; discloses existence, diverging from the established task posture.

---

## R14 — Migration `AddProjects`: the first since slice 002 → **FR-051 is now LIVE**; FK backstops

**Decision**: Add one EF Core migration, `AddProjects`, that (a) creates the `projects` table (R1 columns) with `owner_id → users(id) ON DELETE CASCADE`, a self-referential `parent_id → projects(id) ON DELETE SET NULL`, and a partial index `ix_projects_owner_id` filtered `WHERE deleted_at IS NULL`; and (b) adds the deferred FK `tasks.project_id → projects(id) ON DELETE SET NULL` (the **column already exists** from slice-002 `AddTasks`; only the constraint is new). **FR-051 backup-before-migration — a named no-op in slice 003 — is LIVE for this slice**: it is the first schema change since the hook was specced, so the backup-before-migrate + CI restore-test gate (Constitution VII) must actually run against `AddProjects`. The plan tracks verifying that gate is wired (not assumed).

**Rationale**: The `project_id` column was reserved precisely so this slice adds only the FK, not the column (expand/contract discipline, forward-only). `ON DELETE SET NULL` on both FKs is a defensive backstop under the soft-delete model (R5): dispositions reconcile references *before* the reaper hard-deletes, so the cascade-to-null only ever catches a straggler. The owner FK cascades (Constitution XI erasure parity with tasks).

**Alternatives considered**: (a) `ON DELETE CASCADE` task←project — rejected (R5); removes the user's disposition choice. (b) Defer the `tasks.project_id` FK to a later slice — rejected; referential integrity for `M` (R7) needs the constraint now.

---

## R15 — Optimistic UI + cache keys: project ops paint < 16 ms (SC-003)

**Decision**: Project create/edit/archive/unarchive/delete and `M` move are TanStack Query optimistic mutations on a `['projects']` cache key (sidebar tree) and the existing `['tasks']` key (Inbox/move), following the slice-002 `onMutate`/`onError`/`onSettled` recipe (snapshot → optimistic patch → rollback → settle). The one-level-nesting **prevention message** (AS-03) is computed client-side from the already-loaded tree, so it paints instantly without a round-trip; the server re-validates authoritatively (R3).

**Rationale**: SC-003 (owned by slice 002) requires project create/move/archive to paint within 16 ms. The tree is small (a per-user project set, far under the task working-set anchor), so the client can render the optimistic parent/child placement and the nesting guard locally; the server remains authoritative and rolls back on rejection.

**Alternatives considered**: Block project create on the server round-trip — rejected; violates Principle III/SC-003.

---

## R16 — `TaskResponse` gains `projectId`; sidebar tree assembled client-side

**Decision**: Add a nullable `projectId` to `TaskResponse` (+ `From(...)` projection — the column already exists). The web assembles the parent/child sidebar tree from the flat `GET /api/projects` list client-side (group children under parents by `parentId`), rather than the API returning a nested structure.

**Rationale**: The client needs `projectId` to reflect a task's placement (and the optimistic move, R7/R15). Returning a flat project list (not nested JSON) keeps the contract simple and the typed client trivial; assembling at most one level of nesting on the client is cheap and matches how the optimistic cache is patched.

**Alternatives considered**: A nested `ProjectTreeResponse` from the API — rejected; one level of nesting is trivially assembled client-side, and a flat list reuses the same `ProjectResponse` for sidebar, archived-listing (R8), and selector (R7).

---

## Resolved unknowns summary

| Plan unknown | Resolved by |
|---|---|
| New entity shape, table, aggregate | R1 |
| Archive vs delete lifecycle | R2 |
| One-level nesting enforcement + error shape | R3, R12 |
| Edit/re-parent command surface | R4 |
| Delete/archive dispositions (tasks + children) | R5 |
| Inbox redefinition + project task lists | R6, R16 |
| Move-to-project (`M`) | R7 |
| Archived reachability + unarchive semantics | R8, R9 |
| Preset colors/icons enforcement | R10 |
| Visibility baseline (personal) | R11 |
| Error contract (no new code) | R12 |
| Authorization (ownership, scoped, allow+deny) | R13 |
| Migration + FR-051 now-live + FK behaviors | R14 |
| Optimistic UI budgets | R15 |
| `TaskResponse.projectId` + tree assembly | R16 |

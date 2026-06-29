# Research & Design Decisions: Labels (slice 006)

**Input**: `spec.md`, `.specify/memory/constitution.md` (v4.0.0), `.specify/memory/product-vision.md`, and the slice-002..008 substrate (the `tasks` table; the slice-004 preset-token `Project` aggregate; the slice-005 `TaskAccessGuards` dispatch-by-visibility write path; the slice-007 `ProjectMembership` + `ResolveEffectiveRole`/`RequireRole` policy; the slice-008 `task_assignees` join + `TaskResponse` delta precedent).

This slice realizes **ENT-04 (Label)** — a **per-user** tag (Tier A, `ownerId`) — and its **many-to-many relationship with tasks**, plus the **`L` label selector** (US-08.AS-04). Per the user's scope decision it ships **full label CRUD** (create, list, update = rename+recolor, delete) **and** the per-user apply/remove on a task. Format mirrors slice 005/007/008: each decision is **Decision / Rationale / Alternatives considered**.

**Reference identities for examples**: an authenticated, admitted caller `C` who owns labels `{L1, L2}`; a personal task `Tp` owned by `C`; a shared project `P` with members `{owner O, editor C, viewer V}` and a task `Ts` under `P`.

The repo facts that anchor everything (verified against the tree, not inferred):

1. **There is no `Label` entity, table, or column today.** `apps/api/src/TaskFlow.Domain/TaskManagement/` has `Task`, `Project`, `ProjectMembership`, the strongly-typed ids, and the slice-008 `TaskAssignee`. No `Label*` file exists (`Glob Label*.cs` → none). → Label is a **NEW aggregate**, and task↔label is a **NEW persisted relation** → a **real EF migration** (R1/R10).
2. **The preset-token convention is established** (slice 004): `Project.Color`/`Project.Icon` are stored as short **preset token strings** (`Project.cs` L69–72), validated for preset membership **upstream** (the command validator), never raw CSS/HTML. Labels reuse this posture for `color` (R7).
3. **The standalone-join-entity mapping pattern is established** (slice 007): `ProjectMembershipConfiguration` maps a relation table with **strongly-typed ids via `HasConversion`**, **no navigation property** on the parent aggregate, **dual `ON DELETE CASCADE` FKs** (`project_id→projects`, `user_id→users`), and a **named unique index**. `task_labels` mirrors this exactly (R2).
4. **The slice-005/007 authorization seams are reused verbatim**: `TaskAccessGuards.LoadWritableTaskAsync(id, EffectiveRole.Editor, …)` (personal→ownership 404; shared→`RequireRole`: non-member 404, viewer 403) governs the **task side** of apply/remove (R4).
5. **The error contract is closed** (`TaskFlowDocumentTransformer.ErrorCodes`): `validation_failed, unauthenticated, not_admitted, forbidden, not_found, conflict_lww, last_owner, internal_error, version_conflict`. **No new code this slice** (R9).
6. **The `TaskResponse` read model is a lean record** (`TaskResponse.cs`); slice 008 added a required `Assignees: Guid[]` projected in `From(task)`. Labels add a parallel **caller-scoped** `Labels: Guid[]` — but because labels are **not** on the Task aggregate (R2), `From` gains a **required parameter** (R6).

---

## R1 — `Label` is a per-user aggregate; `labels` + `task_labels` are two new tables; one EF migration (FR-051 LIVE)

**Decision**: Add a **`Label` aggregate** (`AggregateRoot<LabelId>`) with `{ Id, OwnerId (UserId), Name, Color?, CreatedAt, UpdatedAt }`, persisted in a new **`labels`** table. Add a **`task_labels`** join table `(task_id uuid, label_id uuid)`, **composite PK `(task_id, label_id)`**, FK `task_id → tasks(id) ON DELETE CASCADE` and FK `label_id → labels(id) ON DELETE CASCADE`. Both live in the slice's **one** EF migration (`AddLabels`), so **FR-051 is LIVE this slice** (the slice-004/007/008 posture, not the slice-005 no-op): the CI deploy gate `scripts/backup.sh → dotnet ef database update → scripts/restore-test.sh` covers it (already wired — confirmed, not re-wired).

**Rationale**: ENT-04 makes Label a **first-class per-user entity** (`ownerId`, Tier A) with its own lifecycle (create/rename/recolor/delete, reused across many tasks) — that is an **aggregate**, not a child of Task. The task↔label association is a **many-to-many between two aggregates**, whose canonical shape is a join table with a composite PK enforcing set-uniqueness ("a label is applied to a task at most once"). The dual `ON DELETE CASCADE` makes both erasure paths **automatic** with no handler: deleting a label removes its `task_labels` rows; deleting a task (the reaper) removes its rows; **and** `labels.owner_id → users(id) ON DELETE CASCADE` makes the **account-deletion erasure cascade (FR-085, Constitution XI) automatic** for a user's owned labels (R5). Genuine schema change → FR-051 flips LIVE; the gate already exists.

**Alternatives considered**:
- **(a) Labels as a `text[]`/`jsonb` column on `tasks`** — rejected. Labels are reusable shared-across-tasks entities with their own name/color and per-user ownership; an array of strings on the task can't model the entity, can't enforce the owner FK, duplicates name/color per task, and breaks rename/recolor (you'd rewrite every task). A relational entity + join is the only correct shape.
- **(b) `task_labels` as an `OwnsMany` child collection of the `Task` aggregate (the slice-008 `task_assignees` pattern)** — **rejected; this is the load-bearing divergence from slice 008** (see R2). Assignees are *shared* state owned by the task; labels are a *per-user* relation between two independent aggregates. Modelling task_labels as a Task-owned collection would (i) push per-user filtering (`which label-ids does the caller own?`) into the Task aggregate, violating ADR-0003, and (ii) couple a per-user toggle to the task's shared `version`. Rejected.
- **(c) A surrogate PK on `task_labels`** — rejected (YAGNI). The composite `(task_id, label_id)` is the natural key and enforces set-uniqueness; no behavior or second reference needs a surrogate.

---

## R2 — `task_labels` is a standalone M:N relation, NOT under the Task aggregate; per-user application; **no `Task.Version` bump; the apply command carries no `version`**

**Decision**: Map `task_labels` as a **standalone relation** (an explicit join entity `TaskLabel { TaskId, LabelId }`, served by a **dedicated repository** `ITaskLabelRepository`). The mapping is a **hybrid of two existing precedents**: the **composite key over value-converted strongly-typed ids** (`HasKey(e => new { e.TaskId, e.LabelId })` + per-property `HasConversion`) follows the slice-008 `task_assignees` block (`TaskConfiguration`), while the **standalone-entity, no-navigation-property** shape follows the slice-007 `ProjectMembershipConfiguration`. (It is NOT a verbatim copy of either: `ProjectMembership` uses a *surrogate* `Id` PK, and `task_assignees` is an *owned* type, not a standalone entity — a standalone entity with a composite value-converted key is EF-Core/Npgsql-supported and confirmed by a throwaway mapping test.) Applying/removing labels is a **per-user, whole-set replace scoped to the caller's own labels**: `SetTaskLabels` loads the task **read-only purely to authorize** (R4), then mutates **only the caller-owned** join rows (rows whose `label_id` belongs to `C`). It does **not** load or mutate the Task aggregate's state, does **not** bump `Task.Version`, and the command **carries no `version`**. Other owners' label rows on the same shared task are never read or touched.

**Rationale**: Labels are **per-user** (R1/ENT-04), so on a shared task each member sees and manages **only their own** labels — `TaskResponse.labels` is **caller-scoped** (R6). Three consequences follow and all argue against the slice-008 OwnsMany/version pattern:
1. **A per-user set replace cannot be expressed on the Task aggregate** without the aggregate knowing the caller's owned-label-ids — knowledge that lives in the `Label` aggregate. Keeping the mutation in a relation repository keeps per-user authorization out of the Task aggregate (ADR-0003).
2. **No cross-user conflict is possible**: two members editing "their labels" on the same shared task touch **disjoint** rows (partitioned by `label_id`'s owner). So there is nothing for an optimistic `version` to guard — last-write-wins per `(task, owner)` set is correct and conflict-free. The command is **versionless**; re-sending the same set is an idempotent no-op delta.
3. **Bumping the shared `Task.Version` on a private label toggle would be a bug-in-waiting**: it would churn every member's optimistic token and (in slice 016 real-time) fan out a SignalR patch to members **for a change they cannot see**. Decoupling labels from the task version avoids this by construction.

**Reconciliation with the spec's SignalR language**: the spec (Constitution Compliance, Principles II/III) contemplates "a server-initiated label change pushed to an open shared view (a SignalR label patch)" announced via a polite ARIA live region. That is a **future slice-016 (real-time) cross-cutting concern**; in **this** slice, because labels are per-user and no shared state changes, the **only** server-initiated label patch a caller can receive is from **their own other session** (same-user, multi-device LWW) — there is **no cross-member fan-out to suppress or announce**. So there is no contradiction: the per-user/no-fan-out decision here is the correct realization, and the polite-live-region announcement applies to the caller's own reconciled patch (R11).

This also means slice 006 inherits **none** of the slice-008 cross-slice complexity: there is **no clear-on-project-move** (labels are project-independent — a label stays with its task across moves) and **no membership-loss cleanup hook** (R5). The slice is genuinely **smaller and lower-risk than 008**.

**Alternatives considered**:
- **(a) OwnsMany on Task + version guard (slice 008)** — rejected for the three reasons above. Right for shared assignees, wrong for per-user labels.
- **(b) EF skip-navigation many-to-many** (`HasMany(Task).WithMany(Label)`) — rejected. The per-user read projection needs a **filtered** join (`owner_id = caller`), which a plain skip-navigation can't express cleanly, and a skip-nav would tempt eager `Include` of all owners' rows. An explicit join entity + repository gives precise, batched, caller-scoped queries (R6).
- **(c) Versioned `SetTaskLabels` (carry the task `version`)** — rejected. There is no shared state to guard (point 2); a version field would imply false cross-user conflicts and force needless 409s.

---

## R3 — Label CRUD command surface (owner-scoped): Create / List / Update / Delete + the per-user SetTaskLabels

**Decision**: Five handlers.

| Command / query | Verb + route | Body / scope | Notes |
|---|---|---|---|
| `CreateLabel` | `PUT /api/labels/{id}` | `{ name, color? }` (id in path) | **Client-generated `LabelId`** (UUIDv7, `ValueGeneratedNever`, mirrors `TaskId`) carried in the **path** so the selector can optimistically create + paint <16 ms (SC-003) **and a retried create is idempotent** — re-`PUT`ting the same id returns the existing label (200, no error), matching `createTask`/`createProject`. `ownerId = caller`. Duplicate `(owner, normalized name)` → **422** (R7/R9). |
| `ListLabels` | `GET /api/labels` | caller-scoped roster | Returns the caller's labels (`LabelResponse[]`), ordered by `name`. The selector's pick-list and the chip name/color source (R6). |
| `UpdateLabel` | `PATCH /api/labels/{id}` | `{ name, color? }` | **Whole-object replace** realizing **both rename and recolor** in one command (the slice-004 `EditProject` / slice-005 `EditTask` precedent — avoids two near-identical handlers). Ownership-gated; not-owned/absent → **404**. Duplicate name → **422**. |
| `DeleteLabel` | `DELETE /api/labels/{id}` | — | **Hard delete** (labels are not in the FR-040/FR-097 task/project undo scope — slice 014). FK cascade removes the label's `task_labels` rows. Ownership-gated; not-owned/absent → **404**. |
| `SetTaskLabels` | `PATCH /api/tasks/{id}/labels` | `{ labelIds }` (no `version`) | The `L` selector commit (US-08.AS-04). Per-user whole-set replace (R2). Two-sided authz (R4). Returns the task's `TaskResponse` with caller-scoped `labels`. |

**Rationale**: Create + List are the minimum the selector needs (create-in-place + the pick roster); the user elected **full CRUD**, so Update (rename+recolor) and Delete complete the entity lifecycle. `CreateLabel` is **`PUT /api/labels/{id}`** (client-generated id in the path, idempotent-upsert that returns the existing row on a retried `PUT`) — this matches the established `createTask` (`PUT /api/tasks/{id}`) / `createProject` (`PUT /api/projects/{id}`) convention and gives the optimistic-UI recipe **retry-idempotency** (a POST-to-collection with a client id in the body would fail the PK on a network-retry instead of returning 200). Folding rename+recolor into one `UpdateLabel` whole-object replace honors both capabilities without two endpoints/test-pairs, matching the established whole-object-edit precedent. `SetTaskLabels` mirrors the slice-008 `SetTaskAssignees` whole-set-replace ergonomics (the picker produces a full set) but **versionless and per-user** (R2).

**Alternatives considered**:
- **(a) Separate `RenameLabel` + `RecolorLabel`** — rejected; two handlers + two allow/deny test-pairs for what one whole-object PATCH expresses, with no UI distinction (the editor edits name and color together).
- **(b) Add/remove single-label-on-task commands** — rejected; the selector commits a full set, and a whole-set replace gives one optimistic round-trip + a clean per-user delta (mirrors R2/slice-008 R2).
- **(c) Idempotent create-by-name (return existing on duplicate)** — rejected; muddies create semantics. The selector shows the roster, so the user **picks** an existing label rather than re-creating; a genuine duplicate name is a 422 field error (R7).

---

## R4 — Authorization: label CRUD on ownership (Tier A); SetTaskLabels is **two-sided** (task write-access AND caller owns every label)

**Decision**: Deny-by-default at the handler (FR-068), dispatched by the containing resource's visibility (FR-065).

- **Label entity CRUD is Tier A (per-user ownership)**: `CreateLabel` sets `ownerId = caller` (any authenticated caller may create their own labels). `ListLabels` is scoped to `owner_id = caller`. `UpdateLabel`/`DeleteLabel` load the label and require `label.OwnerId == caller`; a label that is absent **or owned by someone else** → **404** (uniform — never distinguish "not yours" from "doesn't exist", so a caller can't probe another user's label ids).
- **`SetTaskLabels` is two-sided**:
  1. **Task side (dispatch-by-visibility)**: `TaskAccessGuards.LoadWritableTaskAsync(id, EffectiveRole.Editor, …)` — personal task not owned by caller → **404**; shared task, caller is non-member → **404**, caller is viewer → **403**; caller is owner/editor (or owns the personal task) → proceed. This is exactly the spec's "applying/removing a label follows the task's OWN authorization" (FR-067).
  2. **Label side (Tier A ownership)**: **every** id in `labelIds` MUST be a current label owned by the caller. Any non-owned/absent id → **422 `validation_failed`** (field error on `labelIds`, FR-049 recoverable), **uniformly** (no existence leak), and **no** row is changed.

**Self-consistency note (the spec's key distinction)**: the per-user label entity does **not** contradict the task-side authorization, because the application surface **inherits the task's dispatch**. A viewer on a shared task is **read-only** and therefore **cannot** apply even their own labels to it (a deliberate, spec-mandated consequence: a viewer cannot privately categorize shared tasks this slice — noted as intentional, not a gap). On a **personal** task only the owner reads/writes, and their labels are their own, so per-user scoping is trivially the owner.

**Rationale**: reuses the proven slice-005/007 seam for the task side verbatim (no new policy), and a simple ownership predicate for the label side. The two-sided gate is the literal realization of FR-065/FR-067 for this slice. 422-for-non-owned-label matches the slice-008 422-for-non-member-assignee posture (bad value in a request field); 404-for-not-owned-label-entity matches the 404-uniform existence-hiding posture used across the app.

**Alternatives considered**:
- **(a) Non-owned label id in `SetTaskLabels` → 404** — rejected; the task exists and the caller may write it, so the failure is about the *payload* (an invalid label id), a 422 field error.
- **(b) Allow a viewer to apply their private labels to a shared task they can only read** — rejected; contradicts FR-067 ("viewer read-only") and the spec's explicit "applying/removing follows the task's authorization (editor/owner to mutate)". Followed as written; the UX consequence is documented.
- **(c) Distinguish "label not yours" (403) from "label absent" (404) on CRUD** — rejected; leaks the existence of other users' label ids. Uniform 404.

---

## R5 — No cross-slice cleanup; no clear-on-project-move (the slice-008 complexity does NOT recur)

**Decision**: This slice adds **no** cleanup hook to the slice-007 membership-removal flows (unshare/remove/leave) and **no** assignee-style clear-on-project-move to `Task.MoveToProject`/`EditTask`. The only erasure is the **FK cascade** (R1).

**Rationale**: two structural facts make cleanup unnecessary:
1. **Labels are project-independent.** A label is the caller's own tag; it is meaningful on a task regardless of the task's project. Moving a task between projects (or to the Inbox) does **not** invalidate the caller's labels on it. So — unlike assignees (project-scoped membership) — there is nothing to clear on move.
2. **A former member's label rows are double-gated, so they leak nothing.** If `C` loses membership of project `P`, every read of a task `Ts` under `P` is denied at the **task** layer (membership dispatch — `C` sees nothing under `P`). The label rows `C` left on `Ts` are therefore unreachable; they are *also* gated by label-read scoping (`owner_id = caller`), but the task gate alone already hides them. They linger harmlessly until the task or the user is deleted (FK cascade). There is **no access window** and **no leak** — so an eager cleanup would be dead code. (Documented here so a reviewer does not flag the absence of a cleanup handler as a gap.)

**Alternatives considered**:
- **(a) Clear a user's labels on a shared task when they lose membership** — rejected as unnecessary (double-gated, no leak) and as scope creep into the slice-007 handlers. If labels ever become *shared/project-scoped* (a future product change), this would revisit — out of scope here.
- **(b) Clear labels on project move (the slice-008 `MoveToProject` clear)** — rejected; labels are project-independent (point 1). Asserted by a test (a labelled task keeps its labels across a move).

---

## R6 — Read model: `TaskResponse.labels` is caller-scoped ids; the roster supplies name/color; batched to avoid N+1

**Decision**: `TaskResponse` gains a **required `labels: Guid[]`** — the **caller's own** label ids applied to that task (ALWAYS present; empty when the caller has applied none). Ids only — names/colors come from the **roster** (`GET /api/labels`, `LabelResponse { id, name, color? }`), mirroring the slice-008 assignees+roster split. Because labels are **not** on the Task aggregate (R2), the projection cannot live in `TaskResponse.From(task)` reading `task.Labels`; instead:
- `TaskResponse.From` gains a **required parameter** `IReadOnlyList<Guid> callerLabelIds` (or an overload), so the **compiler forces every call site** to supply the caller-scoped label ids — the type-safe defense against the slice-008 "missing `Include` silently emits `[]`" failure mode.
- Each task-list query handler, after loading its tasks, calls **one batched** repository method `ITaskLabelRepository.ListLabelIdsForTasksAsync(taskIds, ownerId)` → `Dictionary<TaskId, List<Guid>>`, and maps each `TaskResponse.From(task, dict[task.Id] ?? [])`. **One query per list**, not per row (the 10k working-set perf budget, SC-009/SC-012). Single-task reads use a one-task overload.

The query method is a `task_labels ⋈ labels WHERE task_id = ANY(@ids) AND owner_id = @caller` projection of `(task_id, label_id)`. **Verify it translates** (no Npgsql value-converted-id-in-collection trap) by exercising the repo method directly in a throwaway integration test (the slice-005 EF lesson).

**Affected read paths** (every place that builds a `TaskResponse`, verified against the tree): the **command responses** — `CreateTask` (TWO `From` calls: the create + the idempotent-return), `RenameTask`, `EditTask`, `MoveTaskToProject`, `RescheduleDueDate`, `SetPriority`, `SetTaskDone`, `ReorderTask`, `SetTaskAssignees`; and the **list/query reads** — the Inbox (`GetMyTasks`), the project list (`GetProjectTasks`), the slice-005 **Upcoming** (`GetUpcomingTasks`), and the slice-008 **"Assigned to me"** (`GetAssignedToMe`). (There is **no** single-task `GET /api/tasks/{id}` — only `listTasks`.) Each returns `TaskResponse` directly, so the **required `From` parameter makes every one a compile error** until it supplies caller-scoped label ids — the type-safe defense holds for all of them.

**The one exception — the `TodayTaskResponse` flattened DTO (a genuine gap the required-param defense does NOT cover).** The slice-005 **Today** view (`GetTodayTasks`) returns a **separate flattened record** `TodayTaskResponse` (the contract's `allOf TaskResponse + isOverdue`). `TodayTaskResponse.From` calls `TaskResponse.From(task)` internally and then **hand-copies individual fields** into a new record — so adding `labels` to `TaskResponse` does **not** automatically add it to `TodayTaskResponse`, and the required `From` parameter can be satisfied without surfacing `labels` (exactly the slice-008 silent-missing regression class). **Therefore `TodayTaskResponse` is a first-class affected path**: it MUST gain its own `labels` field, `TodayTaskResponse.From` MUST take `callerLabelIds` and surface it, and `GetTodayTasks` MUST wire the batched `ListLabelIdsForTasksAsync`. A dedicated **Today-carries-caller-labels** test pins it (R12). (Upcoming does NOT have this problem — it returns `TaskResponse` directly.)

This is the slice's main cross-cutting touch (mechanical for the directly-typed paths; **explicit and tested** for the flattened Today path).

**Rationale**: ids+roster keeps `TaskResponse` lean and avoids per-task name/color duplication + rename-staleness. Caller-scoping is the literal realization of per-user labels (FR-065). The required-parameter trick converts the 008 silent-empty risk into a compile-time guarantee. Batching keeps the list reads within the perf budget.

**Alternatives considered**:
- **(a) Embed `labels: {id,name,color}[]` in `TaskResponse`** — rejected; payload bloat across 10k tasks, name/color duplicated per task, and a label rename would require touching every task projection. Ids + a small per-user roster is leaner and rename-safe.
- **(b) Keep `From(task)` and enrich the DTO afterward (mutable)** — rejected; `TaskResponse` is an immutable record (init-only). A required `From` parameter is cleaner and compiler-enforced.
- **(c) Per-row label query** — rejected; N+1 over the working set blows the perf budget. One batched query per list.

---

## R7 — Label `name` and `color`: validation, uniqueness, safety

**Decision**:
- **`name`**: required, trimmed-non-empty, bounded (≤ 50 chars). **Unique per owner, case-insensitive**, enforced via a **persisted normalized column** `name_normalized` (= the trimmed name lower-cased **in C#**, `ToLowerInvariant()`, set by the aggregate whenever `Name` is set) with a **plain unique index** `ux_labels_owner_name` on `(owner_id, name_normalized)`, plus a handler pre-check (`ExistsByNormalizedNameForOwner`). A duplicate on create/update → **422 `validation_failed`** (field error on `name`, FR-049). Output-escaped on render (React default; FR-099) — the name is untrusted user content.
- **`color`**: **optional** (ENT-04 "optional color"; nullable). When present, a **closed-set preset token** (the slice-004 `Project.Color` posture — a short token string like `slate`/`red`/`amber`/…, **not** raw hex/CSS), validated for preset membership **upstream** in the command validator. Color is **never the sole carrier of meaning** (FR-044) — the chip always shows the label **name**; the color is decorative. Null color → a neutral default chip.

**Rationale**: per-owner case-insensitive uniqueness makes the selector's "type a name" deterministic (one label per name per user — no silent twins). **Why a normalized column, not a functional `lower(name)` index**: EF Core 9 + Npgsql `HasIndex` accepts **property selectors only** — it cannot model an expression index like `lower(name)` (the repo has **zero** functional-index precedent; `.HasFilter(...)` is a *partial* index, a different feature). A naive `HasIndex(l => new { l.OwnerId, l.Name }).IsUnique()` yields a **case-SENSITIVE** index, so `Work`/`work` would coexist (violating the rule) **and** a TOCTOU race could let two concurrent creates both pass the handler pre-check and both insert. A persisted `name_normalized` column with a plain unique index keeps **EF owning the model/snapshot** (a clean additive migration, no `migrationBuilder.Sql` snapshot drift) and gives the DB backstop real teeth. Normalizing in C# (`ToLowerInvariant`) keeps the pre-check and the stored value consistent. The preset-token color reuses the established `Project` convention, sidesteps FR-099 (a token can't carry HTML/CSS injection) and the FR-044 contrast obligation (a curated palette can be contrast-checked once), and honors ASM-04 (preset colors, not custom values). Name-always-shown satisfies "color never sole meaning carrier".

**Alternatives considered**:
- **(a) A functional unique index `(owner_id, lower(name))`** — rejected; **not expressible via EF `HasIndex`** (would require raw `migrationBuilder.Sql`, which drifts the model snapshot out of sync — the migration would no longer be the purely EF-generated additive change FR-051's restore-test assumes). The normalized column is EF-native and snapshot-clean.
- **(b) A Postgres `citext` column** — rejected; needs the `citext` extension (an infra/migration dependency) for a problem a plain normalized column solves with no extension.
- **(c) Free-form hex color** — rejected; opens a (small) injection/styling surface, can't be contrast-guaranteed, and contradicts ASM-04's preset philosophy.
- **(d) No uniqueness (allow duplicate names)** — rejected; duplicate-named labels make the selector ambiguous and accrete noise. Per-owner case-insensitive unique.
- **(e) Required color** — rejected; ENT-04 says optional. Null → neutral chip.

---

## R8 — No domain events this slice

**Decision**: Label CRUD and `SetTaskLabels` raise **no** domain events.

**Rationale**: the only event consumer in the system is slice-017 notifications, whose trigger set (FR-079) is **closed** to `{ status, due date, assignee, project-move }` — **labels are explicitly not in it** ("low-signal edits MUST NOT notify"). Labels are per-user private metadata; there is no other member to notify (a label change is invisible to others by construction, R2). So there is no event to raise and no no-op handler to route (contrast slice 008's `TaskAssigned`). This keeps the Program.cs message wiring **unchanged** (no `PublishMessage`/queue/exclusion).

**Alternatives considered**:
- **(a) Raise a `TaskLabelsChanged` event for future use** — rejected (YAGNI). No consumer exists or is planned; an unrouted publish is silently dropped, and a speculative no-op handler is dead code.

---

## R9 — Error contract: no new error code

**Decision**: **No new `errorCode`.** Reuse the closed enum: `validation_failed` (422 — duplicate label name; non-owned/absent label id in `SetTaskLabels`; malformed body), `not_found` (404 — label not owned/absent on update/delete; task not accessible on `SetTaskLabels`), `forbidden` (403 — viewer on a shared task), `unauthenticated` (401 — deny-by-default backstop). All responses use the slice-001 ProblemDetails (RFC 9457) envelope.

**Rationale**: every failure maps cleanly onto the existing taxonomy (mirrors slice 008 R8). Adding a code would force `ERROR_UX` exhaustiveness churn across the web client for no semantic gain.

**Alternatives considered**:
- **(a) A `label_in_use` code for delete** — rejected; deleting a label that is applied to tasks is **allowed** (FK cascade removes the applications). There is no in-use conflict.

---

## R10 — Migration & FR-051: LIVE this slice (one migration: `AddLabels`)

**Decision**: **One** EF migration, `AddLabels`, creating **both** tables: `labels (id, owner_id, name, color NULL, created_at, updated_at)` with the `(owner_id, lower(name))` unique index and an `owner_id` index; and `task_labels (task_id, label_id, PK(task_id,label_id))` with both FKs `ON DELETE CASCADE` and a `(label_id)` index (the reverse lookup + the FK). Additive — the `tasks` table is **unchanged**. **FR-051 is LIVE** (R1): the CI deploy gate `backup.sh → ef database update → restore-test.sh` (live since slice 001/004/007/008) covers it — this slice **confirms** the gate fires; it does not re-wire it. Verify **exactly one** new file under `Persistence/Migrations/` in the diff and read the generated SQL.

**Rationale**: identical posture to slice 008 (one additive migration, gate already wired). Two tables in one migration is correct — they are introduced together and the snapshot gains both atomically.

**Alternatives considered**:
- **(a) Two migrations (one per table)** — rejected; they are one cohesive schema unit. One migration, two `CreateTable`s.

---

## R11 — Web surface: the `L` selector, optimistic create + apply, chips

**Decision**: Reuse the established web seams (no new dependency):
- A **`LabelSelector`** dialog (the shared `Dialog`, FR-101 focus contract: initial focus, trap, Esc, return focus to the invoking task row) opened by **`L`** on the selected task. It lists the caller's labels (the `['labels']` roster query) as keyboard-toggleable options (checkbox semantics, `aria-checked`), supports **type-to-create** (a text input → optimistic `CreateLabel` with a client-generated `LabelId`), and commits a **whole-set** `SetTaskLabels`. FR-031 suppresses bare `L` while the create/filter input is focused.
- **`L`** added to the `useGlobalShortcuts` contextual layer (a new `onLabel` handler), alongside the existing `E`/`M`/`A` contextual keys. **FR-045 (no AT-binding collision)**: bare `L` is a single printable letter on the selected-task context (the same class as the existing `E`/`M`/`A` keys, which already cleared this bar) and does not shadow a native screen-reader command; it is suppressed inside text inputs (FR-031), so it never collides with AT typing.
- **FR-042 (visible focus indicator)**: every focusable element introduced — the selector's toggle options, the create/filter input, and the chip affordance on the task row — carries the app's visible focus ring (the shared focus-indicator styling), so keyboard focus is always perceivable.
- **Optimistic mutations** (TanStack Query, the slice-005/008 `onMutate`/`onError`/`onSettled` recipe): `setTaskLabels` patches the task cache's `labels` array (<16 ms, SC-003), rolls back on error, settles on the server response. `createLabel` optimistically inserts into the `['labels']` roster. **No task `version` interplay** (R2) — the label patch leaves `version` untouched.
  - **CRITICAL — `onSettled` MUST do a labels-ONLY merge, NOT a whole-object writeback.** The slice-008 `setTaskAssignees.onSettled` calls `applyTaskToViewCaches(data, …)` (an **unconditional, version-unguarded** whole-object replace) — tolerable there because assignees **bump `version`**. For the **versionless** `setTaskLabels`, the returned `TaskResponse` carries an **unchanged `version`** and possibly-**stale non-label fields** (it can predate a not-yet-acked concurrent edit to the same task's title/priority), so a whole-object writeback would **clobber that in-flight edit** with no version to detect it. Therefore the label recipe's `onMutate` patches `{...row, labels}` from the **current cache row** (safe — local freshest), and `onSettled` patches **only the `labels` field** from `data.labels` onto the current row + runs `settleViewCaches` (invalidate) for the rest — it MUST NOT pass the server `data` through `applyTaskToViewCaches`. Pinned by a vitest case: *a label toggle does not revert a concurrent title/priority edit on the same task* (R12).
- **Chips**: `TaskRow` renders the caller's labels as **name** chips (the roster supplies name + the preset color as a decorative dot/background; **never color alone** — FR-044). A label change announces via the polite ARIA live region without stealing focus (FR-101).

**Rationale**: every piece has a slice-004/005/008 precedent (the project selector `M`, the assignee picker `A`, the optimistic recipe, the chip pattern). The client-generated `LabelId` is what makes create-then-apply paint instantly.

**Alternatives considered**:
- **(a) A separate label-management screen** — rejected this slice (no FR/scenario for it; YAGNI). The web selector exposes **create + apply/remove + delete**; **rename/recolor (UpdateLabel) are backend-complete and tested but have NO web UI this slice** — this matches the chosen scope (the "full CRUD" option was explicitly "commands + tests with no UI surface this slice"). The entity's full lifecycle is realized at the API; a dedicated management/edit UI (and the `color`-picker that makes ENT-04's optional color settable) is a later concern. (Consequently no `updateLabel` web mutation or `labelColorSchema` is shipped — they would be dead code.)
- **(b) Reuse the `M` project-selector component** — rejected; labels are multi-select toggles with create-in-place, structurally different from the single-select project move. A dedicated `LabelSelector` is clearer.

---

## R12 — Test surface (Constitution VIII/IX)

**Decision**:
- **xUnit (domain)**: `Label` create/rename/recolor (name normalization, ≤50 guard, preset color), `LabelId` round-trip.
- **Testcontainers integration** — **allow + deny per handler** (SC-016):
  - `CreateLabel`: allow (owner-scoped row written); deny (unauthenticated → 401); duplicate name → 422.
  - `ListLabels`: allow (only caller's labels returned); deny/isolation (another user's labels absent).
  - `UpdateLabel`: allow (rename + recolor); deny (not-owned/absent → 404); duplicate name → 422.
  - `DeleteLabel`: allow (row gone + its `task_labels` rows cascade-removed); deny (not-owned/absent → 404).
  - `SetTaskLabels`: allow on a **personal** task (owner applies own labels); allow on a **shared** task (editor applies own labels); the **deny matrix** — viewer-403, non-member-404, personal-foreign-404, **non-owned-label-422**; idempotent no-op; **per-user isolation** (caller's set replace does NOT touch another member's labels on the same shared task).
  - **Read projection**: a normal task read (e.g. the Inbox/project list or Today) **carries the caller's labels** (the batched join), and is **caller-scoped** (member `O`'s labels on `Ts` are absent from `C`'s read). A **labelled task keeps its labels across a project move** (R5 — no clear-on-move).
- **Vitest (web)**: the optimistic `setTaskLabels` + `createLabel` recipes (patch/rollback/settle); **the labels-only-merge settle pin — a label toggle does NOT revert a concurrent title/priority edit on the same task** (the versionless-writeback clobber guard, R11); the selector assembly (toggle, type-to-create); chip rendering (name present, color not sole meaning).
- **Playwright (E2E)**: US-08.AS-04 — select a task, press `L`, add and remove labels entirely via keyboard, verify persistence; plus the SC-008 a11y audit of the selector.

**Rationale**: every new/changed data handler ships allow + deny (Constitution VIII, governance gate); the SC-016 deny matrix is explicit; the caller-scoping and no-clear-on-move invariants (the two things most likely to regress) get dedicated assertions.

---

## Resolved unknowns summary

| # | Decision |
|---|---|
| R1 | `Label` per-user aggregate + `labels` + `task_labels`; one migration `AddLabels`; FR-051 LIVE. |
| R2 | `task_labels` standalone M:N relation (NOT OwnsMany); per-user apply; **no `Task.Version` bump; versionless command**. |
| R3 | Full CRUD: Create/List/Update(rename+recolor)/Delete + per-user `SetTaskLabels`. |
| R4 | Label CRUD on ownership (Tier A); `SetTaskLabels` two-sided (task write-access AND caller owns every label); deny matrix; viewer cannot tag shared tasks (intentional). |
| R5 | No cross-slice cleanup, no clear-on-move (labels project-independent; former-member rows double-gated → no leak). |
| R6 | `TaskResponse.labels` caller-scoped ids (required), roster supplies name/color; `From` required parameter; one batched join per list. |
| R7 | `name` ≤50, unique per owner case-insensitive (dup→422); `color` optional preset token (never raw CSS), name always shown. |
| R8 | No domain events (labels not in the FR-079 notification trigger set). |
| R9 | No new error code. |
| R10 | One migration `AddLabels` (two tables); FR-051 LIVE (gate already wired). |
| R11 | `L` selector (Dialog), optimistic create+apply (client-gen `LabelId`), name chips. |
| R12 | Allow+deny per handler; caller-scoping + no-clear-on-move dedicated assertions; E2E AS-04 + a11y. |

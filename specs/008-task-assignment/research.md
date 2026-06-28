# Research & Design Decisions: Task Assignment (slice 008)

**Input**: `spec.md`, `.specify/memory/constitution.md` (v4.0.0), `.specify/memory/product-vision.md`, and the slice-002..007 substrate (the `tasks` table, the slice-007 `ProjectMembership` aggregate + `ResolveEffectiveRole`/`RequireRole` policy, and the slice-005 `TaskAccessGuards` dispatch-by-visibility write path).

This slice realizes the **`assignees`** attribute of ENT-01 (Task) — **shared-project tasks only** (FR-069) — plus the add/remove command surface (FR-070), the **"Assigned to me"** view (FR-071), the `TaskAssigned` domain event for slice 017, and the assignment-cleanup hook on leave/remove/unshare. Format mirrors slice 005/007: each decision is **Decision / Rationale / Alternatives considered**.

**Reference identity for examples**: an authenticated, admitted caller `U` who is an **editor or owner** of a **shared** project `P` with members `{owner O, editor U, viewer V}`.

The repo facts that anchor everything (verified, not inferred):
1. **The `Task` aggregate has NO assignees today.** `Task.cs` declares `CreatedBy`, `Description`, `Priority`, `DueDate`, `DueHasTime`, `ProjectId`, `CycleId`, `RecurrenceRule` — there is no assignee collection and no reserved column for it. → assignees is a **NEW persisted relation** → a **real EF migration** (R1/R9).
2. **The slice-007 membership substrate is in the tree:** `ProjectMembership` + `IProjectMembershipRepository` (`ListByProjectAsync`/`FindAsync`/`ListProjectIdsForUserAsync`/`RemoveAllForProjectAsync`) + `ResolveEffectiveRole`/`RequireRole` (personal→ownership 404; shared→membership+role: non-member 404, insufficient-role 403) + `MembershipGuards`/`TaskAccessGuards`. Slice 008 reuses these unchanged for authorization.
3. **The slice-007 membership-removal flows exist:** `UnshareProject` (calls `RemoveAllForProjectAsync`), `RemoveMember`, `LeaveProject`. These must now ALSO clear assignments (R5).
4. **The domain-event wiring pattern is established** (Program.cs): `opts.PublishMessage<T>().ToLocalQueue("…")` under `UseDurableLocalQueues()`, a no-op/real handler so the publish is routed (an unrouted publish is silently dropped), and an `AuthorizationMiddleware` exclusion (`chain.MessageType != typeof(T)`) because queue messages have no `HttpContext` (`ICurrentUser.IsAuthenticated` is false off-queue). Mirrors `AccountDeletionRequested` and the slice-007 membership events.

---

## R1 — Persist assignees as a dedicated `task_assignees` join table; ship a real EF migration (FR-051 LIVE)

**Decision**: Model assignees as a **collection of `UserId` on the `Task` aggregate**, mapped by EF Core to a new **`task_assignees`** table: `(task_id uuid, user_id uuid)`, **composite PK `(task_id, user_id)`** (a user is assigned at most once — the set semantics), FK `task_id → tasks(id) ON DELETE CASCADE` and FK `user_id → users(id) ON DELETE CASCADE`. This is the slice's **one** EF migration (`AddTaskAssignees`), so **FR-051 is LIVE this slice** (the slice-004/007 posture, NOT the slice-005 no-op): the CI deploy job's `scripts/backup.sh → migrate → scripts/restore-test.sh` gate applies. The Task aggregate holds the assignees as a private collection with `AddAssignee`/`RemoveAssignee`/`SetAssignees` behaviors; assignees load with the task (the aggregate boundary — one-aggregate-per-transaction, ADR-0003).

**Rationale**: assignees are a **set of users per task** (multiple assignees, FR-069) — the canonical shape is a join table with a composite PK enforcing set-uniqueness at the DB. Modelling it as a child collection on the `Task` aggregate (not a separate aggregate) keeps assignment changes inside the task's optimistic-concurrency `version` guard and one transaction (consistent with how the task owns its own state). The `ON DELETE CASCADE` on `user_id` makes the **account-deletion erasure cascade (FR-085, Constitution XI) automatic** — deleting a user removes their assignee rows with no extra handler. The `ON DELETE CASCADE` on `task_id` cleans up when a task is hard-deleted (the reaper). This is a genuine schema change, so FR-051 flips LIVE — no way around it, and the gate already exists.

**Alternatives considered**:
- **(a) A `text[]`/`jsonb` column of user ids on `tasks`** — rejected. It can't enforce the member-FK or set-uniqueness at the DB, complicates the "Assigned to me" query (needs array-contains over a value-converted column — the slice-005 Npgsql nullable-FK lesson), and offers no referential erasure cascade. A relational join is the correct shape for a many-to-many.
- **(b) A standalone `TaskAssignee` aggregate** — rejected (YAGNI + ADR-0003). Assignment has no lifecycle independent of its task; folding it into the `Task` aggregate keeps it under the task's `version` and one transaction.
- **(c) Defer the migration / fake it** — impossible; there is no reserved column (unlike slice-005's pre-mapped `priority`/`description`). The migration is mandatory.

---

## R2 — Command surface: a whole-set `SetTaskAssignees` replace under the optimistic `version` guard

**Decision**: One command — **`SetTaskAssignees`** — bound by **`PATCH /api/tasks/{id}/assignees`**, body `{ assigneeIds: string[], version }` (a **whole-set replace** of the assignee set, the slice-004/005 anti-silent-null discipline). The handler computes the **delta** (added/removed) against the current set, applies it on the aggregate, and raises **one** `TaskAssigned` event carrying the delta + `actorUserId` (R3). Idempotent: re-sending the same set is a no-op delta → no event, but still a valid 200 (the version still bumps only if the set actually changed — see below). Add/remove are expressed as "send the new full set," matching how the assignee picker commits.

**Rationale**: the assignee **picker** naturally produces a full set ("these N members are assigned"), so a whole-set replace is the honest contract — it makes "change/remove assignees" (AS-02) a single optimistic round-trip and lets the handler compute the precise added/removed delta for the idempotent event (R3) in one place. It mirrors the slice-004 `EditProject` / slice-005 `EditTask` whole-object-replace precedent (required key, never a silent null). A no-op set (no delta) does **not** bump `version` and emits no event (idempotency, R3) — distinct from the slice-005 setters which bump on no-op-equal, because here the "no change" case is the idempotent-replay contract the event depends on.

**Alternatives considered**:
- **(a) Separate `AddAssignee` / `RemoveAssignee` per-user commands** — rejected. Two endpoints + two optimistic surfaces for what the UI does as one set edit; the delta/idempotency logic would be split and the multi-add case becomes N round-trips.
- **(b) Whole-set replace that always bumps version** — rejected for the no-op case; the event idempotency (R3) wants "same set → nothing happens," and a version bump on a true no-op would defeat idempotent retries.

---

## R3 — `TaskAssigned` domain event: assignee delta + `actorUserId`, idempotent, self-suppression deferred to consumer

**Decision**: Adding/removing assignees raises a **`TaskAssigned`** domain event carrying `{ taskId, projectId, addedAssigneeIds[], removedAssigneeIds[], actorUserId }`. It is **idempotent at the source** — emitted only when the delta is non-empty (a no-op set raises nothing). The event is **published to a durable local queue** (`task-assignment-events`) with a **no-op handler this slice** (so the publish is routed, not silently dropped) and an `AuthorizationMiddleware` exclusion; **slice 017 (notifications)** consumes it to generate the in-app notification for each genuinely-added assignee. **Self-suppression** (an actor assigning themselves gets no self-notification) is the **consumer's** responsibility using `actorUserId` — this slice carries `actorUserId` in the payload and documents the rule; it does not deliver notifications (spec L52/L70).

**Rationale**: mirrors the slice-007 membership-event pattern (delta carried, durable-queue routed, consumed downstream). Emitting only on a real delta gives "at most one notification per genuine change" (FR-070 idempotency) at the source. Carrying `actorUserId` lets slice 017 suppress the self-notification without this slice needing a notification subsystem. The no-op handler this slice keeps the publish routed (the Program.cs comment: an unrouted publish is silently dropped) and is replaced/augmented by slice 017's real handler.

**Alternatives considered**:
- **(a) Suppress the self-assignment in THIS slice** — rejected; there is no notification surface here to suppress, so self-suppression has no observable effect until slice 017. Carrying `actorUserId` is the correct seam.
- **(b) One event per added assignee** — rejected; a single event with the delta is fewer messages and lets the consumer fan out, matching the membership-event shape.

---

## R4 — Authorization: dispatch-by-visibility, shared-only, editor/owner to write; assignee is provenance only

**Decision**: `SetTaskAssignees` is **deny-by-default, enforced at the handler** (FR-068), dispatched by the containing project's visibility via the slice-005 `TaskAccessGuards.LoadWritableTaskAsync(id, EffectiveRole.Editor, …)` (personal→ownership; shared→`RequireRole(Editor)`: viewer→**403**, non-member→**404**). On top of that base gate, assignment adds two slice-specific rules:
1. **Shared-only (FR-069)**: assignment exists only on a shared-project task. A personal/Inbox task → **404** (the assignment surface does not exist — mirrors the slice-007 `/members` "exists only on a shared project → 404" posture in `MembershipGuards.LoadManageableSharedProjectAsync`).
2. **Assignee-must-be-a-current-member (FR-069/B3)**: every id in `assigneeIds` MUST be a current `ProjectMembership` of the task's project (the owner counts as a member-equivalent via the owner anchor). A non-member assignee → **422 `validation_failed`** (a field error on `assigneeIds`, FR-049 recoverable) — the assignment is rejected and **no** assignee is added. This has an explicit **deny test** (SC-016).

**Assignee is provenance only** (FR-066): being an assignee confers **no** standalone access — `dispatch-by-visibility` (membership) decides access. On leave/remove/unshare the user loses ALL access AND their assignment rows are cleared (R5). **Self-assignment is allowed** (an editor/owner who is a member may include themselves). The **"Assigned to me"** read (R6) is scoped to the caller's identity AND current membership, so a user who lost membership stops seeing those tasks even if a stale assignee row lingered.

**Rationale**: reuses the proven slice-005/007 seams verbatim (no new policy). The shared-only + member-assignee rules are the only slice-specific additions, both at the handler, both deny-tested. 404-for-personal matches the 007 surface-existence posture; 422-for-non-member-assignee matches the "bad input in a field" posture (the assignee set is a request field). No authorization tier is added — assignee is never part of an authorization conjunction (the constitution's dispatch-by-visibility rule).

**Alternatives considered**:
- **(a) Non-member assignee → 404** — rejected; the task exists and the caller may see it, so the failure is about the *request payload* (an invalid assignee), which is a 422 field error, not a not-found.
- **(b) Personal task assignment → 422** — rejected in favor of 404, matching the slice-007 "manage surface exists only on shared" precedent (existence of the surface, not payload validity).

---

## R5 — Assignment cleanup on leave / remove / unshare (and account deletion)

**Decision**: When membership ends, the user's assignment rows for that project's tasks are cleared, so no stale assignee row outlives access (FR-066, spec scenario 7; slice-007 FR-059/FR-062/FR-063):
- **Unshare** (`UnshareProject`, which already `RemoveAllForProjectAsync`): clear **ALL** `task_assignees` rows for every task in that project (the project reverts to personal; assignment no longer applies).
- **Remove member** (`RemoveMember`) / **leave** (`LeaveProject`): clear that **one user's** `task_assignees` rows across the project's tasks.
- **Account deletion** (FR-085): the `user_id` FK `ON DELETE CASCADE` (R1) clears the deleted user's assignee rows automatically — no handler change.

Implemented via new `ITaskRepository` set-based deletes (`ClearAssigneesForProjectAsync(projectId)` and `ClearAssigneesForUserInProjectAsync(projectId, userId)`, parameterized bulk SQL). **The unshare/remove/leave clears are EVENT-DRIVEN, consuming the slice-007 domain events that were built for exactly this** (the slice-007 `MembershipEventHandlers` TODO names slice 008 as the consumer of `ProjectUnshared`/`MembershipRevoked`):
- `ProjectUnsharedHandler` → `ClearAssigneesForProjectAsync` (clear ALL).
- `MembershipRevokedHandler` → `ClearAssigneesForUserInProjectAsync`, **guarded by a current-membership re-check** so a role DEMOTION (editor→viewer, which also raises `MembershipRevoked`) does NOT clear — a viewer is still a member and may remain an assignee. Remove/leave (the user is gone) clears.

These no-op slice-007 handlers become **real consumers** (their membership allow/deny is inherited from slice 007; the new assertion is "assignment cleared"). Async cleanup is **safe** here precisely because the assignee row is **provenance only** (FR-066, above): the moment membership ends the user already loses access via the membership-gated read (R6), so a row lingering until the durable-queue handler runs is invisible and harmless — there is no access window. (`DeleteProject` `move_to_inbox` is the exception — handled INLINE, below, because it nulls `project_id` before any event handler could find the rows by project.)

**Also — a task that MOVES projects (or to the Inbox) clears its assignees.** Assignment is scoped to the task's project's membership (FR-069: a personal/Inbox task MUST NOT carry assignees, and a different project has different members). So **`Task.MoveToProject` clears the assignee set whenever the project actually changes** — this is a slice-004 behavior modified here (a cross-slice touch, like the membership-removal touches), covering the aggregate-routed move paths: `MoveTaskToProject` (the `M` action) and `EditTask`'s `projectId` field (slice 005, which moves to the Inbox with no ownership check, so **any editor** could otherwise strand assignees on a now-personal task). The clear is a structural cleanup (no `TaskAssigned` event — consistent with the leave/remove cleanup, which also raises no per-removal event); it is asserted by a test on `EditTask`/`MoveTaskToProject` (move-clears-assignees).

**⚠ The bulk-move path bypasses the aggregate — `DeleteProject` with `move_to_inbox` must clear too.** `DeleteProject` (slice 004, owner-only, supports shared projects) with the `move_to_inbox` disposition calls `ProjectRepository.MoveProjectTasksToInboxAsync` — a bulk `ExecuteUpdate` that nulls `project_id` **without loading the `Task` aggregate**, so `Task.MoveToProject`'s clear never fires. Deleting a shared project with this disposition would otherwise strand `task_assignees` rows on the now-Inbox (personal) tasks (which then surface as chips via the eager-load — the exact FR-069 violation the clear exists to prevent). So `DeleteProject`'s `move_to_inbox` branch ALSO calls `ClearAssigneesForProjectAsync(projectId)` in the same transaction. (The `cascade` disposition is NOT a violation — its tasks are soft-deleted and the `task_id` FK `ON DELETE CASCADE` clears the rows at reap.) Together with `MoveToProject`, this closes the `TaskResponse.assignees`-empty-for-personal invariant against **all** move-to-Inbox paths.

**Rationale**: the spec is explicit that assignment must not survive membership loss. The slice-007 events already carry exactly the needed payload (`ProjectUnshared{projectId}`, `MembershipRevoked{projectId, userId}`) and were authored with slice 008 named as their consumer, so consuming them is the architecturally-consistent seam (decoupled; the membership handlers stay ignorant of assignments). Account deletion is handled for free by the FK cascade.

**Alternatives considered**:
- **(a) Rely only on the read-time membership filter (R6) and leave stale rows** — rejected; stale assignee rows would be a privacy residue (Constitution XI) and could resurface if the user is re-added; clearing them is the spec's requirement.
- **(b) Clear INLINE inside the slice-007 handlers (in the membership transaction)** — viable and atomic, but rejected in favor of the event-driven seam: it would couple `RemoveMember`/`LeaveProject`/`UnshareProject` to task assignments and duplicate the demotion-distinction logic, whereas the events already exist for this. The atomicity inline buys is not needed — assignee is provenance-only, so there is no access window to close (see above). `DeleteProject` `move_to_inbox` is still inline (the bulk move nulls `project_id` first).

---

## R6 — "Assigned to me" read query: caller-scoped, membership-dispatched

**Decision**: Add a query **`GetAssignedToMe`** bound by **`GET /api/tasks/assigned`**: the caller's tasks across **shared** projects where the caller is **(a)** a current member of the project AND **(b)** an assignee (`task_assignees` row), excluding done/cancelled/deleted by default, grouped/ordered for display (by project, then the slice-005 R5 task order). Scoped to `ICurrentUser.Id` — never a wire field. Because access is **dispatched by membership** (not by the assignee row), a user who lost membership sees nothing even if a stale row existed (defense-in-depth behind R5's cleanup).

**Member of the project = membership-row member OR the owner.** ⚠ The owner has **no `ProjectMembership` row** (the policy derives owner from `project.OwnerId`; `ListProjectIdsForUserAsync` returns only membership-row projects). Since self-assignment is allowed for an editor/owner (spec scenario 6), the read MUST union the caller's **owned shared** project ids with `ListProjectIdsForUserAsync(caller)` — otherwise an owner who self-assigns would never see the task in "Assigned to me" (a write/read asymmetry). So the readable shared-project set = `ListProjectIdsForUserAsync(caller)` ∪ `{ caller's projects WHERE owner = caller AND visibility = shared }` (a `ProjectRepository` owned-shared-ids seam, e.g. a narrow projection over the existing owner-scoped find).

**Rationale**: FR-071 + the dispatch-by-visibility rule: the assignee row is provenance, membership (or ownership) is the gate, so the query joins `task_assignees` (caller) ∩ the caller's readable **shared** project ids (memberships ∪ owned-shared). Mirrors the slice-005 `ListDueInRangeReadableAsync` shape (caller + shared-project-ids), here additionally constrained to the caller's assignee rows and to shared visibility. Keeping the owner in the readable set fixes the self-assignment write/read asymmetry the design otherwise has.

**Alternatives considered**:
- **(a) Scope only by the assignee row (ignore membership)** — rejected; a stale assignee row after membership loss would leak the task — violates FR-066. Membership must gate.
- **(b) Reuse the Today/Upcoming read** — rejected; "Assigned to me" is a distinct, date-independent view (no Warsaw boundary), so it is its own query + read model.

---

## R7 — Read model: `TaskResponse.assignees`; the "Assigned to me" response

**Decision**: Add **`assignees: string[]`** (an array of assignee **user ids**, uuid strings) to `TaskResponse`, populated from the loaded `task_assignees`; **empty array** for personal-project tasks (assignment never applies). It is a **required** array (always present, possibly empty) so the client type is non-nullable. The "Assigned to me" view returns the existing `TaskResponse` shape (optionally grouped by project, like the slice-005 Today read model). Member display names/avatars are resolved client-side from the already-loaded project roster (slice 007 `MembersResponse`) — `TaskResponse` carries only ids (no PII duplication; Constitution XI/XII).

**Rationale**: the row needs to show who is assigned; ids are the minimal, PII-free payload (the roster supplies names, FR-099 output-encoded). An always-present (possibly empty) array keeps the generated TS type clean (`string[]`, not `string[] | null`). No `createdBy`/`deletedAt` leak (the read-model rule).

**⚠ Eager-load on EVERY task read path.** Because `assignees` is now a **required** field on `TaskResponse`, EVERY query that projects a `TaskResponse` MUST eager-load the `Assignees` collection — not just the assigned view: the Inbox list (`ListOwnedAsync`), the project task list (`ListByProjectAsync`), the single-row loads (`FindOwnedAsync`/`FindByIdAsync`), and the slice-005 Today/Upcoming (`ListDueInRangeReadableAsync`). If a path forgets the `Include`, `TaskResponse.From` silently emits an empty array and assignee chips vanish on that view. The `Include(t => t.Assignees)` is added to all task-loading repository methods, with a test asserting a shared task carries its assignees in a normal (non-assigned) view.

**Alternatives considered**:
- **(a) Embed member names/avatars in `TaskResponse.assignees`** — rejected (PII duplication + staleness); ids + the roster is the single source for names.
- **(b) Nullable `assignees`** — rejected; an empty array is the honest "no assignees," and a non-null array is a cleaner client contract.

---

## R8 — Error contract: no new error code

**Decision**: **No new `ErrorCode`.** A viewer attempting to assign → **403 `forbidden`** (insufficient role); a non-member caller, or assignment on a personal/Inbox task, or a foreign/absent task → **404 `not_found`**; a non-member assignee id (or malformed/duplicate ids, or an over-large set) → **422 `validation_failed`** (field error on `assigneeIds`); a stale token → **409 `version_conflict`**. The `ErrorCode` union and the `ERROR_UX satisfies Record<ErrorCode, ErrorUx>` map stay exhaustive **with no change**; the document transformer stamps the new operationIds + their 403/404/409/422 responses but edits **no** `ErrorCodes` array (the slice-005/007 precedent).

**Rationale**: every failure shape maps onto an existing code (the slice-005/007 reuse discipline). The member-assignee rule is a field-validation failure (422); the surface-existence rules are 404; role is 403; concurrency is 409.

**Alternatives considered**: a `not_a_member` / `assignment_not_allowed` code — rejected; the distinction lives in the 422/404 message + field-error, not a new top-level code (the slice-003/004/005 precedent).

---

## R9 — Migration & FR-051: LIVE this slice (one migration: `AddTaskAssignees`)

**Decision**: This slice ships **exactly one** EF migration — `AddTaskAssignees` (the join table, R1) — so **FR-051 (backup-before-migration) is LIVE** (the slice-004/007 posture). The CI deploy job already runs `scripts/backup.sh → dotnet ef database update → scripts/restore-test.sh`; this slice **confirms** that gate fires, it does not re-wire it. The migration is additive (new table only; no column change to `tasks`), so it is backward-compatible and the model snapshot gains exactly the one table.

**Rationale**: assignees is genuinely new persisted state (R1), so a migration is mandatory and FR-051 is non-negotiably LIVE. The gate exists; the slice's job is to verify exactly one new migration file appears and the backup/restore-test job covers it.

**Alternatives considered**: none — a new relation requires a migration.

---

## R10 — Web surface: the assignee picker + the "Assigned to me" view, optimistic

**Decision**: Reuse the slice-005/007 web substrate. Add: (1) an **assignee picker** (a keyboard-operable, dialog-focus-contract component, FR-101/FR-031) listing the project's members (from the slice-007 roster) with checkboxes; committing calls an optimistic `setTaskAssignees` mutation (SC-003, the slice-005 `useTaskMutations` recipe shape: snapshot → optimistic patch → rollback → settle-writeback). (2) An **"Assigned to me"** route + view (`GET /api/tasks/assigned`) reusing the `DailyView`-style grouped list. (3) Assignee chips on the task row (member names from the roster, output-encoded, FR-099/FR-044 — never avatar-color alone). The picker only appears on **shared-project** tasks (FR-069/AS-04 — no control on personal tasks). Navigation: an "Assigned to me" entry (a `G A` chord, extending the slice-005 `G`-chord nav).

**Rationale**: the optimistic mutation + dialog + grouped-view patterns are all established in slices 005/007; this slice adds bindings + the picker, not machinery. Ids → names via the roster keeps the client PII-light.

**Alternatives considered**: a free-text assignee entry — rejected (FR-069 requires members; a picker over the known roster is correct and avoids free-form input).

---

## R11 — Test surface (Constitution VIII/IX)

**Decision**: Red-Green across tiers:
- **Domain xUnit**: `Task.SetAssignees`/`AddAssignee`/`RemoveAssignee` (set semantics, delta computation, idempotent no-op, version bump only on real change, the `TaskAssigned` event raised with the right delta + actor).
- **Integration (Testcontainers)**: `SetTaskAssignees` — allow (editor assigns members; self-assignment allowed) + the full SC-016 deny matrix (**viewer → 403**, **non-member → 404**, **personal task → 404**, **non-member assignee → 422**, stale version → 409); the `TaskAssigned` event asserted via `host.TrackActivity()…Sent.MessagesOf<TaskAssigned>()` (delta + actor, and a no-op set emits none). `GetAssignedToMe` — allow (caller sees their assigned shared tasks) + deny (a non-member / non-assignee does not; a user who lost membership stops seeing them). The slice-007 leave/remove/unshare handlers get an **assignment-cleared** assertion.
- **Web Vitest**: the optimistic `setTaskAssignees` surface (patch/rollback/settle); the picker membership list; the "Assigned to me" assembly.
- **E2E Playwright**: US-13 AS-01..AS-04 (assign in a shared project; change/remove; "Assigned to me"; no control on a personal task) + the SC-008 a11y audit on the picker + the assigned view.

**Rationale**: every new/changed data handler ships allow+deny (the governance gate); the member-assignee + viewer denies are the SC-016 cases the spec names; the event assertion proves the slice-017 seam.

---

## Resolved unknowns summary

| Plan unknown | Resolved by |
|---|---|
| Assignees persistence (join table vs column) + migration/FR-051 | R1, R9 |
| Command surface (whole-set vs add/remove) | R2 |
| `TaskAssigned` event shape (delta + actor, idempotent, self-suppress) | R3 |
| Authorization (shared-only, editor/owner, member-assignee, provenance-only) | R4 |
| Cleanup on leave/remove/unshare + account deletion | R5 |
| "Assigned to me" query scoping | R6 |
| Read model (`TaskResponse.assignees`; ids not names) | R7 |
| Error contract (no new code) | R8 |
| Web surface (picker + assigned view, optimistic) | R10 |
| Test surface incl. SC-016 deny matrix + event assertion | R11 |

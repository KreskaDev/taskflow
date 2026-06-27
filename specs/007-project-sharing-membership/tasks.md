---
description: "Task list for Project Sharing, Membership & Roles (slice 007)"
---

# Tasks: Project Sharing, Membership & Roles

**Input**: Design documents from `/specs/007-project-sharing-membership/`
**Prerequisites**: plan.md, spec.md, research.md (R1–R17), data-model.md, contracts/openapi.yaml, quickstart.md (all present)

**Tests**: REQUIRED. Constitution v4.0.0 **Principle IX (Authn/Authz) is OWNED and central to this slice** — the
shared-project membership + role branch is the deliverable. The governance gate requires **every new data handler to
ship an allow AND a deny test**, **plus** this slice ships the **role × operation deny matrix** (SC-016) as a
first-class test artifact. Authorization changes require a **non-author reviewer** before merge. Test-First is
Red-Green-Refactor: **the test task has a LOWER id than the implementation it covers — write it, watch it fail (RED),
then implement (GREEN).**

**Organization**: This slice is the **origin of the shared-project authorization branch** and the first entity
migration since slice-004 `AddProjects` (slice 005 was a named no-op) → **FR-051 backup-before-migration is LIVE**.
The work splits into a **Foundational** layer (the `ProjectMembership` entity + the `Project` sharing behaviors + the
dispatch-by-visibility authorization policy + the migration + repository — blocking the story) and **one story**:
- **US-12 — Project Sharing, Membership & Roles (US-12, P1)**: share/unshare (reversible), invite-by-email, change
  role, transfer ownership, remove member, leave — all confirmation-gated with a blast-radius preview (FR-064), with
  the membership + role authorization branch dispatched by `Project.Visibility` (FR-065/066/067/068).

This slice realizes the reserved-but-unwritable **`shared`** visibility value (slice 004 froze `Project.Visibility`
at `personal`) and the **transfer-owner** command (FR-094). **No new error code** (R16): `forbidden` (403, insufficient
role) and `last_owner` (422, last-owner guard) were pre-provisioned in slice 004 and are **first used** here;
`not_found`/`validation_failed`/`version_conflict` are reused. The **"unassign from tasks"** half of FR-059/062/063
is a named slice-008 seam (`Task.assignees` does not exist yet) — this slice performs + tests the **access-revocation**
half and **raises** the events slice 008/016/017 consume.

## Format: `[ID] [P?] [Story] Description`
- **[P]**: Can run in parallel (different files, no dependency on an incomplete task)
- **[Story]**: `[US-12]` for story-phase tasks (Setup/Foundational/Polish carry no story label)

## Path Conventions
Monorepo (per plan.md): backend `apps/api/src/<Project>/…`, backend tests `apps/api/tests/<Project>/…`,
web `apps/web/src/…`, web tests `apps/web/tests/…`.

---

## Phase 1: Setup (Preconditions + frozen role-token surface)

- [ ] T001 [P] Confirm slice-007 preconditions: the slice-004 `projects` table + `visibility` column exist (`AddProjects` migration) with **`visibility` frozen at `personal`** (this slice makes the `shared` value writable, no DDL); `Project.OwnerId` is the ownership anchor and the `Version`/`version_conflict` + client-id/idempotent-insert patterns from `Project.cs` are the templates to mirror; the **ownership** authorization branch lives in `apps/api/src/TaskFlow.Application/Authorization/ResourceAuthorizationPolicy.cs` and `IResourceAuthorizationPolicy.cs` (the seam whose doc already names "membership + role — added in slice 007"); the error codes `forbidden` (403) **and** `last_owner` (422) already exist in `apps/api/src/TaskFlow.Api/OpenApi/TaskFlowDocumentTransformer.cs` (`ErrorCodes` array) **and** the web `ERROR_UX satisfies Record<ErrorCode, ErrorUx>` map in `apps/web/src/lib/api/client.ts` (so reuse leaves the exhaustiveness gate green with **no change**, R16); the `Domain/TaskManagement/Events/` folder + Wolverine transactional outbox exist (the event-raising substrate); `apps/web/src/components/projects/DeleteProjectDialog.tsx` established the blast-radius confirmation-dialog shape to mirror.
- [ ] T002 [P] **[FROZEN ROLE-TOKEN SURFACE — R2]** Freeze the two role vocabularies the validators (T039) and the OpenAPI contract assert verbatim: the **stored/writable** `MembershipRole` = `editor | viewer` (the closed enum on `project_memberships.role` + every invite/change-role payload — `owner` is **never** representable), and the **effective/read** `EffectiveRole` = `owner | editor | viewer` (composed at read time, `owner` derived from `ownerId`). Define the authoritative `editor|viewer` constant/enum on the API side (used by the FluentValidation role rule + the EF `role` CHECK) and mirror the writable set in `apps/web/src/lib/validation/membership.ts`'s source enum. `owner` is unreachable as a write value (Principle VI — the illegal "promote to owner" state is unrepresentable).

---

## Phase 2: Foundational (blocking prerequisites for US-12)

**Purpose**: the `ProjectMembership` entity, the `Project` sharing behaviors + the last-owner guard, the
**dispatch-by-visibility** authorization policy, the four domain events, the persistence + migration + repositories,
and the read-model delta — required by every command/query in the story. **This is the centre of the slice
(Principle IX).**

- [ ] T003 [P] Create `apps/api/src/TaskFlow.Domain/TaskManagement/ProjectMembershipId.cs` — strongly-typed UUIDv7 id with EF value conversion (mirror `ProjectId`/`TaskId`).
- [ ] T004 [P] Create the four membership/sharing domain events in `apps/api/src/TaskFlow.Domain/TaskManagement/Events/` — `ProjectShared.cs`, `ProjectUnshared.cs`, `OwnerTransferred.cs`, `MembershipRevoked.cs` (pure-ID payload records; raised via the Wolverine transactional outbox, consumed by slices 008/016/017 — R13). These are the **authority signal** later slices subscribe to; this slice raises but does not consume them.
- [ ] T005 Create `apps/api/src/TaskFlow.Domain/TaskManagement/ProjectMembership.cs` — the `ProjectMembership` entity (within the `Project` aggregate per ADR-0003): `Id`/`ProjectId`/`UserId`/`Role`(`editor|viewer`)/`CreatedAt`/`UpdatedAt`; near-anemic data holder (no own concurrency token — the Project `version` guards it, R1/R11; exercised via the integration + policy tests, not a standalone unit test) (depends on T003).
- [ ] T006 **(write first — RED)** Create `apps/api/tests/TaskFlow.UnitTests/Domain/TaskManagement/ProjectSharingTests.cs` — `Share(utcNow)` is valid only from `personal` → sets `Visibility="shared"`, bumps `Version`, raises `ProjectShared`; `Unshare(utcNow)` flips back to `personal`, raises `ProjectUnshared` (membership-row removal is the handler's job, R3); `TransferOwnerTo(newOwner, utcNow)` reassigns `OwnerId`, bumps `Version`, raises `OwnerTransferred`; the pure static guard `EnsureNotLastOwner(project, target)` throws `last_owner` when `target == OwnerId` and is a no-op otherwise (covers T007).
- [ ] T007 Modify `apps/api/src/TaskFlow.Domain/TaskManagement/Project.cs` — add `Share(DateTime utcNow)` / `Unshare(DateTime utcNow)` (the first legal `Visibility="shared"` write + back), `TransferOwnerTo(UserId newOwner, DateTime utcNow)` (the only legal `OwnerId` mutation), and the pure static guard `EnsureNotLastOwner(Project, UserId)` (mirrors slice-004 `EnsureNestingAllowed`); each behavior stamps `UpdatedAt`/bumps `Version` via `Touch()` and raises its event (R3,R6,R7,R13; depends on T004; RED via T006).
- [ ] T008 **(write first — RED)** Create `apps/api/tests/TaskFlow.UnitTests/Authorization/ResolveEffectiveRoleTests.cs` (new folder) — `ResolveEffectiveRole(project, memberships, caller)` returns `owner` when `caller == ownerId` (no row lookup), `editor`/`viewer` from the caller's row, `none` when neither; **dispatch-by-visibility**: `personal` → ownership branch, `shared` → membership+role branch (NOT a conjunction, R8); `RequireRole` deny shapes — **viewer attempting a write is denied at the policy contract (403)** (the consuming task-write handler arrives slice 008; the policy contract is asserted HERE — data-model §3), editor attempting manage denied (403), non-member denied (→ the 404 mapping), last-owner-targeted leave/remove/demote → `last_owner` (covers T009).
- [ ] T009 Modify `apps/api/src/TaskFlow.Application/Authorization/IResourceAuthorizationPolicy.cs` + `ResourceAuthorizationPolicy.cs` — add `ResolveEffectiveRole(Project, IReadOnlyCollection<ProjectMembership>, UserId) → owner|editor|viewer|none` and `RequireRole(project, memberships, caller, RequiredRole)` that **dispatches on `Visibility`** (personal → the existing `RequireOwnership`; shared → the membership+role branch) and throws per the deny-shape rule (**non-member → 404 `not_found`**, **insufficient-role member → 403 `forbidden`**, R8/R9); the slice-004 ownership method is reused unchanged as the `personal` arm (depends on T005; RED via T008).
- [ ] T010 [P] Create `apps/api/src/TaskFlow.Application/TaskManagement/MemberResponse.cs` — read model `{ userId, displayName, role (effective), isOwner }`; **NO email** (Constitution XI — R17).
- [ ] T011 [P] Modify `apps/api/src/TaskFlow.Application/TaskManagement/ProjectResponse.cs` — add a nullable effective **`role`** (`owner|editor|viewer`); still **hides `ownerId`/`deletedAt`** (the slice-004 leak rule); `From(Project, EffectiveRole)` projection (R17).
- [ ] T012 [P] Create `apps/api/src/TaskFlow.Application/TaskManagement/IProjectMembershipRepository.cs` — `ListByProjectAsync(projectId)` / `FindAsync(projectId, userId)` / `ListProjectIdsForUserAsync(userId)` / `Add` / `Remove` / `RemoveAllForProjectAsync(projectId)`, loaded transactionally alongside the `Project` aggregate (one-aggregate-per-transaction, ADR-0003 — R1/R15).
- [ ] T013 Modify `apps/api/src/TaskFlow.Application/TaskManagement/IProjectRepository.cs` — add the member-scoped read path (a `FindReadableAsync`/shared-lookup find) alongside the slice-004 owner-scoped finds, so a shared project is loadable for a member (not only its owner), feeding the visibility dispatch (R8/R17).
- [ ] T014 Create `apps/api/src/TaskFlow.Infrastructure/Persistence/Configurations/ProjectMembershipConfiguration.cs` — `project_memberships` table, snake_case columns (R15 types/nullability), **UNIQUE `(project_id, user_id)`** (`ux_project_memberships_project_user`), index `ix_project_memberships_user_id` on `(user_id)`, `project_id→projects(id)` **CASCADE**, `user_id→users(id)` **CASCADE**, `role` CHECK `IN ('editor','viewer')`; **no nav collection on `Project`** (the slice-004 no-nav-prop style — R1); **no DDL on `projects`** (depends on T005).
- [ ] T015 Implement `apps/api/src/TaskFlow.Infrastructure/Persistence/ProjectMembershipRepository.cs` — `IProjectMembershipRepository` over EF; translate `DbUpdateConcurrencyException → VersionConflictException` and unique-violation at `SaveChangesAsync` (mirror `ProjectRepository`) (depends on T012, T014).
- [ ] T016 Generate the EF migration: `dotnet ef migrations add AddProjectMemberships` → creates `project_memberships` (+ unique + `(user_id)` index + both CASCADE FKs + `role` CHECK); **review** it is forward-only/expand-contract (purely additive — no row rewrite; the `shared` value was already a legal column value, only now written) and that **`projects` is NOT touched** (no DDL) (depends on T014).
- [ ] T017 Verify **FR-051** is satisfied for `AddProjectMemberships` (the first entity migration since slice-004 `AddProjects`; slice 005 was a no-op): the automatic pre-migration backup + the CI restore-test gate (Constitution VII) MUST actually run against it — confirm the gate exists and is **green**; **wire it if absent** (in-scope per plan Complexity Tracking) (depends on T016).

---

## Phase 3: User Story 12 — Project Sharing, Membership & Roles (Priority: P1) 🎯

**Goal**: An owner converts a personal project to **shared** (and back, reversibly), **invites members by email** at an
assignable editor/viewer role, **changes** a member's role, **transfers ownership** (demoting the prior owner to
editor), **removes** a member, and a non-owner member may **leave** — every membership/role/visibility mutation
**confirmation-gated with a blast-radius preview, NOT under the 30-second undo** (FR-064). Access to a shared project's
data **requires current membership** dispatched by `Visibility`; leave/remove/unshare **revokes ALL access**
immediately (FR-066); each op requires a sufficient role (viewer=read, editor=write, owner=manage), deny-by-default
(FR-067/068).

**Independent Test**: Share a personal project; invite B (editor) + C (viewer); a viewer is denied a write (policy
contract 403); a non-member X is denied a read (404); change C to editor; transfer ownership A→B (A demoted to
editor); remove a member → their next read 404; a member leaves → their next read 404; the owner cannot
leave/be-removed/be-demoted (422 `last_owner`); unshare → personal again, all members lose access, tasks preserved;
re-share round-trips with zero members.

### Backend — tests FIRST (RED), then the command verticals (each ships an ALLOW + a DENY test)

- [ ] T018 [P] [US-12] **(write first — RED)** Create `apps/api/tests/TaskFlow.IntegrationTests/TaskManagement/ShareUnshareProjectTests.cs` (Testcontainers-Postgres) — share `personal→shared` (first writable `shared`, A's `role=owner`, zero members); unshare `shared→personal` **removes ALL membership rows + revoke-all** (a former member's next read → 404) with owner + tasks retained; raises `ProjectShared`/`ProjectUnshared` to the outbox; **ALLOW** (owner) + **DENY** (share by a non-owner → **404**; unshare by an insufficient-role member → **403**, by a non-member → **404**); stale `version` → **409** (covers T019, T020).
- [ ] T019 [US-12] Create `apps/api/src/TaskFlow.Application/TaskManagement/ShareProject.cs` (+ `ShareProjectRequest.cs` — version-only body; command + validator + owner-only handler raising `ProjectShared`) **and** add `Share` to `apps/api/src/TaskFlow.Api/Endpoints/ProjectEndpoints.cs` (`PATCH /api/projects/{id}/share`) (depends on T007, T009, T011, T015; RED via T018).
- [ ] T020 [US-12] Create `apps/api/src/TaskFlow.Application/TaskManagement/UnshareProject.cs` (+ `UnshareProjectRequest.cs`) — owner-only; `Unshare` + `RemoveAllForProjectAsync` in one transaction (owner + tasks retained); raises `ProjectUnshared`; `version` — **and** `ProjectEndpoints.Unshare` (`PATCH /api/projects/{id}/unshare`) (depends on T007, T009, T015; RED via T018).
- [ ] T021 [P] [US-12] **(write first — RED)** Create `apps/api/tests/TaskFlow.IntegrationTests/TaskManagement/InviteMemberTests.cs` — invite an admitted User (not yet a member) at editor/viewer → row created; **unknown email → 422** (`validation_failed` on `email`, no pending record, OOS-18); **self-invite (owner) → 422**; **duplicate member → 422** (distinct field messages, one shape — R4); **ALLOW** (owner) + **DENY** (editor/viewer caller → **403**, non-member → **404**); stale `version` → **409** (covers T022).
- [ ] T022 [US-12] Create `apps/api/src/TaskFlow.Application/TaskManagement/InviteMember.cs` (+ `InviteMemberRequest.cs` + `InviteMemberValidator` for email + `MembershipRole` shape) — owner-only; resolve email→User server-side (exact, case-normalized); unknown/self/dup → 422; `version` — **and** create `apps/api/src/TaskFlow.Api/Endpoints/MemberEndpoints.cs` (new file, `POST /api/projects/{id}/members`) (depends on T009, T015; RED via T021).
- [ ] T023 [P] [US-12] **(write first — RED)** Create `apps/api/tests/TaskFlow.IntegrationTests/TaskManagement/ChangeMemberRoleTests.cs` — `editor↔viewer` toggle; re-sending the current role is a **no-op + version bump**, not an error (R5); **target == owner → 422 `last_owner`** (checked before the row lookup, R7); target who is neither owner nor member → **404**; **ALLOW** + **DENY** (member caller → 403, non-member → 404); stale `version` → **409** (covers T024).
- [ ] T024 [US-12] Create `apps/api/src/TaskFlow.Application/TaskManagement/ChangeMemberRole.cs` (+ `ChangeMemberRoleRequest.cs` + validator) — owner-only; `EnsureNotLastOwner` precedes the row lookup; set member `role`; `version` — **and** `MemberEndpoints.ChangeRole` (`PATCH /api/projects/{id}/members/{userId}`) (depends on T007, T009, T015; RED via T023).
- [ ] T025 [P] [US-12] **(write first — RED)** Create `apps/api/tests/TaskFlow.IntegrationTests/TaskManagement/RemoveMemberTests.cs` — remove a member → the **removed user loses ALL access** (their next read of project/tasks/members → **404**, R10) + raises `MembershipRevoked`; **target == owner → 422 `last_owner`** (before the row lookup, R7); target neither owner nor member → **404**; **ALLOW** + **DENY** (member caller → 403, non-member → 404); stale `version` → **409** (covers T026).
- [ ] T026 [US-12] Create `apps/api/src/TaskFlow.Application/TaskManagement/RemoveMember.cs` — owner-only; `EnsureNotLastOwner` first; delete the member row → revoke-all; raises `MembershipRevoked`; **`version` carried as a query param** (DELETE bodies, slice-004 posture) — **and** `MemberEndpoints.Remove` (`DELETE /api/projects/{id}/members/{userId}?version=`) (depends on T007, T009, T015; RED via T025).
- [ ] T027 [P] [US-12] **(write first — RED)** Create `apps/api/tests/TaskFlow.IntegrationTests/TaskManagement/LeaveProjectTests.cs` — a non-owner member leaves → loses ALL access (next read → **404**, R10) + raises `MembershipRevoked`; **caller == owner → 422 `last_owner`** (before the row lookup — the owner has no row, R7); non-member caller → **404**; **ALLOW** (member self) + **DENY** (non-member → 404); stale `version` → **409** (covers T028).
- [ ] T028 [US-12] Create `apps/api/src/TaskFlow.Application/TaskManagement/LeaveProject.cs` — any **non-owner** member self-service; `EnsureNotLastOwner(caller)` first; delete the caller's own row → revoke-all; raises `MembershipRevoked`; **`version` query param** — **and** `MemberEndpoints.Leave` (`DELETE /api/projects/{id}/membership?version=`) (depends on T007, T009, T015; RED via T027).
- [ ] T029 [P] [US-12] **(write first — RED)** Create `apps/api/tests/TaskFlow.IntegrationTests/TaskManagement/TransferOwnershipTests.cs` — `ownerId` moves to a named current member, the **new owner's row is removed**, the **prior owner is demoted to a new `editor` row**, raises `OwnerTransferred` (R6); **transfer to a non-member or the current owner → 422 `validation_failed`**; **ALLOW** (owner) + **DENY** (member caller → 403, non-member → 404); stale `version` → **409** (covers T030).
- [ ] T030 [US-12] Create `apps/api/src/TaskFlow.Application/TaskManagement/TransferOwnership.cs` (+ `TransferOwnershipRequest.cs` + validator) — owner-only; in one transaction remove the target's row, `TransferOwnerTo`, insert an `editor` row for the prior owner; target must be a current member; raises `OwnerTransferred`; `version` — **and** `ProjectEndpoints.TransferOwner` (`PATCH /api/projects/{id}/owner`) (depends on T007, T009, T015; RED via T029).
- [ ] T031 [P] [US-12] **(write first — RED)** Create `apps/api/tests/TaskFlow.IntegrationTests/TaskManagement/GetProjectMembersTests.cs` — the composed roster: **owner (from `ownerId`, `isOwner=true`) ∪ the editor/viewer rows**; surfaces the project `version`; **no email leak** (R17); member-only (**non-member → 404**); **ALLOW** (any member viewer+) + **DENY** (non-member → 404) (covers T032).
- [ ] T032 [US-12] Create `apps/api/src/TaskFlow.Application/TaskManagement/Queries/GetProjectMembers.cs` (compose owner ∪ rows; surface `Project.version`; member-only) **and** `MemberEndpoints.List` (`GET /api/projects/{id}/members`) (depends on T009, T010, T015; RED via T031).
- [ ] T033 [P] [US-12] **(write first — RED)** Create `apps/api/tests/TaskFlow.IntegrationTests/TaskManagement/RoleOperationDenyMatrixTests.cs` — **THE SC-016 deny matrix** as first-class tests, asserted through the real handlers: **editor-denied-manage** (editor → invite/change-role/remove/unshare/transfer/**delete** → 403), **non-member-denied-read** (X → project/tasks/members → 404), **removed-member-loses-access** (a once-member, after removal → 404), **last-owner-guard** (leave/remove/change-role targeting the owner → 422). *(viewer-denied-write is asserted at the **policy contract** in T008 — the task-write handler that consumes it on shared projects arrives slice 008; data-model §3.)* This file stays RED until the policy (T009) + the covered read/manage handlers (T035/T036) land (covers T035, T036).
- [ ] T034 [P] [US-12] **(write first — RED)** Create `apps/api/tests/TaskFlow.IntegrationTests/TaskManagement/SharedProjectReadAccessTests.cs` — a shared project's tasks are **readable by any current member** (viewer+ allow); **non-member → 404**; `GetMyProjects` **includes shared projects the caller is a member of** and **populates `ProjectResponse.role`** (owner/editor/viewer); **ALLOW** + **DENY** (covers T035).
- [ ] T035 [US-12] Modify `apps/api/src/TaskFlow.Application/TaskManagement/Queries/GetProjectTasks.cs` (dispatch by visibility — on shared, `RequireRole(viewer)`; non-member → 404; personal arm unchanged) **and** `Queries/GetMyProjects.cs` (include shared projects via `ListProjectIdsForUserAsync` + populate `ProjectResponse.role`, R8/R17) (depends on T009, T011, T013, T015; RED via T034, T033).
- [ ] T036 [US-12] Modify `apps/api/src/TaskFlow.Application/TaskManagement/DeleteProject.cs`, `EditProject.cs`, `ArchiveProject.cs`, `UnarchiveProject.cs` — add the **manage-op visibility dispatch**: on a `shared` project these are owner-only (an editor/viewer member → **403**); the **personal arm is the unchanged slice-004 ownership branch** (R8/R9 matrix) (depends on T009, T015; RED via T033).
- [ ] T037 [US-12] Stamp the new operationIds (`shareProject`/`unshareProject`/`transferProjectOwnership`/`listProjectMembers`/`inviteProjectMember`/`changeProjectMemberRole`/`removeProjectMember`/`leaveProject`) + auto-insert their 401/403/404/409/422 responses in `apps/api/src/TaskFlow.Api/OpenApi/TaskFlowDocumentTransformer.cs` (**no `ErrorCodes` change** — `forbidden`/`last_owner` already present, R16), then regenerate the typed client: `cd apps/web && pnpm gen:api` (API on `localhost:4311`) → `schema.d.ts` gains the members ops + `MembershipRole`/`EffectiveRole`/`MemberResponse`/`MembersResponse`/`ProjectResponse.role`/`visibility="shared"`; `pnpm typecheck` green; confirm it matches `contracts/openapi.yaml`; commit (depends on T019, T020, T022, T024, T026, T028, T030, T032, T035, T036).

### Web — tests FIRST (RED), then impl (NON-optimistic, confirmation-gated — R12)

- [ ] T038 [P] [US-12] **(write first — RED)** Create `apps/web/tests/unit/membership-validation.test.ts` — `inviteSchema` (email shape + `role` **enum `editor|viewer`**, `owner` rejected), `changeRoleSchema`, `transferSchema` (target userId + version) (covers T039).
- [ ] T039 [US-12] Create `apps/web/src/lib/validation/membership.ts` — `inviteSchema` + `changeRoleSchema` + `transferSchema` over the writable `editor|viewer` enum; export inferred types (depends on T002, T037; RED via T038).
- [ ] T040 [P] [US-12] **(write first — RED)** Create `apps/web/tests/unit/use-membership-mutations.test.ts` — the **non-optimistic** family on the `['projects', id, 'members']` key: each mutation **invalidates on settle** (no snapshot/rollback — distinct from the slice-002/004 optimistic recipe, R12); `last_owner`/`forbidden`/`validation_failed`/`version_conflict` surface the FR-049 message; the request body/params carry the right fields (`version` query param for remove/leave) (covers T041, T042).
- [ ] T041 [US-12] Create `apps/web/src/hooks/useProjectMembers.ts` — the roster query hook on `['projects', id, 'members']` (depends on T037).
- [ ] T042 [US-12] Create `apps/web/src/hooks/useMembershipMutations.ts` — the seven **non-optimistic, confirmation-gated** mutations (share/unshare/invite/change-role/remove/leave/transfer) invalidating `['projects', id, 'members']` (and `['projects']` on share/unshare/transfer) on settle (depends on T037, T039; RED via T040).
- [ ] T043 [P] [US-12] **(write first — RED)** Create `apps/web/tests/unit/members-roster.test.ts` — role badges (role **text + icon**, never color alone — FR-044); **role-aware gating** (a viewer sees read-only controls; only the owner sees manage actions); the owner entry renders with `isOwner` (covers T044, T045).
- [ ] T044 [US-12] Create `apps/web/src/components/projects/ShareProjectDialog.tsx` (confirm share/unshare; unshare **states blast radius** — which members lose access, FR-064), `MembersDialog.tsx` (the roster: role badges + change-role/remove/transfer/leave entry points; role-aware gating), and `InviteMemberForm.tsx` (invite-by-email input with **single-key suppression** FR-031 + role picker; surfaces the unknown-email FR-049 message) — all on the dialog focus contract (FR-101) (depends on T041, T042; RED via T043).
- [ ] T045 [US-12] Create `apps/web/src/components/projects/TransferOwnershipDialog.tsx` (pick a current member; blast radius "you become an editor", R6), `RemoveMemberDialog.tsx` (blast radius: member loses access + will be unassigned, FR-064), and `LeaveProjectDialog.tsx` (blast radius: you lose access; the last-owner-guard message, R7) — all confirmation-gated on the dialog focus contract (depends on T041, T042; RED via T043).
- [ ] T046 [US-12] Modify `apps/web/src/components/layout/Sidebar.tsx` — a **shared-visibility indicator** on each shared project + a **keyboard-reachable share/members entry point** on each owned project that opens `ShareProjectDialog`/`MembersDialog` (and through the roster, the transfer/remove/leave dialogs). **Wire the triggers** so the dialogs are reachable from the app shell (do NOT ship them unmounted — avoid the slice-004 decomposition gap that needed a remediation phase) (depends on T044, T045).
- [ ] T047 [US-12] **(write first — RED, then GREEN)** Create `apps/web/tests/e2e/sharing.spec.ts` (Playwright) — **AS-01..AS-06** + invite-unknown-email (422) + viewer-read-only + last-owner-guard (422) + removed-member-loses-access (404) + the `personal↔shared` reversibility round-trip — all **live** against the wired DOM (no `test.fixme`) (depends on T046).

**Checkpoint**: US-12 — a personal project can be shared and re-personalized reversibly; members invited by email at
editor/viewer; roles changed; ownership transferred (prior owner → editor); members removed; non-owners leave; every
mutation confirmation-gated with a blast-radius preview; the membership + role authorization branch is enforced
deny-by-default with the SC-016 deny matrix green.

---

## Phase 4: Polish & Cross-Cutting Concerns

- [ ] T048 [P] Accessibility pass (Principle II): the share, members-roster, invite, change-role, transfer, remove, and leave dialogs follow the **dialog focus contract** (FR-101); role badges carry **role text + icon, never color alone**, contrast ≥ 4.5:1 (FR-044); no hover-only affordances (FR-046); transitions instant/<100 ms under `prefers-reduced-motion` (FR-047); no AT-binding collision (FR-045); single-key suppression in the **invite-email** input (FR-031); membership/access-change toasts announce via the **polite ARIA-live region without stealing focus** (FR-101).
- [ ] T049 [P] Security/privacy/logging (Principle XI/XII): the invite-by-email value is validated/sanitized at the trust boundary (Zod + FluentValidation), resolved server-side; the members roster surfaces `displayName` + `role`, **never emails**; `displayName`/role are React-escaped on render (FR-099); FR-050 structured rejection logs carry `ErrorCode`/`Method`/`Path` only — **never email/displayName/owner**; the unknown/duplicate/self invite cases share **one 422 field-error shape** (no enumeration oracle, R4).
- [ ] T050 [P] Confirm **FR-064**: invite/role-change/transfer/remove/leave/unshare are confirmation-gated, **non-optimistic** (`['projects', id, 'members']` invalidate-on-settle, **no undo toast** — R12), and each confirmation dialog **states its blast radius**; confirm `ProjectResponse.role` drives UI gating **only** — disabling the UI gate still yields a server 403/404 (Principle V/FR-068).
- [ ] T051 Confirm CI gates green: `pnpm gen:api` clean (members ops + `MembershipRole`/`EffectiveRole`/`ProjectResponse.role` present); the `ERROR_UX satisfies Record<ErrorCode, ErrorUx>` map stays exhaustive with **NO change** (`forbidden`/`last_owner` pre-existing, R16); TS strict + C# nullable/analyzers-as-errors; **every** new data handler has an **allow + a deny** test **and** the role × operation deny matrix is green (Principle VIII/IX, SC-013/SC-016); authorization changes carry a **non-author review** (governance gate).
- [ ] T052 Confirm **exactly one** migration (`AddProjectMemberships`) in the slice diff with **no DDL on `projects`** (the `shared` value was already a legal column value); confirm the **FR-051** backup-before-migrate + restore-test gate executed **green** (the first entity migration since slice 004).
- [ ] T053 Run `specs/007-project-sharing-membership/quickstart.md` validation scenarios end-to-end (A share & invite, B the role matrix, C transfer, D last-owner guard, E remove/leave revoke-all, F unshare & reversibility, G authorization coverage, the server-validation table, and the cross-cutting checks — all rows).
- [ ] T054 Confirm the **forward seams** are named, raised, and not over-built: the membership mutations **raise** `MembershipRevoked`/`ProjectUnshared`/`OwnerTransferred`/`ProjectShared` to the outbox (the authority signal); the **assignment-clearing** handler ships with **slice 008** (`Task.assignees` does not exist — this slice's access-revocation half is real + tested); **real-time eviction** (FR-095) is **slice 016**; **notifications** are **slice 017** — no machinery for those is pulled forward (R13/R14).

---

## Dependencies & Execution Order

### Phase dependencies
- **Setup (P1)**: T001 ‖ T002 — start immediately (T002 freezes the role-token surface the validators/contract assert).
- **Foundational (P2)**: depends on Setup. **Blocks the story** — the `ProjectMembership` entity, the `Project` sharing behaviors + last-owner guard, the dispatch-by-visibility policy, the four events, the migration, repositories, and read-model delta. **This is the centre of the slice (Principle IX).**
- **US-12 (P3)**: depends on Foundational. The full sharing/membership/role journey + the SC-016 deny matrix.
- **Polish (P4)**: depends on US-12.

### Test-First ordering (Red-Green) — strict lower-id within each phase
- Foundational: T006 (`ProjectSharingTests`) precedes T007 (`Project` behaviors); T008 (`ResolveEffectiveRoleTests`) precedes T009 (the policy dispatch).
- US-12 backend: T018→T019/T020, T021→T022, T023→T024, T025→T026, T027→T028, T029→T030, T031→T032 (each integration test precedes its command/query). **The SC-016 matrix T033 has a LOWER id than the read-access dispatch T035 and the manage-op dispatch T036 it drives** (a consolidating deny suite — RED until the policy + covered handlers land); T034→T035.
- US-12 web: T038→T039, T040→T041/T042, T043→T044/T045; T047 is the E2E (RED then GREEN).

### Critical path
T002 → T003/T004 → T005 → T007 (RED T006) → **T009 (RED T008) [the policy — the centre]** → T013/T014 → T015/T016 → T017 → (US-12 commands T018..T032) → **T033/T034 → T035/T036 (the visibility dispatch onto the slice-004 queries/manage commands)** → **T037 gen:api** → (US-12 web T038..T046) → T047 E2E → Polish.
The single `gen:api` join (T037) is the backend→client serialization point (web depends on **all** backend handlers).

### Parallel opportunities
- **Setup**: T001 ‖ T002.
- **Foundational**: T003 ‖ T004; T006 ‖ T008 (RED test files); then T010 ‖ T011 ‖ T012 after T005/T009.
- **US-12 backend**: the RED test files T018 ‖ T021 ‖ T023 ‖ T025 ‖ T027 ‖ T029 ‖ T031 ‖ T033 ‖ T034 (different files). The command verticals touch different command files but **share `ProjectEndpoints.cs` (share/unshare/transfer) and `MemberEndpoints.cs` (invite/change-role/remove/leave/list)** — serialize the edits to each endpoint file. T036 touches four slice-004 command files — serialize against any other edit to them.
- **US-12 web**: T038 ‖ T040 ‖ T043 (test files); T044 ‖ T045 after T041/T042 (different component files).
- **Polish**: T048 ‖ T049 ‖ T050.

### Migration & contract gates
- **One** migration only (`AddProjectMemberships`, T016); a second new file under `Persistence/Migrations/` is a defect (T052); **no DDL on `projects`**.
- **FR-051 is live** (T017/T052) — the backup + restore-test gate must run against `AddProjectMemberships`.
- **One** `gen:api` regen (T037), CI-diff-gated; `ERROR_UX` unchanged (T051) — `forbidden`/`last_owner` are first **used**, not added.

---

## Implementation Strategy

### MVP
Foundational (the `ProjectMembership` entity + the **dispatch-by-visibility authorization policy** + migration) →
US-12 backend (the seven commands + the members query + the read/manage visibility dispatch + the SC-016 matrix) →
**T037 gen:api** → US-12 web (the dialogs + the non-optimistic membership mutations + the wired Sidebar entry points)
→ **STOP & VALIDATE** the quickstart (share/invite, the role matrix, transfer, last-owner guard, revoke-all,
reversibility) → Polish. The authorization policy + the SC-016 deny matrix are the load-bearing increment — they are
the access foundation slices 008/009/016/017 build on.

### Notes
- **Authorization is the heart of this slice** (Principle IX — OWNED): deny-by-default, **dispatched by `Project.Visibility`** (FR-065), NOT a conjunction of tiers; every new data handler ships an **allow AND a deny** test, **plus** the **role × operation deny matrix** (SC-016) as a first-class suite. **Authorization changes require a non-author reviewer** (governance gate).
- **Deny-shape rule** (R9): **non-member → 404 `not_found`** (existence not disclosed across the membership boundary); **insufficient-role member → 403 `forbidden`** (the member knows it exists); **last-owner-targeted → 422 `last_owner`** (recoverable — "transfer first", checked **before** the membership-row lookup, R7).
- **Test-First is mandatory** (Constitution VIII): the test task has a lower id than the impl it covers — RED, then GREEN.
- **Two role vocabularies** (R2): the **writable** `MembershipRole` = `editor|viewer` (the illegal "promote to owner" state is unrepresentable); the **read** `EffectiveRole` = `owner|editor|viewer` (`owner` derived from `ownerId`, never a stored row). Ownership moves **only** via the transfer command (FR-094), which demotes the prior owner to editor.
- **Confirmation-gated, non-optimistic** (R12, FR-064): membership/role/visibility mutations take effect only on the confirmed round-trip — a new `useMembershipMutations` family **invalidates on settle** (no snapshot/rollback, no undo toast), a deliberate departure from the slice-002/004 optimistic recipe; each confirmation dialog **states its blast radius** (Principle VII).
- **Revoke-ALL** (R10, FR-066): leave/remove/unshare revokes ALL access immediately, computed from **current** membership at request time — `createdBy`/assignee are **provenance only**. The **access**-revocation half is real + tested here; the **assignment-clearing** half is the slice-008 seam.
- **One migration; FR-051 LIVE** (R15): `AddProjectMemberships` (no DDL on `projects`); the backup + restore-test gate must run green.
- **No new error code** (R16); **no NodaTime** (membership timestamps are plain UTC `timestamptz`; no new date-relative computation).

# Implementation Plan: Project Sharing, Membership & Roles

**Branch**: `007-project-sharing-membership` | **Date**: 2026-06-27 | **Spec**: `specs/007-project-sharing-membership/spec.md`

**Input**: Feature specification from `specs/007-project-sharing-membership/spec.md`

## Summary

Turns a personal project into a **shared, multi-member** project. An owner converts a personal project to **shared** and back to **personal** reversibly (US-12.AS-01/AS-06, FR-058), **invites members by email** — resolved against existing admitted Users only (US-12.AS-01, FR-060, OOS-18) — at an assignable **editor / viewer** role (FR-061), **changes** a member's assignable role between editor and viewer (AS-02, FR-062), **transfers ownership** via an explicit command that demotes the prior owner to editor (FR-094), **removes** a member (AS-04, FR-062), and a non-owner member may **leave** (AS-05, FR-063). A **viewer** is read-only and denied task writes (AS-03, FR-067). All six membership/role/visibility mutations are **confirmation-gated with a blast-radius preview and are NOT under the 30-second undo** (FR-064).

This slice is the **origin of the shared-project authorization branch** (Constitution Principle IX — the centre of the slice). It introduces **ENT-07 `ProjectMembership`** (a `project_memberships` table owned by the `Project` aggregate per ADR-0003), realizes the reserved-but-unwritable **`shared`** visibility value (slice 004 froze `Project.Visibility` at `personal`), and extends the slice-004 application-layer `IResourceAuthorizationPolicy` with a single **dispatch-by-visibility** entry point (FR-065): `personal` → the existing ownership branch; `shared` → the new **membership + role** branch. Access to a shared project's data **requires current membership** — `createdBy`/assignee are provenance only (FR-066) — and leave/remove/unshare **revokes ALL access immediately** (FR-066, R10). Every operation requires a sufficient role (viewer=read, editor=write, owner=manage, FR-067), deny-by-default at the handler layer (FR-068). The slice ships, as a first-class deliverable, the **role × operation deny matrix** (SC-016) plus an **allow and a deny** test per data handler (SC-013) — the deny tests this slice finally makes realizable, since the membership/role branch did not exist before.

It **ships a migration** (`AddProjectMemberships`) — the slice-004 `AddProjects` was the last entity migration, and slice 005 was a named no-op (research R15). Consequently **FR-051 backup-before-migration is LIVE**: the automatic pre-migration backup and the CI restore-test gate (Constitution VII) must actually execute against this migration. **No new error code** is added — `forbidden` (403, insufficient role) and `last_owner` (409, last-owner guard) were pre-provisioned in slice 004 and are **first used** here (R16).

What is **named but deferred**: the **"unassign from tasks"** half of FR-059/FR-062/FR-063 operates on `Task.assignees`, which does not exist until **slice 008** — this slice performs and tests the access-revocation half (real now), **raises** the `MembershipRevoked`/`ProjectUnshared` events, and names the assignment-clearing handler as the slice-008 seam (R13); **real-time subscription eviction** (FR-095) is **slice 016** (R14); **notifications** are slice 017; the **30-second undo UI** (FR-040) is slice 014 (and FR-064 deliberately carves these confirmation-gated actions out of it).

## Technical Context

**Language/Version**: TypeScript (strict) on Node.js 22 LTS / Next.js 15 (App Router), React 19; C# 13 on .NET 9 / ASP.NET Core.

**Primary Dependencies**:
- Frontend (NEW this slice): **none** — reuses TanStack Query v5 (a new **non-optimistic** `useMembershipMutations` family on a `['projects', id, 'members']` key — confirmation-gated, NOT the slice-002/004 optimistic+undo recipe, R12), Zod 3 (invite-email + role validation), openapi-fetch + openapi-typescript (regen), the slice-001 BFF proxy, the existing global ARIA-live announcer + `ERROR_UX` map (unchanged — R16).
- Backend (NEW this slice): **none** — reuses Wolverine + Wolverine.Http, EF Core + Npgsql, `WolverineFx.FluentValidation` (new validators on the membership commands), the `ProblemDetailsMiddleware`, the transactional outbox (for the `ProjectShared`/`ProjectUnshared`/`OwnerTransferred`/`MembershipRevoked` events, R13). **No NodaTime** (no new date-relative computation; membership timestamps are plain UTC `timestamptz` — Constitution X). **No new error code** (R16).

**Storage**: PostgreSQL 17. **One migration** (`AddProjectMemberships`): creates the `project_memberships` table — `project_id → projects(id) ON DELETE CASCADE`, `user_id → users(id) ON DELETE CASCADE`, `role` CHECK `IN ('editor','viewer')`, UNIQUE `(project_id, user_id)`, index on `(user_id)`. **No DDL on `projects`** — `visibility` already exists (slice-004 column); making the `shared` value writable is pure behavior. This slice migrates (the first entity migration since slice-004 `AddProjects`; slice 005 was a named no-op) → **FR-051 is LIVE** (Complexity Tracking).

**Testing**: xUnit (`Project` behavior — `Share`/`Unshare`/`TransferOwnerTo` pre-state guards + the `EnsureNotLastOwner` pure static guard; `ResolveEffectiveRole` composition over owner ∪ rows ∪ none); Testcontainers-Postgres integration (every command/query through the real DB **with an allow AND a deny authorization test** per handler — Constitution VIII/IX + the governance gate) **plus the role × operation deny matrix** as first-class tests (SC-016): viewer-denied-write, editor-denied-manage, non-member-denied-read (404), removed-member-loses-access (404), last-owner-guard (409); Vitest (membership validation, the non-optimistic `useMembershipMutations` surface, role-aware UI gating, members roster rendering); Playwright (E2E — AS-01..AS-06 + the edge cases: invite-unknown-email, viewer-read-only, last-owner-guard, removed-member-loses-access, reversibility round-trip).

**Target Platform**: Linux containers (Docker Compose) on Hetzner VPS; host Caddy for TLS. No infra change.

**Project Type**: Web application (monorepo: Next.js frontend + ASP.NET Core API).

**Performance Goals**: membership mutations are confirmation-gated (non-optimistic, R12), so the <16 ms optimistic-paint target does not apply — they invalidate the `['projects', id, 'members']` key on the confirmed round-trip; server single-aggregate writes p95 <200 ms (the per-request authorization lookup is a single indexed `(project_id, user_id)` probe, R15). The members roster and the per-project authorization lookup are both served by the `project_memberships` indexes.

**Constraints**: authorization is **deny-by-default, dispatched by `Project.Visibility`** (FR-065) — never a conjunction of tiers (R8); the **stored** role enum is `editor | viewer` only — `owner` is the immutable `ownerId`, never a stored row and never a writable role (R2); ownership moves **only** via the transfer command, which demotes the prior owner to editor (R6); the **last-owner guard** degenerates (single-owner model) to "any operation whose target is the `ownerId` is rejected with `last_owner`" (R7); **non-member → 404** (existence not disclosed across the membership boundary), **insufficient-role member → 403** (R9); the **Project `version`** is the single concurrency token for the whole sharing state (R11); **no new error code** (R16); the 30-second undo **UI** is deferred (FR-064 carves these out — Complexity Tracking).

**Scale/Scope**: ~10 users on a single shared instance (ASM-01/ASM-10); per-project membership sets are small (single digits); the owner is almost always the sole manager of a given project's membership (so single-token contention is negligible, R11). This slice adds one table, one aggregate-internal entity, seven commands, the members-roster query, **and the visibility dispatch retro-fitted onto the slice-004 read queries (`GetProjectTasks`, `GetMyProjects`) and owner-only manage commands (`DeleteProject`/`EditProject`/`ArchiveProject`/`UnarchiveProject`)** so a member reads (viewer+) and a non-owner manage attempt on a shared project returns 403 (R9) + the `ProjectResponse.role` projection, the authorization-policy extension, and the web sharing/members surface.

## Constitution Check

*GATE: Re-evaluated against v4.0.0 for slice 007. PASS. This slice is the **origin of the shared-project authorization branch** — **Principle IX is OWNED and central** (the membership + role branch dispatched by visibility), exercised across every new handler with an allow AND a deny test plus a role × operation deny matrix (SC-013/SC-016). It performs the **first migration since slice 004** (Principle VII / FR-051 now LIVE — tracked in Complexity Tracking). Per the governance posture, **authorization changes require a non-author reviewer and tested treatment** (allow + deny + the deny matrix) before merge. The 30-second undo **UI** (FR-040) is not in scope and is — by FR-064 — deliberately **not** the path for these confirmation-gated actions (Complexity Tracking).*

| # | Principle | Status | How Addressed |
|---|---|---|---|
| I | Keyboard-First | PASS | Share/unshare, invite-by-email, change-role, transfer-owner, remove, and leave are all reachable and fully operable by keyboard from the sidebar share/members surface; every confirmation dialog (FR-064) is keyboard-operable and Esc-dismissable. Single-key suppression (FR-031) keeps shortcuts from hijacking the **invite-email** text input. |
| II | Accessibility | PASS | The share, members-roster, invite, change-role, transfer-ownership, remove, and leave dialogs follow the **dialog focus contract** (initial focus, trap, Esc, return focus — FR-101). Role badges never use **color as the sole signal** (role text + icon accompany, FR-044 ≥4.5:1); no hover-only affordances (FR-046); transitions instant/<100 ms under `prefers-reduced-motion` (FR-047); no AT-binding collision (FR-045); correct ARIA roles/labels (FR-043) + visible focus (FR-042). Membership/access-change toasts route through the **polite ARIA-live region without stealing focus** (FR-101). |
| III | Instant Response | PASS | Membership/role/visibility mutations are **confirmation-gated and non-optimistic** by design (FR-064, R12): they take effect only on the confirmed server round-trip — optimistically painting a revocation then rolling back would risk a flash of revoked access, so the deliberate departure from the slice-002/004 optimistic recipe is correct here. The roster reads (`['projects', id, 'members']`) load via TanStack Query with skeletons permitted (Principle IV); server writes stay p95 <200 ms. The blast-radius preview is computed from the loaded roster, shown without a round-trip. |
| IV | Minimalist UI | PASS | One share/unshare control, one members roster (with role selectors + remove/leave/transfer actions), one invite-by-email input — all mounted from the existing sidebar dialog pattern (DeleteProjectDialog established the blast-radius confirmation shape). No wizard, no onboarding, no new top-level surface. |
| V | Connected, Server-Authoritative | PASS | Membership, roles, the immutable `ownerId`, and `visibility` persist server-side and are the **system of record** for who may access a shared project; the client holds **no authoritative copy** and `ProjectResponse.role` drives UI gating only — **never** the security boundary (FR-068 is authoritative). No new external runtime dependency (SC-004); all traffic rides the slice-001 BFF proxy. |
| VI | Type Safety | PASS | TS strict + no `any`; C# nullable + analyzers-as-errors. The membership/role types are generated from the OpenAPI contract (`pnpm gen:api`, CI-diff-gated): `MembershipRole` (`editor\|viewer`, the **writable** role) is kept distinct from `EffectiveRole` (`owner\|editor\|viewer`, the **read** composition) so the illegal "promote to owner" state is **unrepresentable** in every write payload (R2). Runtime validation at both boundaries — Zod (invite email + role) and FluentValidation (command boundary). **No new `errorCode`** → the `ERROR_UX satisfies Record<ErrorCode, ErrorUx>` map stays exhaustive with **no change** (R16). |
| VII | Data Integrity | PASS (**migration here**) | **FR-051 is LIVE**: `AddProjectMemberships` is a real schema change (the first entity migration since slice-004 `AddProjects`; slice 005 was a named no-op), so the automatic pre-migration backup + CI restore-test gate MUST run against it. Whether that deploy/CI gate is wired is **not verifiable from the source artifacts** — confirming (and wiring, if absent) it is an explicit task of this slice (Complexity Tracking), not a claim of completion. Forward-only / expand-contract: a purely additive table (no rewrite of existing rows; the `shared` value was already a legal column value, only now written). FR-049 recoverable errors (the `last_owner` "transfer first" message, the unknown-invite-email "ask them to sign in once first" message); FR-050 structured logging (no email/displayName/owner in logs). The 30-second undo **UI** (FR-040) is — by FR-064 — **not** the path for these high-consequence actions; they are confirmation-gated with a **blast-radius** preview instead (Complexity Tracking). |
| VIII | Test-First | PASS | Red-Green-Refactor. xUnit covers `Project.Share`/`Unshare`/`TransferOwnerTo` (pre-state guards), `EnsureNotLastOwner`, and `ResolveEffectiveRole` BEFORE the code; Testcontainers-Postgres integration covers each command/query through the real DB BEFORE the handler, and — per Constitution VIII + IX and the governance gate — **every** new data handler ships an **allow** AND a **deny** test, **plus** the slice ships the **role × operation deny matrix** (SC-016) as first-class tests. Test ids are LOWER than impl ids (RED-first). E2E covers AS-01..AS-06 and the edge cases. |
| IX | Authn/Authz | PASS (**OWNED — centre of the slice**) | This slice **originates** the shared-project authorization branch. Authorization is **deny-by-default (FR-068)** and **dispatched on `Project.Visibility`** — NOT a conjunction of tiers (FR-065): `personal` → the slice-004 ownership branch; `shared` → the new `ResolveEffectiveRole`/`RequireRole` membership + role branch (R8). Shared-project data access **requires current membership** — `createdBy`/assignee are provenance only (FR-066); leave/remove/unshare **revokes ALL access** live on the next request (R10). Each op requires a sufficient role (viewer=read, editor=write, owner=manage — FR-067); **insufficient-role member → 403 `forbidden`**, **non-member → 404 `not_found`** (existence not disclosed, R9). Owner is the immutable `ownerId`, moved only by the transfer command with the **last-owner guard** (`last_owner`, FR-094/R7). The caller is **always** `ICurrentUser.Id`, never from the wire. SC-013/SC-016: allow + deny per handler + the role × operation deny matrix. Membership is the authority that live-subscription eviction (FR-095, slice 016) will consume (R14). |
| X | Time & Timezone | PASS | Membership rows carry only system timestamps, stored UTC (`timestamptz`, Constitution X). **This slice performs no date-relative computation** (no Today/Upcoming, cycles, recurrence, or NL date resolution), so the Europe/Warsaw reference-zone rule (FR-092) has no new surface here. No NodaTime. |
| XI | Privacy | PASS | The member `user_id` FK is the erasure anchor: `project_memberships.user_id → users(id) ON DELETE CASCADE` removes a user's memberships atomically with the `User` row (parity with `owner_id`/`created_by`). Invitations are addressed **by email** but resolved **only against existing admitted Users** — **no new personal data** is created for non-members (OOS-18). The members roster surfaces `displayName` + `role`, **never member emails** (the residual-attribution rule: a departing user loses ALL access; `createdBy` provenance remains but confers no access). |
| XII | Security | PASS | The **invite-by-email** value is untrusted user input — validated/sanitized at the trust boundary (Zod + FluentValidation); resolution is server-side against existing Users. Authorization decisions **never leak data across the membership boundary**: a non-member receives **404** (no existence disclosure), the unknown/duplicate/self invite cases share one 422 field-error shape (no finer enumeration oracle, R4). `displayName`/role are React-escaped on render; structured rejection logs carry `ErrorCode`/`Method`/`Path` only — never email/displayName/owner (FR-050). Slice-001 CSP/security headers + BFF→API signed carrier reused unchanged; no new secrets. |

## Project Structure

### Documentation (this feature)

```text
specs/007-project-sharing-membership/
├── plan.md              # This file
├── research.md          # Phase 0: design decisions (R1–R17)
├── data-model.md        # Phase 1: ProjectMembership entity (NEW) + Project visibility/ownerId activation + the role×operation matrix + read-model delta
├── quickstart.md        # Phase 1: validation guide (AS-01..AS-06, the role matrix, last-owner guard, revoke-all, reversibility)
├── contracts/
│   └── openapi.yaml     # Phase 1: API contract delta (share/unshare/transfer-owner + the members resource + ProjectResponse.role)
└── tasks.md             # Phase 2 (/speckit-tasks — not yet created)
```

### Source Code (repository root)

Paths are **additive** over the slice-002/003/004 tree. **(NEW)** is net-new; **(MODIFY)** is a surgical change. The only DB change is the **one** `AddProjectMemberships` migration (no DDL on `projects`).

```text
apps/
├── api/                                          # ASP.NET Core 9 (C#, DDD, Wolverine)
│   ├── src/
│   │   ├── TaskFlow.Domain/
│   │   │   └── TaskManagement/
│   │   │       ├── Project.cs                     # (MODIFY) add Share(utcNow)/Unshare(utcNow) (first legal Visibility="shared" + back),
│   │   │       │                                  #   TransferOwnerTo(newOwner, utcNow) (the only legal ownerId mutation), and the pure static
│   │   │       │                                  #   guard EnsureNotLastOwner(project, target) → last_owner (R3,R6,R7)
│   │   │       ├── ProjectMembership.cs           # (NEW) ProjectMembership entity (within the Project aggregate): Id/ProjectId/UserId/Role/timestamps (R1,R2)
│   │   │       └── ProjectMembershipId.cs         # (NEW) strongly-typed id (mirrors ProjectId/TaskId)
│   │   ├── TaskFlow.Application/
│   │   │   ├── Authorization/
│   │   │   │   ├── IResourceAuthorizationPolicy.cs # (MODIFY) add ResolveEffectiveRole(...) + RequireRole(project, memberships, caller, RequiredRole) — the dispatch-by-visibility seam (R8)
│   │   │   │   └── ResourceAuthorizationPolicy.cs  # (MODIFY) realize the shared-project membership+role branch (the slice-004 doc already names it); deny shapes per R9
│   │   │   └── TaskManagement/
│   │   │       ├── ShareProject.cs                # (NEW) request DTO + command + validator + handler (owner-only; personal→shared; version) (R3)
│   │   │       ├── UnshareProject.cs              # (NEW) owner-only; shared→personal + RemoveAllForProject; raises ProjectUnshared; version (R3,R13)
│   │   │       ├── TransferOwnership.cs           # (NEW) owner-only; reassign ownerId + remove new-owner row + insert editor row for prior owner; OwnerTransferred (R6)
│   │   │       ├── InviteMember.cs                # (NEW) owner-only; resolve email→User, create row; unknown/self/dup → 422; version (R4)
│   │   │       ├── ChangeMemberRole.cs            # (NEW) owner-only; set member role editor↔viewer; raises MembershipRevoked on a demotion (editor→viewer); target==owner → last_owner; version (R5,R7)
│   │   │       ├── RemoveMember.cs                # (NEW) owner-only; delete member row → revoke-all; target==owner → last_owner; MembershipRevoked; version (R5,R7,R10)
│   │   │       ├── LeaveProject.cs                # (NEW) non-owner self-service; delete own row → revoke-all; caller==owner → last_owner; MembershipRevoked; version (R5,R7,R10)
│   │   │       ├── MemberResponse.cs              # (NEW) read model: { userId, displayName, role (effective), isOwner } — NO email (R17)
│   │   │       ├── ProjectResponse.cs             # (MODIFY) add nullable effective `role`; still hides ownerId/deletedAt (R17)
│   │   │       ├── IProjectRepository.cs          # (MODIFY) add a member-scoped find/list path (FindReadableAsync / shared lookups) alongside the slice-004 owner-scoped finds (R8,R17)
│   │   │       ├── IProjectMembershipRepository.cs # (NEW) ListByProject / Find(projectId,userId) / ListProjectIdsForUser / Add / Remove / RemoveAllForProject (R1,R15)
│   │   │       ├── DeleteProject.cs               # (MODIFY) dispatch by visibility — on shared, delete is a MANAGE op (owner-only → member 403); personal arm unchanged (R8,R9 matrix)
│   │   │       ├── EditProject.cs                 # (MODIFY) same manage-op visibility dispatch (owner-only on shared → member 403); personal arm unchanged (R8,R9)
│   │   │       ├── ArchiveProject.cs              # (MODIFY) manage-op visibility dispatch (owner-only on shared); personal arm unchanged (R8,R9)
│   │   │       ├── UnarchiveProject.cs            # (MODIFY) manage-op visibility dispatch (owner-only on shared); personal arm unchanged (R8,R9)
│   │   │       └── Queries/
│   │   │           ├── GetProjectMembers.cs       # (NEW) composed roster: owner (from ownerId) ∪ rows; surfaces Project.version; member-only (R9,R17)
│   │   │           ├── GetMyProjects.cs           # (MODIFY) include SHARED projects the caller is a member of (via ListProjectIdsForUser) + populate ProjectResponse.role (R8,R17)
│   │   │           └── GetProjectTasks.cs         # (MODIFY) dispatch by visibility — on shared, any current member (viewer+) may READ (RequireRole viewer); non-member → 404; personal arm unchanged (R8,R9)
│   │   ├── TaskFlow.Infrastructure/
│   │   │   └── Persistence/
│   │   │       ├── Configurations/
│   │   │       │   └── ProjectMembershipConfiguration.cs # (NEW) project_memberships mapping + unique(project_id,user_id) + (user_id) index + FKs CASCADE + role CHECK (R15)
│   │   │       ├── ProjectMembershipRepository.cs # (NEW) IProjectMembershipRepository impl (concurrency + unique-violation translation, mirroring ProjectRepository)
│   │   │       └── Migrations/
│   │   │           └── *_AddProjectMemberships.cs # (NEW) the ONE migration this slice — create project_memberships (no projects DDL)
│   │   └── TaskFlow.Api/
│   │       ├── Endpoints/
│   │       │   ├── ProjectEndpoints.cs            # (MODIFY) add PATCH /api/projects/{id}/share, /unshare, /owner → dispatch the commands
│   │       │   └── MemberEndpoints.cs             # (NEW) GET/POST /members, PATCH/DELETE /members/{userId}, DELETE /membership (leave) → dispatch
│   │       └── OpenApi/
│   │           └── TaskFlowDocumentTransformer.cs # (MODIFY) stamp new operationIds + auto-insert 401/403/404/409/422. NO ErrorCodes edit (no new code — R16).
│   └── tests/
│       ├── TaskFlow.UnitTests/
│       │   ├── Domain/TaskManagement/
│       │   │   └── ProjectSharingTests.cs         # (NEW) Share/Unshare pre-state guards, TransferOwnerTo, EnsureNotLastOwner (R3,R6,R7)
│       │   └── Authorization/
│       │       └── ResolveEffectiveRoleTests.cs   # (NEW) owner∪rows∪none composition; dispatch-by-visibility (R2,R8)
│       └── TaskFlow.IntegrationTests/TaskManagement/
│           ├── ShareUnshareProjectTests.cs        # (NEW) share + unshare round-trip; unshare drops all rows + revoke-all; ALLOW & DENY (404/403); version_conflict
│           ├── InviteMemberTests.cs               # (NEW) invite allow + unknown-email 422 + self/dup 422 + ALLOW & DENY (R4)
│           ├── ChangeMemberRoleTests.cs           # (NEW) editor↔viewer + target==owner last_owner + ALLOW & DENY + version_conflict (R5,R7)
│           ├── RemoveMemberTests.cs               # (NEW) remove → removed-member-loses-access (404) + target==owner last_owner + ALLOW & DENY (R7,R10)
│           ├── LeaveProjectTests.cs               # (NEW) leave → revoke-all + caller==owner last_owner + ALLOW & DENY (R7,R10)
│           ├── TransferOwnershipTests.cs          # (NEW) ownerId moves + prior owner demoted to editor + non-member target 422 + ALLOW & DENY (R6)
│           ├── GetProjectMembersTests.cs          # (NEW) composed roster (owner ∪ rows); member-only (non-member 404); no email leak (R17)
│           ├── SharedProjectReadAccessTests.cs    # (NEW) member-readable GetProjectTasks (viewer+ allow, non-member 404); GetMyProjects includes shared + populates role (R8,R17)
│           └── RoleOperationDenyMatrixTests.cs    # (NEW) THE SC-016 matrix: viewer-denied-write, editor-denied-manage(delete/edit/archive/unarchive), non-member-denied-read(404), removed-member(404), last-owner(409)
│
└── web/                                           # Next.js 15 (App Router, TS strict)
    ├── src/
    │   ├── lib/
    │   │   ├── validation/
    │   │   │   └── membership.ts                  # (NEW) inviteSchema (email + role enum editor|viewer), changeRoleSchema, transferSchema
    │   │   └── api/generated/schema.d.ts          # (REGEN) pnpm gen:api — gains the members ops, MembershipRole/EffectiveRole, MemberResponse, ProjectResponse.role, visibility="shared"
    │   ├── components/
    │   │   ├── layout/
    │   │   │   └── Sidebar.tsx                     # (MODIFY) shared-visibility indicator + a share/members entry point on each owned project
    │   │   └── projects/
    │   │       ├── ShareProjectDialog.tsx         # (NEW) confirm share/unshare; unshare states blast radius (members losing access) (FR-064)
    │   │       ├── MembersDialog.tsx              # (NEW) roster: role badges + change-role/remove/transfer/leave actions; role-aware gating
    │   │       ├── InviteMemberForm.tsx           # (NEW) invite-by-email input (FR-031 suppression) + role picker; surfaces unknown-email FR-049 message
    │   │       ├── TransferOwnershipDialog.tsx    # (NEW) pick a current member; confirm; blast-radius (you become editor) (R6,FR-064)
    │   │       ├── RemoveMemberDialog.tsx         # (NEW) confirm remove; blast radius (member loses access + unassigned) (FR-064)
    │   │       └── LeaveProjectDialog.tsx         # (NEW) confirm leave; blast radius (you lose access); last-owner guard message (FR-064,R7)
    │   └── hooks/
    │       ├── useProjectMembers.ts               # (NEW) roster query hook (['projects', id, 'members']) (R17)
    │       └── useMembershipMutations.ts          # (NEW) NON-optimistic, confirmation-gated share/unshare/invite/change-role/remove/leave/transfer; invalidate-on-settle (R12)
    └── tests/
        ├── unit/
        │   ├── membership-validation.test.ts      # (NEW) inviteSchema email/role; changeRole/transfer shapes
        │   ├── use-membership-mutations.test.ts    # (NEW) non-optimistic invalidate-on-settle; FR-049 error surfacing (no rollback path)
        │   └── members-roster.test.ts             # (NEW) role badges; role-aware gating (viewer sees read-only); owner entry rendering
        └── e2e/
            └── sharing.spec.ts                    # (NEW) AS-01..AS-06 + invite-unknown-email + viewer-read-only + last-owner-guard + removed-member-loses-access + reversibility
```

**Structure Decision**: Reuses the slice-002/003/004 monorepo layout and the proven vertical-slice conventions (command + validator + handler + repository + endpoint + read model), instantiated for `ProjectMembership` and the sharing behaviors. The backend **modifies** the `Project` aggregate (three behaviors + one guard) and the `IResourceAuthorizationPolicy`/`ResourceAuthorizationPolicy` (the slice-004 seams whose docs already name the slice-007 membership branch), and **adds** the `ProjectMembership` entity, its repository + configuration + migration, seven commands, one query, the member endpoints, and the `MemberResponse` read model. The slice-004 **read queries** (`GetProjectTasks`, `GetMyProjects`) and **owner-only manage commands** (`DeleteProject`/`EditProject`/`ArchiveProject`/`UnarchiveProject`) are **modified** to dispatch by visibility: their `personal` arm is the unchanged ownership branch, and their `shared` arm calls the new `RequireRole` (viewer+ to read, owner to manage). The web **adds** a share dialog, members dialog, invite form, transfer/remove/leave dialogs, two hooks (one a non-optimistic mutation family), and a membership validator, **modifying** only the Sidebar and the generated client. The BFF proxy, authentication wiring, `ICurrentUser` resolution, the `AuthorizationMiddleware` deny-by-default **authentication** gate, the error pipeline, the soft-delete reaper, and the slice-004 **ownership policy method** (reused as the `personal` arm; the read/manage *handlers* gain the visibility dispatch around it) are otherwise unchanged.

## Key Design Decisions

These summarize `research.md` (R1–R17).

### `ProjectMembership` is a table owned by the `Project` aggregate (R1)
A `project_memberships` table — one row per `(project_id, user_id)` carrying a `role` — is **logically owned by the `Project` aggregate** (ADR-0003: membership is sharing state that changes transactionally with the project) but physically a separate table loaded by a new `IProjectMembershipRepository` with **no EF navigation collection** on `Project` (honoring the slice-004 no-nav-prop persistence style — most projects are personal with zero memberships, and the sidebar tree query must not eagerly load them).

### Two role vocabularies: stored `editor|viewer` vs effective `owner|editor|viewer` (R2)
ADR-0003 is normative: the **owner has no membership row** — ownership is `Project.ownerId`. So `project_memberships.role` is a closed enum of exactly `editor | viewer`, and `owner` is **derived** at read time from `ownerId == caller`. The contract keeps `MembershipRole` (writable: `editor|viewer`) distinct from `EffectiveRole` (read: `owner|editor|viewer`), making the illegal "promote to owner" state unrepresentable in every write payload.

### Reversible `personal ↔ shared`; unshare drops memberships, keeps owner + tasks (R3)
`Share` is the first legal write of `Visibility="shared"`; `Unshare` flips it back and removes ALL membership rows in the same transaction, retaining the owner and the project's tasks. Both bump `Project.Version`; both are confirmation-gated.

### Invite by email → existing admitted Users only; unknown → 422 (R4, OOS-18)
The handler resolves the email server-side; an unknown email is a **422 `validation_failed`** ("ask them to sign in once first", FR-049) with **no** pending record created. Self-invite and duplicate-member collapse into the same 422 field-error shape — no new code, no enumeration oracle beyond "not an admitted member."

### Transfer ownership moves the immutable `ownerId`, demoting the prior owner to editor (R6, FR-094)
The only legal `ownerId` mutation. In one transaction: remove the new owner's row, set `ownerId`, insert an `editor` row for the prior owner. The target must already be a current member.

### Last-owner guard degenerates to an `ownerId`-equality check → `last_owner` (R7)
Under the single-owner model there is never a *set* of owners to count. `EnsureNotLastOwner(project, target)` (a pure static guard mirroring slice-004's `EnsureNestingAllowed`) rejects any leave/remove/change-role whose target is the `ownerId`, **before** the row lookup, with the recoverable `last_owner` "transfer first" message — not a misleading 404.

### Authorization dispatched by visibility; non-member → 404, insufficient-role → 403 (R8, R9)
The policy reads `Visibility` and picks a branch (never ANDs the tiers): `personal` → ownership; `shared` → `ResolveEffectiveRole` ∪ `RequireRole`. The deny shape follows the leak boundary: disclosing a shared project to a **non-member** is a violation → **404 `not_found`**; denying a **member** a higher-privilege action leaks nothing → **403 `forbidden`**. The role × operation deny matrix is the SC-016 artifact.

### Revoke-ALL on leave/remove/unshare; provenance ≠ access (R10, FR-066)
Authorization is computed from **current** membership at request time — never cached, never derived from `createdBy`/assignee. The instant a row is gone (or visibility flips to personal), the former member is a non-member → 404. The **access**-revocation half is real and tested now; the **assignment-clearing** half is the slice-008 seam (R13, vacuous here — `Task.assignees` does not exist yet).

### Project `version` is the single concurrency token; no new error code (R11, R16)
Membership rows carry no token of their own — the Project's `version` guards the whole sharing state (one aggregate, one token); stale → 409 `version_conflict`. `forbidden` (403) and `last_owner` (**409** — a state conflict, see Self-Review Resolution M1), pre-provisioned in slice 004, are **first used** here; `not_found`/`validation_failed`/`version_conflict` are reused — the `ERROR_UX satisfies Record<ErrorCode, ErrorUx>` gate stays green with no change.

### One migration; FR-051 is now LIVE (R15)
`AddProjectMemberships` creates the table (both FKs `ON DELETE CASCADE`, unique `(project_id, user_id)`, `(user_id)` index). No `projects` DDL — `visibility` already exists. First migration since slice 004, so the backup-before-migrate + CI restore-test gate (Constitution VII) must actually run — see Complexity Tracking.

## Complexity Tracking

| Item | Status this slice | Resolution & where it lands |
|------|-------------------|------------------------------|
| **FR-051 backup-before-migration** (Constitution VII MUST) | **LIVE — exercised, not a no-op.** `AddProjectMemberships` is a real schema change (the first entity migration since slice-004 `AddProjects`; slice 005 was a named no-op per research R15). | The automatic pre-migration backup + the CI restore-test step MUST execute against `AddProjectMemberships` and be **verified green** (not assumed). If the hook/CI step is not wired from the slice-002/004 groundwork, wiring it is in-scope for this slice's tasks. |
| **"Unassign from tasks" on unshare/remove/leave** (FR-059/FR-062/FR-063 second half) | **Named seam — vacuous this slice.** `Task.assignees` does not exist until slice 008. This slice performs + tests the **access-revocation** half (removed-member → 404, real now) and **raises** `MembershipRevoked`/`ProjectUnshared`. | The assignment-clearing event **handler** ships with **slice 008 (task-assignment)** — it subscribes to this slice's events; no change to slice 007's commands. Writing "007 clears assignments" would be a fiction (no column exists); raising the event 008 consumes is accurate (R13). |
| **Real-time subscription re-authorization** (FR-095 — evict a removed member's live subscriptions) | **Named seam — not built.** This slice defines membership as the **authority** that change consumes (the `MembershipRevoked`/`ProjectUnshared`/`OwnerTransferred` events). | The SignalR subscription-eviction mechanism is **slice 016 (real-time-collaboration)**; it reads the membership set this slice makes authoritative and attaches to the events — no change to slice 007 (R14). |
| **30-second undo UI** (FR-040) | **Out of scope AND deliberately not the path here.** FR-064 explicitly carves invite/role-change/transfer/remove/leave/unshare **out of** the 30-second undo: they are confirmation-gated with a **blast-radius preview** instead. | The undo **UI** (over other slices' destructive ops) is **slice 014 (undo)**. These confirmation-gated actions are intentionally never undoable (FR-064) — the blast-radius dialog is their safeguard (Principle VII). |
| **In-app notifications** on invite/role-change/removal (FR-006 family) | **Out of scope (referenced only).** Invite/role-change raise no notification this slice. | **Slice 017 (notifications)** subscribes to this slice's membership events. |

> None of the above is an unjustified constitution violation: FR-051 is satisfied (and verified), FR-040's "deferral" is FR-064's deliberate carve-out (the persistence-free confirmation path is the spec's chosen safeguard), and the assignment-clearing / real-time / notification items are scoped to their owning slices with the authority events raised here so those slices stay additive.

## Self-Review Resolutions

The planning was self-reviewed (workflow `slice007-plan`). Findings + **decisions** below; the decisions are authoritative — **implementation (T-phase) MUST apply them**, propagating across the noted artifacts where the generated drafts still show the pre-decision value.

| # | Sev | Finding | **Decision (apply at implementation)** | Propagate to |
|---|-----|---------|----------------------------------------|--------------|
| **H1** | HIGH | `ChangeMemberRole` raised **no** domain event, breaking the additive-seam promise (ADR-0003:174 wants an event on a demotion; Constitution:289 makes a role change a re-auth trigger; slices 008/016 would otherwise have to MODIFY this command). | `ChangeMemberRole` **raises `MembershipRevoked` on a demotion** (`editor → viewer`) — ADR-0003:174 treats a demotion as a revocation of the editor capability, so 008 (clear the demoted member's assignments) and 016 (live re-auth) consume it with no command change. A **promotion** (`viewer → editor`) is access-additive → no revoke event. (research R5 now states this; finish propagation below.) | research R13 event list; data-model §4 state diagram + §6 events; tasks T004 (event list) + T024 (`ChangeMemberRole`); Complexity-Tracking seam rows |
| **M1** | MED | `last_owner` mapped to **422** across artifacts, deviating from the planning directive + HTTP state-conflict semantics, and bundled under the shared `ValidationFailed` (422) response. | `last_owner` → **409** (the resource is in a single-owner *state* incompatible with the request — a state conflict, not field validation). Give it its **own** response component (not under `ValidationFailed`); it shares HTTP 409 with `version_conflict` but clients branch on `errorCode` (ADR-0009). Maps a new `LastOwnerException → 409 last_owner` in `ProblemDetailsMiddleware` (the code is pre-provisioned; only the mapping is new). | contracts/openapi.yaml (all 3 `last_owner` responses + a new `LastOwnerConflict` component); data-model §3/§4 (the "→ 422 last_owner" lines → 409); research R7/R16; quickstart server-validation table |
| **M2** | MED | `EditProject`/`ArchiveProject`/`UnarchiveProject` are **CHANGED** by this slice (T036 adds the shared-arm owner-only dispatch) but the SC-016 deny matrix (T033) enumerates only `delete` — Constitution:511 requires an allow + a deny test per **changed** handler. | Extend T033's deny matrix (and an allow case) to enumerate **edit / archive / unarchive** on a shared project alongside delete: owner-allowed, editor/viewer → 403, non-member → 404. The governance allow+deny-per-changed-handler gate must cover all four modified manage commands. | tasks T033 (matrix) + T036; plan §Project-Structure RoleOperationDenyMatrixTests note |
| **M3** | MED | AS-03 ("viewer denied task-modify") + FR-061 ("editor: change tasks") are only enforceable at the **policy-contract** level this slice — shared-project task-WRITE dispatch is slice 008, so an editor *also* can't write shared tasks yet, and T047's "viewer-read-only" can only assert client UI gating, not server write-denial. | Make explicit in plan/quickstart/tasks that AS-03 + FR-061's editor-write are realized **only as the policy contract** here (T008 `RequireRole(editor)` denies viewer 403 at the policy); full task-write enforcement + the end-to-end viewer-denied/editor-allowed assertions land in **slice 008**. Clarify T047 verifies **UI gating** (`ProjectResponse.role`), not server write-denial. | plan §Constitution VIII/IX rows; quickstart §B AS-03; tasks T033/T047 |
| **L1** | LOW | research R15 mentions a standalone `(project_id)` index; data-model/plan/tasks specify only the unique `(project_id, user_id)` composite (its prefix serves project lookups) + a `(user_id)` index. | Drop R15's standalone `(project_id)` index mention — the composite covers it. Index set = unique `(project_id, user_id)` + `(user_id)`. | research R15 |
| **L2** | LOW | FR-059/062/063 are half-satisfied (access-revocation real; assignment-clearing vacuous until slice 008). | Already documented (Complexity Tracking row 2). The **H1** demotion event also gives slice 008 the signal to clear a demoted member's assignments — no further change. | — (covered) |

All findings are planning-doc refinements (no code exists yet); none blocks the slice's design, which the reviewer otherwise found sound (coverage strong, authorization model sound, no privilege-escalation hole, FR-051 correctly LIVE). The two policy decisions (H1 event, M1 status) are now propagated across all artifacts; M2/M3/L1 are carried into the T-phase via this table.

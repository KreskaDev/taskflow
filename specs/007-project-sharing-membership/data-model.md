# Data Model: Project Sharing, Membership & Roles (slice 007)

**Input**: `spec.md`, `research.md` (R1–R17), constitution v4.0.0 (esp. Principle IX), `docs/architecture/adr-0003-domain-model.md` (normative on the role model), and the slice-004 schema (the `projects` table + its reserved-but-unwritable `shared` visibility value + the ownership authorization branch).

This slice **introduces one new entity** (`ProjectMembership`, ENT-07) with a new table (`project_memberships`) and one EF migration (`AddProjectMemberships`), and **makes the reserved `shared` visibility value writable** on the existing `projects` table (no DDL — two new behavior methods). It is the **origin of the shared-project authorization branch** (Principle IX): the membership + role branch that slices 005/008/009/016/017 build on.

**Role-token vocabulary (load-bearing — keep aligned across all artifacts, R2)**:
- **Stored role** — what `project_memberships.role` holds: exactly `editor | viewer`. The **owner has no membership row**; ownership is `Project.ownerId`.
- **Effective role** — what the authorization policy resolves and the read-models surface: `owner | editor | viewer` (with `owner` *derived* from `ownerId == caller`, never stored), or **none** (non-member).

---

## 1. Entities

### ENT-07 — ProjectMembership (entity within the Project aggregate) — NEW

Links an admitted User to a **shared** Project with a freely-assignable role. Logically **owned by the `Project` aggregate** (ADR-0003: the membership set is that project's sharing state and changes transactionally with the project), physically a separate `project_memberships` table loaded by a repository — **no EF navigation collection** on `Project` (the slice-004 no-nav-prop persistence style, R1).

| Field | Type | Constraints | Notes |
|---|---|---|---|
| `Id` | `ProjectMembershipId` (uuid) | PK; client-generated UUIDv7; `ValueGeneratedNever` | Surrogate id; strongly-typed wrapper, mirrors `ProjectId`/`TaskId` (R15c) |
| `ProjectId` | `ProjectId` (uuid) | **REQUIRED**; FK → `projects(id)` **ON DELETE CASCADE** | The shared project this membership belongs to (R15) |
| `UserId` | `UserId` (uuid) | **REQUIRED**; FK → `users(id)` **ON DELETE CASCADE** | The member User; erasure parity with `projects.owner_id` / `tasks.created_by` (Constitution XI) |
| `Role` | `string` | **REQUIRED**; CHECK `IN ('editor','viewer')` | Stored role — `owner` is NOT a stored value (R2); freely assignable, toggled by `ChangeMemberRole` |
| `CreatedAt` | `DateTime` | UTC (`timestamptz`); set at create | When the member was invited/added (Constitution X) |
| `UpdatedAt` | `DateTime` | UTC (`timestamptz`); stamped on every mutation | Last role change |

**Table**: `project_memberships`. **Column naming**: snake_case (`project_id`, `user_id`, `role`, `created_at`, `updated_at`), matching `projects`/`tasks`.

**Uniqueness**: **unique `(project_id, user_id)`** — at most one membership per user per project (R15). A second invite of an existing member is caught as a 422 (R4) in the handler; the DB constraint is the backstop.

**Indexes**:
- `ux_project_memberships_project_user` — UNIQUE on `(project_id, user_id)`. Also serves "resolve member X's role on project P" (the per-request authorization lookup) and "list project P's members" (prefix scan on `project_id`).
- `ix_project_memberships_user_id` — on `(user_id)`. Serves "shared projects I belong to" (the sidebar's shared-projects query) and the user-erasure cascade lookup (R15).

**Foreign keys**:
- `project_id → projects(id)` **ON DELETE CASCADE** — a membership row is meaningless without its project; deleting a project removes its sharing state.
- `user_id → users(id)` **ON DELETE CASCADE** — account erasure (Constitution XI) removes the user's memberships, parity with `owner_id`/`created_by`.

**No FK references `ownerId`** — the owner has no membership row (R2), so there is no row to constrain or cascade for ownership.

**Domain rules / invariants** (held by the `Project` aggregate + the application layer):
- **Stored role ∈ {editor, viewer} (R2)** — `owner` is never a stored value; the write schema (`MembershipRole`) makes the illegal state unrepresentable.
- **At most one membership per (project, user) (R15)** — DB-enforced; a re-invite of a current member is a 422 (R4).
- **Memberships exist only while `visibility = shared` (R3)** — `UnshareProject` removes ALL rows in the same transaction; a personal project has zero membership rows.
- **Owner never holds a membership row (R2/R6)** — `InviteMember` of the owner → 422 (R4); `TransferOwnership` *removes* the new owner's row and *inserts* an `editor` row for the demoted prior owner (R6).
- **User coercion (R8)** — the caller is always `ICurrentUser.Id`, never taken from the wire; the *target* member id (`{userId}` in path, `email` in invite, `userId` in transfer) is the only user identity read from input, and is resolved/validated server-side.

### ENT-02 — Project (owned by slice 004) — `Visibility` becomes writable; `OwnerId` becomes transferable

This slice does **not** redefine the `projects` table (no DDL). It activates two previously-frozen behaviors:

| Field | Type | Change this slice |
|---|---|---|
| `Visibility` | `string` | The **`shared` value becomes writable** (slice-004 froze it at `personal`). Flipped by new `Share()` / `Unshare()` behavior methods (R3). Still `personal \| shared`. |
| `OwnerId` | `UserId` (uuid) | Becomes **transferable** — the *only* legal mutation of the otherwise-immutable `ownerId` is the new `TransferOwnerTo(newOwner, utcNow)` method (R6, FR-094). Still the authorization anchor. |

All other Project fields (`Name`, `Color`, `Icon`, `ParentId`, `ArchivedAt`, `CreatedAt`, `UpdatedAt`, `Version`, `DeletedAt`) are unchanged from slice 004.

**New behavior methods on the `Project` aggregate** (each stamps `UpdatedAt` + bumps `Version` via `Touch()`, mirroring slice-004 methods):
- `Share(DateTime utcNow)` — `personal → shared`; the **first legal write** of `Visibility = "shared"`. Pre-state guard: only valid when currently `personal` (R3). Raises `ProjectShared` (R13).
- `Unshare(DateTime utcNow)` — `shared → personal`; the handler **removes all membership rows** in the same transaction (R3). `OwnerId` and the project's tasks are retained. Raises `ProjectUnshared` (R13).
- `TransferOwnerTo(UserId newOwner, DateTime utcNow)` — reassigns `OwnerId` (R6). The handler **removes the new owner's membership row** and **inserts an `editor` row for the prior owner** in the same transaction. Raises `OwnerTransferred` (R13). The target must be a current member (validated in the handler).

**Pure static guard (R7, mirroring slice-004 `EnsureNestingAllowed`)**:
- `EnsureNotLastOwner(Project project, UserId target)` — throws a recoverable `last_owner` error when `target == project.OwnerId`. Called **before** any membership-row lookup by `LeaveProject` (caller == owner), `RemoveMember` (target == owner), and `ChangeMemberRole` (target == owner), so the owner-as-target case yields the actionable "transfer ownership first" message instead of a misleading 404 (the owner has no row to find).

### ENT-01 — Task (owned by slice 002) — assignment-clearing is a slice-008 seam (vacuous here)

This slice does **not** touch the `tasks` table. The "unassign from tasks" half of FR-059/FR-062/FR-063 (AS-04/AS-05/AS-06) operates on `Task.assignees`, which **does not exist until slice 008** (task-assignment). Slice 007 performs the membership mutation + **revokes access** (real and tested now — R10), **raises** `MembershipRevoked` / `ProjectUnshared` (R13), and **names** the assignment-clearing event handler as the seam that ships with slice 008. No `tasks` column is read or written here.

---

## 2. Optimistic concurrency — the **Project `version`** is the token for membership mutations

Membership and sharing mutations are part of the Project aggregate's consistency boundary (R1/R11), so they are guarded by the **single `Project.Version` token** — membership **rows carry no concurrency token of their own**. Every command (`ShareProject`, `UnshareProject`, `InviteMember`, `ChangeMemberRole`, `RemoveMember`, `LeaveProject`, `TransferOwnership`) carries the caller's last-seen `Project.Version` and bumps it on success; a stale token → `VersionConflictException` → **409 `version_conflict`** (existing code, no contract change). The members read-model (§5) surfaces the project `version` so a non-owner calling `LeaveProject` has the token without a separate project fetch (R11).

---

## 3. Authorization scoping (deny-by-default, **dispatched by visibility** — the centre of the slice)

This is the slice's load-bearing surface (Principle IX). The application-layer `IResourceAuthorizationPolicy` (the slice-004 seam whose doc already names "membership + role — added in slice 007") gains a **single dispatch entry point that branches on `Project.Visibility`** — NOT a conjunction of tiers (FR-065):

- `Visibility == "personal"` → existing **ownership** branch: `RequireOwnership(ownerId)` (caller must equal `ownerId`).
- `Visibility == "shared"` → new **membership + role** branch: resolve the caller's **effective role** from `ownerId` (→ `owner`) ∪ the membership set (→ `editor`/`viewer`) ∪ otherwise `none`, then require it meets the operation's `RequiredRole`.

New policy surface:
- `ResolveEffectiveRole(Project, IReadOnlyCollection<ProjectMembership>, UserId) → owner | editor | viewer | none`.
- `RequireRole(Project, memberships, caller, RequiredRole)` — dispatches on `Visibility` internally and throws per the deny-shape rule below.

Handlers load the project **and** (when `shared`) its membership set, then call **one** policy method; the owner is **never** taken from the wire (coerced from `ICurrentUser.Id`).

### Role × operation capability matrix (the SC-016 deny matrix)

| Operation class (on a `shared` project) | Required effective role | viewer | editor | owner | non-member |
|---|---|---|---|---|---|
| Read project / list its tasks / list members | `viewer`+ | allow | allow | allow | **404** |
| Write a task (create/edit/move/complete in the project) — *slice 008+* | `editor`+ | **403** | allow | allow | **404** |
| Manage: invite / change-role / remove / unshare / transfer / delete project | `owner` | **403** | **403** | allow | **404** |
| Leave the project | any non-owner member (self) | allow | allow | **`last_owner` (409)** | **404** |

> The "write a task" row is the policy contract this slice *establishes*; the task-write handlers that consume it on shared projects arrive with slice 008. The shared-project **read** rows (project/tasks/members) and **manage** rows are realized and tested **this** slice.

### Deny-shape rule (load-bearing posture)

- **Non-member** (effective role = `none` — neither owner nor a row-holder; includes a *removed* member and any outsider `X`) → **404 `not_found`**. Existence is **not disclosed** across the membership boundary (Constitution XII; same posture as the slice-004 ownership 404).
- **Member with insufficient role** (e.g. viewer attempting a write; editor attempting a manage op) → **403 `forbidden`**. The member already knows the project exists, so the honest answer is "you lack the role" (FR-067) with an FR-049 recovery.
- **Last-owner-targeted** remove/demote/leave → **409 `last_owner`** (R7), recoverable ("transfer ownership to another member first").

### Revoke-ALL on leave / remove / unshare (FR-066, R10)

The instant a user's membership ends (`LeaveProject`, `RemoveMember`, or `UnshareProject` which ends all non-owner memberships), that user **loses ALL access** to the project's data, evaluated **live** on the next request (no row + not owner → `none` → 404). This holds **regardless of authorship or assignment**: `Task.createdBy` and (slice 008) `assignee` are **provenance only**, conferring no standalone access. A removed editor who *created* a task can no longer read or write it.

### Read-model leak rule (slice-004 posture, extended)

- `ProjectResponse` still **never exposes `OwnerId`** (the lean response surfaces the caller's **effective `role`** instead — §5) or `DeletedAt`.
- The members read-model (§5) surfaces `userId` + `displayName` + `role` + `isOwner`, but **does NOT echo member emails** (Constitution XI privacy — invite is *by* email, the roster need not expose addresses).

### Test coverage (Constitution VIII + IX governance gate)

**Every** data handler ships an **allow** and a **deny** integration test through the real DB, and the slice ships the **role × operation deny matrix** above as first-class tests (SC-013, SC-016): viewer-denied-write, editor-denied-manage, non-member-denied-read (404), removed-member-loses-access (404), last-owner-guard (409) per applicable surface, plus the allow case per handler.

---

## 4. State transitions

### Project visibility (reversible — R3)
```
            Share(utcNow)                              Unshare(utcNow)  (drops ALL membership rows; owner + tasks retained)
  personal ───────────────────────▶ shared ───────────────────────────────────────────▶ personal
   │  (visibility=personal,           (visibility=shared,                                  (visibility=personal,
   │   zero membership rows)           owner via ownerId + 0..n editor/viewer rows)         zero membership rows again)
```

### Membership lifecycle within a shared project (R4/R5)
```
                 InviteMember(email→userId, role)           ChangeMemberRole (editor ↔ viewer)
  (no row) ───────────────────────────────────────▶ member row ⇄ member row
                                                          │
                                                          │  RemoveMember (owner targets member)  ──┐
                                                          │  LeaveProject (member targets self)    ──┼──▶ (no row) ── revoke ALL access (R10)
                                                          │                                          │
                                                          ▼  MembershipRevoked raised (R13) ─────────┘
```
> The `ChangeMemberRole (editor ↔ viewer)` edge also raises **`MembershipRevoked` on a demotion (editor→viewer)** (R5/H1) — a demotion revokes the editor capability; a promotion (viewer→editor) is access-additive and raises no event.

### Ownership transfer (R6 — the only legal `ownerId` mutation)
```
  Before:  owner = A (no row)     members = { B:editor, C:viewer }
           TransferOwnerTo(B):
             1. remove B's membership row      (new owner has no row — R2)
             2. ownerId := B
             3. insert A as an editor row      (prior owner demoted — ADR-0003)
  After:   owner = B (no row)     members = { A:editor, C:viewer }     (OwnerTransferred raised)
```

### Last-owner guard (R7) — degenerate single-owner case
```
  LeaveProject(caller == ownerId)                 → 409 last_owner  ("transfer ownership first")
  RemoveMember(target == ownerId)                 → 409 last_owner
  ChangeMemberRole(target == ownerId)             → 409 last_owner
  (checked BEFORE the membership-row lookup — the owner has no row)
```

---

## 5. Read models (delta)

- **`ProjectResponse`** (slice-004 read model) gains a nullable **`role`** field — the **caller's effective role** for the project (`owner | editor | viewer`). For a `personal` project the caller is always the owner (`role = owner`). Drives client-side UI gating (viewer sees read-only) — **not** the security boundary (server FR-068 is authoritative). `ownerId`/`deletedAt` remain hidden (R17).
- **New members read-model** — `GET /api/projects/{id}/members` returns the composed roster: the **owner** (from `Project.ownerId`, effective role `owner`, `isOwner = true`) **∪** the membership rows (`editor`/`viewer`, `isOwner = false`). Each entry: `{ userId, displayName, role, isOwner }`. It **does not echo emails** (Constitution XI) and the response **surfaces the project `version`** so `leave`/`change-role`/`transfer`/`remove` callers carry the concurrency token (R11). Readable by any current member (viewer+); non-member → 404 (R9).

---

## 6. Migration Plan (`AddProjectMemberships` — R15; **FR-051 is LIVE**)

**EF Core migration** (`apps/api/src/TaskFlow.Infrastructure/Persistence/Migrations/`, new `*_AddProjectMemberships.cs`):
- `Up`: create `project_memberships` (all §1 columns/types/nullability); FK `project_id → projects(id)` **CASCADE**; FK `user_id → users(id)` **CASCADE**; CHECK `role IN ('editor','viewer')`; UNIQUE `(project_id, user_id)` (= `ux_project_memberships_project_user`); index `ix_project_memberships_user_id` on `(user_id)`. **No change to the `projects` table** — `visibility` already exists (slice-004 column); making the `shared` value writable is pure behavior (no DDL).
- `Down`: drop `project_memberships` (and its indexes/constraints). `projects` is untouched.
- **Migration-review checklist** (Constitution VII): forward-only / expand-contract (a purely additive table — no rewrite of existing rows; the `shared` value was already a legal column value, only now written); tested against a representative snapshot.

**FR-051 backup-before-migration is LIVE this slice** (R15): `AddProjectMemberships` is a real schema change, so the automatic pre-migration backup **and** the CI restore-test gate (Constitution VII) must actually execute against it — contrasting the named no-ops of slices 003/005. The plan tracks **verifying that gate fires** against this migration.

**New application-layer seams**:
- `IProjectMembershipRepository` — `ListByProjectAsync(projectId)` / `FindAsync(projectId, userId)` / `ListProjectIdsForUserAsync(userId)` / `Add` / `Remove` / `RemoveAllForProjectAsync(projectId)`, loaded transactionally alongside the `Project` aggregate (one-aggregate-per-transaction, ADR-0003), with the same `DbUpdateConcurrencyException → VersionConflictException` and unique-violation translation posture as `IProjectRepository`.
- `IResourceAuthorizationPolicy` extension — `ResolveEffectiveRole(...)` + `RequireRole(...)` (the §3 dispatch-by-visibility methods).
- FluentValidation validators for `InviteMember` (email shape), `ChangeMemberRole` / `TransferOwnership` (target + role shape); cross-row checks (email resolution, member existence, last-owner) in the handlers (R4/R6/R7).
- `ProjectMembershipId` strongly-typed id + EF value conversion, mirroring `ProjectId`.
- Domain events `ProjectShared` / `ProjectUnshared` / `OwnerTransferred` / `MembershipRevoked` raised via the Wolverine transactional outbox (R13), consumed by slices 008/016/017. **`MembershipRevoked` is raised by `RemoveMember`, `LeaveProject`, AND a `ChangeMemberRole` demotion (editor→viewer)** (R5/H1) — that is the complete raiser set; a promotion (viewer→editor) raises no event.

---

## What is unchanged

- The `projects` table columns and the `tasks` table (no DDL on either) — only `Project` **behavior** grows (`Share`/`Unshare`/`TransferOwnerTo`) and a new table is added.
- The `version` / `version_conflict` optimistic-concurrency machinery; the error contract — **no new error code** (R16): `forbidden` (403) and `last_owner` (409) were pre-provisioned in slice 004 and are *first used* here; `not_found` (404), `validation_failed` (422), `version_conflict` (409) are reused.
- The BFF proxy, authentication wiring, `ICurrentUser` resolution, the `AuthorizationMiddleware` deny-by-default authentication gate, and the slice-004 ownership branch (which remains the `personal`-visibility arm of the new dispatch).
- The soft-delete reaper, the project archive lifecycle, and all slice-002/003/004 task behavior. No NodaTime surface (membership timestamps are plain UTC `timestamptz`; this slice introduces no new date-relative computation — Constitution X).

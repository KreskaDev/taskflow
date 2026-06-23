# Implementation Plan: Project Management

**Branch**: `004-project-management` | **Date**: 2026-06-23 | **Spec**: `specs/004-project-management/spec.md`

**Input**: Feature specification from `specs/004-project-management/spec.md`

## Summary

Introduces **Projects** (ENT-02) — the organizational backbone for grouping tasks beyond the Inbox. A user creates projects with a name + preset color + preset icon (US-10.AS-01), nests them **one level** deep (parent/child, AS-02; grandchildren prevented, AS-03), edits and re-parents them (AS-07/AS-08, rejecting moves that break the one-level rule, AS-09), **archives** them (hidden from default views but reachable to unarchive, AS-05/AS-11), and **deletes** them with a three-way task disposition (cascade / move-to-Inbox / archive-with-tasks, FR-014/EC-03) plus a child disposition when a parent has children (cascade / orphan-to-top, AS-10). The slice also defines the **Inbox** as tasks with no project (FR-021, redefining slice 002's flat list) and wires the **`M`** move-to-project mechanic (US-08.AS-05).

This slice realizes the **personal-visibility / ownership** baseline only (FR-057 personal half, FR-065/FR-068 ownership branch): every project carries a required `ownerId` coerced to the caller, `visibility` defaults to `personal`, and every command/query is deny-by-default and scoped to the caller. The **shared** visibility value, `ProjectMembership`, and the membership+role branch (FR-066/FR-067) are slice 007.

It is the **first slice since 002 to add an entity and an EF migration** (`AddProjects`). Consequently **FR-051 backup-before-migration — a named no-op in slice 003 — is LIVE here**: the automatic pre-migration backup and the CI restore-test gate (Constitution VII) must actually execute against this migration. The `project_id` column on `tasks` already exists (slice-002 reserved forward-compat); this slice adds only its FK constraint, the new `projects` table, the new `Project` aggregate + repository + validators, six new commands, three new/changed queries, and the web sidebar/forms/selector/dialogs.

## Technical Context

**Language/Version**: TypeScript (strict) on Node.js 22 LTS / Next.js 15 (App Router), React 19; C# 13 on .NET 9 / ASP.NET Core.

**Primary Dependencies**:
- Frontend (NEW this slice): **none** — reuses TanStack Query v5 (optimistic project mutations on a new `['projects']` key + the existing `['tasks']` key), Zod 3 (project-form + preset validation), openapi-fetch + openapi-typescript (regen), the BFF proxy. A new `lib/projectPresets.ts` constant (colors + icons, ASM-04) is hand-authored, not a dependency.
- Backend (NEW this slice): **none** — reuses Wolverine + Wolverine.Http, EF Core + Npgsql, `WolverineFx.FluentValidation` (new validators on the project commands), the `ProblemDetailsMiddleware`. **No NodaTime** (this slice does no date-relative computation; project timestamps are plain UTC). No new error code.

**Storage**: PostgreSQL 17. **One migration** (`AddProjects`): creates the `projects` table + indexes + the owner/parent FKs, and adds the deferred `tasks.project_id → projects(id)` FK (column pre-exists from slice-002 `AddTasks`). First migration since slice 002 → FR-051 is live (Complexity Tracking).

**Testing**: xUnit (Project aggregate invariants — esp. one-level nesting, archive/unarchive, soft-delete idempotence), Testcontainers-Postgres integration (every command/query through the real DB **with an allow + a deny authorization test** per handler — Constitution VIII/IX + the governance gate), Vitest (project validation/preset schema, `useProjectMutations` optimistic surface, sidebar/tree assembly), Playwright (E2E — AS-01..AS-05 and AS-07..AS-11, EC-03, the `M` move, Inbox; **AS-06's command-palette search is slice 013** — this slice's archived-disclosure bridge covers reaching + unarchiving an archived project, R8).

**Target Platform**: Linux containers (Docker Compose) on Hetzner VPS; host Caddy for TLS. No infra change.

**Project Type**: Web application (monorepo: Next.js frontend + ASP.NET Core API).

**Performance Goals**: project create / `M` move / archive paint optimistically <16 ms (SC-003, owned by slice 002 — the per-user project set is small and tree assembly is client-side, R15/R16); server single-entity writes p95 <200 ms (one new table, owner-scoped indexed queries).

**Constraints**: one-level nesting is a hard invariant enforced at the application layer (R3); colors/icons are a closed server-known preset set (R10, ASM-04); archive (`archived_at`, reversible) is distinct from soft-delete (`deleted_at`, terminal tombstone, R2/R5); authorization is deny-by-default ownership-branch only (shared/membership is slice 007); no new error code (R12); the 30-second undo **UI** is deferred to slice 014 (tombstone persistence ships here).

**Scale/Scope**: ~10 users; per-user project sets are small (tens), tasks up to 10,000 per the working-set anchor (unchanged). This slice adds one table and a handful of owner-scoped queries.

## Constitution Check

*GATE: Re-evaluated against v4.0.0 for slice 004. PASS. This slice exercises the **ownership** authorization branch (Principle IX) across many new handlers and performs the **first migration since slice 002** (Principle VII / FR-051 now LIVE — tracked in Complexity Tracking). One normative-MUST is a deliberate, accepted deferral: the 30-second undo **UI** (FR-040), tracked below.*

| # | Principle | Status | How Addressed |
|---|---|---|---|
| I | Keyboard-First | PASS | Projects are created via the command palette / dedicated action (AS-01) and a task is moved with **`M`** (AS-05); the project create/edit form, selector, and dialogs are fully keyboard-operable; no mouse required. Single-key suppression (FR-031) keeps `M` from hijacking the name input. |
| II | Accessibility | PASS | The create/edit form, `M` selector, delete-with-tasks dialog, and child-disposition prompt follow the **dialog focus contract** (initial focus, trap, Esc, return focus — FR-101). Preset **color is never the sole signal** (icon + name accompany it, FR-044 ≥4.5:1). No hover-only affordances (FR-046); transitions instant/<100 ms under `prefers-reduced-motion` (FR-047); no AT-binding collision (FR-045). Server-initiated toasts (e.g. a delete-failure or reconcile) route through the polite live region without stealing focus (FR-101). |
| III | Instant Response | PASS | Project create / `M` move / archive paint optimistically <16 ms (SC-003) on the `['projects']`/`['tasks']` keys via the slice-002 `onMutate`/`onError`/`onSettled` recipe (R15); the one-level-nesting **prevention message** is computed client-side from the loaded tree, so AS-03 shows with no round-trip; the server re-validates authoritatively and rolls back on rejection. Server writes stay p95 <200 ms. Skeleton screens permitted for the network-bound project-list/project-view loads (Principle IV) but not to mask an optimistic mutation. |
| IV | Minimalist UI | PASS | A single left sidebar (Inbox + one-level project tree + a minimal "Archived" disclosure), one create/edit form, one selector, and disposition dialogs — no wizard, no onboarding. Preset colors/icons keep the palette muted and constrained (ASM-04). |
| V | Connected, Server-Authoritative | PASS | PostgreSQL via the C# API is the system of record; projects and the Task `project_id` persist server-side in the documented relational schema. The client optimistically renders, but the server owns every write, the nesting invariant, and the dispositions. No new external runtime dependency (SC-004); all traffic rides the slice-001 BFF proxy. |
| VI | Type Safety | PASS | TS strict + no `any`; C# nullable + analyzers-as-errors. The `Project` type is generated from the OpenAPI contract (`pnpm gen:api`, CI-diff-gated); runtime validation at both boundaries — Zod (project form + preset enum) and FluentValidation (command boundary). The one-level rule (FR-012) is a validated invariant (R3). No new `errorCode` → the `ERROR_UX satisfies Record<ErrorCode, ErrorUx>` map stays exhaustive with **no change** (R12). |
| VII | Data Integrity | PASS (**migration here**) | **FR-051 is LIVE**: `AddProjects` is the first migration since slice 002, so the automatic pre-migration backup + CI restore-test gate MUST run against it. Whether that deploy/CI gate is wired is **not verifiable from the source artifacts** — confirming (and wiring, if absent) it is an explicit task of this slice (Complexity Tracking), not a claim of completion. Forward-only / expand-contract: adding a table and a nullable-column FK is purely additive. FR-049 recoverable errors (the nesting message, the delete dialogs that state **blast radius**); FR-050 structured logging (no name/owner in logs). Soft-delete tombstone (FR-097) ships for projects and child-cascades; the 30-second undo **UI** (FR-040) is the accepted deferral to slice 014 (Complexity Tracking). |
| VIII | Test-First | PASS | Red-Green-Refactor. xUnit covers the `Project` aggregate invariants (one-level nesting both failure shapes, archive/unarchive incl. the orphan-on-unarchive rule, idempotent soft-delete) BEFORE the aggregate; Testcontainers-Postgres integration covers each command/query through the real DB BEFORE the handler, and — per Constitution VIII + IX and the governance gate — **every** new data handler ships an **allow** AND a **deny** test (a request for a project the caller does not own MUST 404). E2E covers AS-01..AS-05, AS-07..AS-11, EC-03, and the `M` move (AS-06's palette search is slice 013; the archived disclosure bridges reach + unarchive, R8). |
| IX | Authn/Authz | PASS | Deny-by-default (FR-068), **dispatched on the personal/unprojected → ownership branch** (FR-065): every project create/edit/re-parent/archive/unarchive/delete, the `M` move, and the Inbox/project-list/project-tasks queries coerce `ownerId`/`createdBy` to `ICurrentUser.Id` and scope every read to the caller; a foreign/absent/tombstoned id → **404** (existence not disclosed, R13). `M` authorizes ownership of **both** the task and the target project. `createdBy`/`ownerId` are provenance-grounded ownership, never a wire grant. The shared-project membership+role branch (FR-066/FR-067) is referenced only — realized in slice 007. Sessions/admission are slice 001 (assumed authenticated, admitted caller). |
| X | Time & Timezone | PASS | Project records carry only system timestamps, stored UTC (`timestamptz`, Constitution X). **This slice performs no date-relative computation** (no Today/Upcoming, cycles, recurrence, or NL date resolution), so the Europe/Warsaw reference-zone rule (FR-092) has no new surface here. No NodaTime. |
| XI | Privacy | PASS | The required `ownerId` is the erasure anchor: `projects.owner_id → users(id) ON DELETE CASCADE` erases a user's projects atomically with the `User` row (parity with `tasks.created_by`). No new personal data beyond ownership; retention stance inherited ("retained until account deletion"). The full owned-shared-project transfer cascade (FR-085) is slice 007/015. |
| XII | Security | PASS | The project **name** is untrusted user content — React-escaped on render, never `dangerouslySetInnerHTML` (FR-099); colors/icons are a constrained server-known set, not free-form (R10/ASM-04), closing the styling-injection surface. Structured rejection logs carry `ErrorCode`/`Method`/`Path` only — never the name or owner (FR-050). Slice-001 CSP/security headers + BFF→API signed carrier reused unchanged; no new secrets. |

## Project Structure

### Documentation (this feature)

```text
specs/004-project-management/
├── plan.md              # This file
├── research.md          # Phase 0: design decisions (R1–R16)
├── data-model.md        # Phase 1: Project entity (NEW) + Task.project_id activation + read-model delta
├── quickstart.md        # Phase 1: validation guide (AS-01..AS-05/AS-07..AS-11, EC-03, M move, Inbox; AS-06 search = slice 013)
├── contracts/
│   └── openapi.yaml     # Phase 1: API contract delta (new project ops + TaskResponse.projectId + GET /api/tasks narrowing)
└── tasks.md             # Phase 2 (/speckit-tasks — not yet created)
```

### Source Code (repository root)

Paths are **additive** over the slice-002/003 tree. **(NEW)** is net-new; **(MODIFY)** is a surgical change. The only DB change is the **one** `AddProjects` migration.

```text
apps/
├── api/                                          # ASP.NET Core 9 (C#, DDD, Wolverine)
│   ├── src/
│   │   ├── TaskFlow.Domain/
│   │   │   └── TaskManagement/
│   │   │       ├── Project.cs                     # (NEW) Project aggregate root: Create + Edit + Archive/Unarchive
│   │   │       │                                  #   + SoftDelete; holds the one-level-nesting & archive invariants (R1–R3,R9)
│   │   │       ├── ProjectId.cs                   # (NEW) strongly-typed id (mirrors TaskId)
│   │   │       └── Task.cs                         # (MODIFY) add MoveToProject(projectId?, utcNow) behavior (sets ProjectId; bumps version)
│   │   ├── TaskFlow.Application/
│   │   │   └── TaskManagement/
│   │   │       ├── CreateProject.cs               # (NEW) request DTO + command + validator + handler (idempotent create; owner coercion)
│   │   │       ├── EditProject.cs                 # (NEW) edit name/color/icon/parent together (R4); nesting guard in handler (R3); version
│   │   │       ├── ArchiveProject.cs              # (NEW) archive + childDisposition (AS-10); version
│   │   │       ├── UnarchiveProject.cs            # (NEW) unarchive; orphan-on-still-archived-parent (R9); version
│   │   │       ├── DeleteProject.cs               # (NEW) soft-delete + task/child dispositions (R5); version
│   │   │       ├── MoveTaskToProject.cs           # (NEW) the `M` command (R7); owns both ownership checks; version
│   │   │       ├── ProjectResponse.cs             # (NEW) read model (hides ownerId/deletedAt)
│   │   │       ├── IProjectRepository.cs          # (NEW) owner-scoped finds/lists (+ children, + includeArchived) (mirrors ITaskRepository)
│   │   │       ├── TaskResponse.cs                # (MODIFY) add nullable ProjectId + From(...) projection (R16)
│   │   │       └── Queries/
│   │   │           ├── GetMyProjects.cs           # (NEW) sidebar list (owner-scoped; archived flag, R8)
│   │   │           ├── GetProjectTasks.cs         # (NEW) a project's tasks (owner+project scoped, R6)
│   │   │           └── GetMyTasks.cs              # (MODIFY) narrow to Inbox: AND project_id IS NULL (FR-021/R6)
│   │   ├── TaskFlow.Infrastructure/
│   │   │   └── Persistence/
│   │   │       ├── Configurations/
│   │   │       │   ├── ProjectConfiguration.cs    # (NEW) projects table mapping + indexes + owner/parent FKs (R1,R14)
│   │   │       │   └── TaskConfiguration.cs        # (MODIFY) add the project_id → projects(id) FK (ON DELETE SET NULL) (R14)
│   │   │       ├── ProjectRepository.cs           # (NEW) IProjectRepository impl (+ concurrency/duplicate translation)
│   │   │       └── Migrations/
│   │   │           └── *_AddProjects.cs           # (NEW) the ONE migration this slice — create projects + add tasks.project_id FK
│   │   └── TaskFlow.Api/
│   │       ├── Endpoints/
│   │       │   ├── ProjectEndpoints.cs            # (NEW) PUT/PATCH/DELETE/GET project ops → dispatch the commands/queries
│   │       │   └── TaskEndpoints.cs               # (MODIFY) add PATCH /api/tasks/{id}/project (moveTaskToProject)
│   │       └── OpenApi/
│   │           └── TaskFlowDocumentTransformer.cs # (MODIFY) stamp new operationIds + auto-insert 401/404/409/422 for the
│   │                                              #   project ops. NO ErrorCodes edit (no new errorCode — R12).
│   └── tests/
│       ├── TaskFlow.UnitTests/Domain/TaskManagement/
│       │   └── ProjectTests.cs                    # (NEW) Create/Edit/nesting (both failure shapes)/Archive/Unarchive(R9)/SoftDelete
│       └── TaskFlow.IntegrationTests/TaskManagement/
│           ├── CreateProjectTests.cs              # (NEW) round-trip + idempotent replay + ALLOW & DENY (404 foreign)
│           ├── EditProjectTests.cs                # (NEW) edit + re-parent allow (AS-08) + reject (AS-09 → 422) + version_conflict + DENY
│           ├── ArchiveProjectTests.cs             # (NEW) archive/unarchive + child disposition (AS-10) + R9 orphan + DENY
│           ├── DeleteProjectTests.cs              # (NEW) 3 task dispositions + 2 child dispositions + tombstone + DENY
│           ├── MoveTaskToProjectTests.cs          # (NEW) move + move-to-Inbox(null) + both-ownership DENY + version_conflict
│           └── ProjectQueriesTests.cs             # (NEW) GetMyProjects (active vs archived, R8) + GetProjectTasks + Inbox narrowing (R6) + DENY
│
└── web/                                           # Next.js 15 (App Router, TS strict)
    ├── src/
    │   ├── lib/
    │   │   ├── projectPresets.ts                  # (NEW) the closed color + icon preset sets (ASM-04, R10)
    │   │   ├── validation/
    │   │   │   └── project.ts                      # (NEW) projectSchema (name, color/icon preset enum, parentId?) + dispositions
    │   │   └── api/generated/schema.d.ts          # (REGEN) pnpm gen:api — gains ProjectResponse, project ops, TaskResponse.projectId
    │   ├── components/
    │   │   ├── layout/
    │   │   │   └── Sidebar.tsx                     # (NEW) Inbox + one-level project tree + "Archived" disclosure (R8,R16)
    │   │   ├── projects/
    │   │   │   ├── ProjectForm.tsx                # (NEW) create/edit form: name + preset color/icon pickers + parent selector
    │   │   │   ├── ProjectSelector.tsx           # (NEW) the `M` move-to-project selector (dialog focus contract)
    │   │   │   └── DeleteProjectDialog.tsx       # (NEW) 3-way task disposition + child disposition; states blast radius
    │   │   └── tasks/
    │   │       └── TaskRow.tsx                    # (MODIFY) `M` on the selected task opens the selector; show project chip if projected
    │   ├── hooks/
    │   │   ├── useProjects.ts                     # (NEW) query hook for the project tree (['projects'])
    │   │   ├── useProjectMutations.ts             # (NEW) create/edit/archive/unarchive/delete optimistic mutations
    │   │   ├── useTaskMutations.ts                # (MODIFY) add the move-to-project optimistic mutation (moves task across keys)
    │   │   └── useGlobalShortcuts.ts              # (MODIFY) bind `M` to open the selector for the selected task
    │   └── app/(app)/
    │       └── layout.tsx                         # (MODIFY) mount the Sidebar alongside the existing main area
    └── tests/
        ├── unit/
        │   ├── project-validation.test.ts        # (NEW) projectSchema: preset enum, name bounds, parentId optional
        │   ├── use-project-mutations.test.ts     # (NEW) optimistic create/edit/archive/delete + rollback
        │   └── sidebar.test.ts                    # (NEW) one-level tree assembly from the flat list; archived excluded from default
        └── e2e/
            └── projects.spec.ts                  # (NEW) AS-01..AS-05, AS-07..AS-11, EC-03, the `M` move, Inbox narrowing (AS-06 search = slice 013)
```

**Structure Decision**: Reuses the slice-002/003 monorepo layout and the proven `Task` vertical-slice conventions (command + validator + handler + repository + endpoint + read model), instantiated for `Project`. The backend adds one new aggregate, one repository, six commands, three queries (one a modification), one configuration + migration, and the project endpoints; the Task side gains only a `MoveToProject` behavior + the `project_id` FK + the narrowed Inbox query + `TaskResponse.projectId`. The web adds a sidebar, project form/selector/dialog, two hooks, and a preset constant, modifying the layout, `TaskRow`, the task-mutations hook, and global shortcuts. The TZ seam, BFF proxy, authentication wiring, error pipeline, and reaper are reused unchanged.

## Key Design Decisions

These summarize `research.md` (R1–R16).

### A new `Project` aggregate modeled on `Task` (R1, R2)
A `projects` table + `Project` aggregate reuse the `Task` shape: client id + idempotent insert, optimistic `version`, owner-FK cascade, and a soft-delete tombstone. Archive is a **reversible** `archived_at` state, kept distinct from the **terminal** `deleted_at` tombstone — default views filter both; the reaper only cares about `deleted_at` (R2).

### One-level nesting is an application-layer invariant → 422 (R3, R12)
"At most one level" needs repository lookups (the candidate parent's parent, and whether the project has children), so it is enforced in the handler and surfaced as the existing **422 `validation_failed`** with a field-level message — no new error code, no `ERROR_UX` change. Two failure shapes: parent-is-already-a-child, and project-has-children. The client computes the same check on the loaded tree for an instant prevention message (R15).

### Edit + re-parent are one command; dispositions are explicit (R4, R5)
`EditProject` updates name/color/icon/parent together (one form, one optimistic mutation). `DeleteProject`/`ArchiveProject` take caller-chosen **dispositions** — task disposition (cascade / move-to-Inbox / archive-with-tasks, FR-014/EC-03) and child disposition (cascade / orphan-to-top, AS-10) — applied in-transaction before the tombstone; the FK `ON DELETE SET NULL` backstops only stragglers.

### Inbox = `GET /api/tasks` narrowed to `project_id IS NULL`; `M` to move (R6, R7, R16)
The existing tasks endpoint is narrowed to the Inbox (FR-021) — backward-compatible because all pre-slice-004 tasks have no project. `GET /api/projects/{id}/tasks` lists a project's tasks. `MoveTaskToProject` (the `M` action) sets `project_id`; `null` = back to the Inbox; both task and target-project ownership are checked. `TaskResponse` gains `projectId`; the sidebar tree is assembled client-side from the flat project list.

### Personal/ownership baseline only; no new error code (R11, R12, R13)
Every project is `personal` and owner-scoped; authorization dispatches on the ownership branch, deny-by-default, foreign ids → 404, with an allow + a deny test per handler. Shared visibility + membership + role are slice 007. Nesting/preset → 422, stale → 409, foreign → 404 — all existing codes.

### One migration; FR-051 is now LIVE (R14)
`AddProjects` creates `projects` and adds the deferred `tasks.project_id` FK (column pre-exists). It is the first migration since slice 002, so the backup-before-migrate + CI restore-test gate (Constitution VII) must actually run — see Complexity Tracking.

## Complexity Tracking

| Item | Status this slice | Resolution & where it lands |
|------|-------------------|------------------------------|
| **FR-051 backup-before-migration** (Constitution VII MUST) | **LIVE — exercised, not a no-op.** `AddProjects` is the first schema change since slice 002 (slice 003 was a named no-op). | The automatic pre-migration backup + the CI restore-test step MUST execute against `AddProjects` and be **verified green** (not assumed). If the hook/CI step is not yet wired from the slice-002 groundwork, wiring it is in-scope for this slice's tasks. |
| **FR-040 30-second undo UI** (Constitution VII MUST: destructive ops undoable ≥30 s) | **Deferred (accepted at slicing time).** Per the spec's "Known compliance gap": the soft-delete **persistence** (FR-097 tombstone) ships here for project + child + cascaded-task deletions and is excluded from authz-scoped queries; only the user-facing **undo affordance** is deferred. | The 30-second undo **UI** over this slice's delete / re-parent / move paths is the pure retrofit delivered in **slice 014 (undo)**. The delete/archive dialogs do state their **blast radius** (affected tasks + child projects) per Principle VII. No undo UI is added here, per the slice-004 definition. |
| **Shared visibility + membership + role** (FR-057 shared half, FR-066/FR-067) | **Out of scope (referenced only).** Every project is `personal`; the `visibility` column exists but only `personal` is writable. | Realized in **slice 007 (project-sharing-membership)** — an additive value + the `ProjectMembership` set + the membership+role authorization branch; no `projects` schema change needed then (the column is reserved now). |
| **Command-palette search for archived projects** (US-10.AS-06 locating machinery) | **Bridged.** A minimal owner-scoped "Archived" listing (`GET /api/projects?archived=true`) makes unarchive (AS-11, owned here) reachable. | The polished palette-search integration is **slice 013 (command-palette-search)**; this slice ships only the minimal archived listing so AS-11 is testable (research R8). |

> None of the above is an unjustified constitution violation: FR-051 is satisfied (and verified), FR-040's deferral is the spec's explicitly accepted gap with the persistence half shipped, and the shared/search items are scoped to their owning slices.

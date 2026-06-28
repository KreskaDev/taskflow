# Implementation Plan: Labels

**Branch**: `006-labels` | **Date**: 2026-06-29 | **Spec**: `specs/006-labels/spec.md`

**Input**: Feature specification from `specs/006-labels/spec.md`

## Summary

Realizes **ENT-04 (Label)** — a **per-user** tag (Tier A, `ownerId`) — and its **many-to-many relationship with tasks**, plus the **`L` label selector** (US-08.AS-04): on a selected task, press `L` to add/remove reusable labels entirely via keyboard. Per the scope decision this slice ships **full label CRUD** (create, list, update = rename+recolor, delete) **and** the per-user apply/remove on a task (`SetTaskLabels`).

The load-bearing design fact (research R2): **labels are per-user**, so on a shared task each member sees and manages **only their own** labels — `TaskResponse.labels` is **caller-scoped**. Consequently `task_labels` is modelled NOT as an `OwnsMany` child of the Task aggregate (the slice-008 `task_assignees` pattern) but as a **standalone many-to-many relation** with its own repository; applying labels is a **per-user whole-set replace** that **does not touch the shared `Task.version`** and is **versionless**. This also means slice 006 inherits **none** of the slice-008 cross-slice complexity: **no clear-on-project-move** (labels are project-independent) and **no membership-loss cleanup hook** (a former member's label rows are double-gated → no leak). The slice is genuinely **smaller and lower-risk than 008**.

This is an additive schema change — **two new tables** (`labels`, `task_labels`) in **one EF migration** (`AddLabels`) — so **FR-051 (backup-before-migration) is LIVE** (the slice-004/007/008 posture; the CI `backup → migrate → restore-test` gate applies, already wired). It reuses, unchanged, the slice-005 `TaskAccessGuards` dispatch-by-visibility write path and the slice-004 preset-token convention; it raises **no** domain event (labels are not in the FR-079 notification trigger set) and modifies **no** slice-007 handler.

## Technical Context

**Language/Version**: TypeScript (strict) / Next.js 15 (App Router), React 19; C# 13 / .NET 9 / ASP.NET Core.

**Primary Dependencies**:
- Backend (NEW this slice): **none** — reuses EF Core + Npgsql (the two new tables + their migration), Wolverine + Wolverine.Http (the label CRUD commands + `ListLabels` query + the `SetTaskLabels` command), `WolverineFx.FluentValidation` (the label-name/color + label-set validators), the slice-005 `TaskAccessGuards` write path. **No new error code** (R9), **no domain event** (R8).
- Frontend (NEW this slice): **none** — reuses TanStack Query v5 (the optimistic `setTaskLabels` + `createLabel` recipes on the task/roster caches), the shared `Dialog` (the selector's FR-101 focus contract), the `useGlobalShortcuts` contextual layer (the new `L` key), and the slice-004/008 chip pattern.

**Storage**: PostgreSQL 17. **ONE migration this slice** (`AddLabels`): `labels (id, owner_id, name, name_normalized, color NULL, created_at, updated_at)` with a **plain** `(owner_id, name_normalized)` unique index (the normalized column backs case-insensitive uniqueness — EF Core 9 can't model a functional `lower(name)` index, R7) + an `owner_id` index; and `task_labels (task_id, label_id)`, composite PK, both FKs `ON DELETE CASCADE`, a `label_id` index. Additive — the `tasks` table is unchanged. **FR-051 is LIVE** (R10).

**Testing**: xUnit (`Label` create/edit normalization + `LabelId` round-trip); Testcontainers integration (allow + the SC-016 deny matrix for all five handlers: `createLabel` allow/401/dup-422, `listLabels` allow/isolation, `updateLabel` allow/not-owned-404/dup-422, `deleteLabel` allow+cascade/not-owned-404, `setTaskLabels` personal-allow/shared-allow/viewer-403/non-member-404/personal-foreign-404/non-owned-label-422/per-user-isolation/idempotent; the read-path caller-labels-carried + caller-scoping assertion; the labels-survive-project-move assertion); Vitest (the optimistic `setTaskLabels`/`createLabel` surface + the selector + chip rendering); Playwright (US-08.AS-04 keyboard add/remove + the SC-008 a11y audit).

**Target Platform**: Linux containers (Docker Compose) on Hetzner VPS; host Caddy. No infra change.

**Project Type**: Web application (monorepo: Next.js frontend + ASP.NET Core API).

**Performance Goals**: the label toggle paints optimistically <16 ms (SC-003); server single-entity writes p95 <200 ms (SC-012); the caller-scoped label projection on list reads is **one batched join** over the per-user authz-scoped working set (not per-row — SC-009).

**Constraints**: labels are **per-user** (R2) — `TaskResponse.labels` is caller-scoped and `SetTaskLabels` touches only caller-owned rows; the apply command is **versionless** and never bumps `Task.version`; label CRUD authorizes on **ownership** (404-uniform existence-hide), apply is **two-sided** (task write-access AND caller owns every label; non-owned label → 422); a **viewer cannot tag a shared task** (FR-067, intentional); **no domain event** (R8); **no cross-slice cleanup / no clear-on-move** (R5); exactly one migration, FR-051 LIVE (R10); **no new error code** (R9).

**Scale/Scope**: ~10 users; per-user accessible working set up to 10,000 tasks. Adds one aggregate, one relation, four label handlers + one apply command, the `LabelResponse` read model, the caller-scoped `TaskResponse.labels` delta (a batched projection on every task-read path), and the `L` selector + chips web surface.

## Constitution Check

*GATE: Re-evaluated against v4.0.0 for slice 006. PASS. This slice realizes a **per-user (Tier A)** entity (Principle IX ownership branch) plus a **task-dispatched** application surface (the existing dispatch-by-visibility write path), ships **one migration with FR-051 LIVE** (Principle VII), and raises **no** event. The one cross-cutting subtlety — per-user labels on shared tasks — is resolved in research R2/R4/R6 (caller-scoped reads, two-sided authz, no version coupling). No open questions.*

| # | Principle | Status | How Addressed |
|---|---|---|---|
| I | Keyboard-First | PASS | The label selector opens with `L` on the selected task; labels are added/removed/created entirely via keyboard (US-08.AS-04). The new `L` key joins the `useGlobalShortcuts` contextual layer alongside `E`/`M`/`A`. |
| II | Accessibility | PASS | The selector follows the FR-101 dialog focus contract (initial focus, trap, Esc, return focus to the invoking task row); **FR-042** a visible focus indicator on the selector options, the create input, and the chip affordance; options are keyboard-toggleable with `aria-checked` (FR-043); chips show the label **name** (color never the sole carrier of meaning, FR-044 ≥4.5:1 via a curated preset palette); **FR-045** the new single-key `L` binding does not collide with native AT bindings (same class as the existing `E`/`M`/`A` keys; suppressed in text inputs); no hover-only affordances (FR-046); `prefers-reduced-motion` honored (FR-047); FR-031 suppresses bare `L` while the create/filter input is focused; a label change announces via the polite ARIA live region without stealing focus (FR-101). SC-008 audits the selector. |
| III | Instant Response | PASS | `setTaskLabels` and `createLabel` (client-generated `LabelId`) paint optimistically <16 ms (SC-003) via the slice-005/008 `onMutate`/`onError`/`onSettled` recipe; server writes p95 <200 ms (SC-012). The label patch leaves `Task.version` untouched (R2) — no spurious optimistic-token churn. |
| IV | Minimalist UI | PASS | The selector surfaces only the caller's labels + a create affordance; chips are compact name tags. Skeleton only for the genuine roster network load, never masking an optimistic toggle (R11). |
| V | Connected, Server-Authoritative | PASS | Labels + applications persist server-side in PostgreSQL (the system of record); the client holds no authoritative copy. No third-party data service (SC-004). |
| VI | Type Safety | PASS | `LabelResponse`, the request DTOs, and the `TaskResponse.labels` delta are generated from the OpenAPI contract (`pnpm gen:api`, CI-diff-gated). Runtime validation both tiers (Zod label-name/set + FluentValidation). No new `errorCode` → `ERROR_UX` stays exhaustive unchanged (R9). |
| VII | Data Integrity | PASS (**FR-051 LIVE**) | **One migration** (`AddLabels`, two tables; R10). **FR-051 is LIVE** — the CI `backup.sh → ef database update → restore-test.sh` gate covers it (already wired; this slice confirms, not re-wires). FR-049 recoverable errors (duplicate name → field-level 422; non-owned label → 422; rollback message on optimistic rejection); FR-050 structured logging (ids/codes only). The `owner_id`/`task_id`/`label_id` FK cascades make label, task-delete, and account-deletion (FR-085) erasure automatic. |
| VIII | Test-First | PASS | Red-Green across tiers. Every new data handler ships an **allow** AND a **deny** test; the SC-016 deny matrix (viewer-403, non-member-404, personal-foreign-404, non-owned-label-422, ownership-404 on CRUD) is explicit; the caller-scoping and no-clear-on-move invariants get dedicated assertions. |
| IX | Authn/Authz | PASS | Deny-by-default (FR-068), **dispatched by the containing resource's visibility** (FR-065). The Label **entity** is per-user → label CRUD authorizes on **ownership** (`owner_id`), queries scoped to the caller, 404-uniform existence-hide. **Applying** a label follows the **task's OWN** authorization (FR-067): personal → ownership; shared → membership + role (editor/owner write, viewer 403, non-member 404) via the slice-005 `TaskAccessGuards`. The label side adds a Tier A ownership check (non-owned id → 422). `createdBy`/assignee confer no grant. SC-013/SC-016 (allow+deny per handler). Sessions/admission are slice 001 — assumed in force. |
| X | Time & Timezone | PASS | Label timestamps stored UTC (`timestamptz`); labels carry **no** date-relative computation (no Today/Upcoming/cycle/recurrence surface), so `Europe/Warsaw` (ASM-12) governs storage uniformly with no further treatment (spec L122). |
| XI | Privacy | PASS | A label is per-user personal data (`owner_id`); the `owner_id → users(id) ON DELETE CASCADE` makes the FR-085 account-deletion erasure cascade automatic (a deleted user's labels + their applications vanish). Retention = until account deletion (FR-086). `LabelResponse` carries no `ownerId`; `TaskResponse.labels` is caller-scoped ids only — no cross-user PII. |
| XII | Security | PASS | The label `name` is untrusted user content — React-escaped on render (FR-099); `color` is a **preset token** (not raw hex/CSS), so it cannot carry injection. The selector takes typed ids; no free-form HTML crosses a boundary. Slice-001 CSP/headers reused; no new secrets (FR-100). |

## Project Structure

### Documentation (this feature)

```text
specs/006-labels/
├── plan.md              # This file
├── research.md          # Phase 0: R1–R12
├── data-model.md        # Phase 1: the labels + task_labels relations + the CRUD/apply commands + the caller-scoped TaskResponse.labels + the migration (LIVE)
├── quickstart.md        # Phase 1: validation guide (create/rename/recolor/delete, L selector add/remove, deny matrix, caller-scoping, migration/FR-051)
├── contracts/
│   └── openapi.yaml     # Phase 1: createLabel/listLabels/updateLabel/deleteLabel/setTaskLabels + TaskResponse.labels delta
└── tasks.md             # Phase 2 (/speckit-tasks — not yet created)
```

### Source Code (repository root)

**(NEW)** net-new; **(MODIFY)** surgical change. **ONE DB migration this slice** (`AddLabels`).

```text
apps/
├── api/
│   ├── src/
│   │   ├── TaskFlow.Domain/TaskManagement/
│   │   │   ├── Label.cs                          # (NEW) the Label aggregate (Id, OwnerId, Name, Color?, timestamps; Create/Edit; no Version)
│   │   │   ├── LabelId.cs                        # (NEW) strongly-typed id (client-generated, ValueGeneratedNever; mirrors TaskId)
│   │   │   └── TaskLabel.cs                      # (NEW) the join entity { TaskId, LabelId } (standalone relation, no nav prop)
│   │   ├── TaskFlow.Application/TaskManagement/
│   │   │   ├── Labels/CreateLabel.cs            # (NEW) request DTO + command + validator + handler (owner-scoped; dup-name 422)
│   │   │   ├── Labels/UpdateLabel.cs            # (NEW) rename+recolor whole-object replace (ownership 404; dup-name 422)
│   │   │   ├── Labels/DeleteLabel.cs            # (NEW) hard delete (ownership 404; FK cascade clears applications)
│   │   │   ├── Labels/ListLabels.cs            # (NEW) caller-scoped roster query → LabelResponse[]
│   │   │   ├── Labels/LabelResponse.cs         # (NEW) { id, name, color? }
│   │   │   ├── Labels/SetTaskLabels.cs         # (NEW) request DTO + validator + handler (two-sided authz; per-user set replace; versionless)
│   │   │   ├── Labels/ILabelRepository.cs      # (NEW) Add/Update/Delete/Find/ListForOwner/ListIdsForOwner + ExistsByNameForOwner
│   │   │   ├── Labels/ITaskLabelRepository.cs  # (NEW) SetForOwner(taskId,owner,labelIds) + ListLabelIdsForTask(s)Async(taskIds, owner)
│   │   │   ├── TaskResponse.cs                  # (MODIFY) add required `labels: Guid[]` + a REQUIRED `From(task, callerLabelIds)` parameter (compile-breaks ALL call sites)
│   │   │   ├── Queries/TodayResponse.cs         # (MODIFY) TodayTaskResponse gains `labels` + From(task, callerLabelIds) — the flattened DTO the required param does NOT auto-cover (R6)
│   │   │   ├── {CreateTask,RenameTask,EditTask,MoveTaskToProject,RescheduleDueDate,SetPriority,Commands/SetTaskDone,Commands/ReorderTask,SetTaskAssignees}.cs  # (MODIFY) pass caller label ids to TaskResponse.From (single-task overload)
│   │   │   └── Queries/{GetMyTasks,GetProjectTasks,GetUpcomingTasks,GetTodayTasks,GetAssignedToMe}.cs  # (MODIFY) batch-load caller label ids (ListLabelIdsForTasksAsync) + pass to *.From
│   │   ├── TaskFlow.Infrastructure/Persistence/
│   │   │   ├── Configurations/LabelConfiguration.cs       # (NEW) labels mapping (ids HasConversion; plain unique ux_labels_owner_name on (owner_id, name_normalized); FK owner→users CASCADE)
│   │   │   ├── Configurations/TaskLabelConfiguration.cs   # (NEW) task_labels mapping (ProjectMembership style; composite PK; dual FK CASCADE; ix_task_labels_label_id)
│   │   │   ├── LabelRepository.cs               # (NEW) ILabelRepository impl
│   │   │   ├── TaskLabelRepository.cs           # (NEW) ITaskLabelRepository impl (the batched caller-scoped join)
│   │   │   ├── AppDbContext.cs                  # (MODIFY) DbSet<Label> (+ task_labels via configuration; the join needs no DbSet if keyless-via-entity)
│   │   │   └── Migrations/…_AddLabels.cs        # (NEW) the ONE migration (FR-051 LIVE) — two CreateTable
│   │   └── TaskFlow.Api/
│   │       ├── Endpoints/LabelEndpoints.cs      # (NEW) GET /api/labels; PUT/PATCH/DELETE /api/labels/{id} (PUT = idempotent create, client-gen id)
│   │       ├── Endpoints/TaskEndpoints.cs       # (MODIFY) + PATCH /api/tasks/{id}/labels
│   │       └── OpenApi/TaskFlowDocumentTransformer.cs  # (MODIFY) stamp the 5 operationIds + their responses (NO ErrorCodes edit)
│   └── tests/
│       ├── TaskFlow.UnitTests/Domain/TaskManagement/LabelTests.cs   # (NEW) Label create/edit normalization + LabelId round-trip
│       └── TaskFlow.IntegrationTests/Labels/
│           ├── CreateLabelTests.cs             # (NEW) allow / 401 / dup-422
│           ├── ListLabelsTests.cs             # (NEW) allow / per-user isolation
│           ├── UpdateLabelTests.cs            # (NEW) allow / not-owned-404 / dup-422
│           ├── DeleteLabelTests.cs            # (NEW) allow + cascade / not-owned-404
│           ├── SetTaskLabelsTests.cs          # (NEW) personal-allow, shared-allow, deny matrix, per-user isolation, idempotent
│           └── TaskLabelsReadTests.cs         # (NEW) read-path carries caller labels (incl. Today flattened DTO) + caller-scoping + labels-survive-project-move
└── web/
    ├── src/
    │   ├── lib/validation/label.ts             # (NEW) the label-name/color + label-set Zod schemas
    │   ├── lib/api/generated/schema.d.ts       # (REGEN) pnpm gen:api — the 5 ops + TaskResponse.labels
    │   ├── hooks/useLabels.ts                  # (NEW) the ['labels'] roster query + createLabel/updateLabel/deleteLabel mutations
    │   ├── hooks/useTaskMutations.ts           # (MODIFY) the optimistic setTaskLabels recipe (patch labels only; version untouched)
    │   ├── components/labels/LabelSelector.tsx # (NEW) the `L` Dialog: toggle options + type-to-create
    │   ├── components/tasks/TaskRow.tsx        # (MODIFY) label chips (name from roster) + the selector affordance
    │   └── hooks/useGlobalShortcuts.ts         # (MODIFY) the `L` contextual key → onLabel
    └── tests/
        ├── unit/{label-validation,label-selector,task-view-mutations}.test.ts  # (NEW/MODIFY) validation + selector + optimistic setTaskLabels
        └── e2e/labels.spec.ts                  # (NEW) US-08.AS-04 keyboard add/remove + a11y audit
```

**Structure Decision**: reuses the slice-005/008 vertical-slice conventions, with one deliberate divergence: `task_labels` is a **standalone relation + repository**, NOT an `OwnsMany` child of the Task aggregate (R2 — per-user, versionless), so the apply path does not load or mutate the Task aggregate. The backend adds one aggregate + one relation (+ their migration), four label handlers + one apply command, the `LabelResponse` model, and the caller-scoped `TaskResponse.labels` batched projection on every task-read path. The web adds the `L` selector + a roster hook + the optimistic recipes, reusing the shared `Dialog` and the chip pattern. The slice-005 `TaskAccessGuards` and the slice-004 preset-token convention are reused unchanged; **no** slice-007 handler is touched.

## Key Design Decisions

Summaries of `research.md` (R1–R12):
- **`Label` per-user aggregate + `labels` + `task_labels`; one migration; FR-051 LIVE** (R1/R10) — dual FK cascades make label/task/account erasure automatic.
- **`task_labels` standalone M:N relation, NOT OwnsMany; per-user apply; versionless; no `Task.version` bump** (R2) — the load-bearing divergence from slice 008 (per-user vs shared state).
- **Full CRUD** (R3) — Create/List/Update(rename+recolor)/Delete + the per-user `SetTaskLabels`.
- **Two-sided authorization** (R4) — label CRUD on ownership (404-uniform); apply requires task write-access AND caller owns every label (non-owned → 422); viewer cannot tag shared tasks (intentional).
- **No cross-slice cleanup, no clear-on-move** (R5) — labels project-independent; former-member rows double-gated → no leak.
- **`TaskResponse.labels` caller-scoped ids; required `From` parameter; one batched join per list** (R6).
- **No domain event** (R8); **no new error code** (R9).

## Complexity Tracking

| Item | Status this slice | Resolution & where it lands |
|------|-------------------|------------------------------|
| **FR-051 backup-before-migration** (Constitution VII MUST) | **LIVE** — one EF migration (`AddLabels`, two tables). | No re-wiring: the CI `scripts/backup.sh → dotnet ef database update → scripts/restore-test.sh` gate (live since slice 001/004/007/008) covers it. Verify **exactly one** new file under `Persistence/Migrations/` in the diff and read the generated SQL (the plain `(owner_id, name_normalized)` unique index + the two cascade FKs; no `migrationBuilder.Sql`). |
| **Per-user labels on shared tasks** (the cross-slice subtlety the goal flagged) | **Resolved** — labels are per-user (ENT-04); `TaskResponse.labels` is caller-scoped; each member manages only their own labels on a shared task. | R2/R4/R6: `task_labels` is a standalone relation keyed through `label_id → labels.owner_id`; the read projection filters `owner_id = caller`; `SetTaskLabels` replaces only caller-owned rows. Asserted by a per-user-isolation test (member O's labels absent from C's read; C's set replace doesn't touch O's rows). |
| **Divergence from the slice-008 OwnsMany/version pattern** | **Deliberate** — labels are per-user, so OwnsMany + `Task.version` coupling would be wrong (per-user filtering in the aggregate; spurious shared-version churn / SignalR fan-out for invisible changes). | R2: standalone relation + `ITaskLabelRepository`; versionless command; `Task.version` untouched. Documented so a reviewer doesn't flag the non-008 pattern as an inconsistency. |
| **`TaskResponse.labels` projection on all read paths** (the slice-008 silent-empty lesson) | **Required** — `labels` is a required field; a missed projection would silently emit `[]`. | R6: `TaskResponse.From` gains a **required** `callerLabelIds` parameter → every directly-typed call site (9 command handlers + 4 list queries) is a **compile error** until it supplies caller-scoped ids (type-safe, not convention). Each list handler calls **one batched** `ListLabelIdsForTasksAsync(taskIds, caller)`. **The one gap the compiler does NOT catch: the flattened `TodayTaskResponse`** (`allOf TaskResponse + isOverdue`, hand-copies fields) — so it is treated as a first-class affected path (its own `labels` field + `From(callerLabelIds)` + the batched wire in `GetTodayTasks`) and pinned by a dedicated **Today-carries-caller-labels** test. The join translates (non-nullable ids — not the slice-005 nullable-FK trap; precedent `ProjectRepository.ListByIdsAsync`), verified by a throwaway test as cheap insurance. |
| **No cleanup on membership loss / project move** (would be flagged as a gap by analogy to slice 008) | **Intentionally absent** (R5). | Labels are project-independent (no clear-on-move) and a former member's label rows are double-gated (no task access AND no label-read access → no leak), so cleanup would be dead code. Documented in research/data-model; the slice-007 handlers are untouched. A `labels-survive-project-move` test pins the no-clear-on-move decision. |
| **Viewer cannot tag a shared task with private labels** | **Intentional** — `SetTaskLabels` follows the task's authorization (FR-067: viewer read-only). | R4: documented consequence, not a gap. A viewer-403 deny test pins it. (If private-viewer-tagging were ever wanted, it would be a spec change — out of scope.) |
| **No domain event** (contrast slice 008's `TaskAssigned`) | **Intentional** (R8) — labels are not in the FR-079 notification trigger set; a label change is invisible to other members by construction. | Program.cs message wiring is **unchanged** (no `PublishMessage`/queue/exclusion/no-op handler). Documented so the absence isn't read as an omission. |

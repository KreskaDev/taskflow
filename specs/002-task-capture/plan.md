# Implementation Plan: Task Capture

**Branch**: `002-task-capture` | **Date**: 2026-06-18 | **Spec**: `specs/002-task-capture/spec.md`

**Input**: Feature specification from `specs/002-task-capture/spec.md`

## Summary

Adds the first **Task Management** bounded context on top of the slice-001 foundation: the `Task` aggregate and its keyboard-driven capture surface. A user presses `C` to open a focused capture input (≤16 ms, synchronous mount + focus), types a title, and Enter creates a task — the client mints a UUIDv7 `id` and a newest-first `position` rank, paints the row optimistically at the top, and the server reconciles. The caller's own non-deleted tasks list in an ownership-scoped, virtualized (10k @ 60 fps) keyboard list with ↑/↓ navigation, Space toggle (done↔backlog), `E` inline rename, `Del` soft-delete, and Alt+↑/↓ manual reorder. Edits are optimistic-concurrency-safe via an integer `version` token (stale → `409 version_conflict` → client refetch + intent-based reapply). Soft-delete sets a `deleted_at` tombstone (excluded from all queries) and schedules a 30-second Wolverine reaper that hard-deletes; the user-facing undo toast is deferred to slice 014. Every Task command/query handler ships an allow AND a deny test (SC-013/SC-016), dispatched to the deny-by-default **ownership** branch (`createdBy = caller`).

This slice does NOT bootstrap — it reuses the slice-001 Wolverine handler pattern, `AppDbContext` + migrations, the BFF→API HMAC carrier + authenticated proxy, the OpenAPI document transformer, the ProblemDetails error contract, and the web `Dialog`/`LiveRegion`/`Toast` primitives. It adds NOTHING to the global authorization wiring (the deny-by-default `AuthorizationMiddleware` is already woven on both the message pipeline and the HTTP endpoint group).

## Technical Context

**Language/Version**: TypeScript (strict) on Node.js 22 LTS / Next.js 15 (App Router), React 19; C# 13 on .NET 9 / ASP.NET Core

**Primary Dependencies**:
- Frontend (NEW this slice): `@tanstack/react-virtual` (list windowing for 10k rows), `uuid` v11 (UUIDv7 minter via `v7()`, wrapped behind `lib/id.ts`), `fractional-indexing` (`generateKeyBetween` for the `position` rank). Reused: TanStack Query v5 (optimistic mutations), Zod 3, openapi-fetch + openapi-typescript, the BFF proxy + session/carrier plumbing.
- Backend (NEW this slice): `WolverineFx.FluentValidation` (`opts.UseFluentValidation()` — not wired in slice 001) for the title rule → `422 validation_failed`. Reused: Wolverine + Wolverine.Http + Wolverine.EntityFrameworkCore (messaging, outbox, scheduled delivery, `DurabilityMode.Solo`), Npgsql.EntityFrameworkCore.PostgreSQL, Microsoft.AspNetCore.OpenApi.

**Storage**: PostgreSQL 17 (EF Core code-first migration `AddTasks` — the schema source of truth). New `tasks` table; no new BFF session schema.

**Testing**: xUnit + Testcontainers-Postgres (backend unit + integration; allow + deny per handler), Vitest (frontend unit/component), Playwright (E2E — extends `tests/e2e`).

**Target Platform**: Linux containers (Docker Compose) on Hetzner VPS; host Caddy for TLS. No infra change this slice.

**Project Type**: Web application (monorepo: Next.js frontend + ASP.NET Core API).

**Performance Goals**: Optimistic capture/mutation paint <16 ms (SC-003); server single-entity writes p95 <200 ms (SC-012); list scroll 60 fps at 10k tasks (SC-010/EC-06); browser tab <300 MB at 10k tasks (SC-011).

**Constraints**: Single Hetzner VPS; deny-by-default ownership-scoped authorization on every Task operation; soft-deleted rows never returned by any query; `position` and its index under `COLLATE "C"` (byte-ordinal) or ordering drifts silently.

**Scale/Scope**: ~10 users; up to 10,000 tasks per user. The constitution's "10k = per-user authorization-scoped working set" anchor applies, but this slice is **personal/ownership-only** (no shared projects), so the working set is a per-user flat list — perf-regression seed data is shaped accordingly (no shared-project membership overlap is exercised here).

## Constitution Check

*GATE: Re-evaluated against v4.0.0 for slice 002. PASS, with one named, sanctioned deferral tracked in Complexity Tracking (the user-facing 30s undo affordance). The account-deletion erasure seam for tasks is DECIDED and lands in this slice — see Complexity Tracking.*

| # | Principle | Status | How Addressed |
|---|---|---|---|
| I | Keyboard-First | PASS | Every action is a keyboard shortcut: `C` capture, ↑/↓ navigate, Space toggle, `E` rename, `Del` soft-delete, Alt+↑/↓ reorder, Esc cancel, `?` help overlay (discoverable). The grammar is composable (bare keys = command; modifier chords reserved); FR-031/EC-08 suppresses single keys inside text inputs so typing is never intercepted. |
| II | Accessibility | PASS | List is WAI-ARIA `role=listbox` + `role=option` with `aria-activedescendant` on a stable container (survives virtualization unmount); the virtualizer **force-includes the selected index in the rendered window** (overscan plus an explicit always-render-selected entry) so even when the selection is scrolled out of view by wheel/scrollbar `aria-activedescendant` always references a LIVE DOM node a screen reader can resolve; FR-042 selection indicator styled on `aria-selected`, not `:focus`. Capture surface and `?` overlay reuse the `Dialog.tsx` FR-101 focus contract (initial focus, trap, Esc, focus-restore). Toasts/`version_conflict`/rollback announcements go through the polite `LiveRegion` (`role=status`, `aria-live=polite`) WITHOUT stealing focus, coalesced under bursts. Transitions gated to instant/<100 ms under `prefers-reduced-motion` (FR-047). FR-045 (no AT-binding collision): the bare/modifier single-key map passes, and the **Alt+↑/↓ reorder chord is FROZEN as SAFE** (verified against the target browser + screen-reader matrix — the back/forward conflict is `Alt+Left/Right` horizontal only, the vertical pair is free, and the W3C ARIA APG rearrangeable-listbox example uses this exact chord; see research.md **R18** for evidence + the load-bearing mitigations: `preventDefault()` on the listbox keydown, listbox-focus-only via the R11 gate, plain `role=listbox` not combobox, `aria-keyshortcuts`). |
| III | Instant Response | PASS | **Central to this slice.** Optimistic capture paints the focused input <16 ms (synchronous mount + `.focus()`, no network/lazy-import on the `C` path); all mutations use the TanStack `onMutate`/`onError`/`onSettled` cancel-snapshot-rollback recipe (single `['tasks']` key) — optimistic paint then async reconcile/rollback. Server single-entity writes target p95 <200 ms (single-row indexed `position` writes, one-row reorder). 60 fps virtualization at 10k. UI accepts input while a mutation is in flight. |
| IV | Minimalist UI | PASS | Capture is a single focused input; the list is a quiet ordered list; the empty Inbox (EC-01) is an accessible hint to press `C`, not an onboarding wizard. No tooltips-on-first-run, no modal interruptions. Optimistic UI renders content immediately rather than spinners. |
| V | Connected, Server-Authoritative | PASS | All reads/writes go through the C# API (the sole source of truth) via the slice-001 BFF proxy — NO direct browser→API calls, no new external runtime data dependency. The server is the SOLE writer of `position` under the version guard (client computes the optimistic rank; server validates format). The 30s reaper is server-authoritative (FR-097), not a client timer. |
| VI | Type Safety | PASS | TS strict + no `any`; C# nullable + analyzers-as-errors. The OpenAPI document (transformer + checked-in `contracts/openapi.yaml`) is the typed contract; the TS client is regenerated (`pnpm gen:api`) and CI-diff-gated. The new `version_conflict` code lands in the generated `errorCode` union via the regen-and-diff CI gate, so any web reference to it is type-checked; exhaustive `ERROR_UX` coverage is enforced **at compile time** by typing the map `satisfies Record<ErrorCode, ErrorUx>` (where `ErrorCode` is derived from the generated `ProblemDetails.errorCode` union), so `tsc` fails if any code is unmapped — the existing TS-strict CI type-check IS the gate (no bespoke runtime test needed). EF Core migration `AddTasks` is the schema source of truth. Zod (client) + FluentValidation (server) at both trust boundaries. |
| VII | Data Integrity | PASS (with named deferral) | Soft-delete establishes a server-side **30-second recoverability window** (the `deleted_at` tombstone is the restorable state; the reaper hard-deletes only after the window, restore-aware so a slice-014 restore wins the race). The constitution's "undoable for ≥30 s" MUST is satisfied at the data layer here; the **user-facing undo affordance (toast/restore UX) is deferred to slice 014** by the spec's scope split — see Complexity Tracking. FR-049 recoverable error messages on every failed mutation (rollback reappears the row in its original position); structured logs carry no secrets; backup-before-migrate hook runs (effectively a no-op for a brand-new table). |
| VIII | Test-First | PASS | Red-Green-Refactor. Domain unit tests (`TaskTests.cs`) cover aggregate invariants (title bounds, default backlog, done↔backlog + `completed_at` stamp/clear, immutable `createdBy`, soft-delete sets `deleted_at`). Each of the six handlers ships an allow AND a deny test (SC-013/SC-016) through Testcontainers-Postgres; mutating handlers add a stale-`version` → 409 test; create adds an idempotent-replay + foreign-id test; reorder adds an equal-rank tie-break test. |
| IX | Authn/Authz | PASS | Deny-by-default is already enforced by the global `AuthorizationMiddleware.Before` (authentication) on both pipelines — slice 002 adds NOTHING to that wiring. Authorization dispatches to the **ownership** branch only (`createdBy = caller`); writes load the row then `RequireOwnership`; queries are scoped `WHERE created_by = currentUser.Id`. No shared-project path. Foreign/absent/tombstoned single-item ops resolve to `404 not_found` BEFORE `RequireOwnership` could throw 403 (existence-disclosure guard — no task op emits 403 this slice; `403 forbidden` is reserved for the slice-007+ shared-project role case). A re-DELETE of the caller's own tombstone is the idempotent `204` no-op (not 404). The reaper handler runs off the durable queue with no `HttpContext` and is added to the auth-policy exclusion predicate alongside `AccountDeletionRequested`. |
| X | Time & Timezone | PASS | `created_at`/`updated_at`/`completed_at`/`deleted_at` stored UTC (`timestamptz`, `DateTimeKind.Utc`); behavior methods take an injected `utcNow`. Europe/Warsaw is the reference zone but NO date parsing / Today / Upcoming is in this slice's scope (FR-004 timestamps only). |
| XI | Privacy | PASS | **Retention stance for the new soft-deleted (undo-window) data is explicit**: a soft-deleted task is a `deleted_at` tombstone that is hard-deleted by the 30-second reaper (not retained indefinitely); it is never returned to any caller in the interim. The reaper is restore-aware for the future slice-014 path. **Account-deletion erasure seam — DECIDED: `ON DELETE CASCADE`.** Slice 002 adds `tasks.created_by → users(id)` NOT NULL; slice-001's `DeleteAccountHandler` HARD-deletes the `User` row. The FK is configured `ON DELETE CASCADE`, so deleting an account erases its personal tasks atomically with the `User` row. For personal/never-shared tasks, cascade-delete **IS** complete erasure (nothing is orphaned or retained) — fully satisfying Constitution XI. XI's "reassign `createdBy`/assignee references to the tombstone" rule targets SHARED / thread-anchoring content, which does not exist until sharing (slice 007+); at that point the erasure coordinator will reattribute SHARED tasks to `UserId.Tombstone` instead of cascade-deleting them. Slice-001's `AccountDeletionRequestedHandler` therefore stays a deliberate no-op this slice (the FK performs task erasure — no coordinator change here). A regression test asserts that deleting an account that owns tasks succeeds and the tasks are gone. |
| XII | Security | PASS | Title is plaintext, stored verbatim UTF-8, rendered ESCAPED (React default; never `dangerouslySetInnerHTML`) — raw HTML injection impossible (FR-099); HTML sanitization of rich content is slice 009. The slice-001 CSP/security headers and BFF→API HMAC carrier are reused unchanged; no new secrets. Structured logs carry only `ErrorCode`/`Method`/`Path` (the existing `ProblemDetailsMiddleware` posture), never the carrier or task content. |

## Project Structure

### Documentation (this feature)

```text
specs/002-task-capture/
├── plan.md              # This file
├── research.md          # Phase 0: design decisions (R1–R17)
├── data-model.md        # Phase 1: Task (ENT-01) entity, position/version, reaper
├── quickstart.md        # Phase 1: validation guide (delta over slice 001)
├── contracts/
│   └── openapi.yaml     # Phase 1: API contract (slice 002 task endpoints + version_conflict)
└── tasks.md             # Phase 2 (/speckit-tasks — not yet created)
```

### Source Code (repository root)

All paths below are **additive** over the existing slice-001 tree. Files marked **(MODIFY)** are existing slice-001 files that gain a small, surgical change.

```text
apps/
├── api/                                          # ASP.NET Core 9 (C#, DDD, Wolverine)
│   ├── src/
│   │   ├── TaskFlow.Api/
│   │   │   ├── Program.cs                         # (MODIFY) AddScoped<ITaskRepository,TaskRepository>; opts.UseFluentValidation();
│   │   │   │                                      #          PublishMessage<ReapDeletedTask>().ToLocalQueue("task-reaper");
│   │   │   │                                      #          add ReapDeletedTask to the auth-policy exclusion predicate
│   │   │   ├── Endpoints/
│   │   │   │   └── TaskEndpoints.cs               # NEW: public static Wolverine.Http endpoints → IMessageBus.InvokeAsync
│   │   │   │                                      #      (PUT /api/tasks/{id}, GET /api/tasks, PATCH .../{title,status,position}, DELETE)
│   │   │   ├── Middleware/
│   │   │   │   └── ProblemDetailsMiddleware.cs    # (MODIFY) add NotFoundException→404 not_found, VersionConflictException→409 version_conflict
│   │   │   └── OpenApi/
│   │   │       └── TaskFlowDocumentTransformer.cs # (MODIFY) Task component schema; add version_conflict to ErrorCodes[];
│   │   │                                          #          task operationIds + documented 404/409/422 responses
│   │   │                                          #          (403 NOT emitted by any task op this slice — reserved for slice-007 shared-project role denial)
│   │   ├── TaskFlow.Domain/
│   │   │   └── TaskManagement/                    # NEW bounded context
│   │   │       ├── Task.cs                        # ENT-01 aggregate root (AggregateRoot<TaskId>)
│   │   │       ├── TaskId.cs                      # Strongly-typed id (no server New(); From(Guid) for rehydration)
│   │   │       ├── TaskStatus.cs                  # enum backlog|todo|in_progress|done|cancelled
│   │   │       └── Events/
│   │   │           └── ReapDeletedTask.cs         # Scheduled reaper message (TaskId, DeletedAtInstant)
│   │   ├── TaskFlow.Application/
│   │   │   ├── Errors/                            # NEW (or co-located with Authorization)
│   │   │   │   ├── NotFoundException.cs           # → 404 not_found
│   │   │   │   └── VersionConflictException.cs    # → 409 version_conflict
│   │   │   └── TaskManagement/
│   │   │       ├── ITaskRepository.cs             # persistence seam (mirrors IUserRepository)
│   │   │       ├── Commands/
│   │   │       │   ├── CreateTask.cs              # command + static handler (insert-if-not-exists by id)
│   │   │       │   ├── RenameTask.cs              # + CreateTaskValidator/RenameTaskValidator (FluentValidation)
│   │   │       │   ├── SetTaskDone.cs             # desired-state status command
│   │   │       │   ├── ReorderTask.cs
│   │   │       │   ├── DeleteTask.cs              # soft-delete + publish scheduled ReapDeletedTask
│   │   │       │   └── ReapDeletedTaskHandler.cs  # idempotent, restore-aware hard-delete
│   │   │       └── Queries/
│   │   │           └── GetMyTasks.cs              # query + handler (ownership-scoped, soft-delete-excluding)
│   │   └── TaskFlow.Infrastructure/
│   │       └── Persistence/
│   │           ├── AppDbContext.cs                # (MODIFY) public DbSet<Task> Tasks => Set<Task>();
│   │           ├── TaskRepository.cs              # NEW (mirrors UserRepository)
│   │           ├── Configurations/
│   │           │   └── TaskConfiguration.cs       # NEW (HasConversion id/status/created_by; IsConcurrencyToken;
│   │           │                                  #      position UseCollation("C"); partial index; FK on-delete; Ignore DomainEvents)
│   │           └── Migrations/
│   │               └── *_AddTasks.cs              # NEW EF Core migration (tasks table + partial COLLATE "C" index)
│   └── tests/
│       ├── TaskFlow.UnitTests/
│       │   └── Domain/TaskManagement/
│       │       └── TaskTests.cs                   # aggregate invariants
│       └── TaskFlow.IntegrationTests/
│           └── TaskManagement/                    # allow + deny per handler (reuse IntegrationTestBase + TestJwtHelper)
│               ├── CreateTaskTests.cs             # allow, foreign-id→404, idempotent-replay, UUIDv7-id
│               ├── GetMyTasksTests.cs             # allow, deny (another user excluded, soft-deleted excluded, no-JWT 401)
│               ├── RenameTaskTests.cs             # allow, deny, stale-version→409, >500/empty→422
│               ├── SetTaskDoneTests.cs            # allow, deny, stale-version→409, completed_at stamp/clear
│               ├── ReorderTaskTests.cs            # allow, deny, stale-version→409, equal-rank tie-break
│               ├── DeleteTaskTests.cs             # allow, deny, idempotent second-delete, reaper hard-delete
│               └── AccountDeletionCascadeTests.cs # REGRESSION: deleting an account that owns tasks succeeds AND the tasks are gone
│                                                  #   (FK ON DELETE CASCADE = complete erasure). May instead extend slice-001's
│                                                  #   DeleteAccountTests.cs — wherever the account-deletion path is exercised.
│
└── web/                                           # Next.js 15 (App Router, TS strict)
    ├── src/
    │   ├── app/(app)/
    │   │   └── page.tsx                           # (MODIFY) replace static placeholder with live task list + empty Inbox (EC-01)
    │   ├── lib/
    │   │   ├── id.ts                              # NEW: UUIDv7 minter wrapper (must emit v7, auditable)
    │   │   ├── position.ts                        # NEW: generateKeyBetween wrapper (pinned alphabet)
    │   │   └── api/
    │   │       ├── client.ts                      # (MODIFY) extend ERROR_UX map with version_conflict (FR-049)
    │   │       └── generated/schema.d.ts          # (REGEN) pnpm gen:api after API changes; CI-diff-gated
    │   ├── components/tasks/
    │   │   ├── TaskCapture.tsx                    # `C` capture surface (Dialog contract; synchronous focus ≤16ms)
    │   │   ├── TaskList.tsx                       # role=listbox + aria-activedescendant + @tanstack/react-virtual
    │   │   ├── TaskRow.tsx                        # role=option; inline rename (E); aria-selected indicator
    │   │   └── ShortcutsHelp.tsx                  # `?` overlay (Dialog contract); static shortcut table
    │   ├── hooks/
    │   │   ├── useTasks.ts                        # GET /api/tasks query (['tasks'] key)
    │   │   ├── useTaskMutations.ts                # optimistic create/rename/toggle/reorder/delete; 409 reapply
    │   │   └── useGlobalShortcuts.ts              # app-shell keydown gate (activeElement suppression, FR-031/EC-08)
    │   └── components/ui/
    │       └── Toast.tsx                          # (MODIFY) add a small queue/auto-dismiss + coalescing layer
    └── tests/
        ├── unit/                                  # Vitest: id.ts is-v7, position between(), shortcut gate, optimistic recipe
        └── e2e/
            └── tasks.spec.ts                      # NEW Playwright: capture, Esc-cancel, nav, toggle, rename,
                                                   #   soft-delete + rollback-in-place, reorder, ? overlay, single-key
                                                   #   suppression (AS-09), virtualization-focus (scroll selection out by wheel/scrollbar →
                                                   #   selected row stays mounted/addressable for aria-activedescendant AND ↑/↓ still works)
```

**Structure Decision**: Reuses the slice-001 monorepo layout (`apps/web` + `apps/api`, four-project DDD backend). The new code is a `TaskManagement` bounded-context namespace mirroring `IdentityAccess`: `Task`/`TaskId`/`TaskStatus` in Domain; `ITaskRepository` + six static command/query handlers in Application; `TaskEndpoints` (thin Wolverine.Http adapters delegating to the bus) in Api; `TaskConfiguration` + `TaskRepository` + the `AddTasks` migration in Infrastructure. On the web, task UI is a `components/tasks` cluster plus three hooks (query, optimistic mutations, global shortcut gate) and two tiny lib wrappers (`id.ts`, `position.ts`). The global authorization wiring, proxy, session, Dialog/LiveRegion/Toast primitives, and OpenAPI pipeline are reused unchanged except for the surgical `(MODIFY)` points above.

## Key Design Decisions

These summarize and cross-reference `research.md` (R1–R17), `data-model.md`, and `contracts/openapi.yaml`. **Where the consolidated research inputs diverge, the written artifacts are authoritative** — most notably, DELETE is version-free (see Soft-delete below), superseding the broader "every mutation carries `version`" phrasing in some inputs.

### Task aggregate & state-stored persistence (R1, ADR-0003 Decision 5)

`Task : AggregateRoot<TaskId>` in the new `TaskManagement` context, house-styled exactly like slice-001's `User`: private EF materialization ctor, static `Create(TaskId id, UserId createdBy, string title, string position, DateTime utcNow)`, behavior methods (`Rename`, `MarkDone`, `MarkBacklog`, `Reorder`, `SoftDelete`) that take an injected `utcNow`, stamp `UpdatedAt`, and bump `Version`. Invariants live in the aggregate (status defaults `backlog`; `completed_at` iff `done`; immutable `createdBy`; soft-delete idempotent; title trimmed-non-empty ≤500 with an `ArgumentException` backstop). Authorization stays OUT of the aggregate (ADR-0003 Decision 6). The full FR-003 status enum is stored from day one (only `backlog`/`done` reachable) to avoid a later enum-widening migration. Seven reserved nullable forward-compat columns (description, priority, due_date, due_has_time, project_id, cycle_id, recurrence_rule) are mapped but unused — no nav properties, no labels/assignees join tables this slice.

### Client UUIDv7 idempotency (R2, R13)

The id is **client-minted (UUIDv7)** and carried in the route: `PUT /api/tasks/{id}` is insert-if-not-exists. (a) no row → insert; (b) row owned by caller → idempotent replay, return the existing row UNCHANGED (create is NOT a replace; edits go through the dedicated commands); (c) row owned by a different user (or a reused tombstoned id) → `404 not_found` (no enumeration oracle). The DB PK on `id` is the race backstop (a concurrent double-insert surfaces as a unique-violation re-resolved through the same find-then-decide path). The web must use a true `v7()` minter (wrapped in `lib/id.ts`), never `crypto.randomUUID()` (v4) — v4 breaks newest-first seeding, the time-ordered idempotency story, and the `ORDER BY position, id` tie-break.

### Optimistic-concurrency `version` token (R4)

A mapped `int Version` (`.IsConcurrencyToken()`, NOT Postgres `xmin` — kept out of the wire contract), incremented by the aggregate on every mutating method and returned in the DTO. Rename/status/reorder carry the caller's last-seen `version`; the handler load-then-compares (`row.Version != command.Version` → `VersionConflictException`), with EF's `DbUpdateConcurrencyException` as the interleaved-race backstop. Both map to **HTTP 409 `version_conflict`** — a NEW additive code, explicitly NOT `conflict_lww` (which means a last-write-wins overwrite SUCCEEDED, the opposite semantics, reserved for slice-014 undo). The web reapplies intent ONCE per operation on 409 (rename re-stamps the typed title; toggle no-ops if the server already reflects intent; reorder recomputes the rank from fresh neighbors), capped to prevent livelock.

### `position` / reorder model (R5, R6, R7)

`position` is a **lexicographic fractional rank string** (`varchar COLLATE "C" NOT NULL`, fractional-indexing alphabet), the sole `ORDER BY` key (ascending, **top-is-lowest**, pinned once). Create/reorder/seed all reduce to `between(left, right)` writing ONLY the moved row — O(1), no whole-list renumber. This is chosen over `double`+midpoint specifically because a `double`'s precision-exhaustion fallback would renumber N rows and bump N versions → a **per-row-version conflict storm**; a rank string grows a character instead, so that path never exists. `position` is client-authoritative on create; the server is a **format-validator only**, the sole writer under the version guard (no TS↔C# byte-identical-generator obligation). **`COLLATE "C"` is mandatory on BOTH the column AND its index** (else ordering drifts silently between TS code-unit order and Postgres default collation). Newest-first seed = `between(null, head)`; empty-list first task = `between(null, null)`. No unique constraint on `position`; equal ranks tie-break by UUIDv7 `id` (canonical-hex string order = Postgres `uuid` byte order). The serving index is one **partial composite** `ix_tasks_created_by_position` on `(created_by, position)` `WHERE deleted_at IS NULL`.

### Ownership-scoped list query & no pagination (R7)

`GET /api/tasks` returns the caller's FULL non-deleted set, scoped in the query: `WHERE created_by = currentUser.Id AND deleted_at IS NULL ORDER BY position, id`. No server pagination — the client virtualizes 10k rows (SC-010/SC-011). The lean DTO (`id, title, status, position, version, createdAt, updatedAt, completedAt`) keeps the single-payload serialization tight; `deleted_at` and all reserved columns are never exposed.

### Desired-state toggle (R3)

`PATCH /api/tasks/{id}/status` accepts the **desired** state (`done`|`backlog`) plus `version`, not a blind flip — so it is idempotent under optimistic retry (two retries of one Space keypress don't cancel out). `done` stamps `completed_at`; `backlog` clears it. The request enum is `done|backlog` this slice; the response exposes the full FR-003 enum for forward-compat.

### Soft-delete + 30s reaper (R8); DELETE is version-free (R4)

`DELETE /api/tasks/{id}` soft-deletes (sets `deleted_at`, bumps `version`, saves) and publishes a Wolverine **scheduled** `ReapDeletedTask` delayed 30 s, enrolled in the same outbox transaction (mirrors slice-001's `AccountDeletionRequested` + `AutoApplyTransactions`; `ToLocalQueue("task-reaper")`, `DurabilityMode.Solo`). DELETE carries **NO `version`** and is idempotent — a caller re-deleting their OWN already-soft-deleted task is the idempotent no-op `204` (NOT 404; the 404 posture applies only to foreign/absent ids). This requires DELETE's load to be owner-scoped but **tombstone-inclusive** (NOT the generic `deleted_at IS NULL` filter), so the handler can tell an own-tombstone (→ 204) from foreign/absent (→ 404); the delete-rollback UX is about 404/network, not racing a concurrent edit, so a stale-delete 409 buys nothing — this **supersedes** the broader "every mutation carries version" phrasing in some research inputs. `ReapDeletedTaskHandler` is idempotent and **restore-aware**: it hard-deletes only if the row still exists AND `deleted_at` is non-null AND unchanged (so a slice-014 restore wins the race). The reaper runs off the durable queue with no `HttpContext` and is added to the `AuthorizationMiddleware` exclusion predicate alongside `AccountDeletionRequested`. The reaper is infrastructure, NOT a REST endpoint. The user-facing undo toast is slice 014.

### Deny-by-default ownership authorization (R9, R17, ADR-0005)

The global `AuthorizationMiddleware.Before` already enforces authentication on both pipelines — slice 002 adds NOTHING to that wiring. Every WRITE handler must resolve a foreign/absent/soft-deleted row to **`404 not_found` BEFORE** `RequireOwnership` could throw 403 — load-scoped-to-not-found (or remap the ownership branch's `ForbiddenException` to `NotFoundException`) — so no task op emits 403 in this slice; the query is scoped to `currentUser.Id`. This is the ONLY authorization path — the ownership branch (`createdBy = caller`); no shared-project branch. **Existence-disclosure posture**: a foreign/absent/soft-deleted single-item op returns **`404 not_found`**, NOT 403; `403 forbidden` is reserved for a future shared-project insufficient-role case (slice 007+) that does not exist in this slice. **Carve-out**: a caller re-deleting their OWN already-soft-deleted task is the idempotent **`204`** no-op (per DELETE's version-free idempotency), NOT 404 — the 404 posture applies to foreign/absent ids and to non-DELETE ops. To make this carve-out work, **DELETE's load is owner-scoped but TOMBSTONE-INCLUSIVE** (it does NOT apply the generic `deleted_at IS NULL` filter that non-DELETE writes use), so it can distinguish own-tombstone (→ 204 no-op) from foreign/absent (→ 404); the generic `WHERE created_by = caller AND deleted_at IS NULL → 404-if-not-found` load rule governs the non-DELETE write/read paths only. The deny tests assert the exact code per path (another user's task → 404, soft-deleted foreign → excluded/404, no-JWT → 401). This needed sign-off and is hereby adopted (R17).

### Title handling (R16, FR-099); FluentValidation wiring

Title is plaintext, stored verbatim UTF-8, rendered escaped (React default). Validation (non-empty-after-trim, ≤500) at BOTH boundaries: Zod (client composer) and FluentValidation (server). Slice 001 references the FluentValidation package but wires no validator and no Wolverine middleware; slice 002 adds `WolverineFx.FluentValidation` (`opts.UseFluentValidation()`) + `CreateTaskValidator`/`RenameTaskValidator`, so a violation throws `ValidationException` → **422 `validation_failed`** (with a field-level `errors` map) via the existing middleware. The aggregate keeps an `ArgumentException` backstop.

### `version_conflict` — three-point synchronized amendment (R15)

Following the AS-BUILT slice-001 pattern (C# transformer + per-slice checked-in YAML), NOT the never-implemented ADR-0009 composed-document vision. The new additive `version_conflict` (409) lands in lockstep across: (1) `ProblemDetailsMiddleware.Map` (add `NotFoundException`→404, `VersionConflictException`→409 — today it maps only 401/403/422/500); (2) `TaskFlowDocumentTransformer.ErrorCodes[]`; (3) `contracts/openapi.yaml` errorCode enum; and the web `ERROR_UX` map. The regen-and-diff CI gate guarantees `version_conflict` lands in the generated `errorCode` union (so any web reference to it is type-checked); `ERROR_UX` exhaustiveness over that union is then enforced **at compile time** by typing the map `satisfies Record<ErrorCode, ErrorUx>` (`ErrorCode` derived from the generated `ProblemDetails.errorCode` union), so `tsc` — the existing TS-strict CI type-check — fails if any code is unmapped (no bespoke runtime test needed). The compile-time retype is applied in US1 right after the first `pnpm gen:api`, since `version_conflict` only enters the generated union then. The `TaskResponse` DTO MUST include `version` to round-trip the token, else 409 can never be exercised.

### BFF capture, virtualized list & optimistic mutations (R10, R11, R12, R14)

Capture is a global `C` keydown → `Dialog.tsx` with synchronous focus on the title input (≤16 ms; no lazy-import on the path). The list is `role=listbox` (`tabIndex=0`, holds DOM focus) + `role=option` rows with stable ids + `aria-activedescendant` — NOT roving tabindex, which breaks when the selected row unmounts under virtualization. `@tanstack/react-virtual` windows the 10k rows; ↑/↓ update `selectedIndex` + `scrollToIndex`. The virtualizer **force-includes the selected index in the rendered window** (overscan plus an explicit always-render-selected entry) so the selected row stays mounted even when scrolled out of view by wheel/scrollbar — guaranteeing `aria-activedescendant` always points at a live DOM node (a screen reader can't resolve an unmounted option). A single app-shell keydown gate checks `document.activeElement` ∈ {input, textarea, contenteditable, role=textbox} → only modifier chords pass (FR-031/EC-08/AS-09). Mutations use the TanStack `onMutate`/`onError`/`onSettled` recipe on one `['tasks']` key (snapshot the full ordering so a rolled-back delete reappears in place). All traffic rides the slice-001 proxy unchanged; the typed client is regenerated and CI-diff-gated.

## Complexity Tracking

One intentional deferral (the user-facing undo affordance) plus one now-DECIDED cross-slice seam (account-deletion erasure) are named here so a compliance reviewer reads them as decisions, not gaps. Neither is a constitution violation; both touch a normative MUST and warrant explicit tracking.

| Item | Why deferred / needed | Resolution & where it lands |
|------|------------------------|------------------------------|
| User-facing 30s **undo affordance** (toast + restore) deferred to slice 014, while Constitution VII makes destructive-action undo a ≥30 s MUST | The spec splits the work: the server-authoritative recoverability window (the `deleted_at` tombstone + restore-aware reaper) is the data-integrity guarantee and ships here; the user-facing undo *UX* (the toast and the restore command, which is a normal LWW write per Principle VII) is a discrete slice-014 deliverable. Shipping the tombstone first makes slice 014 a pure retrofit with no schema change. | The 30 s window + reaper ship in slice 002 (satisfying the data-layer MUST). The undo toast + `RestoreTask` command + `conflict_lww` LWW-overwrite surfacing land in slice 014. The reaper is already restore-aware so the retrofit cannot lose a restore race. |
| Account-deletion **erasure semantics for tasks** — `tasks.created_by → users(id)` NOT NULL vs. slice-001's hard-delete of the `User` row (**DECIDED: `ON DELETE CASCADE`**) | Slice 002 introduces the first table with a NOT NULL FK to `users`. `DeleteAccountHandler` hard-deletes the `User` row. Under default RESTRICT, deleting an account that owns tasks would FK-violate — slice 002 would silently break slice-001's deletion path. This is the privacy/erasure cross-slice seam (Principle XI), and it must be resolved (not left open) before this slice can ship. | **DECIDED: the FK is `ON DELETE CASCADE`.** Deleting an account erases its personal tasks atomically with the `User` row. For personal/never-shared tasks, cascade-delete IS complete erasure — nothing is orphaned or retained, so Constitution XI is fully satisfied. XI's "reassign `createdBy`/assignee references to the tombstone" rule targets SHARED / thread-anchoring content, which does not exist until sharing (slice 007+); at that point the erasure coordinator will reattribute SHARED tasks to `UserId.Tombstone` instead of cascade-deleting them. Because the FK performs task erasure directly, slice-001's `AccountDeletionRequestedHandler` stays a deliberate no-op this slice (no coordinator change here). Hard constraints: the `AddTasks` migration MUST set the FK to `ON DELETE CASCADE` (never default RESTRICT), and a regression test (`AccountDeletionCascadeTests`, or an extension of slice-001's `DeleteAccountTests`) MUST assert that deleting an account that owns tasks succeeds AND the tasks are gone. |

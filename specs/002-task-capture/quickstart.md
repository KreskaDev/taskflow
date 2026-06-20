# Quickstart: Task Capture (002)

Slice 001 (Accounts & Auth) already bootstrapped the monorepo, Postgres, the BFF→API
carrier, the OpenAPI pipeline, deny-by-default authorization, and the Playwright e2e
harness. This slice **adds** the Task aggregate and its CRUD + ordering surface — it does
not re-bootstrap. Everything below is a **delta** over the slice-001 quickstart.

## Prerequisites

No new prerequisites beyond slice 001:

- Node.js 22 LTS + pnpm 9.x
- .NET 9 SDK
- Docker + Docker Compose

A working slice-001 setup (an admitted Google account that can sign in and reach the
empty workspace) is the only precondition — task capture rides the existing session and
proxy.

## Environment Setup

**No new environment variables.** Task capture adds no secrets and no runtime config —
it reuses the slice-001 `.env` (session, JWT signing key, admission, `API_INTERNAL_URL`,
etc.) verbatim. If slice 001 runs, slice 002 runs.

## New Dependencies

### Web (`apps/web`)

Three additive dependencies (none currently in `package.json`):

```bash
cd apps/web
pnpm add @tanstack/react-virtual   # list windowing for 10k rows (SC-010/SC-011)
pnpm add uuid                      # UUIDv7 minter — uuid v11 exposes v7(); wrapped in lib/id.ts
pnpm add fractional-indexing       # generateKeyBetween() for the position rank (FR-102)
```

> CRITICAL: the id minter MUST emit a **v7** (time-ordered) UUID — do NOT use
> `crypto.randomUUID()` (v4). A v4 id silently breaks newest-first position seeding and
> the time-ordered idempotency story. The minter is isolated behind `apps/web/src/lib/id.ts`
> so the "must be v7" contract is auditable in one place.

### API (`apps/api`)

One additive package (not present in slice 001):

```bash
cd apps/api/src/TaskFlow.Api    # (or the project that wires Wolverine)
dotnet add package WolverineFx.FluentValidation
```

Wired in `Program.cs` via `opts.UseFluentValidation()` so a thrown `ValidationException`
maps to `422 validation_failed` through the existing `ProblemDetailsMiddleware`.

### New EF Core migration

The Task table is added by a new code-first migration (the schema is the source of truth,
Constitution VI):

```bash
cd apps/api
dotnet ef migrations add AddTasks --project src/TaskFlow.Infrastructure --startup-project src/TaskFlow.Api
```

The migration is applied automatically on API startup (`db.Database.MigrateAsync()` in
`Program.cs`), exactly as the slice-001 `InitialCreate` migration is.

## Start

Same flow as slice 001; the only deltas are `pnpm install` (to pull the new web deps) and
the auto-applied `AddTasks` migration:

```bash
# 1. Infrastructure
docker compose -f docker/docker-compose.yml -f docker/docker-compose.dev.yml up -d postgres

# 2. Backend (auto-applies the AddTasks migration on startup)
cd apps/api && dotnet run --project src/TaskFlow.Api

# 3. Frontend (installs @tanstack/react-virtual, uuid, fractional-indexing)
cd apps/web && pnpm install && pnpm dev
```

Sign in with an admitted account, land on the home page, and press `C` to capture a task.

## Run Tests

Same commands as slice 001; the new coverage lives under the `TaskManagement` namespaces:

```bash
# Backend unit + integration (Testcontainers — needs Docker)
#   - TaskFlow.UnitTests/Domain/TaskManagement/TaskTests.cs        (aggregate invariants)
#   - TaskFlow.IntegrationTests/TaskManagement/*Tests.cs           (allow + deny per handler)
cd apps/api && dotnet test

# Frontend unit (Vitest)
cd apps/web && pnpm test

# E2E (requires running app)
cd apps/web && pnpm e2e
```

Every Task command/query handler ships **both** an allow and a deny test (SC-013/SC-016).

## OpenAPI Client Regeneration

**Required for this slice** — the Task endpoints add new DTOs (`Task`/`TaskResponse`, the
request bodies) and the `version_conflict` error code to the ProblemDetails enum, so the
generated TypeScript client must be regenerated:

```bash
cd apps/web && pnpm gen:api
```

This fetches `/openapi/v1.json` from the running API, regenerates
`src/lib/api/generated/schema.d.ts`, and diffs against the committed version. CI fails on
drift. The regen lands `version_conflict` in the generated `errorCode` union, so any web
*reference* to it is type-checked. `ERROR_UX` exhaustiveness over that union is enforced **at
compile time** by typing the map `satisfies Record<ErrorCode, ErrorUx>` (`ErrorCode` derived
from the generated `ProblemDetails.errorCode` union), so `tsc` — the existing TS-strict CI
type-check — fails if any code is unmapped (no bespoke runtime test needed).

## Validation Scenarios

Ref: `specs/002-task-capture/spec.md` acceptance scenarios and slice rules. Mechanics for
`Space` (toggle), `E` (rename), and `Del` (delete) ship here even though their *canonical*
acceptance scenarios are owned by later slices (see spec Provenance); their tests are part
of this slice and appear below.

| Scenario | Steps | Expected |
|---|---|---|
| **US-01.AS-01** Capture opens | On any view, press `C` | A title input appears, focused, within 16ms of keypress (synchronous mount + `.focus()`, no network) |
| **US-01.AS-06** Create (no date) | In the capture input, type a title (no date expression), press Enter | Task created with no due date; row paints optimistically at the top; client-minted UUIDv7 `id` sent to `PUT /api/tasks/{id}` |
| **US-01.AS-07** Cancel capture | In the capture input, press Esc | Creation cancelled, no task created, focus returns to the invoker (the list/previous view) |
| **US-08.AS-03** Navigate | Open the list, press ↑/↓ | Selection moves between tasks with a visible indicator (styled on `aria-selected` of the active `role=option`, not `:focus`) |
| **US-08.AS-07** Shortcuts help | Press `?` | A help overlay (Dialog focus contract) lists all slice-002 shortcuts; Esc dismisses and returns focus |
| **US-08.AS-09** Single-key suppression | Focus a text input (capture/rename), press `C`/`E`/`1` | The character is typed as text, not interpreted as a shortcut; only modifier chords (Ctrl/Cmd/Alt) pass (FR-031/EC-08) |
| **FR-102** Manual reorder | Select a task, press Alt+↑ / Alt+↓ | Task moves one position; the new persisted `position` (rank between neighbors) is written via the reorder endpoint under the `version` rule; order survives refetch |
| **FR-102** Newest-first seed | Create several tasks | Each new task appears at the top of the list (seeded `between(null, head)`), consistent with the Inbox default (FR-021) |
| **Toggle-done** (Space mechanic) | Select a task, press Space; press Space again | First press: `status = done`, `completed_at` stamped; second press: `status = backlog`, `completed_at` cleared (done↔backlog, FR-003) |
| **Inline rename** (`E` mechanic) | Select a task, press `E`, edit the title, press Enter | Title updated (≤500, non-empty after trim) under the `version` rule; >500 or empty → `422 validation_failed` with field-level errors |
| **Soft-delete** (`Del` mechanic) | Select a task, press Del | Row removed optimistically; server sets `deleted_at`; the task is excluded from the list and counts (FR-097) |
| **Soft-delete rollback** | Trigger a delete the server rejects (404/network — no 403 this slice) | The optimistically-removed row reappears **in its original position** with an FR-049 message + retry recovery |
| **30s reaper** | Soft-delete a task, wait > 30s | A server-side scheduled job hard-deletes the tombstoned row; a row restored within the window (slice 014) is skipped (idempotent, restore-aware) |
| **Stale `version` → 409** | Edit/toggle/reorder a task carrying an out-of-date `version` | `409 version_conflict` ProblemDetails; client refetches the list and reapplies the user's intent once |
| **Idempotent create** | Re-send the same `PUT /api/tasks/{id}` (same id + owner + title) | Success returning the existing row (insert-if-not-exists by id); a foreign id → `404 not_found` (no existence oracle) |
| **EC-01** Empty Inbox | Sign in with no tasks | Accessible empty-Inbox state with a hint to press `C` |
| **EC-06 / SC-010 / SC-011** Virtualization | Load 10,000 tasks; select a row, then scroll the selection out of the rendered window by **wheel/scrollbar** (not ↑/↓) | List scrolls at 60fps; tab memory < 300MB. The selected row **stays mounted and addressable** — its `role=option` node still exists in the DOM and the container's `aria-activedescendant` still resolves to a live node (the virtualizer force-includes the selected index) — so a screen reader can announce the active option; ↑/↓ still navigates afterward |
| **SC-016** Allow (per handler) | Caller acts on their own `createdBy` task (create/list/rename/toggle/reorder/delete) | Operation succeeds |
| **SC-016** Deny (per handler) | Caller targets another user's task; and a soft-deleted task; and a request with no JWT | Another user's task → `404 not_found`; soft-deleted → excluded/`404`; no JWT → `401 unauthenticated` |

# Quickstart & Validation Guide: Labels (slice 006)

Validates the **US-08.AS-04 label selector** journey end-to-end against the real stack: create/rename/recolor/delete labels (Tier A, per-user), and add/remove labels on a task via the `L` selector entirely by keyboard, with **per-user (caller-scoped)** semantics on shared tasks. References `spec.md`, `data-model.md`, `research.md`, and `contracts/openapi.yaml`.

> This is a run/validation guide — not implementation. Bodies, validators, and full suites belong in `tasks.md`.

## Prerequisites

- The slice-002..008 stack boots locally (see the local-run & E2E runbook): PostgreSQL + the C# API on `:4311` (for `gen:api`), the Next.js web app, the fake-IdP/BFF.
- **ONE migration this slice** (`AddLabels`, research R10): two tables — `labels (id, owner_id, name, name_normalized, color NULL, created_at, updated_at)` (with a **plain** `(owner_id, name_normalized)` unique index — the normalized column backs case-insensitive uniqueness; EF can't model a functional `lower(name)` index, R7) and `task_labels (task_id, label_id)` (composite PK, both FKs `ON DELETE CASCADE`). **FR-051 is LIVE** — confirm the CI deploy job's `scripts/backup.sh → dotnet ef database update → scripts/restore-test.sh` gate covers it, and that **exactly one** new file appears under `TaskFlow.Infrastructure/Persistence/Migrations/` in this slice's diff (no `migrationBuilder.Sql` — the unique index is fully EF-generated).
- Typed client regenerated: `cd apps/web && pnpm gen:api` → `schema.d.ts` gains `createLabel`/`listLabels`/`updateLabel`/`deleteLabel`/`setTaskLabels` + `TaskResponse.labels`; `pnpm typecheck` green.
- Reference identities: a caller `C` who owns labels `{L1, L2}`; a personal task `Tp` owned by `C`; a **shared** project `P` with members `{owner O, editor C, viewer V}` and a task `Ts` under `P`; a stranger `X` (non-member).

## Setup

```bash
# Backend (from repo root)
dotnet build
dotnet test                       # xUnit (Label create/edit) + Testcontainers integration
                                  #   (create/list/update/delete allow + deny; setTaskLabels allow +
                                  #    SC-016 deny matrix; caller-scoping; labels-survive-project-move)
# Web (from apps/web)
pnpm install
pnpm gen:api                      # API must be on :4311
pnpm test                         # Vitest (optimistic setTaskLabels + createLabel + selector + chips)
pnpm e2e                          # Playwright — US-08.AS-04 + a11y audit
```

## Validation scenarios

### A. The `L` label selector (US-08.AS-04)

| # | Action | Expected |
|---|---|---|
| AS-04 | Select a task, press `L` | The label selector dialog opens with focus inside it (FR-101); it lists the caller's labels as keyboard-toggleable options + a "type a name to create" input |
| add | Toggle a label on (or type a new name + Enter), commit | The label is applied; the chip paints optimistically <16 ms (SC-003); `setTaskLabels` persists; `Task.version` is **unchanged** (R2) |
| remove | Toggle an applied label off, commit | The label is removed from the task; the chip disappears optimistically; the server reconciles |
| create-in-place | Type a brand-new name + Enter | A label is created (client-generated id, optimistic) AND applied to the task in one flow |
| esc | Press Esc | The selector closes, focus returns to the invoking task row (FR-101); no change committed |

### B. Label CRUD (Tier A — per-user ownership)

| # | Action | Expected |
|---|---|---|
| create | `PUT /api/labels/{id} { name, color? }` (client-gen id in path) | **200** — a label owned by `C`; appears in `GET /api/labels`. Re-`PUT`ting the same id is idempotent (returns the existing label, no error) |
| list | `GET /api/labels` | Only `C`'s labels, ordered by name (another user's labels are **absent** — per-user isolation) |
| rename+recolor | `PATCH /api/labels/{id} { name, color }` | **200** — both name and color updated (whole-object replace); the new name/color flow to every task's chip via the roster |
| delete | `DELETE /api/labels/{id}` | **204** — the label is gone AND its `task_labels` rows are cascade-removed (it vanishes from every task it was on) |

### C. Per-user semantics on a SHARED task (the cross-slice subtlety)

| # | Action | Expected |
|---|---|---|
| isolation-read | `O` applies their label to `Ts`; `C` reads `Ts` | `C` sees only `C`'s own labels on `Ts` — `O`'s labels are **absent** from `C`'s `TaskResponse.labels` (caller-scoped, R6) |
| isolation-write | `C` replaces their label set on `Ts` | Only `C`-owned `task_labels` rows change; `O`'s labels on `Ts` are **untouched** (R2) |
| move-keeps | Apply labels to a task, then move it to the Inbox / another project (`M` or the editor) | The labels **remain** — labels are project-independent; there is **no** clear-on-move (R5) |
| normal-view | View a labelled task in the project list / Inbox / Upcoming | The caller's label chips are present — `labels` is projected (batched join) on **every** task read path (R6) |
| today-view | View a labelled task in the **Today** view | The caller's label chips are present — the flattened `TodayTaskResponse` carries `labels` too (the one DTO the required-param defense doesn't auto-cover, R6 — explicitly wired + tested) |

## Server-validation & authorization table (the SC-016 deny matrix)

| Input | Expected |
|---|---|
| `C` creates a label | **200** — owned by `C` (allow) |
| Create/update with a duplicate `(owner, lower(name))` | **422 validation_failed** (field `name`) |
| Create/update with a non-preset `color` | **422 validation_failed** (field `color`) |
| `UpdateLabel`/`DeleteLabel` on a label **not owned** by the caller (or absent) | **404 not_found** (uniform existence-hide — no leak of another user's label ids) |
| `ListLabels` as `C` | Only `C`'s labels (another user's absent — per-user isolation) |
| `setTaskLabels` on a **personal** task owned by the caller, with caller-owned labels | **200** — applied (allow) |
| `setTaskLabels` on a **shared** task as editor/owner, with caller-owned labels | **200** — applied (allow) |
| `setTaskLabels` as a **viewer** on a shared task | **403 forbidden** (viewer read-only, FR-067 — a viewer cannot tag, intentional) |
| `setTaskLabels` as a **non-member** of the shared project | **404 not_found** (existence not disclosed) |
| `setTaskLabels` on a **personal task not owned** by the caller | **404 not_found** |
| `setTaskLabels` with a `labelId` **not owned** by the caller (or absent) | **422 validation_failed** (field `labelIds`, uniform; no row changed) |
| Duplicate / malformed `labelIds` | **422 validation_failed** |
| Any request without a valid session | **401 unauthenticated** |

## Cross-cutting checks

- **A11y (Principle II / SC-008)**: the selector follows the dialog focus contract (initial focus, trap, Esc, return focus, FR-101); a **visible focus indicator** on the options/create-input/chip affordance (FR-042); options are keyboard-toggleable with `aria-checked` (FR-043); chips show the label **name** (output-encoded; color never sole meaning, FR-044 — curated preset palette ≥4.5:1); the new single-key `L` does **not collide** with native AT bindings (FR-045 — same class as `E`/`M`/`A`); no hover-only affordances (FR-046); single-key `L` suppressed in the create/filter input (FR-031); `prefers-reduced-motion` honored (FR-047). The selector passes the WCAG 2.1 AA audit (SC-008).
- **Instant response (SC-003)**: `setTaskLabels` and `createLabel` paint optimistically <16 ms (snapshot → patch → rollback → settle-writeback); the label patch leaves `Task.version` untouched; a rollback is announced via the polite live region (FR-049/FR-101).
- **Authorization (Constitution IX / SC-013/SC-016)**: every new handler ships an allow + a deny test; the deny matrix above is mechanically covered; label CRUD is 404-uniform existence-hide; `setTaskLabels` is two-sided; per-user isolation on shared tasks is asserted.
- **Privacy (Constitution XI)**: account deletion clears a user's labels + their applications via the `owner_id → users(id)` (→ `label_id`) FK cascades; `LabelResponse` carries no `ownerId`; `TaskResponse.labels` is caller-scoped ids only.
- **Migration (Principle VII)**: confirm **exactly one** migration file (`AddLabels`, two tables) in the diff, read the generated SQL (the plain `(owner_id, name_normalized)` unique index + the two cascade FKs; no `migrationBuilder.Sql`), and that **FR-051's backup/restore-test gate** runs (LIVE this slice).
- **No event / no new code (R8/R9)**: confirm Program.cs message wiring is unchanged (no label event) and `ERROR_UX`/the `errorCode` enum is unchanged.

## Definition of done

- Every scenario passes (manual + automated).
- All five handlers ship allow + deny; the SC-016 deny matrix is mechanically covered; the caller-scoping + labels-survive-project-move invariants have dedicated assertions.
- `pnpm gen:api` clean; `pnpm typecheck`/`lint` green; `dotnet test` green; **exactly one** migration (`AddLabels`) + **FR-051 LIVE** confirmed; `ERROR_UX`/`errorCode` unchanged (no new code); Program.cs message wiring unchanged (no event).
- Create/rename/recolor/delete + the `L` add/remove flow all operate mouse-free (SC-001-style); the selector passes the WCAG 2.1 AA audit (SC-008).

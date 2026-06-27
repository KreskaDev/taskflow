# Quickstart & Validation Guide: Daily Planning (slice 005)

Validates the **US-02 Daily Planning Session** journey (AS-01..AS-08) and the **US-08** navigation mechanics (AS-01 `G I`, AS-02 `G U`) end-to-end against the real stack: the **Today** and **Upcoming** views, the `1`-`4` priority triage, the `T` reschedule, and the `E` task editor (`Ctrl+Enter` save / `Esc` discard). References `spec.md`, `data-model.md`, `research.md`, and `contracts/openapi.yaml` rather than duplicating them.

> This is a run/validation guide — not implementation. Bodies, validators, and full test suites belong in `tasks.md` and the implementation phase.

> ⚠ **Authorization scope (research open-question #1 — the BLOCKER)**: this slice realizes the **ownership** authorization branch in full; the shared-project **membership + role** branch (FR-066/FR-067) and the SC-016 **viewer-deny / non-member-deny** tests are a **named, not-yet-realized seam** — the `ProjectMembership` substrate is deferred to slice 007. The authorization section below validates the ownership allow+deny per handler; the two shared-project deny cases are listed as **BLOCKED** (not skipped silently). See `plan.md` → Complexity Tracking.

## Prerequisites

- The slice-002/003/004 stack boots locally (see the local-run & E2E runbook): PostgreSQL + the C# API on `:4311` (for `gen:api`), the Next.js web app, and the fake-IdP/BFF for an authenticated, admitted caller.
- **No migration this slice** (research R9): `priority`/`description` activate pre-existing mapped columns; NodaTime is boundary-computation only (no column remap, no `Npgsql.NodaTime`) → no EF migration is generated. **FR-051 is a named no-op** — confirm **no new file appears under `TaskFlow.Infrastructure/Persistence/Migrations/`** in this slice's diff (the failure mode is an accidental migration flipping FR-051 to LIVE).
- **`NodaTime` package added** to `TaskFlow.Application` (boundary computation only — research R1); `Npgsql.NodaTime` is NOT added.
- Typed client regenerated: `cd apps/web && pnpm gen:api` (API on `:4311`) → `schema.d.ts` gains the `getTodayTasks`/`getUpcomingTasks` reads, the `setTaskPriority`/`rescheduleTaskDueDate`/`editTask` ops, the `TodayResponse`/`UpcomingResponse` schemas, and `TaskResponse.priority` + `TaskResponse.description`; `pnpm typecheck` green.

> Reference "now" for the expected outcomes below: **2026-06-27 (Saturday), CEST (+02:00)**, instance zone `Europe/Warsaw`. In tests the Warsaw clock is injected (frozen) so Today/Upcoming membership is deterministic.

## Setup

```bash
# Backend (from repo root)
dotnet build
dotnet test                       # xUnit (Task.SetPriority/Reschedule/EditTask) + Testcontainers-Postgres integration
                                  #   (today/upcoming membership + R5 order + DST-boundary + authz allow+deny)

# Web (from apps/web)
pnpm install
pnpm gen:api                      # API must be on :4311
pnpm test                         # Vitest unit/component (optimistic surfaces + client membership recompute + DST parity)
pnpm e2e                          # Playwright (self-boots its own stack) — AS-01..AS-08, G I/G U, WCAG audit
```

## Validation scenarios

### A. Today view & priority triage (US-02 — AS-01..AS-04)

Seed several tasks across projects/priorities with today's, yesterday's (overdue), and tomorrow's due dates.

| # | Action | Expected |
|---|---|---|
| AS-01 | From any view, press **`G T`** | The Today view opens showing only tasks due **today in Europe/Warsaw** **plus** overdue-incomplete tasks (due before today, not done/cancelled — flagged **overdue**); done/cancelled and no-due-date tasks are NOT shown |
| AS-02 | View renders with tasks from multiple projects | Tasks are **grouped by project** (Inbox/unprojected = its own group); within each group sorted **priority P0 first → due time → createdAt → id**, with **NULL priority LAST** and a **date-only** task sorting as start-of-day (research R5) |
| AS-03 | Select a task, press **`Space`** | Status toggles to **done**, `completed_at` recorded; the row **leaves** Today (and Upcoming) — the optimistic paint is <16 ms, the client recomputes membership (R7) |
| AS-04 | Select a task, press **`1`** (then try `2`/`3`/`4`) | Priority changes to **P0** (`1`→P0, `2`→P1, `3`→P2, `4`→P3; P0 = highest) with immediate visual feedback (<16 ms); the row re-sorts within its group |

### B. Reschedule (`T`) & the task editor (`E`) (US-02 — AS-05..AS-08)

| # | Action | Expected |
|---|---|---|
| AS-05 | Select a today task, press **`T`**, type **"jutro"**, press Enter | A date input appears (dialog focus contract); the client parses the Polish phrase (slice-003 parser) → resolves the instant against Europe/Warsaw; the task's due date becomes **tomorrow** and it **disappears from Today** (moves into Upcoming) — <16 ms optimistic; the server re-validates the resolved instant |
| AS-06 | Select a task, press **`E`** | The task **editor** opens with the **title field focused**, allowing inline editing of title / description / priority / due date / project (dialog focus contract — initial focus, trap, Esc, return focus to the originating row) |
| AS-07 | In the editor, press **`Ctrl+Enter`** | Changes are saved **atomically** (whole-object replace) and the editor closes; focus returns to the task row |
| AS-08 | In the editor, make changes, press **`Esc`** | Changes are **discarded entirely** (no partial commit, no request sent); the editor closes; focus returns to the task row |

### C. Upcoming view & navigation (US-08 — AS-01/AS-02)

| # | Action | Expected |
|---|---|---|
| AS-02 (US-08) | From any view, press **`G U`** | The Upcoming view opens showing tasks for the **next 7 days** (the window `[start of tomorrow-Warsaw, start of (today+8)-Warsaw)`), **grouped by day**, groups ascending by date; each day's tasks follow the same R5 order |
| partition | Compare Today vs Upcoming for a task due **tomorrow** | It appears in **Upcoming, not Today** (Constitution X — the views partition the timeline, no overlap) |
| no-due | A task with **no due date** | Appears in **neither** Today nor Upcoming (it lives in the Inbox / project lists) |
| AS-01 (US-08) | From any view, press **`G I`** | The **Inbox** view opens (the slice-004 Inbox definition — `GET /api/tasks` narrowed to `project_id IS NULL`) |

### D. Zone-aware membership & DST boundary (Principle X / FR-092 — research R13)

| Case | Setup | Expected |
|---|---|---|
| same Warsaw day | A task due `2026-06-27T21:30:00Z` (= 27 Jun 23:30 Warsaw, CEST) | In **Today** on 2026-06-27 — membership is by the **Warsaw** calendar day, not the UTC day |
| Upcoming group key | A task due late on a Warsaw evening near the UTC midnight seam | Grouped under its **Warsaw** `LocalDate` (`YYYY-MM-DD`), **never** the truncated UTC date (the off-by-one the slice exists to prevent — research R1/R3) |
| DST boundary | A task due right at the late-Mar / late-Oct Warsaw DST transition | The day boundary is computed by the **tzdb library** (NodaTime server / `date-fns-tz` client), never fixed-offset arithmetic — the same fact on both tiers (FR-092). This test runs on **both** the server (Testcontainers) and the client (Vitest, frozen clock) to prove the identical-rule guarantee |

## Server-validation table (trust boundary)

| Input | Expected |
|---|---|
| `priority` outside `{P0,P1,P2,P3}` (and not null) on set-priority or edit | 422 `validation_failed` (closed set validated both tiers, research R2) |
| Reschedule/edit with `dueDate` set but `dueHasTime` null (or vice versa) | 422 `validation_failed` (the slice-003 pairing invariant, reused — research R4) |
| Reschedule/edit `dueDate` in a non-`Z` form (offset or unspecified kind) | 422 `validation_failed`, **NOT 500** (the slice-003 UTC-kind trust-boundary guard) |
| Reschedule/edit `dueDate` implausibly out of range (year 1500 / now + 50y) | 422 `validation_failed` (the slice-003 range rule) |
| `description` longer than 8000 chars on edit | 422 `validation_failed` (length validated both tiers, research R3) |
| `editTask` request **omitting** any editable key (`title`/`description`/`priority`/`dueDate`/`dueHasTime`/`projectId`/`version`) | 422 `validation_failed` — whole-object replace, an omitted field is never a silent null (research R4, mirrors slice-004 `EditProjectRequest`) |
| `editTask` `projectId` referencing a foreign/absent project | 404 `not_found` (reuses the move-to-project ownership check) |
| Stale `version` on set-priority / reschedule / edit | 409 `version_conflict` |
| Foreign/absent/tombstoned task `id` on any mutation | 404 `not_found` (existence not disclosed — the ownership posture) |

## Authorization (Constitution VIII + IX — every handler)

| Case | Expected |
|---|---|
| Caller reads Today/Upcoming, or set-priority / reschedule / edit / toggle-done **their own** task | Allowed — the row is owner-scoped (`created_by = caller`) |
| Caller targets **another user's** personal task id (priority / reschedule / edit) | **404 not_found** (existence not disclosed) — both an allow and a deny test exist **per handler** |
| Any request without a valid session | **401 unauthenticated** |
| ⚠ **SC-016 — a viewer attempting a mutation on a shared task** | **BLOCKED** — unwritable until the `ProjectMembership` substrate exists (slice 007); see the BLOCKER. Not skipped silently — tracked as blocked |
| ⚠ **SC-016 — a non-member reading another project's task** | **BLOCKED** — same reason (the membership arm is a named seam) |

## Cross-cutting checks

- **A11y (Principle II / SC-008)**: the task editor and the `T` reschedule input follow the dialog focus contract (initial focus, trap, Esc, return focus to the originating row, FR-101); priority is never the sole carrier of meaning (a P0–P3 label/text accompanies any color, FR-044 ≥4.5:1); no hover-only affordances (FR-046); `prefers-reduced-motion` respected (FR-047); `1`-`4`/`E`/`T` and other single-key shortcuts are suppressed in the editor fields and the `T` date input (FR-031); no AT-binding collision (FR-045); server-initiated toasts (an optimistic-write rollback) route through the polite live region without stealing focus (FR-101). The Today and Upcoming views pass the automated WCAG 2.1 AA audit (SC-008).
- **Instant response (SC-003)**: set-priority, reschedule, toggle-done, and edit paint optimistically <16 ms; a mutation that changes view membership (reschedule-to-tomorrow, toggle-done) recomputes membership **client-side** via the same Warsaw boundary helper the server uses (`lib/timezone.ts`, research R7) so the optimistic patch equals the authoritative result; the server reconciles / rolls back, with the rollback announced via the polite live region.
- **Time (Principle X / FR-092)**: Today/Upcoming membership is computed against `Europe/Warsaw` **identically on client and server** — the server in `WarsawDayBounds` (NodaTime, research R1), the client in `lib/timezone.ts`; DST by the library, never fixed-offset arithmetic; the SQL filters a plain UTC `due_date` range (zone-free). Per-user zones OOS-19.
- **Security (Principle XII)**: task `description` (markdown source) and `title` are React-escaped on render (FR-099) — **no markdown renderer this slice** (research R3/R12), so the description renders as escaped raw text; structured rejection logs carry `ErrorCode`/`Method`/`Path` only — never the title/description (FR-050). Slice-001 CSP / security headers + signed BFF→API carrier reused; no new secrets (FR-100).
- **Migration (Principle VII)**: confirm **no migration file** appears in this slice's diff (FR-051 named no-op, research R9); the backup-before-migrate hook stays in place for the next migrating slice.

## Definition of done

- Every scenario above passes (manual + automated).
- Today/Upcoming membership tests are deterministic against an injected Warsaw clock and cover due-today, overdue-in-Today, tomorrow-in-Upcoming-not-Today, no-due-in-neither, done/cancelled-excluded, the R5 deterministic order (NULL-priority-last, date-only-as-start-of-day), and a **DST-boundary** case on **both** tiers.
- Every new data handler ships an **allow** and a **deny** integration test through the real DB (deny = another user's personal task → 404). The two SC-016 shared-project deny tests are tracked as **BLOCKED** pending slice 007 (not faked).
- `pnpm gen:api` clean; `pnpm typecheck` green; `dotnet test` green; **no new migration file** in the diff (FR-051 no-op).
- `1`-`4`/`T`/`E`/`Space`/`Ctrl+Enter`/`Esc` and `G T`/`G I`/`G U` all operate mouse-free (SC-001); Today + Upcoming pass the WCAG 2.1 AA audit (SC-008).

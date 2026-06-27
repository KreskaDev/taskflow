# Data Model: Daily Planning (slice 005)

**Input**: `spec.md`, `research.md` (R1–R13), constitution v4.0.0, and the slice-002/003/004 substrate (the `tasks` table + its reserved forward-compatible columns, including the **already-mapped** `priority` and `description`).

This slice **introduces no new entity and no EF migration**. It **activates two reserved columns** on the existing `tasks` table (`priority`, `description` — both already mapped `string?` in `TaskConfiguration`, already declared on the `Task` aggregate as "Reserved (slice 005)"), adds the **Today** / **Upcoming** read models with **zone-aware membership** computed against `Europe/Warsaw`, and introduces **NodaTime server-side for boundary computation only** (no column remap, no Npgsql plugin). It is the first slice that computes "same calendar day in `Europe/Warsaw`" server-side — the responsibility slice 003 handed forward (003 plan Complexity Tracking).

---

## ⚠ Authorization scope is governed by an open blocker (research open-question #1)

The spec mandates the **shared-project membership + role** authorization branch (FR-066/FR-067) as in-scope, with SC-016 deny tests (viewer-mutation-deny, non-member-read-deny). **The `ProjectMembership` substrate does not exist** (deferred to slice 007 by slice 004 R11) and slice 005's "Depends on" lists only 003+004. **This data model realizes the OWNERSHIP branch in full** and structures the read/write handlers as a **dispatch-by-visibility seam** with the membership arm a **named, not-yet-realized branch** (research R10). The SC-016 viewer-deny / non-member-deny tests are **blocked** pending that substrate. See research.md → BLOCKER for the sequencing/spec resolution. No `ProjectMembership` is designed or pulled forward here (that is slice 007's owned scope; building it here is the scope creep YAGNI forbids).

---

## 1. Entities

### ENT-01 — Task (owned by slice 002) — `priority` + `description` activated

This slice does **not** own the Task entity. It activates two reserved columns by beginning to read/write them via new behavior on the aggregate. **No column is added or altered** — both pre-exist as nullable text from the slice-002 `AddTasks` migration and are already mapped.

| Field | Type | Change this slice | Constraints / validation |
|---|---|---|---|
| `Priority` | `string?` | **Activated** — read/written by `SetPriority` (R2/R4) and `EditTask` (R4). Column pre-exists (`HasColumnName("priority")`, nullable text). | Closed set `{P0, P1, P2, P3}` **or NULL** (NULL = unprioritized). Validated at BOTH trust boundaries: FluentValidation on the command (server), Zod enum at the editor input (client) — Constitution VI. Out-of-set → 422 `validation_failed`. |
| `Description` | `string?` | **Activated** — read/written by `EditTask` (R3/R4). Column pre-exists (`HasColumnName("description")`, nullable text). | Plain text (markdown **source**), trimmed; length ≤ 8 000 chars, validated both tiers. Over-long → 422 `validation_failed`. Stored verbatim; **no markdown renderer this slice** (R3) — output-escaped on render (R12). NULL = no description. |

All other Task fields are unchanged from slices 002/003/004. The remaining reserved columns (`CycleId`, `RecurrenceRule`) stay mapped-but-unused for their owning slices.

**Priority encoding (R2)** — wire + storage are the **token string** `"P0" | "P1" | "P2" | "P3"` (matching the existing `string?` CLR type → no enum-column migration). The keyboard mapping is fixed by the spec (AS-04): **`1` → P0, `2` → P1, `3` → P2, `4` → P3**, with **P0 = highest** urgency. NULL is the absence of priority and sorts **last** (R5). Encoded the same way `status` is (a wire string, not a DB enum).

**New aggregate behavior (mirrors existing `Task` methods — set field, `Touch` to stamp `UpdatedAt` + bump `Version`):**
- `SetPriority(string? priority, DateTime utcNow)` — validates membership in `{P0,P1,P2,P3}` or null; sets `Priority`; `Touch`. The `1`-`4` instant mutation (AS-04). A no-op-equal set still bumps `Version` (consistent with the other setters); the closed-set guard is belt-and-braces behind the command validator.
- `Reschedule(DateTime? dueDate, bool? dueHasTime, DateTime utcNow)` — sets `DueDate`/`DueHasTime` (the slice-003 pairing invariant — both set or both null — is enforced upstream by the reused validator); `Touch`. The `T` reschedule (AS-05); realizes the reschedule slice 003 deferred ("reschedule = slice 005").
- `EditTask(string title, string? description, string? priority, DateTime? dueDate, bool? dueHasTime, ProjectId? projectId, DateTime utcNow)` — **whole-object replace** of the editable fields, saved atomically (R4). Reuses `NormalizeTitle` for `title`, the closed-set guard for `priority`, the pairing invariant for the due fields, and **`MoveToProject` semantics internally** for `projectId` (no duplicate move logic). One `Touch`.

**Reused, unchanged:** `MarkDone`/`MarkBacklog` (the `Space` toggle, AS-03 — exercised from the Today view, no new command) and `MoveToProject` (slice 004 — `EditTask` reuses it internally for the project field).

---

## 2. Value objects / new types

- **No new persisted value object.** `priority` is a **validated string token** + the `SetPriority` behavior, not a value-object type (deliberately under-modelled; mirrors how `status` is a wire string). `description` is a plain `string`.
- **`WarsawDayBounds` (server time seam — R1)** — a small **in-process** helper (not persisted), the **one** place the Warsaw calendar-day boundary math lives, mirroring the client's `lib/timezone.ts`. Given "now" (a UTC `Instant`/`DateTime`) it produces UTC `DateTime` bounds:
  - `StartOfTodayUtc` — the UTC instant at the start of **today** in `Europe/Warsaw`.
  - `StartOfTomorrowUtc` — the UTC instant at the start of **tomorrow** in `Europe/Warsaw` (the Today/Upcoming split point).
  - `StartOfDayPlusUtc(int days)` — used for the Upcoming upper bound (`+8`).
  - `WarsawLocalDate(DateTime utcInstant)` — the **`LocalDate`** (calendar date in Warsaw) a given UTC instant falls on; used for the Upcoming **group key** (see R5/§6).

  Implemented with NodaTime: `DateTimeZoneProviders.Tzdb["Europe/Warsaw"]`, `LocalDate` → `ZonedDateTime` (start-of-day) → `Instant` → `DateTime` with `DateTimeKind.Utc`. DST is handled by the tzdb library, **not** fixed-offset arithmetic (Constitution X / FR-092).

---

## 3. NodaTime mapping note (the boundary-only adoption — R1)

**NodaTime is introduced this slice for boundary computation ONLY.** It does **not** remap any column and does **not** add the `Npgsql.NodaTime` plugin.

- The `timestamptz` columns (`due_date`, `created_at`, `updated_at`, `completed_at`, …) stay mapped **exactly as today**: `HasColumnType("timestamp with time zone")` over CLR `DateTime`. The EF model snapshot is therefore **unchanged** → **no migration is generated** by adopting NodaTime (§7).
- NodaTime types live **only inside `WarsawDayBounds`** (and the Application read-query handlers that call it). The conversion path is **`LocalDate`/`ZonedDateTime` → `Instant` → `DateTime` (UTC kind)**: the helper emits plain UTC `DateTime` bounds, after which the SQL range filter is **zone-free** — `due_date >= @lo AND due_date < @hi` over the unchanged `timestamptz` column. The `Instant ↔ DateTime(UTC)` boundary is crossed **inside the helper**; nothing downstream sees a NodaTime type.
- The Upcoming **group key** is a NodaTime **`LocalDate`** projected to a wire `date` string (`YYYY-MM-DD`) — the Warsaw calendar date, **distinct from `dueDate`** (the UTC instant). It is computed via `WarsawLocalDate(...)`, **never** by truncating the UTC instant's date portion (which would reintroduce the off-by-one-day bug across the Warsaw/UTC offset — the exact bug this slice exists to prevent).

**Package**: add `NodaTime` to `TaskFlow.Application` (and, if a domain time seam is preferred, a thin `TaskFlow.Domain` reference). Do **not** add `Npgsql.NodaTime`.

---

## 4. The full task editor — command surface (R4)

Split by interaction shape (slice-002 per-field PATCH for single-key verbs; slice-004 R4 combined command for the form). All commands are deny-by-default, ownership-coerced, version-carrying (§5/§9).

| Command | Trigger | Endpoint | Body | Notes |
|---|---|---|---|---|
| `SetPriority` (NEW) | `1`-`4` (AS-04) | `PATCH /api/tasks/{id}/priority` | `{ priority: "P0".."P3" \| null, version }` | One optimistic paint, one `version` round-trip. |
| `RescheduleDueDate` (NEW) | `T` (AS-05) | `PATCH /api/tasks/{id}/due-date` | `{ dueDate: <utc-instant> \| null, dueHasTime: bool \| null, version }` | Client parses the Polish phrase (slice-003 parser) → resolves the instant; **server re-validates** the resolved instant (pairing invariant + UTC-kind + plausible range — the slice-003 `CreateTaskValidator` rules, reused). Realizes the deferred reschedule. |
| `SetTaskDone` (REUSE) | `Space` (AS-03) | existing slice-002 status endpoint | existing | No new command/handler — exercised from the Today view. |
| `EditTask` (NEW, combined) | `E` → `Ctrl+Enter` (AS-06/07/08) | `PATCH /api/tasks/{id}/edit` | `{ title, description, priority, dueDate, dueHasTime, projectId, version }` | **Whole-object replace** of the editable fields, atomic on `Ctrl+Enter`, discarded entirely on `Esc`. All fields are **required keys, nullable values** (`title` required-non-null; the rest required-but-nullable) — the slice-004 R4 anti-silent-null discipline. Reuses `MoveToProject` for the project field. |

**Path choice**: `/api/tasks/{id}/edit`, **not** bare `/api/tasks/{id}` — the bare path is taken by the slice-002 create (`PUT`) and delete (`DELETE`); every per-field mutation follows the verified `/api/tasks/{id}/<field>` convention (`/title`, `/status`, `/position`, `/project`), so `/due-date`, `/priority`, `/edit` are the convention-consistent slots.

**Pairing invariant** (slice-003 rule, reused) applies in **both** `RescheduleDueDate` **and** `EditTask`: `dueDate` and `dueHasTime` are **both** non-null or **both** null; a half-set pair → 422 `validation_failed`.

---

## 5. Optimistic concurrency — the `version` token (reused pattern)

Unchanged machinery. `SetPriority`, `RescheduleDueDate`, `EditTask`, and the reused `SetTaskDone`/`MoveTaskToProject` carry the caller's last-seen `version`; a stale token → `VersionConflictException` → **409 `version_conflict`** (existing code, no contract change). Every mutating behavior method `Touch`es (`UpdatedAt` + `Version++`). The read queries (§6) are non-mutating and carry no token.

---

## 6. Read models — Today & Upcoming (R5/R6)

Two new owner-scoped query handlers; server-side zone-aware filtering over a **plain UTC range** + server-side grouping & deterministic ordering (the client renders a ready-to-paint list; the boundary fact is server-authoritative).

### `GetTodayTasks` — `GET /api/tasks/today`
1. `WarsawDayBounds` (R1) → `StartOfTodayUtc`, `StartOfTomorrowUtc`.
2. Owner-scoped SQL: `WHERE created_by = caller AND deleted_at IS NULL AND status NOT IN ('done','cancelled') AND due_date IS NOT NULL AND due_date < @startOfTomorrowUtc` (zone-free). This yields **due-today** (`due_date` in `[StartOfTodayUtc, StartOfTomorrowUtc)`) **OR overdue incomplete** (`due_date < StartOfTodayUtc`) — "so nothing silently falls off the day."
3. Per row, derive **`isOverdue`** = `due_date < StartOfTodayUtc`.
4. **Group by project** (Inbox/unprojected = `projectId: null` group); within each group apply the R5 order.

**`TodayResponse`** (nested-group envelope): `{ groups: [ { projectId: uuid|null, tasks: [TodayTaskResponse] } ] }`, where **`TodayTaskResponse` = all `TaskResponse` fields + `isOverdue: boolean`**. `isOverdue` lives **only** on the Today read model — **not** on base `TaskResponse`.

### `GetUpcomingTasks` — `GET /api/tasks/upcoming`
1. `WarsawDayBounds` → `StartOfTomorrowUtc`, `StartOfDayPlusUtc(8)`.
2. Owner-scoped SQL: `WHERE created_by = caller AND deleted_at IS NULL AND status NOT IN ('done','cancelled') AND due_date >= @startOfTomorrowUtc AND due_date < @startOfDayPlus8Utc` — the **7 calendar days after today** (the window `[start of tomorrow-Warsaw, start of (today+8)-Warsaw)`). Today's tasks are **not** in Upcoming (Constitution X). Tasks with no `due_date` are in neither view.
3. **Group by day** — the group key is the **Warsaw `LocalDate`** of each task's `due_date` (via `WarsawLocalDate`, R3), projected to a `date` string (`YYYY-MM-DD`). Within each day apply the R5 order.

**`UpcomingResponse`** (nested-group envelope): `{ groups: [ { date: "YYYY-MM-DD", tasks: [TaskResponse] } ] }`, groups ordered ascending by `date`. Upcoming rows reuse plain `TaskResponse` (no `isOverdue` — nothing in this window is overdue).

### Deterministic order (R5) — applied server-side in both views
Within each group: **priority (P0 first) → due time → created → id**.
- **NULL priority sorts LAST** (after P3) — unprioritized = lowest triage rank.
- **Due-time tie-break**: a **date-only** task (`due_has_time = false`) sorts as **start-of-day** (precedes same-day timed tasks); then `created_at`, then `id` for total determinism.
- Both views **exclude `done` and `cancelled`** by default.

> **Routing note**: the literal segments `/today` and `/upcoming` must be registered so they win over the `/{id}` route template (an implied constraint of these paths — register the literals, or constrain `{id}` to a uuid).

---

## 7. Migration Plan — **NO migration this slice**

**This slice introduces no EF migration** (verified by the absence of a new file under `TaskFlow.Infrastructure/Persistence/Migrations/` in this slice's diff):
- `priority` and `description` **activate pre-existing mapped columns** (§1) — already created nullable-text by the slice-002 `AddTasks` migration, already mapped in `TaskConfiguration`. Reading/writing them changes **no** schema.
- **NodaTime is adopted for boundary computation only** (§3) — it does **not** remap any column and does **not** add `Npgsql.NodaTime`, so the EF model **snapshot is unchanged** → no migration is generated.
- The optional Today/Upcoming range index (`(created_by, due_date) WHERE deleted_at IS NULL`) is **deferred** (R6-c, following slice-004's deferred-index precedent) — added later only if profiling warrants, as a contained additive migration.

**FR-051 (backup-before-migration) is a NAMED NO-OP this slice** — there is no schema change to back up before — exactly the **slice-003 posture**, NOT the slice-004 LIVE posture. `research.md`, `plan.md`, and this data model MUST all state FR-051 as a no-op; a divergence (one artifact claiming LIVE) is the failure mode to avoid.

**New application-layer seams** (no migration):
- `WarsawDayBounds` time seam (§2/§3) + the `NodaTime` package reference.
- Two query handlers `GetTodayTasks` / `GetUpcomingTasks` (CQRS read; `TodayResponse` / `UpcomingResponse` projections, §6).
- Three command handlers `SetPriority` / `RescheduleDueDate` / `EditTask` + their FluentValidation validators (closed-set priority, the reused due-pairing rule, description length).
- The reused `RescheduleDueDate` validator shares the slice-003 due-date rule extracted from `CreateTaskValidator`.

---

## 8. State transitions

**No new lifecycle.** The two activated fields are scalar attributes, not state machines: `SetPriority` / `Reschedule` / `EditTask` are field mutations (each bumps `Version`), and the done/undone transition is the **reused** slice-002 `MarkDone`/`MarkBacklog` (`Space` toggle, AS-03), unchanged. No new diagram. (View **membership** is a *derived* function of `due_date`/`status` against the Warsaw boundary — §6 — not stored state; a reschedule or toggle-done simply changes the inputs to that derivation, which the client recomputes optimistically, R7.)

---

## 9. Authorization scoping (deny-by-default — R10; governed by the §0 blocker)

- **Branch realized**: **ownership** (personal/unprojected tasks). Every new handler is **deny-by-default, enforced at the handler** (FR-068).
- **Reads** (`GetTodayTasks`, `GetUpcomingTasks`): scoped `WHERE created_by = caller AND deleted_at IS NULL` + the §6 view predicates. Per-user isolation (FR-065 ownership half).
- **Writes** (`SetPriority`, `RescheduleDueDate`, `EditTask`; reused `SetTaskDone`/`MoveTaskToProject`): coerce no owner from the wire; a foreign/absent/tombstoned id → **404** (existence not disclosed — the slice-002/003/004 posture). `EditTask`'s project field additionally checks ownership of the **target project** (via the reused `MoveToProject` path). `createdBy` is **provenance only**, never a standalone grant (FR-066's applicable half).
- **Read-model leak rule**: `TodayTaskResponse`/`TaskResponse` continue to hide `created_by`/`deleted_at`; the Today/Upcoming responses expose only the projected fields + group key (+ `isOverdue` on Today).
- **Dispatch-by-visibility seam**: handlers are written so the **ownership arm is live** and the **shared-project membership + role arm (FR-067: viewer=read, editor/owner=write) is a named, not-yet-realized branch** — it fills the seam without reshaping the query/command once the blocker resolves.
- **Test coverage (Constitution VIII + governance gate)**: **every** new handler ships an **allow** and a **deny** integration test through the real DB (deny = a caller acting on another user's personal task → 404). The SC-016 **viewer-deny** / **non-member-deny** tests are **tracked as BLOCKED** pending the `ProjectMembership` substrate (slice 007) — see §0.

---

## 10. Read models (delta) — summary

- **`TaskResponse`** (delta): gains **`priority`** (`"P0".."P3" | null`) and **`description`** (`string | null`) + the `From(...)` projection lines. Both nullable, **NOT** added to `required[]`. No other change. (`isOverdue` is **not** added here.)
- **`TodayResponse`** (NEW): `{ groups: [ { projectId: uuid|null, tasks: [TodayTaskResponse] } ] }`; `TodayTaskResponse` = `TaskResponse` + `isOverdue: boolean`.
- **`UpcomingResponse`** (NEW): `{ groups: [ { date: "YYYY-MM-DD", tasks: [TaskResponse] } ] }`.

---

## What is unchanged

- **No new entity, no new table, no migration** (§7); the `tasks` table schema is untouched (`priority`/`description` columns and their `string?` mapping pre-exist).
- **The `timestamptz` columns stay mapped to `DateTime`** — NodaTime is **boundary-computation only**, **no `Npgsql.NodaTime` plugin, no column remap, no snapshot delta** (§3). (Contrast slice 004, which had "No NodaTime"; this slice **introduces** it, but narrowly.)
- The error contract — **no new code** (R11): bad priority / bad reschedule instant / over-long description → 422 `validation_failed`; foreign/absent id → 404 `not_found`; stale token → 409 `version_conflict`. The `ErrorCode` union + `ERROR_UX` map stay exhaustive with no change; no `TaskFlowDocumentTransformer` `ErrorCodes` edit.
- The `version`/`version_conflict` machinery; the BFF proxy, authentication wiring, and `ICurrentUser` resolution; the slice-002 shortcut/suppression substrate (R8); the slice-001 CSP / security headers + signed BFF→API carrier; **no new secrets** (R12/FR-100).

# Data Model: Natural-Language Dates (slice 003)

**Feature**: `003-natural-language-dates` | **Date**: 2026-06-21

This slice introduces **no new entity and no schema migration**. It **activates** two columns that slice 002 reserved on **ENT-01 — Task** (`due_date`, `due_has_time`) — they already exist in the `AddTasks` migration, are mapped in `TaskConfiguration.cs`, and are declared as `Task.DueDate`/`Task.DueHasTime` properties. This document is the **delta** over `specs/002-task-capture/data-model.md`; everything not mentioned here is unchanged.

## Activated columns (ENT-01 — Task)

| Field | Column | Type | Constraints | Notes |
|---|---|---|---|---|
| `DueDate` | `due_date` | `timestamptz` | NULL | The resolved due **instant** in UTC (Constitution X / FR-092). Already exists (slice 002). Now read + written by `CreateTask`. |
| `DueHasTime` | `due_has_time` | `boolean` | NULL | The `has_time` flag (ADR-0003 Decision 8): `true` = date-time ("po 17"), `false` = date-only ("jutro"). Already exists (slice 002). |

No type change, no new index, no new constraint, no migration. (These were two of the seven reserved scalar nullables in slice-002 data-model.md.)

### Pairing invariant

`due_date` and `due_has_time` are **both NULL** (no due date) or **both NON-NULL** (a due date is set). `due_has_time` is meaningless without a date; a date without `due_has_time` is ambiguous. Enforced at both boundaries (R8, R11):
- **Client**: a **NEW** `createTaskSchema` Zod object with a `.refine()` pairing rule (both-present or both-absent). This is net-new — `lib/validation/task.ts` currently has only `taskTitleSchema = z.string()…`, and `createTask(title)` takes only a title, so a payload-level object schema must be added and `createTask` reworked to validate `{ title, dueDate?, dueHasTime? }`.
- **Server**: `CreateTaskValidator` rule (FluentValidation) → `422 validation_failed` on violation.

> Not enforced as a DB `CHECK` constraint this slice — the columns were created NULL/NULL by slice 002 without one, and adding a constraint would be a migration this slice deliberately avoids (R10). The invariant is upheld by the two application-layer boundaries, which is consistent with how slice 002 enforces title/position rules.

## Representation rules (R5, R9; FR-092)

- **Date-time** (`due_has_time = true`): the wall-clock time the user typed, interpreted in **Europe/Warsaw**, converted to a UTC instant. E.g. "po 17" on 2026-06-21 (CEST +02:00) → `due_date = 2026-06-21T15:00:00Z`, `due_has_time = true`.
- **Date-only** (`due_has_time = false`): **midnight Europe/Warsaw** of the resolved calendar day, converted to a UTC instant. E.g. "jutro" on 2026-06-21 → wall-clock `2026-06-22T00:00:00` Warsaw → `due_date = 2026-06-21T22:00:00Z`, `due_has_time = false`.
- **Recovery for display**: convert `due_date` back to Europe/Warsaw (`toReferenceZone`) and take the wall-clock / calendar day. A naive read of the *UTC* Y-M-D lands a day early for date-only — always convert to the reference zone first (R9).
- **DST**: all conversions go through `date-fns-tz` (IANA); never fixed-offset arithmetic (R-DST).

## Domain change (Task aggregate)

`Task.Create(...)` gains an **optional-due overload** (or extended signature):

```
Create(TaskId id, UserId createdBy, string title, string position, DateTime utcNow,
       DateTime? dueDate, bool? dueHasTime)
```

- Sets `DueDate`/`DueHasTime` in the ctor at construction time. **No version bump** — creation, not a mutation.
- The existing no-due `Create(...)` overload remains (leaves both NULL).
- **No new behavior method** to *change* a due date — rescheduling an existing task is slice 005 (OOS here).
- The pairing invariant is validated upstream (validator); the aggregate sets whatever the validated command supplies.

## Read model (`TaskResponse`) delta

`TaskResponse` gains two fields (and `From(...)` projects them):

| Field | Type | Notes |
|---|---|---|
| `dueDate` | `string \| null` (`date-time`) | UTC instant; null when unset. |
| `dueHasTime` | `boolean \| null` | The `has_time` flag; null when `dueDate` is null. |

Still excludes `deleted_at` and the other reserved columns; `createdBy` still omitted. Both new fields are declared as plain nullable, **non-`required`** C# init props (`DateTime? DueDate`, `bool? DueHasTime`), exactly like the existing `CompletedAt`. The .NET generator emits such props as **optional** `… | null` in `schema.d.ts` (verified: `completedAt?: string | null`), so `dueDate`/`dueHasTime` are **NOT** added to the contract's `required` list — "no due date" is conveyed by null/absent. (The earlier framing of `completedAt` as "nullable-but-required" was incorrect about the generated output.)

## Create-chain delta (wire DTO → endpoint → command)

The codebase separates three types — **all three** must gain the two optional fields:
- **`CreateTaskRequest`** (`Application/TaskManagement/CreateTaskRequest.cs`) — the **wire body DTO** `{ Title, Position }` that Wolverine.Http binds and that the OpenAPI `CreateTaskRequest` schema auto-derives from. Add `DueDate (DateTime?)` + `DueHasTime (bool?)`. (This is the contract-bearing type — not the command.)
- **`TaskEndpoints.Create`** (`Api/Endpoints/TaskEndpoints.cs`) — maps the body + route id into the command. Extend the `new CreateTask { … }` projection to carry `DueDate`/`DueHasTime`.
- **`CreateTask`** (the bus command record) — gains optional `DueDate (DateTime?)` + `DueHasTime (bool?)`; this is the type `CreateTaskValidator` validates. `CreateTaskValidator` gains:
- **Pairing**: both null or both non-null.
- **UTC kind** (R13): when present, `DueDate.Value.Kind == DateTimeKind.Utc`, else `422`. `due_date` is the **first client-supplied `DateTime`** to reach a `timestamptz` column — a non-`Z` (offset/unspecified) form would otherwise deserialize to non-UTC and make Npgsql throw an unhandled **500**; the validator rejects it as a clean 422 at the trust boundary.
- **Plausible range** (when present): `due_date` ∈ [`MinDue`, `MaxDue`] — a wide sanity window (e.g. ≥ 2000-01-01, ≤ now + ~10 years) to reject corrupt/absurd instants (not business logic). A UTC-instant comparison — **zone-agnostic** (no NodaTime, R7).

`CreateTaskHandler` passes the two fields to `Task.Create`. On **idempotent replay** (the caller's own live row already exists), the existing due date is returned **unchanged** — create is not a replace (consistent with title/position/version replay).

## What is unchanged

- The `tasks` table schema, the `ix_tasks_created_by_position` partial index, the `created_by → users(id) ON DELETE CASCADE` FK, the soft-delete/reaper model, the `version` optimistic-concurrency token, and `ORDER BY position, id`.
- The list query `GET /api/tasks` (it returns the full read model, so it now surfaces `dueDate`/`dueHasTime` for free via the expanded `TaskResponse` — no query change).
- Authorization (ownership branch; allow + deny already shipped on `CreateTask`).

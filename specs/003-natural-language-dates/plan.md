# Implementation Plan: Natural-Language Dates

**Branch**: `003-natural-language-dates` | **Date**: 2026-06-21 | **Spec**: `specs/003-natural-language-dates/spec.md`

**Input**: Feature specification from `specs/003-natural-language-dates/spec.md`

## Summary

Completes the **Daily Task Capture** journey begun in slice 002: a typed Polish natural-language date phrase at the end of a task title sets the new task's due date. A user presses `C`, types e.g. "Kupic mleko po 17", and Enter — the client **parses the trailing phrase** (`lib/dates.ts`, a closed-set Polish parser), strips it from the title ("Kupic mleko"), resolves it against the **Europe/Warsaw** reference zone to a UTC instant + a `has_time` flag, paints the row optimistically (<16 ms), and sends `{dueDate, dueHasTime}` on the existing `PUT /api/tasks/{id}`. When the trailing token is a date *attempt* that cannot resolve (e.g. "Spotkanie 30.02"), the due date stays empty and a red **"nie rozpoznano"** renders below the field (FR-006/EC-02); when there is no date-shaped trailing token (the common case), the task is created with the full title and no error.

This slice **owns the time rule** (FR-092, Constitution X): timestamps stored UTC, every date-relative computation evaluated against `Europe/Warsaw` applied identically on client and server, the `due_date` carrying a `has_time` flag, DST handled by the timezone library. The **parser runs client-side** (interpret, for optimistic paint); the **server validates** the resolved instant (well-formed, plausible range, pairing invariant) and persists UTC — it performs **no** Polish parsing (Principle V).

This slice introduces **no new entity, no new command, and no EF migration**. It extends the slice-002 create chain — the `CreateTaskRequest` wire DTO, the `TaskEndpoints.Create` request→command mapping, the `CreateTask` command/handler/validator, the `Task.Create` factory, and the `TaskResponse` read model — to carry the optional due date — the `due_date`/`due_has_time` columns were already created and mapped by slice 002 (reserved forward-compat). The client TZ seam (`lib/timezone.ts` over `date-fns-tz`) and `date-fns` are already present. The change is: a new client parser, a capture-flow wire-up, a DTO/contract expansion (+ `pnpm gen:api` regen), and the matching validation + tests.

## Technical Context

**Language/Version**: TypeScript (strict) on Node.js 22 LTS / Next.js 15 (App Router), React 19; C# 13 on .NET 9 / ASP.NET Core.

**Primary Dependencies**:
- Frontend (NEW this slice): **none** — `lib/dates.ts` is hand-rolled (R1) on top of the already-present `date-fns` v4 + `date-fns-tz` v3 (wrapped by the existing `lib/timezone.ts`). Reused: TanStack Query v5 (optimistic create), Zod 3 (boundary validation of the resolved due date), openapi-fetch + openapi-typescript, the BFF proxy.
- Backend (NEW this slice): **none** — `WolverineFx.FluentValidation` (wired in slice 002) gains a due-date rule on the existing `CreateTaskValidator`. **No NodaTime** (R7 — deferred to slice 005). Reused: Wolverine + Wolverine.Http + EF Core, Npgsql, the `ProblemDetailsMiddleware` (`422 validation_failed` already exists — no new error code).

**Storage**: PostgreSQL 17. **No migration** — `tasks.due_date` (`timestamptz` NULL) + `tasks.due_has_time` (`boolean` NULL) already exist (slice-002 `AddTasks`). This slice begins reading/writing them.

**Testing**: Vitest (parser unit tests — every phrase class + ambiguity edge + EC-02, against an injected Warsaw clock), xUnit + Testcontainers-Postgres (due-date round-trip + validation rejects through the real DB), Playwright (E2E — AS-02..AS-05 + EC-02 recovery).

**Target Platform**: Linux containers (Docker Compose) on Hetzner VPS; host Caddy for TLS. No infra change.

**Project Type**: Web application (monorepo: Next.js frontend + ASP.NET Core API).

**Performance Goals**: Parsed due date painted optimistically <16 ms of the Enter keypress (SC-003, owned by slice 002 — the parse is synchronous, in-process, no network/lazy-import on the path); server single-entity write p95 <200 ms (unchanged — one extra nullable column on the existing insert).

**Constraints**: One fixed instance reference zone `Europe/Warsaw` (per-user zones OOS-19); DST via library, never fixed-offset arithmetic (FR-092); the closed parser grammar (R2) is the entire recognized surface; "nie rozpoznano" fires only on a genuine end-anchored date *attempt* that fails (R4), never on ordinary dateless titles; authorization unchanged (rides `CreateTask`'s ownership branch).

**Scale/Scope**: ~10 users; up to 10,000 tasks per user (unchanged from slice 002). This slice adds no rows and no new query — only two nullable fields on the existing capture write and read model.

## Constitution Check

*GATE: Re-evaluated against v4.0.0 for slice 003. PASS. This slice OWNS the Time & Timezone rule (Principle X / FR-092). Two normative-MUST touchpoints are named no-ops this slice and tracked in Complexity Tracking (FR-051 backup-before-migration; the server TZ library).*

| # | Principle | Status | How Addressed |
|---|---|---|---|
| I | Keyboard-First | PASS | Date entry is inline in the existing `C` capture flow — the user types the phrase at the end of the title and presses Enter; no mouse, no separate date picker. The single-key-suppression gate (FR-031/EC-08, slice 002) already keeps typing un-intercepted inside the capture input. |
| II | Accessibility | PASS | The FR-006 "nie rozpoznano" error is rendered as **text** below the field (not color alone, FR-044 contrast) and announced via an **explicit** polite status region (`role=status`, `aria-live=polite` — the existing `LiveRegion` component or `useToast().push`) WITHOUT stealing focus (FR-101). **Note:** a client-side parse failure creates **no** TanStack mutation, so it does NOT ride the automatic MutationCache error-announcer — `TaskCapture` must announce it explicitly. The capture surface keeps the slice-002 `Dialog` focus contract. Any due-date display reuses focusable/visible affordances (FR-042/FR-046); transitions stay instant/<100 ms under `prefers-reduced-motion` (FR-047). No new keybinding (FR-045). |
| III | Instant Response | PASS | The parse is **synchronous and in-process** (a closed-set string match + date-fns arithmetic, R5) — the parsed due date paints with the optimistic create row <16 ms of Enter (SC-003), no network/await on the path. The create rides the existing TanStack `onMutate`/`onError`/`onSettled` recipe on the single `['tasks']` key; a server rejection of the resolved due date (422) rolls the row back. Server write stays p95 <200 ms (one extra nullable column). |
| IV | Minimalist UI | PASS | No new UI chrome — the due date is parsed from the same single capture input; "nie rozpoznano" is a single quiet inline message, not a modal or wizard. A parsed due date renders as a small, purposeful label on the row, not a date-picker widget. |
| V | Connected, Server-Authoritative | PASS | The Polish parser **interprets** client-side (input interpretation for optimistic paint, R1); the client resolves to a concrete ISO-8601 **UTC instant** + `has_time` and the `due_date` write is server-authoritative — the server **validates** (well-formed instant, plausible range, pairing invariant, R11) and persists UTC through the C# API. The server does **NOT** re-parse Polish text. No new external runtime dependency (SC-004); all traffic rides the slice-001 BFF proxy. |
| VI | Type Safety | PASS | TS strict + no `any`; C# nullable + analyzers-as-errors. The parser returns a **typed** result (a resolved `{ dueDate: Date; dueHasTime: boolean }` or an `"unrecognized"` outcome that drives FR-006). The new `dueDate`/`dueHasTime` fields cross the boundary in the generated typed contract (`pnpm gen:api`, CI-diff-gated) and are validated at runtime (Zod client, FluentValidation server). No new `errorCode` (reuses `validation_failed`), so the `ERROR_UX satisfies Record<ErrorCode, ErrorUx>` map stays exhaustive without change. |
| VII | Data Integrity | PASS | FR-006 is the in-flow recovery (retain the prior/empty date value, no silent loss → satisfies FR-049); FR-050 logs a server-side rejection with structured context (no secrets, no raw title). **FR-051 (backup-before-migration) is a named no-op this slice** — there is NO schema change (the columns pre-exist) — tracked in Complexity Tracking so a reviewer reads it as a decision, not a gap. The pairing invariant (both-null or both-non-null) prevents a half-populated due date. |
| VIII | Test-First | PASS | Red-Green-Refactor. Parser unit tests (Vitest, injected Warsaw clock) cover every R2 phrase class, every R3 ambiguity, and EC-02 (`30.02` → error; `Wersja 2.0` → no date/no error; bare `jutro` → dateless task) BEFORE the parser exists; backend tests cover due-date round-trip + validation rejects (→ 422) + idempotent-replay-leaves-due-date-unchanged through Testcontainers-Postgres BEFORE the handler change; E2E covers AS-02..05 + EC-02. A DST-boundary unit test guards FR-092. |
| IX | Authn/Authz | PASS | **No new authorization path.** Setting `due_date` happens inside `CreateTask`, which already dispatches to the deny-by-default **ownership** branch (`createdBy = caller`) and already ships an allow AND a deny test (SC-013/SC-016). The wire never supplies `createdBy`; a caller can only create a task it owns. Foreign/absent/tombstoned ids still resolve to `404 not_found` (slice-002 posture, unchanged). The due date adds data to an already-authorized write — it widens no surface. |
| X | Time & Timezone | PASS (**owned here**) | This slice owns FR-092. Timestamps stored UTC (`due_date` is `timestamptz`, `DateTimeKind.Utc`); NL date resolution is evaluated against the single reference zone **`Europe/Warsaw`**, applied **identically on client and server** — by the client deriving the instant (the server stores it verbatim, computing nothing zone-dependent THIS slice; R7). `due_date` carries a `has_time` flag (date-only vs date-time, R2); date-only = **midnight Warsaw → UTC instant** (R9). DST is handled by `date-fns-tz` (IANA), **never** fixed-offset arithmetic (R5/R-DST). "Equal to today" = same calendar day in Warsaw (recover via `toReferenceZone`, R9). Per-user zones OOS-19. Downstream Today/Upcoming computation is slice 005. |
| XI | Privacy | PASS | No new personal-data store — this writes the `due_date` of an existing Task. The slice-002 account-deletion erasure cascade (`tasks.created_by → users(id) ON DELETE CASCADE`) already erases a user's tasks (and their due dates) atomically with the `User` row; no coordinator change. Retention stance is inherited ("retained until account deletion"). |
| XII | Security | PASS | The typed phrase is **untrusted input**: it is parsed to a typed result and the resolved instant is type-checked + range-validated at the trust boundary (Principle VI/R11); no free text from this flow is rendered as raw HTML (the stripped title is still React-escaped, FR-099). Structured rejection logging (FR-050) carries `ErrorCode`/`Method`/`Path` only — never the carrier or task title/phrase. The slice-001 CSP/security headers + BFF→API HMAC carrier are reused unchanged; no new secrets. |

## Project Structure

### Documentation (this feature)

```text
specs/003-natural-language-dates/
├── plan.md              # This file
├── research.md          # Phase 0: design decisions (R1–R13 + R-DST)
├── data-model.md        # Phase 1: due_date/due_has_time activation (delta over slice 002 — no new entity)
├── quickstart.md        # Phase 1: validation guide (AS-02..05 + EC-02; delta over slice 002)
├── contracts/
│   └── openapi.yaml     # Phase 1: API contract delta (CreateTaskRequest + TaskResponse gain dueDate/dueHasTime)
└── tasks.md             # Phase 2 (/speckit-tasks — not yet created)
```

### Source Code (repository root)

All paths below are **additive or surgical** over the existing slice-002 tree. Files marked **(MODIFY)** gain a small change; **(NEW)** is new. **There is NO migration** (the columns already exist).

```text
apps/
├── api/                                          # ASP.NET Core 9 (C#, DDD, Wolverine)
│   ├── src/
│   │   ├── TaskFlow.Domain/
│   │   │   └── TaskManagement/
│   │   │       └── Task.cs                        # (MODIFY) add an optional-due overload of Create(...)
│   │   │                                          #   (id, createdBy, title, position, utcNow, DateTime? dueDate, bool? dueHasTime)
│   │   │                                          #   sets DueDate/DueHasTime in the ctor; no version bump (it's creation).
│   │   │                                          #   The DueDate/DueHasTime PROPERTIES already exist (slice 002).
│   │   ├── TaskFlow.Application/
│   │   │   └── TaskManagement/
│   │   │       ├── CreateTaskRequest.cs           # (MODIFY) THE wire body DTO {Title, Position} — add DueDate (DateTime?)
│   │   │       │                                  #   + DueHasTime (bool?). This is the type the OpenAPI `CreateTaskRequest`
│   │   │       │                                  #   schema auto-derives from (NOT the command) — editing it is what makes
│   │   │       │                                  #   the two fields appear in `pnpm gen:api`.
│   │   │       ├── CreateTask.cs                  # (MODIFY) add DueDate + DueHasTime to the COMMAND record (the bus message);
│   │   │       │                                  #   CreateTaskValidator gains pairing + UTC-kind (R13) + plausible-range (R11);
│   │   │       │                                  #   CreateTaskHandler passes them to Task.Create (replay leaves them unchanged)
│   │   │       └── TaskResponse.cs                # (MODIFY) add DueDate + DueHasTime to the DTO + From(...) projection
│   │   └── TaskFlow.Api/
│   │       ├── Endpoints/
│   │       │   └── TaskEndpoints.cs               # (MODIFY) Create(): map request.DueDate/DueHasTime into the
│   │       │                                      #   `new CreateTask { … }` it dispatches (request→command wiring)
│   │       └── OpenApi/
│   │           └── TaskFlowDocumentTransformer.cs # (NO CHANGE) — verified: it only hand-builds ProblemDetails +
│   │                                              #   stamps operationIds/error responses. The success-body schemas
│   │                                              #   (CreateTaskRequest/TaskResponse) AUTO-DERIVE from the C# DTOs
│   │                                              #   (CreateTaskRequest.cs / TaskResponse.cs above), so the new fields
│   │                                              #   flow through once THOSE files are edited. No new errorCode
│   │                                              #   (422 validation_failed already documented), no new operation →
│   │                                              #   transformer untouched. Only `pnpm gen:api` regen.
│   └── tests/
│       └── TaskFlow.IntegrationTests/
│           └── TaskManagement/
│               └── CreateTaskTests.cs            # (MODIFY) add: due-date-time round-trip; date-only round-trip;
│                                                  #   bad pairing → 422; non-UTC (non-`Z`) dueDate → 422 not 500 (R13);
│                                                  #   implausible range → 422; idempotent replay leaves due date unchanged
│       └── TaskFlow.UnitTests/
│           └── Domain/TaskManagement/
│               └── TaskTests.cs                  # (MODIFY) Create-with-due sets DueDate/DueHasTime; Create-without leaves them null
│
└── web/                                           # Next.js 15 (App Router, TS strict)
    ├── src/
    │   ├── lib/
    │   │   ├── dates.ts                           # NEW: closed-set Polish NL parser (R1–R5). Pure, injected `now`.
    │   │   │                                      #   parseTaskInput(raw, now) → { title, dueDate?, dueHasTime?, error? }
    │   │   │                                      #   builds on timezone.ts (fromReferenceZone/toReferenceZone)
    │   │   ├── timezone.ts                        # (REUSE — no change) REFERENCE_TIME_ZONE + from/toReferenceZone helpers
    │   │   ├── validation/
    │   │   │   └── task.ts                         # (MODIFY) add a NEW createTaskSchema object with a .refine() pairing
    │   │   │                                       #   rule (today holds ONLY taskTitleSchema = z.string()); rework
    │   │   │                                       #   createTask to validate { title, dueDate?, dueHasTime? }
    │   │   └── api/
    │   │       └── generated/schema.d.ts          # (REGEN) pnpm gen:api after the contract change; CI-diff-gated
    │   ├── components/tasks/
    │   │   ├── TaskCapture.tsx                    # (MODIFY) on Enter, run parseTaskInput; on error show "nie rozpoznano"
    │   │   │                                      #   + EXPLICITLY announce via role=status/useToast (no mutation fires on a
    │   │   │                                      #   client parse failure, so the auto error-announcer won't, FR-006); else
    │   │   │                                      #   create with title + due
    │   │   └── TaskRow.tsx                        # (MODIFY) render a small due-date label via formatInReferenceZone
    │   │                                          #   (date-only vs date-time per dueHasTime)
    │   └── hooks/
    │       └── useTaskMutations.ts               # (MODIFY) create mutation accepts + sends dueDate/dueHasTime;
    │                                              #   optimistic row carries the parsed due date
    └── tests/
        ├── unit/
        │   ├── dates.test.ts                      # NEW Vitest: every R2 class, every R3 edge, EC-02 cases,
        │   │                                      #   the four AS scenarios verbatim, + one DST-boundary case (R12)
        │   ├── use-task-mutations.test.ts         # (MODIFY) the optimistic-create surface (CreateTaskVariables +
        │   │                                      #   the optimisticTask literal) gains dueDate/dueHasTime
        │   └── task-validation.test.ts            # (MODIFY) add cases for the NEW createTaskSchema pairing .refine()
        │                                          #   rule (both-present / both-absent; mismatch rejected) — Test-First
        └── e2e/
            └── tasks.spec.ts                      # (MODIFY) add AS-02..05 capture-with-date + EC-02 "nie rozpoznano"
```

**Structure Decision**: Reuses the slice-002 layout entirely. The only **new** files are the client parser (`lib/dates.ts`) and its unit test (`dates.test.ts`); everything else is a surgical `(MODIFY)` on existing slice-002 files. The backend change is confined to the `CreateTask` vertical (command/validator/handler), the `Task.Create` factory, the `TaskResponse` DTO, and the OpenAPI transformer — no new namespace, no repository change, no new endpoint. The client change is confined to the capture flow (`TaskCapture` → `useTaskMutations`) plus a row-display tweak. The TZ seam (`timezone.ts`), authorization wiring, BFF proxy, and error pipeline are reused unchanged.

## Key Design Decisions

These summarize and cross-reference `research.md` (R1–R13). Where a decision refined the spec, the spec prose was edited to match — most notably the wire contract (R8), which removed a redundant "+ UTC offset" from the spec prose so spec and contract agree.

### Client-side closed-set Polish parser (R1, R2, R4)

`lib/dates.ts` is a hand-rolled parser over a **closed grammar** (R2: `dzis`/`jutro`/`pojutrze`, weekdays, `za N dni`, `po HH`, `o HH:MM`, `DD.MM`), with a normalization step (lowercase + NFD-diacritic-strip) so no-diacritic input (`piatek`) matches. No NL library — `chrono-node` has no Polish, and a closed set is exactly where a fuzzy parser is liability. The split is **conservative** (R4): only an **end-anchored, longest-match, range-validated** trailing token with a **non-empty remainder** is consumed as a date — so an ordinary title never loses a word and a task literally titled "jutro" stays "jutro" with no due date. Three outcomes on Enter: no date-token → full title, no error; date-token resolves → strip + set due; date-token fails (`30.02`) → "nie rozpoznano" (FR-006/EC-02).

### Time rule ownership: DST-safe resolution against Europe/Warsaw (R5, R6, R9; FR-092)

This slice owns FR-092. All arithmetic runs in the **Warsaw wall-clock domain** then converts to a UTC instant via the existing `fromReferenceZone` (`date-fns-tz`, IANA, DST-correct) — never fixed-offset arithmetic (R-DST shows the one-hour summer error). Date-only = **midnight Warsaw → UTC instant** (`has_time = false`), recovered for display by converting back with `toReferenceZone` (R9). The parser takes an **injected `now`** so resolution is deterministic under test (R12).

### Wire contract: `{UTC instant + has_time}`, offset dropped (R8) — spec reconciled

The create payload + read model carry **`dueDate`** (ISO-8601 **UTC instant**, nullable) **+ `dueHasTime`** (boolean, nullable). A separate UTC offset is **redundant** under a single fixed instance zone — the wall-clock is recoverable from `{instant + Europe/Warsaw}` — so the spec's elaboration prose was **reconciled** to drop it (the offset never appeared in FR-092's normative statement, so the FR is untouched). **Pairing invariant**: both null or both non-null.

### Extend `CreateTask`; no new command, entity, or migration (R10)

Due date is set only at capture time this slice (reschedule = slice 005, recurrence = slice 012 — both OOS). So the full create chain gains the two optional fields: **`CreateTaskRequest`** (the wire body DTO the OpenAPI schema derives from), **`TaskEndpoints.Create`** (the request→command mapping), **`CreateTask`** (command record + validator + handler), **`Task.Create`** (new optional-due overload), and **`TaskResponse`**; **no new command, no new entity**. (The codebase separates the wire DTO `CreateTaskRequest` from the bus command `CreateTask` — both must be edited, plus the endpoint that maps one to the other; editing only the command would leave the wire contract and the handler input unchanged.) The `due_date`/`due_has_time` columns and `Task.DueDate`/`DueHasTime` properties already exist from slice 002 → **no EF migration**. On **idempotent replay**, the due date is left **unchanged** (create is not a replace — consistent with the existing title/position/version replay rule). A `pnpm gen:api` regen lands `dueDate`/`dueHasTime` in the generated TS client (CI-diff-gated).

### Server validates, never re-parses (R7, R11; Principle V)

The server adds FluentValidation rules on `CreateTaskValidator`: the **pairing** invariant + a **UTC-kind** check (R13) + a **plausible-range** sanity window on the resolved instant (a UTC comparison — zone-agnostic, so **no NodaTime this slice**, R7). The UTC-kind check is load-bearing: `due_date` is the **first client-supplied `DateTime`** to hit a `timestamptz` column, and a non-`Z` (offset/unspecified) form would otherwise reach Npgsql as an unhandled **500** — the validator rejects it as a clean **422** at the trust boundary (Principle VI/XII, FR-049). A violation → **422 `validation_failed`** via the existing pipeline (no new error code — unlike slice-002's `version_conflict`). The server performs **no** Polish parsing and computes nothing zone-dependent; NodaTime arrives in slice 005 with the first Today/Upcoming computation.

### Test surface (R12; Constitution VIII)

Parser unit tests (Vitest, frozen Warsaw clock) are written first and cover every phrase class, every ambiguity, EC-02, the four AS scenarios verbatim, and a DST-boundary case. Backend tests add due-date round-trip (date-time + date-only), validation rejects (→ 422), and idempotent-replay-unchanged. Authorization tests are **unchanged** (`CreateTask` already ships allow + deny). E2E covers AS-02..05 + EC-02.

## Complexity Tracking

Two normative-MUST touchpoints are named no-ops this slice — listed so a compliance reviewer reads them as decisions, not gaps. Neither is a constitution violation.

| Item | Why a no-op / deferred this slice | Resolution & where it lands |
|------|------------------------------------|------------------------------|
| **FR-051 backup-before-migration** (Constitution VII makes a pre-migration backup a MUST) | This slice has **NO schema change** — `due_date`/`due_has_time` were created and mapped by slice-002's `AddTasks` (reserved forward-compat columns). There is no EF migration to back up before. | No action this slice. The pre-migration backup hook remains in place for the next slice that DOES migrate (e.g. slice 004 `project_id` activation / slice 005). Verified by the absence of a new file under `Persistence/Migrations/` in this slice's diff. |
| **Server timezone library (NodaTime)** (FR-092/Constitution X is owned here) | The server **validates** a client-resolved UTC instant and **stores** it; it computes nothing against Europe/Warsaw this slice (a range check is a zone-agnostic UTC comparison, R7). Pulling NodaTime + the Npgsql NodaTime plugin in now would be a dependency + mapping layer ahead of need. | NodaTime is introduced in **slice 005** (daily-planning), the first slice that computes "same calendar day in Europe/Warsaw" server-side (Today/Upcoming). FR-092 is fully satisfied this slice by client-side resolution + UTC storage + the `has_time` flag, applied identically client/server (the server's identity = storing the client's instant verbatim). |

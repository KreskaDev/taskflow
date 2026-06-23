---
description: "Task list for Natural-Language Dates (slice 003)"
---

# Tasks: Natural-Language Dates

**Input**: Design documents from `/specs/003-natural-language-dates/`
**Prerequisites**: plan.md, spec.md, research.md (R1–R13 + R-DST), data-model.md, contracts/openapi.yaml, quickstart.md (all present)

**Tests**: REQUIRED for this slice. Constitution v4.0.0 Principle VIII (Test-First) and SC-013/SC-016 govern.
Test tasks are first-class and follow Red-Green-Refactor: **the test task has a LOWER id than the
implementation it covers — write it, watch it fail (RED), then implement (GREEN).** Authorization tests are
**unchanged** — `CreateTask` already ships an allow + a deny test (slice 002 T029); this slice adds NO new
authorization path (the due date rides `CreateTask`'s already-authorized ownership branch), so the new
backend tests cover due-date *behavior*, not a new auth surface.

**Organization**: This slice completes the **US-01 Daily Task Capture** journey begun in slice 002 — it has a
single user story (**US1**), delivering acceptance scenarios AS-02..AS-05 + EC-02. The work splits into two
independent tracks that join at `pnpm gen:api`:
- **Backend track** — extend the create chain (wire DTO → command/validator/handler → `Task.Create` →
  read model → endpoint) to carry an optional resolved due date and validate it at the trust boundary.
- **Client track** — a NEW closed-set Polish parser (`lib/dates.ts`) + the capture-flow wire-up
  (validation schema → create mutation → `TaskCapture` → `TaskRow`).

This slice is **surgical over slice 002** and introduces **NO new entity, NO new command, NO new error code,
and NO EF migration** — `tasks.due_date`/`tasks.due_has_time` and `Task.DueDate`/`DueHasTime` already exist
(slice-002 `AddTasks`, reserved forward-compat); `date-fns`/`date-fns-tz` (via `lib/timezone.ts`) and
`WolverineFx.FluentValidation` are already present. **No new web or API dependency.**

## Format: `[ID] [P?] [Story] Description`
- **[P]**: Can run in parallel (different files, no dependency on an incomplete task)
- **[Story]**: `[US1]` for story-phase tasks (Setup/Polish carry no story label)

## Path Conventions
Monorepo (per plan.md): backend `apps/api/src/<Project>/…`, backend tests `apps/api/tests/<Project>/…`,
web `apps/web/src/…`, web tests `apps/web/tests/…`.

---

## Phase 1: Setup (Preconditions — no new deps, no migration)

**Purpose**: Confirm the slice-002 substrate this slice extends, and FREEZE the parser grammar surface before any parser test is authored. **No code freezes until the grammar is fixed (T002).**

- [ ] T001 [P] Confirm slice-003 preconditions: `tasks.due_date` (`timestamptz` NULL) + `tasks.due_has_time` (`boolean` NULL) columns and `Task.DueDate`/`Task.DueHasTime` properties already exist (slice-002 `AddTasks` + `TaskConfiguration.cs`); `apps/web/src/lib/timezone.ts` exports `REFERENCE_TIME_ZONE` (`"Europe/Warsaw"`), `fromReferenceZone`, `toReferenceZone`, `formatInReferenceZone`. **NO EF migration this slice** (FR-051 backup-before-migration is a named no-op — plan Complexity Tracking); **NO new web or API dependency** (plan Technical Context — both NEW-dep lists are empty).
- [ ] T002 **[FROZEN GRAMMAR DECISION — RESOLVED]** Freeze the closed parser grammar for `lib/dates.ts` per research **R2/R3** — this is the surface the parser tests (T011) assert verbatim. **Recognized (post-normalization: lowercase → NFD diacritic-strip → ASCII keyword table):** `dzis`/`dzisiaj`, `jutro`, `pojutrze`, weekdays (`poniedzialek..niedziela`), `za N dni` (N ≥ 1), `po HH` (HH 0–23), `o HH`/`o HH:MM`, `DD.MM` (DD 1–31, MM 1–12). **Decision: `o HH(:MM)` is IN** (R2 recommendation — natural "at a clock time" form, trivially in-grammar). **`has_time` mapping:** `po/o HH(:MM)` → `true`; all others → `false`. **R3 ambiguity rules (fixed once):** weekday-when-today-is-that-weekday → **+7 days**; `po/o HH` already passed today → **still today**; `DD.MM` already past this year → **next year**. **OUT** (named cuts, not oversights): inflected/prepositional weekdays (`w piatek`), combined token+time (`jutro o 17`), `za tydzien`/`za N godzin`, `DD.MM.YYYY` and `/`-/`-`-separated dates, English, bare trailing numbers, month names.

---

## Phase 2: User Story 1 — Daily Task Capture (Priority: P1) 🎯 MVP

**Goal**: Press `C`, type a title with a trailing Polish date phrase (e.g. "Kupic mleko po 17"), press Enter → the client parses + strips the phrase, resolves it against Europe/Warsaw to a UTC instant + `has_time`, paints the due date optimistically (<16 ms), and persists it server-side. A trailing date *attempt* that can't resolve ("30.02") shows red **"nie rozpoznano"** and creates nothing; a title with no date-shaped trailing token is created as-is with no error.

**Independent Test**: Launch the app, press `C`, type each quickstart Input and Enter → the task appears with the stripped title and the expected due date (AS-02..05 + the `dzis`/`o HH:MM`/`DD.MM` rows); type "Spotkanie 30.02" → no task created, "nie rozpoznano" announced, field retains its value (EC-02); type "Wersja 2.0" → created as-is, no error (guard).

> Reference "now" for parser expectations: **2026-06-21 (Sunday), CEST (+02:00)**; in tests `now` is injected (R12) so all expectations are deterministic.

### Backend track — tests FIRST (RED), then the create chain

- [ ] T003 [P] [US1] **(write first — RED)** Extend `apps/api/tests/TaskFlow.UnitTests/Domain/TaskManagement/TaskTests.cs` — `Create(...)` with a due date sets `DueDate`/`DueHasTime` (both date-time and date-only); the existing no-due `Create(...)` leaves both null; creation does **not** bump `version` (covers T005)
- [ ] T004 [P] [US1] **(write first — RED)** Extend `apps/api/tests/TaskFlow.IntegrationTests/TaskManagement/CreateTaskTests.cs` (through Testcontainers-Postgres) — **date-time round-trip** (`dueDate` + `dueHasTime=true` → persisted UTC, echoed in `TaskResponse`); **date-only round-trip** (`dueHasTime=false`, midnight-Warsaw instant → round-trips, display recovers the calendar day via `toReferenceZone`); **bad pairing** (`dueDate` set + `dueHasTime` null, and vice versa) → **422 validation_failed**; **non-UTC `dueDate`** (offset `…+02:00` or unspecified `…T17:00:00`, non-`Z`) → **422, NOT 500** (R13 trust-boundary guard); **implausible range** (year 1500 / now + 50y) → **422**; **idempotent replay** (re-PUT same id) → existing due date returned **unchanged** (covers T006–T009)
- [ ] T005 [P] [US1] Modify `apps/api/src/TaskFlow.Domain/TaskManagement/Task.cs` — add an optional-due overload (or extended signature) of `Create(TaskId, UserId createdBy, string title, string position, DateTime utcNow, DateTime? dueDate, bool? dueHasTime)` that sets the existing `DueDate`/`DueHasTime` properties in the ctor; **no version bump** (creation, not a mutation); the existing no-due `Create(...)` overload remains (leaves both null). The pairing invariant is validated upstream (T007) — the aggregate sets whatever the validated command supplies (RED via T003)
- [ ] T006 [P] [US1] Modify `apps/api/src/TaskFlow.Application/TaskManagement/CreateTaskRequest.cs` — add `DueDate (DateTime?)` + `DueHasTime (bool?)` plain nullable `init` props (NOT C# `required` — mirror the existing nullability so they emit OPTIONAL in `schema.d.ts`). **This is the wire body DTO the OpenAPI `CreateTaskRequest` schema auto-derives from** — editing it is what surfaces the two fields in `pnpm gen:api`
- [ ] T007 [US1] Modify `apps/api/src/TaskFlow.Application/TaskManagement/CreateTask.cs` — (a) add `DueDate (DateTime?)` + `DueHasTime (bool?)` to the **command record**; (b) extend `CreateTaskValidator` with three rules → **422 validation_failed** via the existing pipeline (no new error code): **pairing** (`DueDate` and `DueHasTime` both null OR both non-null), **UTC-kind** (R13 — when present, `DueDate.Value.Kind == DateTimeKind.Utc`, else 422 — guards the first client-supplied `DateTime` to a `timestamptz` column from becoming an unhandled Npgsql 500), **plausible-range** (when present, `DueDate ∈ [≈2000-01-01, now + ~10y]` — a UTC-instant comparison, zone-agnostic, **no NodaTime**, R7/R11); (c) `CreateTaskHandler` passes `command.DueDate`/`command.DueHasTime` into `TaskEntity.Create(...)` (the idempotent-replay branch already returns the existing row UNCHANGED via `TaskResponse.From` — no extra code needed for replay-unchanged) (depends on T005; RED via T004)
- [ ] T008 [US1] Modify `apps/api/src/TaskFlow.Api/Endpoints/TaskEndpoints.cs` — in `Create(...)`, map `request.DueDate`/`request.DueHasTime` into the `new CreateTask { … }` it dispatches (request→command wiring; without this the due date never reaches the handler) (depends on T006, T007)
- [ ] T009 [P] [US1] Modify `apps/api/src/TaskFlow.Application/TaskManagement/TaskResponse.cs` — add `DueDate (DateTime?)` + `DueHasTime (bool?)` plain nullable `init` props (mirroring `CompletedAt`, NOT `required`) and project them in `From(...)`. So the existing `GET /api/tasks` read model surfaces them for free (no query change) (RED via T004)

### Contract regen (backend → client join point)

- [ ] T010 [US1] Regenerate the typed client — `cd apps/web && pnpm gen:api` (API running on `localhost:4311`) → `apps/web/src/lib/api/generated/schema.d.ts` gains `dueDate`/`dueHasTime` on `CreateTaskRequest` + `TaskResponse`, emitted as **OPTIONAL** `… | null` (NOT added to any `required` list — mirrors `completedAt`); `pnpm typecheck` green; commit the regenerated file; confirm it matches the `contracts/openapi.yaml` delta. No `TaskFlowDocumentTransformer.cs` edit (success-body schemas auto-derive; no new operation/errorCode). CI regen-and-diff gated (depends on T006, T008, T009)

### Client track — parser (tests FIRST → impl); independent of the backend track

- [ ] T011 [P] [US1] **(write first — RED)** Create `apps/web/tests/unit/dates.test.ts` (Vitest, injected Warsaw `now` = 2026-06-21 Sun CEST) — **every R2 phrase class** (one case each); **the four AS scenarios verbatim** ("Kupic mleko po 17"→today 17:00 `2026-06-21T15:00:00Z`/`has_time=true`; "Raport jutro"→tomorrow date-only; "Meeting piatek"→next Friday 2026-06-26; "Zakupy za 3 dni"→2026-06-24); plus `dzis`, `o 9:30`, `30.06`; **R3 edges** (`piatek` on a Friday→+7; `po 17` when now=18:00→still today; `30.06` when now=2026-07-01→2027-06-30); **EC-02** ("Spotkanie 30.02"→`error: "unrecognized"`, no due); **guards** ("Wersja 2.0"→no date/no error; "skala 3.14"→no date/no error; bare `jutro`→title "jutro", no due, no error; "Kupic mleko"→no due); **one DST-boundary case** (a `jutro`/`za N dni` crossing the late-Mar or late-Oct Warsaw transition → instant maps to **midnight Warsaw**, not a fixed-offset slip) (covers T012)
- [ ] T012 [US1] Create `apps/web/src/lib/dates.ts` — the closed-set Polish parser (R1–R5), pure, injected `now`: `parseTaskInput(raw: string, now: Date) → { title: string; dueDate?: Date; dueHasTime?: boolean; error?: "unrecognized" }`. Normalization (lowercase + NFD diacritic-strip → ASCII keyword table, R2); **conservative split** — end-anchored, longest-trailing-match, word-boundary, **non-empty-remainder** required (R4); the **`DD.MM` / `po HH` / `o HH:MM` range gates** (in-range-but-impossible like `30.02`→`error`; out-of-clock/calendar-range like `2.0`/`3.14`→plain title text, no error, R4); **all arithmetic in the Warsaw wall-clock domain then `fromReferenceZone(...)`** (R5 — never fixed-offset; date-only = midnight Warsaw → UTC instant, R9), built on `lib/timezone.ts` (RED via T011)

### Client track — capture wire-up (tests FIRST → impl)

- [ ] T013 [P] [US1] **(write first — RED)** Extend `apps/web/tests/unit/task-validation.test.ts` — the NEW `createTaskSchema` pairing `.refine()`: both-present accepted, both-absent accepted, mismatch (`dueDate` set + `dueHasTime` undefined, and vice versa) rejected (covers T014)
- [ ] T014 [US1] Modify `apps/web/src/lib/validation/task.ts` — add a **NEW** `createTaskSchema` Zod object `{ title: taskTitleSchema, dueDate?, dueHasTime? }` with a `.refine()` pairing rule (both-present or both-absent) + export its inferred type. Net-new — today the file holds ONLY `taskTitleSchema`; this is the create-payload object schema the capture flow validates (RED via T013)
- [ ] T015 [P] [US1] **(write first — RED)** Extend `apps/web/tests/unit/use-task-mutations.test.ts` — the optimistic-**create** surface (the `CreateTaskVariables` type + the `optimisticTask` literal) carries `dueDate`/`dueHasTime`; `onMutate` paints them on the optimistic row; `onError` rolls back; the request body sends them (covers T016)
- [ ] T016 [US1] Modify `apps/web/src/hooks/useTaskMutations.ts` — thread the due date through **all FOUR surfaces** (the trap: the recipe-layer edits alone leave the public wrapper unable to carry it): (a) add `dueDate?`/`dueHasTime?` to the `CreateTaskVariables` interface; (b) send them in the `mutationFn` `apiClient.PUT` body (today `{ title, position }`); (c) carry them on the `optimisticTask` literal in `onMutate` (`dueDate: variables.dueDate ?? null`, `dueHasTime: variables.dueHasTime ?? null` — so the optimistic row paints the due date and matches the now-nullable `TaskResponse` type); (d) **widen the public `createTask` wrapper** — today `createTask(title: string)` runs `taskTitleSchema.parse(title)` then mutates `{ id, title, position }`; rework it to accept the resolved `{ title, dueDate?, dueHasTime? }`, validate via `createTaskSchema` (T014, **replacing** the `taskTitleSchema.parse` call — the defensive boundary parse, mirroring its current role), mint id + newest-first rank, and forward `dueDate`/`dueHasTime` onto `createMutation.mutate`. Rides the existing `onMutate`/`onError`/`onSettled` recipe on the single `['tasks']` key (depends on T010; RED via T015)
- [ ] T017 [US1] Modify `apps/web/src/components/tasks/TaskCapture.tsx` — on Enter, run `parseTaskInput(raw, new Date())` and branch on the three outcomes (R4): **no date token** → call `createTask({ title })` with the full title, no due; **resolves** → call `createTask({ title, dueDate, dueHasTime })` with the stripped prefix; **fails** → create nothing, render red **"nie rozpoznano"** below the field and **EXPLICITLY announce** it via `role=status`/`aria-live=polite` (the existing `LiveRegion` or `useToast().push`) WITHOUT stealing focus — a client parse failure fires NO mutation, so the auto MutationCache error-announcer will NOT fire (FR-006/FR-101). Preserve the current empty-title no-op via `createTaskSchema.safeParse(...)` (mirroring today's `taskTitleSchema.safeParse` — invalid/empty → stay open, no create). Keep the slice-002 `Dialog` focus contract (depends on T012, T014, T016)
- [ ] T018 [P] [US1] Modify `apps/web/src/components/tasks/TaskRow.tsx` — render a small, purposeful due-date label via `formatInReferenceZone` (date-only vs date-time per `dueHasTime`; convert to the reference zone first so date-only shows the correct calendar day, R9); text meets FR-044 contrast, no hover-only affordance (FR-046), no new keybinding (depends on T010)

### E2E

- [ ] T019 [US1] Extend `apps/web/tests/e2e/tasks.spec.ts` — AS-02..AS-05 capture-with-date (title is stripped, the due-date label renders); EC-02 "Spotkanie 30.02" → no task created, "nie rozpoznano" announced + field retains value; guard "Wersja 2.0" → created as-is with no error (depends on T017, T018)

**Checkpoint**: US1 complete — the Daily Task Capture journey (AS-01..AS-07) is now whole; a typed Polish date phrase sets the due date end-to-end, with recoverable failure on an impossible date.

---

## Phase 3: Polish & Cross-Cutting Concerns

**Purpose**: Verify the cross-cutting principles and the no-op decisions for the slice that OWNS the time rule (FR-092).

- [ ] T020 [P] Accessibility pass: "nie rozpoznano" is conveyed as **text** (not color alone) at contrast ≥ 4.5:1 (FR-044), announced via a **polite** `role=status`/`LiveRegion` without stealing focus (FR-101); the due-date label is a visible, non-hover-only affordance (FR-046); transitions instant/<100 ms under `prefers-reduced-motion` (FR-047); no new keybinding collides with AT bindings (FR-045); single-key suppression in the capture input is unchanged (FR-031)
- [ ] T021 [P] Confirm SC-003: the parsed due date paints with the optimistic create row **< 16 ms** of Enter — verify the parse path is synchronous + in-process (no network, no lazy-import on the `C`→Enter path)
- [ ] T022 [P] Verify FR-050 structured logging on a server due-date rejection (422) carries only `ErrorCode`/`Method`/`Path` — **never** the carrier or the task title/phrase; FR-099 the stripped title is React-escaped (no `dangerouslySetInnerHTML`)
- [ ] T023 Confirm the CI regen-and-diff gate is green (`pnpm gen:api` clean), `dueDate`/`dueHasTime` are present and **OPTIONAL** (not in any `required` list) in `schema.d.ts`; the `ERROR_UX satisfies Record<ErrorCode, ErrorUx>` map stays exhaustive with NO change (no new errorCode); SC-007 (TS strict + C# nullable/analyzers-as-errors CI gates green); SC-004 (NO new third-party runtime data dependency — `lib/dates.ts` is hand-rolled, `date-fns-tz` was already present)
- [ ] T024 Confirm **NO new migration file** in the slice diff — verify the absence of any new file under `apps/api/src/TaskFlow.Infrastructure/Persistence/Migrations/` (FR-051 backup-before-migration is a named no-op this slice; plan Complexity Tracking)
- [ ] T025 Run `specs/003-natural-language-dates/quickstart.md` validation scenarios end-to-end — the validation table, the ambiguity-edges table, and the server-validation table (all rows)

---

## Dependencies & Execution Order

### Phase dependencies
- **Setup (P1)**: no dependencies — start immediately (T002 is a blocking *grammar decision* the parser tests depend on).
- **US1 (P2)**: depends on Setup. The MVP and the whole slice.
- **Polish (P3)**: depends on US1.

### Test-First ordering (Red-Green) — strict lower-id within US1
- **Backend**: T003 (TaskTests) precedes T005 (`Task.Create`); T004 (CreateTaskTests, the full-chain round-trip + reject suite) precedes T006–T009.
- **Client parser**: T011 (dates.test) precedes T012 (dates.ts).
- **Client wire-up**: T013 (task-validation.test) precedes T014 (createTaskSchema); T015 (use-task-mutations.test) precedes T016 (useTaskMutations).
- Authorization tests are **unchanged** — `CreateTask` already ships allow + deny (slice 002); the new tests cover due-date behavior, not a new auth path.

### Critical path
T002 (grammar) → T011 → T012 → T017 → T019 (client) **‖** T003/T004 → T005 → T007 → T008 / T009 → T010 → T016 → T017 (backend → contract → wire-up). The two tracks join at **T010** (`pnpm gen:api`), which T016/T018 consume.

### Track independence
The **backend track** (T003–T009) and the **client parser track** (T011–T012) share no files and can proceed fully in parallel (two developers). Both feed the capture wire-up: the backend via the regenerated contract (T010), the parser directly (T012). T017 (`TaskCapture`) is the integration point — it depends on the parser (T012), the schema (T014), and the create mutation (T016).

### Parallel opportunities
- **Setup**: T001 ‖ T002.
- **US1 backend**: T003 ‖ T004 (test files); then T005 ‖ T006 ‖ T009 (different files); T007 follows T005; T008 follows T006+T007.
- **US1 client**: T011 ‖ (backend track); T013 ‖ T015 (test files); T018 ‖ T017-prep after T010.
- **Polish**: T020 ‖ T021 ‖ T022.

---

## Parallel Example: User Story 1 (two tracks)

```bash
# Backend track — write the failing tests FIRST (parallel, different files):
Task: "TaskTests.cs — Create-with-due sets DueDate/DueHasTime; no-due leaves null; no version bump"   # T003
Task: "CreateTaskTests.cs — round-trip (date-time/date-only), bad pairing→422, non-UTC→422 not 500, range→422, replay-unchanged"  # T004

# Client parser track — in parallel with the backend track (no shared files):
Task: "dates.test.ts — every R2 class, AS-02..05 verbatim, R3 edges, EC-02, guards, DST boundary"     # T011
Task: "dates.ts — closed-set parser, conservative split, Warsaw-wall-clock→fromReferenceZone"          # T012
```

---

## Implementation Strategy

### MVP (this slice IS the MVP increment for US-01's date scenarios)
1. Phase 1 Setup (freeze the grammar) → 2. Phase 2 US1 (both tracks, joining at `pnpm gen:api`) → **STOP & VALIDATE** capture-with-date end-to-end (quickstart) → demo. This completes the Daily Task Capture journey.

### Notes
- **Test-First is mandatory** (Constitution VIII): the test task has a lower id than the impl it covers — write it, watch it fail (RED), then implement (GREEN).
- **The whole create chain must change** — editing only the `CreateTask` command is a trap: the wire DTO `CreateTaskRequest` (T006) and the endpoint mapping in `TaskEndpoints.Create` (T008) are separate types; without them the due date never reaches the wire or the handler.
- **DST safety (FR-092, the #1 trap)**: do ALL date arithmetic in the Warsaw wall-clock domain, then convert via `fromReferenceZone`; NEVER fixed-offset (R5/R-DST). Date-only = midnight Warsaw → UTC instant; recover for display via `toReferenceZone` (R9).
- **"nie rozpoznano" fires only on a genuine end-anchored date *attempt* that fails** (R4) — never on an ordinary dateless title, and never via the auto error-announcer (a client parse failure creates no mutation, so `TaskCapture` MUST announce it explicitly — T017).
- **No NodaTime this slice** (R7): the server validates a client-resolved UTC instant + stores it; it computes nothing zone-dependent (a range check is a zone-agnostic UTC comparison). NodaTime arrives in slice 005.
- **No migration** (R10): the columns and properties pre-exist (slice-002 `AddTasks`); a new file under `Persistence/Migrations/` in the diff is a defect (T024).

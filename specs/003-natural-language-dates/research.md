# Research: Natural-Language Dates (slice 003)

**Feature**: `003-natural-language-dates` | **Date**: 2026-06-21 | **Spec**: `specs/003-natural-language-dates/spec.md`

Design decisions (R1–R13) that resolve the spec's open questions before Phase 1. Each entry is **Decision / Rationale / Alternatives**. Where a decision refined the spec, the spec prose was edited to match (rather than left divergent) — most notably the wire contract (R8), where a redundant "+ UTC offset" was removed from the spec prose so spec and contract agree.

The two repo facts that anchor everything (verified in code, not inferred):

1. **The columns already exist.** `tasks.due_date` (`timestamptz` NULL) and `tasks.due_has_time` (`boolean` NULL) were created by slice-002's `AddTasks` migration and are mapped in `TaskConfiguration.cs`; the `Task` aggregate already declares `DueDate`/`DueHasTime` properties (private setters). → **No EF migration this slice.**
2. **The client timezone seam already exists.** `apps/web/src/lib/timezone.ts` wraps `date-fns-tz` (already in `package.json`) and exports `REFERENCE_TIME_ZONE = "Europe/Warsaw"`, `fromReferenceZone`, `toReferenceZone`, `formatInReferenceZone`. → The parser builds on it; **no new client date dependency.**

---

## R1 — Polish NL parser: build a small closed-set parser, add NO library

**Decision**: Hand-roll a ~150-line closed-set Polish parser in `apps/web/src/lib/dates.ts`. Do not add `chrono-node` or any NL date library.

**Rationale**: `chrono-node` (the only maintained general-purpose JS NL date parser) has **no Polish locale** (supported: fi, fr, ja, nl, ru, uk, vi; partial: de, es, it, pt, sv, zh — `pl` in neither). Supporting Polish via chrono means authoring the full Polish logic *inside* chrono's regex/refiner framework anyway — the same authoring effort, plus a ~25 KB dependency and an **open-set** matcher that fights our deliberately **conservative, closed** title/date split (R4). The MVP phrase set is small and closed (FR-005), which is exactly the case where a general parser's fuzzy recall is wasted and its false-positive surface is pure liability. date-fns / date-fns-tz (already present) supply all date math + DST-correct zone conversion.

**Alternatives rejected**: (a) chrono-node with a custom `pl` locale — same effort, extra dep, open-set semantics; (b) any other NL lib — none has Polish; (c) server-side parsing — explicitly excluded by the spec (client interprets, server validates a resolved instant, Principle V).

## R2 — Closed grammar and `has_time` mapping

**Decision**: The parser recognizes exactly the phrase classes below and nothing else. A candidate token is **normalized** first: lowercase → strip diacritics (Unicode NFD + drop combining marks) → match an ASCII keyword table. This collapses `Piątek`/`piatek`/`PIĄTEK` to one entry and satisfies the no-diacritics requirement (ASM-03) with a single table.

| Class | Surface forms (post-normalization) | Resolution (Europe/Warsaw) | `has_time` |
|---|---|---|---|
| Today | `dzis`, `dzisiaj` | today's date, **midnight Warsaw** | `false` |
| Tomorrow | `jutro` | today + 1 day, midnight Warsaw | `false` |
| Day-after | `pojutrze` | today + 2 days, midnight Warsaw | `false` |
| Weekday | `poniedzialek, wtorek, sroda, czwartek, piatek, sobota, niedziela` | **next strictly-future** occurrence (R3-A), midnight Warsaw | `false` |
| Relative days | `za N dni` (N = digits, ≥ 1) | today + N days, midnight Warsaw | `false` |
| Time-today | `po HH` (HH 0–23) | **today** at `HH:00` Warsaw (R3-B) | `true` |
| Explicit time | `o HH` / `o HH:MM` (HH 0–23, MM 0–59) | today at `HH:MM` Warsaw | `true` |
| Day.Month | `DD.MM` (DD 1–31, MM 1–12) | `DD.MM` this year, roll to next year if past (R3-C), midnight Warsaw | `false` |

**Explicitly OUT** (named cuts, not oversights): inflected/prepositional weekdays (`w piatek`, `we wtorek`, `w przyszly piatek`); combined token+time (`jutro o 17`, `piatek 15:00`); `za tydzien` / `za N tygodni` / `za N godzin`; `DD.MM.YYYY` and `/`- or `-`-separated dates; English phrases; bare trailing numbers; month names (`czerwca`). FR-005 lists only `DD.MM`, so the wider date forms stay out to keep the set closed and testable.

`o HH(:MM)` is included alongside `po HH` (natural "at a clock time" form, trivially in-grammar, one extra test). It may be cut for the strictest FR-005 reading; recommended IN.

**Rationale**: A closed, enumerated grammar is deterministic, fully unit-testable (R12), and lets the title/date split (R4) be conservative. The `has_time` split is purely structural: phrase classes that name a clock time are date-time; all others are date-only.

**Alternatives rejected**: open/fuzzy grammar (untestable, false-positive prone); supporting inflected weekday prepositions now (real Polish, but unbounded surface — deferred).

## R3 — Ambiguity resolutions

**Decision**: three rules, fixed once:
- **A. Weekday when today *is* that weekday** → resolve to **+7 days** (strictly future). "Next occurring Friday" (AS-04) means the upcoming one; a same-day task uses `dzis`.
- **B. `po HH` / `o HH` when that time already passed today** → **still today** at that time (`has_time = true`). Literal to AS-02; an overdue due-time is a valid state, and a keyboard-first user can re-edit.
- **C. `DD.MM` already past this year** → roll to **next year**.

**Rationale**: each picks the least-surprising reading and avoids silently changing the day/field the user typed.

**Alternatives rejected**: today-inclusive weekday (surprising); `po HH` rolling to tomorrow (silently changes the day); `DD.MM` always-current-year (creates past dates).

## R4 — Title/date split heuristic + three-outcome error model (FR-006 / EC-02)

**Decision**: a single capture input. On Enter, examine only the **trailing token** anchored to end-of-string, and branch:
1. **No date-shaped trailing token** → create with the **full title**, no due date, **no error** (the common case — most titles have no date).
2. **Date-shaped trailing token that resolves** → strip it, set `due_date` + `has_time`, create with the trimmed prefix.
3. **Date-shaped trailing token that fails to resolve** → due date stays empty (or its prior value), render the red **"nie rozpoznano"** below the field (FR-006).

"Date-shaped" = the trailing token matches an R2 surface shape (keyword, or regex `za \d+ dni` / `po \d{1,2}` / `o \d{1,2}(:\d{2})?` / `\d{1,2}\.\d{1,2}`). Shape decides "this was a date attempt"; range/calendar validity decides resolve-vs-fail.

**Conservative split rules** (false-positive guards):
- **End-anchored** (`…$`) — an interior word never triggers a split.
- **Longest trailing match wins** — `za 3 dni` matches as one unit, not `dni`.
- **Non-empty remainder required** — accept the split only if the trimmed prefix is non-empty. So a task literally titled `jutro` / `piatek` / `30.06` (nothing else) stays a **dateless task with that title** — never consumed, never an error.
- **Word boundary** — the token must be preceded by whitespace (or be the whole string, which the non-empty-remainder rule then rejects).

**The `DD.MM` gate** (protects version numbers / decimals): trailing `\d{1,2}\.\d{1,2}` is a date attempt **only if DD ∈ 1–31 and MM ∈ 1–12**. Out of range (`Wersja 2.0` → MM 0; `skala 3.14` → MM 14) → **title text, no error**. In range but not a real date (`30.02`, `31.04`) → **"nie rozpoznano"** (a clear date attempt that cannot resolve). The same range-gate applies to `po HH`/`o HH:MM` (HH/MM out of clock range → title text, no error).

**Rationale**: This is the load-bearing link between FR-005 and FR-006. "nie rozpoznano" must NOT fire on every dateless title; the gate fires only on a genuine, end-anchored date *attempt* that fails — exactly EC-02's intent.

**Alternatives rejected**: a separate explicit date field (contradicts the keyboard-first single-input capture from slice 002); firing the error on any unparsed trailing word (would error on ordinary titles).

## R5 — DST-safe resolution algorithm (FR-092, the #1 correctness trap)

**Decision**: do **all** arithmetic in the **Warsaw wall-clock domain**, then convert to a UTC instant via the existing `fromReferenceZone`:
1. `now_wall = toReferenceZone(new Date())`.
2. add N **calendar days** to `now_wall` (date-fns `addDays`) and/or set the wall-clock time (midnight for date-only; `HH:MM` for timed).
3. `instant = fromReferenceZone(wall)` → the UTC `Date` to send.

**Never** add days/hours to a raw UTC instant. Date-only anchors to **midnight Europe/Warsaw** (see R9). The parser takes an **injected `now`** so weekday / `za N dni` / `DD.MM` rollover are deterministic under test (R12).

**Rationale**: Europe/Warsaw is CET (+01:00) winter / CEST (+02:00) summer. Library-mediated zoned conversion handles the two annual transitions; fixed-offset arithmetic silently corrupts the instant for half the year (R-DST below).

**Alternatives rejected**: UTC-domain arithmetic with a fixed offset (wrong across DST — Constitution X forbids it).

## R6 — Client TZ library: reuse `date-fns-tz` via `timezone.ts`

**Decision**: use `date-fns-tz` (already in `package.json`, already wrapped by `lib/timezone.ts`). **Zero new dependency.**

**Rationale**: it delegates zone math to the platform `Intl`/IANA database (no bundled tz data, unlike `@js-joda/timezone`), tree-shakes well with strict TS, and already exposes the exact two primitives this slice needs (`fromReferenceZone`, `toReferenceZone`/`formatInReferenceZone`). DST is correct via IANA. Adding a second date stack would be pure cost.

**Alternatives rejected**: **Luxon** (new dep, heavier, no advantage); **js-joda + @js-joda/timezone** (largest bundle of the four — overkill for resolve-at-capture); **TC39 Temporal** (the right long-term model, but not yet baseline in browsers/Node as of early 2026 → still a sizable polyfill; revisit when baseline).

## R7 — Server TZ library: NO NodaTime this slice

**Decision**: do **not** add NodaTime. Validate the client-resolved UTC instant with plain `DateTime` (`DateTimeKind.Utc`) + FluentValidation, and store via the existing `timestamptz` mapping. Defer NodaTime to **slice 005** (the first slice that computes "same calendar day in Europe/Warsaw" server-side, for Today/Upcoming).

**Rationale** (tightest-constraint — don't pull in what only a later slice needs): the server's job this slice is narrow — accept a resolved instant + `has_time`, validate well-formed + plausible range, persist UTC. A range check compares two UTC instants; it is **zone-agnostic**. The server computes nothing against Europe/Warsaw this slice, so NodaTime (and the Npgsql NodaTime plugin) would be a premature dependency + mapping layer.

**Alternatives rejected**: adding NodaTime "for correctness now" — buys nothing this slice, adds a mapping layer ahead of need.

## R8 — Wire/storage contract: `{UTC instant + has_time}`; offset dropped (spec reconciled)

**Decision**: the create payload carries **`dueDate`** (ISO-8601 **UTC instant**, e.g. `2026-06-21T15:00:00Z`, nullable) **+ `dueHasTime`** (boolean, nullable). **No separate UTC-offset field.** The `TaskResponse` read model echoes both.

> **Spec reconciled (already applied).** The spec's elaboration prose previously said the client sends "resolved ISO-8601 timestamp **+ UTC offset**" (the parser-split paragraph and Principle V). That offset is **redundant** under a single fixed instance zone, so the prose has been edited to "resolved ISO-8601 UTC instant + `has_time`" — spec and contract now agree. The offset appeared only in prose; FR-092's normative statement never mentioned it, so the FR itself is untouched.

**Rationale**: with one fixed instance zone (per-user zones are OOS-19), Europe/Warsaw is a constant the client already holds, so the wall-clock the user typed is fully recoverable from `{instant + Europe/Warsaw}` via `toReferenceZone`/`formatInReferenceZone`. A stored offset would be derivable-not-authoritative — pure redundancy and a drift hazard if it ever disagreed with the instant. (If per-user zones ever arrived, you'd store the instant + an IANA zone *id*, never a numeric offset.)

**`dueDate`/`dueHasTime` pairing invariant**: both null (no due date) or both non-null. `dueHasTime` is meaningless without a date; a date without `dueHasTime` is ambiguous. Enforced server-side (R11) and client-side via a **NEW** `createTaskSchema` Zod object with a `.refine()` pairing rule — note `lib/validation/task.ts` today holds **only** `taskTitleSchema = z.string()…` (no create-payload object), and `useTaskMutations.createTask(title)` takes only a title, so this is net-new: add the payload object schema and rework `createTask` to validate `{ title, dueDate?, dueHasTime? }`, not an extension of an existing payload schema.

**Alternatives rejected**: `{instant + offset}` or `{wall-clock string + offset}` — adds a field that is redundant (single zone) or a drift hazard, for zero benefit.

## R9 — Date-only representation: midnight-Warsaw → UTC instant

**Decision**: a date-only due date (`has_time = false`) is stored as **midnight Europe/Warsaw of that calendar day, converted to a UTC instant**. Example: "jutro" on 2026-06-21 → wall-clock `2026-06-22T00:00:00` Warsaw → (CEST +02:00) → stored `2026-06-21T22:00:00Z`, `due_has_time = false`. To recover the calendar day, convert the stored instant **back to Europe/Warsaw** (`toReferenceZone`) and take Y-M-D.

**Consumer-discipline caveat**: a naive reader taking Y-M-D of the *UTC* instant lands a day early (`2026-06-21`). The fix is "always convert to the reference zone first" — the `timezone.ts` helpers enforce it. A stored offset would NOT save that naive reader, so date-only is not an argument for carrying an offset (reinforces R8).

**Rationale**: round-trips correctly across DST because the library does both conversions; keeps a single `timestamptz` column for both date-only and date-time (the `has_time` flag, not the column type, carries the distinction).

**Alternatives rejected**: a separate `date`-typed column (would split storage and break the reserved-column reuse); storing UTC midnight (wrong calendar day in Warsaw).

## R10 — Extend `CreateTask`; no new command / entity / migration

**Decision**: due date is set **only at capture time** this slice (every scenario is "type … and press Enter"; rescheduling via `T` is slice 005, recurrence is slice 012 — both OOS here). So:
- **Extend** the existing `CreateTask` command/handler/validator and the `TaskResponse` DTO with optional `DueDate`/`DueHasTime` — **no new command, no new entity**.
- Add an optional-due overload (or extend the signature) of **`Task.Create(...)`** to set `DueDate`/`DueHasTime` in the ctor. On **idempotent replay** the due date is **not** changed (create is not a replace — consistent with the existing title/position/version replay rule).
- **No EF migration** — the columns already exist (anchor fact #1). The change is domain + handler + DTO + contract + web, plus a `pnpm gen:api` regen.

**Rationale**: a separate set-due-date command would mean a second round-trip and break the single-create idempotency + <16 ms optimistic-paint story (Principle III, SC-003). Capture-time-only matches the spec scope exactly.

**Alternatives rejected**: a dedicated `SetDueDate` command (premature — that surface is slice 005's reschedule); a new migration (unnecessary — columns reserved).

## R11 — Server validation (no Polish re-parse)

**Decision**: extend `CreateTaskValidator` (FluentValidation, already wired) with:
- **Pairing**: `DueDate` and `DueHasTime` are both-null or both-non-null (R8 invariant).
- **Plausible range**: when present, `DueDate` ∈ [`MinDue`, `MaxDue`] — a wide sanity window (e.g. not before 2000-01-01, not after now + ~10 years) to reject corrupt/absurd instants, not to enforce business logic. Range is a UTC-instant comparison (zone-agnostic, R7).
- (`DateTimeKind.Utc` is asserted at the boundary; the server stores the instant verbatim.)

A violation → **422 `validation_failed`** via the existing Wolverine FluentValidation + `ProblemDetailsMiddleware` pipeline (no new error code needed — unlike slice-002's `version_conflict`). The server performs **no** Polish natural-language parsing (Principle V).

**Rationale**: the trust boundary type-checks and range-checks the resolved value (Principle VI/XII) without duplicating the client's interpretation logic.

**Alternatives rejected**: re-parsing on the server (violates the parser-responsibility split); a new error code (422 already covers boundary validation).

## R12 — Testability

**Decision**:
- **Client (Vitest)**: the parser takes an **injected `now`** (or fake timers); every R2 phrase class + every R3 ambiguity edge + the EC-02 error cases are deterministic unit tests against a frozen Warsaw clock. Minimum cases: one per class; the four AS scenarios verbatim; `piatek`-on-Friday → +7; `po 17` after 17:00 → still today; `30.06` after → next year; `30.02` → "nie rozpoznano"; `Wersja 2.0` → no date/no error; bare `jutro` → dateless task titled "jutro"; and **one DST-boundary case** (a `jutro`/`za N dni` crossing the late-March or late-October Warsaw transition, asserting the instant maps to midnight Warsaw, not a fixed-offset slip).
- **Server (xUnit + Testcontainers)**: due-date round-trip (date-time and date-only) through the real DB; validation rejects (bad pairing, implausible range) → 422; idempotent-replay leaves an existing due date unchanged. Authorization is **unchanged** — `CreateTask` already ships allow + deny (SC-013/SC-016); the new tests cover the due-date behavior, not a new auth path.
- **E2E (Playwright)**: AS-02..AS-05 capture-with-date and EC-02 "nie rozpoznano" recovery.

**Rationale**: a closed grammar + injected clock makes the whole parser exhaustively and deterministically testable (Constitution VIII, Red-Green-Refactor).

## R13 — `DateTimeKind.Utc` boundary check (the one NEW 500-risk this slice introduces)

**Decision**: `CreateTaskValidator` MUST reject a non-UTC `DueDate` with **422 `validation_failed`**. The rule: `DueDate is null || DueDate.Value.Kind == DateTimeKind.Utc`. Add an integration test that a `dueDate` in a non-`Z` form (offset `…+02:00` or unspecified `…T17:00:00`) returns **422, not 500**.

**Rationale**: `due_date` is the **first client-supplied `DateTime`** in the whole API to land in a `timestamptz` column — every slice-002 `DateTime` is server-minted `DateTime.UtcNow`, so slice 002 sets no precedent here. `System.Text.Json` yields `Kind == Utc` only for a `Z`-suffixed string; an offset form deserializes to `Kind == Local` and an unspecified form to `Kind == Unspecified`. Npgsql **throws** when writing a non-UTC `DateTime` to `timestamptz`, which would surface as an **unhandled 500**, not a clean error. The happy path is safe (the client always sends `toISOString()` → `Z`), but a buggy or hostile client must get a deny-by-default **422** at the trust boundary — exactly the Principle VI/XII obligation that the FR-092-owning slice should close, not leave as a latent 500.

**Alternatives rejected**: (a) silently normalizing via `ToUniversalTime()` — would coerce `Kind == Unspecified` (no zone info) into a guessed instant, masking malformed input rather than rejecting it; strict-reject is the cleaner, more testable trust-boundary posture given the client contract is always `Z`. (b) Leaving it to Npgsql — produces a 500, violating FR-049 (no operation fails without a clean, recoverable error).

---

## R-DST — Worked DST example (why fixed-offset fails)

"today at 17:00" Warsaw on 2026-06-21 (summer, CEST +02:00):
- **Correct** (library): `17:00 − 02:00 = 2026-06-21T15:00:00Z`.
- **Fixed +01:00** (CET) arithmetic: `2026-06-21T16:00:00Z` — **wrong by one hour**.

`fromReferenceZone` consults the IANA database, sees 2026-06-21 is in CEST, applies +02:00, and yields `15:00Z`. A hard-coded offset cannot know the date is in summer time. (The spring-forward gap 02:00→03:00 on the last Sunday of March is the second illustration, where a wall-clock time is non-existent.)

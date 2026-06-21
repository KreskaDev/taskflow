# Quickstart: Natural-Language Dates (slice 003)

**Feature**: `003-natural-language-dates` | **Date**: 2026-06-21

A validation guide for the slice-003 delta. It assumes the slice-002 capture surface works and reuses the same harness. Run these after implementation to prove FR-005 / FR-006 / FR-092 end-to-end.

## Prerequisites

- The slice-001 + slice-002 stack boots (PostgreSQL + API + fake-IdP + BFF + web) via the existing E2E harness (`apps/web/tests/e2e`). **No new infra, no migration** — `due_date`/`due_has_time` already exist.
- `pnpm gen:api` is clean (the regenerated TS client includes `dueDate`/`dueHasTime`; CI-diff-gated).
- The instance reference zone is `Europe/Warsaw` (`lib/timezone.ts`).
- `pnpm gen:api` hits the live document at `http://localhost:4311/openapi/v1.json`, so the **API must be running** for the regen/typecheck step (inherited from the slice-002 setup).

> Reference "now" for the expected outcomes below: **2026-06-21 (Sunday), CEST (+02:00)**. Weekday/relative/`DD.MM` expectations are relative to this; in tests the parser's `now` is injected (R12), so they are deterministic.

## Build / run / test

```bash
# Backend (RED→GREEN): due-date round-trip + validation rejects
cd apps/api && dotnet test           # CreateTaskTests, TaskTests

# Frontend unit (RED→GREEN): the parser — every phrase class + edges
cd apps/web && pnpm test dates       # tests/unit/dates.test.ts (Vitest)

# Contract regen + typecheck
cd apps/web && pnpm gen:api && pnpm typecheck

# E2E (capture-with-date + EC-02 recovery)
cd apps/web && pnpm e2e tasks
```

## Validation scenarios

Each row: focus the `C` capture input, type the **Input**, press Enter.

| # | Scenario | Input | Expected title | Expected due | `has_time` |
|---|---|---|---|---|---|
| AS-02 | time-today (`po`) | `Kupic mleko po 17` | "Kupic mleko" | today 17:00 Warsaw (`2026-06-21T15:00:00Z`) | true |
| AS-03 | tomorrow | `Raport jutro` | "Raport" | tomorrow, date-only (`2026-06-22` midnight Warsaw) | false |
| AS-04 | weekday | `Meeting piatek` | "Meeting" | next Friday (2026-06-26), date-only | false |
| AS-05 | relative days | `Zakupy za 3 dni` | "Zakupy" | +3 days (2026-06-24), date-only | false |
| — | today | `Telefon dzis` | "Telefon" | today, date-only | false |
| — | explicit time | `Call o 9:30` | "Call" | today 09:30 Warsaw | true |
| — | day.month | `Urodziny 30.06` | "Urodziny" | 2026-06-30, date-only | false |
| EC-02 | impossible date | `Spotkanie 30.02` | — (no create) | none; red **"nie rozpoznano"** below field | — |
| guard | no date phrase | `Kupic mleko` | "Kupic mleko" | none | — |
| guard | version number (not a date) | `Wersja 2.0` | "Wersja 2.0" | none; **no error** | — |
| guard | bare date word | `jutro` (nothing else) | "jutro" | none; **no error** (non-empty-remainder rule, R4) | — |

### Ambiguity edges (unit-test level, R3)

| Case | Setup | Expected |
|---|---|---|
| weekday == today | inject now = a Friday, input `… piatek` | resolves to **+7 days** (next Friday), not today |
| `po HH` already passed | inject now = 18:00, input `… po 17` | **still today** 17:00 (overdue is valid) |
| `DD.MM` already past | inject now = 2026-07-01, input `… 30.06` | rolls to **next year** (2027-06-30) |
| DST boundary | input `… jutro` resolving across the late-Mar / late-Oct Warsaw transition | instant maps to **midnight Warsaw** (library), not a fixed-offset slip |

### Server validation (backend tests)

| Case | Expectation |
|---|---|
| date-time round-trip | `PUT` with `dueDate` + `dueHasTime=true` → persisted UTC, echoed in `TaskResponse` |
| date-only round-trip | `dueHasTime=false`, midnight-Warsaw instant → round-trips; display recovers the correct calendar day via `toReferenceZone` |
| bad pairing | `dueDate` set, `dueHasTime` null (or vice versa) → **422 validation_failed** |
| non-UTC instant (R13) | `dueDate` in a non-`Z` form (offset `…+02:00` or unspecified) → **422, NOT 500** (trust-boundary guard; Npgsql would otherwise throw on a non-UTC `timestamptz` write) |
| implausible range | `dueDate` = year 1500 or now + 50y → **422 validation_failed** |
| idempotent replay | re-`PUT` same id → existing due date returned **unchanged** (create is not a replace) |
| authorization (unchanged) | the slice-002 allow + deny tests on `CreateTask` still pass |

## Definition of done

- Every row above passes (manual + automated).
- Parser unit tests are deterministic against an injected Warsaw clock and cover every R2 class + every R3 edge + EC-02 + a DST-boundary case.
- `pnpm gen:api` clean; `pnpm typecheck` green; `dotnet test` green; no new migration file in the diff (FR-051 no-op, plan Complexity Tracking).
- "nie rozpoznano" is announced via the polite `LiveRegion` and meets contrast (FR-006/FR-044/FR-101); it fires only on a genuine date *attempt*, never on an ordinary dateless title.

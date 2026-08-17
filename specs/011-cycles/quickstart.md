# Quickstart — Slice 011 (Cycles) validation

Runnable proof that the slice works end-to-end. Contracts: `contracts/`; entities:
`data-model.md`.

## Prerequisites

- Docker up; repo-root `.env` present (see the local-run runbook).
- `dotnet build apps/api/src/TaskFlow.Api -c Debug` (the E2E harness runs the Debug DLL).
- Ports 55432/4311/4321/3000 free (stop the dev API and `taskflow-demo-pg` first).

## Static gates

```bash
pnpm --dir apps/web typecheck && pnpm --dir apps/web lint && pnpm --dir apps/web audit:dead
pnpm --dir apps/web test                      # unit incl. cycle builders + inventory gate
```

## Backend suites

```bash
cd apps/api && dotnet test                    # incl. TaskFlow.IntegrationTests.Cycles
```

Key facts to see green: activate single-active race (partial index), close rollover matrix
(next/backlog/keep + overrides + invisible-task default), delete guards (active / non-empty),
cycle-tasks visibility filtering + EC-12, SetTaskCycle allow+deny, preferences roundtrip.

## OpenAPI sync

```bash
# with dev PG + API on :4311 (runbook):
pnpm --dir apps/web gen:api && git diff --exit-code apps/web/src/lib/api/generated
```

## E2E

```bash
pnpm --dir apps/web e2e cycles                # lifecycle journey + grouping + settings
pnpm --dir apps/web e2e axe                   # /cycle joins the ×4-palette walk
pnpm --dir apps/web e2e tasks daily-planning board   # touched menu/grouping surfaces stay green
```

## Manual walk (mirrors US-05)

1. Sidebar shows „Cykl" → click → `/cycle` empty state → „Nowy cykl" (name pre-filled
   „Cykl 1", end = start + settings duration). Create a second, later cycle.
2. „Aktywuj" the first cycle — sidebar entry now shows its name. Try „Aktywuj" on the second →
   blocked with the single-active message.
3. On any task row: „⋯" → „Cykl…" → assign to the active cycle (AS-01/02). Verify the project
   List „Grupuj: Cykl" shows the cycle group + „Bez cyklu" last (AS-07/FR-024).
4. `/cycle`: metrics show %, days remaining, breakdown (AS-03). Complete one task → % moves.
5. „Zamknij cykl" → review lists incomplete tasks (AS-04) → „Przenieś wszystkie do następnego
   cyklu" → confirm (AS-05); counts in the toast. Tasks now sit in cycle 2.
6. Close cycle 2 with NO planned cycle and rollover „next" → the „Najpierw utwórz nowy cykl"
   prompt (AS-06).
7. Activate a cycle, invoke „Usuń" (VISIBLE also on active) → refused with
   „Cyklu nie można usunąć — najpierw go zamknij" (AS-07 verbatim); delete a non-empty closed
   cycle → refused with recovery copy (EC-04/FR-020); empty planned cycle deletes fine.
8. /settings: change „Domyślna długość cyklu (dni)" → create form pre-fill follows.

## Visual baselines

`[V]` cycle screens ride the standard docker regeneration
(`apps/web/tests/e2e/update-visual-baselines.ps1`) — relative-date seeding keeps
days-remaining constant; absolute date text is stylePath-hidden (contract `ui-cycle.md`).

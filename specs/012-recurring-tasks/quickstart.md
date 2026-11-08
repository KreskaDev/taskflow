# Quickstart — Slice 012 (Recurring Tasks) validation

Runnable proof that the slice works end-to-end. Contracts: `contracts/`; entities:
`data-model.md`.

## Prerequisites

- Docker up; repo-root `.env` present (local-run runbook).
- `dotnet build apps/api/src/TaskFlow.Api -c Debug` (the E2E harness runs the Debug DLL).
- Ports 55432/4311/4321/3000 free (stop the dev API and `taskflow-demo-pg` first).

## Static gates

```bash
pnpm --dir apps/web typecheck && pnpm --dir apps/web lint && pnpm --dir apps/web audit:dead
pnpm --dir apps/web test                      # unit incl. lib/recurrence fact table + inventory gate
```

## Backend suites

```bash
cd apps/api && dotnet test                    # incl. TaskFlow.UnitTests RecurrenceRuleTests + IntegrationTests.Recurrence
```

Key facts to see green: the shared fact table (clamp, leap year, DST wall-clock) in BOTH the
Domain unit suite and the web unit suite; spawn-now vs schedule by frozen clock; one envelope
under retry; undo soft-deletes / detaches; deferred job no-op matrix; shared-project assignee
re-validation; `recurrence_requires_due_date` on edit and reschedule.

## OpenAPI sync

```bash
# with dev PG + API on :4311 (runbook):
pnpm --dir apps/web gen:api && git diff --exit-code apps/web/src/lib/api/generated
```

## E2E

```bash
pnpm --dir apps/web e2e recurrence            # editor/drawer field, indicator, spawn, early, late, undo, cancel, guard
pnpm --dir apps/web e2e axe                   # editor + drawer with the recurrence controls open join the ×4 walk
pnpm --dir apps/web e2e tasks daily-planning board cycles   # touched row/editor/drawer surfaces stay green
```

## Manual walk (mirrors US-06)

1. Inbox: add „Podlej kwiaty", set its due to today (`Termin…`), open „Szczegóły i komentarze"
   → „Powtarzanie" = „Codziennie" → preview shows tomorrow → the row shows the „codziennie"
   chip (AS-01).
2. Complete the row → toast „Utworzono kolejne wystąpienie"; a new „Podlej kwiaty" due
   tomorrow appears under it with the same chip (AS-02).
3. On a task due next Monday set „W wybrane dni tygodnia" Pn + Śr; set the due to a past
   Monday via `Termin…`; complete → successor due the Wednesday after that Monday (AS-03/AS-05).
4. A task due tomorrow with „Codziennie": complete → NO new row, no toast (AS-04). Reopen it →
   nothing to remove; complete again → still nothing (one scheduled job).
5. Reopen the task from step 2 → the tomorrow instance disappears (undo). Complete again → a
   fresh tomorrow instance.
6. Cancel a recurring task (`PATCH /api/tasks/{id}/status` → `cancelled`; the UI has no cancel affordance — slice-010 scope) → reload: no new instance (AS-06).
7. Today view, `E` on a recurring task → „Usuń termin" + „Zapisz" → blocked with „Zadanie
   cykliczne musi mieć termin…"; switch „Powtarzanie" to „Nie powtarza się" → save succeeds,
   chip gone.
8. Drawer on a dateless task → „Powtarzanie" disabled with „Ustaw najpierw termin".

## Visual baselines

`[V]` editor/drawer-with-field and List-row-with-chip screens ride the standard docker
regeneration (`apps/web/tests/e2e/update-visual-baselines.ps1`) with relative-date seeding;
the preview's absolute date is stylePath-hidden (contract `ui-recurrence.md`).

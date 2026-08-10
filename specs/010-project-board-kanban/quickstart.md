# Quickstart: Validating slice 010 (Project Board / Kanban)

**Spec**: `spec.md` | **Contracts**: `contracts/` | **Data model**: `data-model.md`
Validation/run guide — implementation detail lives in `tasks.md`.

## Prerequisites

Same stack as 019 (`specs/019-ui-design-system/quickstart.md`): Docker Desktop, .NET 9 SDK,
Node + pnpm, repo-root `.env`. After API contract changes regenerate the client:

```powershell
# boot PG + API per 019 quickstart, then:
pnpm --dir apps/web gen:api      # commit schema.d.ts — CI openapi-sync diffs it
```

Interactive stack: `docker run -d --name taskflow-e2e-pg -e POSTGRES_USER=taskflow -e POSTGRES_PASSWORD=taskflow_e2e -e POSTGRES_DB=taskflow -p 55432:5432 postgres:17`, then `node apps/web/dev-run.mjs` (`--stop` = teardown).

## Scenario 1 — Board rendering & column moves (US-03.AS-03..06)

1. Open a project from the sidebar (US-03.AS-01 — visible entry), switch to **Tablica**.
2. Four columns render: Backlog, Do zrobienia, W toku, Zrobione; cards show title/chips/avatars
   on catalog components; each column announces its count.
3. Drag a card from Do zrobienia to W toku: paint is immediate (optimistic), the card's „⋯"
   shows the new column's neighbours; DevTools Network shows ONE `PATCH /api/tasks/{id}/status`
   with `in_progress`.
4. Menu path: card „⋯" → „Przenieś w prawo" moves one column right; in Zrobione the item is
   absent (AS-05); „Przenieś w lewo" symmetric.
5. `done` moves stamp/clear the check state coherently in List and Board (completedAt
   invariant).

## Scenario 2 — Last-used mode (US-03.AS-02)

Switch to Tablica, reload → Board renders; open a DIFFERENT project → defaults to Lista;
return → still Board (per-project persistence). Clear site data → defaults to Lista.

## Scenario 3 — Groupable List (US-03.AS-07) & cancelled (EC-11)

1. In Lista set „Grupuj: Status": groups render in column order; a task seeded as `cancelled`
   (via API — see tests) appears in an „Anulowane" group; switch to Tablica → that task is
   NOWHERE on the Board.
2. „Grupuj: Priorytet": P0→P3 then „Bez priorytetu"; „Brak" restores the flat list; grouping
   choice survives reload; „wg cyklu" is NOT offered (slice 011).

## Scenario 4 — Authorization (FR-065..068)

With a shared project (owner Ada + editor + viewer + non-member):
- viewer: opens Board/List, regroups (reads OK); no drag, no move menu items; a forged PATCH
  → 403 `forbidden`.
- non-member: `GET /api/projects/{id}/tasks` → 404; the project never appears in their sidebar.
- editor: full move rights.
- **Scoping repair (D4)**: a task created by the editor is visible to the owner in List, Board
  AND the sidebar count.

## Automated suites

```powershell
pnpm --dir apps/web test          # unit: board mapping, group builders, persistence hook,
                                  #   menu builder, inventory-coverage gate (new INV rows)
dotnet build apps/api/src/TaskFlow.Api -c Debug
pnpm --dir apps/web e2e           # E2E incl. board journey + axe ×4 palettes (board view joins axe.spec)
cd apps/api ; dotnet test         # unit + integration: SetTaskStatusBoardTests,
                                  #   ProjectScopedTaskListingTests (allow+deny per contract)
pnpm --dir apps/web exec knip     # zero dead modules
```

## Exit criteria

- All owned AS + EC-11 pass as tagged tests; new INV rows 100% covered (gate green).
- Allow+deny integration tests green for the widened status write and the scoping repair.
- `openapi-sync`, `web-quality`, `web-e2e`, api jobs green in CI; shard capacity re-checked
  after the new container-per-fact tests (research D12).
- [V] baselines: board/list screens join `visual.spec.ts` ONLY when 019's baseline generation
  (T066) runs — one regeneration covers both.

# Quickstart: Validating slice 019 (UI Design System & Full UI Operability)

**Spec**: `spec.md` | **Contracts**: `contracts/` | **Data model**: `data-model.md`
This is a validation/run guide — implementation detail lives in `tasks.md`.

## Prerequisites

- Docker Desktop running (dev Postgres + Testcontainers), .NET 9 SDK, Node + pnpm.
- Repo-root `.env` (copy `.env.example`; local password `changeme-local-only`).
- One-time after pulling: `pnpm --dir apps/web install`.

## Boot the stack for development / client regeneration

```powershell
# Dev Postgres (:5432) — MUST pass --env-file because -f docker/ shifts the project dir
docker compose --env-file .env -f docker/docker-compose.yml -f docker/docker-compose.dev.yml up -d postgres

# API (:4311) — auto-migrates on boot; poll /openapi/v1.json for readiness (no /health in dev)
$env:ASPNETCORE_URLS='http://localhost:4311'
$env:ConnectionStrings__Postgres='Host=localhost;Port=5432;Database=taskflow;Username=taskflow;Password=changeme-local-only'
$env:Jwt__SigningKey='local-dev-signing-key-32-chars-min!!'
dotnet run --project apps/api/src/TaskFlow.Api --no-launch-profile

# After adding the two new endpoints (contracts/): regenerate the typed client
pnpm --dir apps/web gen:api      # CI job openapi-sync must stay green

# Web dev server
pnpm --dir apps/web dev
```

## Scenario 1 — Token foundation & four palettes (Story 1)

1. Load `http://localhost:3000` signed in, with no stored preference: page renders in
   `dark-cool` (server-rendered `<html class="dark-cool">`, no FOUC — hard-refresh with
   network throttling to observe pre-hydration paint).
2. In DevTools, swap the `<html>` class to `dark-warm`, `light-cool`, `light-warm`:
   every surface re-themes with no layout shift; native scrollbars/controls follow the
   mode (`color-scheme`).
3. `pnpm --dir apps/web test` — includes the **token contrast matrix** (all normative
   pairings ≥4.5:1 text / ≥3:1 non-text, per palette, incl. hover/selected/disabled trap
   rows) and the **hex audit** (zero raw colors outside `tokens.css`).

**Expected**: all four palettes render token-complete; matrix and audit green.

## Scenario 2 — Regression inventory gate (Story 2)

1. `specs/019-ui-design-system/feature-inventory.md` exists; every shipped feature of
   slices 001–009 appears as an `INV-###` Given/When/Then row with FR/AS cross-refs.
2. `pnpm --dir apps/web test` — the inventory-coverage test parses the inventory and
   fails on any `INV-###` without a `[INV-###]`-tagged covering test.
3. Full suite (below) green ⇒ every inventory entry passes against the redesigned UI.

**Expected**: 100% coverage mapping; suite green; the gate runs in CI (`web-quality` +
the new `web-e2e` job) so later slices inherit it.

## Scenario 3 — UI-operable daily workflow, shortcut removal (Story 3)

Manual walk (or run the SC-018 E2E journey): with a fresh user and no instruction —
create via global "+ Nowy task" AND inline quick-add; edit; set priority/date/labels
from the row "⋯" menu; move to a project; complete; comment on a shared task; reorder
via drag handle AND "Przenieś wyżej/niżej" menu items; "Duplikuj" creates an adjacent
copy. Press C/E/M/L/T/1–4, G-chords, `?` over a focused list: nothing fires; the
shortcuts overlay is gone; Ctrl+Enter/Esc still work in editors; sidebar shows counts
and collapses.

```powershell
# E2E (self-boots PG :55432 + API :4311 + fake IdP :4321 — stop any dev API on :4311 first!)
dotnet build apps/api/src/TaskFlow.Api -c Debug
pnpm --dir apps/web e2e
```

**Expected**: SC-018 journey passes pointer-only + Tab/Enter/Esc; binding-audit
inventory entries pass.

## Scenario 4 — Drawer (Story 4)

Open a task by clicking its title from any listing view: right-side non-modal drawer;
list behind stays interactive; all fields editable inline; shared-project tasks show
the slice-009 thread (avatars, @mentions, tombstones, sanitized markdown; viewer sees
no composer). Copy the URL (`…?task=<id>`), open in a new tab: drawer deep-links;
back/forward preserve list state. Esc closes (except while a text field is focused —
first Esc cancels the field edit).

## Scenario 5 — Full sweep, states & cleanup (Story 5)

1. Visit project view, Today, Upcoming, Assigned, Sign-in, Settings: design-system
   components only; empty states = hint + action; errors surface with recovery; skeletons
   only on genuine network-bound loads; 768–1024px window → no horizontal scroll, drawer
   overlays, sidebar collapsible.
2. Audits:

```powershell
pnpm --dir apps/web exec knip     # zero unreferenced UI-layer modules (dead code gate)
pnpm --dir apps/web test          # hex audit + contrast matrix (again, per palette)
pnpm --dir apps/web e2e           # includes axe per-palette [A] + screenshot [V] suites
```

**Expected**: knip zero findings (shortcut-system remnants gone), zero raw hexes outside
`tokens.css`, axe AA zero violations ×4 palettes, [V] baselines match (first run:
`--update-snapshots` to establish, then human-approve per palette).

## Backend verification (new endpoints)

```powershell
cd apps/api ; dotnet test         # Testcontainers; includes allow+deny tests for
                                  # GET /api/views/counts and POST /api/tasks/{id}/duplicate
```

## Exit criteria (spec Success Criteria)

- All [C]/[E]/[A] green in CI (including the NEW `web-e2e` CI job), [V] baseline
  approved per palette, [P] deferred to post-baseline benchmark suite.
- Inventory 100% covered & green; sweep audit (knip + hex) zero findings; SC-018
  E2E journey green; `openapi-sync` green.

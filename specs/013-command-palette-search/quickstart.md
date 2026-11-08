# Quickstart — Slice 013 (Command Palette & Search) validation

Runnable proof that the slice works end-to-end. Contracts: `contracts/`; read model:
`data-model.md`.

## Prerequisites

- Docker up; repo-root `.env` present (local-run runbook).
- `dotnet build apps/api/src/TaskFlow.Api -c Debug` (the E2E harness runs the Debug DLL).
- Ports 55432/4311/4321/3000 free (stop the dev API and `taskflow-demo-pg` first).

## Static gates

```bash
pnpm --dir apps/web typecheck && pnpm --dir apps/web lint && pnpm --dir apps/web audit:dead
pnpm --dir apps/web test                      # unit incl. lib/palette + lib/filter + inventory gate
```

## Backend suites

```bash
cd apps/api && dotnet test                    # incl. TaskFlow.IntegrationTests.Search
```

Key facts to see green: token-AND matching on title/description, ranking order, caps, LIKE
metacharacters literal, personal isolation, member/non-member/former-member matrix, archived
project visible to members, labels per-user, the 10k characterization (logged timing).

## OpenAPI sync

```bash
# with dev PG + API on :4311 (runbook):
pnpm --dir apps/web gen:api && git diff --exit-code apps/web/src/lib/api/generated
```

## E2E

```bash
pnpm --dir apps/web e2e search                # palette journey + view filter + access scoping (UI side)
pnpm --dir apps/web e2e axe                   # palette open (empty + populated) joins the ×4 walk
pnpm --dir apps/web e2e shortcut-removal tasks daily-planning board cycles   # opener/filter headers stay green
```

## Manual walk (mirrors US-04 / US-08.AS-08)

1. Any view → click the topbar „Szukaj…" control → the palette opens with the input focused and
   the Akcje list (Nowy task, Nowy projekt, Nowy cykl, Przejdź: …) (AS-01).
2. Type „mleko" → tasks whose title or description contains it appear under Zadania (AS-02);
   type „kup mleko" → only tasks containing both tokens.
3. Arrow to a task, Enter → the Inbox/project opens with that task's drawer (AS-03).
4. Type „create project" → the „Nowy projekt" action appears; Enter → the project form opens
   (AS-04).
5. Type a project name → it appears under Projekty; Enter → the project view (AS-05). Archive a
   project, search it → „(zarchiwizowany)" + „Przywróć" → click → restored.
6. Esc → the palette closes and focus returns to the „Szukaj…" control (AS-06).
7. Inbox header → type „sro" in „Filtruj…" → only tasks containing „śro…" remain; clear → all
   back (US-08.AS-08). Repeat on Dziś and a project Board (columns collapse).
8. Sign in as a second user who is NOT a member of the first user's shared project → searching
   that project's task title returns nothing.

## Visual baselines

`[V]` palette-open (empty + populated) and Inbox-with-filter screens ride the standard docker
regeneration (`apps/web/tests/e2e/update-visual-baselines.ps1`); the opener's visual change
(placeholder → button) re-bakes the five existing screens' topbar as well — expect those
baselines to be regenerated and re-approved.

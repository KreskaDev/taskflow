---
name: "speckit-verify"
description: "Run the full local regression suite (static checks, web unit, API unit + integration, E2E) layer by layer and report a single green/red verdict. The post-implementation verification stage of the spec kit workflow."
argument-hint: "Optional layer filter: static | web | api | e2e | quick (= all except e2e)"
compatibility: "TaskFlow monorepo (apps/web + apps/api); Docker required for api and e2e layers"
metadata:
  author: "taskflow"
user-invocable: true
disable-model-invocation: false
---

## User Input

```text
$ARGUMENTS
```

If a layer filter is given, run only that layer (`quick` = everything except `e2e`).
Otherwise run ALL layers in the order below (cheapest first, fail-fast is NOT
applied — always run every requested layer so the final report is complete).

## Layers

Run from the repo root. Record pass/fail + duration for each step.

### 1. static — no services needed

```
pnpm --filter @taskflow/web lint
pnpm --filter @taskflow/web typecheck
pnpm --filter @taskflow/web audit:dead
```

### 2. web — unit tests, no services needed

```
pnpm --filter @taskflow/web test
```

### 3. api — build + unit + integration (needs Docker running; Testcontainers boots its own throwaway Postgres)

```
dotnet build apps/api/TaskFlow.sln -c Release
dotnet test apps/api/tests/TaskFlow.UnitTests -c Release --no-build
dotnet test apps/api/tests/TaskFlow.IntegrationTests -c Release --no-build
```

Integration suite takes several minutes locally (container-per-fact). That is normal.

### 4. e2e — Playwright, self-boots the whole stack

Pre-flight (MANDATORY, in this order):

1. Port `:4311` MUST be free — the harness boots its OWN API there. If a dev API is
   running, kill the port owner's process tree:
   `Get-NetTCPConnection -LocalPort 4311` → `Stop-Process -Force` (kill the child
   `TaskFlow.Api` process too; it holds the `bin/Debug` file lock).
2. `dotnet build apps/api/src/TaskFlow.Api -c Debug` — globalSetup runs the built
   `bin/Debug/net9.0/TaskFlow.Api.dll`, not sources.
3. Docker must be running (harness starts a disposable Postgres on `:55432`).

Then:

```
pnpm --filter @taskflow/web e2e
```

## Report

Finish with a single summary table: layer | step | result | duration, followed by
one verdict line: **GREEN — safe to proceed** or **RED — N failures**.

On RED:

- List each failing test with its one-line failure reason.
- Map each failure to the slice that owns the behavior (specs/NNN-*).
- Do NOT fix anything as part of this stage. A failing regression test after an
  implementation is either (a) a real regression — route it through
  `docs/bugfix-workflow.md` (repro exists already; fix until green), or (b) an
  intentional behavior change — then the test's expectation is a spec question for
  the owning slice, never a silent test edit.

## Notes

- This stage is registered as an `after_implement` hook in `.specify/extensions.yml`,
  so it runs automatically at the end of `/speckit-implement`. It can also be invoked
  manually at any time during development to check the current regression status.
- The CI equivalent for a branch without a PR: `gh workflow run ci.yml --ref <branch>`.

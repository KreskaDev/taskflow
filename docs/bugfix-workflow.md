# Bugfix workflow: regression-test-first

Every reported bug is fixed in two verifiable steps: first a test that proves the bug
exists, then the fix that turns that test green. The test is the bug's executable
specification — after merge it permanently guards against regression.

## The rule

1. **Reproduce as a test (red).** Before touching any production code, write an
   automated test that fails *because of the bug* — not because of setup errors.
   Run it and confirm the failure message matches the reported symptom.
2. **Fix (green).** Change production code until the repro test passes. Do not
   modify the test to make it pass; if the test turns out to encode the wrong
   expectation, that is a spec question — stop and resolve it against the owning
   slice's `spec.md` first.
3. **Verify no collateral damage.** Run the full suite of the affected layer
   (and E2E when the fix touches API contracts or UI flows).

## Commit discipline

- Commit 1: `test(NNN): repro for <bug> — red` (the failing test; verified red locally).
- Commit 2: `fix(NNN): <what was wrong>` (the fix; repro test now green).
- Both land in the same branch/PR so `main` and CI stay green at all times.
- `NNN` = the slice that owns the broken behavior. Reference the violated FR
  in the commit body when one exists (e.g. `Violates FR-050 of slice 005`).

## Choosing the test layer

Write the repro at the **cheapest layer that can actually reproduce the bug**:

| Bug lives in | Layer | Where |
|---|---|---|
| Pure logic, date math, formatting, reducers | Web unit (vitest) | `apps/web` |
| Handler/domain logic, persistence, authorization | Backend integration (xunit + Testcontainers) | `apps/api` |
| API ↔ UI contract, rendering, navigation, a11y | E2E (Playwright) | `apps/web/e2e` |

Only escalate to E2E when a lower layer cannot observe the failure — E2E repros
are the most expensive to run and maintain.

## Bugs triaged but not yet being fixed

If a repro test is written but the fix is deferred, do not leave CI red:

- Playwright: `test.fixme('<bug ref>: <symptom>', ...)`
- xunit: `[Fact(Skip = "<bug ref>: repro red, fix pending")]`
- vitest: `it.todo(...)` or `it.skip(...)` with the bug reference

The skip marker is the backlog entry's anchor; removing it is part of the fix.

## CI enforcement

The `bugfix-gate` job in `.github/workflows/ci.yml` enforces this mechanically on
every PR and push to main:

- Every commit whose subject starts with `fix(NNN):` must be preceded **in the same
  range** by a `test(NNN):` commit that touches test files (`apps/api/tests/` or
  `apps/web/tests/`). Order matters — the red repro must come first.
- A scope-less `fix:` commit requires any earlier test commit touching test files.
- Non-slice scopes (e.g. `fix(deps):`) are exempt.
- Escape hatch for a genuinely untestable fix: put `Repro-Test: none — <why>` on its
  own line in the commit body. The gate passes but emits a warning annotation, so the
  justification is visible in review.

`build-push` (and therefore deploy) depends on this gate.

## Ambiguities are not bugs

If the "bug" turns out to be behavior the spec never defined (or defined
differently), it is a spec gap, not a regression. Route it through Spec Kit
(`/speckit-clarify` on the owning slice, or a new slice for a coherent batch)
instead of this workflow.

export const meta = {
  name: 'slice004-implement',
  description: 'Implement slice 004 (Project Management) in dependency order with build/test gates + adversarial self-review',
  phases: [
    { title: 'Foundational', detail: 'Project aggregate + migration + repository + read model (T001–T011)' },
    { title: 'US1-Backend', detail: 'create/edit/archive/delete commands + GetMyProjects, Test-First (T012–T021)' },
    { title: 'Contract-1', detail: 'transformer operationIds + pnpm gen:api for project ops (T022)' },
    { title: 'US1-Web', detail: 'validation, hooks, sidebar, form, delete dialog (T023–T030)' },
    { title: 'US2-Backend', detail: 'Task.MoveToProject + move command + Inbox narrowing + project tasks (T031–T036)' },
    { title: 'Contract-2', detail: 'TaskResponse.projectId + pnpm gen:api (T037)' },
    { title: 'US2-Web', detail: 'move mutation, project selector, M shortcut, task chip (T038–T041)' },
    { title: 'E2E', detail: 'Playwright projects.spec.ts (T042)' },
    { title: 'Polish', detail: 'a11y / perf / security / gate audits (T043–T048)' },
    { title: 'Self-Review', detail: 'adversarial review over the full diff, then fix + re-review' },
  ],
}

const STATUS = {
  type: 'object',
  required: ['summary', 'tasksCompleted', 'buildGreen', 'testsGreen', 'blockers'],
  additionalProperties: true,
  properties: {
    summary: { type: 'string', description: 'One-paragraph summary of what was implemented and verified' },
    tasksCompleted: { type: 'array', items: { type: 'string' }, description: 'Task IDs fully done (e.g. T005)' },
    tasksIncomplete: { type: 'array', items: { type: 'string' }, description: 'Task IDs not done or partial' },
    buildGreen: { type: 'boolean', description: 'Did the relevant build compile cleanly at the end?' },
    testsGreen: { type: 'boolean', description: 'Did the relevant test suite pass at the end?' },
    filesChanged: { type: 'array', items: { type: 'string' } },
    verifyCommands: { type: 'array', items: { type: 'string' }, description: 'Exact commands run to verify, with pass/fail' },
    blockers: { type: 'array', items: { type: 'string' }, description: 'Anything that blocked completion (empty if none)' },
  },
}

const REVIEW = {
  type: 'object',
  required: ['verdict', 'findings'],
  additionalProperties: true,
  properties: {
    verdict: { type: 'string', enum: ['OK', 'ISSUES_FOUND'] },
    score: { type: 'number' },
    findings: {
      type: 'array',
      items: {
        type: 'object',
        required: ['severity', 'issue'],
        additionalProperties: true,
        properties: {
          severity: { type: 'string', enum: ['critical', 'high', 'medium', 'low'] },
          file: { type: 'string' },
          issue: { type: 'string' },
          fix: { type: 'string' },
        },
      },
    },
    notes: { type: 'string' },
  },
}

const COMMON = `
You are implementing slice 004 (Project Management) of the TaskFlow monorepo at E:\\specflow, on the
branch 004-project-management. AUTHORITATIVE design docs — READ THEM FIRST:
- specs/004-project-management/tasks.md (the task list — implement the task IDs you're assigned)
- specs/004-project-management/plan.md (source tree + decisions)
- specs/004-project-management/research.md (R1–R16), data-model.md, contracts/openapi.yaml

Mirror the EXISTING slice-002/003 'Task' vertical exactly for conventions (Wolverine handler discovery needs
public concrete types; command + validator + handler + endpoint + read model; ICurrentUser ownership coercion;
owner-scoped queries; 404-not-403 for foreign ids; optimistic 'version'/version_conflict; soft-delete tombstone).
Study apps/api/src/TaskFlow.Application/TaskManagement/CreateTask.cs, TaskResponse.cs, Queries/GetMyTasks.cs,
TaskEndpoints.cs, Task.cs, TaskConfiguration.cs, ITaskRepository.cs/TaskRepository.cs before writing.

RULES:
- TEST-FIRST (Constitution VIII): write the RED test BEFORE the impl it covers; watch it fail, then make it pass.
- Every new data handler ships an ALLOW and a DENY integration test (Principle IX + governance gate).
- 404 BEFORE 422 for parentId (foreign/absent → 404 no existence leak; caller-owned-but-illegal nesting → 422).
- NO new error code (reuse validation_failed/not_found/version_conflict). NO NodaTime.
- Leave the working tree COMPILING. Do NOT commit, push, or touch git. Do NOT edit files outside your assigned scope.
- VERIFY with the real toolchain and report exact commands + pass/fail honestly. If something cannot be made
  green, set buildGreen/testsGreen accordingly and list the blocker — do NOT fake success.
`

phase('Foundational')
const found = await agent(`${COMMON}
SCOPE: Phase 2 Foundational, tasks T001–T011.
Implement: ProjectId (T003); RED ProjectTests.cs (T004) then the Project aggregate (T005) — Create/Edit/Archive/
Unarchive/SoftDelete, the one-level-nesting invariant surface, archive_at vs deleted_at; ProjectResponse (T006);
IProjectRepository (T007); ProjectConfiguration + add the tasks.project_id FK to TaskConfiguration (T008);
ProjectRepository (T009); generate the AddProjects EF migration via 'dotnet ef migrations add AddProjects' and
review it is purely additive and touches no other table (T010). For T002, freeze the preset color/icon set on the
API side (a constant the validators will use). For T011, REPORT whether a backup-before-migrate + restore-test CI
gate exists for migrations (search .github/ and docker/); do not invent CI you can't verify.
VERIFY: 'dotnet build' clean; 'dotnet test' for the TaskFlow.UnitTests Project tests green. Register the new
EF configuration + repository in DI if the project wires them explicitly.`,
  { schema: STATUS, phase: 'Foundational', effort: 'high' })

if (!found || !found.buildGreen) {
  return { stoppedAt: 'Foundational', reason: 'foundation did not compile — downstream phases cannot proceed', found }
}

phase('US1-Backend')
const us1be = await agent(`${COMMON}
SCOPE: Phase 3 US1 backend, tasks T012–T021 (the five command verticals + GetMyProjects query).
For EACH of create/edit/archive+unarchive/delete/list: write the RED Testcontainers-Postgres integration test
FIRST (CreateProjectTests, EditProjectTests, ArchiveProjectTests, DeleteProjectTests, ProjectQueriesTests — each
with ALLOW + DENY), then the command (request DTO + command + validator + handler) and its endpoint in a new
ProjectEndpoints.cs. Edit ProjectEndpoints.cs SERIALLY across the verticals (one file). Implement the 404-before-422
parent resolution + nesting check in the handlers; whole-object replace on edit (parentId required); the three task
dispositions + two child dispositions on delete applied in-txn before the tombstone; versioned (non-idempotent) delete.
Do NOT run gen:api (next phase). VERIFY: 'dotnet build' clean; 'dotnet test' integration suite green (Docker is available).`,
  { schema: STATUS, phase: 'US1-Backend', effort: 'high' })

if (!us1be || !us1be.buildGreen) {
  return { stoppedAt: 'US1-Backend', reason: 'US1 backend did not compile — gen:api and web cannot proceed',
    foundational: found, us1be }
}

phase('Contract-1')
const gen1 = await agent(`${COMMON}
SCOPE: T022 — surface the project operations in the typed client.
(a) In apps/api/src/TaskFlow.Api/OpenApi/TaskFlowDocumentTransformer.cs, stamp the new project operationIds and
auto-insert their 401/404/409/422 responses — do NOT change the ErrorCodes array (no new errorCode).
(b) Regenerate the typed client: discover and reuse the project's established gen:api recipe (check apps/web/package.json
'gen:api', the CI workflow under .github/, and any runbook/README). You must boot the API so it serves
http://localhost:4311/openapi/v1.json (the API needs a Postgres — use the same mechanism CI/local uses; e.g. docker
compose for PG, then 'dotnet run' the API), wait until /openapi/v1.json responds, run 'cd apps/web && pnpm gen:api',
then STOP the API and free port 4311 (the E2E suite self-boots its own :4311 later).
VERIFY: 'cd apps/web && pnpm typecheck' clean; confirm schema.d.ts now contains the project operations + ProjectResponse.
Report the exact boot recipe you used in verifyCommands. buildGreen = typecheck clean; testsGreen = schema regenerated & matches contract.`,
  { schema: STATUS, phase: 'Contract-1', effort: 'high' })

const webOk1 = gen1 && gen1.buildGreen
if (webOk1) {
  phase('US1-Web')
  await agent(`${COMMON}
SCOPE: Phase 3 US1 web, tasks T023–T030. Test-First where noted.
RED project-validation.test.ts (T023) → lib/validation/project.ts using the frozen presets (T024); RED
use-project-mutations.test.ts (T025) → useProjects.ts + useProjectMutations.ts (T026, optimistic on ['projects'],
client-side nesting prevention message); RED sidebar.test.ts (T027) → Sidebar.tsx + mount in app/(app)/layout.tsx
(T028, Inbox + one-level tree + Archived disclosure); ProjectForm.tsx (T029, dialog focus contract, color+icon+text,
FR-031 in the name input); DeleteProjectDialog.tsx (T030, 3-way task + child disposition, states blast radius).
VERIFY: 'cd apps/web && pnpm test' green; 'pnpm typecheck' clean.`,
    { schema: STATUS, phase: 'US1-Web', effort: 'high' })
} else {
  log('Skipping US1-Web — Contract-1 (gen:api/typecheck) was not green.')
}

phase('US2-Backend')
const us2be = await agent(`${COMMON}
SCOPE: Phase 4 US2 backend, tasks T031–T036.
RED TaskTests MoveToProject case (T031) → Task.MoveToProject(projectId?, utcNow) (T032); RED MoveTaskToProjectTests
(T033, move / move-to-inbox(null) / both-ownership DENY / version_conflict / ALLOW) → MoveTaskToProject command +
PATCH /api/tasks/{id}/project endpoint (T034); RED InboxAndProjectTasksTests (T035) → narrow GetMyTasks with
'AND project_id IS NULL' keeping ORDER BY position,id (FR-021/R6) + GetProjectTasks query + GET /api/projects/{id}/tasks
(T036). Do NOT run gen:api. VERIFY: 'dotnet build' clean; 'dotnet test' integration suite green.`,
  { schema: STATUS, phase: 'US2-Backend', effort: 'high' })

let gen2 = null
if (us2be && us2be.buildGreen) {
  phase('Contract-2')
  gen2 = await agent(`${COMMON}
SCOPE: T037. Add nullable ProjectId to TaskResponse (+ From projection, R16); stamp the moveTaskToProject +
listProjectTasks operationIds in TaskFlowDocumentTransformer.cs (no ErrorCodes change). Then regenerate the typed
client using the same boot recipe as Contract-1 (API on :4311 with Postgres → 'cd apps/web && pnpm gen:api' → stop API).
VERIFY: 'cd apps/web && pnpm typecheck' clean; schema.d.ts has TaskResponse.projectId + the two new task/project ops.`,
    { schema: STATUS, phase: 'Contract-2', effort: 'high' })

  if (gen2 && gen2.buildGreen) {
    phase('US2-Web')
    await agent(`${COMMON}
SCOPE: Phase 4 US2 web, tasks T038–T041. RED use-task-mutations move case (T038) → add the move-to-project optimistic
mutation to useTaskMutations.ts (T039, moves a task across the ['tasks']/project caches, null→Inbox, rollback);
ProjectSelector.tsx (T040, the M selector, lists owned projects + Inbox, dialog focus contract); modify TaskRow.tsx
(project chip + open selector) and useGlobalShortcuts.ts (bind M to the selected task, suppressed in inputs) (T041).
VERIFY: 'cd apps/web && pnpm test' green; 'pnpm typecheck' clean.`,
      { schema: STATUS, phase: 'US2-Web', effort: 'high' })

    phase('E2E')
    await agent(`${COMMON}
SCOPE: T042. Create apps/web/tests/e2e/projects.spec.ts (Playwright) covering AS-01..AS-05, AS-07..AS-11, EC-03, the
M move (incl. move-to-Inbox), and the Inbox narrowing. The E2E suite self-boots its own stack on :4311 — ensure no
other process holds the port. VERIFY: 'cd apps/web && pnpm e2e' (or the project's documented e2e command). Report
pass/fail per scenario; if the harness can't boot in this environment, say so explicitly in blockers.`,
      { schema: STATUS, phase: 'E2E', effort: 'high' })
  } else {
    log('Skipping US2-Web + E2E — Contract-2 was not green.')
  }
} else {
  log('Skipping Contract-2 + US2-Web + E2E — US2-Backend did not compile.')
}

phase('Polish')
await agent(`${COMMON}
SCOPE: Phase 5 Polish, tasks T043–T048 — these are AUDITS, not new features. Verify and report (fix small gaps):
a11y (dialog focus contract on form/selector/dialogs; color+icon+text; FR-031/44/45/46/47); SC-003 optimistic <16ms +
client-side nesting message; security/logging (name React-escaped, preset-constrained, logs carry no name/owner);
CI gates (gen:api clean, ERROR_UX unchanged & exhaustive, TS strict + C# analyzers, allow+deny per handler); exactly
ONE migration (AddProjects) in the diff; FR-051 backup/restore-test gate status. Run 'dotnet build', 'cd apps/web &&
pnpm typecheck && pnpm test' as a final green check. Report per-task findings.`,
  { schema: STATUS, phase: 'Polish', effort: 'medium' })

// ---- Self-review policy: adversarial review over the full diff, then fix + re-review ----
phase('Self-Review')
let review = await agent(`${COMMON}
You are an INDEPENDENT, read-only reviewer. Do NOT modify files. Review the ENTIRE slice-004 implementation against
the design. The implementation is UNCOMMITTED in the working tree: run 'git status --short' to list ALL modified and
untracked files, READ the new/modified source files directly, and run 'git --no-pager diff HEAD' for tracked edits.
Verify, critically:
- All 48 tasks' intent is realized; Test-First honored (RED tests exist); allow+deny per handler.
- 404-before-422 parentId posture; whole-object edit (parentId required); versioned non-idempotent delete;
  archive vs delete lifecycles; Inbox narrowing keeps position ordering; no new error code; ERROR_UX exhaustive.
- The build compiles and tests pass: run 'dotnet build' and 'cd apps/web && pnpm typecheck'. Note any failures.
- Bugs, broken references, wrong assumptions, security gaps (name escaping, preset constraint), missing edge cases.
Report findings with severity + file + concrete fix. End with verdict OK or ISSUES_FOUND.`,
  { schema: REVIEW, phase: 'Self-Review', effort: 'high' })

let rounds = 0
while (review && review.verdict === 'ISSUES_FOUND'
       && (review.findings || []).some(f => f.severity === 'critical' || f.severity === 'high')
       && rounds < 2) {
  rounds++
  const blocking = review.findings.filter(f => f.severity === 'critical' || f.severity === 'high')
  await agent(`${COMMON}
SCOPE: Fix ONLY these reviewer-identified blocking issues, then re-verify the build/tests are green. Do not refactor
beyond the fixes. Issues to fix:\n${blocking.map((f, i) => `${i + 1}. [${f.severity}] ${f.file || ''}: ${f.issue}\n   Suggested fix: ${f.fix || '(use your judgment)'}`).join('\n')}
VERIFY: 'dotnet build' clean; 'cd apps/web && pnpm typecheck && pnpm test' green.`,
    { schema: STATUS, phase: 'Self-Review', effort: 'high', label: `fix-round-${rounds}` })

  review = await agent(`${COMMON}
You are a FRESH independent read-only reviewer. Do NOT modify files. Independently review the current slice-004
implementation, which is UNCOMMITTED in the working tree: run 'git status --short' to list all modified/untracked
files, READ the changed source directly, and use 'git --no-pager diff HEAD' for tracked edits. Verify the build
compiles ('dotnet build') and web typechecks ('cd apps/web && pnpm typecheck'). Look for correctness/security/
spec-alignment issues. Report findings with severity + file + fix and end with verdict OK or ISSUES_FOUND.
Empty findings is a valid result.`,
    { schema: REVIEW, phase: 'Self-Review', effort: 'high', label: `review-round-${rounds}` })
}

return {
  done: true,
  reviewRounds: rounds,
  finalReview: review,
  phases: { foundational: found, us1be, gen1, us2be, gen2 },
}

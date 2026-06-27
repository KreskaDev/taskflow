export const meta = {
  name: 'slice005-plan',
  description: 'Kick off slice 005 (Daily Planning): generate the Spec Kit design artifacts + tasks, with self-review',
  phases: [
    { title: 'Scout', detail: 'gather the slice-5 substrate digest (spec, constitution, prior plans, codebase)' },
    { title: 'Research', detail: 'research.md — design decisions (NodaTime, priorities, Today/Upcoming, task editor)' },
    { title: 'Model+Contract', detail: 'data-model.md + contracts/openapi.yaml' },
    { title: 'Plan+Quickstart', detail: 'plan.md (filled) + quickstart.md' },
    { title: 'Tasks', detail: 'tasks.md (Test-First, dependency-ordered)' },
    { title: 'Self-Review', detail: 'adversarial review of the artifacts for consistency + constitution alignment' },
  ],
}

const WROTE = {
  type: 'object',
  required: ['filesWritten', 'summary'],
  additionalProperties: true,
  properties: {
    filesWritten: { type: 'array', items: { type: 'string' } },
    summary: { type: 'string', description: 'One-paragraph summary of what was written + key decisions' },
    blockers: { type: 'array', items: { type: 'string' } },
  },
}

const REVIEW = {
  type: 'object',
  required: ['verdict', 'findings'],
  additionalProperties: true,
  properties: {
    verdict: { type: 'string', enum: ['OK', 'ISSUES_FOUND'] },
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

const REFS = `
You are doing Spec Kit PLANNING for slice 005 (Daily Planning) of the TaskFlow monorepo at E:\\specflow,
branch 005-daily-planning. This produces DESIGN DOCS ONLY — do NOT write application code, do NOT run builds.
AUTHORITATIVE inputs to read:
- specs/005-daily-planning/spec.md (the spec — the source of truth for scope/acceptance scenarios)
- .specify/memory/constitution.md (v4.0.0 — the 12 principles + governance; the Constitution Check gates)
- .specify/memory/product-vision.md (FR/entity IDs — product-vision is the sole ID allocator; keep FR text verbatim)
Mirror the STRUCTURE + depth of the prior slices' plans as templates:
- specs/004-project-management/{plan.md,research.md,data-model.md,contracts/openapi.yaml,quickstart.md,tasks.md}
- specs/003-natural-language-dates/plan.md (esp. the Time & Timezone handoff: slice 003 deferred NodaTime to
  slice 005 — "NodaTime arrives in slice 005, the first slice that computes same calendar day in Europe/Warsaw
  server-side (Today/Upcoming)". Slice 005 OWNS introducing NodaTime + the Npgsql NodaTime plugin.)
Key slice-005 design themes to get right (verify against the spec, don't invent scope): introduce NodaTime
server-side for Today/Upcoming membership computed against Europe/Warsaw (Constitution X / FR-092); activate the
Task priority (P0–P3) and description reserved columns (slice-002 forward-compat); the Today + Upcoming read
queries; a full keyboard-driven task editor (priority, description, reschedule due date, project); authorization
rides the existing ownership branch (no new auth surface — like slice 003). No org/tenant dimension.
Write files with the Write tool to their exact paths under specs/005-daily-planning/. Be precise + internally
consistent (field names, error codes, endpoints must agree across all artifacts).
`

phase('Scout')
const digest = await agent(`${REFS}
TASK: Produce a tight, factual DIGEST (return as text; also you may read freely) of the slice-005 substrate so the
later planning phases are accurate. Cover:
1. The slice-005 spec: list every user story + acceptance scenario id, the owned FRs, the entities/attributes it
   touches, the explicit out-of-scope, and the success criteria. Quote the FR text where it matters.
2. The Task entity reserved columns this slice activates — read apps/api/src/TaskFlow.Domain/TaskManagement/Task.cs
   and apps/api/src/TaskFlow.Infrastructure/Persistence/Configurations/TaskConfiguration.cs: confirm priority +
   description columns exist (types/nullability) and how status/due_date are modeled. Report the existing query
   pattern (GetMyTasks / GetProjectTasks) + how due_date is stored (UTC timestamptz + due_has_time).
3. The time-rule handoff: read specs/003-natural-language-dates/plan.md + research.md — exactly what slice 003
   deferred to slice 005 re NodaTime, and the Europe/Warsaw reference-zone rule (FR-092). Note the client TZ seam
   (apps/web/src/lib/timezone.ts) already present.
4. The web substrate: the current routing/views (apps/web/src/app/(app)/), the sidebar (slice 004), TaskRow/TaskList,
   the hooks pattern (useTasks/useTaskMutations), the generated client + gen:api, and the error contract
   (TaskFlowDocumentTransformer ErrorCodes + the ERROR_UX map). Note what a task editor would reuse.
5. The doc-artifact conventions from slice 004's plan/research/data-model/contracts/tasks (headings + structure)
   so the new artifacts match house style.
Return the digest organized by these 5 points with file:line citations. Do NOT write any files in this phase.`,
  { schema: WROTE, phase: 'Scout', effort: 'high' }).catch(() => null)

// Scout returns a WROTE-shaped object; fold its summary into the context the writers get.
const notes = digest?.summary ?? 'Scout digest unavailable — writers must read the sources directly.'

phase('Research')
await agent(`${REFS}
SCOUT DIGEST (context):\n${notes}\n
TASK: Write specs/005-daily-planning/research.md — the design decisions (R1..Rn), Decision/Rationale/Alternatives
format, mirroring slice-004 research.md depth. MUST cover at minimum: introducing NodaTime + the Npgsql NodaTime
plugin and WHERE the server computes "same calendar day in Europe/Warsaw" for Today/Upcoming (FR-092/Constitution X);
the priority model (P0–P3) activation; the description field activation (and whether markdown rendering is in scope
per the spec/Constitution XII — confirm against the spec, don't assume); the Today vs Upcoming membership rules
(overdue handling, the 7-day window, ordering); the full task-editor surface (which fields, which endpoints — reuse
vs new); the read-query shape (server-side zone-aware filtering vs client); error-contract impact (reuse existing
codes if possible); authorization (rides ownership, no new surface). Resolve every NEEDS-CLARIFICATION against the spec.`,
  { schema: WROTE, phase: 'Research', effort: 'high' }).catch(() => null)

phase('Model+Contract')
await agent(`${REFS}
SCOUT DIGEST (context):\n${notes}\n
TASK: Read specs/005-daily-planning/research.md (just written) and write TWO files, internally consistent with it:
1. specs/005-daily-planning/data-model.md — the Task attribute activations (priority, description) with types/
   constraints/validation; the Today/Upcoming read models + their zone-aware membership rules; any new value objects;
   the NodaTime mapping note (Instant/LocalDate, the Npgsql plugin); state transitions if any; the migration plan
   (is a migration needed? the columns pre-exist from slice 002 — confirm; NodaTime is a mapping/dependency change,
   not necessarily a schema change — reason about it); authorization scoping. Mirror slice-004 data-model.md headings.
2. specs/005-daily-planning/contracts/openapi.yaml — the API contract DELTA (new Today/Upcoming endpoints, the task-
   editor PATCH endpoints for priority/description/reschedule, TaskResponse additions like priority/description).
   Reuse existing error responses; no new errorCode unless the spec truly requires one (justify). Valid OpenAPI 3.1
   YAML. Mirror slice-004 contracts/openapi.yaml style (delta header note + changed/added schemas).`,
  { schema: WROTE, phase: 'Model+Contract', effort: 'high' }).catch(() => null)

phase('Plan+Quickstart')
await agent(`${REFS}
SCOUT DIGEST (context):\n${notes}\n
TASK: Read the just-written research.md + data-model.md + contracts/openapi.yaml and write TWO files:
1. specs/005-daily-planning/plan.md — fill the plan fully (Summary; Technical Context incl. the NEW NodaTime +
   Npgsql.NodaTime dependency; a 12-principle Constitution Check table marking each PASS with how-addressed —
   Principle X is OWNED/exercised here for the first time server-side; Project Structure source tree with (NEW)/
   (MODIFY) annotations; Key Design Decisions cross-referencing research; Complexity Tracking for any
   named-no-op/deferred items). Mirror specs/004-project-management/plan.md structure EXACTLY.
2. specs/005-daily-planning/quickstart.md — the runnable validation guide (the Today/Upcoming scenarios, priority
   triage, the task editor edits, server-validation table), mirroring slice-004 quickstart.md.
Also update the agent-context marker: ensure CLAUDE.md's <!-- SPECKIT --> block points at specs/005-daily-planning/plan.md
(it should already — leave it if so).`,
  { schema: WROTE, phase: 'Plan+Quickstart', effort: 'high' }).catch(() => null)

phase('Tasks')
await agent(`${REFS}
TASK: Read ALL the slice-005 artifacts just written (plan.md, research.md, data-model.md, contracts/openapi.yaml,
quickstart.md) and write specs/005-daily-planning/tasks.md — the dependency-ordered, Test-First task list, mirroring
specs/004-project-management/tasks.md EXACTLY in format: the checklist format "- [ ] T### [P?] [Story?] Description
with file path"; phases (Setup → Foundational → User Stories in priority order → Polish); Test-First ordering (the
RED test task has a LOWER id than the impl it covers); allow+deny per new data handler (Constitution VIII/IX);
the gen:api join points; a Dependencies & Execution Order section + parallel examples + an Implementation Strategy.
Every task must name exact file paths under apps/api/... and apps/web/....`,
  { schema: WROTE, phase: 'Tasks', effort: 'high' }).catch(() => null)

phase('Self-Review')
const review = await agent(`${REFS}
You are an INDEPENDENT read-only reviewer. Do NOT modify files. Read ALL six slice-005 artifacts under
specs/005-daily-planning/ (spec.md, research.md, data-model.md, contracts/openapi.yaml, plan.md, quickstart.md,
tasks.md) and review CRITICALLY for: (1) every spec acceptance scenario + owned FR is covered by the plan/tasks;
(2) internal consistency across artifacts (field names, endpoints, error codes, entity attributes agree); (3) the
Constitution Check claims are accurate and not hand-waved — especially Principle X (NodaTime/Europe/Warsaw, owned
here), VI (type safety / contract regen), IX (authorization), VII (migration/backup if any); (4) the NodaTime
introduction is correctly scoped (where it computes, the Npgsql plugin, no fixed-offset arithmetic); (5) anything
invented beyond the spec or contradicting it; (6) the contracts/openapi.yaml is well-formed and reuses error codes.
Report concrete findings with severity + file + fix. End with verdict OK or ISSUES_FOUND.`,
  { schema: REVIEW, phase: 'Self-Review', effort: 'high' }).catch(() => null)

return { done: true, scoutOk: !!digest, review }

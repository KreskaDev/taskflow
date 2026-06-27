export const meta = {
  name: 'slice007-plan',
  description: 'Kick off slice 007 (Project Sharing, Membership & Roles): generate the Spec Kit design artifacts + tasks, with self-review',
  phases: [
    { title: 'Scout', detail: 'gather the slice-7 substrate digest (spec, constitution, slice-004 Project aggregate, the authz policy, the slice-005 seam)' },
    { title: 'Research', detail: 'research.md — design decisions (ProjectMembership aggregate, roles, personal↔shared, invite/transfer/leave, the membership+role authz branch)' },
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
You are doing Spec Kit PLANNING for slice 007 (Project Sharing, Membership & Roles) of the TaskFlow monorepo at
E:\\specflow, branch 007-project-sharing-membership. This produces DESIGN DOCS ONLY — do NOT write application code,
do NOT run builds. AUTHORITATIVE inputs to read:
- specs/007-project-sharing-membership/spec.md (the spec — source of truth for scope/acceptance scenarios)
- .specify/memory/constitution.md (v4.0.0 — esp. Principle IX Authorization: the membership+role branch is the
  CENTRE of this slice; and the Architecture note that the authorization policy consumes each shared project's
  ProjectMembership set, modeled in the Project aggregate per ADR-0003)
- .specify/memory/product-vision.md (FR/entity IDs — product-vision is the sole ID allocator; keep FR text verbatim)
Mirror the STRUCTURE + depth of the prior slices' plans as templates:
- specs/004-project-management/{plan.md,research.md,data-model.md,contracts/openapi.yaml,quickstart.md,tasks.md}
  — slice 004 OWNS the Project aggregate + the 'visibility' column (only 'personal' writable so far) + the
  ownership authorization branch. Slice 007 EXTENDS this: it realizes the 'shared' visibility value, introduces
  the ProjectMembership set + roles, and the membership+role authorization branch.
- specs/005-daily-planning/plan.md — slice 005 shaped a 'dispatch-by-visibility seam' and left the membership arm
  as a named not-yet-realized branch awaiting THIS slice. 007 is the access foundation that 005 (and assignment,
  comments, real-time, notifications) build on.
Key slice-007 design themes to get right (verify against the spec, don't invent scope): the ProjectMembership
aggregate/entity (member User + role) modeled per ADR-0003 (consumed by the application-layer authorization policy);
the role set (viewer = read-only, editor = write/comment, owner = manage/share/delete; owner = the immutable
ownerId moved only by an explicit transfer command, NOT a freely-assignable role); convert personal↔shared
(reversibly); invite members BY EMAIL resolved against existing signed-in Users only (OOS-18 — no pending/pre-account
invites); change role / transfer ownership / remove member / leave; the deny-by-default membership+role
authorization branch dispatched by the containing project's visibility (FR-065/066/067/068); the rule that
leave/remove/unshare revokes ALL access to that project's data regardless of authorship/assignment (FR-066); the
'last owner' guard (cannot remove/demote the last owner — note the existing 'last_owner' error code already in the
contract). Real-time subscription re-authorization (SignalR) is slice 016 — 007 names the seam, does not build it.
Authorization here REQUIRES allow + deny tests AND a role-matrix (viewer-denied-write, non-member-denied-read,
removed-member-loses-access). Per Constitution governance, authorization changes need careful, tested treatment.
Write files with the Write tool to their exact paths under specs/007-project-sharing-membership/. Be precise +
internally consistent (field names, error codes, endpoints, role tokens must agree across all artifacts).
`

phase('Scout')
const digest = await agent(`${REFS}
TASK: Produce a tight, factual DIGEST (return as text; read freely) of the slice-007 substrate. Cover:
1. The slice-007 spec: every user story + acceptance scenario id, owned FRs (quote FR-057 shared half, FR-066,
   FR-067 role rules, plus the sharing/invite/transfer/leave FRs), the ProjectMembership entity (ENT), the explicit
   out-of-scope (esp. OOS-18 no pre-account invites; real-time is slice 016), and the success criteria (incl. the
   SC-016 deny tests this slice finally makes realizable).
2. The slice-004 Project aggregate + authorization substrate — read apps/api/src/TaskFlow.Domain/TaskManagement/
   Project.cs (the Visibility property — confirm 'shared' is reserved-but-not-writable), apps/api/src/TaskFlow.
   Application/TaskManagement/ProjectResponse.cs, and the authorization wiring (find ICurrentUser + any existing
   authorization policy/Authorization namespace + how ownership is enforced today). Report how a ProjectMembership
   set would attach to Project (ADR-0003) and how the policy would consume it.
3. The error contract — read apps/api/src/TaskFlow.Api/OpenApi/TaskFlowDocumentTransformer.cs + apps/web/src/lib/
   api/client.ts: confirm the existing error codes (esp. 'last_owner' and 'forbidden' — are they already present?
   does this slice finally USE them?). Report how a new code is added if truly needed.
4. The web substrate: the sidebar (slice 004), the project form/dialogs, the hooks (useProjects/useProjectMutations),
   the generated client + gen:api. Note what a sharing/members UI would reuse.
5. The doc-artifact conventions from slice 004's plan/research/data-model/contracts/tasks so the new artifacts match
   house style. Return the digest organized by these 5 points with file:line citations. Do NOT write files here.`,
  { schema: WROTE, phase: 'Scout', effort: 'high' }).catch(() => null)

const notes = digest?.summary ?? 'Scout digest unavailable — writers must read the sources directly.'

phase('Research')
await agent(`${REFS}
SCOUT DIGEST (context):\n${notes}\n
TASK: Write specs/007-project-sharing-membership/research.md — the design decisions (R1..Rn), Decision/Rationale/
Alternatives format, mirroring slice-004 research.md depth. MUST cover at minimum: the ProjectMembership model (own
table/aggregate vs owned collection on Project per ADR-0003 — reason about it); the role set + the owner-as-immutable-
ownerId vs assignable-role distinction (transfer-ownership command); convert personal↔shared semantics (what happens
to membership when re-personalizing); invite-by-email resolution against existing Users (404/leak posture for unknown
emails, OOS-18); the membership+role authorization branch (how the app-layer policy dispatches on visibility, FR-065;
viewer/editor/owner gates, FR-067; the revoke-ALL-on-leave/remove/unshare rule, FR-066); the last-owner guard (reuse
the existing 'last_owner' error code); the migration (a new project_memberships table — this slice DOES migrate, so
FR-051 backup-before-migrate is LIVE, contrast slice-005's no-op); the real-time re-auth SEAM (slice 016, named not
built); error-contract impact (forbidden/last_owner now used). Resolve every NEEDS-CLARIFICATION against the spec.`,
  { schema: WROTE, phase: 'Research', effort: 'high' }).catch(() => null)

phase('Model+Contract')
await agent(`${REFS}
SCOUT DIGEST (context):\n${notes}\n
TASK: Read specs/007-project-sharing-membership/research.md (just written) and write TWO files, consistent with it:
1. specs/007-project-sharing-membership/data-model.md — the ProjectMembership entity (fields: project ref, member
   User ref, role, timestamps; the role enum; uniqueness per (project, member)); the Project changes (shared
   visibility now writable; owner transfer); validation rules + invariants (last-owner guard, role transitions,
   one membership per user per project); the authorization scoping rules (dispatch by visibility; viewer/editor/owner;
   revoke-all); state transitions (personal↔shared, role changes, ownership transfer, member lifecycle); the MIGRATION
   plan (new project_memberships table + FK/indexes — FR-051 LIVE this slice). Mirror slice-004 data-model.md headings.
2. specs/007-project-sharing-membership/contracts/openapi.yaml — the API contract DELTA (share/unshare, invite/add
   member, change-role, transfer-ownership, remove-member, leave; list-members; the membership-aware reads if any).
   Use 403 forbidden (insufficient role) + 409 last_owner + 404 (non-member/foreign) appropriately; add a new
   errorCode ONLY if the spec truly requires one (justify). Valid OpenAPI 3.1 YAML, mirroring slice-004 style.`,
  { schema: WROTE, phase: 'Model+Contract', effort: 'high' }).catch(() => null)

phase('Plan+Quickstart')
await agent(`${REFS}
SCOUT DIGEST (context):\n${notes}\n
TASK: Read the just-written research.md + data-model.md + contracts/openapi.yaml and write TWO files:
1. specs/007-project-sharing-membership/plan.md — fill the plan fully (Summary; Technical Context; a 12-principle
   Constitution Check table marking each PASS with how-addressed — Principle IX is OWNED/central here, the membership+
   role branch; Principle VII FR-051 is LIVE — a migration ships; the governance rule that authorization changes need
   a non-author reviewer + allow+deny tests; Project Structure source tree with (NEW)/(MODIFY); Key Design Decisions
   cross-referencing research; Complexity Tracking incl. the real-time re-auth seam deferred to slice 016). Mirror
   specs/004-project-management/plan.md structure EXACTLY.
2. specs/007-project-sharing-membership/quickstart.md — the runnable validation guide (share a project, invite a
   member, the role matrix: viewer-denied-write / editor-allowed / non-member-denied-read, transfer ownership,
   last-owner guard, remove/leave revokes all access, re-personalize). Mirror slice-004 quickstart.md.`,
  { schema: WROTE, phase: 'Plan+Quickstart', effort: 'high' }).catch(() => null)

phase('Tasks')
await agent(`${REFS}
TASK: Read ALL the slice-007 artifacts just written (plan.md, research.md, data-model.md, contracts/openapi.yaml,
quickstart.md) and write specs/007-project-sharing-membership/tasks.md — the dependency-ordered, Test-First task
list, mirroring specs/004-project-management/tasks.md EXACTLY in format: the checklist format
"- [ ] T### [P?] [Story?] Description with file path"; phases (Setup → Foundational → User Stories in priority order
→ Polish); Test-First ordering (RED test task LOWER id than the impl); per new data handler an ALLOW + a DENY test
AND the role-matrix deny tests (viewer-write-denied, non-member-read-denied, removed-member-access-revoked,
last-owner-guard) — authorization is the heart of this slice; the gen:api join point; the AddProjectMemberships
migration; a Dependencies & Execution Order section + parallel examples + an Implementation Strategy. Every task
must name exact file paths under apps/api/... and apps/web/....`,
  { schema: WROTE, phase: 'Tasks', effort: 'high' }).catch(() => null)

phase('Self-Review')
const review = await agent(`${REFS}
You are an INDEPENDENT read-only reviewer. Do NOT modify files. Read ALL six slice-007 artifacts under
specs/007-project-sharing-membership/ (research.md, data-model.md, contracts/openapi.yaml, plan.md, quickstart.md,
tasks.md) + spec.md and review CRITICALLY for: (1) every spec acceptance scenario + owned FR covered by plan/tasks;
(2) internal consistency across artifacts (role tokens, endpoints, error codes, entity attributes agree); (3) the
Constitution Check claims accurate — ESPECIALLY Principle IX (the membership+role branch: dispatch-by-visibility,
viewer/editor/owner gates, revoke-all-on-leave/remove/unshare, last-owner guard, allow+deny+role-matrix tests),
Principle VII (FR-051 LIVE — the migration), and the governance non-author-reviewer rule; (4) the authorization model
is sound — no privilege-escalation hole, no way a removed member retains access, owner-transfer is explicit-only,
last-owner cannot be removed/demoted; (5) anything invented beyond the spec or contradicting it (esp. OOS-18 no
pre-account invites; real-time is slice 016, must be a named seam not built); (6) contracts/openapi.yaml well-formed,
error codes used correctly (403/409 last_owner/404). Report concrete findings with severity + file + fix. End with
verdict OK or ISSUES_FOUND.`,
  { schema: REVIEW, phase: 'Self-Review', effort: 'high' }).catch(() => null)

return { done: true, scoutOk: !!digest, review }

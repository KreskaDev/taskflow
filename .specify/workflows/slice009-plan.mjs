export const meta = {
  name: 'slice009-plan',
  description: 'Kick off slice 009 (Comments & @Mentions): generate the Spec Kit design artifacts + tasks, with self-review',
  phases: [
    { title: 'Scout', detail: 'gather the slice-9 substrate digest (spec, constitution, the slice-007 membership+role authz policy, the Task entity, the event/outbox substrate, web thread reuse)' },
    { title: 'Research', detail: 'research.md — design decisions (Comment aggregate, authorship object-level grant, @mention typed token + UserMentioned event, the comment-on-shared-task authz gate, FR-098/099 safety/sanitization, the migration)' },
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
You are doing Spec Kit PLANNING for slice 009 (Comments & @Mentions) of the TaskFlow monorepo at
E:\\specflow, branch 009-comments-mentions. This produces DESIGN DOCS ONLY — do NOT write application code,
do NOT run builds. AUTHORITATIVE inputs to read:
- specs/009-comments-mentions/spec.md (the spec — source of truth for scope/acceptance scenarios; US-14 AS-01..AS-04)
- .specify/memory/constitution.md (v4.0.0 — esp. Principle IX Authorization: comments live ONLY on shared-project
  tasks, so they authorize on the containing project's current ProjectMembership + role — dispatch-by-visibility,
  NOT a Tier A/B conjunction; Principle XII Security — comment bodies + @mention tokens are the FIRST free-form
  user content and a stored-XSS surface; Principle XI Privacy — author identity must be anonymizable to a tombstone)
- .specify/memory/product-vision.md (FR/entity IDs — product-vision is the sole ID allocator; keep FR text verbatim)

Slice 009 builds DIRECTLY on slice 007 (project-sharing-membership) and references slice 008 (assignment). BOTH are
already implemented and present in the tree. Mirror the STRUCTURE + depth of the prior slices' plans as templates,
with slice 007 as the PRIMARY template (it owns the membership+role authorization branch this slice consumes):
- specs/007-project-sharing-membership/{plan.md,research.md,data-model.md,contracts/openapi.yaml,quickstart.md,tasks.md}
  — slice 007 OWNS ProjectMembership, the role set (owner/editor/viewer), the dispatch-by-visibility authorization
  policy (ResolveEffectiveRole / RequireRole), the 'forbidden'(403)/'last_owner'(409) error-code usage, the
  UserMentioned-style domain events raised to the Wolverine outbox, and the AddProjectMemberships migration +
  FR-051-live posture. Slice 009 CONSUMES this policy: a comment write requires editor|owner on the parent task's
  shared project; a viewer is read-only; a non-member gets 404.
- specs/004-project-management/tasks.md — the canonical Test-First tasks.md format/depth to mirror.

Key slice-009 design themes to get right (verify against the spec, DO NOT invent scope):
- ENT-08 Comment aggregate/entity: author User ref, parent Task ref, body, the set of @mentioned member User-ids,
  created_at/edited_at (UTC). Reason about aggregate boundary (its own aggregate root vs owned — comments are a
  first-class list on a Task in a shared project; contrast the slice-007 ProjectMembership-in-Project decision and
  the slice-008 assignees relation, and PICK with justification).
- Authorization: comments exist ONLY on tasks in SHARED projects (personal projects have no membership set →
  no commenting). Every comment read/write dispatches by the parent project's Visibility onto the slice-007
  membership+role branch (FR-065/066/067/068): read = viewer+ (any current member); post = editor|owner (viewer
  DENIED → 403); non-member → 404 (no existence leak). REUSE the slice-007 policy; do not fork it.
- Authorship as a distinct OBJECT-LEVEL grant (FR-075): only the comment's author may edit/delete it — project role
  (INCLUDING the project owner) does NOT override authorship. BUT loss of project membership OVERRIDES the author
  grant: a former member (left/removed/unshare) can neither view, edit, nor delete their previously authored
  comments (FR-066 revoke-ALL wins over the author grant). Model this ordering explicitly.
- @mention: stored as a TYPED TOKEN referencing a User id (NOT free text); mention candidates limited to CURRENT
  members of the comment's parent project; a non-member is not mentionable. Each @mention emits a UserMentioned
  domain event to the Wolverine outbox — RAISED here, CONSUMED by slice 017 (notifications). This slice does NOT
  build the notification center / toast / mark-read / preferences (slice 017 seam — name it, do not build it).
- FR-098 comment safety (OWNED + introduced here): max length bound; empty/whitespace-only rejected (no thread
  entry); output sanitized to a CONSTRAINED SAFE SUBSET (plain text + safe markdown) so raw HTML/script injection
  is impossible on render; @mention stored as the typed User-id token. FR-099: output-encoding + CSP + standard
  security response headers in production (justify where CSP/headers live — middleware).
- Migration: a NEW 'comments' table ships → FR-051 backup-before-migrate is LIVE this slice (mirror the slice-007
  AddProjectMemberships posture: the first entity migration is a real gate). FK to tasks + to users; the @mention
  set persistence (a typed collection / child table). No DDL beyond the new table(s).
- Error contract: prefer NO new error code (reuse validation_failed(422 empty/over-length), forbidden(403 viewer
  posting), not_found(404 non-member/foreign), version_conflict(409) if comments are versioned). Justify if the
  spec truly forces a new code (it should not — R16 posture).
- Time (Principle X): created_at/edited_at UTC (timestamptz); any relative-time display computed against the single
  instance reference timezone Europe/Warsaw (per-user tz OOS). Follow the repo's existing time posture — do not
  introduce a new date library if the repo has an established one; report what the repo uses.
- Privacy (Principle XI): model the Comment so the author reference is anonymizable to a tombstone (nullable /
  reassignable) for the slice-015 erasure cascade, and the @mention token is subject to the same residual rule —
  MODEL for it, do not build slice-015's cascade.
- Real-time (Principle III / slice 016): a posted/edited/deleted comment paints optimistically within one frame,
  server reconciles under last-write-wins without clobbering a pending local edit; but LIVE propagation of a remote
  comment into an open thread is slice 016 (real-time-collaboration) — name the seam, do not build SignalR here.

Write files with the Write tool to their exact paths under specs/009-comments-mentions/. Be precise + internally
consistent (field names, error codes, endpoints, role tokens, event names must agree across ALL artifacts).
`

phase('Scout')
const digest = await agent(`${REFS}
TASK: Produce a tight, factual DIGEST (return as text; read freely) of the slice-009 substrate. Cover:
1. The slice-009 spec: every user story + acceptance scenario id (US-14 AS-01..AS-04), the owned FRs (quote FR-072
   viewer-cannot-comment, FR-073 author grant + @mentions, FR-074 mention-notifies, FR-075 author-only edit/delete
   with the membership-loss override, FR-098 safety rules), ENT-08 Comment, the edge cases (viewer read-only,
   author-grant vs owner, former-member loses author right, mention candidacy = current members, personal projects
   have no comments, empty/over-length rejected, sanitization + typed token), and SC-013/SC-016 (allow+deny per
   handler + the role deny matrix).
2. The slice-007 authorization substrate to REUSE — read apps/api/src/TaskFlow.Application/Authorization/
   ResourceAuthorizationPolicy.cs + IResourceAuthorizationPolicy.cs (the ResolveEffectiveRole / RequireRole
   dispatch-by-visibility methods), apps/api/src/TaskFlow.Domain/TaskManagement/ProjectMembership.cs, and how a
   handler loads the Project + its ProjectMembership set to authorize. Report EXACTLY how a comment handler would
   call the policy to gate read (viewer+) vs post (editor|owner) vs non-member (404), and where authorship
   (author-only edit/delete, membership-loss override) layers on top.
3. The Task + event/outbox substrate — read apps/api/src/TaskFlow.Domain/TaskManagement/Task.cs (the parent), a
   slice-007/008 domain event (e.g. Events/ProjectShared.cs or TaskAssigned.cs) + how it is raised to the Wolverine
   transactional outbox, so UserMentioned mirrors it exactly. Report the id value-object pattern (TaskId/
   ProjectMembershipId) a CommentId must mirror.
4. The error contract + security substrate — read apps/api/src/TaskFlow.Api/OpenApi/TaskFlowDocumentTransformer.cs
   (confirm validation_failed/forbidden/not_found/version_conflict already exist → NO new code) + apps/web/src/lib/
   api/client.ts (ERROR_UX exhaustive map). Report where security headers / CSP + output sanitization live or should
   live (middleware), and the repo's date/time posture (timestamptz; any existing relative-time util).
5. The web substrate: how a task detail/thread would mount, the existing hooks (optimistic vs invalidate-on-settle —
   slice 002/004 optimistic vs slice 007 non-optimistic), the gen:api recipe, and the doc-artifact conventions from
   slice 007's plan/research/data-model/contracts/tasks so the new artifacts match house style.
Return the digest organized by these 5 points with file:line citations. Do NOT write files here.`,
  { schema: WROTE, phase: 'Scout', effort: 'high' }).catch(() => null)

const notes = digest?.summary ?? 'Scout digest unavailable — writers must read the sources directly.'

phase('Research')
await agent(`${REFS}
SCOUT DIGEST (context):\n${notes}\n
TASK: Write specs/009-comments-mentions/research.md — the design decisions (R1..Rn), Decision/Rationale/Alternatives
format, mirroring slice-007 research.md depth. MUST cover at minimum: the Comment aggregate boundary (own aggregate
root vs owned-on-Task — reason vs the slice-007 ProjectMembership and slice-008 assignees decisions); the authorship
object-level grant + the STRICT ordering (membership-loss revokes access BEFORE the author grant is consulted, FR-066
> FR-075); the comment-on-shared-task authorization (reuse the slice-007 ResolveEffectiveRole/RequireRole policy —
read=viewer+, post/reply=editor|owner, non-member=404, viewer-post=403; personal project = no comments); the @mention
model (typed User-id token, candidacy = current members, persistence as a child collection/table) + the UserMentioned
domain event raised to the outbox (consumed by slice 017 — a NAMED seam); FR-098 safety (max length, empty rejected,
sanitize-to-safe-subset, typed token) + FR-099 (CSP + security headers — where they live); the 'comments' migration
(new table + FKs + the mention persistence — FR-051 LIVE this slice, mirror slice-007 AddProjectMemberships); the
error-contract posture (NO new code — reuse validation_failed/forbidden/not_found/version_conflict, justify);
the time posture (UTC timestamptz + Europe/Warsaw relative display, follow the repo's existing util); the privacy
model (author anonymizable to a tombstone for slice-015; @mention residual rule); and the real-time SEAM (slice 016 —
optimistic paint now, live propagation later, named not built). Resolve every NEEDS-CLARIFICATION against the spec.`,
  { schema: WROTE, phase: 'Research', effort: 'high' }).catch(() => null)

phase('Model+Contract')
await agent(`${REFS}
SCOUT DIGEST (context):\n${notes}\n
TASK: Read specs/009-comments-mentions/research.md (just written) and write TWO files, consistent with it:
1. specs/009-comments-mentions/data-model.md — the Comment entity (fields: CommentId value object, parent Task ref,
   author User ref [anonymizable/nullable for the slice-015 tombstone], body [bounded], the @mentioned User-id set,
   created_at/edited_at UTC; whether it carries its own version/concurrency token); the @mention persistence shape
   (typed token collection / child table, FK to users); validation rules + invariants (non-empty/whitespace-only
   rejected, max length, mention candidacy = current project members, comments only on shared-project tasks); the
   AUTHORIZATION rules table (dispatch by visibility; read=viewer+, post=editor|owner, edit/delete=author-only with
   the membership-loss override; non-member=404); state transitions (create → edit → delete; author anonymization
   seam); and the MIGRATION plan (new comments [+ mention] table, FKs/indexes — FR-051 LIVE). Mirror slice-007
   data-model.md headings.
2. specs/009-comments-mentions/contracts/openapi.yaml — the API contract DELTA (list comments for a task; post a
   comment [body + @mentions]; edit own comment; delete own comment; the mention-candidates lookup if the design
   needs one). Use 403 forbidden (viewer posting / non-author edit-delete), 404 (non-member / foreign task or
   comment), 422 validation_failed (empty/over-length), 409 version_conflict if versioned — REUSE existing codes,
   add NO new errorCode (justify). Valid OpenAPI 3.1 YAML, mirroring slice-007 style.`,
  { schema: WROTE, phase: 'Model+Contract', effort: 'high' }).catch(() => null)

phase('Plan+Quickstart')
await agent(`${REFS}
SCOUT DIGEST (context):\n${notes}\n
TASK: Read the just-written research.md + data-model.md + contracts/openapi.yaml and write TWO files:
1. specs/009-comments-mentions/plan.md — fill the plan fully (Summary; Technical Context; a 12-principle Constitution
   Check table marking each PASS with how-addressed — Principle IX is CONSUMED here [the slice-007 membership+role
   branch gates commenting; authorship is an object-level grant on top], Principle XII is CENTRAL [first free-form
   user content → FR-098 sanitize + FR-099 CSP/headers], Principle VII FR-051 is LIVE [the comments migration ships],
   Principle XI [author anonymizable], Principle III [optimistic paint; live propagation is the slice-016 seam],
   the governance rule that authorization changes need a non-author reviewer + allow+deny tests); Project Structure
   source tree with (NEW)/(MODIFY); Key Design Decisions cross-referencing research; Complexity Tracking incl. the
   slice-017 notification seam + the slice-016 real-time seam + the slice-015 anonymization seam, all deferred).
   Mirror specs/007-project-sharing-membership/plan.md structure EXACTLY.
2. specs/009-comments-mentions/quickstart.md — the runnable validation guide (as an editor post a comment on a shared
   task; @mention a member and assert the UserMentioned event/outbox; as a viewer confirm NO comment input + a post
   attempt is 403; as a non-member a thread read is 404; author edits/deletes own comment; a non-author owner is
   denied edit/delete [403]; a removed member loses access to their own authored comment [404]; empty + over-length
   rejected [422]; sanitization: an HTML/script payload renders inert). Mirror slice-007 quickstart.md.`,
  { schema: WROTE, phase: 'Plan+Quickstart', effort: 'high' }).catch(() => null)

phase('Tasks')
await agent(`${REFS}
TASK: Read ALL the slice-009 artifacts just written (plan.md, research.md, data-model.md, contracts/openapi.yaml,
quickstart.md) and write specs/009-comments-mentions/tasks.md — the dependency-ordered, Test-First task list,
mirroring specs/004-project-management/tasks.md and specs/007-project-sharing-membership/tasks.md EXACTLY in format:
the checklist "- [ ] T### [P?] [Story?] Description with exact file path"; phases (Setup → Foundational → User Story
US-14 → Polish); Test-First ordering (the RED test task has a LOWER id than the impl it covers); per NEW data handler
an ALLOW + a DENY test AND the role/authorship deny-matrix tests (viewer-post-denied 403, non-member-read-denied 404,
non-author-edit/delete-denied 403 incl. the project owner, removed-member-loses-authored-comment 404, empty/over-length
422) — authorization + the authorship grant are the heart of this slice; the single gen:api join point; the
AddComments migration (FR-051 live); a Dependencies & Execution Order section + parallel examples + an Implementation
Strategy. Name exact file paths under apps/api/... (Domain/TaskManagement Comment + CommentId + Events/UserMentioned;
Application/TaskManagement comment commands/queries + the reused Authorization policy; Infrastructure/Persistence
CommentConfiguration + repository + migration; Api/Endpoints CommentEndpoints; OpenApi transformer) and apps/web/...
(lib/validation comment schema; hooks useComments/useCommentMutations; components the thread + composer + @mention
picker + edit/delete; tests unit + e2e). Encode the slice-016/017/015 seams as explicit 'named, not built' notes.`,
  { schema: WROTE, phase: 'Tasks', effort: 'high' }).catch(() => null)

phase('Self-Review')
const review = await agent(`${REFS}
You are an INDEPENDENT read-only reviewer. Do NOT modify files. Read ALL six slice-009 artifacts under
specs/009-comments-mentions/ (research.md, data-model.md, contracts/openapi.yaml, plan.md, quickstart.md, tasks.md)
+ spec.md and review CRITICALLY for: (1) every spec acceptance scenario (US-14 AS-01..AS-04) + owned FR
(072/073/074/075/098/099) covered by plan/tasks; (2) internal consistency across artifacts (entity fields, endpoints,
error codes, role tokens, the UserMentioned event name agree); (3) the Constitution Check claims accurate —
ESPECIALLY Principle IX (comments authorize on the CONSUMED slice-007 membership+role branch dispatched by
visibility: read=viewer+, post=editor|owner, non-member=404; NOT a new/forked policy), the AUTHORSHIP grant + its
ordering (membership-loss OVERRIDES the author edit/delete grant — a former member is denied even on their own
comment), Principle XII (FR-098 sanitize-to-safe-subset + typed @mention token + FR-099 CSP/headers — the stored-XSS
surface is actually closed), Principle VII (FR-051 LIVE — the comments migration), Principle XI (author anonymizable
seam), and the governance non-author-reviewer rule; (4) the authorization + authorship model is SOUND — no way a
viewer posts, no way a non-author (incl. the owner) edits/deletes, no way a removed member retains access to their
authored comment, @mention candidacy limited to current members, comments impossible on personal-project tasks;
(5) anything invented beyond the spec or contradicting it (esp. NOT building slice-017 notification delivery, slice-016
real-time propagation, or slice-015 anonymization cascade — each must be a NAMED seam only; NO new error code);
(6) contracts/openapi.yaml well-formed, error codes used correctly (403/404/422/409). Report concrete findings with
severity + file + fix. End with verdict OK or ISSUES_FOUND.`,
  { schema: REVIEW, phase: 'Self-Review', effort: 'high' }).catch(() => null)

return { done: true, scoutOk: !!digest, review }

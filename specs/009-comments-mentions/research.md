# Research & Design Decisions: Comments & @Mentions (slice 009)

**Input**: `spec.md`, `.specify/memory/constitution.md` (v4.0.0 — esp. Principle IX Authorization, X Time, XI Privacy, XII Security), `.specify/memory/product-vision.md`, and the **already-implemented** slice-007 (project-sharing-membership) and slice-008 (task-assignment) substrate present in the tree.

This slice introduces the **first free-form user content** in the product — the `Comment` (ENT-08) — on tasks in **shared** projects. It **consumes** the slice-007 dispatch-by-visibility authorization policy (`ResolveEffectiveRole` / `RequireRole`) unchanged, layers an **object-level authorship grant** (FR-075) on top of the role gate for edit/delete, emits a `UserMentioned` domain event to the Wolverine outbox (consumed by slice 017), and **adds a table** (`comments` + a `comment_mentions` child table) — so, like slices 004/007/008, **FR-051 backup-before-migration is LIVE** (R12). The format mirrors slice 007: each decision is **Decision / Rationale / Alternatives considered**, cross-referenced from `plan.md`, `data-model.md`, and `contracts/openapi.yaml`.

**Reference identity for examples**: a shared project `P` with owner `A`, editors `E` and `E2`, viewer `V`; a **former** member `F` (left/removed/unshared) who previously authored a comment; a non-member admitted User `X`. Role tokens are lowercase `owner | editor | viewer`; visibility values `personal | shared`. `E` authors comment `c`.

**Authoritative-source note**: slice 007 **owns** the membership + role authorization branch and the `forbidden`(403)/`not_found`(404) posture; slice 009 reuses it verbatim and does **not** fork it. Where the SCOUT DIGEST (non-authoritative context) and the spec differ, the spec + constitution win.

---

## R1 — `Comment` (ENT-08) is its **OWN aggregate root** (`Comment : AggregateRoot<CommentId>`), NOT an owned child of `Task`

**Decision**: Model `Comment` as a **first-class aggregate root** — `sealed class Comment : AggregateRoot<CommentId>` — with a server-minted `CommentId` (a `readonly record struct CommentId(Guid Value)` exposing `New() => Guid.CreateVersion7()` + `From(Guid)`, EF-mapped `ValueGeneratedNever`, mirroring `ProjectMembershipId`). Each comment persists to its own `comments` row (R12) carrying `author_id`, `task_id`, `body`, `created_at`, `edited_at`, and owns a small child collection of typed @mention tokens in a `comment_mentions` table (R6). The comment references its parent `Task` and its `author` **by id only** (no EF navigation collection on `Task` — the established no-nav-prop persistence style, slice-007 R1). The `Task` aggregate is **not modified** by this slice (spec:36).

**Rationale — independent lifecycle is the discriminator.** Slice 008 rejected a standalone `TaskAssignee` aggregate because "assignment has no lifecycle independent of its task" (008 R1) — assignees are folded into `Task` under `Task.Version`. Slice 007 modeled `ProjectMembership` as an entity **owned by the `Project` aggregate**, guarded by `Project.Version` (007 R1). A **comment is the opposite case**: it has its **own** creation/edit lifecycle, its **own** author grant (FR-073/075), its **own** edit/delete surface, its **own** @mention set, and emits its **own** domain event. Its consistency boundary is the single comment (body + mention set change atomically), not the task and not the whole thread. Folding hundreds of comments into the `Task` aggregate would force every comment write to load and re-version the entire task (one-aggregate-per-transaction, ADR-0003) and serialize unrelated comments behind `Task.Version` — the wrong boundary. So `Comment` is its own aggregate.

**Payoff to make explicit** (the concrete consequence that makes this boundary real): comment create/edit/delete **do not touch `Task.Version` or `Project.Version`** — the parent `Task` and its `Project` are loaded **read-only, for authorization only** (R3). Contrast slice 007 (`Project.Version` guards the membership set) and slice 008 (`Task.Version` guards the assignee set). A comment write concurrent with an unrelated task edit does **not** conflict.

**"Own aggregate" ≠ self-authorizing.** The aggregate boundary is about **transaction / consistency / lifecycle**, not authorization independence. Every comment read and write still routes authorization through the **parent**: load `Task` → resolve its `Project.Visibility` → membership set → role (R3). The `Comment` aggregate carries no membership or visibility of its own.

**Alternatives considered**: (a) `OwnsMany`/child collection of comments on `Task` (the slice-008 assignees shape) — rejected; comments have independent lifecycle + their own event + author grant, and coupling them to `Task.Version` would make every comment write bump the task and serialize the thread behind one token. (b) A comment aggregate rooted in a separate bounded context — rejected (over-engineering for a ~10-user team; the comment references `Task`/`User`/`ProjectMembership` by id and authorizes through the existing Task-Management guards, exactly like assignment).

---

## R2 — Comments are **VERSIONLESS (last-write-wins)** — no per-comment concurrency token, `version_conflict`(409) is **not used**

**Decision**: A `Comment` carries **no** optimistic-concurrency `version` column. `EditComment` and `DeleteComment` are **last-write-wins**: the edit overwrites `body` + the mention set and stamps `edited_at`; a delete **soft-deletes** the row (stamps `deleted_at`; the row is hard-purged by a scheduled reaper after the 30s undo window — R5). `AggregateRoot<TId>` mandates no version field (verified — the base holds only `Id` + the domain-event list), so a versionless aggregate is structurally clean. This slice therefore **does not use `version_conflict`(409)** at all (R9) — and, unlike the task reaper (which leans on `Task.Version` for its interleaved restore-race backstop), the comment reaper has **no** version token, so the `deleted_at`-instant equality is its sole restore-aware/idempotent guard (R5).

**Rationale**: Constitution Principle III (spec:127) is explicit that the thread reconciles under **last-write-wins** and that a posted/edited/deleted comment paints optimistically within one animation frame — the same posture as slice-006 labels ("VERSIONLESS, no 409"). The safety discriminator: **no comment edit has a multi-field invariant that a stale overwrite could corrupt** — `body` and the mention set are independent, edit is **author-only** (FR-075), so the only possible conflict is the same author editing `c` from two tabs, where clobbering to the latest write is the correct, expected outcome. There is no cross-field or cross-entity invariant a version guard would protect, so a token would add a 409 failure mode and a contract field for no behavioral gain.

**Alternatives considered**: (a) A per-comment `version` + `version_conflict`(409) (the slice-007/008 posture) — rejected; it contradicts the Principle III LWW mandate for the thread, over-builds for an author-only edit surface with negligible contention, and adds a code (409) the failure taxonomy otherwise never needs. (b) Guard comment writes with the parent `Task.Version` — rejected; that recouples the comment to the task aggregate (R1) and would 409 a comment edit behind an unrelated concurrent task change.

---

## R3 — Authorization **dispatched by the parent project's visibility** — reuse the slice-007 policy unchanged; comments exist ONLY on shared-project tasks

**Decision**: Every comment read and write authorizes by loading the **parent `Task`**, resolving its **`Project.Visibility`**, and dispatching onto the slice-007 branch — **no fork, no new policy** (FR-065/068). The gate mirrors `TaskAccessGuards.LoadWritableTaskAsync` (slice 005/008) exactly, with the personal branch **denied** because comments have no surface on personal tasks:

1. Load the parent `Task` by id via `ITaskRepository`; null → **404 `not_found`**.
2. If `task.ProjectId is null` (Inbox / personal) → comments **do not exist there** → **404** for read AND write (personal projects have no membership set; FR-072). This inverts the ownership-allow of the generic write guard's personal branch — comments are shared-only, exactly as `SetTaskAssignees` 404s the null-`ProjectId` case (008).
3. `projects.FindReadableAsync(projectId, caller)` → foreign/absent OR shared-non-member → null → **404**. Then require `project.Visibility == Shared` else **404** (comments only on shared tasks).
4. `members.ListByProjectAsync(projectId)` then `authorization.RequireRole(project, memberships, required)` (the caller is resolved internally from `ICurrentUser` — the 3-arg deny-path form; `ResolveEffectiveRole(project, memberships, caller)` is the pure, explicit-caller resolver):
   - **Read the thread / list comments** → `EffectiveRole.Viewer` (any current member; non-member → 404). Precedent: `GetProjectTasks` shared arm requires `Viewer`.
   - **Post a comment** (and the **role floor** for edit/delete) → `EffectiveRole.Editor` (viewer → **403 `forbidden`**, non-member → **404**). Precedent: `SetTaskAssignees` requires `Editor`.

The caller is **always** `ICurrentUser.Id`, never the wire (slice-004 posture). `owner` is derived from `Project.OwnerId` (no row), `editor`/`viewer` from the membership row, non-member = none (slice-007 `ResolveEffectiveRole`). This is the FR-066/067/068 branch realized for the comment surface; the 404-vs-403 split is the slice-007 leak boundary (non-member → 404 no existence leak; insufficient-role member → 403 honest "you lack the role").

**Rationale**: Constitution IX / FR-065 mandate dispatch **by the containing resource's visibility**, not a Tier A/B conjunction — and comments **live only on shared-project tasks** (spec:132), so the personal branch is simply "no comment surface → 404." Reusing `RequireRole` keeps authorization in one place (the SC-016 matrix is verifiable against a single policy) and inherits the slice-007 deny shapes with zero new code.

**Alternatives considered**: (a) A comment-specific authorization helper — rejected; duplicates `RequireRole` and forks the deny matrix. (b) Allow comments on personal tasks (author-only) — rejected; contradicts FR-072 ("shared projects" only) and would invent a second, unspecified authorization mode.

---

## R4 — Authorship is a **distinct object-level grant** for edit/delete: **`RequireRole(Editor)` FIRST, then author-equality** — FR-066 (membership loss) structurally beats FR-075 (author grant)

**Decision**: `EditComment` and `DeleteComment` run a **strictly ordered** two-step gate:

1. **Role floor first** — the full R3 dispatch, requiring `EffectiveRole.Editor` on the parent project (viewer → 403, non-member → 404, personal/foreign → 404).
2. **Author-equality second** — only after the floor passes: assert `comment.AuthorId == caller` else **403 `forbidden`**. Project role — **including the project owner `A`** — does **not** override authorship (FR-075): a non-author owner/editor is denied.

This single ordering yields **every** required outcome, so the membership-loss override (FR-066 > FR-075) is **structural, not a special case**:

| Actor on comment `c` (authored by `E`) | Step 1 `RequireRole(Editor)` | Step 2 `author == caller` | Result |
|---|---|---|---|
| `E` (author, current editor/owner) | pass | pass | **allow** |
| `A`/`E2` (non-author owner/editor) | pass | fail | **403** (role does not override authorship) |
| `V` (viewer) — even if author via later demotion | **fail (403)** | not reached | **403** (member, insufficient role) |
| `F` (former member, incl. former author of `c`) | **fail (404)** | not reached | **404** (membership loss revokes ALL; FR-066) |
| `X` (non-member) | **fail (404)** | not reached | **404** |

**Rationale — resolving the FR-075 vs edge-case collision.** spec:64 forbids a viewer from editing/deleting; FR-075 says only *membership loss* overrides the author grant (a role **demotion** to viewer is not membership loss). These collide for a demoted former-editor who authored `c`. The resolution: the **Editor role floor is ANDed and runs first**. FR-075's "role does not override authorship" means a *higher* role cannot edit *someone else's* comment (no privilege escalation) — it does **not** waive the role floor for the author. Because membership is resolved **live** in step 1, a former member `F` is 404'd **before** the author check ever runs — so FR-066's revoke-ALL wins over FR-075's author grant **for free**, with no explicit "was this a former author" branch. `createdBy`/authorship confers **no standalone access** (FR-066): `F` can neither view (R3 read gate), edit, nor delete `c`.

The "viewer edit/delete → 403" and "former-author → 404" rows are the two the deny matrix (SC-016) and a reviewer will check; both fall out of the fixed order without special-casing.

**Alternatives considered**: (a) Author-equality first, role floor second — rejected; a former author would pass author-equality and only then fail the (still-required) membership check, but ordering the membership check first is clearer and makes the FR-066>FR-075 precedence a structural property, not an accident. (b) Let the project owner edit/delete any comment (moderation) — rejected; flatly contradicts FR-075 ("project role including owner MUST NOT override"). Moderation is not in scope (no OOS promotion for it).

---

## R5 — Command + read surface: `PostComment` / `EditComment` / `DeleteComment` + a `ListTaskComments` thread read

**Decision**: Four operations, agreeing field-for-field with `contracts/openapi.yaml` + `TaskFlowDocumentTransformer` operationIds:

- **`GET /api/tasks/{taskId}/comments`** — `listTaskComments`. Returns the thread (chronological). Role: **viewer+** (R3). Denies: 404 (non-member/personal/foreign task).
- **`POST /api/tasks/{taskId}/comments`** — `postTaskComment`. Body `{ body: string, mentionedUserIds: string[] }`. Server mints `CommentId` (server-minted, like membership rows; the id is **not** client-supplied — contrast client-minted `TaskId`). Role: **editor+**. Denies: 403 (viewer), 404 (non-member/personal/foreign), 422 (empty/whitespace/over-length body, or a mention id that is not a current member — R6).
- **`PATCH /api/comments/{commentId}`** — `editComment`. Body `{ body, mentionedUserIds }` (whole-body replace + whole mention-set replace, the anti-silent-null discipline). Keyed by `commentId` (its own aggregate); the handler loads the comment (null → 404), then its parent task/project for the R3+R4 gate. Role floor: editor+; then author-only. Denies: 403 (viewer / non-author), 404 (non-member/former member/foreign/comment-on-now-personal-task), 422 (empty/over-length/non-member-mention).
- **`DELETE /api/comments/{commentId}`** — `deleteComment`. **Soft-deletes** the row (stamps `deleted_at`) and publishes a **scheduled `ReapDeletedComment` (30s-delayed)** to the outbox; the comment leaves the thread immediately (the thread read filters `deleted_at IS NULL`) while the row lingers for the Constitution-VII 30s undo window, then the reaper hard-purges it. **Version-free / idempotent**: a tombstone-inclusive load distinguishes a foreign/absent id (→ **404**) from the caller's **own already-soft-deleted** comment (→ idempotent **204** no-op, no second reaper scheduled). Role floor: editor+; then author-only (the gate is **unchanged** — only hard→soft changes). Denies: 403 (viewer / non-author), 404 (non-member/former member/foreign).

`edited_at` is set on `EditComment` (null until first edit) to drive an "edited" affordance (FR-073 timestamp). No `version` field on any request/response (R2).

**Soft-delete + scheduled reaper (the task-delete mirror; Constitution VII 30s-undo MUST).** A comment is task/project **data**, so its delete is covered by the Constitution-VII rule that "delete … MUST be undoable for a minimum of 30 seconds after execution … performed only by the original actor" — the only carve-out is membership/role changes, which a comment is not. `DeleteComment` therefore follows the **identical posture** to `DeleteTask`/`ReapDeletedTask` (do **not** invent a new mechanism):
- `Comment.SoftDelete(deletedAt)` stamps `deleted_at` (versionless — **no** version bump, unlike `Task.SoftDelete`), and the command publishes `new ReapDeletedComment(commentId, deletedAt)` with `DeliveryOptions { ScheduleDelay = 30s }` to the outbox, committing publish + `deleted_at` write in the same per-message transaction.
- `ReapDeletedCommentHandler` — a deferred reaper off a durable **`comment-reaper`** local queue (routed in `Program.cs`), with **no caller** (excluded from the deny-by-default authz predicate alongside `ReapDeletedTask`/`AccountDeletionRequested`, since a queue message has no `HttpContext`/`ICurrentUser`). It loads by raw id tombstone-inclusive and **hard-purges ONLY if** the row still exists **AND** `deleted_at` is non-null **AND** `deleted_at == the scheduled instant` (compared at Postgres µs resolution) — restore-aware and idempotent for the common case. Because comments are versionless, there is **no** `Task.Version`-style 0-rows-DELETE concurrency backstop: the task reaper's restore-awareness is **two layers** (the load-time `deleted_at`-instant check **and** a commit-time `VersionConflictException` backstop for a restore that interleaves *between* the reaper's load and its commit), but the comment reaper has **only** the load-time check — so covering a slice-014 restore that clears `deleted_at` in that narrow load↔commit window is a **slice-014 seam concern** (it would add the concurrency guard when it builds restore); the `deleted_at`-instant match is the sole guard shipped here (R2).
- **The user-facing restore/undo is the slice-014 seam — NAMED, not built here.** Exactly as task delete ships only the soft-delete + scheduled reaper substrate (its own doc: "a slice-014 restore CLEARS `deleted_at`"), this slice builds **no** comment restore/undo command or endpoint (that would exceed the task-delete scope). A slice-014 restore that clears `deleted_at` (or a re-delete that re-stamps a new instant) makes the scheduled reaper no-op. No client-side undo toast is added either — the task-delete web mutation (`useTaskMutations`) has none, so comment delete matches its scope exactly (optimistic-remove-then-reconcile, R14).

**Rationale**: `taskId`-scoped POST/list matches the thread-on-a-task mental model and the R3 gate (which must load the parent task anyway). `commentId`-scoped edit/delete matches the comment-is-its-own-aggregate boundary (R1); the handler still resolves the parent for authorization (R3/R4). Whole-body + whole-mention-set replace on edit mirrors slice-004/005/008 "required key, never a silent null."

**Alternatives considered**: (a) `PATCH/DELETE /api/tasks/{taskId}/comments/{commentId}` (nested) — acceptable but redundant; the comment id is globally unique (UUIDv7) and its own aggregate, so a flat `/api/comments/{id}` is the honest shape. (b) **Hard-delete the row on author-initiated delete** — **rejected** (this reverses the earlier draft): a comment is task/project data, so Constitution VII's 30s-undo MUST covers it, and a bare hard-delete leaves **no** undo window (violation). **Soft-delete is ADOPTED** — and it does **not** contradict AS-04 ("the thread updates"): the comment leaves the view **immediately** on soft-delete (the thread filters `deleted_at IS NULL`), exactly like task delete, while the row lingers 30s for the Constitution-VII undo window; the `ReapDeletedComment` reaper hard-purges on expiry, restore-aware. Author **account** deletion is the separate, distinct tombstone case (R11 — `author_id → NULL`, `ON DELETE SET NULL`, the comment **survives** where it anchors a thread) and is **not** the same as this delete soft-delete/reaper — the two do not interact.

---

## R6 — @mention: a **typed User-id token**, candidacy = **current members of the parent project**, persisted as a `comment_mentions` child collection

**Decision**: An @mention is **structured data, never free text** (FR-098). The command carries `mentionedUserIds: string[]`; the server persists them as a child collection `comment_mentions(comment_id, user_id)` owned by the `Comment` aggregate (composite PK `(comment_id, user_id)` — a user is mentioned at most once per comment). Validation, server-side, against the **current** member set of the comment's parent project (owner `A` ∪ membership rows, via the R3-loaded roster):

- A mention id that is **not a current member** (non-member `X`, or a former member) → **422 `validation_failed`**, field-level message on `mentionedUserIds` ("only current project members can be mentioned"). The client @mention picker only offers current members, so this is the defense-in-depth backstop (FR-066/073 — a non-member "MUST NOT be mentionable").
- The set is **de-duplicated**; a **self-mention** (author mentions themself) is permitted but emits **no** event (R7) — you are not notified of your own mention.

The rendered @mention is produced from the **typed token** (resolve id → displayName at render, like the members roster), **not** parsed out of the body text — so body sanitization (R8) never has to interpret `@` syntax for security, and a literal "@" in prose is inert.

**Rationale**: Storing mentions as typed User-id tokens (not scraped from text) makes candidacy enforceable at the API layer (FR-066), makes the residual-attribution / erasure rule apply uniformly (R11), and decouples mention semantics from body sanitization. Rejecting a non-member mention with 422 (rather than silently dropping) is honest + testable (a first-class deny-matrix row, SC-016).

**Alternatives considered**: (a) Parse `@name` from the body server-side and resolve to ids — rejected; couples mention semantics to free-text parsing (fragile, ambiguous on duplicate display names) and muddies the sanitization boundary. (b) Silently drop non-member mention ids — rejected; a 422 is the honest, testable contract and matches the spec's "MUST NOT be mentionable."

---

## R7 — `UserMentioned` domain event raised to the Wolverine outbox — **delta-on-edit (added only)**; consumed by slice 017 (a named seam)

**Decision**: Posting or editing a comment raises **`UserMentioned`** through the transactional outbox, mirroring `TaskAssigned` exactly: a `sealed record UserMentioned : DomainEvent` carrying **pure ids only** — `CommentId`, `TaskId`, `ProjectId`, the collection of **newly-added** mentioned `UserId`s, and the actor/author `UserId` (no names/PII). Semantics:

- **On post** — emit for the full mention set (minus any self-mention, R6).
- **On edit** — emit only for mentions **newly added** relative to the prior set (the delta). **Removing** a mention emits nothing (no "un-notify"); re-saving an unchanged set emits nothing (idempotent — FR-074 is "notify the mentioned member," not "re-notify on every edit").

The event is published via `DomainEventDispatch.PublishAndClearAsync` **after** the aggregate records it and **before** `SaveChangesAsync` (the `SetTaskAssignees` sequence), so the publish enrolls in the same per-message transaction. Because Wolverine routes by runtime type and an **unrouted publish is silently dropped**, this slice registers a **no-op routable handler** `static Handle(UserMentioned _){}` (the `TaskAssignedHandler` precedent) plus the `PublishMessage<UserMentioned>().ToLocalQueue(...)` route and the `AuthorizationMiddleware` off-queue exclusion (queue messages have no `HttpContext`). **Slice 017 (notifications)** replaces the no-op with the real consumer that creates ENT-09 Notifications — a **named seam**, built there, not here.

**Rationale**: This is the slice-007/008 event posture: **raise + register a routable no-op now** so the publish is observable (`.Sent.MessagesOf<UserMentioned>()`) and the downstream slice attaches additively (a handler subscribes; no change to slice-009 commands). Pure-id payload honors Constitution XI (no PII on the wire). Delta-on-edit prevents notification spam on unrelated edits while still notifying a member added in a later edit (AS-02).

**Alternatives considered**: (a) Re-emit the whole mention set on every edit — rejected; would re-notify unchanged mentions on an unrelated body fix (notification spam). (b) Defer raising `UserMentioned` until slice 017 exists — rejected; the event is the mention-authority contract slice 017 was planned against, and raising it now keeps 017 additive. (c) One event per mention vs one event carrying the added-collection — chose the **collection** shape (mirrors `TaskAssigned`'s added/removed), one publish per command.

---

## R8 — FR-098 comment safety + FR-099: length/non-empty at the **command boundary** (422); **sanitize to a safe subset at the render boundary** (this slice's ONE new web dependency); CSP + headers reused

**Decision — two boundaries, split by concern:**

- **Input validation (server-authoritative, FR-098 length + empty)** — FluentValidation at the command boundary (the `InviteMemberValidator` / `SetTaskAssigneesValidator` precedent): `body` **required, not whitespace-only** (trim → non-empty) and **≤ a `MaxCommentLength` const** (a single named constant, e.g. 4000 chars — the exact bound set in data-model.md). A violation → **422 `validation_failed`** with a field message on `body` (FR-049 recoverable). An empty/whitespace-only body **MUST NOT create a thread entry** (rejected before persistence). The **stored** `body` is the author's raw text, unmodified, so edit round-trips faithfully and the stored form is renderer-agnostic.

- **Output sanitization (FR-098 "constrained safe subset (plain text + safe markdown)", FR-099 no raw HTML injection)** — realized at the **web render boundary**. Because comments are the **first free-form content surface** in the product and a stored-XSS target (Principle XII), rendering the "safe subset" requires a **vetted markdown renderer + HTML sanitizer** — this is a **conscious departure** from the slice-007 "no new web deps" posture and is called out in `plan.md`'s Technical Context (Deps). Constraints the chosen library set must satisfy (recorded here; exact package pinned at implementation): markdown → HTML with a **strict allowlist** (basic inline/block formatting only — no raw HTML passthrough, no `<script>`/`<style>`/event handlers), URL schemes restricted to safe protocols (no `javascript:`), and @mentions rendered from the **typed token** (R6), not parsed from prose. Sanitization runs on **render**, so it also covers historical rows. React's default text-escaping remains the backstop for any plain-text path.

- **CSP + security headers (FR-099) already exist — reused, not built.** The API `SecurityHeadersMiddleware` (all environments) and the user-facing CSP in `apps/web/next.config.ts` are the slice-007 substrate; this slice **adds no header infrastructure**. The web CSP (no inline script) is the defense-in-depth layer behind the DOM sanitizer. Existing seams `apps/web/tests/unit/security-headers.test.ts` + `SecurityHeadersTests.cs` are extended, not replaced.

**Rationale**: Splitting validation (server, authoritative, → 422) from sanitization (render, → safe DOM) puts each control where it belongs: the server is the system of record for *acceptability* (length/non-empty), and the *render* boundary is where untrusted text becomes DOM and XSS is possible. Storing raw text (not pre-sanitized HTML) keeps edit faithful and lets a later renderer upgrade re-sanitize old rows. Honoring FR-098's literal "safe markdown" requires a renderer; naming it as the slice's one new dependency (with the allowlist constraints) is the honest call rather than silently degrading to plain-text-only.

**Alternatives considered**: (a) **Plain-text-only render, zero new dep** (React auto-escaping) — defensible and provably XSS-safe, but under-delivers FR-098's explicit "safe markdown"; rejected in favor of the vetted-renderer path, with plain-text escaping kept as the backstop. (b) Sanitize-on-write into stored HTML — rejected; corrupts edit round-tripping and freezes the sanitization policy at write time (can't re-tighten the allowlist for existing rows). (c) Trust the client to sanitize — rejected; the server/render boundary must assume the body is hostile (Principle XII).

---

## R9 — Error contract: **NO new error code** — reuse `validation_failed` / `forbidden` / `not_found`; `version_conflict` unused

**Decision**: This slice adds **no** `ErrorCode`. Every failure maps onto an existing code (the slice-006/007/008 R16 posture — the `TaskFlowDocumentTransformer` `ErrorCodes` array and the web `ERROR_UX satisfies Record<ErrorCode, ErrorUx>` map stay **unchanged**, so the exhaustiveness gate stays green with no edit):

- **`validation_failed` (422)** — empty/whitespace-only body, over-length body, a mention id that is not a current member (R6/R8).
- **`forbidden` (403)** — a viewer attempting to post/edit/delete (insufficient role), and a **non-author** (any role, including owner) attempting to edit/delete (R4).
- **`not_found` (404)** — non-member / former member / foreign task or comment / comment on a now-personal (unshared) task / personal-project task (existence not disclosed across the membership boundary — R3/R4).
- **`version_conflict` (409)** — **NOT used** this slice (comments are versionless, R2). One fewer reused code than slice 007.

New `SetOperation` lines register the per-operation extra responses: `listTaskComments` → 404; `postTaskComment` → 403, 404, 422; `editComment` → 403, 404, 422; `deleteComment` → 403, 404. The human specifics (which field, which role) ride in the ProblemDetails `errors` envelope (RFC 9457, ADR-0009).

**Rationale**: Every comment failure shape already has a code; a new code (`insufficient_role`, `content_too_long`, `not_mentionable`) would cost a two-place protocol edit (transformer + `ERROR_UX`) for a distinction the field-error text already carries. The `forbidden` copy may want a comment-specific refinement for the non-author case ("only the author can edit this comment") — that is **copy, not a new code** (FR-049 actionability).

**Alternatives considered**: A dedicated code for the non-author-edit case or the non-member-mention case — rejected (above); `forbidden` + `validation_failed` field messages carry the distinction the client needs.

---

## R10 — Time: `created_at` / `edited_at` UTC `timestamptz`; relative thread display against **`Europe/Warsaw`** via the existing web util (no new date library)

**Decision**: `comments.created_at` and `comments.edited_at` (nullable until first edit) are `.NET DateTime` **UTC** persisted as PostgreSQL **`timestamptz`** (the `ProjectMembership`/`Task` timestamp posture). The write path stamps `utcNow` from the injected `TimeProvider`/clock (the integration-test frozen-clock seam). Relative-time display in the thread ("2h ago", "edited") is computed on the **web** against the single instance reference zone **`Europe/Warsaw`** using the **existing** `date-fns` v4 + `date-fns-tz` v3 stack and `apps/web/src/lib/timezone.ts` (`REFERENCE_TIME_ZONE`, `formatInReferenceZone`) — **no new date library**, and **no** server-side Warsaw conversion needed for comments (`WarsawDayBounds` is a day-bucketing concern, not a relative-time one). Per-user timezones are OOS (OOS-19 / ASM-12).

**Rationale**: Principle X (spec:133) requires UTC storage + Warsaw-referenced relative display with DST handled by the library — `date-fns-tz` already does DST. The repo does **not** use NodaTime (slice-007); introducing another date library would violate the "follow the established posture" rule.

**Alternatives considered**: Absolute timestamps only (no "2h ago") — rejected; AS-01 wants the thread to show author + timestamp legibly, and the relative-time util already exists. A new relative-time dependency — rejected; `date-fns` covers `formatDistanceToNow`.

---

## R11 — Privacy: `author_id` is **anonymizable to a tombstone** — FK `ON DELETE SET NULL`, **NOT cascade**; @mention token subject to the same residual rule

**Decision**: The `Comment` is modeled so the author reference is **nullable / reassignable to a tombstone** for the slice-015 erasure cascade (FR-085, Constitution XI) — a comment that anchors a thread is **anonymized, not hard-deleted** (spec:134). This **splits** the FK posture from slices 007/008 (which cascade-delete membership/assignee rows on user erasure):

- `comments.task_id → tasks(id)` **`ON DELETE CASCADE`** — a comment is meaningless without its task (deleting/reaping a task removes its thread).
- `comments.author_id → users(id)` **`ON DELETE SET NULL`** (nullable column) — on account erasure the author becomes a **tombstone** (`author_id = NULL`, rendered as a neutral "deleted user" identity); the comment **survives** where it anchors a thread. A blanket cascade here would **violate** FR-085 by hard-deleting the comment.
- `comment_mentions.user_id → users(id)` **`ON DELETE SET NULL`** (or row-removal) — the @mention token is a User-id reference subject to the **same residual-attribution rule**: an erased mentioned user is anonymized, not leaked, and never cascade-deletes the comment.

This slice **models** for anonymization (nullable author, tombstone-renderable read model) but **does not build** the slice-015 erasure cascade — a named seam. Uniqueness/scoping: `comment_mentions` composite PK `(comment_id, user_id)`; no unique constraint references `author_id` (author is provenance, not identity of the row).

**Retention stance (Constitution XI — stated explicitly, silence is not acceptable):** comments are **retained until the author's account erasure**, at which point the author reference is **tombstoned** (nullable, `ON DELETE SET NULL`) and the comment **survives** where it anchors a thread; a **soft-deleted** comment within the **30s undo window** is **purged** by the `ReapDeletedComment` reaper on expiry (R5). This covers both the erasure-tombstone path and the undo-window path.

**Rationale**: This is the one FK the house-style "ON DELETE CASCADE for erasure parity" note gets **wrong** for comments: Principle XI explicitly requires the author to be tombstoned "not hard-deleted where they anchor a thread." `SET NULL` (author survives as tombstone) is the correct erasure semantics; `CASCADE` would destroy thread history. The mention token follows the same residual rule.

**Alternatives considered**: (a) `author_id ON DELETE CASCADE` (parity with `task_assignees.user_id`) — **rejected**; hard-deletes comments on erasure, violating FR-085. (b) `RESTRICT` + an app-level tombstone-reassignment in the slice-015 cascade — acceptable and equivalent in intent; `SET NULL` is chosen as the simpler DB-level default with the read model rendering `null` author as the tombstone identity (data-model.md fixes the exact mechanism). (c) Hard-delete the author's comments on erasure — rejected; loses thread coherence and contradicts spec:134.

---

## R12 — Migration `AddComments`: **FR-051 LIVE** — new `comments` + `comment_mentions` tables (mirror slice-007 `AddProjectMemberships`)

**Decision**: One EF Core migration, `AddComments`, creating:

- **`comments`** — `id uuid` PK (UUIDv7, `ValueGeneratedNever`), `task_id uuid NOT NULL` FK → `tasks(id)` **`ON DELETE CASCADE`**, `author_id uuid NULL` FK → `users(id)` **`ON DELETE SET NULL`** (R11), `body text NOT NULL` (length enforced at the app boundary, R8; an optional DB CHECK on max length is a backstop), `created_at timestamptz NOT NULL`, `edited_at timestamptz NULL`. Indexes: **`(task_id, created_at)`** (the thread read, chronological — R5) and **`(author_id)`** (the erasure cascade + "my comments").
- **`comment_mentions`** — `comment_id uuid NOT NULL` FK → `comments(id)` **`ON DELETE CASCADE`** (mentions are owned by the comment), `user_id uuid NOT NULL` FK → `users(id)` **`ON DELETE SET NULL`** (R11), composite PK **`(comment_id, user_id)`** (a member mentioned at most once per comment). Index `(user_id)` for the mention-authority / erasure query.

No DDL beyond these two tables; **no change** to `tasks` / `projects` / `project_memberships`. Because a table ships, **FR-051 backup-before-migration is LIVE** (the slice-004/007/008 posture, NOT the slice-003/005 no-op): the CI `scripts/backup.sh → migrate → scripts/restore-test.sh` gate (Constitution VII) must actually run against `AddComments`.

**Rationale**: A pure additive two-table migration (forward-only, no rewrite of existing rows). FK directions encode ownership + erasure: `task_id`/`comment_id` cascade (structural children), `author_id`/`mention.user_id` `SET NULL` (privacy tombstone, R11). The `(task_id, created_at)` composite covers the thread-list query without a separate `(task_id)` index. This is a genuine schema change → FR-051 flips LIVE, no way around it, and the gate already exists.

**Alternatives considered**: (a) Store mentions as a `uuid[]` column on `comments` — rejected; can't FK-enforce member candidacy, can't `SET NULL` a single erased mentioned user, and complicates the mention-authority query (the slice-005 value-converted-array lesson). A relational child table is correct. (b) Fold `comment_mentions` into `comments` as JSON — rejected; same referential-integrity and erasure-cascade loss.

---

## R13 — Real-time is a **named seam** (slice 016): optimistic paint now, live remote propagation later — not built here

**Decision**: A posted/edited/deleted comment **paints optimistically within one animation frame** and the server reconciles under **last-write-wins** (R2), matching Constitution III (spec:127). The **web** implements this now (R14). But **live propagation of a *remote* comment into an open thread** — another member's post appearing without a refetch, and re-sync of a former member — is **slice 016 (real-time-collaboration)**: this slice builds **no** SignalR hub, no live transport, no subscription eviction. The reconciliation rule this slice records for 016 to honor: an inbound remote comment **MUST NOT clobber a pending local optimistic edit** (last-write-wins on the server, but the open composer's unsent draft is preserved). Slice 016's live re-authorization (evicting a removed member's thread subscription) reads the **membership authority** slice 007 owns; this slice's **handler-level** R3/R4 checks already deny a former member at the API layer regardless of any live transport.

**Rationale**: Mirrors the slice-007/004 discipline of naming a forward seam without pulling its machinery forward. The optimistic-paint half is realizable now (client-side, no transport); the live-propagation half needs the slice-016 transport that does not yet exist. Naming the reconciliation contract (don't clobber a pending local edit) is sufficient for 016 to attach without changing slice 009.

**Alternatives considered**: Build a minimal live push now — rejected; the real-time transport is slice 016's owned scope and there is no hub in the app to extend.

---

## R14 — Web: **optimistic** comment mutations (the `useTaskMutations` recipe) + a lazy thread read hook; composer suppresses single-key shortcuts; ARIA-live + dialog focus

**Decision**:
- **Read** — a `useComments` query keyed **`['tasks', taskId, 'comments']`**, `enabled` when the thread panel is open (the lazy-fetch shape of slice-007 `useProjectMembers`), surfacing `mapError`/ProblemDetails on failure (FR-049).
- **Mutations** — `usePostComment` / `useEditComment` / `useDeleteComment` follow the **optimistic + rollback** recipe (`onMutate` snapshot / apply / `onError` rollback / invalidate on settle) of `useTaskMutations` — **not** the non-optimistic `useMembershipMutations` shape — because Principle III (spec:127) wants comments to paint within one frame. Delete is optimistic-remove-then-reconcile.
- **Surface / mounting** — the comment thread is a **NEW** surface; there is no task-detail/drawer component today (only `TaskCapture.tsx` + `TaskRow.tsx`). The plan names it as a **task detail panel** where the thread + composer mount; slice 016 later wires live inbound comments into it.
- **Accessibility** — the composer input suppresses single-key shortcuts while focused (FR-031, `useGlobalShortcuts` seam); the @mention picker + delete-confirmation dialog follow the dialog focus contract (FR-101 — initial focus, focus trap, Esc-dismiss, return focus); server-initiated updates/toasts announce via the existing ARIA-live region without stealing focus (FR-101/FR-043/FR-046 — no hover-only affordances). The viewer sees the thread **read-only with no composer** (AS-03) — the composer is gated on the caller's effective `role` from the read model (server remains authoritative, FR-068).
- **Types** — regenerate `apps/web/src/lib/api/generated/schema.d.ts` via `gen:api` (openapi-typescript against the API on :4311) after the comment ops land in `contracts/openapi.yaml` + the transformer.

**Rationale**: Comments are the one mutation family the spec explicitly routes to the **optimistic** recipe (unlike membership, which slice-007 deliberately made non-optimistic/confirm-gated). Gating the composer on the read-model role (R15) is UI convenience; the server R3 gate is the real boundary.

**Alternatives considered**: Non-optimistic (invalidate-on-settle) comments — rejected; contradicts Principle III's one-frame mandate for comments. A dedicated new date/markdown stack — see R8/R10 (markdown renderer is the one new dep; date library reused).

---

## R15 — Read model: `CommentResponse` (author identity + tombstone-safe, mention tokens, `edited_at`, `canEdit`); thread is `viewer+`

**Decision**: `listTaskComments` returns an ordered list of `CommentResponse`:
`{ id, taskId, authorId (nullable), authorDisplayName (tombstone-safe — "Deleted user" when authorId is null, R11), body, mentions: [{ userId, displayName }], createdAt, editedAt (nullable), canEdit (bool) }`.

- `canEdit` = `authorId == caller` — the server-computed authorship signal that gates the client edit/delete affordances (the real check is R4 server-side; this is UI convenience, not the boundary).
- `mentions` renders from the **typed tokens** (R6), resolving id → displayName at read; an erased mentioned user renders tombstone-safe.
- Emails are **not** echoed (Constitution XI — the members roster already withholds emails, slice-007 R17).
- The thread read is **viewer+** (R3): a viewer receives the full thread but the client renders **no composer** (AS-03); a non-member/former member receives **404**, not an empty thread (no existence leak, R3).

**Rationale**: A member needs author identity + relative timestamp (AS-01), the mention rendering (AS-02), and a cheap `canEdit` to gate affordances (AS-04) — without exposing emails or `authorId` of erased users as raw identity. Tombstone-safe author/mention rendering is the read-side realization of the R11 privacy model.

**Alternatives considered**: (a) Expose author email on the thread — rejected (Principle XI; unnecessary PII). (b) Omit `canEdit` and have the client compare `authorId == caller` itself — acceptable; `canEdit` is a small server-computed convenience that keeps the authorship rule (R4) in one conceptual place and survives future role nuances.

---

## Resolved unknowns summary

| Plan unknown | Resolved by |
|---|---|
| `Comment` aggregate boundary: own root vs owned-on-Task (vs slice-007 membership / slice-008 assignees) | R1 |
| Concurrency: versioned (409) vs versionless LWW | R2 |
| Authorization dispatch-by-visibility; read=viewer+/post=editor+; non-member 404 / viewer 403; personal→no comments | R3 |
| Authorship object-level grant + strict ordering (RequireRole THEN author; FR-066 > FR-075); viewer-edit / former-author rows | R4 |
| Command + read surface (post/edit/delete/list); endpoints + operationIds; **delete = soft-delete + scheduled `ReapDeletedComment` reaper** (Constitution VII 30s-undo; slice-014 restore seam) | R5 |
| @mention typed token; candidacy = current members; child-table persistence; non-member → 422 | R6 |
| `UserMentioned` outbox event; delta-on-edit (added only); slice-017 seam | R7 |
| FR-098 length/empty (422, command boundary) + sanitize-to-safe-subset (render boundary, one new web dep); FR-099 CSP/headers reused | R8 |
| Error contract: no new code; `version_conflict` unused | R9 |
| Time: UTC `timestamptz` + Warsaw relative display via existing `date-fns` util | R10 |
| Privacy: author tombstone → FK `ON DELETE SET NULL` (NOT cascade); mention residual rule | R11 |
| Migration `AddComments`; FR-051 LIVE; two tables + split FK posture | R12 |
| Real-time seam (slice 016): optimistic now, live propagation later | R13 |
| Web: optimistic mutation recipe + lazy thread read; composer/ARIA/dialog; mounting surface | R14 |
| Read model `CommentResponse`; tombstone-safe author/mentions; `canEdit`; viewer+ | R15 |

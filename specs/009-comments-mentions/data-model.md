# Data Model: Comments & @Mentions (slice 009)

**Input**: `spec.md`, `research.md` (R1–R15), constitution v4.0.0 (esp. Principle IX Authorization, X Time, XI Privacy, XII Security), `docs/architecture/adr-0003-domain-model.md` (one-aggregate-per-transaction), and the **already-implemented** slice-007 (project-sharing-membership — the dispatch-by-visibility policy this slice consumes) and slice-008 (task-assignment — the shared-only load guard and the `TaskAssigned` outbox-event posture this slice mirrors).

This slice **introduces one new aggregate** (`Comment`, ENT-08) with **two new tables** (`comments` + the `comment_mentions` child collection) and one EF migration (`AddComments`), and raises a new domain event (`UserMentioned`) to the Wolverine transactional outbox. It is the product's **first free-form user-content surface** and a stored-XSS target (Principle XII). It **adds no new error code** and **does not modify** the `tasks`, `projects`, or `project_memberships` tables (R9/R12).

**Vocabulary (load-bearing — keep aligned across all artifacts)**:
- **Effective role** — resolved by the reused slice-007 policy: `owner | editor | viewer`, or **none** (non-member). `owner` is derived from `Project.ownerId` (never a stored row); `editor`/`viewer` come from the `project_memberships` row.
- **Authorship grant** — an **object-level** capability distinct from role: `comment.author_id == caller`. It gates **edit/delete only**, and **only after** the role floor passes (R4). Project role — including `owner` — does **not** override it; **loss of membership overrides it** (FR-066 > FR-075).
- **Visibility** — `personal | shared`. Comments exist **only** on tasks whose project is `shared` (FR-072).

---

## 1. Entities

### ENT-08 — Comment (aggregate root) — NEW

A single authored message on a **shared-project** `Task`, carrying its own author grant, timestamps, edit/delete lifecycle, and @mention set. Modeled as its **own aggregate root** — `sealed class Comment : AggregateRoot<CommentId>` — **not** an owned child of `Task` (R1): a comment has a lifecycle independent of its task, its own author grant (FR-073/075), and emits its own event, so its consistency boundary is the single comment, not the task or the whole thread. It references its parent `Task` and its `author` **by id only** (no EF navigation collection on `Task` — the established no-nav-prop style). The `Task` aggregate is **not modified** by this slice.

| Field | Type | Constraints | Notes |
|---|---|---|---|
| `Id` | `CommentId` (uuid) | PK; **server-minted** UUIDv7; `ValueGeneratedNever` | `readonly record struct CommentId(Guid Value)` exposing `New() => Guid.CreateVersion7()` + `From(Guid)`, mirroring `ProjectMembershipId` (server-minted rows). Contrast client-minted `TaskId` (R1/R5). |
| `TaskId` | `TaskId` (uuid) | **REQUIRED**; FK → `tasks(id)` **ON DELETE CASCADE** | The parent task the thread hangs on. A comment is meaningless without its task (R11/R12). |
| `AuthorId` | `UserId` (uuid) | **NULLABLE**; FK → `users(id)` **ON DELETE SET NULL** | The author. Nullable **by design** so account erasure tombstones the author without destroying thread history (FR-085, Principle XI — R11). Provenance, not identity of the row. |
| `Body` | `string` | **REQUIRED**; non-empty after trim; `≤ MaxCommentLength` | The author's **raw** text, stored unmodified (renderer-agnostic; edit round-trips faithfully — R8). Length/non-empty enforced at the command boundary (§ validation); an optional DB CHECK on max length is a backstop. |
| `CreatedAt` | `DateTime` | UTC (`timestamptz`); set at create | When posted (Constitution X); stamped from the injected clock/`TimeProvider` (R10). |
| `EditedAt` | `DateTime?` | UTC (`timestamptz`); **null until first edit** | Set on every `EditComment`; drives the "edited" affordance (FR-073). Null = never edited (R5). |
| `DeletedAt` | `DateTime?` | UTC (`timestamptz`); **null until soft-delete** | The soft-delete tombstone (R5, Constitution VII 30s-undo). `DeleteComment` stamps it via `Comment.SoftDelete(deletedAt)` (versionless — **no** version bump, unlike `Task.SoftDelete`); the thread read (`ListTaskComments`) filters `deleted_at IS NULL`; the scheduled `ReapDeletedComment` reaper hard-purges the row on the 30s-window expiry. A slice-014 restore that clears it is a **named seam** (not built). |

**Mention set** — a small child collection owned by the aggregate, `IReadOnlyList<CommentMention>` over a private backing field, replaced wholesale by the comment's mutators (R6). See the `comment_mentions` sub-entity below.

**Table**: `comments`. **Column naming**: snake_case (`id`, `task_id`, `author_id`, `body`, `created_at`, `edited_at`, `deleted_at`), matching `tasks`/`project_memberships`.

**Indexes**:
- `ix_comments_task_created` — on `(task_id, created_at)`. Serves the chronological thread read (R5/R15) without a separate `(task_id)` index.
- `ix_comments_author_id` — on `(author_id)`. Serves the slice-015 erasure cascade + a "my comments" query (R11/R12).

**Foreign keys**:
- `task_id → tasks(id)` **ON DELETE CASCADE** — structural child; deleting/reaping a task removes its thread (R11).
- `author_id → users(id)` **ON DELETE SET NULL** — the **one FK that deliberately breaks** the house-style cascade parity: on account erasure the author becomes a tombstone (`author_id = NULL`), the comment **survives** where it anchors a thread (FR-085; a blanket cascade would hard-delete the comment — R11).

**No concurrency token** — a `Comment` carries **no `version` column** (R2). Edits/deletes are last-write-wins (§2).

**Soft-delete + scheduled reaper (Constitution VII 30s-undo — the `DeleteTask`/`ReapDeletedTask` mirror).** A comment is task/project **data**, so its delete is covered by the Constitution-VII rule ("delete … MUST be undoable for a minimum of 30 seconds … performed only by the original actor"; the sole carve-out is membership/role changes). `DeleteComment` does **not** hard-delete: `Comment.SoftDelete(deletedAt)` stamps `deleted_at` (versionless — no version bump), and the command publishes `new ReapDeletedComment(commentId, deletedAt)` with `DeliveryOptions { ScheduleDelay = 30s }` to the outbox (publish + `deleted_at` write commit together). `ReapDeletedCommentHandler` — off a durable **`comment-reaper`** local queue (`Program.cs`), **no caller** (excluded from the deny-by-default authz predicate alongside `ReapDeletedTask`/`AccountDeletionRequested`) — loads by raw id tombstone-inclusive and **hard-purges ONLY if** the row still exists **AND** `deleted_at` is non-null **AND** `deleted_at == the scheduled instant` (µs resolution) — restore-aware/idempotent. Because comments are versionless there is **no** `Task.Version`-style 0-rows-DELETE backstop; the instant match is the sole guard. The user-facing **restore/undo is the slice-014 seam** (a restore clears `deleted_at`, making the reaper no-op) — named, not built here (identical to task delete).

### comment_mentions (child collection owned by the Comment aggregate) — NEW

The typed @mention set: an @mention is **structured data, never free text** (FR-098). A user is mentioned **at most once** per comment.

| Field | Type | Constraints | Notes |
|---|---|---|---|
| `Id` | `Guid` (uuid) | PK (surrogate); UUIDv7; `ValueGeneratedNever` | Surrogate key — a **PK column can't be `SET NULL`**, so the tombstone-able `user_id` cannot itself be part of the key (R11). |
| `CommentId` | `CommentId` (uuid) | **REQUIRED**; FK → `comments(id)` **ON DELETE CASCADE** | The owning comment (mentions are owned — deleting the comment drops its mentions). |
| `UserId` | `UserId?` (uuid) | **NULLABLE**; FK → `users(id)` **ON DELETE SET NULL** | The mentioned member. Subject to the **same residual-attribution rule** as `author_id` — an erased mentioned user is tombstoned (`user_id = NULL`), not leaked, and never cascade-deletes the comment (R11). |

**Table**: `comment_mentions`. **Uniqueness**: `ux_comment_mentions_comment_user` — UNIQUE on `(comment_id, user_id)` de-duplicates a member's mention per comment (Postgres treats NULLs as distinct, so multiple erased-mention tombstones on one comment are harmless and live de-dup still holds). **Index** `ix_comment_mentions_user_id` on `(user_id)` for the mention-authority / erasure query.

### ENT-01 — Task (owned by slice 002) — parent, referenced by id, **unchanged**

This slice does **not** touch the `tasks` table. `Comment.TaskId` references `tasks(id)`; the parent task is loaded **read-only, for authorization only** (R3) — comment writes **do not** read or bump `Task.Version`. The task's `ProjectId` (nullable) is what the authorization gate dispatches on (null → personal → no comment surface → 404).

### ENT-02 — Project / ENT-07 ProjectMembership (owned by slices 004/007) — **consumed, unchanged**

No DDL. The comment gate resolves the parent task's `Project.Visibility` and (when `shared`) its membership roster via the existing `IProjectRepository.FindReadableAsync` + `IProjectMembershipRepository.ListByProjectAsync`, then calls the slice-007 `RequireRole` — **no fork** (R3). `Project.Version` is **not** read or bumped by comment writes (R1/R2).

### ENT-09 — Notification (owned by slice 017) — **NOT created here**

The @mention set drives the `UserMentioned` event this slice **raises** (R7); the ENT-09 Notification it eventually creates is **slice 017's** aggregate — a named seam, not built here.

---

## 2. Optimistic concurrency — **none: comments are versionless (last-write-wins)**

A `Comment` carries **no** optimistic-concurrency token, and this slice **does not use `version_conflict`(409)** at all (R2/R9). `EditComment` overwrites `body` + the whole mention set and stamps `edited_at`; `DeleteComment` **soft-deletes** the row (stamps `deleted_at`; the scheduled `ReapDeletedComment` reaper hard-purges after the 30s undo window — §1) — both **last-write-wins**, per Constitution Principle III (spec:127; the same posture as slice-006 labels, "versionless, no 409"). The safety discriminator: edit is **author-only**, and `body` + the mention set have **no cross-field or cross-entity invariant** a stale overwrite could corrupt, so the only possible conflict (the same author editing from two tabs) is correctly resolved by clobbering to the latest write. No comment request/response schema carries a `version` field. This is one fewer reused code than slice 007 (which used `version_conflict` + `last_owner`).

---

## 3. Authorization scoping (deny-by-default, **dispatched by the parent project's visibility** — reuse slice 007)

Comments authorize **through the parent** (Principle IX / FR-065): they carry no visibility or membership of their own. Every read and write loads the parent `Task`, resolves its `Project.Visibility`, and dispatches onto the **slice-007 membership + role branch unchanged** (`ResolveEffectiveRole` / `RequireRole`) — **no comment-specific policy** (R3). The gate mirrors `TaskAccessGuards.LoadWritableTaskAsync` (slice 005/008), with the personal branch **denied** (comments have no surface on personal tasks):

1. Load the parent `Task` by id (`ITaskRepository`); null → **404 `not_found`**.
2. `task.ProjectId is null` (Inbox / personal) → **404** for read AND write — personal projects have no membership set, so no comment surface (FR-072; the `SetTaskAssignees` shared-only 404 precedent).
3. `projects.FindReadableAsync(projectId, caller)` → foreign/absent OR shared-non-member → null → **404**. Then require `project.Visibility == Shared` else **404** (comments only on shared tasks).
4. `members.ListByProjectAsync(projectId)` then `authorization.RequireRole(project, memberships, required)` (3-arg deny-path form — the caller is resolved internally from `ICurrentUser`; the explicit-caller resolver is `ResolveEffectiveRole(project, memberships, caller)`):
   - **List / read the thread** → `EffectiveRole.Viewer` (any current member; non-member → 404). Precedent: `GetProjectTasks` shared arm.
   - **Post**, and the **role floor** for edit/delete → `EffectiveRole.Editor` (viewer → **403 `forbidden`**; non-member → **404**). Precedent: `SetTaskAssignees` requires `Editor`.
5. **Authorship (edit/delete only)** — **after** the role floor passes: assert `comment.AuthorId == caller` else **403 `forbidden`**. Project role (including `owner`) does **not** override (FR-075) — R4.

> **`DeleteComment` idempotency (soft-delete, §1).** The comment is loaded **tombstone-inclusive** (`FindByIdIncludingDeletedAsync`; absent/foreign → **404**) so the author-equality step can run even on an already-soft-deleted row (`author_id` is unchanged by soft-delete). The full gate then runs — `CommentAccessGuards(editor)` on the parent, then author-equality — so a non-member/viewer/non-author caller still gets their 404/403 as usual. **Only after the gate passes** does the `deleted_at` state decide: the caller's **own already-soft-deleted** comment → idempotent **204** no-op (no second reaper scheduled); a live comment → `SoftDelete` + publish the scheduled `ReapDeletedComment`. The authorization gate itself is **unchanged**; only hard→soft delete + the idempotent-replay short-circuit are added.

The caller is **always** `ICurrentUser.Id`, never the wire (slice-004 posture). The 404-vs-403 split is the slice-007 leak boundary: non-member → 404 (existence not disclosed); insufficient-role member → 403 (honest "you lack the role", FR-067).

### The strict two-step ordering for edit/delete (R4 — FR-066 structurally beats FR-075)

`EditComment` / `DeleteComment` run **role floor FIRST, author-equality SECOND**. Because membership is resolved **live** in the floor, a former member is 404'd **before** the author check ever runs — so FR-066's revoke-ALL wins over FR-075's author grant **for free**, with no "was this a former author" branch:

| Actor on comment `c` (authored by `E`) | Step 1 `RequireRole(Editor)` | Step 2 `author == caller` | Result |
|---|---|---|---|
| `E` (author, current editor/owner) | pass | pass | **allow** |
| `A` / `E2` (non-author owner/editor) | pass | fail | **403** (role does not override authorship) |
| `V` (viewer — even if author via later demotion) | **fail (403)** | not reached | **403** (member, insufficient role) |
| `F` (former member — incl. former author of `c`) | **fail (404)** | not reached | **404** (membership loss revokes ALL; FR-066) |
| `X` (non-member) | **fail (404)** | not reached | **404** |

### Role × operation capability matrix (the SC-016 deny matrix)

All operations are on a task in a **`shared`** project. A **personal/Inbox** task or a **foreign/absent** task/comment is **404** for every row (no comment surface / existence not disclosed).

| Operation | Required floor | viewer | editor (non-author) | editor/owner (author) | owner (non-author) | non-member `X` | former member `F` |
|---|---|---|---|---|---|---|---|
| List / read thread (`listTaskComments`) | `viewer`+ | allow | allow | allow | allow | **404** | **404** |
| Post comment (`postTaskComment`) | `editor`+ | **403** | allow | allow | allow | **404** | **404** |
| Edit own comment (`editComment`) | `editor`+ **then author** | **403** | **403** | allow | **403** | **404** | **404** |
| Delete own comment (`deleteComment`) | `editor`+ **then author** | **403** | **403** | allow | **403** | **404** | **404** |

> The "owner (non-author) → 403" cells encode FR-075: project role, **including owner**, does not override authorship. The "former member → 404" column encodes FR-066 winning over FR-075.

### Deny-shape rule (slice-007 posture, inherited)

- **Non-member / former member / personal-or-foreign task / foreign comment** → **404 `not_found`** (existence not disclosed across the membership boundary — Constitution XII).
- **Member with insufficient role** (viewer posting/editing/deleting) → **403 `forbidden`** (the member knows it exists; honest "you lack the role" — FR-067).
- **Non-author with sufficient role** (owner/editor editing/deleting someone else's comment) → **403 `forbidden`** (FR-075 — authorship not overridden by role).
- **Invalid content** (empty/whitespace-only body, over-length body, a mention id that is not a current member) → **422 `validation_failed`**, field-level (§ validation).
- **No `version_conflict`(409)** — comments are versionless (R2/§2).

### Mention candidacy (FR-066/073)

`mentionedUserIds` are validated **server-side** against the **current** member set of the comment's parent project (owner `A` ∪ membership rows, from the R3-loaded roster). A mention id that is **not** a current member (non-member `X` or a former member) → **422 `validation_failed`** on `mentionedUserIds` ("only current project members can be mentioned"); **no** mention is persisted. The set is de-duplicated. A **self-mention** is permitted but emits no event (R6/R7). Authorship/mention confer **no standalone access** (FR-066).

> **No new mention-candidates endpoint.** The @mention picker sources its candidate list from the **existing** slice-007 `GET /api/projects/{id}/members` roster (the project of the comment's parent task); this server-side validation is the defense-in-depth backstop behind that picker. This slice adds **no** candidate-lookup endpoint to the contract.

### Content safety split (FR-098 / FR-099)

- **Input (server-authoritative, → 422)** — FluentValidation at the command boundary (`InviteMemberValidator` precedent): `body` required, **not whitespace-only** (trim → non-empty), `≤ MaxCommentLength` (a single named constant — **4000** characters). An empty/whitespace-only body **MUST NOT create a thread entry** (rejected before persistence). Violations → 422 with a field message on `body` (FR-049 recoverable).
- **Output (render boundary, → safe DOM)** — sanitization to the "constrained safe subset (plain text + safe markdown)" runs at the **web render boundary** and is this slice's **one new web dependency** (a vetted markdown renderer + HTML sanitizer; strict allowlist, no raw HTML/`<script>`/event handlers, safe URL schemes only). The stored `body` is raw text; sanitization runs on render, so it covers historical rows. React's default text-escaping is the backstop. Detailed in `research.md` R8 + `plan.md` Technical Context.
- **CSP + security headers (FR-099)** — the API `SecurityHeadersMiddleware` (all environments) and the user-facing CSP in `apps/web/next.config.ts` **already exist** and are **reused**, not built (R8).

### Test coverage (Constitution VIII + IX governance gate)

**Every** comment data handler ships an **allow** and a **deny** integration test through the real DB, and the slice ships the **role × operation deny matrix** above as first-class tests (SC-013, SC-016): viewer-denied-post (403), viewer-denied-edit/delete (403), non-author-owner-denied-edit/delete (403), non-member-denied-read/post (404), former-member-denied-view/edit/delete (404), non-member-mention rejected (422), empty/over-length body rejected (422), plus the allow case per handler.

---

## 4. State transitions

### Comment lifecycle (R2/R5 — versionless, author-only edit/delete)
```
        PostComment(taskId, body, mentions)            EditComment(commentId, body, mentions)   [author only, LWW]
  (no row) ─────────────────────────────────▶ comment ⇄ comment           (overwrites body + mention set,
            [editor+ on the parent shared         │        (edited_at stamped, UserMentioned delta raised — R7)
             project; server mints CommentId;      │
             created_at stamped; UserMentioned     │  DeleteComment(commentId)  [author only, LWW]
             raised for the full mention set]      └──────────────────────────────────────────▶ (soft-deleted: deleted_at stamped,
                                                                                                  ReapDeletedComment scheduled +30s)
                                                                                                  the comment leaves the thread (AS-04);
                                                                                                  the reaper hard-purges on expiry
```
> `edited_at` is null until the first `EditComment`. Edit and delete require `RequireRole(Editor)` on the parent project **then** `author_id == caller` (R4). A viewer/non-author/former-member never reaches the mutation (§3 matrix). `DeleteComment` **soft-deletes** (stamps `deleted_at`) and is version-free/idempotent — the caller's own already-tombstoned comment replays as a **204** no-op (§1/§3).

### Delete soft-delete + reaper seam (R5 — Constitution VII 30s-undo; slice-014 restore NOT built here)
```
  DeleteComment(commentId)  →  Comment.SoftDelete(deletedAt): stamp deleted_at (versionless);
                                publish ReapDeletedComment(commentId, deletedAt) delayed +30s  [built here]
  ReapDeletedComment fires   →  hard-purge the row (mentions cascade) ONLY if still-tombstoned at the
                                scheduled instant (µs match) — restore-aware/idempotent            [built here]
  Slice-014 restore/undo     →  clears deleted_at (or a re-delete re-stamps a new instant) → the
                                scheduled reaper no-ops                                       [named seam, NOT built]
```
> The user-facing restore/undo command + endpoint are **slice 014** — this slice ships only the soft-delete + scheduled-reaper substrate, exactly as `DeleteTask`/`ReapDeletedTask` do (their own doc: "a slice-014 restore CLEARS deleted_at"). No comment restore endpoint and no client-side undo toast are added (the task-delete web mutation has none — R14).

### @mention set (R6/R7 — whole-set replace on each write)
```
  PostComment  →  persist comment_mentions = { validated current-member ids, de-duped };
                   UserMentioned raised for that set (minus self-mention)
  EditComment  →  replace comment_mentions with the new validated set;
                   UserMentioned raised ONLY for the delta (newly-added ids); removals emit nothing (no "un-notify")
```

### Author anonymization seam (R11 — slice-015 erasure, NOT built here)
```
  Author account erased (slice 015 cascade)  →  comments.author_id := NULL   (ON DELETE SET NULL)
                                                 the comment SURVIVES where it anchors a thread (FR-085)
                                                 read model renders null author as a neutral tombstone ("Deleted user" — §5)
  Mentioned user erased                       →  comment_mentions.user_id := NULL  (same residual rule; comment not cascade-deleted)
```
> This slice **models** for anonymization (nullable `author_id`, tombstone-safe read model); the erasure cascade itself ships in slice 015.

### Real-time seam (R13 — slice 016, NOT built here)
```
  Local post/edit/delete  →  paints optimistically within one frame; server reconciles LWW (R2/R14)  [built here, web]
  Remote comment into an open thread  →  live propagation is slice 016 (SignalR)                     [named seam, NOT built]
       reconciliation contract for 016: an inbound remote comment MUST NOT clobber a pending local optimistic edit
```

---

## 5. Read models (delta)

- **New `CommentResponse`** — `listTaskComments` returns an ordered (chronological, `created_at`) list of:
  `{ id, taskId, authorId (nullable), authorDisplayName (tombstone-safe), body, mentions: [{ userId, displayName }], createdAt, editedAt (nullable), canEdit (bool) }`.
  - `authorId` nullable + `authorDisplayName` renders **"Deleted user"** when `authorId` is null (the R11 tombstone realized on the read side).
  - `mentions` renders from the **typed tokens** (R6), resolving id → `displayName` at read; an erased mentioned user renders tombstone-safe.
  - `canEdit` = `authorId == caller` — a server-computed convenience gating the client edit/delete affordances (AS-04). The **real** check is the R4 server gate; `canEdit` is **not** the security boundary (FR-068 authoritative).
  - **Emails are never echoed** (Constitution XI — the members roster already withholds them, slice-007 R17).
  - Thread read is **viewer+**: a viewer receives the full thread but the client renders **no composer** (AS-03); a non-member/former member receives **404**, not an empty thread (no existence leak — R3/R15).
  - The thread read **EXCLUDES soft-deleted comments** — `ListTaskComments` (via `ICommentRepository.ListByTaskAsync`) filters `deleted_at IS NULL`, so a comment leaves the thread the instant its author deletes it (AS-04), even though the row lingers for the 30s undo window before the `ReapDeletedComment` reaper hard-purges it (§1/R5).
- **`TaskResponse` / `ProjectResponse`** — **unchanged**. Comments are read via the dedicated thread endpoint, not embedded in the task read model (the thread is lazily fetched when the detail panel opens — R14). No existing read model gains a comment field.

---

## 6. Migration Plan (`AddComments` — R12; **FR-051 is LIVE**)

**EF Core migration** (`apps/api/src/TaskFlow.Infrastructure/Persistence/Migrations/`, new `*_AddComments.cs`):

- `Up`:
  - Create **`comments`**: `id uuid` PK (UUIDv7, `ValueGeneratedNever`), `task_id uuid NOT NULL` FK → `tasks(id)` **ON DELETE CASCADE**, `author_id uuid NULL` FK → `users(id)` **ON DELETE SET NULL**, `body text NOT NULL` (app-boundary length, R8; optional CHECK `char_length(body) <= 4000` backstop), `created_at timestamptz NOT NULL`, `edited_at timestamptz NULL`, `deleted_at timestamptz NULL` (the soft-delete tombstone — R5, Constitution VII 30s-undo; the thread read filters `deleted_at IS NULL`, the scheduled `ReapDeletedComment` reaper hard-purges on expiry). Indexes `ix_comments_task_created (task_id, created_at)` and `ix_comments_author_id (author_id)`.
  - Create **`comment_mentions`**: `id uuid` PK (surrogate — a PK column can't be `SET NULL`, R11), `comment_id uuid NOT NULL` FK → `comments(id)` **ON DELETE CASCADE**, `user_id uuid NULL` FK → `users(id)` **ON DELETE SET NULL**, UNIQUE `(comment_id, user_id)` (= `ux_comment_mentions_comment_user`, de-dup), index `ix_comment_mentions_user_id (user_id)`.
  - **No change** to `tasks` / `projects` / `project_memberships`.
- `Down`: drop `comment_mentions` then `comments` (and their indexes/constraints). No other table touched.
- **Migration-review checklist** (Constitution VII): forward-only / expand-only — a purely additive two-table migration, **no rewrite** of existing rows; tested against a representative snapshot. FK directions encode ownership vs privacy: `task_id`/`comment_id` **CASCADE** (structural children); `author_id`/`mention.user_id` **SET NULL** (privacy tombstone — R11).

**FR-051 backup-before-migration is LIVE this slice** (R12): `AddComments` is a real schema change, so the automatic pre-migration backup **and** the CI `backup → migrate → restore-test` gate (Constitution VII) must actually execute against it — the slice-004/007/008 posture, NOT the slice-003/005 no-op. The plan tracks **verifying that gate fires** against `AddComments`.

**New application-layer seams**:
- `ICommentRepository` — `AddAsync(Comment)` / `FindByIdAsync(CommentId)` (with its mention set; **live only** — `deleted_at IS NULL`) / `FindByIdIncludingDeletedAsync(CommentId)` (tombstone-inclusive — for `DeleteComment`'s idempotent replay + the reaper, mirroring `ITaskRepository.FindByIdIncludingDeletedAsync`) / `ListByTaskAsync(TaskId)` (chronological thread read; **filters `deleted_at IS NULL`**) / `Remove(Comment)` (the reaper's hard-purge), loaded transactionally (one-aggregate-per-transaction, ADR-0003), with the same `DbUpdateException` unique/constraint translation posture as `IProjectMembershipRepository`.
- `CommentId` strongly-typed id + EF value conversion (`ValueGeneratedNever`), mirroring `ProjectMembershipId` (server-minted `New()`).
- Commands `PostComment` / `EditComment` / `DeleteComment` + read `ListTaskComments`, each authorizing through the **reused** slice-007 `RequireRole` dispatch (R3) plus the R4 authorship step for edit/delete. `DeleteComment` **soft-deletes** (`Comment.SoftDelete(deletedAt)` + publish the scheduled `ReapDeletedComment`) and is version-free/idempotent (§1/§3).
- `PostCommentValidator` / `EditCommentValidator` (FluentValidation) — `body` non-empty-after-trim + `≤ MaxCommentLength` (4000); mention-candidacy (current-member) is a cross-row check in the handler against the R3-loaded roster (R6).
- Domain event `UserMentioned` (`sealed record : DomainEvent`, pure ids: `CommentId`, `TaskId`, `ProjectId`, added `IReadOnlyCollection<UserId>`, actor `UserId`) raised via `DomainEventDispatch.PublishAndClearAsync` **before** `SaveChangesAsync` (the `SetTaskAssignees` sequence), plus a **registered no-op routable handler** `static Handle(UserMentioned _){}` + the `PublishMessage<UserMentioned>().ToLocalQueue(...)` route + the `AuthorizationMiddleware` off-queue exclusion — consumed by slice 017 (R7).
- Scheduled reaper `ReapDeletedComment` (`sealed record ReapDeletedComment(CommentId, DeletedAtInstant) : DomainEvent`, pure ids — mirrors `ReapDeletedTask`) + `ReapDeletedCommentHandler` (off the durable **`comment-reaper`** local queue routed in `Program.cs` via `PublishMessage<ReapDeletedComment>().ToLocalQueue("comment-reaper")`; **no caller** — added to the deny-by-default authz predicate exclusion alongside `ReapDeletedTask`/`AccountDeletionRequested`). The handler loads by raw id tombstone-inclusive and hard-purges the row (mentions cascade) **only if** still-tombstoned at the scheduled instant (µs match) — restore-aware/idempotent; the slice-014 restore that clears `deleted_at` is a named seam (R5).

---

## What is unchanged

- The `tasks`, `projects`, and `project_memberships` tables (no DDL on any) — `Comment` references `tasks(id)` / `users(id)` by id and authorizes through the **reused** slice-007 policy; `Task.Version` / `Project.Version` are **not** read or bumped by comment writes (R1/R2).
- The slice-007 dispatch-by-visibility authorization policy (`ResolveEffectiveRole` / `RequireRole`) — **consumed verbatim, not forked** (R3).
- The error contract — **no new error code** (R9): `forbidden` (403), `not_found` (404), `validation_failed` (422) are reused; `version_conflict` (409) is **not used** this slice (comments are versionless — R2). The transformer `ErrorCodes` array and the web `ERROR_UX` map stay **unchanged**, so the exhaustiveness gate stays green with no edit.
- The BFF proxy, authentication wiring, `ICurrentUser` resolution, the `AuthorizationMiddleware` deny-by-default gate, the `SecurityHeadersMiddleware` + `next.config.ts` CSP/headers (FR-099 — reused, R8), and the existing task soft-delete reaper (`DeleteTask`/`ReapDeletedTask`) — whose durable-local-queue + no-caller-exclusion substrate this slice **mirrors** (not modifies) for the new `comment-reaper` queue + `ReapDeletedComment` handler (R5).
- The time posture — UTC `timestamptz` on the server; Warsaw-referenced **relative** display computed on the web via the existing `date-fns` + `apps/web/src/lib/timezone.ts` util (no new date library; no server-side Warsaw conversion for comments — R10). No NodaTime.

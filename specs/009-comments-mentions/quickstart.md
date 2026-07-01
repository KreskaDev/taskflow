# Quickstart & Validation Guide: Comments & @Mentions (slice 009)

Validates the **US-14 Comments & @Mentions** journey (AS-01..AS-04) and the **comment authorization surface** (Principle IX — the role × operation deny matrix, the object-level authorship grant, and the membership-loss override) end-to-end, against the real stack. Comments **consume** the slice-007 dispatch-by-visibility policy unchanged (a comment authorizes on its parent task's **shared** project's membership + role); this slice validates the **comment** surface — post/edit/delete/list, the @mention typed token + the `UserMentioned` outbox event, and FR-098 content safety (length/empty rejection + render-boundary sanitization). The **in-app notification** the mention triggers is owned by slice 017 — this slice validates only that `UserMentioned` is **raised** (research R7). **Live propagation** of a remote comment into an open thread is slice 016. References `spec.md`, `data-model.md`, `research.md`, and `contracts/openapi.yaml` rather than duplicating them.

> This is a run/validation guide — not implementation. Bodies, migrations, and full test suites belong in `tasks.md` and the implementation phase.

## Prerequisites

- The slice-002..008 stack boots locally (see the local-run & E2E runbook): PostgreSQL + the C# API on `:4311` (for `gen:api`), the Next.js web app, and the fake-IdP/BFF for **at least four** authenticated, admitted callers — an owner **A**, editors **E** and **E2**, a viewer **V**, plus a non-member **X** (ASM-13 admission). A shared project **P** (owner **A**; **E**, **E2** editors; **V** viewer) with at least one task **T** (`P` is `shared`), and one **personal** task **Tp** (no project / Inbox) for the no-comment-surface case.
- `AddComments` migration applied. **FR-051 (research R12): confirm the backup-before-migrate hook ran and the CI restore-test gate is green** before relying on the migration — `AddComments` is a real schema change (two tables: `comments` + `comment_mentions`).
- The **one new web dependency** (the markdown renderer + HTML sanitizer, research R8) is installed and pinned; `src/lib/markdown/safeMarkdown.tsx` renders the safe subset with a strict allowlist.
- Typed client regenerated: `cd apps/web && pnpm gen:api` (API on `:4311`) → `schema.d.ts` gains the comment operations (`listTaskComments`, `postTaskComment`, `editComment`, `deleteComment`), `CommentResponse`, `CommentMention`, `CommentListResponse`, `PostCommentRequest`, `EditCommentRequest`; `pnpm typecheck` green. **No new `errorCode`** — `ERROR_UX` stays exhaustive with no change (R9).

## Setup

```bash
# Backend (from repo root)
dotnet build
dotnet test                       # xUnit Comment behavior + Testcontainers-Postgres integration (incl. authz allow+deny AND the role×operation deny matrix + the UserMentioned-raised assertion)

# Web (from apps/web)
pnpm install                      # installs the new markdown renderer + sanitizer
pnpm gen:api                      # API must be on :4311
pnpm test                         # Vitest unit/component (incl. the sanitization-inert regression)
pnpm e2e                          # Playwright (self-boots its own stack)
```

## Validation scenarios

### A. Post a comment (US-14.AS-01)

| # | Action | Expected |
|---|---|---|
| AS-01 (editor posts) | As editor **E**, open task **T**'s detail panel and post a comment | **200** — the comment appears in **T**'s thread with **E** as author and a `createdAt` timestamp (rendered relative against `Europe/Warsaw`, R10); the server **minted** the `CommentId` (UUIDv7, not client-supplied, R1). `editedAt` is null (never edited) |
| owner posts | As owner **A**, post a comment on **T** | **200** — owner may comment (editor+ floor, FR-072) |
| chronological thread | As **E2**, open **T**'s thread | **200** — `listTaskComments` returns the thread ordered by `createdAt`; each item carries author identity, mentions, `createdAt`/`editedAt`, and `canEdit` (true only for the caller's own comments) |

### B. @mention a member (US-14.AS-02 — the UserMentioned event)

| # | Action | Expected |
|---|---|---|
| AS-02 (mention) | As **E**, post a comment on **T** with `mentionedUserIds: [E2]` (E2 is a current member) | **200** — the mention is persisted as a **typed token** in `comment_mentions`; a **`UserMentioned`** event is raised to the outbox (pure ids: `CommentId`/`TaskId`/`ProjectId`/added=`[E2]`/actor=`E`). Assert via `.Sent.MessagesOf<UserMentioned>()` — the slice registers a **no-op routable handler**; slice 017 consumes it |
| mention non-member | As **E**, post with `mentionedUserIds: [X]` (X is not a member of **P**) | **422 `validation_failed`** on `mentionedUserIds` ("only current project members can be mentioned", R6); **no** comment is created |
| self-mention | As **E**, post with `mentionedUserIds: [E]` | **200** — allowed and persisted, but **no** `UserMentioned` for the self-mention (you are not notified of your own mention, R6/R7) |
| edit adds a mention | As **E**, edit the AS-02 comment to `mentionedUserIds: [E2, A]` | **200** — `UserMentioned` raised for the **delta only** (`added=[A]`); E2 (unchanged) is **not** re-notified (R7) |
| edit removes a mention | As **E**, edit again to `mentionedUserIds: [A]` | **200** — E2's mention is dropped; **no** event (removals emit nothing — no "un-notify", R7) |

### C. Viewer is read-only (US-14.AS-03 — FR-072)

| # | Action | Expected |
|---|---|---|
| AS-03 (viewer reads) | As viewer **V**, open **T**'s detail panel | **200** — V receives the **full thread** (viewer+ read, R3) but the client renders **NO composer** (AS-03); `canEdit` is false on every item |
| viewer-denied-post | As viewer **V**, attempt `postTaskComment` on **T** (bypassing the UI) | **403 `forbidden`** — "you have viewer access; commenting requires editor" (FR-067/072/049). The server is authoritative — the missing composer is UI convenience only (FR-068) |

### D. Author edits/deletes own comment (US-14.AS-04 — FR-075 authorship grant)

| # | Action | Expected |
|---|---|---|
| AS-04 (author edits) | As **E** (author of comment `c`), edit `c`'s body | **200** — whole-body + whole-mention-set replace; `editedAt` stamped (drives the "edited" affordance); **last-write-wins**, no `version`/409 (R2). The thread updates |
| AS-04 (author deletes) | As **E**, delete `c` | **204** — `c` is **soft-deleted** (`deleted_at` stamped) and **leaves the thread immediately** (the read filters `deleted_at IS NULL` — AS-04); a scheduled `ReapDeletedComment` (+30s) hard-purges the row (its `comment_mentions` cascade) after the Constitution-VII undo window (R5) |
| delete is idempotent | As **E**, delete `c` again (replay) | **204** — idempotent no-op (own already-soft-deleted row); **no** second reaper is scheduled (R5) |
| non-author-owner-denied | As owner **A** (NOT the author), attempt to edit or delete **E**'s comment | **403 `forbidden`** — "only the author can edit/delete this comment" — project role, **including the owner**, does NOT override authorship (FR-075, R4) |
| non-author-editor-denied | As editor **E2** (NOT the author), attempt to edit or delete **E**'s comment | **403 `forbidden`** (R4) |
| two-tab LWW | As **E**, edit `c` from two tabs concurrently | Both succeed; the **later** write wins (no 409 — comments are versionless, R2). No pending local optimistic edit is clobbered on the web (Principle III, R14) |

### E. Membership loss overrides the author grant (FR-066 > FR-075)

| # | Action | Expected |
|---|---|---|
| former-member-denied-view | **E** authors `c`; then **A** removes **E** from **P**. As **E**, read **T**'s thread | **404 `not_found`** — membership loss revokes ALL access; `createdBy`/authorship confers **no** standalone access (FR-066, R4). Existence not disclosed |
| former-member-denied-edit/delete | As former member **E**, attempt to edit or delete their previously authored `c` | **404 `not_found`** — the role floor (step 1) 404s **before** the author check ever runs, so FR-066 structurally beats FR-075 (R4). Not a 403 (E is no longer a member) |
| re-add restores access | **A** re-invites **E** as editor; as **E**, edit `c` again | **200** — current membership is what authorizes (live per request); `c` survived the removal (only access was revoked, not the row) |

### F. Non-member / personal-task have no comment surface (FR-072)

| # | Action | Expected |
|---|---|---|
| non-member-denied-read | As non-member **X**, read **T**'s thread / post / edit / delete | **404 `not_found`** — existence NOT disclosed across the membership boundary (R3). Never 403 |
| personal-task-no-comments | As the owner of personal task **Tp**, read or post a comment on **Tp** | **404 `not_found`** — a personal/Inbox task (null `ProjectId`) has **no membership set → no comment surface** (FR-072, R3). Even the task owner gets 404 for comments |
| foreign/absent task or comment | Any caller, `listTaskComments` on an absent task id, or `editComment`/`deleteComment` on an absent/foreign comment id | **404 `not_found`** (R3/R5) |

### G. Authorization coverage (Constitution VIII + IX — every handler; SC-013 / SC-016)

| Case | Expected |
|---|---|
| Each data handler (`postTaskComment` / `editComment` / `deleteComment` / `listTaskComments`) | Ships **both** an **allow** AND a **deny** integration test through the real DB (SC-013) |
| The **role × operation deny matrix** (SC-016) | viewer-denied-post (403), viewer/non-author-denied-edit/delete (403), non-author-owner-denied-edit/delete (403), non-member-denied-read/post (404), former-member-denied-view/edit/delete (404), non-member-mention (422), empty/over-length body (422) — each asserted as a first-class test (data-model §3) |
| The `UserMentioned` outbox event | Asserted **raised** on post/edit (added-only delta) via `.Sent.MessagesOf<UserMentioned>()`; the no-op routable handler makes the publish observable (R7) |
| Any request without a valid session | **401 `unauthenticated`** |
| Governance | Authorization changes reviewed by a **non-author** before merge (governance gate); the deny matrix is the mechanically-verifiable artifact |

## Server-validation table (trust boundary — FR-098)

| Input | Expected |
|---|---|
| Post/edit an **empty** or **whitespace-only** body | **422 `validation_failed`** on `body` — and **no** thread entry is created (FR-098, R8) |
| Post/edit a body **> 4000** chars (`MaxCommentLength`) | **422 `validation_failed`** on `body` — "comment is too long" (FR-049 recoverable, R8); no comment created/updated |
| Post/edit with a `mentionedUserIds` entry that is **not a current member** of **P** (non-member or former member) | **422 `validation_failed`** on `mentionedUserIds`; no comment created/updated (R6) |
| A **stored-XSS** payload in `body` (e.g. `<script>…</script>`, `<img onerror=…>`, `javascript:` URL, raw HTML) | Accepted + stored **raw** (server is not the render boundary), then rendered **INERT** by `safeMarkdown` — no script executes, no raw HTML/event handler survives, only the safe-subset markdown renders (FR-098/099, Principle XII). The `comment-render.test.ts` regression asserts this |
| A literal `@` in prose (not a typed mention) | Rendered as inert text — mentions come from the **typed token**, never scraped from the body (R6) |
| Non-member targets any comment operation | **404 `not_found`** (existence not disclosed, R3) |
| Member with insufficient role (viewer post/edit/delete) OR a non-author (any role, incl. owner) edit/delete | **403 `forbidden`** (R3/R4) |
| Any comment request carrying a `version` field | There is no `version` field — comments are versionless (no 409 this slice, R2) |

## Cross-cutting checks

- **A11y (Principle II)**: the comment **composer**, the **@mention picker**, and the **delete-confirmation** dialog follow the dialog focus contract (initial focus, focus trap, Esc, return focus — FR-101); the author **edit/delete affordances** and the mention picker have keyboard/focus-triggered equivalents — **no hover-only** content (FR-046); author/timestamp/mention never carry meaning by color alone (FR-044); visible focus (FR-042); correct ARIA roles/labels (FR-043); `prefers-reduced-motion` respected (FR-047); single-key shortcuts suppressed in the **composer** and **mention picker** (FR-031); no AT-binding collision (FR-045). Server-initiated updates + mutation toasts announce via the polite ARIA-live region **without stealing focus** (FR-101).
- **Instant response (Principle III/FR)**: post/edit/delete **paint optimistically within one animation frame** (the `useCommentMutations` optimistic + rollback recipe, R14); on server error the optimistic change **rolls back** with an actionable message (FR-049). Confirm an inbound remote comment (once slice 016 exists) would reconcile under LWW **without clobbering a pending local optimistic edit** — the reconciliation contract this slice records for 016 (R13).
- **Server-authoritative (Principle V/FR-068)**: `CommentResponse.canEdit` drives the client edit/delete affordances (and the viewer sees no composer) but is **never** the security boundary — disable the UI gate and confirm the server still returns 403 (non-author/viewer) / 404 (non-member). The client holds no authoritative copy.
- **Privacy (Principle XI/XII)**: the thread surfaces `authorDisplayName` + mention `displayName`, **never** emails (Constitution XI). Confirm the read model is **tombstone-safe**: with `author_id = NULL` (the slice-015 erasure seam, simulated) the item renders **"Deleted user"** and the comment **survives** in the thread (FR-085, R11/R15); an erased mentioned user renders tombstone-safe too. Structured rejection logs carry `ErrorCode`/`Method`/`Path` only — **never the comment body or @mention identities** (FR-050).
- **Delete soft-delete + reaper (Principle VII 30s-undo)**: confirm `DeleteComment` **soft-deletes** (`deleted_at` stamped, the comment leaves the thread) rather than hard-deleting, and publishes a scheduled `ReapDeletedComment` (+30s) off the durable `comment-reaper` queue; confirm the reaper hard-purges only if still-tombstoned at the scheduled instant (restore-aware/idempotent) and that a replayed delete is an idempotent **204** no-op with no second reaper. The user-facing restore/undo is the **slice-014** seam (not built) — the `DeleteTask`/`ReapDeletedTask` mirror (R5).
- **Migration (Principle VII)**: confirm no unexpected migration beyond `AddComments`; confirm **no DDL on `tasks` / `projects` / `project_memberships`**; confirm `comments` has the `deleted_at timestamptz NULL` soft-delete column; confirm `comments.author_id` is **`ON DELETE SET NULL`** (not cascade — the tombstone FK) while `task_id`/`comment_id` **cascade**; confirm `comment_mentions` uses a **surrogate PK** with a UNIQUE `(comment_id, user_id)` (so `user_id` can be `SET NULL`); confirm the backup-before-migrate + restore-test gate executed and is green (FR-051, R12).
- **Forward seams (named, not built)**: confirm post/edit **raise** `UserMentioned` (the authority signal) — the **notification** consumer (slice 017), **live propagation** (slice 016), the **author-anonymization cascade** (slice 015), and the **comment restore/undo** command+endpoint (slice 014 — this slice ships only the soft-delete + scheduled-reaper substrate) subscribe/attach later with no change to this slice (R5/R7/R11/R13).

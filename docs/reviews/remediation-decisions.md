# Remediation Decision Ledger (from 2026-06-15 design review)

Authoritative resolutions for the 33 blockers / 256 findings. Workflow agents MUST follow these exact decisions (no re-introducing the flagged ambiguity). Product-vision is the sole ID allocator; new IDs are contiguous after the current max.

## Owner sign-off decisions
- **Timezone:** single instance reference zone = **Europe/Warsaw**; store UTC; per-user timezones out of scope.
- **Admission:** account creation **gated** to an email allowlist OR Google Workspace hosted-domain (`hd`). Reverses "open sign-up" → constitution bump is **MAJOR → v4.0.0**.

## Blocker resolutions
- **B1 Owner:** `Project.ownerId` is the single **immutable** owner (unshare/delete/transfer authority). Assignable roles are **editor/viewer only** — owner is NOT a freely assignable role. Ownership moves only via an explicit **transfer-owner** command. Guard: last owner cannot be removed/demoted/leave (recoverable FR-049 error).
- **B2 Cycle/Label anchors:** **Label** gets `ownerId` (Tier A per-user); *applying* a label to a task follows the task's tier. **Cycle** is **team-wide** (single instance, ASM-10) with explicit create/activate/close/delete authorization; keep the global partial-unique active-cycle index. Fix slice-011 "Tier A per-user" language to "team-wide".
- **B3 Tier dispatch:** authorization is **selected by the containing project's visibility**, not a conjunction. Personal/unprojected → ownership; shared-project entities → `ProjectMembership` + role. `createdBy`/assignee are **provenance only**, never a standalone grant. Leave/remove/unshare revokes **all** access. Reclassify slices 005/012/015 from "Tier A" to dispatch.
- **B4 Comment authorship:** authorship is a distinct object-level grant — author (and only author) edits/deletes own comments; project role does not override; loss of membership overrides the author right. Reconcile ADR-0005 "no ACLs".
- **B5 Live revocation:** membership/role change MUST evict the affected SignalR connections (and/or re-check at fan-out) → removed member gets no further patches, client forced to 403 re-sync.
- **B6 Notification re-auth:** resolving a notification source re-runs deny-by-default authz; payload carries no content beyond current authz; denied → "no longer available". ENT-09 source = **live reference** (re-checked), not a snapshot.
- **B7 Undo vs LWW:** undo restore is a **normal optimistic write subject to LWW**; "fully restores" is conditional on the no-concurrent-edit case and surfaces overwrites (FR-049); fans out over SignalR. Only the original actor (scoped by current role) may undo. Cover cascade-delete + cycle-rollover undo.
- **B8 Soft-delete:** slice 002 ships **soft-delete from day one** (`deleted_at`, excluded from authz-scoped queries, reaped after the 30s window) so 014 is a pure retrofit. Optimistic-delete rollback UX = row reappears + FR-049 message.
- **B9 WebSocket:** **Caddy reverse-proxies the SignalR hub path directly to the api container** (handles the WS upgrade); it does NOT route through Next.js. WS is the SLA-bearing transport.
- **B10 Time:** see timezone decision; `due_date` carries a `has_time` flag (date vs datetime). Parser: **client parses for optimistic paint and sends resolved ISO + offset; server validates, does not re-parse Polish** — fix the §V "no Polish on server" claim. "Equal to today" = same calendar day in Europe/Warsaw.
- **B11 Error contract:** adopt **ProblemDetails (RFC 9457)** + field-level validation extension + stable machine-readable error-code enum; generated TS client + Zod model it. New **ADR-0009 (API & error contract)**. Repo-wide Error-UX mapping (validation / 403 / 404 / 409 / 500 / network → message + recovery); slice 002 owns optimistic-rollback UX scenarios.
- **B12 Sessions/OAuth:** session store named (Postgres-backed); absolute + idle lifetimes; new session id at OAuth completion; server-side sign-out invalidation; explicit SameSite + CSRF token/origin check on mutations; OAuth `state`+`nonce`+PKCE + id_token validation. BFF→API carrier = **signed short-lived token** over the internal Docker network (API port not externally reachable).
- **B13 Invitations:** invite **by email, resolved against existing signed-in Users only**; unknown email → clear FR-049 message ("ask them to sign in once first"); pending/pre-account invites are OOS.
- **B14 Deployment:** secrets via env-file (chmod 600)/Docker secrets, never committed/baked/logged; immutable **git-SHA image tags** (rollback = redeploy prior SHA) + expand/contract migrations + failure runbook; scheduled `pg_dump` with retention independent of deploys + **offsite copy** + CI **restore-test**; Compose healthchecks (`pg_isready`, `depends_on: service_healthy`).
- **B15 Recurrence:** successor carries forward the **recurrence rule + anchor** (chain continues; test a 2nd successor). FR-009 spawn = **server-side Wolverine scheduled job** (not client startup); idempotent (≤1 successor/instance); undo of completion removes the successor; re-validate carried assignees against current membership, no re-notification.

## Needs-more-info resolutions
- **Overdue tasks (005):** Today includes overdue incomplete tasks (shown as overdue). Today/Upcoming exclude done/cancelled by default. Deterministic order: project group → priority → due time → created.
- **Next cycle (011):** cycles are ordered by start date; "next cycle" = the next planned cycle by start date; planned→active is a manual activation (single active invariant).
- **Monthly recurrence (012):** day-of-month with end-of-month clamping (Jan 31 → Feb 28/29); recurrence requires a due date (if none, recurrence is unavailable).
- **Project hierarchy (004):** re-parenting allowed within the 1-level rule; deleting/archiving a parent prompts for children (cascade/orphan-to-top); project **edit** (name/color/icon/parent) gets an FR + scenario; unarchive gets an FR.
- **Assignee membership (008):** assignees MUST be current members of the task's shared project (enforced; deny test). `TaskAssigned` carries the delta + actorUserId; idempotent; self-assignment allowed (no self-notification).
- **"Changed" notification (017):** closed trigger set = status / due date / assignee / project-move; low-signal edits (description typos) excluded; coalesce rapid changes; suppress self-actions via `actorUserId`. Add an Edge Cases section to slice 017.
- **Comment safety (009):** max length (e.g. 10k chars), empty rejected, plain-text + safe markdown subset, output-sanitized; @mention stored as a typed token referencing a User id.
- **Open view (016):** "open/visible shared view" = a client currently subscribed to a shared project's SignalR group; echo-suppression via originating connection id. 
- **Account deletion (privacy):** in scope (new US-17 + FR-085/086) — cascade per Principle XI.
- **Search (013):** **server search endpoint** (authz-filtered inside the 50ms budget over the caller's accessible set); not a client-only index. Invalidate/refetch on access-loss events.
- **10k benchmark:** all "10,000 tasks" figures = per-user authz-scoped accessible working set; perf seed reflects overlapping shared membership.

## New ID allocations (product-vision)
- **US-17 — Account & Data Management** (P2): export own data; delete account with erasure cascade. (export detail stays in US-07/slice 015.)
- **New FRs (contiguous after FR-084):**
  - FR-085 account deletion & erasure cascade (anonymize authored comments to a tombstone identity; null/reassign createdBy/assignee; transfer or delete owned shared projects; purge recipient notifications).
  - FR-086 data-retention stance (backups, soft-deleted/undo-window data, comments, notifications — retained until account deletion).
  - FR-087 admission control (allowlist / Workspace `hd`; reject non-admitted).
  - FR-088 session policy (absolute+idle lifetime, rotation at OAuth completion, server-side sign-out invalidation, Postgres-backed store).
  - FR-089 CSRF + SameSite on BFF mutations.
  - FR-090 OAuth hardening (state, nonce, PKCE, id_token validation).
  - FR-091 BFF→API identity carrier integrity (signed short-lived token; API port internal-only).
  - FR-092 time rule (UTC storage; Europe/Warsaw reference zone for all date-relative computation; due_date has_time flag; DST via library).
  - FR-093 error contract (ProblemDetails RFC 9457 + validation extension + error-code enum; modeled by generated client + Zod).
  - FR-094 ownership transfer command + last-owner guard.
  - FR-095 live-subscription authorization (membership change revokes/re-auths SignalR subscriptions).
  - FR-096 notification dereference re-authorization (no content beyond current authz; denied → unavailable).
  - FR-097 soft-delete for undoable deletions (deleted_at tombstone; excluded from authz queries; reaped after 30s).
  - FR-098 comment safety (length bound, empty rejected, sanitized output, typed @mention token).
  - FR-099 content sanitization & CSP / security headers.
  - FR-100 secrets handling (runtime-injected, never committed/baked/logged).
  - FR-101 ARIA-live for server-initiated updates/toasts + dialog focus contract.
- **New ENT-10 — Undo Snapshot** (typed pre-action snapshot / tombstone record backing the 30s undo window).
- **New SC-016** (authz coverage mechanism: every handler has allow+deny tests; role×operation deny matrix) and **SC-017** (account deletion removes/anonymizes all personal data).
- **New ASM-12** (instance timezone Europe/Warsaw; per-user TZ out of scope) and **ASM-13** (admission gated; not public).
- **New OOS-18** (pending/pre-account invitations) and **OOS-19** (per-user timezones).
- **Amend ENT-03 Cycle** (+team-wide ownership/authorization note), **ENT-04 Label** (+ownerId), **ENT-09 Notification** (source = live reference).
- **Amend US-12.AS-02** (per B1): a member's *assignable* role changes between **editor/viewer** only; owner is reached solely via the explicit FR-094 ownership-transfer command, not by setting a member to an "owner" role.
- **Amend FRs:** FR-008 (+recurrence rule/anchor carry-forward), FR-009 (server scheduled job), FR-035 + SC-005 (owner-scoped export), FR-040 (conditional restore under LWW + soft-delete + who-may-undo + cascade/rollover), FR-052 (gated sign-up → FR-087), FR-061 (roles editor/viewer only), FR-062 (owner via transfer not promotion), FR-065..068 (dispatch by visibility; createdBy provenance-only; revoke-all-on-leave), FR-073/075 (comment author grant + safety), FR-079..084 (closed "changed" set, coalescing, self-suppression, re-auth).

## Out (still) — confirm
Email/push notifications, presence, activity/audit feed, public/guest share links, non-Google SSO, per-user timezones, pending pre-account invitations, organizations/multi-tenancy.

# TaskFlow Design Review — Consolidated Report (2026-06-15)

*Source: 50 independent reviewers (18 slices, 8 ADRs, 20 cross-cutting dimensions, 4 constitution lenses) + 2 synthesizers. 256 findings — 33 blocker, 149 major, 65 minor, 9 nit; 81 constitution candidates. Read-only audit of the spec/architecture layer (no code exists yet).*

## 1. Executive summary

TaskFlow is a well-structured, traceability-conscious spec set whose **slice-level decomposition and provenance discipline are genuinely strong**, but it carries a coherent set of cross-cutting defects that will cause builders to ship the wrong thing if left unresolved. The dominant theme by a wide margin is **authorization-model incoherence**: "owner" means two different things, Cycle and Label have no ownership anchor, and the Tier A (ownership) vs Tier B (membership) tiers are specified as a conjunction rather than a dispatch — colliding across the domain model, sharing, search, and real-time. Two secondary themes are nearly as load-bearing: **real-time/undo/delete reconciliation** (undo "fully restores" vs last-write-wins, WebSocket proxying, mid-session access revocation) and **missing cross-cutting primitives** every slice silently assumes (timezone rule, error-response contract, session policy, secrets handling, invitation mechanics). None are deep architectural failures — they are decisions deferred into ambiguity. Resolve the ~15 clusters below (mostly by amending ADR-0003/0004/0005/0006/0007 and adding 3–4 constitution clauses) and the design is buildable.

## 2. Top blocker / major clusters

- **B1 — "Owner" is two incompatible concepts** [~5 reviewers]: immutable `Project.ownerId` (isolation basis) vs promotable owner role (FR-062). A project can lose its owner; transfer/unshare/delete authority undefined. → `ownerId` = single immutable owner with unshare/delete/transfer authority; roles are editor/viewer only; ownership moves via explicit transfer; forbid last-owner removal/demotion/leave. Align slice-007, ADR-0003/0005/0006.
- **B2 — Cycle & Label have no ownership/visibility anchor** [~6]: ADR-0005 claims "every entity has an anchor" but these don't; the single-active-cycle DB index is globally scoped; slices 006/013 contradict on label scope. → Label gets `ownerId` (entity Tier A; *applying* a label follows the task's tier). Cycle: either team-wide with explicit authz (keep global index) or `ownerId` + `UNIQUE(owner_id) WHERE is_active`. Amend ADR-0003 first.
- **B3 — Tier A/B is a conjunction, not a dispatch** [~6]: `createdBy` may confer residual access after a user leaves/removed; slices 005/012/015 mislabel themselves Tier A while surfacing shared data. **Real authz-bypass hazard.** → Authorization selected by containing-project visibility; `createdBy` is provenance only; leave/remove/unshare revokes ALL access; add negative test.
- **B4 — Comment author-only edit/delete** [~3]: FR-075 can't be expressed in the two-tier role model; ADR-0005 "no resource-level ACLs" contradicts it. → Add authorship as a distinct object-level grant (not overridden by role); add deny test.
- **B5 — Mid-session revocation doesn't evict SignalR subscriptions** [~3]: removed member keeps receiving live fan-out. **Live data leak.** → `MembershipRevoked`/`ProjectUnshared` evict connections / re-check at push; force 403 re-sync.
- **B6 — Notification dereference not re-authorized** [~4]: stale notifications leak now-inaccessible data. → Re-check at click time; no embedded content beyond current authz; "no longer available" on denied.
- **B7 — Undo "fully restores" collides with last-write-wins** [~5]: FR-040 vs FR-077 give opposite rules; slice-014 claims "no new entity" yet must persist snapshots. → Undo restore = normal optimistic write under LWW; specify persistence (tombstone/UndoEntry); define who may undo, concurrency rule, cascade/rollover variants.
- **B8 — Hard-delete in slice 002 forecloses slice-014 undo** [blocker]: → soft-delete from day one (`deleted_at`, filtered under isolation).
- **B9 — Next.js can't proxy WebSocket upgrades** [repeated error]: stated SignalR path won't work. → Caddy reverse-proxies the hub path directly to the api container.
- **B10 — No timezone / day-boundary rule anywhere** [~5, all cc]: "Today", "next 7 days", cycle boundaries, recurrence, NL parsing all undefined; parser client-vs-server contradiction; date-vs-datetime granularity. → One canonical rule (UTC store + reference zone); pin parser contract; define `due_date` granularity.
- **B11 — FR-049 "actionable recovery" claimed everywhere, no error contract exists** [~4]: → API-contract ADR adopting ProblemDetails (RFC 9457) + error-code enum + repo-wide Error-UX table; slice 002 owns optimistic-rollback UX.
- **B12 — Session/CSRF/OAuth hardening unspecified** [~3, blockers]: no session lifetime, rotation, SameSite, CSRF, `state`/`nonce`/PKCE/id-token validation, BFF→API trust carrier. → Amend ADR-0004 + slice-001 FRs.
- **B13 — Invitation mechanics unspecified** [~3, blockers]: how an invitee is identified / must they have an account. → Invite by email resolved against existing Users; reject unknown with FR-049 message; pre-account invites out of scope.
- **B14 — Deployment hardening: secrets, rollback, backups** [~4]: secrets unspecified; no deploy rollback; backups deploy-triggered only (contradicts VII "restorable"). → Secrets via env/Docker secrets; immutable git-SHA tags + expand/contract migrations + runbook; scheduled+offsite+restore-tested backups; Compose healthchecks.
- **B15 — Recurrence chain dies after one spawn; client-spawn breaks server-authoritative** [~3]: FR-008 omits the recurrence rule from carry-forward; FR-009 "client startup" is single-user-desktop thinking. → Carry the rule forward; re-spec FR-009 as a server-side Wolverine scheduled job; add idempotency + undo reconciliation + re-validate assignees against membership.

## 3. Needs reinterpretation
Owner semantics (B1), Tier A/B (B3), undo-vs-LWW (B7), parser location & date granularity (B10); slice-010 "group by cycle" before cycles exist (split AS-07); real-time reconciliation granularity (whole-DTO LWW vs field-level merge); cycle authorization model; export scope under sharing (a viewer could exfiltrate others' PII — redefine SC-005 as owner-scoped); self-action notification suppression needs `actorUserId` on the event envelope.

## 4. Needs more information
Overdue tasks' home (slice-005); "next cycle" ordering + planned→active transition (011); monthly recurrence parameters + due-date-optional (012); project hierarchy lifecycle / re-parent / edit / unarchive (004); assignee-must-be-member enforcement + `TaskAssigned` contract (008); "changed" notification trigger set + slice-017 has no Edge Cases section; comment body constraints / XSS / @mention format (009); "open/visible shared view" definition + echo suppression (016); account deletion / erasure (absent everywhere — add path or explicit OOS); search architecture client-vs-server + authz-filtered 50ms (013); "10,000 tasks" per-user vs per-team anchor.

## 5. Notable enhancements
ARIA-live for real-time updates & toasts (Principle II wrongly "unchanged"); OpenAPI ownership across 18 slices + SignalR payload typing + regenerate-and-diff CI gate; demote RabbitMQ to Wolverine durable local queues (both ADRs self-flag it as YAGNI); ADR-0001 AMENDED banner; SC-013 needs a handler registry + CI allow/deny coverage check + role×operation deny matrix; a "Security by Default" baseline + append-only audit of authz-changing events. **Praised:** the FR-057 personal/shared split (model traceability example); collaboration features are correctly sized; slice 018 appropriately light.

## 6. By-area heatmap
🔴 Weakest: ADR-0003 (domain), ADR-0005 (authz), ADR-0004 (identity), slice-014 (undo), slice-007/ADR-0006 (sharing), slice-011/cycles.
🟠 Needs work: slice-016/ADR-0007 (real-time), slice-017/ADR-0008 (notifications), slice-012 (recurring), slice-015 (export), slices 003/005 (dates/planning), cross-cutting primitives, ADR-0002 (deployment).
🟡 Localized gaps: slices 002, 006, 004, 001, 008, 009, 010, 013.
🟢 Solid: slice-018 (theming), traceability/provenance, scope/YAGNI.

**Bottom line:** fixing B1–B3 (the ownership/authorization core) plus four constitution-level primitives (timezone, error contract, session/secrets, account-deletion stance) clears the majority of blockers and de-risks ~2/3 of the major findings — most slice-level defects are downstream symptoms of those same unresolved decisions.

---

# Constitution — Proposed Changes

Filtered to genuinely cross-cutting, not-already-covered, constitution-altitude items.

## ADD
- **A1 — New Principle X · Time & Timezone** (MINOR): UTC storage; all date-relative computation (Today/Upcoming, cycles, recurrence, NL parsing) evaluated against a single documented instance reference timezone (recommend `Europe/Warsaw`), identical client/server; DST via library; per-user TZ out of scope. *Product decision: fixed zone vs per-user.*
- **A2 — New Principle XI · Privacy & Personal Data** (MINOR): account-deletion/erasure path with a defined cascade (transfer/delete owned projects, anonymize authored comments to a tombstone, null/reassign refs, purge notifications); same attribution rule on leave/remove/unshare; explicit data-retention stance for backups/soft-deleted/comments/notifications.
- **A3 — New Principle XII · Security by Default** (MINOR): untrusted user content (markdown, comments, @mentions) output-encoded/sanitized; CSP + security headers in prod; secrets injected at runtime, never committed/baked/logged.

## STRENGTHEN
- **S1 — Principle IX** (MINOR, disambiguation): authorization selected by containing-project visibility (dispatch, not conjunction); `createdBy` is provenance only (no residual grant; leave/remove/unshare revokes all); authorship = distinct object-level grant for own comments (role does not override); authorization governs live SignalR subscriptions (revoke on membership change).
- **S2 — Principle IX · Sessions & Admission** (MINOR, or **MAJOR if it reverses open sign-up**): session absolute + idle lifetime, new id at OAuth completion, server-side sign-out invalidation, explicit SameSite + CSRF; OAuth `state`/`nonce`/PKCE + id_token validation; **admission control** — gate account creation to an allowlist / Google Workspace `hd` rather than any Google account. *Product decision: admission gate (MAJOR) vs keep open sign-up + rate-limiting (MINOR).*
- **S3 — Principle VII** (MINOR): scheduled + offsite + restore-tested backups (or explicit accepted-risk waiver); deploy rollback (prior pinned image) + expand/contract migrations; undo restore = normal write under LWW, surfaced not silent; destructive non-undoable actions show blast radius.
- **S4 — Principle II** (MINOR): ARIA-live (polite, coalesced, no focus-steal) for real-time updates + toasts; standard focus contract for confirmation/command-palette dialogs. (Update Sync Impact so II is no longer "unchanged.")
- **S5 — Performance Standards & III** (PATCH/MINOR): make the two unverifiable MUSTs measurable (p95 concurrency budget; fan-out p95 commit-to-paint < 1000 ms); anchor "10,000 tasks" to the per-user authz-scoped working set; search <50 ms is authz-filtered; resolve the SHOULD-vs-"hard budget" 200 ms contradiction (recommend MUST).

## REJECT (belongs in an ADR/slice, not the constitution)
Optimistic-concurrency token → ADR-0007/0003; error-response contract (ProblemDetails) → new API-contract ADR (+ 1-line VI note); canonical OpenAPI doc + SignalR DTO gen → API-contract ADR; theme/preferences persistence → ADR-0003; Cycle/Label ownership specifics → ADR-0003 (+ slice 006); single-owner vs role → ADR-0003/0006; `actorUserId` envelope → ADR-0008; RabbitMQ→Wolverine-local-queues → ADR-0001/0002 + Architecture wording; per-slice connectivity/rollback UX → slices 016/002; empty/first-run state → optional 1-liner in IV; SC-013 coverage mechanism → Governance + plan/tasks templates; export PII scope → covered by XI + S1, contract in slice 015.

## Version bump
A1, A2, A3, S1, S3, S4 = MINOR; S5 = PATCH/MINOR. **S2 is the pivot:** admission gate reverses "open sign-up" → MAJOR. Net: **v3.1.0 if open sign-up stays; v4.0.0 if the admission gate is adopted.**

## Two product decisions needing ratification
1. **Timezone scope** — recommend fixed `Europe/Warsaw`.
2. **Admission model** — recommend allowlist / Google Workspace `hd` gate (accepting the MAJOR bump).

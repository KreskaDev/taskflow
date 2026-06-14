# Re-foundation plan — collaborative multi-user TaskFlow

**Status:** Proposed (awaiting approval to execute)
**Date:** 2026-06-14
**Driver:** pivot from single-user to a collaborative, multi-user web app (~10 users).

This is a product expansion, not just an amendment: it reverses the project's hardest scope
boundary (single-user) and **adds new requirements**. Execution is via reviewed workflows.

## 1. Decision ledger (locked)

| Area | Decision |
|---|---|
| Tenancy | Collaborative, single shared instance, ~10 users |
| Sharing | Personal + shared hybrid; a personal project can be converted to shared |
| Roles | Per shared project: **owner** (manage members, delete), **editor** (change tasks), **viewer** (read-only) |
| Assignment | **Multiple assignees** per task; shared projects only; "assigned to me" |
| Comments | Task **comment threads + @mentions** (new Comment entity) |
| Real-time | **SignalR** live updates on shared views; last-write-wins reconciliation |
| Notifications | **In-app center + live SignalR toasts**; triggers: assigned, mentioned, changes to items you're involved in; **no email** |
| Auth | **Google OAuth only**, **open sign-up**, **cookie-based sessions** (HttpOnly) via the single-origin web BFF |
| Access control | Authorization enforced on **every read and write** (ownership + membership + role) |
| Out (still) | email notifications, presence indicators, activity/audit feed, external guest links, SSO beyond Google, public share links |

Carried over from prior ADRs: monorepo; Next.js hybrid; C# + Wolverine + EF Core + Postgres;
full tactical DDD; event-driven cross-aggregate consistency; UUIDv7; pre-action-snapshot undo;
state-stored; RabbitMQ; Docker/Compose on Hetzner; host Caddy.

## 2. Domain additions

**New bounded context: Identity & Access.**

**New entities:**
- **ENT-06 User** — identity (Google subject), display name, avatar, email.
- **ENT-07 ProjectMembership** — user × project × role (owner/editor/viewer).
- **ENT-08 Comment** — author, task, body, @mentions, timestamps.
- **ENT-09 Notification** — recipient, type (assigned/mentioned/changed), source ref, read flag.

**Amended entities:**
- **ENT-01 Task** — add `createdBy` (User) and `assignees` (Users, many-to-many; shared only).
- **ENT-02 Project** — add `ownerId` (User) and `visibility` (personal | shared).

**New domain decisions (for the ADR rewrite):**
- Permission checks live in an **application-layer authorization policy** (per command/query),
  backed by ProjectMembership; aggregates stay focused on domain invariants.
- **Caddy `basic_auth` is removed** — replaced by real app auth; Caddy keeps TLS + proxy.
- Notifications are produced by **domain-event handlers** (`TaskAssigned`, `UserMentioned`,
  `TaskChanged`) via Wolverine — no sagas.

## 3. Product-vision expansion

**New user stories (collaboration epic):**
- **US-11 — Account & OAuth sign-in** (Google sign up/in/out, profile)
- **US-12 — Project sharing, membership & roles** (convert to shared, invite, owner/editor/viewer, remove/leave)
- **US-13 — Task assignment** (multi-assignee, reassign, "assigned to me")
- **US-14 — Comments & @mentions** (task comment thread, mention a member)
- **US-15 — Real-time collaboration** (shared views update live via SignalR)
- **US-16 — Notifications** (in-app center + live toasts; mark read; preferences)

**New functional requirements (~33):**
- Auth FR-052..056 · Sharing/membership/roles FR-057..064 · Access control FR-065..068 ·
  Assignment FR-069..071 · Comments/mentions FR-072..075 · Real-time FR-076..078 ·
  Notifications FR-079..084. (Exact text drafted during execution; numbering continues from FR-051.)

**New success criteria:** SC-013 (authorization enforced on 100% of data operations),
SC-014 (real-time propagation budget across members), SC-015 (~10 concurrent users without
degradation). New assumptions ASM-10 (~10-user team), ASM-11 (Google sole IdP).

**Out-of-scope handling (preserve ID stability):** mark **OOS-01** (collaboration) and
**OOS-06** (notifications) as *promoted to in-scope* (→ US-12 / US-16) rather than deleting
the IDs; add **OOS-13..OOS-17** for the new exclusions (email, presence, activity feed,
guest links, non-Google SSO).

## 4. Constitution → v3.0.0 (MAJOR)

- **Replace** "Single-User Only" constraint with **"Collaborative, Multi-User"**: accounts,
  per-user ownership, project membership, and **authorization on every read/write**. Ownership
  fields (ownerId, createdBy, assignees, membership) are now **required**, not forbidden.
- **Add Principle: Authentication & Authorization** — Google OAuth sign-in; least-privilege
  role checks on every operation; deny-by-default.
- **Amend Principle V** (Connected, Server-Authoritative) — carve out the **OAuth IdP** as a
  permitted external runtime dependency for authentication (still no third-party *data* services).
- **Restore real-time + notifications** in Architecture & Stack (SignalR; in-app notifications);
  remove their YAGNI/OOS exclusions.
- **Performance**: add ~10 concurrent users + real-time fan-out budget.
- **Architecture & Stack**: add Identity & Access context, SignalR, authorization layer;
  replace Caddy `basic_auth` gate with app auth (Caddy keeps TLS/proxy).

## 5. ADR program

- **ADR-0004 — Identity & Access**: Google OAuth, cookie sessions via BFF, User model, open sign-up.
- **ADR-0005 — Authorization**: roles (owner/editor/viewer), policy-based enforcement, deny-by-default.
- **ADR-0006 — Sharing model**: personal/shared visibility, membership, convert-to-shared, invitations.
- **ADR-0007 — Real-time**: SignalR hub, shared-view groups, last-write-wins reconciliation.
- **ADR-0008 — Notifications**: in-app center, event-driven generation, live toasts, no email.
- **Rewrite ADR-0003** — add User/Membership/Assignment/Comment/Notification, ownership/visibility,
  permission checks; keep the 5 prior decisions.

## 6. Re-slicing (12 → ~18 slices)

Auth becomes foundational; existing feature slices become **access-aware** (queries filtered by
ownership/membership, writes role-checked). Proposed order:

| New # | Slice | Origin |
|---|---|---|
| 001 | accounts-and-auth | **NEW** (foundational) |
| 002 | task-capture | re-scope (was 001; +createdBy, per-user) |
| 003 | natural-language-dates | was 002 |
| 004 | project-management | re-scope (was 003; +ownerId, visibility=personal) |
| 005 | daily-planning | was 004 |
| 006 | labels | was 005 |
| 007 | project-sharing-membership | **NEW** (convert→shared, invite, roles, access control) |
| 008 | task-assignment | **NEW** (multi-assignee, assigned-to-me) |
| 009 | comments-mentions | **NEW** |
| 010 | project-board-kanban | re-scope (was 006) |
| 011 | cycles | was 007 |
| 012 | recurring-tasks | was 008 |
| 013 | command-palette-search | re-scope (was 009; access-scoped results) |
| 014 | undo | was 010 |
| 015 | data-export-import | re-scope (was 011; per-user/access-aware) |
| 016 | real-time-collaboration | **NEW** (SignalR) |
| 017 | notifications | **NEW** |
| 018 | appearance-theming | was 012 |

**New cross-cutting concern:** access control / authorization applies to every data-touching
slice from 007 onward (and per-user isolation from 002). This joins the existing UI + resilience
cross-cutting sets in each slice's Provenance.

> Folder renames: existing `specs/00N-*` are renumbered. Done as git moves to preserve history;
> Provenance/`feature.json` updated accordingly.

## 7. Execution sequence (workflows)

1. **Constitution v3.0.0** — via `/speckit-constitution` (main loop, governed path).
2. **Workflow A — Vision + ADRs**: amend product-vision (US-11..16, FR-052..084, SC/ASM/OOS
   changes) and draft ADR-0004..0008 + rewrite ADR-0003; each artifact generated then
   adversarially reviewed, iterate-to-95.
3. **Workflow B — Re-slice**: renumber/move existing slices, generate the 6 new slices, and
   patch all slices to be access-aware vs the amended vision + v3.0.0; reviewer + iterate-to-95
   per slice (as in the prior two runs).
4. Update `feature.json`; commit; push for review.

## 8. Risks & tensions (surfaced)

- **OAuth vs Principle V** — external IdP at runtime; resolved by the v3.0.0 carve-out (auth only,
  not data).
- **Scope growth** — collaboration roughly doubles the FR count and adds 6 slices; biggest change
  in the project.
- **Authorization everywhere** — every query/command must be access-checked; easy to miss a path
  (mitigated by deny-by-default + tests; SC-013).
- **Real-time conflict handling** — concurrent edits on shared tasks; last-write-wins chosen for
  simplicity (revisit if it bites).
- **Comments** — net-new feature surface (entity, US, FRs, UI).
- **Renumbering slices** — touches every spec folder; done as tracked git moves.

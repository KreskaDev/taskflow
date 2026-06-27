# Quickstart & Validation Guide: Project Sharing, Membership & Roles (slice 007)

Validates the **US-12 Project Sharing** journey (AS-01..AS-06) and the **shared-project authorization branch** (Principle IX — the role × operation deny matrix, the last-owner guard, revoke-all on leave/remove/unshare, and `personal ↔ shared` reversibility) end-to-end, against the real stack. The **"unassign from tasks"** half of AS-04/AS-05/AS-06 is owned by slice 008 (`Task.assignees` does not exist yet) — this slice validates the **access-revocation** half (a removed member is denied) and confirms the `MembershipRevoked`/`ProjectUnshared` events are raised (research R13). References `spec.md`, `data-model.md`, `research.md`, and `contracts/openapi.yaml` rather than duplicating them.

> This is a run/validation guide — not implementation. Bodies, migrations, and full test suites belong in `tasks.md` and the implementation phase.

## Prerequisites

- The slice-002/003/004 stack boots locally (see the local-run & E2E runbook): PostgreSQL + the C# API on `:4311` (for `gen:api`), the Next.js web app, and the fake-IdP/BFF for **at least three** authenticated, admitted callers — an owner **A** and two members **B**, **C**, plus a non-member **X** (ASM-13 admission).
- `AddProjectMemberships` migration applied. **FR-051 (research R15): confirm the backup-before-migrate hook ran and the CI restore-test gate is green** before relying on the migration — this is the first schema change since slice 004.
- Typed client regenerated: `cd apps/web && pnpm gen:api` (API on `:4311`) → `schema.d.ts` gains the members operations, `MembershipRole` (`editor|viewer`) and `EffectiveRole` (`owner|editor|viewer`), `MemberResponse`, `ProjectResponse.role`, and `visibility="shared"`; `pnpm typecheck` green. **No new `errorCode`** — `ERROR_UX` stays exhaustive with no change (R16).

## Setup

```bash
# Backend (from repo root)
dotnet build
dotnet test                       # xUnit unit + Testcontainers-Postgres integration (incl. authz allow+deny AND the role×operation deny matrix)

# Web (from apps/web)
pnpm install
pnpm gen:api                      # API must be on :4311
pnpm test                         # Vitest unit/component
pnpm e2e                          # Playwright (self-boots its own stack)
```

## Validation scenarios

### A. Share & invite (US-12.AS-01)

| # | Action | Expected |
|---|---|---|
| AS-01 (share) | As owner **A**, open the share control on a personal project and confirm | `visibility` flips to **shared** (the first writable `shared` value); the project shows a shared indicator; A's effective `role` is `owner`. A freshly shared project has **zero** members (R3) |
| AS-01 (invite) | As **A**, invite **B** by email at role **editor**, confirm | B becomes an editor member; B can now read the project and its tasks at editor access. The roster (`GET /api/projects/{id}/members`) lists A (`owner`, `isOwner=true`) ∪ B (`editor`) — **no emails shown** (R17) |
| Invite viewer | As **A**, invite **C** by email at role **viewer** | C becomes a viewer member (read-only) |

### B. The role matrix (US-12.AS-02 / AS-03 — Principle IX, the centre)

| # | Action | Expected |
|---|---|---|
| AS-03 (viewer-denied-write) | As viewer **C**, attempt to modify a task in the shared project | **403 `forbidden`** — read-only; "you have viewer access; ask the owner for editor" (FR-067/FR-049). *(The task-write handler that consumes this on shared projects arrives with slice 008; the policy contract + deny test are established here — data-model §3.)* |
| editor-allowed | As editor **B**, read the project + its tasks | Allowed (viewer+ can read; editor+ can write) |
| AS-02 (change role) | As owner **A**, change **C** from viewer to **editor**, confirm | C's effective role becomes editor; C may now write. Re-sending the current role is a no-op + version bump, not an error (R5) |
| non-member-denied-read | As non-member **X**, read the shared project / its tasks / its members | **404 `not_found`** — existence NOT disclosed across the membership boundary (R9). Never 403 |
| editor-denied-manage | As editor **B**, attempt to invite / change-role / remove / unshare / transfer | **403 `forbidden`** — manage operations require `owner` (R9) |

### C. Transfer ownership (FR-094)

| # | Action | Expected |
|---|---|---|
| transfer | As owner **A**, transfer ownership to current member **B**, confirm | `ownerId` moves to **B**; **B's** membership row is removed (the owner has no row); **A** is demoted to a new **editor** row; `OwnerTransferred` raised (R6). The blast-radius preview states "you become an editor" |
| transfer-to-non-member | As **A**, attempt to transfer to non-member **X** | **422 `validation_failed`** — the target must already be a current member (R6) |

### D. Last-owner guard (FR-061 / FR-094 — recoverable)

| # | Action | Expected |
|---|---|---|
| owner-cannot-leave | As the owner, attempt to **leave** the project | **422 `last_owner`** — "transfer ownership to another member first" (FR-049 recoverable); checked **before** any row lookup (the owner has no row, R7) |
| owner-cannot-be-removed | As owner **A**, attempt to **remove** the owner (target == `ownerId`) | **422 `last_owner`** (not a misleading 404) |
| owner-cannot-be-demoted | As owner **A**, attempt to **change-role** on the owner | **422 `last_owner`** |

### E. Remove / leave revoke ALL access (US-12.AS-04 / AS-05 — FR-066)

| # | Action | Expected |
|---|---|---|
| AS-04 (remove) | As owner **A**, remove member **B** (after confirmation, blast radius shown) | B's row is deleted; B **loses ALL access immediately** — B's next read of the project/its tasks/its members → **404** (removed-member-loses-access, R10). `MembershipRevoked` raised. *(Unassign-from-tasks is the slice-008 seam.)* |
| AS-05 (leave) | As non-owner member **C**, leave the project (after confirmation) | C's own row is deleted; C loses ALL access → C's next read → **404** (R10). `MembershipRevoked` raised |
| provenance ≠ access | A removed editor who **created** a task in the project attempts to read it | **404** — `createdBy` is provenance only, confers no access (FR-066, R10) |

### F. Unshare & reversibility (US-12.AS-06 — FR-058/FR-059)

| # | Action | Expected |
|---|---|---|
| AS-06 (unshare) | As owner **A**, unshare the shared project (after confirmation, blast radius shows which members lose access) | `visibility` flips back to **personal**; **all** non-owner membership rows are removed in the same transaction; every former member loses ALL access (→ 404); `ProjectUnshared` raised. The **owner is retained** and the project's **tasks are preserved** (R3) |
| reversibility | After unshare, the project is personal again; re-share it | Round-trips cleanly to `shared` with **zero** members (a deliberate fresh invite flow — no membership tombstones, R3); tasks untouched throughout |

### G. Authorization coverage (Constitution VIII + IX — every handler; SC-013 / SC-016)

| Case | Expected |
|---|---|
| Each data handler (share/unshare/invite/change-role/remove/leave/transfer/list-members) | Ships **both** an **allow** AND a **deny** integration test through the real DB (SC-013) |
| The **role × operation deny matrix** (SC-016) | viewer-denied-write (403), editor-denied-manage (403), non-member-denied-read (404), removed-member-loses-access (404), last-owner-guard (422) — each asserted as a first-class test (data-model §3) |
| Any request without a valid session | **401 `unauthenticated`** |
| Governance | Authorization changes reviewed by a **non-author** before merge (governance gate); the deny matrix is the mechanically-verifiable artifact |

## Server-validation table (trust boundary)

| Input | Expected |
|---|---|
| Invite an email with **no admitted User** | **422 `validation_failed`** on `email` — "ask them to sign in once first" (OOS-18, R4); **no** pending record created |
| Invite the **owner's** own email, or an **already-existing member** | **422 `validation_failed`** ("already the owner" / "already a member — change their role instead", R4) |
| Invite/change-role with `role` outside `{editor, viewer}` (e.g. `owner`) | Rejected at the boundary — `owner` is **not a representable** `MembershipRole` (R2) |
| Transfer to a **non-member** or the **current owner** | **422 `validation_failed`** (R6) |
| Leave / remove / change-role targeting the **owner** | **422 `last_owner`** (R7) |
| Non-member targets any shared-project operation | **404 `not_found`** (existence not disclosed, R9) |
| Member with insufficient role (viewer→write, editor→manage) | **403 `forbidden`** (R9) |
| Stale `version` on any sharing/membership mutation | **409 `version_conflict`** (the Project version guards the whole sharing state, R11) |

## Cross-cutting checks

- **A11y (Principle II)**: the share, members-roster, invite, change-role, transfer, remove, and leave dialogs follow the dialog focus contract (initial focus, focus trap, Esc, return focus — FR-101); role badges never carry meaning by color alone (role text + icon accompany, FR-044); no hover-only affordances (FR-046); `prefers-reduced-motion` respected (FR-047); single-key shortcuts suppressed in the **invite-email** input (FR-031); no AT-binding collision (FR-045). Membership/access-change toasts announce via the polite ARIA-live region **without stealing focus** (FR-101).
- **Confirmation, not undo (FR-064)**: invite, role-change, transfer, remove, leave, and unshare each require explicit confirmation and are **NOT** under the 30-second undo; the confirmation dialog states its **blast radius** (which members lose access, which assignments will be cleared). There is **no undo toast** — the web mutations are non-optimistic (invalidate-on-settle), distinct from the slice-002/004 optimistic recipe (R12).
- **Server-authoritative (Principle V/FR-068)**: `ProjectResponse.role` drives client-side UI gating (a viewer sees read-only controls) but is **never** the security boundary — disable the UI gate and confirm the server still returns 403/404. The client holds no authoritative copy of membership.
- **Privacy (Principle XI/XII)**: the members roster surfaces `displayName` + `role`, **never** member emails; invite-by-email is the *input* path only. Structured rejection logs carry `ErrorCode`/`Method`/`Path` only — never the email, displayName, or owner (FR-050). The unknown/duplicate/self invite cases share one 422 field-error shape — no enumeration oracle beyond "not an admitted member" (R4).
- **Migration (Principle VII)**: confirm no unexpected migration beyond `AddProjectMemberships`; confirm **no DDL on `projects`** (the `shared` value was already a legal column value — only now written); confirm the backup-before-migrate + restore-test gate executed and is green (FR-051, R15).
- **Forward seams (named, not built)**: confirm the membership mutations **raise** `MembershipRevoked`/`ProjectUnshared`/`OwnerTransferred` (the authority signal) — the assignment-clearing handler (slice 008), real-time eviction (FR-095, slice 016), and notifications (slice 017) subscribe later with no change to this slice (R13/R14).

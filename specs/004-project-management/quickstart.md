# Quickstart & Validation Guide: Project Management (slice 004)

Validates the **US-10 Project Management** journey (AS-01..AS-05, AS-07..AS-11 + EC-03) and the **US-08.AS-05** `M` move-to-project mechanic end-to-end, against the real stack. **AS-06** (find an archived project via the *command palette*) is owned by slice 013 — this slice validates only the minimal archived-disclosure bridge that makes unarchive (AS-11) reachable (research R8). References `spec.md`, `data-model.md`, `research.md`, and `contracts/openapi.yaml` rather than duplicating them.

> This is a run/validation guide — not implementation. Bodies, migrations, and full test suites belong in `tasks.md` and the implementation phase.

## Prerequisites

- The slice-002/003 stack boots locally (see the local-run & E2E runbook): PostgreSQL + the C# API on `:4311` (for `gen:api`), the Next.js web app, and the fake-IdP/BFF for an authenticated, admitted caller.
- `AddProjects` migration applied. **FR-051 (research R14): confirm the backup-before-migrate hook ran and the CI restore-test gate is green** before relying on the migration — this is the first schema change since slice 002.
- Typed client regenerated: `cd apps/web && pnpm gen:api` (API on `:4311`) → `schema.d.ts` gains `ProjectResponse`, the project operations, `TaskResponse.projectId`, and the `*Disposition` enums; `pnpm typecheck` green.

## Setup

```bash
# Backend (from repo root)
dotnet build
dotnet test                       # xUnit unit + Testcontainers-Postgres integration (incl. authz allow+deny)

# Web (from apps/web)
pnpm install
pnpm gen:api                      # API must be on :4311
pnpm test                         # Vitest unit/component
pnpm e2e                          # Playwright (self-boots its own stack)
```

## Validation scenarios

### A. Project lifecycle (US-10)

| # | Action | Expected |
|---|---|---|
| AS-01 | Open the create-project action (command palette / dedicated action), fill name + pick a preset color + preset icon, optional parent | Form shows exactly those fields; on save the project appears in the sidebar (<16 ms optimistic paint, SC-003) |
| AS-02 | Create a child under an existing top-level parent | Child appears nested under the parent (one level) |
| AS-03 | Try to set a **child** as a new project's parent | Rejected with the one-level-nesting message; no project created |
| AS-07 | Edit an existing project's name / color / icon / parent and save | Changes persist; sidebar + project view reflect them |
| AS-08 | Re-parent a top-level project under another **top-level** project | Becomes a child; still one level |
| AS-09 | Re-parent under a target that is already a child, **or** re-parent a project that has its own children | Rejected 422 with the one-level-nesting field message (FR-049 recoverable); re-parent does not apply |
| AS-05 | Archive a project | Disappears from the sidebar and default project list |
| AS-11 (AS-06 bridge) | Open the "Archived projects" disclosure (research R8 — the slice-004 bridge; the full command-palette *search* of AS-06 is slice 013), find the archived project, unarchive it | Appears in the archived listing; unarchive restores it to default views. A child whose parent is still archived returns as **top-level** (AS-11/R9) |

### B. Delete & archive dispositions (FR-014 / EC-03 / AS-10)

| # | Action | Expected |
|---|---|---|
| EC-03 | Delete a project **that has tasks** | Dialog offers three task dispositions: **cascade** / **move to Inbox** / **archive with tasks**; the chosen one is applied; dialog states blast radius (task count) |
| AS-10 (delete) | Delete a **parent that has child projects** | Dialog also prompts a child disposition: **cascade** / **orphan-to-top** (promote children to top-level); applied as chosen; blast radius shown |
| AS-10 (archive) | Archive a **parent that has child projects** | Same child-disposition prompt; applied as chosen |
| — | After cascade-delete | Tasks/children are tombstoned (soft-delete); excluded from all views. (The 30-second **undo UI** is slice 014 — not present here.) |

### C. Inbox & move-to-project (FR-021 / US-08.AS-05)

| # | Action | Expected |
|---|---|---|
| Inbox | View the default task list (`GET /api/tasks`) | Shows only **unprojected** tasks (project_id IS NULL), newest first. All pre-slice-004 tasks still appear (backward-compatible, R6) |
| AS-05 (`M`) | Select a task, press **`M`**, choose a target project | Project selector appears (dialog focus contract); task moves to the project and **leaves the Inbox** (<16 ms optimistic paint) |
| `M` → Inbox | Press `M` on a projected task, choose "Inbox" (null target) | Task returns to the Inbox |
| Project view | Open a project (`GET /api/projects/{id}/tasks`) | Shows that project's tasks only |

### D. Authorization (Constitution VIII + IX — every handler)

| Case | Expected |
|---|---|
| Caller lists/edits/archives/deletes **their own** project | Allowed |
| Caller targets **another user's** project id (create-under, edit, archive, delete, list-tasks, `M`-into) | **404 not_found** (existence not disclosed) — both an allow and a deny test exist per handler |
| Any request without a valid session | **401 unauthenticated** |

## Server-validation table (trust boundary)

| Input | Expected |
|---|---|
| `parentId` referencing a project that is itself a child | 422 `validation_failed` (one-level nesting, R3) |
| Re-parent a project that has children | 422 `validation_failed` (one-level nesting, R3) |
| `color` / `icon` outside the preset set | 422 `validation_failed` (R10) |
| Empty / >200-char `name` | 422 `validation_failed` |
| Stale `version` on edit/archive/unarchive/delete/move | 409 `version_conflict` |
| Delete a project with tasks and omit `taskDisposition` | 422 `validation_failed` |
| Replay `createProject` with the same id | Existing project returned UNCHANGED (idempotent, FR-001 parity) |

## Cross-cutting checks

- **A11y (Principle II)**: the create/edit form, project selector (`M`), delete-with-tasks dialog, and child-disposition prompt follow the dialog focus contract (initial focus, focus trap, Esc, return focus); color is never the sole carrier of meaning (icon + text accompany it, FR-044); no hover-only affordances (FR-046); `prefers-reduced-motion` respected (FR-047); `M` and other single-key shortcuts suppressed in the name input (FR-031); no AT-binding collision (FR-045).
- **Instant response (SC-003)**: project create, `M` move, and archive paint optimistically <16 ms; the one-level-nesting prevention message is computed client-side (R15) so it shows without a round-trip; server reconciles/rolls back.
- **Security (Principle XII)**: project `name` is React-escaped on render (FR-099); color/icon are preset-constrained (R10); structured rejection logs carry `ErrorCode`/`Method`/`Path` only — never the name or owner (FR-050).
- **Migration (Principle VII)**: confirm no unexpected migration beyond `AddProjects`; confirm the backup-before-migrate + restore-test gate executed (FR-051, R14).

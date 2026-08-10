# Data Model: UI Design System & Full UI Operability (slice 019)

**Date**: 2026-08-09 | **Spec**: `spec.md` | **Research**: `research.md`

## Persisted entities

**None introduced or modified.** ENT-01 (Task), ENT-02 (Project), ENT-04 (Label),
ENT-06 (User), ENT-07 (ProjectMembership), ENT-08 (Comment) are rendered by the
redesigned surfaces with behavior unchanged. No EF Core migration ships in this slice
(FR-051 not triggered).

## New read model: ViewCounts (FR-109; clarified 2026-08-09)

A per-caller, authorization-scoped aggregate served by `GET /api/views/counts`
(contract: `contracts/view-counts.md`). Not persisted — computed from the caller's
accessible task set at request time.

| Field | Type | Definition (count of INCOMPLETE tasks the view would list) |
|---|---|---|
| `inbox` | int ≥ 0 | caller-owned tasks with no project (FR-021 scope) |
| `today` | int ≥ 0 | due today **+ overdue** (FR-022 scope + overdue clause) |
| `upcoming` | int ≥ 0 | due within the Upcoming window (next 7 days, FR-023 scope) |
| `assigned` | int ≥ 0 | incomplete tasks assigned to the caller (any accessible project) |
| `projects[]` | array | one entry per project the caller can access |
| `projects[].projectId` | uuid | project identifier |
| `projects[].count` | int ≥ 0 | incomplete tasks in that project |

Rules:
- **Incomplete** = status ∉ {done, cancelled} — identical predicate to the view queries
  the counts summarize; a count MUST equal the length of the corresponding view listing
  filtered to incomplete (asserted by integration test).
- **Date boundaries** evaluated in `Europe/Warsaw` (Principle X), same computation as
  the existing today/upcoming queries; date-only vs date-time via `has_time` unchanged.
- **Authorization** (FR-065/FR-068): inbox/today/upcoming/assigned slices scoped to the
  caller (ownership for unprojected, membership for projected); `projects[]` contains
  ONLY projects with current membership (or personal ownership). Deny-by-default at the
  handler; allow + deny integration tests required. Archived projects are excluded
  (they are not sidebar entries).

Invalidation (client): the `['views','counts']` query is invalidated wherever task
mutations already invalidate the view-key family (`onSettled` in the mutation factories),
so counts track optimistic flows without new plumbing.

## New command: DuplicateTask (FR-112; UIT-041)

Served by `POST /api/tasks/{id}/duplicate` (contract: `contracts/task-duplicate.md`).
Creates a NEW ENT-01 row; no schema change.

| Aspect | Rule |
|---|---|
| Identity | `newTaskId` supplied by the client (idempotency + optimistic paint with known id — same idiom as `PUT /api/tasks/{id}` create). Replay with the same id is a no-op success. |
| Copied | title, description, priority, due date (incl. `has_time`), labels, project assignment, assignees |
| Assignee filter | copied assignees are re-validated against CURRENT project membership; non-members are silently dropped (mirrors the FR-008 carry-forward precedent); no notifications re-fired |
| NOT copied | status/completion (duplicate starts per FR-003 default), `completed_at`, comments, timestamps (fresh `created_at`/`updated_at`) |
| Position | fractional index directly AFTER the source row (between source and successor) — the duplicate appears adjacent to its source (D7) |
| Context | same context as source: same project, or Inbox if unprojected |
| Authorization | same scoping as task creation in that context (FR-065 ownership for Inbox; membership+role for shared projects; FR-068 deny-by-default). Deny tests: non-member, viewer-role member, cross-user Inbox task. |

## UI-layer state (not persisted, documented for contract completeness)

| State | Home | Notes |
|---|---|---|
| Theme palette | composite class on `<html>` (`dark-cool` \| `dark-warm` \| `light-cool` \| `light-warm`) | hard-defaulted to `dark-cool` server-side; no persistence until slice 018 (contract: `contracts/ui-theme.md`) |
| Drawer open task | URL query `?task=<taskId>` on the current listing route | deep-linkable, back/forward-safe (S4.3); invalid/inaccessible id renders the drawer's error state with recovery (FR-049) |
| Sidebar collapsed | client state (component) | not persisted this slice; `aria-expanded` reflects state (UIT-023) |

## State transitions

None added. Task status transitions (FR-003), reorder position writes (FR-102), and
comment lifecycle (slice 009) are unchanged; the duplicate starts at the FR-003 default
status exactly like a created task.

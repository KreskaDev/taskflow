# TaskFlow

A keyboard-first, **collaborative multi-user** task manager for a small team (~10) — combining Todoist's simplicity with Linear's speed and aesthetics.

This repository holds the **spec-driven product definition** for the TaskFlow MVP. There is no application code yet; the work here is the specification + architecture layer, organized as sequential, independently shippable vertical slices.

## Product at a glance

A connected web app: members sign in with Google, keep private personal projects, and collaborate on shared projects with owner/editor/viewer roles, multiple assignees, comments + @mentions, real-time updates, and in-app notifications. Authorization is enforced on every read and write (deny-by-default).

## Structure

```
.specify/
  memory/
    constitution.md            # Project constitution v3.0.0 (9 principles)
    product-vision.md          # Canonical source of truth — the full MVP, with stable IDs
    archive/                   # Read-only historical snapshot of the initial monolithic draft
  templates/                   # Spec / plan / tasks templates
  feature.json                 # Active feature pointer
specs/
  001-accounts-and-auth/         # Google OAuth sign-in, sessions, profile, deny-by-default
  002-task-capture/              # Keyboard capture into a per-user task list
  003-natural-language-dates/    # Polish natural-language due dates
  004-project-management/        # Projects, nesting, archive, Inbox; ownership + personal visibility
  005-daily-planning/            # Today/Upcoming, priorities, full editor
  006-labels/                    # Reusable labels
  007-project-sharing-membership/# Share/unshare, invite, owner/editor/viewer roles
  008-task-assignment/           # Multiple assignees, "assigned to me"
  009-comments-mentions/         # Task comments + @mentions (viewers read-only)
  010-project-board-kanban/      # Kanban board (access-scoped)
  011-cycles/                    # 2-week cycles
  012-recurring-tasks/           # Recurrence rules (carry assignees forward)
  013-command-palette-search/    # Ctrl+K palette & search (access-scoped)
  014-undo/                      # 30s undo for destructive data actions
  015-data-export-import/        # JSON/CSV export, import with mapping
  016-real-time-collaboration/   # SignalR live updates on shared views
  017-notifications/             # In-app notification center + live toasts
  018-appearance-theming/        # Dark/light theming
docs/
  architecture/                  # ADR-0001..0008 (stack, deployment, domain, identity,
                                 #   authorization, sharing, real-time, notifications)
  plans/                         # Re-foundation program notes
  blog-orchestration-token-economics.md   # Notes on the multi-agent workflow used to build these specs
```

## How it's organized

- **`product-vision.md` is the single source of truth.** It carries the full MVP (16 user stories, 84 functional requirements, 12 edge cases, 15 success criteria, 9 entities, 11 assumptions, 17 out-of-scope items) under stable IDs.
- **Each slice in `specs/`** is an independently shippable increment whose `spec.md` opens with a **Provenance** section — a pure-ID trace anchor back to `product-vision.md` — followed by the full requirement text for the IDs that slice realizes. Reading order equals dependency order (auth is foundational).
- **Cross-cutting requirements** are realized in every slice to which they apply: accessibility (FR-031, FR-042–047), resilience (FR-049–051), and **access control** — per-user isolation (FR-065) plus, for shared projects, membership + role checks (FR-066–068). The full out-of-scope boundary (OOS-01..17) is confirmed in each slice.

## Architecture

Monorepo. Next.js + TypeScript (hybrid RSC + client islands, optimistic UI) talking over a typed REST/OpenAPI contract to a C# / ASP.NET Core backend (full tactical DDD: Task Management + Identity & Access contexts; Wolverine + RabbitMQ; EF Core + PostgreSQL; SignalR for real-time). Google OAuth with HttpOnly cookie sessions via the web BFF. Containerized with Docker Compose on a single Hetzner VPS behind host Caddy (TLS + reverse proxy). See `docs/architecture/adr-0001..0008`.

## Status

Specification + architecture phase. The constitution (v3.0.0), product vision, 18 slice specs, and ADRs are complete; implementation has not started.

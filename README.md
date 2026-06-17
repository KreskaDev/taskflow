# TaskFlow

A keyboard-first, **collaborative multi-user** task manager for a small team (~10) — combining Todoist's simplicity with Linear's speed and aesthetics.

This monorepo holds both the **spec-driven product definition** for the TaskFlow MVP and the **application code**, organized as sequential, independently shippable vertical slices. Implementation has begun: **slice 001 (Accounts & Auth)** is built and green.

## Product at a glance

A connected web app: members sign in with Google, keep private personal projects, and collaborate on shared projects with owner/editor/viewer roles, multiple assignees, comments + @mentions, real-time updates, and in-app notifications. Account creation is gated by admission control (email allowlist and/or Google Workspace hosted-domain). Authorization is enforced on every read and write (deny-by-default).

## Tech stack

- **Frontend / BFF** — Next.js 15 (App Router, RSC + client islands), React 19, TypeScript 5.7, TanStack Query, Zod. The Next.js layer also acts as the Backend-for-Frontend: it owns Google OAuth, HttpOnly cookie sessions (Postgres-backed session store via `pg`), and proxies to the API over an internal-only origin.
- **Backend API** — C# / ASP.NET Core on .NET 9, tactical DDD (Identity & Access context today; Task Management to follow), Wolverine (in-process messaging + transactional outbox), EF Core + Npgsql.
- **Database** — PostgreSQL 17 (application data for the API; session store for the BFF).
- **Contract** — typed REST over OpenAPI; the TypeScript client is generated from the live `/openapi/v1.json` and CI fails on drift.
- **Deployment** — Docker Compose on a single VPS behind host Caddy (TLS + reverse proxy). The API port is bound to `127.0.0.1` and never publicly reachable; the BFF is the only public surface.

> Forward-looking: the MVP scope also includes RabbitMQ (cross-context messaging) and SignalR (real-time). Those are designed in the ADRs but are not part of the slice-001 runtime — the production compose stack today is `postgres + api + web` only.

## Repository layout

```
apps/
  api/                         # .NET 9 ASP.NET Core API (TaskFlow.sln)
    src/
      TaskFlow.Api/            # HTTP endpoints, auth, middleware, Program.cs (auto-migrates on boot)
      TaskFlow.Application/    # use-case handlers (IdentityAccess), authorization
      TaskFlow.Domain/         # domain model (IdentityAccess, Common)
      TaskFlow.Infrastructure/ # EF Core persistence + Migrations (InitialCreate seeds the tombstone user)
    tests/
      TaskFlow.UnitTests/         # xUnit domain unit tests
      TaskFlow.IntegrationTests/  # xUnit + Testcontainers (real Postgres) integration tests
  web/                         # Next.js 15 BFF + UI (pnpm workspace package @taskflow/web)
    src/                       # app/, components/, hooks/, lib/, middleware.ts
    sql/001-sessions.sql       # BFF session table (created on startup)
    tests/                     # e2e/ (Playwright specs) + unit/ (vitest specs)
    vitest.config.ts           # unit tests (vitest + Testing Library)
    playwright.config.ts       # e2e config (contains clearly-labelled TEST-ONLY fixtures)
docker/
  Dockerfile.api               # multi-stage build for the API
  Dockerfile.web               # multi-stage build for the Next.js standalone bundle
  docker-compose.yml           # production stack: postgres + api + web (API internal-only)
  docker-compose.dev.yml       # dev overrides: build from source, expose Postgres on :5432
scripts/
  backup.sh                    # pg_dump custom-format backup (Constitution VII / FR-051)
  restore-test.sh              # restore a dump into a throwaway DB and assert integrity
Caddyfile                      # host reverse-proxy (TLS, /hub/* -> API, everything else -> BFF)
.env.example                   # environment template (NO real secrets — runtime-injected, FR-100)
global.json                    # pins the .NET SDK (9.0.315, latestFeature roll-forward)
pnpm-workspace.yaml            # pnpm workspace (apps/web only; the .NET API is built with dotnet)
.github/workflows/ci.yml       # CI: web quality, api build+test, OpenAPI drift gate, build/push, deploy
.specify/
  memory/
    constitution.md            # Project constitution v4.0.0 (12 principles)
    product-vision.md          # Canonical source of truth — the full MVP, with stable IDs
    archive/                   # Read-only historical snapshot of the initial monolithic draft
  templates/                   # Spec / plan / tasks templates
  feature.json                 # Active feature pointer
specs/
  001-accounts-and-auth/         # Google OAuth sign-in, sessions, profile, deny-by-default (BUILT)
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
```

## How the specs are organized

- **`product-vision.md` is the single source of truth.** It carries the full MVP under stable IDs.
- **Each slice in `specs/`** is an independently shippable increment whose `spec.md` opens with a **Provenance** section — a pure-ID trace anchor back to `product-vision.md` — followed by the full requirement text for the IDs that slice realizes. Reading order equals dependency order (auth is foundational).
- **Cross-cutting requirements** are realized in every slice to which they apply: accessibility (FR-031, FR-042–047), resilience (FR-049–051), and **access control** — per-user isolation (FR-065) plus, for shared projects, membership + role checks (FR-066–068). The full out-of-scope boundary (OOS-01..17) is confirmed in each slice.

The project is governed by `.specify/memory/constitution.md` (v4.0.0, 12 principles) — keyboard-first, WCAG 2.1 AA accessibility, instant response, minimalist UI, server-authoritative, end-to-end type safety, data integrity & resilience, test-first, authn/authz, time & timezone, privacy, and security-by-default.

## Prerequisites

- Node.js 22 LTS + pnpm 9.x (`corepack enable`)
- .NET 9 SDK (pinned by `global.json` — 9.0.315)
- Docker + Docker Compose (the API integration tests use Testcontainers, which need a running Docker daemon)
- A Google Cloud project with OAuth 2.0 credentials (Web application) for live sign-in

## Environment

Copy `.env.example` to `.env` and fill in real values. **Never commit a populated `.env`** — secrets are runtime-injected (FR-100), and `.env` is git-ignored.

Key variables (see `.env.example` for the full annotated list):

| Variable | Purpose |
|---|---|
| `POSTGRES_USER` / `POSTGRES_PASSWORD` / `POSTGRES_DB` | Postgres credentials |
| `ConnectionStrings__Postgres` | API (EF Core / Npgsql) connection string (maps to config key `ConnectionStrings:postgres`) |
| `DATABASE_URL` | BFF session store (node-postgres) connection string |
| `GOOGLE_CLIENT_ID` / `GOOGLE_CLIENT_SECRET` | Google OAuth (sign-in only) |
| `JWT_SIGNING_KEY` | Shared HMAC key the BFF signs the short-lived BFF→API identity carrier with, and the API validates (config key `Jwt:SigningKey`) |
| `SESSION_SECRET` | Key securing the BFF session-cookie machinery |
| `ADMISSION_EMAILS` / `ADMISSION_HD` | Admission control — at least one must be set or the BFF fails fast at startup (FR-087) |
| `APP_URL` | Public origin (default `http://localhost:3000`) |
| `API_INTERNAL_URL` | Internal API base URL the BFF proxies to (default `http://localhost:4311`) |
| `SESSION_ABSOLUTE_LIFETIME_HOURS` / `SESSION_IDLE_TIMEOUT_HOURS` | Session policy (defaults 168h / 24h) |

Google OAuth redirect URI for local dev: `http://localhost:3000/api/auth/callback`.

## Run locally

```bash
# 1. Infrastructure — Postgres (dev override exposes it on :5432 for host tooling)
docker compose -f docker/docker-compose.yml -f docker/docker-compose.dev.yml up -d postgres

# 2. Backend API — applies EF Core migrations on startup (auto-migrate in Program.cs)
cd apps/api && dotnet run --project src/TaskFlow.Api

# 3. Frontend / BFF — creates the session table on startup
cd apps/web && pnpm install && pnpm dev
```

To run the full stack from source in containers:

```bash
docker compose -f docker/docker-compose.yml -f docker/docker-compose.dev.yml up --build
```

## Test suites

```bash
# Backend — xUnit unit + integration (integration uses Testcontainers; needs Docker)
cd apps/api && dotnet test
#   -> 14 unit + 26 integration, 0-warning strict build (analyzers-as-errors)

# Frontend — vitest unit (jsdom + Testing Library)
cd apps/web && pnpm test

# Frontend — typecheck + lint gates
cd apps/web && pnpm typecheck && pnpm lint

# Frontend — production build
cd apps/web && pnpm build

# End-to-end — Playwright (boots the app per playwright.config.ts)
cd apps/web && pnpm e2e
```

CI (`.github/workflows/ci.yml`) runs the web quality gates (lint/typecheck/unit), the API build+test, and an **OpenAPI drift gate** that regenerates the typed client from the live API contract and fails on any diff. On `main`, it then builds + pushes images to GHCR and deploys over SSH.

## OpenAPI client regeneration

```bash
# With the API running on :4311
cd apps/web && pnpm gen:api
```

This fetches `/openapi/v1.json` from the running API and regenerates the TypeScript types in `src/lib/api/generated/`. CI fails if the committed client drifts from the live contract.

## Backups & restore verification

Per Constitution VII / FR-051, backups are taken before every migration and "restorable" is proven by an actual restore:

```bash
# Create a custom-format pg_dump (echoes the dump path on stdout)
DUMP=$(./scripts/backup.sh)

# Restore that dump into a throwaway DB and assert schema integrity
./scripts/restore-test.sh "$DUMP"
```

Both scripts read connection details from `DATABASE_URL` (or the `POSTGRES_*` variables) and require the standard Postgres client tools (`pg_dump`, `pg_restore`, `psql`, `createdb`, `dropdb`). The CI deploy job runs `backup.sh` → migrate → `restore-test.sh` on the latest dump as a pre-cutover gate.

## Status

Slice 001 (**Accounts & Auth**) is implemented and green: Google OAuth sign-in via the BFF, HttpOnly cookie sessions, profile display, deny-by-default authorization, and account deletion (hard-delete + tombstone). The remaining slices (002–018) are specified and queued. The constitution (v4.0.0), product vision, 18 slice specs, and ADRs are complete.

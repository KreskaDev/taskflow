# Implementation Plan: Accounts & Auth

**Branch**: `001-accounts-and-auth` | **Date**: 2026-06-15 | **Spec**: `specs/001-accounts-and-auth/spec.md`

**Input**: Feature specification from `specs/001-accounts-and-auth/spec.md`

## Summary

Bootstraps the TaskFlow monorepo and implements the foundational authentication + authorization layer: Google OAuth sign-in with an admission gate (email allowlist / Workspace `hd`), Postgres-backed HttpOnly cookie sessions managed by the Next.js BFF, a signed JWT identity carrier from BFF to API, deny-by-default per-user isolation, account deletion with an erasure cascade, and the full operational stack (Docker Compose, Caddy, CI/CD, backup infrastructure). Every later slice builds on the identity, session, authorization policy, OpenAPI pipeline, and deployment primitives established here.

## Technical Context

**Language/Version**: TypeScript (strict) on Node.js 22 LTS / Next.js 15; C# 13 on .NET 9 / ASP.NET Core

**Primary Dependencies**:
- Frontend: Next.js 15 (App Router), React 19, TanStack Query v5, Zod 3, openapi-fetch + openapi-typescript, jose (JWT), pg (session store), date-fns + date-fns-tz (timezone)
- Backend: ASP.NET Core 9, Wolverine + Wolverine.Http + Wolverine.EntityFrameworkCore, Npgsql.EntityFrameworkCore.PostgreSQL, Microsoft.AspNetCore.OpenApi, FluentValidation

**Storage**: PostgreSQL 17 (EF Core code-first migrations for domain; BFF session table via `pg`)

**Testing**: xUnit + Testcontainers-Postgres (backend), Vitest (frontend unit), Playwright (E2E)

**Target Platform**: Linux containers (Docker Compose) on Hetzner VPS; host Caddy for TLS

**Project Type**: Web application (monorepo: Next.js frontend + ASP.NET Core API)

**Performance Goals**: Optimistic paint <16ms; server mutations p95 <200ms; ~10 concurrent users

**Constraints**: Single Hetzner VPS; deny-by-default authorization on every operation; ~10 admitted users

**Scale/Scope**: ~10 users, 18 slices total; this slice establishes all foundational infrastructure

## Constitution Check

*GATE: Passes for v4.0.0. No violations.*

| # | Principle | Status | How Addressed |
|---|---|---|---|
| I | Keyboard-First | PASS | Sign-in, sign-out, profile, deletion operable via keyboard; FR-031 suppresses single-key shortcuts in text inputs |
| II | Accessibility | PASS | FR-042-047 on all surfaces; FR-101 dialog focus contract on account-deletion dialog; ARIA-live for auth error toasts |
| III | Instant Response | PASS | OAuth redirect is inherently network-bound (skeleton permitted); profile/workspace use optimistic UI where applicable |
| IV | Minimalist UI | PASS | Empty workspace on first sign-in; no onboarding wizard, tooltips-on-first-run, or modal interruptions |
| V | Connected, Server-Auth | PASS | Google OAuth is the sole external runtime dep (auth only); User + Session in Postgres via own API |
| VI | Type Safety | PASS | TS strict + no `any`; C# nullable + analyzers-as-errors; OpenAPI-generated client; EF migrations as schema source of truth |
| VII | Data Integrity | PASS | Backup-before-migrate + scheduled + offsite + restore-test infrastructure; FR-049 error messages; account deletion cascade |
| VIII | Test-First | PASS | Red-Green-Refactor; every handler ships allow + deny authorization test (SC-016) |
| IX | Authn/Authz | PASS | Core of this slice: OAuth + admission gate + PKCE/state/nonce + HttpOnly sessions + signed BFF-to-API carrier + deny-by-default dispatch |
| X | Time & Timezone | PASS | UTC storage (`timestamptz`); Europe/Warsaw reference timezone utility established |
| XI | Privacy | PASS | Account deletion + erasure cascade (FR-085); explicit data-retention stance (FR-086) |
| XII | Security | PASS | CSP + security headers baseline; output-encoding of profile fields; secrets runtime-injected, never committed/logged |

## Project Structure

### Documentation (this feature)

```text
specs/001-accounts-and-auth/
├── plan.md              # This file
├── research.md          # Phase 0: technology decisions
├── data-model.md        # Phase 1: User + Session entities
├── quickstart.md        # Phase 1: validation guide
├── contracts/
│   └── openapi.yaml     # Phase 1: API contract (slice 001 endpoints)
└── tasks.md             # Phase 2 (/speckit-tasks — not yet created)
```

### Source Code (repository root)

```text
apps/
├── web/                                    # Next.js 15 (App Router, TS strict)
│   ├── src/
│   │   ├── app/
│   │   │   ├── layout.tsx                  # Root layout (providers, global styles)
│   │   │   ├── (auth)/
│   │   │   │   ├── layout.tsx              # Centered auth layout
│   │   │   │   └── signin/
│   │   │   │       └── page.tsx            # Sign-in page
│   │   │   ├── (app)/
│   │   │   │   ├── layout.tsx              # Authenticated app shell
│   │   │   │   ├── page.tsx                # Home (empty workspace, EC-01)
│   │   │   │   └── settings/
│   │   │   │       └── page.tsx            # Profile + account deletion
│   │   │   └── api/
│   │   │       ├── auth/
│   │   │       │   ├── signin/route.ts     # Initiate Google OAuth (PKCE, state, nonce)
│   │   │       │   ├── callback/route.ts   # OAuth callback: validate, admit, ensure user, create session
│   │   │       │   ├── signout/route.ts    # POST: invalidate session, clear cookie
│   │   │       │   └── session/route.ts    # GET: current session info for client
│   │   │       └── proxy/
│   │   │           └── [...path]/route.ts  # Authenticated proxy: session check -> mint JWT -> forward to API
│   │   ├── lib/
│   │   │   ├── api/
│   │   │   │   ├── client.ts              # openapi-fetch wrapper
│   │   │   │   └── generated/             # openapi-typescript output (committed, CI-gated)
│   │   │   ├── auth/
│   │   │   │   ├── session.ts             # Postgres session CRUD (pg)
│   │   │   │   ├── oauth.ts              # Google OAuth helpers (PKCE, state, nonce, id_token)
│   │   │   │   ├── admission.ts           # Allowlist / HD check
│   │   │   │   ├── csrf.ts               # Origin-header validation middleware
│   │   │   │   └── token.ts              # JWT minting for BFF-to-API carrier (jose)
│   │   │   └── timezone.ts               # Europe/Warsaw utility (date-fns-tz)
│   │   ├── components/
│   │   │   ├── ui/                        # Base UI primitives (button, dialog, toast)
│   │   │   └── auth/
│   │   │       ├── SignInButton.tsx
│   │   │       └── DeleteAccountDialog.tsx # FR-101 dialog focus contract
│   │   └── hooks/
│   │       └── useSession.ts              # Client-side session state
│   ├── tests/
│   │   ├── unit/                          # Vitest
│   │   └── e2e/
│   │       └── auth.spec.ts              # Playwright: sign-in, sign-out, deny, profile, deletion
│   ├── sql/
│   │   └── 001-sessions.sql              # Session table DDL (applied on startup)
│   ├── next.config.ts
│   ├── tsconfig.json
│   ├── vitest.config.ts
│   ├── playwright.config.ts
│   └── package.json
│
└── api/                                    # ASP.NET Core 9 (C#, DDD, Wolverine)
    ├── TaskFlow.sln
    ├── Directory.Build.props               # Nullable enable, TreatWarningsAsErrors, AnalysisLevel latest-all
    ├── src/
    │   ├── TaskFlow.Api/
    │   │   ├── TaskFlow.Api.csproj
    │   │   ├── Program.cs                  # Host: Wolverine, EF Core, JWT auth, OpenAPI, CORS (none—single origin)
    │   │   ├── appsettings.json
    │   │   ├── appsettings.Development.json
    │   │   ├── Endpoints/
    │   │   │   └── UserEndpoints.cs        # Wolverine.Http: GET /api/users/me, DELETE /api/users/me, POST /api/users/ensure
    │   │   └── Middleware/
    │   │       ├── ProblemDetailsMiddleware.cs   # RFC 9457 error envelope
    │   │       └── SecurityHeadersMiddleware.cs  # CSP + standard headers (FR-099)
    │   ├── TaskFlow.Domain/
    │   │   ├── TaskFlow.Domain.csproj
    │   │   ├── Common/
    │   │   │   ├── AggregateRoot.cs
    │   │   │   └── DomainEvent.cs
    │   │   └── IdentityAccess/
    │   │       ├── User.cs                 # ENT-06: aggregate root
    │   │       ├── UserId.cs               # Strongly-typed ID (UUIDv7)
    │   │       └── Events/
    │   │           └── AccountDeletionRequested.cs
    │   ├── TaskFlow.Application/
    │   │   ├── TaskFlow.Application.csproj
    │   │   ├── Authorization/
    │   │   │   ├── IResourceAuthorizationPolicy.cs  # Dispatch-by-visibility contract
    │   │   │   ├── ResourceAuthorizationPolicy.cs   # Deny-by-default implementation
    │   │   │   └── AuthorizationMiddleware.cs        # Wolverine pipeline middleware
    │   │   └── IdentityAccess/
    │   │       ├── Commands/
    │   │       │   ├── EnsureUser.cs       # Command + handler: create or match user
    │   │       │   └── DeleteAccount.cs    # Command + handler: hard-delete row + cascade event
    │   │       └── Queries/
    │   │           └── GetCurrentUser.cs   # Query + handler: return profile
    │   └── TaskFlow.Infrastructure/
    │       ├── TaskFlow.Infrastructure.csproj
    │       └── Persistence/
    │           ├── AppDbContext.cs
    │           ├── Configurations/
    │           │   └── UserConfiguration.cs  # EF Core entity config + tombstone seed
    │           └── Migrations/              # EF Core code-first migrations
    └── tests/
        ├── TaskFlow.UnitTests/
        │   ├── TaskFlow.UnitTests.csproj
        │   └── Domain/
        │       └── IdentityAccess/
        │           └── UserTests.cs         # Aggregate invariants
        └── TaskFlow.IntegrationTests/
            ├── TaskFlow.IntegrationTests.csproj
            ├── Infrastructure/
            │   ├── IntegrationTestBase.cs   # WebApplicationFactory + Testcontainers-Postgres
            │   └── TestJwtHelper.cs         # Mint test JWTs for allow/deny tests
            └── IdentityAccess/
                ├── EnsureUserTests.cs       # allow + deny
                ├── GetCurrentUserTests.cs   # allow + deny
                └── DeleteAccountTests.cs    # allow + deny

docker/
├── docker-compose.yml                      # Production: postgres, api, web
├── docker-compose.dev.yml                  # Dev overrides (build from source, bind mounts)
├── Dockerfile.web                          # Multi-stage: deps -> build -> runtime
└── Dockerfile.api                          # Multi-stage: restore -> build -> publish -> runtime

.github/
└── workflows/
    └── ci.yml                              # lint/typecheck -> test -> openapi-sync -> build/push -> deploy

scripts/
├── backup.sh                              # pg_dump wrapper (pre-migration + scheduled)
└── restore-test.sh                        # Restore backup into throwaway DB, assert integrity

Caddyfile                                   # Host Caddy: TLS + proxy web(:3000) + hub(:4311)
.env.example                                # All env vars documented (no secret values)
.gitattributes                              # * text=auto eol=lf
.gitignore                                  # Updated for monorepo (.env, node_modules, bin/obj, etc.)
pnpm-workspace.yaml                         # packages: ['apps/web']
```

**Structure Decision**: Monorepo with `apps/web` (Next.js) and `apps/api` (ASP.NET Core) per ADR-0001. The .NET side uses a conventional 4-project DDD layout (Api, Domain, Application, Infrastructure) with namespace-level bounded-context separation (`IdentityAccess`, `TaskManagement`). Tests split into UnitTests (domain invariants) and IntegrationTests (handlers through Testcontainers-Postgres, with allow + deny authorization tests). Docker Compose runs three services; host Caddy terminates TLS. Session table managed by the BFF; domain tables by EF Core — both in the same Postgres database.

## Key Design Decisions

### Authentication Flow (ADR-0004)

```
Browser                    BFF (Next.js)                  API (.NET)             Google
  │                            │                              │                    │
  ├─ GET /auth/signin ────────►│                              │                    │
  │                            ├─ generate state,nonce,PKCE ──┤                    │
  │◄─ 302 → Google OAuth ─────┤                              │                    │
  │                            │                              │                    │
  ├─ complete OAuth ──────────────────────────────────────────────────────────────►│
  │◄─ 302 → /auth/callback ───────────────────────────────────────────────────────┤
  │                            │                              │                    │
  ├─ GET /auth/callback ──────►│                              │                    │
  │                            ├─ validate state,nonce,PKCE   │                    │
  │                            ├─ exchange code → tokens ─────────────────────────►│
  │                            │◄─ id_token + access_token ───────────────────────┤
  │                            ├─ validate id_token           │                    │
  │                            ├─ check admission (hd/email)  │                    │
  │                            ├─ POST /api/users/ensure ────►│                    │
  │                            │◄─ UserProfile ──────────────┤│                    │
  │                            ├─ create session (Postgres)   │                    │
  │◄─ Set-Cookie + 302 → / ───┤                              │                    │
```

### Authorization Policy (ADR-0005)

Deny-by-default, dispatch-by-visibility. Enforced via Wolverine pipeline middleware on every command/query handler:
- **Personal/unprojected data**: authorize on ownership (`userId == currentUser.Id`); queries scoped to caller
- **Shared-project data**: authorize on `ProjectMembership` + role (editor/viewer); owner is immutable `ownerId`
- In this slice: only the ownership branch is exercised (no shared projects yet)
- Every handler ships an allow test (valid user -> 200) and a deny test (no JWT / wrong user -> 401/403)

### BFF-to-API Proxy

The catch-all route at `app/api/proxy/[...path]/route.ts`:
1. Reads the `taskflow_session` cookie
2. Validates the session in Postgres (exists, not invalidated, not expired, not idle)
3. Touches `last_accessed_at`
4. Mints a 60-second HMAC-SHA256 JWT with `sub`, `email`, `name`
5. Forwards the request to `API_INTERNAL_URL` with `Authorization: Bearer <jwt>`
6. Returns the API response to the browser

### Error Contract (ADR-0009)

All non-2xx responses are RFC 9457 ProblemDetails with a stable `errorCode` enum. The generated TS client models these via Zod. Error-UX mapping:

| Error Code | HTTP | User-Facing Message |
|---|---|---|
| `unauthenticated` | 401 | Redirect to sign-in |
| `not_admitted` | 403 | "Your account is not authorized to access TaskFlow" |
| `forbidden` | 403 | "You don't have access to this resource" |
| `validation_failed` | 422 | Inline field-level messages |
| `internal_error` | 500 | "Something went wrong. Please try again." |

### Caddy Configuration

```caddyfile
taskflow.example.com {
    # SignalR hub — direct to API (WebSocket upgrade)
    handle /hub/* {
        reverse_proxy 127.0.0.1:4311
    }
    # Everything else — to Next.js
    handle {
        reverse_proxy 127.0.0.1:3000
    }
}
```

Host Caddy terminates TLS (auto Let's Encrypt). API port 4311 is bound to 127.0.0.1 only (not externally reachable).

### CI/CD Pipeline

```
push / PR
  └─► lint + typecheck (TS strict; C# nullable/analyzers-as-errors)
        └─► test (xUnit + Testcontainers; Vitest; Playwright)
              └─► openapi-sync (regenerate TS client, diff, fail on drift)
                    └─► build + push GHCR (immutable git-SHA tag)
                          └─► deploy (main only)
                                ├─ SSH to Hetzner
                                ├─ pg_dump backup
                                ├─ docker compose pull
                                ├─ EF Core migrations
                                ├─ restore-test (throwaway DB)
                                └─ docker compose up -d
```

Rollback: redeploy the prior SHA image; restore the pre-migration dump if schema changed.

## Complexity Tracking

No constitution violations. No entries needed.

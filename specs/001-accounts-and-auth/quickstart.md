# Quickstart: Accounts & Auth (001)

## Prerequisites

- Node.js 22 LTS + pnpm 9.x
- .NET 9 SDK
- Docker + Docker Compose
- Google Cloud project with OAuth 2.0 credentials (Web application type)

## Environment Setup

Copy `.env.example` to `.env` and fill in:

```env
POSTGRES_USER=taskflow
POSTGRES_PASSWORD=<generate>
GOOGLE_CLIENT_ID=<from-google-console>
GOOGLE_CLIENT_SECRET=<from-google-console>
JWT_SIGNING_KEY=<random-64-char-hex>
SESSION_SECRET=<random-64-char-hex>
ADMISSION_EMAILS=your@email.com
APP_URL=http://localhost:3000
API_INTERNAL_URL=http://localhost:4311
```

Google OAuth redirect URI: `http://localhost:3000/api/auth/callback`

## Start

```bash
# 1. Infrastructure
docker compose -f docker/docker-compose.yml -f docker/docker-compose.dev.yml up -d postgres

# 2. Backend (runs EF Core migrations on startup)
cd apps/api && dotnet run --project src/TaskFlow.Api

# 3. Frontend (creates session table on startup)
cd apps/web && pnpm install && pnpm dev
```

## Run Tests

```bash
# Backend unit + integration (Testcontainers — needs Docker)
cd apps/api && dotnet test

# Frontend unit
cd apps/web && pnpm test

# E2E (requires running app)
cd apps/web && pnpm e2e
```

## Validation Scenarios

Ref: `specs/001-accounts-and-auth/spec.md` acceptance scenarios.

| Scenario | Steps | Expected |
|---|---|---|
| **US-11.AS-01** Admitted sign-in | Open app -> "Sign in with Google" -> complete OAuth with admitted account | Account created, land in empty workspace |
| **US-11.AS-01** Non-admitted denied | Sign in with account NOT on allowlist/HD, or whose id_token `email_verified` is not true | Rejection message, no User created |
| **US-11.AS-02** Sign-out | Click sign-out | Session ended, protected routes inaccessible |
| **US-11.AS-03** Unauthenticated denied | Access protected route without session | Redirect to sign-in |
| **US-11.AS-04** Profile display | Open profile/settings while signed in | Google name + avatar shown |
| **US-17.AS-02** Account deletion | Settings -> Delete Account -> confirm dialog | Session ended, user record hard-deleted (no residual row); re-sign-in with the same Google identity creates a fresh empty account |
| **SC-016** Allow test | `GET /api/users/me` with valid JWT | 200 + profile |
| **SC-016** Deny test | `GET /api/users/me` without JWT | 401 |
| **SC-016** Deleted deny | `GET /api/users/me` with JWT for a hard-deleted (no-longer-existent) user | 401 |

## OpenAPI Client Regeneration

```bash
cd apps/web && pnpm gen:api
```

This fetches `/openapi/v1.json` from the running API, generates TypeScript types into `src/lib/api/generated/`, and diffs against the committed version. CI fails on drift.

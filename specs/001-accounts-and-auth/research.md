# Research: Accounts & Auth (001)

## R1: OAuth Implementation — Custom vs NextAuth.js

**Decision**: Custom OAuth implementation using Next.js Route Handlers.

**Rationale**: NextAuth.js abstracts session management with its own model (JWT-based or adapter-based). Our requirements demand Postgres-backed server-side sessions with absolute + idle lifetimes, session ID rotation at OAuth completion, server-side invalidation, and a signed JWT carrier to the API. NextAuth's adapter interface fights us on session rotation, idle timeout tracking, and the per-request JWT minting. A custom implementation gives precise control over the session lifecycle at ~200 lines of focused code.

**Alternatives considered**: NextAuth.js with custom Postgres adapter — rejected because session rotation, idle timeout, and JWT minting requirements demand more control than the adapter interface provides cleanly.

## R2: Session Store — pg vs ORM

**Decision**: Raw `pg` (node-postgres) for the session table.

**Rationale**: The BFF accesses exactly one table (sessions) with four operations (insert, read-by-id, update-last-accessed, invalidate). An ORM adds dependency weight for no benefit. The session table is managed separately from the .NET API's EF Core domain tables — the BFF creates it on startup via `CREATE TABLE IF NOT EXISTS`. A shared Postgres database keeps deployment simple.

**Alternatives considered**: Prisma/Drizzle — rejected (heavy dependency for one table; separate migration story conflicts with EF Core as schema source of truth for domain tables).

## R3: BFF-to-API Identity Carrier

**Decision**: HMAC-SHA256-signed JWT with 60-second expiry, minted per-request by the BFF.

**Rationale**: Standard, well-supported in both Node.js (`jose`) and .NET (`Microsoft.AspNetCore.Authentication.JwtBearer`). Short-lived enough that no revocation list is needed. Claims: `sub` (user ID), `email`, `name`, `iat`, `exp`. Shared signing key injected at runtime via `JWT_SIGNING_KEY` env var. The API validates the JWT on every request and extracts the authenticated principal.

**Alternatives considered**: Opaque token with API-side session lookup — rejected (adds a DB round-trip; JWT is self-contained). mTLS — rejected (overkill for single-VPS Docker network).

## R4: CSRF Protection

**Decision**: Origin header validation + SameSite=Lax cookie.

**Rationale**: `SameSite=Lax` prevents cross-site POST/PUT/DELETE from sending the session cookie. Origin header validation on state-changing requests provides defense-in-depth (Referer fallback for edge cases). Simpler than a double-submit token pattern and equally effective for a single-origin application.

**Alternatives considered**: Double-submit anti-CSRF token — rejected (unnecessary complexity when SameSite + Origin check are sufficient for a same-origin app).

## R5: OpenAPI Pipeline

**Decision**: `Microsoft.AspNetCore.OpenApi` (built-in .NET 9) generates the OpenAPI 3.1 document at `/openapi/v1.json`. `openapi-typescript` generates TypeScript types. `openapi-fetch` provides the type-safe runtime client.

**Rationale**: Built-in .NET 9 OpenAPI support is first-class, maintained, and integrates with Minimal API / Wolverine.Http endpoints. `openapi-typescript` + `openapi-fetch` are the lightest, most type-safe client generation stack — pure compile-time types + a thin fetch wrapper, no runtime codegen. CI gate: regenerate-and-diff.

**Alternatives considered**: Swashbuckle — deprecated in favor of built-in .NET 9 support. NSwag/Kiota — heavier than needed for this scope.

## R6: Wolverine Setup

**Decision**: Wolverine for command/query dispatch + transactional outbox; Wolverine.Http for endpoint routing; Wolverine.EntityFrameworkCore for persistence integration. Durable Postgres local queues as default transport.

**Rationale**: Wolverine's pipeline runs middleware (authorization) before every handler — a single enforcement point for deny-by-default. The transactional outbox ensures domain events (`AccountDeletionRequested`) are published reliably. Wolverine.Http maps HTTP endpoints directly to handlers, eliminating manual wiring. No RabbitMQ until a real cross-process consumer exists (per remediation decision).

**Alternatives considered**: MediatR + Minimal API — no built-in outbox or HTTP integration. Raw Minimal API — no pipeline middleware for authorization.

## R7: Admission Control

**Decision**: Environment-configured via `ADMISSION_EMAILS` (comma-separated) and/or `ADMISSION_HD` (Google Workspace hosted-domain `hd`). Checked in the BFF OAuth callback before User creation.

**Rationale**: Environment configuration keeps the allowlist out of the codebase and allows runtime changes via restart. The `hd` claim in Google's id_token identifies the Workspace domain. Either match (email in list OR `hd` matches) admits the user. Neither match returns a `not_admitted` ProblemDetails error. The check happens before the API is called — non-admitted users never create a User record.

## R8: Account Deletion Cascade

**Decision**: `AccountDeletionRequested` domain event dispatched via Wolverine outbox. In slice 001, only the User record is affected (soft-delete via `deleted_at` + all sessions invalidated). Later slices register handlers for their entities (projects: transfer/delete, comments: anonymize to tombstone, assignments: null, notifications: purge).

**Rationale**: Event-driven cascade is extensible — each slice owns its cleanup handler. The User's `deleted_at` is set immediately, sessions invalidated, and cascade handlers run asynchronously via the outbox. This is a deliberate, irreversible action (not covered by 30s undo).

## R9: Docker, Caddy, CI/CD

**Decision**:
- Docker Compose: `postgres` (17), `api`, `web` services
- Multi-stage Dockerfiles for minimal images
- Host-installed Caddy terminates TLS, proxies web (:3000) + SignalR hub (:4311)
- GitHub Actions: lint/typecheck -> test -> OpenAPI sync -> build/push GHCR (git-SHA tag) -> deploy via SSH
- Backup: `pg_dump` before migration + scheduled daily + restore-test in CI

**Rationale**: Per ADR-0002. Single VPS, no orchestrator. Caddy is host-installed for TLS cert management. Git-SHA tags ensure immutable, rollback-friendly images. Backup-before-migrate + restore-test satisfies Constitution VII.

## R10: Session Policy Values

**Decision**:
- Absolute lifetime: 7 days
- Idle timeout: 24 hours
- Cookie: `taskflow_session`, HttpOnly, Secure (production), SameSite=Lax, Path=/
- Session ID: UUIDv4 (cryptographically random), rotated at OAuth completion

**Rationale**: 7-day absolute lifetime balances security with UX. 24-hour idle timeout catches abandoned sessions. Configurable via `SESSION_ABSOLUTE_LIFETIME_HOURS` and `SESSION_IDLE_TIMEOUT_HOURS` env vars.

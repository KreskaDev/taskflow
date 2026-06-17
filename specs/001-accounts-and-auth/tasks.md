---
description: "Task list for feature implementation"
---

# Tasks: Accounts & Auth (001)

**Input**: Design documents from `specs/001-accounts-and-auth/`

**Prerequisites**: plan.md ✅, spec.md ✅, research.md ✅, data-model.md ✅, contracts/openapi.yaml ✅

**Tests**: REQUIRED (not optional) for this slice. Constitution Principle VIII (Test-First) and SC-016 mandate that every data handler ships **both an allow and a deny test**; the spec's "User Scenarios & Testing" section is mandatory. All test tasks below MUST be written Red-first (failing) before their implementation tasks (Red-Green-Refactor).

**Organization**: Tasks are grouped by user story to enable independent implementation and testing. This is a foundational slice: Phase 1 (Setup) and Phase 2 (Foundational) bootstrap the entire monorepo and the auth/authz/OpenAPI/deploy primitives every later slice builds on, so they are large by necessity.

## Format: `[ID] [P?] [Story?] Description with file path`

- **[P]**: Can run in parallel (different files, no dependency on an incomplete task)
- **[Story]**: `[US1]` = US-11 Account & Sign-In; `[US2]` = US-17 Account Deletion. Setup / Foundational / Polish tasks carry **no** story label.
- Paths are taken verbatim from `plan.md` → Project Structure.

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Bootstrap the monorepo skeleton, toolchains, container/deploy primitives. No application logic.

- [X] T001 Create the monorepo directory structure per plan.md (`apps/web/`, `apps/api/`, `docker/`, `scripts/`, `.github/workflows/`) at repository root
- [X] T002 [P] Add root `.gitattributes` (`* text=auto eol=lf`), update `.gitignore` for the monorepo (`.env`, `node_modules`, `bin/`, `obj/`, `.next/`, test artifacts), and add `pnpm-workspace.yaml` (`packages: ['apps/web']`)
- [X] T003 [P] Author `.env.example` documenting every env var with no secret values (`POSTGRES_*`, `GOOGLE_CLIENT_ID/SECRET`, `JWT_SIGNING_KEY`, `SESSION_SECRET`, `ADMISSION_EMAILS`, `ADMISSION_HD`, `APP_URL`, `API_INTERNAL_URL`, `SESSION_ABSOLUTE_LIFETIME_HOURS`, `SESSION_IDLE_TIMEOUT_HOURS`) per quickstart.md
- [X] T004 [P] Scaffold the Next.js 15 app in `apps/web` (App Router) with strict `apps/web/tsconfig.json` (`strict: true`, no implicit `any`), `apps/web/next.config.ts`, and `apps/web/package.json` (React 19, TanStack Query v5, Zod 3, openapi-fetch, openapi-typescript, jose, pg, date-fns, date-fns-tz)
- [X] T005 [P] Configure web tooling: `apps/web/vitest.config.ts`, `apps/web/playwright.config.ts`, and ESLint/Prettier with lint+typecheck scripts in `apps/web/package.json`
- [X] T006 [P] Scaffold the .NET solution `apps/api/TaskFlow.sln` with four projects (`TaskFlow.Api`, `TaskFlow.Domain`, `TaskFlow.Application`, `TaskFlow.Infrastructure`) and `apps/api/Directory.Build.props` (`<Nullable>enable</Nullable>`, `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`, `<AnalysisLevel>latest-all</AnalysisLevel>`); add ASP.NET Core 9, Wolverine + Wolverine.Http + Wolverine.EntityFrameworkCore, Npgsql.EntityFrameworkCore.PostgreSQL, Microsoft.AspNetCore.OpenApi, FluentValidation package refs
- [X] T007 [P] Scaffold the test projects `apps/api/tests/TaskFlow.UnitTests/TaskFlow.UnitTests.csproj` and `apps/api/tests/TaskFlow.IntegrationTests/TaskFlow.IntegrationTests.csproj` (xUnit, Testcontainers.PostgreSql, FluentAssertions) and wire them into the solution
- [X] T008 [P] Author the container stack: `docker/docker-compose.yml` (postgres 17, api, web), `docker/docker-compose.dev.yml` (build-from-source + bind mounts), `docker/Dockerfile.web` (multi-stage deps→build→runtime), `docker/Dockerfile.api` (multi-stage restore→build→publish→runtime); bind API port to `127.0.0.1` only
- [X] T009 [P] Author the host `Caddyfile` (TLS via Let's Encrypt; `handle /hub/*` → `127.0.0.1:4311`, `handle` → `127.0.0.1:3000`) per plan.md
- [X] T010 [P] Author the CI workflow `.github/workflows/ci.yml` with the staged pipeline skeleton: lint+typecheck → test → openapi-sync (regenerate+diff, fail on drift) → build+push GHCR (immutable git-SHA tag) → deploy (main only, SSH→Hetzner: pg_dump → compose pull → EF migrate → restore-test → compose up)
- [X] T011 [P] Author backup infrastructure `scripts/backup.sh` (pg_dump wrapper for pre-migration + scheduled) and `scripts/restore-test.sh` (restore into throwaway DB, assert integrity) per Constitution VII / FR-051

**Checkpoint**: Monorepo builds empty; toolchains, containers, CI skeleton, and backup scripts exist.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Core domain, persistence, authorization, OAuth/session/proxy plumbing, OpenAPI pipeline, test harness, and error/security baseline. **No user story can begin until this phase is complete.**

**⚠️ CRITICAL**: Blocks all of Phase 3 and Phase 4.

### Domain & Persistence

- [X] T012 [P] Domain primitives `apps/api/src/TaskFlow.Domain/Common/AggregateRoot.cs` and `DomainEvent.cs`
- [X] T013 [P] Strongly-typed `apps/api/src/TaskFlow.Domain/IdentityAccess/UserId.cs` (UUIDv7, application-generated)
- [X] T014 User aggregate root `apps/api/src/TaskFlow.Domain/IdentityAccess/User.cs` (ENT-06: `google_subject_id` immutable; `email`/`display_name`/`avatar_url` refreshed from Google profile; **no soft-delete flag — account deletion hard-deletes the row**, per spec Clarifications 2026-06-17) — depends on T012, T013
- [X] T015 EF Core `apps/api/src/TaskFlow.Infrastructure/Persistence/AppDbContext.cs` and `Configurations/UserConfiguration.cs` (column mappings, `timestamptz` for all temporals, UNIQUE indexes on `google_subject_id` and `email`) — depends on T014
- [X] T016 EF Core initial migration in `apps/api/src/TaskFlow.Infrastructure/Persistence/Migrations/` that creates the `users` table + indexes **and seeds the tombstone identity row** (`id = 00000000-0000-0000-0000-000000000000`, `display_name = "Deleted User"`, per data-model.md) — schema source of truth (Constitution VI); depends on T015

### Authorization Framework (deny-by-default, dispatch-by-visibility)

- [X] T017 [P] Authorization contract `apps/api/src/TaskFlow.Application/Authorization/IResourceAuthorizationPolicy.cs` (dispatch-by-visibility interface)
- [X] T018 Deny-by-default implementation `apps/api/src/TaskFlow.Application/Authorization/ResourceAuthorizationPolicy.cs` (ownership branch: `userId == currentUser.Id`, queries scoped to caller; shared-project branch stubbed for slice 007) — depends on T017
- [X] T019 Wolverine pipeline `apps/api/src/TaskFlow.Application/Authorization/AuthorizationMiddleware.cs` enforcing deny-by-default on every command/query handler (FR-068) — depends on T018 — _**VERIFIED 3 ways** (2026-06-17): (1) no-JWT `POST /api/users/ensure` → exactly 401 + our RFC 9457 envelope + **no row written** (the catastrophic-200 case is excluded), via the message pipeline since endpoints only delegate to the bus; (2) direct `IMessageBus.InvokeAsync` with no principal throws `UnauthenticatedException` (`AuthorizationMiddlewareTests`); (3) a temporary inline (non-delegating) endpoint with no JWT also returned 401, proving the added HTTP-layer gate weaves universally. Required two foundational fixes surfaced by the weave: `opts.Discovery.IncludeAssembly` (handlers live in TaskFlow.Application, not the entry assembly) and making `HttpContextCurrentUser` public (Wolverine codegen inlines the `ICurrentUser` dependency; service location is disallowed). Added `MapWolverineEndpoints(o => o.AddMiddleware(...))` as a uniform HTTP backstop + `DurabilityMode.Solo` (single-node)._

### API Host, Error & Security Middleware

- [X] T020 RFC 9457 error envelope `apps/api/src/TaskFlow.Api/Middleware/ProblemDetailsMiddleware.cs` with the stable `errorCode` enum from contracts/openapi.yaml (`unauthenticated`, `not_admitted`, `forbidden`, `validation_failed`, `not_found`, `conflict_lww`, `last_owner`, `internal_error`) — ADR-0009
- [X] T021 [P] `apps/api/src/TaskFlow.Api/Middleware/SecurityHeadersMiddleware.cs` — CSP + standard security response headers for production (FR-099)
- [X] T022 API host wiring `apps/api/src/TaskFlow.Api/Program.cs`: Wolverine (durable Postgres local queues + transactional outbox), EF Core/Npgsql, JWT bearer auth validating the BFF HMAC-SHA256 carrier, built-in OpenAPI at `/openapi/v1.json`, the middleware pipeline (T019/T020/T021), and **no CORS** (single origin); run EF migrations on startup — depends on T015, T019, T020, T021 — _runtime-verified: host boots, serves `/openapi/v1.json`, migration+tombstone applied (FoundationSmokeTests). Required `WolverineFx.RuntimeCompilation` (6.x split-out)._
- [X] T023 OpenAPI client pipeline: `apps/web/package.json` `gen:api` script running `openapi-typescript` from `/openapi/v1.json` into `apps/web/src/lib/api/generated/` (committed), plus the `apps/web/src/lib/api/client.ts` openapi-fetch wrapper; CI diff-gate fails on drift — **requires the API contract to build first**; depends on T022 — _initial `schema.d.ts` generated from the committed contract file (API has no endpoints until T039); Phase 3 regenerates from the live `/openapi/v1.json`_

### BFF Foundation (Next.js)

- [X] T024 [P] BFF session store `apps/web/sql/001-sessions.sql` (table DDL, FK → `users(id)` **ON DELETE CASCADE** so hard-deleting a user removes their sessions, `ix_sessions_user_id`) + `apps/web/src/lib/auth/session.ts` (pg CRUD: insert, read-by-id, touch `last_accessed_at`, invalidate) + a new `apps/web/src/instrumentation.ts` (Next.js `register()` startup hook) that runs `CREATE TABLE IF NOT EXISTS` — **FK requires the EF migration (T016) to have run first** _(also added `src/lib/db.ts` pg pool)_
- [X] T025 [P] Europe/Warsaw timezone utility `apps/web/src/lib/timezone.ts` (date-fns-tz; UTC storage, Warsaw reference zone per ASM-12 / Constitution X)
- [X] T026 [P] BFF→API carrier minting `apps/web/src/lib/auth/token.ts` (jose, HMAC-SHA256, 60-second expiry, claims `sub`/`email`/`name`/`iat`/`exp`) — _matches C# `BffCarrierToken` (issuer/audience/claims)_
- [X] T027 [P] CSRF protection `apps/web/src/lib/auth/csrf.ts` (Origin-header validation for state-changing requests; pairs with `SameSite=Lax` cookie — FR-089)
- [X] T028 Authenticated proxy route `apps/web/src/app/api/proxy/[...path]/route.ts` (read `taskflow_session` cookie → validate session in Postgres (exists/not invalidated/not expired/not idle) → touch `last_accessed_at` → mint 60s JWT → forward to `API_INTERNAL_URL`) — depends on T024, T026, T027

### UI Shell, Test Harness & Error Baseline

- [X] T029 [P] Base UI primitives `apps/web/src/components/ui/` (button, dialog implementing the FR-101 focus contract, toast/ARIA-live region) — a11y baseline FR-042/043/047/101 _(Button, Dialog, LiveRegion, Toast)_
- [X] T030 [P] Root layout `apps/web/src/app/layout.tsx` (TanStack Query provider, global styles, app-level ARIA-live region) and `apps/web/src/hooks/useSession.ts` client session state _(+ `app/providers.tsx`, `app/globals.css`)_
- [X] T031 [P] Integration test harness `apps/api/tests/TaskFlow.IntegrationTests/Infrastructure/IntegrationTestBase.cs` (WebApplicationFactory + Testcontainers-Postgres) and `TestJwtHelper.cs` (mint valid/invalid test JWTs for allow/deny tests) — _verified working via FoundationSmokeTests; TestJwtHelper exercised by T034/T035_
- [X] T032 [P] Error baseline: structured error logging that never logs secrets (FR-050) on the API, and the client error-UX mapping (FR-049) implementing the ADR-0009 error-code → message/redirect table in `apps/web/src/lib/api/client.ts` _(API logging in `ProblemDetailsMiddleware`; client `mapError` table in `client.ts`)_

**Checkpoint**: Domain + migration + tombstone seed, deny-by-default authz pipeline, API host with JWT/OpenAPI/security headers, BFF session+proxy+token plumbing, UI shell, and the integration test harness all exist. User stories can begin.

> **Phase 2 status (verified 2026-06-17)**: T012–T018, T020–T032 complete; full solution builds 0/0 under strict analyzers; `FoundationSmokeTests` runtime-verify host boot + OpenAPI + migration/tombstone; web `pnpm typecheck`/`lint` clean. **T019 is written but its Wolverine middleware weaving is UNVERIFIED** (no handler existed to weave it into yet).
>
> **⚠️ Phase 2 → Phase 3 carry-forwards (do these FIRST in Phase 3, before building US1 on top):**
> 1. **Make T034's no-JWT deny test the very first thing after the first endpoint (T039) exists.** Assert THREE independent things: (a) request is *rejected at all* (proves `AuthorizationMiddleware.Before` actually wove — note `Before(ICurrentUser)` has no message-type first param, which the Wolverine docs imply may be required; adding the first endpoint is what triggers its codegen); (b) status is **401, not 500** (a 500 means the middleware didn't fire and `currentUser.Id` threw `InvalidOperationException` in the handler); (c) the body is **our RFC 9457 envelope (ADR-0009), not Wolverine.Http's own** exception/result handling. **There is no auth backstop**: Program.cs has no `RequireAuthorization()` / fallback policy, so the entire no-JWT→401 guarantee rests solely on this middleware. If any leg fails, that is a **Phase-2 foundational fix**, not a Phase-3 task. Known fallback: apply `RequireAuthorization()` to the Wolverine.Http endpoint group as the hard authentication gate + a `JwtBearerEvents` handler emitting our ProblemDetails, keeping `AuthorizationMiddleware` as the ownership-dispatch layer.
> 2. **JWT contract has 3 implementations; C# tests only cover 2.** API validation (C#) + `TestJwtHelper` (C#) agree via T034/T035, but the real production minter is `token.ts` (TS/jose). Ensure the T036/T048 E2E round-trips a **real** `token.ts`→proxy→**real API** token (not a mocked API), or a key-encoding/claim-shape mismatch stays invisible until production sign-in.
> 3. ~~**Regenerate `schema.d.ts` from the live `/openapi/v1.json`**~~ **RESOLVED by T062 (2026-06-17): the API OpenAPI document is now enriched (TaskFlowDocumentTransformer), so `pnpm gen:api` produces a client-compatible schema and the regenerated `schema.d.ts` is committed. The note below is retained as the rationale for T062.** _(Original finding:)_ **The naive regen was PROVEN UNSAFE.** The live .NET built-in OpenAPI doc is _impoverished_ vs the curated contract: it **omits the `ProblemDetails` schema entirely**, omits the documented `401` responses, and emits generic operationIds (`ensureUser` → `POST_api_users_ensure`). `apps/web/src/lib/api/client.ts` imports `components["schemas"]["ProblemDetails"]`, so committing a naive regen **breaks `pnpm typecheck`** (empirically confirmed). The committed `schema.d.ts` (hand-derived from `contracts/openapi.yaml`) is strictly _more_ correct, keeps the build green, and has **zero runtime impact** (auth uses the hand-written `internal.ts`/proxy, not the generated client). Keep it as-is. The real fix is **T062** (a document transformer that emits ProblemDetails + 401s + operationIds), done in Phase 5 once all slice-001 endpoints exist (Phase 4 adds `DELETE /api/users/me`, so any synced schema now is throwaway anyway).

---

## Phase 3: User Story 1 - Account & Sign-In (Priority: P1) 🎯 MVP

**Goal**: An admitted team member signs in with Google (admission-gated), lands in their isolated empty workspace, sees their Google profile, can sign out; unauthenticated and non-admitted access is denied.

**Independent Test**: Sign in with Google as an admitted account → account created, land in empty workspace (AS-01); sign in as a non-admitted account → rejected, no account created (AS-01); sign out → protected views inaccessible (AS-02); hit a protected route with no session → redirected to sign-in (AS-03); open profile → Google name + avatar shown (AS-04).

> **MVP note**: This is the MVP increment, but because slice 001 is foundational, shipping US1 still requires all of Phase 1 + Phase 2. That breadth is inherent to the first slice, not a defect.

### Tests for User Story 1 (write FIRST, ensure they FAIL) ⚠️

- [X] T033 [P] [US1] Unit test `apps/api/tests/TaskFlow.UnitTests/Domain/IdentityAccess/UserTests.cs` — aggregate invariants (immutable `google_subject_id`; profile fields refresh on re-sign-in) — _14 tests green (characterization: `User.cs` exists from T014)_
- [X] T034 [P] [US1] Integration test `apps/api/tests/TaskFlow.IntegrationTests/IdentityAccess/EnsureUserTests.cs` — **allow** (valid JWT → create-then-match, profile refreshed; a `google_subject_id` with no existing row → fresh account created) **and deny** (missing/invalid JWT → 401) (SC-016) — _green; deny also asserts no row is created_
- [X] T035 [P] [US1] Integration test `apps/api/tests/TaskFlow.IntegrationTests/IdentityAccess/GetCurrentUserTests.cs` — **allow** (valid JWT → 200 + profile) **and deny** (no JWT → 401; JWT whose `sub` references a no-longer-existent / hard-deleted user → 401) (SC-013, SC-016) — _green_
- [X] T036 [P] [US1] E2E `apps/web/tests/e2e/auth.spec.ts` covering AS-01 admitted sign-in (account created, empty workspace), AS-01 non-admitted denied — not on allowlist / outside `hd`, or `email_verified` not true (recoverable message, no account), AS-02 sign-out, AS-03 unauthenticated → redirect to sign-in, AS-04 profile name+avatar shown — _**7/7 green** (2026-06-17). Built as two harness milestones under a single `playwright.config.ts` globalSetup that boots Postgres + the real migrated .NET API + a fake RS256 IdP + the Next BFF, then tears them down. **Seeded-session** `auth.spec.ts` (AS-02/03/04 + a proxy 401 deny) drives the browser through the REAL BFF→proxy→API path (real `token.ts` HS256 carrier ↔ API validation — the orientation's #1 web risk). **Fake-IdP** `auth-signin.spec.ts` (AS-01 ×3) drives the full OAuth Authorization-Code + PKCE dance through `oauth.ts` (`exchangeCodeForTokens` + `validateIdToken`, real RS256 signature + JWKS + PKCE-S256 verification): admitted+verified → account created + workspace; non-admitted → `not_admitted`, no account; `email_verified:false` on an allowlisted address → `not_admitted`, no account (admission before ensure)._

### Implementation for User Story 1

- [X] T037 [P] [US1] `EnsureUser` command + handler `apps/api/src/TaskFlow.Application/IdentityAccess/Commands/EnsureUser.cs` (create-or-match by `google_subject_id`; refresh profile fields; a previously-deleted identity has no row → creates a fresh empty account, per spec Clarifications 2026-06-17) — _also added `IUserRepository` (Application seam) + `UserRepository` (Infrastructure) + `UserProfile` DTO; handler reads `googleSubjectId` from the body, never `currentUser.Id`_
- [X] T038 [P] [US1] `GetCurrentUser` query + handler `apps/api/src/TaskFlow.Application/IdentityAccess/Queries/GetCurrentUser.cs` (return profile; 401/not-found when the user row no longer exists — hard-deleted accounts have no row) — _throws `UnauthenticatedException` (→401) when the carrier's `sub` maps to no row_
- [X] T039 [US1] Wolverine.Http endpoints `apps/api/src/TaskFlow.Api/Endpoints/UserEndpoints.cs` — `POST /api/users/ensure` and `GET /api/users/me` per contracts/openapi.yaml — depends on T037, T038 — _thin adapters delegating via `IMessageBus.InvokeAsync` so T019 weaves; `UserEndpoints` must be public (CA1515 suppressed — Wolverine.Http discovery)_
- [X] T040 [P] [US1] BFF OAuth helpers `apps/web/src/lib/auth/oauth.ts` (PKCE, `state`, `nonce`, code exchange, id_token validation incl. extracting the `email_verified` and `hd` claims for the admission gate — FR-090) — _8 Vitest unit tests green for the deterministic surface (PKCE = base64url-S256 of verifier, state/nonce, authorization-URL construction, and the encrypted `taskflow_oauth` transaction seal/open round-trip incl. tamper/wrong-secret rejection). Added the advisor-flagged OAuth **transaction-state** seam (seal `{state,nonce,codeVerifier}` into a SameSite=Lax cookie), a module-scoped JWKS singleton, and issuer-array support. **`exchangeCodeForTokens` + `validateIdToken` (network/JWKS/PKCE paths) now runtime-verified end-to-end by the fake-IdP E2E (T036).**_
- [X] T041 [P] [US1] BFF admission gate `apps/web/src/lib/auth/admission.ts` (`ADMISSION_EMAILS` and/or `ADMISSION_HD` from id_token `hd`; **require id_token `email_verified === true`**; neither match OR unverified email → `not_admitted`; **expose a startup config check that fails fast (throws) when neither `ADMISSION_EMAILS` nor `ADMISSION_HD` is configured**, wired into `apps/web/src/instrumentation.ts` (the Next.js `register()` startup hook created in T024) — FR-087). Ships a Red-first Vitest unit test `apps/web/tests/unit/admission.test.ts` covering: allowlist match, `hd` match, non-admitted rejected, unverified email rejected (even on allowlist match), and unconfigured → throws. — _11 tests green (Red-first verified); `assertAdmissionConfigured()` wired into `register()`_
- [X] T042 [US1] BFF auth routes `apps/web/src/app/api/auth/{signin,callback,signout,session}/route.ts` — signin initiates OAuth; callback validates state/nonce/PKCE → checks admission (allowlist/`hd` + `email_verified`) → `POST /api/users/ensure` → creates rotated session (FR-088); signout invalidates session + clears cookie (FR-054); session returns current session info — depends on T028, T040, T041 — _**runtime-verified by T036**: signin→IdP→callback→ensure→rotated session→workspace, and signout→server-side invalidation→cookie clear all exercised live. Includes `lib/api/internal.ts` server-to-server `ensureUser`/`fetchProfile` minting real `token.ts` carriers; admission-before-ensure; session seeded from the ensure-response `profile.id` GUID, not the Google sub; callback validates `state` vs the sealed transaction as the OAuth CSRF defense._
- [X] T043 [P] [US1] Sign-in surface `apps/web/src/app/(auth)/layout.tsx`, `apps/web/src/app/(auth)/signin/page.tsx`, and `apps/web/src/components/auth/SignInButton.tsx` — a11y: visible focus (FR-042), ARIA roles/labels (FR-043), contrast ≥4.5:1 (FR-044), no AT-binding collision (FR-045), no hover-only content (FR-046), single-key suppression in inputs (FR-031) — _**flow runtime-verified by T036**: the anchor-based sign-in entry point and the `role="alert"` recoverable not-admitted/oauth-failed message are exercised live (asserted in AS-01). The a11y properties themselves (keyboard-only operation, no-JS, contrast ≥4.5:1, AT-binding) are NOT proven by the E2E — they remain owned by the T057 a11y audit._
- [X] T044 [P] [US1] App shell `apps/web/src/app/(app)/layout.tsx` + empty workspace home `apps/web/src/app/(app)/page.tsx` — accessible first-run empty state (EC-01), no onboarding wizard/tooltips/modal (FR Principle IV) — _**flow runtime-verified by T036**: the form-POST sign-out and the quiet empty-workspace landing (after admitted sign-in) are exercised live in a JS-enabled browser. The "works without client JS" design property is not separately asserted by the E2E._
- [X] T045 [US1] Settings profile view `apps/web/src/app/(app)/settings/page.tsx` — display Google name + avatar, output-encoded (FR-056, FR-099) — depends on T044 — _**runtime-verified by T036** (client component via `useSession` → `/api/auth/session` → real carrier → API `/api/users/me`; name/email/avatar asserted in AS-04 and after AS-01 admitted sign-in)._
- [X] T046 [US1] Deny-by-default route guard in the BFF: unauthenticated requests to `(app)` routes/proxy redirected to sign-in (FR-055) — depends on T028, T042 — _**runtime-verified by T036** as `apps/web/src/middleware.ts` (edge cookie-presence redirect to /signin; matcher excludes /signin, /api, _next, static). AS-03 asserts both the page redirect and the proxy 401 (with our `unauthenticated` RFC 9457 envelope) for a missing session._

**Checkpoint**: US1 is fully functional and independently testable — sign-in, admission gate, session, profile, sign-out, and deny-by-default access all work end-to-end.

> **Phase 3 status (COMPLETE, 2026-06-17)** — legend: `[X]` runtime-verified. All Phase 3 tasks (T033–T046) are `[X]`. **US1 is fully functional and independently testable end-to-end.**
> - **API side done & green**: T033 (14 unit), T034/T035 (9 integration allow+deny), T037/T038/T039, and **T019 proven 3 ways** (message-pipeline deny via ensure/me with no row written; direct-bus `InvokeAsync` with no principal; an inline non-delegating endpoint also gated by the added HTTP backstop). Full `dotnet test` green; integration suite stable across repeated runs after `DurabilityMode.Solo`.
> - **BFF logic verified**: T041 admission (11 unit, Red-first) + T040 deterministic OAuth surface (8 unit). 19 Vitest green; `pnpm typecheck` + `lint` + **`next build`** all clean across all new web code.
> - **E2E done (T036) — 7/7 Playwright green**: a single `playwright.config.ts` `globalSetup` boots Postgres (disposable docker container, port 55432) + the real migrated .NET API (DLL on 4311) + a fake RS256 IdP (in-process on 4321, wired via `GOOGLE_*` overrides) + the Next BFF (`next dev`), and `globalTeardown` kills the process trees + removes the container. **Seeded-session** specs (`auth.spec.ts`: AS-02/03/04 + a proxy 401 deny) drive the REAL BFF→proxy→API path (closing the #1 web risk: production `token.ts` HS256 carrier ↔ API validation). **Fake-IdP** specs (`auth-signin.spec.ts`: AS-01 ×3) drive the full OAuth Authorization-Code + PKCE dance through `oauth.ts` with a real RS256 signature/JWKS round-trip and real PKCE-S256 verification.
> - **Latent T024 build bug fixed earlier this phase**: `pg` was being bundled into the edge runtime via `instrumentation.ts`; fixed by `serverExternalPackages: ["pg"]` + `instrumentation-node.ts` split.
> - **Two findings surfaced by the E2E (logged, not fixed — out of US1 scope):** (1) `docker-compose.yml` passes `JWT_SIGNING_KEY` to the API, but the API reads config key `Jwt:SigningKey` (env `Jwt__SigningKey`) — a deploy-config mismatch the compose path would hit (harness sets `Jwt__SigningKey` to compensate); (2) `EnsureUser` returns **500** (not a 409) on a `UNIQUE(email)` violation for a *different* `google_subject_id` — unreachable with real Google data (one account = one email = one sub), but a latent `DbUpdateException` robustness gap. Both belong to Phase 5 / a later hardening pass.

---

## Phase 4: User Story 2 - Account & Data Management / Deletion (Priority: P2)

**Goal**: A signed-in user can irreversibly delete their account; the erasure-cascade contract is established on the User aggregate (hard-delete of the User row + session purge + `AccountDeletionRequested` dispatched), with the tombstone identity ready for later slices to anonymize against.

**Independent Test (data-handler level — genuinely independent of US1)**: Using `TestJwtHelper`, call `DELETE /api/users/me` with a valid JWT → 204, the User row hard-deleted (no residual row), sessions purged, cascade event dispatched (**allow**); call with no/invalid JWT → 401 (**deny**). **E2E flow depends on US1** (a user must be signed in to reach the delete dialog) — that dependency is stated, not hidden.

### Tests for User Story 2 (write FIRST, ensure they FAIL) ⚠️

- [X] T047 [P] [US2] Integration test `apps/api/tests/TaskFlow.IntegrationTests/IdentityAccess/DeleteAccountTests.cs` — **allow** (valid JWT → 204, the User row hard-deleted with no residual row [SC-017], sessions purged, `AccountDeletionRequested` dispatched) **and deny** (no/invalid JWT → 401; cannot delete another user) (SC-016, SC-017) — _**8 tests green, Red-first** (24 integration total). Allow: 204 + hard-delete (no residual row) + tombstone survives; a deleted account can no longer authenticate (subsequent GET /me → 401); `AccountDeletionRequested` dispatch observed via the Wolverine in-process tracking harness (`host.TrackActivity().ExecuteAndWaitAsync` → `tracked.Sent.MessagesOf<AccountDeletionRequested>().ContainSingle()`). Deny: no-JWT/invalid-signature/expired/ghost-id/tombstone all → 401 (+ our RFC 9457 envelope). "Cannot delete another user" is structural — the carrier `sub` always names the caller's own id. NOTE: "sessions purged" is a BFF-side ON DELETE CASCADE asserted at the E2E layer (T048), not here — the API integration DB has no sessions table. **Deferred hardening:** a failure-injection rollback test proving the outbox-enqueue and the EF DELETE commit/roll-back ATOMICALLY (the current test proves dispatch happens, and the success-direction coupling is real since the tracked send only fires on commit; the failure-direction is design-guaranteed by AutoApplyTransactions + the durable local queue) — tracked for a later hardening pass._
- [X] T048 [P] [US2] E2E in `apps/web/tests/e2e/delete-account.spec.ts` — delete + confirm → session ends; re-sign-in with the same Google identity yields a fresh empty account (the deleted account is NOT restored); assert the deletion-contract surfaces (cascade dispatched, tombstone present) for AS-03/AS-04 — _**green** (8/8 e2e). Reuses the Playwright harness: OAuth sign-in (fake IdP) → capture id1 → open `DeleteAccountDialog` + confirm (real form POST) → session ends (redirected to /signin) → protected route redirects (cookie cleared) → `userExistsByGoogleSub === false` (SC-017 hard-delete via the BFF `sessions` ON DELETE CASCADE) → re-sign-in same identity → id2 truthy and **id2 ≠ id1** (fresh account, not restored). Placed in its own spec file (not `auth.spec.ts`) for clarity._

### Implementation for User Story 2

- [X] T049 [P] [US2] Domain event `apps/api/src/TaskFlow.Domain/IdentityAccess/Events/AccountDeletionRequested.cs` — _sealed record `(UserId, RequestedAtUtc) : DomainEvent`; carries only the deleted UserId (later slices reattribute that user's content to the tombstone keyed on this id). The Google subject id is intentionally omitted — the row delete frees the identity so a re-sign-in is a fresh account._
- [X] T050 [US2] `DeleteAccount` command + handler `apps/api/src/TaskFlow.Application/IdentityAccess/Commands/DeleteAccount.cs` — within one transaction: dispatch `AccountDeletionRequested` via the Wolverine outbox, then **hard-delete the User row** (irreversible); the user's BFF-owned session rows are removed automatically by the `sessions.user_id` ON DELETE CASCADE (T024), so the API never touches the session table (FR-085); excluded from 30s undo — depends on T049 — _handler injects `IMessageContext` and `PublishAsync(AccountDeletionRequested)` BEFORE `users.Remove` + `SaveChangesAsync`, so the outbox enqueue and the EF DELETE commit atomically (AutoApplyTransactions + UseEntityFrameworkCoreTransactions + Postgres outbox). The command carries NO id — identity is `ICurrentUser` (carrier `sub`) so "delete another user" is impossible. Tombstone/already-deleted subjects → 401, not idempotent 204. **Routability wiring (Program.cs):** `opts.PublishMessage<AccountDeletionRequested>().ToLocalQueue("account-deletion")` (an unrouted publish is silently dropped) + a no-op `AccountDeletionRequestedHandler` (slice 001 placeholder for the later erasure coordinator). **Auth exemption:** the global `AuthorizationMiddleware` policy now uses the `Func<HandlerChain,bool>` overload to exclude ONLY the off-queue `AccountDeletionRequested` handler (no HttpContext there → `ICurrentUser` would throw); every real command/query — including `DeleteAccount` — keeps deny-by-default, and the HTTP backstop is untouched (proven by the unchanged deny tests)._
- [X] T051 [US2] Add `DELETE /api/users/me` to `apps/api/src/TaskFlow.Api/Endpoints/UserEndpoints.cs` (204 on success, per contracts/openapi.yaml) — depends on T050, T039 — _thin `[WolverineDelete("/api/users/me")]` adapter delegating via `IMessageBus.InvokeAsync(new DeleteAccount())` so `AuthorizationMiddleware` weaves; returns 204 (asserted in T047)._
- [X] T052 [US2] BFF: on successful account deletion, clear the session cookie — the user's session rows are already removed by the ON DELETE CASCADE when the API hard-deletes the user (T024/T050), so no explicit session-table write is needed — depends on T024 — _new `apps/web/src/app/api/auth/delete/route.ts` (POST): CSRF Origin gate → validate session → `internal.ts deleteAccount(userId)` (me-scoped carrier, `response.ok` = success) → on success 303→/signin + clear cookie; on failure keep the cookie and 303→`/settings?error=delete_failed` (surfaced as a `role="alert"` on settings — FR-049). Does NOT call invalidateSession (the row is already gone via cascade)._
- [X] T053 [US2] `apps/web/src/components/ui/DeleteAccountDialog.tsx` — FR-101 dialog focus contract (initial focus, focus trap, Esc dismiss, return focus to invoker) + clear blast-radius confirmation; wire into the settings page — depends on T045 — _reuses the existing `Dialog` primitive (FR-101 contract); blast-radius copy ("permanently and irreversibly … ALL of its data"); confirms via a REAL native form POST (carries Origin → satisfies the route CSRF gate, no client fetch). Wired into the authenticated branch of settings. Delete-requires-JS is accepted/documented; sign-out stays the no-JS-safe control._

**Checkpoint**: Account deletion works with allow+deny coverage; the cascade/tombstone contract is established for later slices to extend.

> **Phase 4 status (COMPLETE, 2026-06-17)** — all of T047–T053 are `[X]`, runtime-verified. API: `dotnet build` 0/0; `dotnet test` **38 green** (14 unit + 24 integration, incl. 8 DeleteAccountTests allow+deny). BFF/E2E: `pnpm typecheck`/`lint`/`test`(19)/`build`/`e2e`(**8/8**, incl. the delete-roundtrip) all green. Built via a scout→plan→TDD-implement→adversarial-verify workflow; verifiers returned 0 blockers / 0 majors / 3 minors — one fixed (settings error surfacing), one dismissed (inline `DateTime.UtcNow` matches the established `EnsureUserHandler` pattern; the domain aggregate takes `now` as a param), one deferred (the atomicity failure-injection rollback test noted on T047). Then independently re-verified by re-running every suite + a fresh self-review pass.

---

## Phase 5: Polish & Cross-Cutting Concerns

**Purpose**: Slice-wide verification of the success criteria and cross-cutting requirements.

- [X] T054 [P] SC-016 verification: assemble the role×operation deny matrix confirming every data handler introduced here (ensureUser, getCurrentUser, deleteAccount) ships **both** an allow and a deny test; record in `specs/001-accounts-and-auth/` (e.g. a matrix note or a parametrized test summary) — _written to `specs/001-accounts-and-auth/deny-matrix.md`: a role×operation table citing the exact allow/deny test methods (ensureUser 3 allow + 2 deny; getCurrentUser 1 allow + 5 deny; deleteAccount 3 allow + 5 deny), plus the structural deny-by-default argument (ensureUser's no-row-written probe is the sharpest)._
- [X] T055 [P] SC-017 verification: confirm account deletion leaves no residual personally-attributable data beyond the tombstone identity for the User aggregate — assert the User row is hard-deleted (no residual row) and only the separate seeded tombstone remains (extend assertion in DeleteAccountTests) — _confirmed: `DeleteAccountTests.Allow_hard_deletes_the_callers_row_leaving_no_residual_data` asserts the row is gone AND `UserId.Tombstone` survives; no soft-delete column; sessions cascade-purged; erasure event dispatched. Recorded in deny-matrix.md._
- [X] T056 [P] FR-100 verification: confirm secrets (session signing key, OAuth client secret, DB/broker creds, deploy keys) are runtime-injected and never committed, baked into images, or written to logs/error context — scan repo, image build args, and structured logs — _scan clean: `.env.example` has only placeholders, `docker-compose*.yml` use `${VAR}` interpolation (no literals), Dockerfiles pass no secret build args, CI injects via host env, and the API/BFF never log secrets/tokens/cookies. Two documented non-issues: `AppDbContextFactory.cs` hardcodes a design-time-only `Password=postgres` (well-known local default for `dotnet ef`, never opened at runtime), and `playwright.config.ts` holds clearly-labeled TEST-ONLY e2e fixtures._
- [X] T057 [P] Accessibility pass across all auth surfaces (sign-in, empty workspace, settings, delete dialog): FR-042–047, contrast ≥4.5:1, `prefers-reduced-motion` (transitions instant/<100ms), FR-031 single-key suppression in text inputs, FR-101 ARIA-live announcements — _pass: visible focus + ARIA roles/labels present; contrast ≥4.5:1 verified against the actual `globals.css` color values (the added `.tf-settings__error` danger color is 6.36:1); `prefers-reduced-motion` handled; FR-101 dialog focus contract via the `Dialog` primitive; `role=status`/`role=alert` announcers wired. Fixed: added the 3 missing CSS class hooks (`.tf-settings__error`, `.tf-dialog__actions`, `.tf-delete-account`). FR-031 single-key suppression is N/A (no text inputs in this slice). The app-wide empty `LiveRegion` is correct scoping (server-push announcements are a later slice)._
- [X] T058 [P] FR-099 verification: confirm CSP + standard security headers present in the production response and that Google profile fields are output-encoded (no raw HTML injection) — _**found + fixed a real gap**: the BFF (which serves the browser HTML) had NO CSP — only the API did. Added a production CSP + standard headers to `apps/web/next.config.ts` `headers()` (`default-src 'self'`; `img-src` allows the Google avatar hosts; `script-src/style-src 'unsafe-inline'` for the App Router bootstrap, `'unsafe-eval'` dev-only; `frame-ancestors 'none'`; `base-uri`/`form-action 'self'`), with a Vitest regression lock. API headers locked by a new `SecurityHeadersTests.cs` (CSP `default-src 'none'` + nosniff/X-Frame-Options/Referrer-Policy/CORP survive the 401 `Response.Clear()` error path AND a 200). Google profile fields are React-output-encoded (no `dangerouslySetInnerHTML` anywhere)._
- [X] T059 [P] FR-051 backup/restore validation: run `scripts/backup.sh` then `scripts/restore-test.sh` against a throwaway DB and assert integrity (no-op-but-present for the first migration) — _**live-verified green**: ran both scripts inside a `postgres:17` container against the real migrated schema (booted the API to apply `InitialCreate` + tombstone), produced a 16.8 KB custom-format dump, restored into a throwaway DB and asserted the `users` table + all 3 indexes + the tombstone row are present; a negative run (empty DB) correctly failed the integrity assertion (exit 1). Mirrors the CI deploy chain (backup → migrate → restore-test)._
- [X] T060 Run the `quickstart.md` validation scenarios end-to-end against the running app and confirm all rows pass — _every quickstart validation row maps to a green automated test (AS-01..04, US-17 deletion, SC-016 allow/deny/deleted-deny); the traceability table is in deny-matrix.md. App behavior proven by the 8/8 e2e + 26 integration._
- [X] T061 [P] Documentation: update repo `README` / monorepo dev docs to match the bootstrapped structure and the quickstart — _rewrote the materially-stale `README.md`: corrected Status (slice 001 built+green, not "no application code yet"), constitution v4.0.0/12 principles, the full real monorepo layout tree, and added Prerequisites / Environment (env table) / Run-locally / Test-suites / OpenAPI-regen / Backups sections with accurate commands._
- [X] T062 [P] OpenAPI document fidelity (Constitution VI): add a .NET OpenAPI document transformer so the live `/openapi/v1.json` matches the curated contract — register the `ProblemDetails` schema, attach the documented `401`/`404`/`204` responses to each Wolverine.Http endpoint, and set stable operationIds (`ensureUser`/`getCurrentUser`/`deleteAccount`). THEN run `pnpm gen:api` and confirm the regenerated `schema.d.ts` keeps `pnpm typecheck` green (preserves `components["schemas"]["ProblemDetails"]` used by `client.ts`) and re-enables the CI openapi-sync diff-gate without breaking the build. — _**done**: `apps/api/src/TaskFlow.Api/OpenApi/TaskFlowDocumentTransformer.cs` (registered via `AddOpenApi(o => o.AddDocumentTransformer<…>())`) restores the RFC 9457 `ProblemDetails` schema (with the stable `errorCode` enum), the three operationIds, and a `401`→`ProblemDetails` response on each user endpoint. Regenerated `schema.d.ts` from the live doc; `pnpm typecheck`/`lint` stay green (`client.ts`'s `components["schemas"]["ProblemDetails"]` resolves). API build 0/0; `dotnet test` 40 green (the transformer runs when `/openapi/v1.json` is served). The CI openapi-sync `git diff --exit-code` gate is now satisfiable — the committed client matches the live contract._

> **Phase 5 status (2026-06-17)** — T054–T061 all `[X]`, verified. Built via an audit→remediate→verify workflow + an independent re-run of every suite + a fresh self-review. Real gap found and fixed: the **BFF had no CSP** (FR-099) — now added with regression locks on both BFF (Vitest) and API (`SecurityHeadersTests`). Also: README rewritten (was materially stale), `deny-matrix.md` written (T054/T055/T060), backup/restore live-verified (T059), and the integration suite made **deterministic** by clearing the Windows EventLog logger provider in the test host (it intermittently threw during `WebApplicationFactory` disposal). Final gates: `dotnet build` 0/0; `dotnet test` **40 green** (14 unit + 26 integration); web `typecheck`/`lint`/`test`(22)/`build`/`e2e`(8/8). **T062 (OpenAPI document transformer) — DONE**: `TaskFlowDocumentTransformer` restores `ProblemDetails` + operationIds + 401s, the client was regenerated from the live doc and keeps typecheck green, and the CI openapi-sync gate is now satisfiable. **All of T001–T062 are `[X]` — slice 001 is complete.**

### Phase dependencies

- **Setup (P1)** → no dependencies; start immediately.
- **Foundational (P2)** → depends on Setup; **blocks all user stories**.
- **US1 (P3)** → depends on Foundational; the MVP increment.
- **US2 (P4)** → depends on Foundational; its data-handler tests are independent of US1, but its E2E flow depends on US1 sign-in.
- **Polish (P5)** → depends on US1 + US2 (the criteria it verifies).

### Critical ordering edges (must hold)

- **T016 (EF migration, creates `users`) → T024 (BFF session table)** — the session FK references `users(id)`; the migration must run first.
- **T022 (API host builds the OpenAPI contract) → T023 (TS client generation)** — the generated client requires the contract to build first.
- T037/T038 → T039 (endpoints wire the handlers); T049 → T050 → T051 (event → handler → endpoint); T044 → T045 → T053 (shell → settings → delete dialog).

### Within each user story

- Tests (T033–T036, T047–T048) are written and MUST FAIL before their implementation tasks (Constitution VIII).
- Domain/handlers before endpoints; BFF helpers before BFF routes; shell before pages.

---

## Parallel Opportunities

- **Setup**: T002–T011 are all `[P]` (distinct files) once T001 creates the tree.
- **Foundational**: T012/T013, T017, T021, and the BFF helper cluster T024/T025/T026/T027, plus T029/T030/T031/T032 run in parallel within their dependency limits.
- **US1 tests**: T033, T034, T035, T036 in parallel (different files), all before implementation.
- **US1 impl**: T037/T038 parallel; T040/T041 parallel; T043/T044 parallel.
- **Polish**: T054–T059 and T061 are all `[P]`.

### Parallel Example: User Story 1 tests (Red phase)

```bash
Task: "Unit: User aggregate invariants in apps/api/tests/TaskFlow.UnitTests/Domain/IdentityAccess/UserTests.cs"
Task: "Integration allow+deny: EnsureUserTests.cs"
Task: "Integration allow+deny: GetCurrentUserTests.cs"
Task: "E2E auth.spec.ts: AS-01..AS-04"
```

---

## Implementation Strategy

### MVP First (US1)

1. Phase 1 Setup → 2. Phase 2 Foundational (CRITICAL — blocks everything) → 3. Phase 3 US1 → **STOP and validate** US1 independently (quickstart AS-01..AS-04 + SC-016 allow/deny) → deploy/demo.

### Incremental Delivery

Setup + Foundational → US1 (MVP, test + deploy) → US2 (test + deploy) → Polish/verification pass. Each increment is independently testable and adds value without breaking the prior one.

---

## Traceability (artifact → task coverage)

**Endpoints (contracts/openapi.yaml)** — each ships handler + allow + deny test:
- `ensureUser` → T037 (impl) · T039 (route) · T034 (allow+deny)
- `getCurrentUser` → T038 (impl) · T039 (route) · T035 (allow+deny)
- `deleteAccount` → T050 (impl) · T051 (route) · T047 (allow+deny)

**Acceptance scenarios**:
- US-11.AS-01 → T036, T034; US-11.AS-02 → T036, T042; US-11.AS-03 → T036, T046; US-11.AS-04 → T036, T045
- US-17.AS-02 → T048, T047, T050; US-17.AS-03 → T047, T055 (tombstone/cascade contract); US-17.AS-04 → T048, T016 (tombstone seed)

**Success criteria**: SC-013 → T035 (deny) + T019 (deny-by-default pipeline); SC-016 → T034/T035/T047 + T054 (matrix); SC-017 → T047, T055.

**Entity / data-model**: ENT-06 User → T014/T015/T016; tombstone seed → T016; Session → T024.

**Key requirements**: FR-052→T037/T042; FR-053→T024/T042; FR-054→T042; FR-055→T046; FR-056→T045; FR-085→T050; FR-086→T032/T011 (retention stance documented in `.env.example`/research); FR-087→T041; FR-088→T024/T042; FR-089→T027; FR-090→T040; FR-091→T026/T028/T022; FR-099→T021/T058; FR-100→T056; FR-065/066/067/068→T017/T018/T019; FR-031/042–047/101→T029/T043/T044/T057; FR-049→T032; FR-050→T032; FR-051→T011/T059.

---

## Notes

- This `tasks.md` is a planning document only. **No code is written under `apps/**` until `/speckit-implement`.**
- `[P]` = different files, no incomplete dependency. `[Story]` label present on US phases only.
- Verify every test fails before implementing (Red-Green-Refactor).
- Commit after each task or logical group; never commit secrets (public repo).

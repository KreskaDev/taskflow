# ADR-0004 — Identity & Access

**Status:** Accepted (2026-06-14); amended (2026-06-15) under constitution v4.0.0 to reverse open sign-up to an admission gate and to harden the session and OAuth model (remediation ledger B12; the 2026-06-15 design review)
**Builds on:** ADR-0001 (technical foundation: Next.js BFF, C# API, single origin)
**Realizes:** constitution v4.0.0 Principle IX (authentication half + admission control) and the Identity & Access bounded context

## Context

TaskFlow is a connected, collaborative, multi-user web application (constitution
v4.0.0). Every request must carry an identity before any data is read or written:
there is no anonymous or guest access (OOS-16), and the data model is built on
ownership and membership keyed to a `User`. The product therefore needs a sign-in
mechanism, a session model, and a way for the C# API to know *who* is calling.

The product decisions are fixed by the vision and constitution: sign-in is Google
OAuth (US-11, FR-052, ASM-11 — Google is the sole identity provider), sessions are
HttpOnly cookies with no tokens in client JavaScript (FR-053), sign-out ends the
session (FR-054), unauthenticated access is denied and redirected to sign-in
(FR-055), and the user's Google profile (name, avatar) is shown (FR-056). This ADR
records *how* those are realized on the ADR-0001 stack.

Two things changed in the 2026-06-15 remediation (ledger B12 and the admission
decision), bumping the constitution to v4.0.0 (MAJOR):

- **Sign-up is no longer open.** Account creation is gated to an explicit email
  allowlist or a Google Workspace hosted-domain (`hd`); sign-in is not open to any
  Google account (FR-087, ASM-13, constitution IX *Admission control*). The earlier
  "open sign-up" reading of FR-052 is superseded.
- **The session and OAuth model is specified, not left implicit.** The earlier ADR
  said "HttpOnly cookie session" without naming the store, lifetimes, rotation,
  CSRF posture, OAuth hardening, or the integrity of the BFF→API identity carrier.
  B12 fixes each of these (FR-088–FR-091).

**Scope boundary:** this ADR covers **authentication and identity only** — proving
*who* the caller is, and *whether* they are admitted. **Authorization** — per-user
data isolation, project membership, and viewer/editor/owner role checks (Principle
IX authorization half; FR-065–FR-068) — is deferred to **ADR-0005**. Here,
deny-by-default applies to *unauthenticated* and *non-admitted* requests (FR-055,
FR-087); deny-by-default authorization on every data operation (FR-068) is
ADR-0005's concern.

## Decisions

1. **Google OAuth as the sole IdP, with an admission gate (not open sign-up).**
   Sign-in is "Sign in with Google" only (ASM-11; no other SSO — OOS-17). Account
   creation is **gated**: a first-time sign-in creates an account **only if** the
   Google identity is admitted — its verified email is on the configured allowlist
   **or** its Workspace hosted-domain (`hd`) claim matches the configured domain. A
   non-admitted sign-in is rejected before any `User` is provisioned and is shown a
   clear "access not available" message; no account, session, or task data is
   created for it (FR-087, ASM-13). A returning admitted sign-in matches the
   existing account (US-11.AS-01). There is no password store and no local
   credential flow.

2. **Sessions are server-managed, Postgres-backed, HttpOnly cookie sessions issued
   by the Next.js BFF.** The single-origin Next.js BFF (ADR-0001) runs the OAuth
   code exchange with Google, then establishes a **server-side session persisted in
   PostgreSQL** (so sessions survive a BFF restart and can be invalidated
   server-side). The session is represented to the browser as an **HttpOnly,
   Secure** cookie with an **explicit `SameSite`** attribute. **No access tokens or
   ID tokens are exposed to client JavaScript** (FR-053): the browser never holds a
   bearer credential, only the opaque session cookie the BFF set (FR-088).

3. **Sessions have bounded lifetimes and a rotated id.** Each session carries a
   server-enforced **absolute lifetime** and an **idle (inactivity) timeout**, both
   documented; reaching either ends the session. A **new session id is issued at
   OAuth completion** — the pre-authentication session id is not reused (session-
   fixation defense). Expiry and rotation are enforced server-side against the
   Postgres session store, not by the cookie alone (FR-088).

4. **Mutations are CSRF-protected.** Because the session is a cookie, every
   state-changing request through the BFF MUST be CSRF-protected — an anti-CSRF
   token and/or a same-origin (`Origin`/`Referer`) check — in addition to the
   `SameSite` cookie attribute. Safe (read-only) requests do not require the token
   (FR-089, constitution XII).

5. **The OAuth flow is hardened.** The authorization-code flow MUST use **`state`**
   (CSRF/replay defense on the redirect), a **`nonce`**, and **PKCE**, and the BFF
   MUST **validate the returned `id_token`** (signature, issuer, audience,
   expiry, and nonce match) before establishing a session or provisioning a `User`.
   The admission check (Decision 1) runs against the validated identity claims
   (FR-090).

6. **The C# API trusts a signed, short-lived, BFF-minted identity token — and is
   not externally reachable.** The API has no public surface and is reached only via
   the web origin's `/api` path over the internal Docker network (ADR-0001 /
   constitution Architecture & Stack — no separate public API, no CORS); its port is
   **not externally reachable**. On each request the BFF validates the session, then
   forwards the authenticated user's identity inward as a **signed, short-lived
   token** (integrity-protected carrier), which the API verifies and treats as the
   authenticated principal for the request. The API does not perform the OAuth
   handshake itself and does not accept browser-supplied credentials (FR-091).

7. **User entity (ENT-06) is provisioned from Google.** On first admitted sign-in a
   `User` is created from the Google identity: the Google **subject id** (stable
   identifier), **email**, **display name**, and **avatar**. The subject id is the
   matching key for returning sign-ins; the profile fields back the in-app profile
   display (FR-056, US-11.AS-04). This is the single source of identity for the
   Identity & Access bounded context.

8. **Sign-out invalidates the session server-side.** Signing out invalidates the
   server-side session **record in Postgres** and clears the session cookie;
   protected views and endpoints are no longer accessible afterward, and a stolen
   cookie is useless once the record is gone (FR-054, FR-088, US-11.AS-02).

9. **Deny-by-default for unauthenticated requests.** Any request to a protected
   route or endpoint without a valid session is denied and directed to sign-in
   (FR-055, US-11.AS-03). Authentication is the gate in front of every protected
   surface; authorization of *what* an authenticated user may do is layered on top
   in ADR-0005.

## Consequences

- **Principle V carve-out is honored.** Google OAuth is the one permitted external
  runtime dependency, and only for sign-in — "never for storing application data."
  The IdP authenticates the user; no task, project, or other application data leaves
  the system to Google. The data-sovereignty guarantee of Principle V is preserved.

- **The product is invite-shaped, not public.** The admission gate (Decision 1)
  means the deployed instance is restricted to a known team (allowlist or one
  Workspace domain), consistent with the single-team scope (constitution
  *Collaborative, Multi-User*). Adding a member is an operational act (update the
  allowlist / domain config), not self-service public sign-up. Pending /
  pre-account invitations remain out of scope (OOS-18); invitations resolve against
  already-signed-in Users (ledger B13).

- **The application-layer auth replaces the edge access gate.** The Caddy
  `basic_auth` shared-password gate is removed (constitution v3.0.0+); Caddy keeps
  TLS termination and single-origin reverse proxy. Public access is now gated by
  Google OAuth + the admission gate + BFF sessions. This is the access-control
  change reflected in ADR-0002 (deployment).

- **Tokens never reach the browser, narrowing the client attack surface.** The BFF
  holds the OAuth artifacts and the browser holds only an HttpOnly cookie, so XSS
  cannot exfiltrate a bearer token. Server-side session storage plus id rotation,
  bounded lifetimes, and server-side sign-out invalidation close session-fixation
  and stolen-cookie-after-logout gaps. CSRF protection plus `SameSite` closes the
  cookie's cross-site write surface. This is the reason the BFF-session model is
  chosen over a public-API-plus-client-token model.

- **The internal carrier is integrity-protected.** A signed, short-lived BFF→API
  token over the internal network (API port not externally reachable) means the API
  can trust the forwarded principal without re-running OAuth and without a spoofable
  plaintext header. The signing key is a runtime-injected secret (constitution XII;
  FR-100).

- **New persistence: a session store.** Sessions live in PostgreSQL (EF Core
  migration), adding a `Session` (or equivalent) table to the schema owned by the
  Identity & Access context. This is the only new storage this ADR introduces.

- **Identity becomes the foundation for authorization (ADR-0005).** Every request
  now carries a known, admitted `User`. ADR-0005 builds on this to enforce per-user
  isolation and project membership/role checks (FR-065–FR-068) — deny-by-default on
  every read and write. Per constitution Principle VIII, those authorization paths
  are tested, including deny cases.

- **The Identity & Access bounded context owns User and Session.** `User` (ENT-06),
  the session store, the admission policy, and the identity provisioning logic live
  in the Identity & Access context (constitution Architecture & Stack).
  `ProjectMembership` (ENT-07) and the authorization policy also belong to this
  context but are specified in ADR-0005.

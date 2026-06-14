# ADR-0004 — Identity & Access

**Status:** Accepted (2026-06-14)
**Builds on:** ADR-0001 (technical foundation: Next.js BFF, C# API, single origin)
**Realizes:** constitution v3.0.0 Principle IX (authentication half) and the Identity & Access bounded context

## Context

TaskFlow is a connected, collaborative, multi-user web application (constitution
v3.0.0). Every request must carry an identity before any data is read or written:
there is no anonymous or guest access (OOS-16), and the data model is built on
ownership and membership keyed to a `User`. The product therefore needs a sign-in
mechanism, a session model, and a way for the C# API to know *who* is calling.

The product decisions are already fixed by the vision and constitution: sign-in is
Google OAuth with open sign-up (US-11, FR-052, ASM-11 — Google is the sole identity
provider), sessions are HttpOnly cookies with no tokens in client JavaScript
(FR-053), sign-out ends the session (FR-054), unauthenticated access is denied and
redirected to sign-in (FR-055), and the user's Google profile (name, avatar) is
shown (FR-056). This ADR records *how* those are realized on the ADR-0001 stack.

**Scope boundary:** this ADR covers **authentication and identity only** — proving
*who* the caller is. **Authorization** — per-user data isolation, project
membership, and viewer/editor/owner role checks (Principle IX authorization half;
FR-065–FR-068) — is deferred to **ADR-0005**. Here, deny-by-default applies to
*unauthenticated* requests (FR-055); deny-by-default authorization on every data
operation (FR-068) is ADR-0005's concern.

## Decisions

1. **Google OAuth as the sole IdP, open sign-up.** Sign-in is "Sign in with Google"
   only (ASM-11; no other SSO — OOS-17). Sign-up is open: a first-time sign-in
   creates an account, a returning sign-in matches the existing one (FR-052,
   US-11.AS-01). There is no password store and no local credential flow.

2. **Sessions are HttpOnly cookie sessions issued and managed by the Next.js BFF.**
   The single-origin Next.js BFF (ADR-0001) runs the OAuth code exchange with Google,
   then establishes a server-managed session represented to the browser as an
   HttpOnly, Secure, SameSite cookie. **No access tokens or ID tokens are exposed to
   client JavaScript** (FR-053): the browser never holds a bearer credential, only the
   opaque session cookie the BFF set.

3. **The C# API trusts the BFF-forwarded identity.** The API has no public surface
   and is reached only via the web origin's `/api` path over the internal Docker
   network (ADR-0001 / constitution Architecture & Stack — no separate public API,
   no CORS). The BFF validates the session on each request and forwards the
   authenticated user's identity inward; the API trusts that forwarded identity as
   the authenticated principal for the request. The API is not exposed for the
   browser to call directly, so it does not perform the OAuth handshake itself.

4. **User entity (ENT-06) is provisioned from Google.** On first sign-in a `User` is
   created from the Google identity: the Google **subject id** (stable identifier),
   **email**, **display name**, and **avatar**. The subject id is the matching key for
   returning sign-ins; the profile fields back the in-app profile display (FR-056,
   US-11.AS-04). This is the single source of identity for the Identity & Access
   bounded context.

5. **Sign-out ends the session.** Signing out invalidates the server-side session and
   clears the session cookie; protected views and endpoints are no longer accessible
   afterward (FR-054, US-11.AS-02).

6. **Deny-by-default for unauthenticated requests.** Any request to a protected route
   or endpoint without a valid session is denied and directed to sign-in (FR-055,
   US-11.AS-03). Authentication is the gate in front of every protected surface;
   authorization of *what* an authenticated user may do is layered on top in ADR-0005.

## Consequences

- **Principle V carve-out is honored.** Google OAuth is the one permitted external
  runtime dependency, and only for sign-in — "never for storing application data."
  The IdP authenticates the user; no task, project, or other application data leaves
  the system to Google. The data-sovereignty guarantee of Principle V is preserved.

- **The application-layer auth replaces the edge access gate.** The Caddy `basic_auth`
  shared-password gate is removed (constitution v3.0.0); Caddy keeps TLS termination
  and single-origin reverse proxy. Public access is now gated by Google OAuth +
  BFF sessions. This is the access-control change to reflect in ADR-0002 (deployment).

- **Tokens never reach the browser, narrowing the client attack surface.** Because the
  BFF holds the OAuth artifacts and the browser holds only an HttpOnly cookie, XSS
  cannot exfiltrate a bearer token. This is the reason the BFF-session model is chosen
  over a public-API-plus-client-token model.

- **Identity becomes the foundation for authorization (ADR-0005).** Every request now
  carries a known `User`. ADR-0005 builds on this to enforce per-user isolation and
  project membership/role checks (FR-065–FR-068) — deny-by-default on every read and
  write. Per constitution Principle VIII, those authorization paths are tested,
  including deny cases.

- **The Identity & Access bounded context owns User.** `User` (ENT-06) and the
  identity provisioning logic live in the Identity & Access context (constitution
  Architecture & Stack). `ProjectMembership` (ENT-07) and the authorization policy
  also belong to this context but are specified in ADR-0005.

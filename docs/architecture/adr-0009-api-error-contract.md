# ADR-0009 — API & error contract

**Status:** Accepted (2026-06-15)
**Builds on:** ADR-0001 (stack: REST + OpenAPI as the source-of-truth contract, a generated
TypeScript client, Zod at frontend trust boundaries, FluentValidation / data annotations at
the API boundary; C# / ASP.NET Core), ADR-0007 (Real-time: the SignalR hub whose payloads
this ADR brings under the same typed contract), and the 2026-06-15 remediation decision
ledger (B11, FR-093).
**Drives:** constitution v4.0.0 Principle VI ("The error contract is a documented,
machine-readable schema (see ADR-0009) modeled by the generated client") and the
Architecture & Stack "API contract" bullet; the Error-UX side of Principle VII / FR-049.
**Referenced by:** constitution Principle VI.
**Scope:** the shape of every API response that is not the happy-path body — errors,
validation failures, conflicts — plus how the single OpenAPI document (and the SignalR
payload types) is owned, assembled, generated, and kept honest across the 18 slices. It does
not redefine *which* errors each slice raises (that is each slice's domain concern); it fixes
the *contract* they all speak.

## Context

ADR-0001 settled that REST + an OpenAPI specification is the typed contract and that the
TypeScript client is generated from it, never hand-written (constitution VI). What it left
open is the **error** half of that contract. With a client, a BFF, an API, a database, and a
SignalR channel in the path (constitution V / IX), failures are routine and varied:
validation rejections (FR-006, FR-098), authorization denials (deny-by-default, FR-068,
FR-095), not-found, last-write-wins conflicts surfaced to the user (Principle III / VII,
FR-040, FR-049), server faults, and plain network loss. Principle VII / FR-049 require that
**every** one of these reach the user as a clear message with an actionable recovery
affordance, and that none fail silently.

Two gaps make that unbuildable without a decision. First, there is no agreed **error body
shape**: if each handler invents its own JSON, the generated client cannot type it, Zod
cannot validate it, and the frontend cannot map it to a consistent UX — error handling
degrades to stringly-typed guessing. Second, the contract was described as "an OpenAPI
specification" (singular) but the product is sliced into **18 vertical slices**
(`specs/001`..`018`), each adding endpoints; without an ownership and assembly convention the
single document either fragments into 18 drifting files or becomes a merge-conflict choke
point, and the SignalR payloads (ADR-0007/0008) sit entirely outside the generated contract
today. This ADR closes both gaps so that "type safety end-to-end, contract machine-generated,
never hand-synced" (Principle VI) holds for failures and live payloads, not just success
bodies.

## Decisions

1. **Error body: ProblemDetails (RFC 9457).** Every non-2xx API response carries an
   `application/problem+json` body conforming to RFC 9457 (Problem Details for HTTP APIs):
   the standard `type`, `title`, `status`, `detail`, and `instance` members. ASP.NET Core's
   built-in `ProblemDetails` / `IProblemDetailsService` produces it; a single exception-handling
   middleware maps domain and framework exceptions to the appropriate status + body so no
   handler hand-rolls error JSON. This is the one error shape across the whole API surface.

2. **Field-level validation extension.** Validation failures (HTTP 400, and 422 where a
   request is well-formed but semantically invalid) extend ProblemDetails with a structured
   `errors` member: a map of field path → list of messages (the `ValidationProblemDetails`
   shape), so the frontend can attach each message to its field rather than showing one
   opaque banner. FluentValidation / data-annotation failures at the API boundary
   (constitution VI) project into this extension. The Polish parser failure (FR-006,
   "nie rozpoznano") and comment-safety rejections (FR-098) are ordinary instances of it.

3. **Stable, machine-readable error-code enum.** ProblemDetails additionally carries a
   `code` extension member drawn from a **stable, versioned enum** of machine-readable codes
   (e.g. `validation_failed`, `forbidden`, `not_found`, `conflict_lww`, `last_owner`,
   `not_admitted`, `internal_error`). The code — not the human `title`/`detail` text and not
   the bare HTTP status — is the value the client branches on. `title`/`detail` are
   human-facing and may change; the code is a contract token and changes only by additive
   amendment. The `conflict_lww` code is how an undo/edit overwrite of a concurrent change is
   surfaced (Principle III / VII, FR-040, FR-049); `last_owner` is the recoverable
   last-owner-guard error (FR-061, FR-094).

4. **Modeled by the generated TS client and Zod.** The ProblemDetails body, the validation
   extension, and the error-code enum are all declared as component schemas in the OpenAPI
   document, so the generated TypeScript client types them with no hand-syncing
   (constitution VI). At the frontend trust boundary, the **same** error schema is parsed
   with **Zod** (constitution VI: "Zod … on the web for … API responses"), so a malformed or
   unexpected error body is itself caught rather than crashing the handler. The error-code
   enum is a single generated TS union — exhaustive `switch` over it is compiler-checked, so a
   new code cannot be added server-side without the client failing to compile until it is
   handled.

5. **Repo-wide Error-UX mapping.** A single, repo-wide mapping table (owned in the web app's
   shared error layer) maps each error class to a user-facing message **and** a recovery
   affordance, satisfying FR-049 uniformly instead of per-screen ad hoc handling:
   - *validation* (`validation_failed`) → inline per-field messages on the offending fields;
     recovery = correct and resubmit (no toast for field errors).
   - *403 deny* (`forbidden`) → "You no longer have access" / "Your role doesn't allow this";
     recovery = re-sync / refresh the view (ties to the FR-095 live-revocation 403 re-sync).
   - *404* (`not_found`) → "This item is no longer available"; recovery = back to the list /
     refetch (ties to notification dereference, FR-080/FR-096 "no longer available").
   - *409 conflict* (`conflict_lww`) → "Someone else changed this; your change overwrote
     theirs" (or was rejected); recovery = show the current value / undo affordance
     (FR-040/FR-049).
   - *500* (`internal_error`) → "Something went wrong on our end"; recovery = retry, with the
     structured server log (FR-050) carrying the real detail (never leaked to the body).
   - *network / offline* (no response) → "Can't reach the server"; recovery = retry /
     auto-retry on reconnect (consistent with the SignalR reconnect re-sync, ADR-0007 / FR-078).
   Slice 002 owns the optimistic-rollback UX scenarios (ledger B8/B11): a rejected optimistic
   write rolls the UI back and renders the mapped message + affordance. All messages flow
   through the ARIA-live / toast contract (Principle II / FR-101) so they reach assistive tech.

6. **A single canonical OpenAPI document with an ownership/assembly convention.** There is
   **one** canonical OpenAPI document for the API (constitution VI: "an OpenAPI specification
   is the typed contract"). To keep it from being either a drifting set of copies or a
   single-file merge bottleneck across 18 slices, it is **assembled**: each slice owns the
   path/operation definitions for the endpoints it introduces (co-located with that slice's
   work and reviewed with it), and a build step composes them into the single canonical
   document that is the generation input. Cross-cutting **shared component schemas** — the
   ProblemDetails body, the validation extension, the error-code enum, and the common domain
   DTOs (Task, Project, Cycle, Label, User, Notification view DTOs, paging envelopes) — live
   in **one shared components section owned centrally**, and slices `$ref` them rather than
   redeclaring. This makes "the error contract is one schema" literally true: every slice
   references the same ProblemDetails/enum components.

7. **SignalR hub/notification payloads are typed and generated too.** The live-update and
   notification-toast payloads pushed over the SignalR hub (ADR-0007 view-DTO patches;
   ADR-0008 notification toasts) are **not** exempt from the typed contract. They are declared
   as schemas (reusing the same shared component DTOs as the REST read side) and the
   TypeScript types for hub messages are **generated**, not hand-written, so a client handling
   a live patch or a toast is type-checked against the same source of truth as the REST
   responses. This closes the gap where real-time payloads previously sat outside the
   generated contract; it does not change ADR-0007's transport, group model, or reconciliation
   rule, only how its payloads are typed.

8. **CI regenerate-and-diff gate.** CI regenerates the OpenAPI document from the API (and the
   SignalR payload schemas) and the TypeScript client from that document, then **diffs**: if
   the checked-in canonical document or the generated client is not byte-identical to the
   freshly generated output, the build fails. This is the mechanical enforcement of "machine-
   generated, never hand-synced" (constitution VI) and of the Development Workflow CI gate
   "OpenAPI client generation in sync." A drifted, hand-edited, or stale client cannot merge.

## Consequences

- **Constitution alignment, no bump.** This ADR realizes Principle VI's already-present
  reference to "ADR-0009" and the Architecture & Stack "ProblemDetails-based (ADR-0009)" line;
  it adds detail under an existing reference rather than changing a normative statement, so no
  constitution version change is required. It is the design backing FR-093 (and supports
  FR-049/FR-050 on the Error-UX side).
- **Cross-cutting, realized in every data slice.** The ProblemDetails body, validation
  extension, and error-code enum are shared components every slice references; the Error-UX
  mapping is applied wherever a slice renders a failure (consistent with the product-vision
  rule that resilience FR-049/FR-050 are realized, not merely referenced, in every slice that
  modifies data). Slice 002 owns the canonical optimistic-rollback UX scenarios; later slices
  add their own codes/affordances against the same table.
- **One contract for REST and real-time.** With Decision 7, the generated client is the single
  typed surface for both happy-path REST bodies and live SignalR payloads, removing the
  hand-written drift risk on the real-time side.
- **Generation is load-bearing in CI.** Decision 8 makes the regenerate-and-diff a merge
  blocker, matching the existing CI gate list; a new error code or endpoint forces a
  regeneration commit, and the compiler-checked enum union (Decision 4) forces the client to
  handle it.
- **Maps FR-093** (ProblemDetails RFC 9457 + validation extension + error-code enum modeled
  by generated client + Zod) and supports FR-049/FR-050 (clear message + recovery affordance;
  structured logging with no secret leakage into the body).

## Accepted risks / deferrals

- **Assembled-then-composed OpenAPI rather than a single hand-maintained file** — chosen to
  avoid 18-slice merge contention; the composition step and the CI diff (Decision 8) keep the
  single canonical document authoritative, so the per-slice authoring split introduces no
  divergence.
- **Error-code enum grows additively.** New codes are an additive amendment; removing or
  repurposing a code is a breaking contract change and is treated as such (the compiler-checked
  union and the diff gate make an accidental break fail CI rather than ship silently).
- **No problem-type registry / documentation URLs hosted yet.** RFC 9457 `type` URIs are
  stable identifiers but are not required to dereference to hosted docs in this iteration
  (YAGNI for a single-team instance); the machine-readable `code` enum (Decision 3), not the
  `type` URL, is the client's branching key, so this costs nothing now and is reversible.

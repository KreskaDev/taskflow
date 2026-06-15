# ADR-0007 — Real-time

**Status:** Accepted (2026-06-14); amended 2026-06-15 to apply the design-review
remediation ledger — Decision 1 (transport topology) revised per B9; Decisions 2 and 5
strengthened with live revocation (B5); the "open/visible shared view" term and echo
suppression pinned (open-view 016 resolution); reconciliation granularity stated (B7/LWW).
The prior decisions are otherwise retained.
**Builds on:** ADR-0001 (stack: C# / ASP.NET Core, Next.js, Wolverine + domain events),
ADR-0002 (deployment: single origin, Caddy, single-node Compose), ADR-0003 (domain
model; events are the change signal), and ADR-0006 (Sharing/Membership — `ProjectMembership`
is the authorization basis for group fan-out)
**Drives:** the Real-time bullet in constitution v4.0.0 (Architecture & Stack) and the
real-time reconciliation clause of Principle III; live-subscription authorization
(Principle IX, FR-095)
**Scope:** live propagation of changes to shared projects (US-15, slice 016). Notifications
(US-16) reuse the same transport but are specified in ADR-0008.

## Context

Constitution v4.0.0 makes TaskFlow a connected, multi-user (~10) collaborative app. When a
member changes a shared item, other members viewing it must see the change live, without a
manual refresh (US-15, FR-076). This sits on top of the existing optimistic-UI model
(Principle III): the originating client already paints its own change within 16 ms and the
server remains the source of truth, so the only new requirement is **server-initiated
push** to *other* viewers and a reconciliation rule that does not regress the instant feel.

ADR-0002 explicitly deferred realtime under YAGNI for the single-user product ("No
realtime; TanStack Query invalidation keeps data fresh"). The move to multi-user reverses
that deferral for shared projects only — personal data has no second viewer to notify.

The 2026-06-15 design review added three constraints this ADR now absorbs: the WebSocket
transport carries a fan-out SLA and so cannot ride a hop that does not cleanly upgrade
WebSocket (B9); a real-time subscription is itself an authorization surface that a
mid-session membership/role change must close (B5, Principle IX, FR-095); and the
reconciliation rule needs a stated granularity and conflict-detection mechanism (B7).

## Decisions

1. **Transport: a single SignalR hub, reverse-proxied by Caddy directly to `api`.**
   ASP.NET Core SignalR pushes live updates to connected clients. **Caddy reverse-proxies
   the hub path directly to the `api` container and performs the WebSocket upgrade; the hub
   does NOT route through the Next.js BFF** (revises the prior "Next.js proxies the hub"
   decision per B9). WebSocket is the **SLA-bearing transport** carrying the fan-out budget
   (Decision in Performance), with SignalR's built-in fallback negotiation behind it. The
   hub still rides the existing single public origin (ADR-0002): no new public port or
   surface is introduced, and the `api` port remains internal-only — Caddy is the only
   externally reachable hop. BFF→API session identity is carried on the hub connection as
   the signed short-lived token already used for REST (constitution Principle IX, FR-091).
2. **Group model: one group per shared project, gated by authorization at join.** A member
   viewing a shared project joins that project's group (`project:{projectId}`); the server
   fans a change out to that group only. Group membership is gated by the application-layer
   authorization policy (Principle IX, FR-066): a connection may join a project group only
   if the caller has current `ProjectMembership` on it. Personal projects have no group —
   there is no second viewer, so nothing is pushed.
3. **Change source: domain events, not ad-hoc broadcasts.** The hub is driven off the same
   domain events that already flow through Wolverine (ADR-0003). A handler projects the
   committed change to a view DTO and pushes it to the affected project group **after** the
   write transaction commits (push reflects server-authoritative state, never an
   uncommitted optimistic guess). This keeps the write path unchanged and makes real-time a
   read-side concern.
4. **Reconciliation: whole-entity last-write-wins, yielding to in-flight local edits
   (Principle III, FR-077).** The reconciliation granularity is the **whole entity**, not a
   per-field merge: an inbound remote patch replaces the receiving client's view of that
   entity under last-write-wins. This is the accepted **~10-user tradeoff** (B7) — at this
   concurrency, simultaneous edits of the *same* entity are rare, and whole-entity LWW
   avoids the complexity and bug surface of field-level CRDT/merge. To make a lost write
   *visible* rather than silent, each entity carries an **optimistic-concurrency token**
   (e.g. a `RowVersion`/`xmin`-backed version): a write that targets a stale token is
   detected as a conflict and surfaced to the user (409 / FR-049), rather than blindly
   overwritten. The remote patch is still applied under LWW **except** when the receiving
   client has a pending local optimistic mutation on the same entity: it is then deferred
   until that local mutation's server acknowledgement resolves, and reconciled afterward. A
   remote update MUST NOT clobber an in-flight local edit.
5. **Echo suppression by originating connection id; "open/visible shared view" defined.**
   An **open/visible shared view** is a client currently **subscribed to that shared
   project's SignalR group** (it has joined `project:{projectId}` and is rendering that
   project's data) — this is the precise referent of "other members' open shared views" in
   the fan-out budget and of FR-076/FR-078. The **originating client suppresses the echo of
   its own change**: each fan-out frame carries the **originating connection id**, and a
   client ignores a frame stamped with its own connection id (it already holds the
   optimistic result, reconciled by its own server-ack). Self-suppression is keyed on
   connection id, not user id, so a user's *other* sessions/tabs still receive and apply the
   change.
6. **Live revocation: a membership/role change evicts the connection and re-checks at
   fan-out (B5, Principle IX, FR-095).** A mid-session membership or role change MUST
   immediately revoke the affected user's live subscription: the server **removes the
   affected connection(s) from the project's SignalR group** on the membership/role-change
   event, **and** re-checks current membership at fan-out time as a defense-in-depth backstop
   (so a connection that slips eviction still receives no patch it is no longer entitled
   to). A removed or downgraded member therefore receives **no further patches**, and the
   client is forced to a **403 re-sync** (treat as access-loss: drop the now-stale view and
   refetch, surfacing "no longer available" where the project is gone). A role *downgrade*
   that still permits read (editor→viewer) keeps the subscription but the client's
   capabilities are re-derived; a change to *no access* (removed/unshared/leave) is the
   evict-and-403 path.
7. **Reconnect re-syncs visible shared views (FR-078).** SignalR automatic reconnection is
   enabled. On reconnect the client rejoins the groups for its currently **open/visible
   shared views** (Decision 5) — each rejoin re-runs the join authorization check
   (Decision 2), so a membership change during the gap collapses into the same 403 re-sync
   as Decision 6 — and refetches their current state (a TanStack Query invalidation of the
   visible shared queries), so any changes missed during the gap are pulled rather than
   replayed. No per-client message buffering or backlog is kept on the server.
8. **No presence.** The hub carries data changes only. Who-is-online / who-is-viewing
   indicators, cursors, and typing indicators are out of scope (constitution Operational
   scope; OOS-15). Group membership exists purely to scope fan-out, not to expose presence.

## Performance

- **Fan-out budget p95 < 1000 ms commit-to-paint (SC-014, FR-076, Performance Standards).**
  A change to a shared item MUST reach other members' open/visible shared views within ~1
  second of commit, measured commit-to-paint on the receiving client. WebSocket is the
  SLA-bearing transport for this budget (Decision 1). With ~10 concurrent users (SC-015) and
  per-project groups, fan-out volume is small; the event handler runs after commit so it
  adds no latency to the originating write (which keeps its p95-200 ms budget, Principle III
  / SC-012).

## Consequences

- Slice 016 (`specs/016-real-time-collaboration`) materializes: the SignalR hub + the
  authorization-checked join, the post-commit event→group push handler, the view DTOs it
  pushes (each carrying its originating connection id and optimistic-concurrency token), the
  membership/role-change eviction + fan-out re-check (Decision 6), and the client-side
  reconciliation layer over TanStack Query (whole-entity LWW, conflict surfacing,
  defer-to-in-flight, echo suppression, reconnect re-sync). Its acceptance scenarios
  (US-15.AS-01..03) map directly onto Decisions 4–7.
- This supersedes ADR-0002 Decision 10 ("No realtime") for shared projects. The deployment
  topology adds **one routing rule**: Caddy proxies the hub path directly to `api` and
  upgrades WebSocket there, rather than via the Next.js origin (B9). No new container, port,
  or public surface is introduced; the `api` port stays internal-only.
- The membership/role-change eviction (Decision 6) is the real-time half of constitution
  Principle IX / FR-095; ADR-0006 (Sharing/Membership) emits the membership/role-change
  events this handler consumes.
- Maps FR-076 (live propagation within the fan-out budget), FR-077 (in-flight local edit not
  clobbered; whole-entity LWW with conflict token after server-ack), FR-078 (reconnect
  re-sync of visible shared views), FR-095 (live-subscription revocation/re-auth).

## Accepted risks / deferrals

- **Whole-entity LWW rather than field-level merge** — accepted as the ~10-user tradeoff
  (B7). Two members editing different fields of the *same* entity within the fan-out window
  will have the later write win the whole entity; the optimistic-concurrency token makes the
  overwrite *detectable and surfaced* (FR-049) rather than silent. Field-level CRDT/merge is
  deferred under YAGNI; it would only pay off at concurrency well above this scope.
- **Refetch-on-reconnect rather than message replay** — a missed-message backlog buffer is
  deferred under YAGNI; at ~10 users a refetch of the visible shared views is cheap and
  strictly correct (it reads server-authoritative state).
- **Single-node SignalR (no backplane).** The Docker Compose deployment is single-node
  (ADR-0002), so no Redis backplane is needed; in-process group membership and connection
  eviction (Decision 6) are exact on a single node. A backplane would only become necessary
  under multi-node scale-out, which is out of scope for this iteration.

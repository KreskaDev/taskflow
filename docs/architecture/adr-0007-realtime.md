# ADR-0007 — Real-time

**Status:** Accepted (2026-06-14)
**Builds on:** ADR-0001 (stack: C# / ASP.NET Core, Next.js, Wolverine + domain events),
ADR-0003 (domain model; events are the change signal), and the Sharing/Membership ADR
(ProjectMembership is the authorization basis for group fan-out)
**Drives:** the Real-time bullet in constitution v3.0.0 (Architecture & Stack) and the
real-time reconciliation clause of Principle III
**Scope:** live propagation of changes to shared projects (US-15, slice 016). Notifications
(US-16) reuse the same transport but are specified in their own ADR.

## Context

Constitution v3.0.0 makes TaskFlow a connected, multi-user (~10) collaborative app. When a
member changes a shared item, other members viewing it must see the change live, without a
manual refresh (US-15, FR-076). This sits on top of the existing optimistic-UI model
(Principle III): the originating client already paints its own change within 16 ms and the
server remains the source of truth, so the only new requirement is **server-initiated
push** to *other* viewers and a reconciliation rule that does not regress the instant feel.

ADR-0002 explicitly deferred realtime under YAGNI for the single-user product ("No
realtime; TanStack Query invalidation keeps data fresh"). The move to multi-user reverses
that deferral for shared projects only — personal data has no second viewer to notify.

## Decisions

1. **Transport: a single SignalR hub.** ASP.NET Core SignalR pushes live updates to
   connected clients over the existing single origin (ADR-0002): the Next.js app proxies
   the hub endpoint to the `api` container over the internal Docker network, so there is no
   new public surface and no CORS. WebSocket is the primary transport with SignalR's
   built-in fallback negotiation.
2. **Group model: one group per shared project.** A member viewing a shared project joins
   that project's group (`project:{projectId}`); the server fans a change out to that group
   only. Group membership is gated by the application-layer authorization policy
   (Principle IX, FR-066): a connection may join a project group only if the caller has
   `ProjectMembership` on it. Personal projects have no group — there is no second viewer,
   so nothing is pushed.
3. **Change source: domain events, not ad-hoc broadcasts.** The hub is driven off the same
   domain events that already flow through Wolverine (ADR-0003). A handler projects the
   committed change to a view DTO and pushes it to the affected project group **after** the
   write transaction commits (push reflects server-authoritative state, never an
   uncommitted optimistic guess). This keeps the write path unchanged and makes real-time a
   read-side concern.
4. **Reconciliation: last-write-wins, yielding to in-flight local edits (Principle III,
   FR-077).** An inbound remote patch is applied under last-write-wins **except** when the
   receiving client has a pending local optimistic mutation on the same item: the remote
   patch is then deferred until that local mutation's server acknowledgement resolves, and
   reconciled afterward. A remote update MUST NOT clobber an in-flight local edit. The
   originating client ignores the echo of its own change (it already has the optimistic
   result, reconciled by its own server-ack).
5. **Reconnect re-syncs visible shared views (FR-078).** SignalR automatic reconnection is
   enabled. On reconnect the client rejoins the groups for its currently visible shared
   views and refetches their current state (a TanStack Query invalidation of the visible
   shared queries), so any changes missed during the gap are pulled rather than replayed.
   No per-client message buffering or backlog is kept on the server.
6. **No presence.** The hub carries data changes only. Who-is-online / who-is-viewing
   indicators, cursors, and typing indicators are out of scope (constitution Operational
   scope; OOS-15). Group membership exists purely to scope fan-out, not to expose presence.

## Performance

- **Fan-out budget ~1 s (SC-014, FR-076, Performance Standards).** A change to a shared item
  MUST reach other members' open shared views within ~1 second of commit. With ~10
  concurrent users (SC-015) and per-project groups, fan-out volume is small; the event
  handler runs after commit so it adds no latency to the originating write (which keeps its
  p95-200 ms budget, Principle III / SC-012).

## Consequences

- Slice 016 (`specs/016-real-time-collaboration`) materializes: the SignalR hub + the
  authorization-checked join, the post-commit event→group push handler, the view DTOs it
  pushes, and the client-side reconciliation layer over TanStack Query (defer-to-in-flight,
  reconnect re-sync). Its acceptance scenarios (US-15.AS-01..03) map directly onto Decisions
  4–5.
- This supersedes ADR-0002 Decision 10 ("No realtime") for shared projects. The deployment
  topology is otherwise unchanged: the hub rides the single origin already proxied by Caddy;
  no new container, port, or public surface is introduced.
- Maps FR-076 (live propagation within the fan-out budget), FR-077 (in-flight local edit not
  clobbered; LWW after server-ack), FR-078 (reconnect re-sync of visible shared views).

## Accepted risks / deferrals

- **Refetch-on-reconnect rather than message replay** — a missed-message backlog buffer is
  deferred under YAGNI; at ~10 users a refetch of the visible shared views is cheap and
  strictly correct (it reads server-authoritative state).
- **Single-node SignalR (no backplane).** The Docker Compose deployment is single-node
  (ADR-0002), so no Redis backplane is needed. A backplane would only become necessary under
  multi-node scale-out, which is out of scope for this iteration.

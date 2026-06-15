# ADR-0002 — Deployment & operations

**Status:** Accepted (2026-06-14), amended 2026-06-15
**Builds on:** ADR-0001 (technical foundation)
**Drives:** the Deployment & Operations parts of the constitution's Architecture & Stack
section (v4.0.0)

## Context

TaskFlow is a connected, collaborative multi-user (~10) web app (ADR-0001, constitution
v4.0.0). It must be containerized and continuously deployed to a Hetzner VPS that already
hosts another application. A reverse proxy (Caddy) is already installed on the target host.
This ADR records the deployment, networking, and operations decisions. Access control is an
application-layer concern (Google OAuth + admission control + sessions, ADR-0004 / Principle
IX) and is out of scope here; Caddy keeps TLS + reverse proxy only.

## Decisions

1. **Containerization.** Multi-stage Dockerfiles for `apps/web` (Next.js `output: 'standalone'`)
   and `apps/api` (.NET publish). Orchestrated with **Docker Compose**.
2. **Host.** A single **Hetzner VPS** (its own IP address, distinct from the existing
   application's server) runs the Compose stack: `web`, `api`, `postgres` (and an
   **optional** `rabbitmq` only if a real cross-process consumer is introduced — see
   ADR-0001 note and decision 12).
3. **Topology.** Single-node Docker Compose — no Swarm/Kubernetes (right-sized for ~10 users).
4. **Networking & ports.**
   - The existing app on the host reserves the **3xxx** range; this app uses the **43xx**
     range. The web container is published to `127.0.0.1:4310`.
   - The api container is published to **`127.0.0.1:4311`** so that host-installed Caddy can
     reach it directly for the SignalR hub route (decision 6). Binding to loopback only keeps
     the API port **not externally reachable** (Principle IX); it is not a public surface.
   - **PostgreSQL (and the optional RabbitMQ) are never published publicly** — they are
     reachable only on the internal Docker network (and/or bound to localhost).
5. **Edge & routing (one public origin, two upstreams).** Host-installed **Caddy** terminates
   TLS for a **single public origin** (one hostname, one certificate) and reverse-proxies it.
   Two upstreams sit behind that one origin:
   - the **web** container for the app and its REST `/api` calls — the Next.js BFF proxies
     `/api` to the `api` container over the internal Docker network, carrying the BFF→API
     signed short-lived identity token (ADR-0004 / Principle IX); and
   - the **api** container for the real-time hub path (decision 6).

   Because everything is served from the same public origin there is **no separate API host
   and no CORS**. Caddy does **not** gate access: public access is controlled at the
   application layer by **Google OAuth + admission control + sessions** (ADR-0004,
   Principle IX), not by any edge password.
6. **Real-time transport.** **SignalR** live updates (Principle III, constitution Architecture
   & Stack) are the **SLA-bearing transport**. Caddy reverse-proxies the **SignalR hub path
   directly to the `api` container** (`127.0.0.1:4311`), handling the WebSocket upgrade — it
   does **NOT** route the hub through Next.js, because **Next.js cannot proxy WebSocket
   upgrades**. Only the hub path bypasses the BFF; all REST `/api` traffic continues to flow
   through the Next.js BFF (decision 5). Hub authentication is an application-layer concern
   (ADR-0004), not the BFF token path. No new public origin or host is introduced — the hub
   shares the single public origin, just a different Caddy upstream.
7. **Registry & CI/CD.** Images are published to **GHCR** with **immutable git-SHA image
   tags** (one tag per commit; never a moving `latest` in production). **GitHub Actions**
   builds and tests both stacks, pushes images, and deploys to the VPS over SSH for a
   **production-only** environment (no staging yet).
8. **Migrations on deploy & rollback.** Each deploy runs a **`pg_dump` backup first**
   (Principle VII), then applies **EF Core migrations** following **expand/contract**
   discipline (additive expand before the new code; contract only after it is confirmed
   live), then starts/updates services. **Rollback on failure** is a documented runbook:
   redeploy the **prior pinned git-SHA image**, and restore the **pre-migration dump** when
   the schema had changed. Expand/contract keeps a single-step rollback safe because the prior
   image still runs against the expanded schema.
9. **Backups.** A **scheduled `pg_dump`** runs **independently of deploys**, on its own
   cadence with a defined **retention window**. At least one copy is kept **offsite** so loss
   of the VPS is survivable. "Restorable" is not assertable until verified: a **CI restore-test**
   restores a backup into a throwaway database and asserts integrity (Principle VII). This is
   in addition to the pre-migration dump of decision 8.
10. **Secrets.** The session signing key, Google OAuth client secret, database (and optional
    broker) credentials, and deploy SSH keys are injected at runtime via an **env-file
    (`chmod 600`) or Docker secrets**. Secrets MUST NOT be committed to the repository, baked
    into container images, or written to logs or error context (Principle XII).
11. **Health & startup ordering.** Compose **healthchecks** gate service readiness:
    `postgres` uses **`pg_isready`**; `api` (and `web`) declare **`depends_on:
    { postgres: { condition: service_healthy } }`** so the stack starts in dependency order and
    migrations never run against an unready database.
12. **Observability.** Structured logs (Serilog on the API; structured client logs) to
    **stdout → `docker logs`**. No log aggregator yet.
13. **Runtimes.** .NET 9, Node 22 LTS.

## Consequences

- The compose stack (with `pg_isready` healthchecks and `depends_on: service_healthy`
  ordering), Caddyfile (TLS + single-origin reverse proxy with two upstreams — web for the
  app and REST `/api`, api directly for the SignalR hub WebSocket upgrade), and a GitHub
  Actions workflow (build → test → push git-SHA-tagged images to GHCR → SSH deploy → backup →
  expand-migrate → up, with a documented rollback path) become part of the monorepo,
  materialized during slice-001 planning/implementation.
- A scheduled, offsite, restore-tested backup job (independent of deploys) and the secrets
  env-file/Docker-secrets convention are operational artifacts of the same slice.
- The constitution's Architecture & Stack section (v4.0.0) carries these Deployment &
  Operations constraints (internal-only data services; one public origin with the hub proxied
  by Caddy directly to api; backup-before-migrate plus scheduled offsite restore-tested
  backups; immutable git-SHA tags with a rollback path; runtime-injected secrets;
  healthchecked startup ordering).

## Accepted risks / deferrals

- **Optional RabbitMQ** — not deployed; the default stack uses Wolverine's durable
  Postgres-backed local queues. A `rabbitmq` service is added only when a real cross-process
  consumer exists (see ADR-0001 note).
- **Production-only** (no staging) and **no log aggregation** — deferred under YAGNI;
  revisit when a concrete need appears.
- **Email/push notifications, presence indicators** — out of scope for this iteration
  (constitution v4.0.0 Operational scope); in-app notifications and SignalR real-time are
  in scope.

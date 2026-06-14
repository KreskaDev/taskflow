# ADR-0002 — Deployment & operations

**Status:** Accepted (2026-06-14), amended 2026-06-14
**Builds on:** ADR-0001 (technical foundation)
**Drives:** the Deployment & Operations parts of the constitution's Architecture & Stack
section (v3.0.0)

## Context

TaskFlow is a connected, collaborative multi-user (~10) web app (ADR-0001, constitution
v3.0.0). It must be containerized and continuously deployed to a Hetzner VPS that already
hosts another application. A reverse proxy (Caddy) is already installed on the target host.
This ADR records the deployment, networking, and operations decisions. Access control is an
application-layer concern (Google OAuth + sessions, ADR-0004 / Principle IX) and is out of
scope here; Caddy keeps TLS + reverse proxy only.

## Decisions

1. **Containerization.** Multi-stage Dockerfiles for `apps/web` (Next.js `output: 'standalone'`)
   and `apps/api` (.NET publish). Orchestrated with **Docker Compose**.
2. **Host.** A single **Hetzner VPS** (its own IP address, distinct from the existing
   application's server) runs the Compose stack: `web`, `api`, `postgres`, `rabbitmq`.
3. **Topology.** Single-node Docker Compose — no Swarm/Kubernetes (right-sized for ~10 users).
4. **Networking & ports.**
   - The existing app on the host reserves the **3xxx** range; this app uses the **43xx**
     range. The web container is published to `127.0.0.1:4310`.
   - **PostgreSQL and RabbitMQ are never published publicly** — they are reachable only on
     the internal Docker network (and/or bound to localhost).
5. **Edge & routing (single origin).** Host-installed **Caddy** terminates TLS and
   reverse-proxies a **single origin** to the web container. The Next.js app proxies `/api`
   to the `api` container over the internal Docker network — so there is **one public
   surface, no separate API host, and no CORS**. Caddy does **not** gate access: public
   access is controlled at the application layer by **Google OAuth + sessions** (ADR-0004,
   Principle IX), not by any edge password.
6. **Real-time transport.** **SignalR** live updates (Principle III, constitution Architecture
   & Stack) ride the **existing single web origin on the single published port** — no new
   public port and no separate host. Caddy forwards the WebSocket upgrade over the same
   single origin to the web container, which proxies it to the `api` container on the
   internal Docker network. No additional edge surface is introduced.
7. **Registry & CI/CD.** Images are published to **GHCR**. **GitHub Actions** builds and
   tests both stacks, pushes images, and deploys to the VPS over SSH for a **production-only**
   environment (no staging yet).
8. **Migrations on deploy.** Each deploy runs a **`pg_dump` backup first** (Principle VII),
   then applies **EF Core migrations**, then starts/updates services.
9. **Backups.** `pg_dump` to a **local volume only** — accepted risk: loss of the VPS loses
   the backups. Offsite backup is explicitly deferred.
10. **Observability.** Structured logs (Serilog on the API; structured client logs) to
    **stdout → `docker logs`**. No log aggregator yet.
11. **Runtimes.** .NET 9, Node 22 LTS.

## Consequences

- The compose stack, Caddyfile (TLS + single-origin reverse proxy, forwarding the WebSocket
  upgrade for SignalR), and a GitHub Actions workflow (build → test → GHCR → SSH deploy →
  backup → migrate → up) become part of the monorepo, materialized during slice-001
  planning/implementation.
- The constitution's Architecture & Stack section (v3.0.0) carries these Deployment &
  Operations constraints (internal-only data services, single-origin edge, backup-before-
  migrate, real-time over the single origin).

## Accepted risks / deferrals

- **Local-only backups** (no offsite copy) — single point of failure if the VPS is lost;
  offsite backup deferred.
- **RabbitMQ** included though no cross-process consumer exists yet (see ADR-0001 note).
- **Production-only** (no staging) and **no log aggregation** — deferred under YAGNI;
  revisit when a concrete need appears.
- **Email/push notifications, presence indicators** — out of scope for this iteration
  (constitution v3.0.0 Operational scope); in-app notifications and SignalR real-time are
  in scope.

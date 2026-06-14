# ADR-0002 — Deployment & operations

**Status:** Accepted (2026-06-14)
**Builds on:** ADR-0001 (technical foundation)
**Drives:** the Deployment & Operations additions to constitution v2.0.0 (Architecture & Stack)

## Context

TaskFlow is a connected, single-user web app (ADR-0001). It must be containerized and
continuously deployed to a Hetzner VPS that already hosts another application. A reverse
proxy (Caddy) is already installed on the target host. This ADR records the deployment,
networking, and operations decisions.

## Decisions

1. **Containerization.** Multi-stage Dockerfiles for `apps/web` (Next.js `output: 'standalone'`)
   and `apps/api` (.NET publish). Orchestrated with **Docker Compose**.
2. **Host.** A single **Hetzner VPS** (its own IP address, distinct from the existing
   application's server) runs the Compose stack: `web`, `api`, `postgres`, `rabbitmq`.
3. **Topology.** Single-node Docker Compose — no Swarm/Kubernetes (right-sized for one user).
4. **Networking & ports.**
   - The existing app on the host reserves the **3xxx** range; this app uses the **43xx**
     range. The web container is published to `127.0.0.1:4310`.
   - **PostgreSQL and RabbitMQ are never published publicly** — they are reachable only on
     the internal Docker network (and/or bound to localhost).
5. **Edge & routing (single origin).** Host-installed **Caddy** terminates TLS and
   reverse-proxies a **single origin** to the web container. The Next.js app proxies `/api`
   to the `api` container over the internal Docker network — so there is **one public
   surface, no separate API host, and no CORS**.
6. **Access gate.** The app has no in-domain authentication (single-user). Because it is now
   internet-facing, **Caddy `basic_auth`** (a single credential, over HTTPS) gates all
   public access. Credentials live in host/GitHub secrets, never in the repo.
7. **Registry & CI/CD.** Images are published to **GHCR**. **GitHub Actions** builds and
   tests both stacks, pushes images, and deploys to the VPS over SSH for a **production-only**
   environment (no staging yet).
8. **Migrations on deploy.** Each deploy runs a **`pg_dump` backup first** (Principle VII),
   then applies **EF Core migrations**, then starts/updates services.
9. **Backups.** `pg_dump` to a **local volume only** — accepted risk: loss of the VPS loses
   the backups. Offsite backup is explicitly deferred.
10. **No realtime.** No websockets/SignalR; TanStack Query invalidation keeps data fresh
    (single user, typically one session). YAGNI.
11. **Observability.** Structured logs (Serilog on the API; structured client logs) to
    **stdout → `docker logs`**. No log aggregator yet.
12. **Runtimes.** .NET 9, Node 22 LTS.

## Consequences

- The compose stack, Caddyfile (with `basic_auth` + single-origin reverse proxy), and a
  GitHub Actions workflow (build → test → GHCR → SSH deploy → backup → migrate → up) become
  part of the monorepo, materialized during slice-001 planning/implementation.
- Constitution v2.0.0's Architecture & Stack section is extended with these Deployment &
  Operations constraints (access gate, internal-only data services, backup-before-migrate).

## Accepted risks / deferrals

- **Local-only backups** (no offsite copy) — single point of failure if the VPS is lost.
- **RabbitMQ** included though no cross-process consumer exists yet (see ADR-0001 note).
- **Production-only** (no staging), **no realtime**, **no log aggregation** — all deferred
  under YAGNI; revisit when a concrete need appears.

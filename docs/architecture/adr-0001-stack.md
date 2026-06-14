# ADR-0001 — TaskFlow technical foundation

**Status:** Accepted (2026-06-14)
**Supersedes:** the offline-only, local-first desktop assumption of constitution v1.1.0
**Drives:** constitution v2.0.0 amendment and the product-vision technical-requirement updates

## Context

TaskFlow's feature set (10 user stories, 12 vertical slices) was specified against an
offline-only, local-first, single-user **desktop** model. A decision was taken to deliver
it instead as a **connected, single-user web application** with a dedicated backend and a
relational database, adopting a Domain-Driven Design backend. This is a technical
re-foundation: the feature scope is unchanged, but several constitutional principles and a
small set of requirements (FR-041, SC-004, etc.) are no longer accurate and must be amended.

## Decisions

1. **Product model:** Online, single-user, no authentication. A backend serves a single
   owner; the data model carries no owner/org/role fields (single-user constraint retained).
2. **Repository:** Monorepo in `taskflow/` — `apps/web` (Next.js), `apps/api` (.NET
   solution), alongside the existing `specs/` and `.specify/`.
3. **Frontend:** Next.js + TypeScript (strict). **Hybrid rendering** — React Server
   Components render the initial shell with server-side skeletons; interactive areas are
   client islands using TanStack Query with optimistic mutations and Suspense skeletons.
4. **API contract:** REST with an OpenAPI specification as the source of truth; a typed
   TypeScript client is generated from it.
5. **Backend:** C# / ASP.NET Core with **full tactical DDD** — aggregate roots, value
   objects, domain events, repositories, and a CQRS command/query split.
6. **Messaging & CQRS dispatch:** **Wolverine** for command/query handling and the
   transactional outbox, with **RabbitMQ** as the message transport from day one.
7. **Write persistence:** **EF Core** (code-first migrations) over **PostgreSQL**;
   aggregates are state-stored. Migrations are the schema source of truth.
8. **Read side:** CQRS read projections (query handlers → DTOs) optimized for the view
   requirements (Today, Upcoming, Board, Cycle).
9. **Validation:** Zod at frontend trust boundaries; FluentValidation / data annotations at
   the API boundary.
10. **Testing:** xUnit + integration tests (API); Vitest + Playwright (web). Test-first.

## Proposed domain model (DDD)

- **Bounded context:** Task Management (single context for the MVP).
- **Aggregate roots:** `Task` (owns its `RecurrenceRule`), `Project`, `Cycle`.
- **Value objects:** `Priority` (P0–P3), `DateRange`, `RecurrenceRule`, status enums.
- **Entity:** `Label` (many-to-many references to tasks).
- **Domain events:** e.g. `TaskCompleted`, `RecurringInstanceGenerated`, `CycleClosed`,
  dispatched via Wolverine.
- **Invariants enforced in aggregates:** single active cycle (Cycle), one-level project
  nesting (Project), recurrence/status transitions and timestamp rules (Task).

## Consequences

- **Constitution → v2.0.0 (MAJOR).** Principle V (Offline-Only) is replaced by a
  Connected, Server-Authoritative principle; III (Instant Response) and IV (Minimalist UI)
  are amended to permit skeletons and a networked latency budget while preserving the
  optimistic, instant *feel*; VI (Type Safety) is extended across TS + C# with OpenAPI as the
  contract; VII (Data Integrity) adapts migrations/backup to EF Core + Postgres; I, II, VIII
  and the single-user constraint are retained. A new Architecture & Stack section codifies
  the decisions above.
- **Product-vision amendments.** FR-041 and SC-004 (zero network / offline) are rewritten;
  SC-002/SC-003 (cold start / 16 ms) are reinterpreted for web + optimistic UI; ASM-02
  (desktop) → web; ASM-08 (local format) → documented Postgres schema; SC-010/011/012 perf
  budgets are reinterpreted across the client/server split.
- **Slice ripple is concentrated in slice 001** (the foundation owns most affected IDs),
  plus two one-line cross-references in slices 006 and 008. The 12-slice feature content is
  otherwise unchanged — no re-slicing required.

## Notes

- **RabbitMQ was included against a YAGNI caution.** For a single-user, single-service app
  there is no current cross-process consumer; Wolverine's durable Postgres-backed local
  queues would cover the only async need in scope (FR-009 deferred recurrence). RabbitMQ was
  adopted deliberately for an anticipated multi-service future; Wolverine keeps the transport
  swappable, so this choice is reversible at low cost.

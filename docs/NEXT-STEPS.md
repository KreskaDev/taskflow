# TaskFlow — Next Steps (execution roadmap)

This is the ordered work plan to take TaskFlow from "specs complete, no code" to a deployed, maintained product. Pair it with `docs/handoff-prompt.md` (orientation) and the four sources of truth (`constitution.md`, `product-vision.md`, `adr-0001..0009`, `remediation-decisions.md`).

## How to use this file
- Work **top to bottom**. Each slice is one unit run through the loop: **`/speckit-plan` → `/speckit-tasks` → `/speckit-implement` (test-first) → verify → ship.**
- Produce **one stage at a time, present it, and check in** before the next. **Never auto-advance.**
- **The next step at any moment** = the first unchecked box under the **active slice** (`.specify/feature.json` → `feature_directory`). If a slice is fully done, advance `feature.json` to the next and start its `/speckit-plan`.
- `/speckit-plan` and `/speckit-tasks` produce **documents only** — no code under `apps/**` until `/speckit-implement`.

## Phase 0 — Orient (once)
- [ ] Read `docs/handoff-prompt.md` and `docs/HANDOFF.md`.
- [ ] Skim the constitution (v4.0.0) and `remediation-decisions.md`.
- [ ] Confirm `.specify/feature.json` → `specs/001-accounts-and-auth`.
- [ ] Confirm git state is clean (the two handoff docs may be untracked — that's expected).

## Global cross-cutting checklist — apply in EVERY slice it touches
- [ ] Authorization: deny-by-default, **dispatch by project visibility**; every data handler ships an **allow AND a deny test** (SC-016).
- [ ] Accessibility: FR-031, FR-042–047, **FR-101** (ARIA-live for real-time/toasts; dialog focus contract).
- [ ] Resilience: FR-049–051 (clear errors + recovery, structured logs, backup hook); soft-delete/undo for destructive ops.
- [ ] Type safety: TS strict; C# nullable + analyzers-as-errors; **regenerate the OpenAPI TS client** (fail CI on drift); Zod (web) + FluentValidation (api) at boundaries.
- [ ] Time: store UTC; compute in **Europe/Warsaw**.
- [ ] Perf budgets: optimistic <16ms, server p95 <200ms, fan-out <1s, search <50ms, 60fps @10k.
- [ ] Tests green (xUnit + integration + Vitest + Playwright); `/speckit-analyze` clean; OOS-01..19 still honored.

---

## Phase 1 — Foundation (Slice 001 · accounts-and-auth)
This slice bootstraps the whole product; its plan is heavier than a feature slice.
- [ ] **`/speckit-plan` 001** → `plan.md` (passes the v4.0.0 Constitution Check). The plan MUST design:
  - [ ] Monorepo: `apps/web` (pnpm + Next.js + TS strict, hybrid RSC + TanStack Query) and `apps/api` (.NET ASP.NET Core, tactical DDD, two bounded contexts).
  - [ ] Google OAuth + **admission gate** (allowlist / Workspace `hd`) + HttpOnly cookie sessions via the Next.js BFF; CSRF/SameSite; PKCE/state/nonce/id_token; server-side sign-out; BFF→API signed short-lived token.
  - [ ] `User` entity + Identity & Access context; **deny-by-default authorization policy** skeleton (dispatch by visibility) used by every handler.
  - [ ] Account deletion / erasure cascade (Principle XI).
  - [ ] Secrets handling (`.env.example`, runtime injection, never committed/logged); CSP + security headers (Principle XII).
  - [ ] OpenAPI pipeline + generated TS client + ProblemDetails error scaffolding (the full FR-093 contract is owned by slice 002).
  - [ ] EF Core + PostgreSQL baseline + migration tooling; backup-before-migrate + **CI restore-test**.
  - [ ] Docker Compose (web, api, postgres; healthchecks) + Caddyfile (TLS + single web origin + **SignalR hub proxied directly to api**).
  - [ ] GitHub Actions CI: lint, type-check, tests (incl. authz), OpenAPI drift gate, build multi-stage images → **GHCR** (immutable git-SHA tags) → deploy to **Hetzner** over SSH.
- [ ] **`/speckit-tasks` 001** → `tasks.md` (dependency-ordered, test-first).
- [ ] **`/speckit-implement` 001** (Red-Green-Refactor): build the scaffold + auth + authz policy; allow+deny tests; full suite green.
- [ ] **Run & verify**: sign-in (admitted vs rejected), session/sign-out, profile, account deletion.
- [ ] **First deploy**: CI → GHCR → Hetzner; verify live behind Caddy (TLS, web origin, hub upgrade).
- [ ] On approval, **advance `feature.json` → `specs/002-task-capture`**.

---

## Phase 2 — Core single-user loop (slices 002–006)
Run the full loop (plan → tasks → implement → verify → ship) for each:
- [ ] **002 task-capture** — keyboard capture → per-user list; `createdBy`; **soft-delete from day one**; the ProblemDetails error contract (FR-093); optimistic-create/delete rollback UX.
- [ ] **003 natural-language-dates** — Polish NL due-date parsing (client parses for optimistic paint, server validates — does *not* re-parse Polish); UTC + Europe/Warsaw; `due_date` `has_time` flag (FR-092).
- [ ] **004 project-management** — projects, 1-level nesting, archive, Inbox; `ownerId` + personal `visibility`; hierarchy lifecycle (re-parent, delete-parent prompt, edit, unarchive); `M` move.
- [ ] **005 daily-planning** — Today (incl. overdue) / Upcoming; priorities; full task editor; nav `G I`/`G U`. (Reclassify authz as dispatch — not "Tier A".)
- [ ] **006 labels** — reusable labels with `ownerId` (per-user); `L` selector. (Applying a label follows the task's own authorization.)

## Phase 3 — Collaboration (slices 007–010)
- [ ] **007 project-sharing-membership** — convert personal↔shared; invite **existing Users by email**; owner/editor/viewer; **transfer-owner** + **last-owner guard**; unshare/remove shows **blast radius** + clears non-owner assignments. (Tier-B membership/role authz originates here.)
- [ ] **008 task-assignment** — multiple assignees (**members only**, enforced + deny test); "assigned to me"; emits `TaskAssigned` (delta + `actorUserId`) for slice 017.
- [ ] **009 comments-mentions** — comment threads + @mentions; **editor/owner only (viewers can't comment)**; sanitized bodies; emits `UserMentioned` for slice 017.
- [ ] **010 project-board-kanban** — Kanban board + groupable list, **access-scoped** (viewing/regrouping = read for any member; moving columns = editor/owner write). Cancelled tasks hidden.

## Phase 4 — Time-boxing & power features (slices 011–013)
- [ ] **011 cycles** — team-wide 2-week cycles (explicit authz; global active-cycle index); metrics; rollover; "next cycle" = next planned by start date; **completes the FR-028/FR-029 shortcut umbrellas**.
- [ ] **012 recurring-tasks** — recurrence rules; successor carries rule + anchor + assignees (**re-validated vs current membership; no re-notification**); **server-side Wolverine scheduled spawn** (not client startup); monthly day-of-month with end-of-month clamp; idempotent.
- [ ] **013 command-palette-search** — `Ctrl+K` **server search endpoint**, authorization-filtered inside the <50ms budget over the caller's accessible set; `/` filter; never leaks inaccessible items.

## Phase 5 — Safety, portability, real-time, polish (slices 014–018)
- [ ] **014 undo** — 30s undo via **Undo Snapshot + soft-delete**; restore is a write under **LWW**; original-actor only; covers task/project delete, bulk moves, cycle rollover. (Membership/role changes are confirm-gated, not undoable.)
- [ ] **015 data-export-import** — JSON/CSV export + TaskFlow/Todoist import, **owner/accessible-scoped** (SC-005); FR-051 backup before bulk import.
- [ ] **016 real-time-collaboration** — SignalR hub (**Caddy → api**); live updates on shared views; **mid-session membership change evicts** the subscriber (403 re-sync); whole-entity LWW + optimistic-concurrency token. *(Build before 017 — it's the toast channel.)*
- [ ] **017 notifications** — in-app center + live toasts; **source re-authorized at click**; closed "changed" set (status/due/assignee/project-move); self-actions suppressed via `actorUserId`; consumes 008/009/016 events.
- [ ] **018 appearance-theming** — dark/light following OS; per-user preference persisted; no flash-of-wrong-theme.

---

## Definition of done
**Per slice:** `plan.md` passes Constitution Check → `tasks.md` test-first → implementation Red-Green-Refactor with allow+deny authz tests → full suite green → acceptance scenarios pass when you **run the app** → CI green → increment **deployed & verified** → `/speckit-analyze` clean.
**Project:** all 18 slices done; all five primary journeys pass E2E (SC-006); perf + accessibility + authorization-coverage criteria met; a clean deploy + restore-test on Hetzner.

## Ongoing / maintenance (after MVP)
- **New feature or scope change** → add it to `product-vision.md` FIRST (new IDs), then a slice; **never invent requirements** in a plan/spec/code.
- **Bug** → write a failing test, fix, keep the suite green; if it reveals a missing requirement, route through product-vision.
- **Constitution change** → only via `/speckit-constitution` (semver + Sync Impact Report).
- Keep the OpenAPI client regenerated, migrations expand/contract, backups restore-tested, and `/speckit-analyze` clean.

## Guardrails (don't violate)
product-vision is the sole ID allocator (never renumber) · no code before `/speckit-implement` · authorization on every read/write + live subscriptions · commit/push only when asked · never commit secrets (public repo) · ask before large multi-agent workflows · don't re-litigate settled pivots or "fix" intentionally-frozen text (ADR-0001 body, v3.0.0 OOS tags).

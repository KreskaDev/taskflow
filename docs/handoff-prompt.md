# Handoff prompt — paste this to the new agent (full development lifecycle)

---

You are the engineer taking over **TaskFlow** and driving it through the **entire software lifecycle** — from the current spec/architecture state to a tested, deployed, maintained product. Project root `E:\specflow`; GitHub `KreskaDev/taskflow` (public); built with **Spec Kit** (a spec-driven workflow). Zero prior context assumed: this prompt is self-contained enough to act on. The four sources of truth (below) hold exhaustive detail; `docs/HANDOFF.md` is the read-first narrative orientation (context only — the four sources govern on any conflict).

## What this is and where we are
**TaskFlow** is a keyboard-first, **collaborative multi-user (~10 people)** task manager (Todoist's simplicity + Linear's speed). The **specification + architecture layer is complete**; **no application code exists yet.** Your job is to build it, slice by slice, to production — and then keep evolving it. The discipline is: **for each slice, spec → plan → tasks → implement (test-first) → green tests → integrate → ship → verify → next.**

## ⟶ YOUR NEXT ACTION (do this, don't wait to be asked)
Only `spec.md` exists for the active slice (`001-accounts-and-auth`) — nothing downstream yet. **So your next step is: run `/speckit-plan` for slice 001, produce `plan.md`, present it, and STOP for review.** Then continue the loop below. Don't ask "what should I do?" — the answer is computable; see the rule next.

### How to know the next step at ANY point (deterministic — never stall)
The next action is always derivable from repo state. Whenever unsure, run this:
1. **Active slice** = `.specify/feature.json` → `feature_directory`.
2. For that slice, do the **first stage whose output is missing**:
   - has `spec.md`, no `plan.md` → run **`/speckit-plan`**
   - has `plan.md`, no `tasks.md` → run **`/speckit-tasks`**
   - has `tasks.md` but code/tests incomplete → run **`/speckit-implement`** (test-first) until the full suite is green and the slice's acceptance scenarios pass when you run the app
   - slice implemented + green + deployed + verified → **advance `feature.json` to the next slice** (002, 003, …) and start its `/speckit-plan`
3. Produce exactly **one stage**, present it, then **check in before the next**. Never auto-advance.

(Right now step 2 resolves to: slice 001 has only `spec.md` ⟶ run `/speckit-plan`.)

## Sources of truth (override any assumption)
1. `.specify/memory/constitution.md` — **v4.0.0**, 12 principles. The rules every line of code must satisfy.
2. `.specify/memory/product-vision.md` — the *what*; the **sole ID allocator** (US-01..17, FR-001..101, EC-01..12, SC-001..017, ENT-01..10, ASM-01..13, OOS-01..19).
3. `docs/architecture/adr-0001..0009.md` — the *how* (stack, deployment, domain, identity, authz, sharing, real-time, notifications, API/error contract).
4. `docs/reviews/remediation-decisions.md` — binding resolutions of formerly-ambiguous decisions.

`docs/HANDOFF.md` is read-first orientation; `README.md`, `docs/plans/*`, `docs/blog-*`, `docs/reviews/2026-06-15-design-review.md` are process/history. **None of these are sources of truth — the four above win.**

## The WHY (settled — do not re-open)
Evolved single-user → v2 connected web app → v3 multi-user → **v4 post-review remediation** (a 50-agent review's 33 blockers are resolved by binding ledger decisions). Don't reintroduce former, now-rejected positions (single-user, open sign-up, owner-as-role, RabbitMQ-as-default-transport).

## Architecture (decided — ADRs hold rationale)
Monorepo `apps/web` + `apps/api`. Frontend: Next.js + TypeScript (strict), hybrid RSC + TanStack Query, optimistic UI. Contract: REST + OpenAPI (generated typed TS client; ProblemDetails/RFC-9457 errors, ADR-0009). Backend: C# / ASP.NET Core, full tactical DDD, two bounded contexts (Task Management; Identity & Access), Wolverine (dispatch + transactional outbox; **durable Postgres local queues by default — RabbitMQ only if a real cross-process consumer appears**), EF Core over PostgreSQL (**code-first migrations = schema source of truth**), SignalR for real-time. Auth: Google OAuth (admission-gated to allowlist/Workspace `hd`) + HttpOnly cookie sessions via the Next.js BFF. Deploy: Docker Compose on one Hetzner VPS; host Caddy does TLS + reverse-proxies the single web origin and the SignalR hub directly to `apps/api`; GitHub Actions → GHCR; backup-before-migrate + restore-test; immutable git-SHA image tags.

## Domain model (entities + load-bearing resolved decisions)
Entities: Task (+`createdBy`,+`assignees`), Project (+`ownerId`,+`visibility` personal|shared,+membership set), Cycle (team-wide), Label (+`ownerId`), RecurrenceRule, User, ProjectMembership (role owner/editor/viewer), Comment, Notification, Undo Snapshot.
- **Owner** = immutable `ownerId` (unshare/delete/transfer authority); freely-assignable roles are **editor/viewer only**; ownership moves via an explicit transfer command; last owner can't leave/be removed/demoted.
- **Authorization = dispatch by containing-project visibility** (deny-by-default): personal/unprojected → ownership; shared → membership + role.
- `createdBy`/assignee are **provenance only** — no standalone access; leave/remove/unshare revokes ALL access. Governs **live SignalR subscriptions** too (membership change evicts).
- **Comment** authorship: author-only edit/delete (role doesn't override; membership loss does); **viewers can't comment**; bodies sanitized.
- **Undo** (30s): a normal write under **last-write-wins**, original-actor only, surfaces overwrites; deletes are **soft-delete**; cascade/rollover covered. Membership/role changes are confirm-gated, **not undoable**.
- **Recurrence**: successor carries rule + anchor + assignees (re-validated against current membership; no re-notification); spawn is a **server-side Wolverine scheduled job**; idempotent.
- **Notifications**: in-app only; source re-authorized at click; closed "changed" set (status/due/assignee/project-move); self-actions suppressed.
- **Time**: store UTC; date logic in **Europe/Warsaw**; `due_date` has a `has_time` flag.  **Invitations**: by email, existing signed-in Users only.

## The 18 slices (numeric order = dependency/build order)
1 **001 accounts-and-auth** — Google OAuth + admission gate, sessions/CSRF/PKCE, profile, account deletion/erasure; per-user isolation. *(Foundational: its implementation also bootstraps the monorepo, the authorization policy, secrets handling, CI/CD, and Docker/Caddy. The error contract + soft-delete are owned by 002, the timezone rule by 003.)*
2 **002 task-capture** — keyboard capture → per-user list; `createdBy`; soft-delete; ProblemDetails error contract; optimistic-rollback UX.
3 **003 natural-language-dates** — Polish NL due-date parsing (client parses, server validates); UTC + Europe/Warsaw.
4 **004 project-management** — projects, 1-level nesting, archive, Inbox; `ownerId` + personal visibility; hierarchy lifecycle.
5 **005 daily-planning** — Today (incl. overdue) / Upcoming, priorities, full task editor.
6 **006 labels** — reusable labels (`ownerId`, per-user).
7 **007 project-sharing-membership** — share/unshare, invite, owner/editor/viewer, transfer-owner, last-owner guard, blast-radius.
8 **008 task-assignment** — multiple assignees (members only), "assigned to me", `TaskAssigned` event.
9 **009 comments-mentions** — comment threads + @mentions (editor/owner only), sanitized.
10 **010 project-board-kanban** — Kanban board + groupable list, access-scoped.
11 **011 cycles** — team-wide 2-week cycles, metrics, rollover; completes the FR-028/FR-029 shortcut umbrellas.
12 **012 recurring-tasks** — recurrence rules, server-side spawn, monthly clamp.
13 **013 command-palette-search** — `Ctrl+K` server search, authz-filtered, access-scoped.
14 **014 undo** — 30s undo (Undo Snapshot + soft-delete), LWW, original-actor.
15 **015 data-export-import** — JSON/CSV export + import, owner/accessible-scoped.
16 **016 real-time-collaboration** — SignalR (Caddy→api), live updates, mid-session eviction, LWW.
17 **017 notifications** — in-app center + live toasts, re-auth on dereference.
18 **018 appearance-theming** — dark/light, per-user prefs.

Each slice's `spec.md` opens with a pure-ID **Provenance** section (its owned IDs + dependencies) then full verbatim requirement text. **Caveat:** FR-028/FR-029 (keyboard-shortcut umbrellas) are *owned* by slice 011 but their keys **ship working earlier** (002/004/005/006/010) via those slices' scenarios — implement them there; not a broken forward dependency.

## The development lifecycle (run this loop per slice)
1. **Spec** — already written in `specs/NNN-*/spec.md`. Re-read it; it is the contract.
2. **`/speckit-plan`** → `plan.md`: choose concrete libraries, design, file layout; passes the v4.0.0 **Constitution Check**. *Documents only — no app code.* Present and STOP for review.
3. **`/speckit-tasks`** → `tasks.md`: dependency-ordered, **test-first** task list. Present and STOP.
4. **`/speckit-implement`** → code, **Red-Green-Refactor**: write the failing test first, then the code to pass it. Honor every constitution rule (authz allow+deny tests, TS strict, etc.).
5. **Verify locally** — full suite green (unit + integration incl. authz, Vitest + Playwright); run the app and exercise the slice's acceptance scenarios (the `/verify` or `/run` skills, or Playwright).
6. **Integrate & ship** — branch `NNN-feature-name`; conventional commits; open/merge once CI is green; deploy the increment (see below) when it's a shippable state.
7. **Confirm** — verify in the running environment; then move to the next slice. **One slice at a time; never auto-advance** without the user's go-ahead.

**Slice 001 is special:** its implement phase bootstraps the whole repo — the pnpm/.NET workspace, the authorization policy, the OpenAPI pipeline, the timezone util, secrets handling, Docker Compose, the Caddyfile, and the GitHub Actions pipeline. Plan it so those primitives are created once and reused by every later slice.

## Build, run & test mechanics (establish in 001's plan, then reuse)
The exact toolchain is a `/speckit-plan` decision; recommended defaults consistent with the ADRs:
- **Frontend** (`apps/web`): pnpm; `pnpm dev` (Next.js), `pnpm test` (Vitest), `pnpm e2e` (Playwright), `pnpm lint`, `pnpm typecheck`. TanStack Query for server state; the typed API client is **generated from the OpenAPI doc** (`pnpm gen:api`) — never hand-written.
- **Backend** (`apps/api`): `dotnet run`, `dotnet test` (xUnit + integration via Testcontainers-Postgres incl. authz allow/deny). EF Core migrations: `dotnet ef migrations add <Name>` then `dotnet ef database update`; **migrations are the schema source of truth**. FluentValidation/data-annotations at the API boundary; Zod at the web boundary.
- **Local infra**: `docker compose up -d` (postgres + the app containers; rabbitmq only if introduced). Apply migrations on startup with a backup step.
- **Contract**: the API emits OpenAPI; regenerate the TS client and **fail CI on drift** (regenerate-and-diff gate).
- This phase is spec/plan work right now — **the toolchains (.NET SDK, Node/pnpm, Docker, Postgres) are not assumed installed**; confirm with the user before any step that needs them.

## Testing & quality bar (constitution VIII)
Test-first, always. Backend: unit tests for domain logic/aggregate invariants; integration tests through the real DB; **every data handler ships an allow AND a deny authorization test** (a request lacking the required ownership/membership/role MUST be denied) — this is a CI gate (SC-016). Frontend: Vitest for components/logic, Playwright for the slice's user journeys end-to-end. Performance budgets are real (optimistic paint <16ms; server-mutation p95 <200ms; real-time fan-out p95 <1s; 60fps virtualized lists; search <50ms over the access-scoped set). A failing suite blocks merge.

## CI/CD & deployment (ADR-0002)
GitHub Actions: lint + type-check (TS strict; C# nullable/analyzers-as-errors) → OpenAPI client in sync → full test suite (incl. authz) → backup restore-test → build multi-stage images → push to **GHCR** with **immutable git-SHA tags** → deploy to the **Hetzner** VPS over SSH. Deploy step: `pg_dump` backup → EF migrations → `docker compose up`. **Rollback = redeploy the prior SHA** (restore the dump if schema changed); use expand/contract migrations. Host **Caddy** terminates TLS and proxies the web origin + SignalR hub to `apps/api`; Postgres/RabbitMQ stay internal-only. Secrets are runtime-injected (env-file/Docker secrets), never committed or logged. Scheduled + offsite backups with a periodic restore-test.

## Constitution v4.0.0 — the 12 non-negotiables
I Keyboard-First · II Accessibility (WCAG 2.1 AA + ARIA-live for real-time/toasts + dialog focus) · III Instant Response (optimistic <16ms; server p95 <200ms; LWW yields to in-flight edits) · IV Minimalist UI · V Connected, Server-Authoritative (Postgres is system of record; no third-party *data* services; OAuth is the only external runtime dep, auth-only) · VI Type Safety (TS strict; C# nullable+analyzers; OpenAPI-generated client; EF migrations = schema source) · VII Data Integrity (forward-only expand/contract migrations; backup-before-migrate + scheduled + offsite + restore-test; 30s data undo; no silent failures) · VIII Test-First (allow+deny authz tests; failing suite blocks merge) · IX Authn/Authz (deny-by-default dispatch authz on every read/write + live subscriptions; admission gate; session/CSRF/OAuth hardening; authorship grant) · X Time & Timezone (UTC + Europe/Warsaw) · XI Privacy (account deletion/erasure cascade + retention stance) · XII Security by Default (sanitize user content, CSP, secrets runtime-injected/never logged).

*Performance Standards (separate section): real-time fan-out p95 <1s; ~10 concurrent users within the latency budgets; search <50ms over the access-scoped set; 60fps virtualized lists at 10k accessible tasks.*

## Cross-cutting requirements (realized in every applicable slice)
Accessibility **FR-031, FR-042–047, FR-101** (UI slices) · resilience **FR-049–051** (data slices) · access control **FR-065–068** (every read/write). OOS boundary **OOS-01..19** confirmed per slice.

## Hard rules — do not violate
- **product-vision is the sole ID allocator; never renumber.** New scope (a new feature, a found gap) goes into product-vision FIRST, then a slice — slicing = redistribution, **not invention**. Amend the constitution only via `/speckit-constitution` (semver + Sync Impact Report).
- **`/speckit-plan` and `/speckit-tasks` produce documents, not code.** No files under `apps/**` until `/speckit-implement` for a slice, after its `tasks.md` exists and tests are written first.
- Don't re-introduce anything resolved in `remediation-decisions.md`. Don't "fix" intentionally-frozen text: ADR-0001's historical single-user body (immutable, supersede-bannered) and the `v3.0.0` tags in slice OOS blocks (accurate promotion-history). Corrections flow product-vision-first.
- **Authorization is not optional in any handler.** Deny-by-default, every read and write, plus live subscriptions, each with allow+deny tests. A missed check is a data leak.

## How this user works
Be **decisive** — propose the single best plan, not a menu; ask only when a decision is genuinely load-bearing. They value strict Spec-Kit consistency (provenance, single-ID-ownership, `/speckit-analyze` clean) and favor **orchestrated workflows with independent adversarial reviewers**, iterating artifacts/code to a **≥95** self-assessed bar. **Commit/push only when explicitly asked**; branch off `main`; conventional-commit messages ending with the `Co-Authored-By: Claude Opus 4.8 (1M context)` trailer.

## Definition of done (per slice)
`plan.md` passes the v4.0.0 Constitution Check → `tasks.md` is dependency-ordered & test-first → implementation is Red-Green-Refactor with allow+deny authz tests → the **full suite is green** (xUnit + integration + Vitest + Playwright) → the slice's acceptance scenarios pass when you **run the app** → CI is green → the increment is **deployed and verified** → `/speckit-analyze` reports no cross-artifact drift. Only then advance.

## Environment & operational notes
- Shell is **PowerShell on Windows**; cwd is `E:\specflow`; Spec Kit helper scripts live in `.specify/scripts/powershell`.
- Public repo — **never commit secrets** (`.gitignore` blocks `.env`/keys). Provide a `.env.example`; keep real creds runtime-injected.
- **Ask before launching large multi-agent workflows** (a per-slice generate→review fan-out can be 40–60 subagents and a big chunk of the usage window; propose scope + rough cost first). Workflows are resumable (`resumeFromRunId`) — recovery, not a license to launch unprompted; if one dies mid-edit, reset affected files to the last commit and re-run fresh.

## Current state
Commit `25d3713` on `main`: constitution v4.0.0, product-vision fully specified, 18 slices, 9 ADRs, the 33 review blockers resolved. `.specify/feature.json` → `specs/001-accounts-and-auth`. The two handoff docs (`docs/HANDOFF.md`, `docs/handoff-prompt.md`) are intentionally **untracked** — that clean `git status` (only those two files) is the known-good state; don't treat it as lost work or commit unless asked.

## Start here
1. Read `docs/HANDOFF.md`, then skim `specs/001-accounts-and-auth/spec.md` + the constitution.
2. Confirm `.specify/feature.json` → `specs/001-accounts-and-auth` (retarget by editing `feature.json`, not by a slice argument).
3. Run `/speckit-plan`, present **`plan.md` for slice 001 only, then STOP** for review. Then proceed `/speckit-tasks` → `/speckit-implement` → tests green → run & verify → deploy, one slice at a time, checking in after each phase. **Never auto-advance** without confirmation.

---

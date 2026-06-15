# TaskFlow — Agent Handoff

**As of:** 2026-06-15 · commit `25d3713` on `main` · GitHub `KreskaDev/taskflow` (public) · local `E:\specflow`.

Read this first if you are a new agent picking up TaskFlow. It tells you what exists, the rules you must follow, what to do next, and the traps to avoid.

---

## 1. What this project is

A **spec-driven** definition (using **Spec Kit**, "specflow") of **TaskFlow**: a keyboard-first, **collaborative multi-user (~10)** task manager — Todoist simplicity + Linear speed. **There is no application code yet.** The repo is the governance + specification + architecture layer. The next phase is turning specs into plans, tasks, and code.

Product shape: connected web app; Google OAuth sign-in (admission-gated); personal + shared projects; owner/editor/viewer roles; multiple assignees; comments + @mentions; real-time (SignalR); in-app notifications; authorization on every read/write.

---

## 2. Current state (verified)

| Artifact | State |
|---|---|
| `.specify/memory/constitution.md` | **v4.0.0**, 12 principles (I–XII) |
| `.specify/memory/product-vision.md` | US-01..**17**, FR-001..**101**, EC-01..**12**, SC-001..**017**, ENT-01..**10**, ASM-01..**13**, OOS-01..**19** — the single source of truth |
| `specs/001..018/spec.md` | **18 slice specs** (renumbered; reading order = dependency order) |
| `docs/architecture/adr-0001..0009` | **9 ADRs** (stack, deployment, domain, identity, authorization, sharing, real-time, notifications, API/error-contract) |
| `docs/reviews/` | `2026-06-15-design-review.md` (50-reviewer audit) + `remediation-decisions.md` (authoritative resolutions) |
| `.specify/feature.json` | → `specs/001-accounts-and-auth` (active slice) |
| Everything | committed + pushed |

The design has been through three governed iterations (v2.0.0 connected web app → v3.0.0 multi-user → v4.0.0 post-audit remediation) and a 50-agent review whose 33 blockers are resolved. It is internally consistent and considered buildable.

### The 18 slices (numeric order = dependency/build order)
001 accounts-and-auth · 002 task-capture · 003 natural-language-dates · 004 project-management · 005 daily-planning · 006 labels · 007 project-sharing-membership · 008 task-assignment · 009 comments-mentions · 010 project-board-kanban · 011 cycles · 012 recurring-tasks · 013 command-palette-search · 014 undo · 015 data-export-import · 016 real-time-collaboration · 017 notifications · 018 appearance-theming.

Each slice's `spec.md` Provenance lists its exact dependencies and owned IDs.

> **Ordering caveat — "owned late, exercised early".** Numeric order is the build order, but a few **umbrella requirements are formally *owned* by a later slice while their individual mechanics are *exercised* (with acceptance scenarios) earlier.** Notably the keyboard-shortcut umbrellas **FR-028 (navigation) and FR-029 (list shortcuts) are owned by slice 011 (cycles)**, yet their keys ship working in 002/004/005/006/010 via those slices' own scenarios (each flags this as an "exercised-but-not-owned / deferred" note). So when an earlier slice says a shortcut *requirement* "is delivered in slice 011," that's expected — the mechanic still ships in the earlier slice. Don't treat it as a broken forward dependency.

---

## 3. Rules you MUST follow (specflow conventions)

1. **`product-vision.md` is the single source of truth and the sole ID allocator.** Stable IDs (US/AS/FR/EC/SC/ENT/ASM/OOS) are the trace anchor; **never renumber** existing IDs. New product scope is added to product-vision FIRST, then sliced.
2. **Folder numbers are cosmetic** — IDs are the anchor. If you ever re-slice, `git mv` and update Provenance/`feature.json`.
3. **Each `specs/NNN-*/spec.md`** = a pure-ID **Provenance** section + the **full verbatim** requirement text for its owned IDs (never "see product-vision"). Each non-cross-cutting ID is owned by exactly one slice (annotated splits allowed, e.g. FR-057 personal/shared).
4. **Cross-cutting, realized in every applicable slice:** accessibility FR-031/FR-042–047/**FR-101**; resilience FR-049–051; **access control FR-065–068** (dispatch by visibility). OOS-01..19 confirmed per slice.
5. **Amend the constitution only via `/speckit-constitution`** (semver bump + Sync Impact Report). Two bounded contexts, deny-by-default authorization, etc. are constitutional — honor them.
6. **`docs/reviews/remediation-decisions.md` is binding** — it resolves the previously-ambiguous decisions (owner model, Tier dispatch, undo-under-LWW, timezone, invitations, …). Do not re-introduce those ambiguities.
7. **Slicing = redistribution, not invention.** Don't fabricate requirements in slices or plans.

Treat this section as the authoritative inline copy. (If your environment carries cross-session memory, a condensed copy also lives there as `taskflow-slicing-conventions` — but do not depend on it.)

---

## 4. What's next — the roadmap

These `/speckit-*` commands are **Spec Kit skills provided by this harness** (`/speckit-plan`, `/speckit-tasks`, `/speckit-implement`, `/speckit-analyze`, `/speckit-clarify`, `/speckit-constitution`). Downstream Spec Kit pipeline, **per slice, in numeric order**:

```
/speckit-plan <slice>   →  plan.md   (names libraries, scaffolds structure, runs Constitution Check vs v4.0.0)
/speckit-tasks <slice>  →  tasks.md  (dependency-ordered, test-first)
/speckit-implement      →  code      (Red-Green-Refactor)
```

**Start with `001-accounts-and-auth`** (`feature.json` already points there). It is foundational and stands up most cross-cutting primitives — plan it so the following are established once and reused:

- Monorepo scaffold: `apps/web` (Next.js + TS strict, hybrid RSC + TanStack Query) and `apps/api` (C# / ASP.NET Core, DDD, Wolverine, EF Core, Postgres).
- **Authorization policy** (application-layer, deny-by-default, dispatch by project visibility) — every handler gets allow + deny tests (SC-016, a CI gate).
- **Google OAuth** + HttpOnly cookie sessions via the Next.js BFF + **admission gate** (allowlist / Workspace `hd`); CSRF/SameSite; PKCE/state/nonce/id_token.
- **Error contract** (ProblemDetails, ADR-0009) + generated typed OpenAPI client + Zod.
- **Timezone** util (UTC store, Europe/Warsaw reference).
- **Security baseline** (sanitization, CSP, secrets at runtime), **soft-delete**, **backup/restore + CI restore-test**, Docker/Compose + Caddy (TLS + hub proxy) + healthchecks, GitHub Actions → GHCR → Hetzner.

Then proceed 002 → 018, each respecting its Provenance dependencies. SignalR hub (016) precedes notifications (017); assignment/comments (008/009) emit events that 017 consumes.

> Tip: do **one slice at a time** through plan→tasks→implement, and re-run `/speckit-analyze` periodically for cross-artifact consistency.

---

## 5. Non-negotiables the implementation must honor (from the constitution v4.0.0)

- **Authorization** deny-by-default on every read/write **and live SignalR subscriptions**; dispatch by containing-project visibility; `createdBy`/assignee are provenance only; leave/remove/unshare revokes all access. Allow+deny test per handler.
- **Test-first** (xUnit + integration incl. authz; Vitest + Playwright). Failing suite blocks merge.
- **Type safety**: TS strict, C# nullable + analyzers-as-errors, OpenAPI-generated client, EF migrations as schema source of truth.
- **Instant feel**: optimistic paint <16 ms; server p95 <200 ms; real-time fan-out p95 <1 s; reconciliation = LWW yielding to in-flight local edits.
- **Time**: UTC + Europe/Warsaw everywhere; `due_date` has a `has_time` flag.
- **Privacy**: account deletion/erasure cascade; data-retention stance.
- **Security by default**: sanitize user content, CSP, secrets runtime-injected (never committed/logged).
- **Data integrity**: forward-only EF migrations (expand/contract), backup-before-migrate + scheduled + offsite + restore-tested; 30s undo for data (LWW, original-actor); membership/role changes are confirm-gated, not undoable.

---

## 6. Operational notes & traps

- **Session limits.** A single workflow of ~40–60 subagents can exhaust the usage window (it happened once during remediation). Mitigations: batch large fan-outs, run off-peak, and remember **workflows are resumable** (`resumeFromRunId`). If a run dies mid-way leaving partial edits, **reset the affected files to the last commit and re-run fresh** rather than resuming onto half-edited files.
- **Token economy.** Heavy generate/review work belongs in subagents/workflows (paid once, not re-billed into the main window). (If cross-session memory is available, see `delegate-to-protect-main-context`.)
- **Secrets.** Never commit env/keys; `.gitignore` already blocks `.env`/`*.key`/etc. The repo is **public**. App-layer OAuth replaced the old Caddy `basic_auth` gate.
- **Git.** Commit/push only when the user asks; branch off `main` for feature work; end commit messages with the `Co-Authored-By: Claude Opus 4.8 (1M context)` trailer. CRLF warnings on Windows are harmless (a `.gitattributes` with `* text=auto eol=lf` would silence them — not yet added).
- **Quality bar.** The established pattern is generate → independent adversarial review → iterate until ≥95. Keep using it for batch artifact work.
- **`v3.0.0` strings in slice OOS blocks are intentional** — they are accurate promotion-history tags ("collaboration promoted in v3.0.0"), not stale version references.

---

## 7. How to verify the design is still consistent

- `/speckit-analyze` — cross-artifact consistency across constitution ↔ product-vision ↔ slices.
- Grep sweeps: no `single-user`/`open sign-up`/`basic_auth` in normative bodies; every `slice NNN` reference resolves; OOS = 01..19; slices cite v4.0.0. **FR single-ownership** holds *except* the cross-cutting FRs that legitimately appear in many slices — FR-031, FR-042–047, FR-049–051, FR-065–068, FR-101 — and the deliberate **annotated splits** FR-057 (personal half in 004 / shared half in 007) and the FR-028/FR-029 shortcut umbrellas (owned by 011, exercised earlier — see §2). Don't flag those as duplicates.
- An adversarial review workflow (per-slice or per-dimension reviewers, iterate-to-95).

---

## 8. Known residuals (minor, non-blocking)

- A few slices append scoping parentheticals to copied FR text (defensible slice-scoping, not 1:1).
- `plan-template.md` Constitution Check is a dynamic gate — it re-derives from v4.0.0 at `/speckit-plan` time (no edit needed).
- No `.gitattributes` yet (line-ending normalization).
- ADR-0001's body remains historical (single-user) with a supersede banner — intentional ADR immutability.
- **Other repo docs are non-authoritative context:** `README.md` (public overview) and `docs/plans/refoundation-multiuser.md` + `docs/blog-orchestration-token-economics.md` (process/history). The four sources in §9 always win over these.

---

## 9. First concrete action

1. Confirm `feature.json` → `specs/001-accounts-and-auth`.
2. Run **`/speckit-plan`** on slice 001 (it will Constitution-Check against v4.0.0 and scaffold the monorepo plan).
3. Then `/speckit-tasks`, then implement test-first.
4. Repeat for 002 → 018 in order.

Everything you need to make decisions is in `product-vision.md` (the what), the ADRs (the how), the constitution (the rules), and `docs/reviews/remediation-decisions.md` (the resolved ambiguities). When in doubt, those four win over any assumption.

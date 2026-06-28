# Implementation Plan: Daily Planning

**Branch**: `005-daily-planning` | **Date**: 2026-06-27 | **Spec**: `specs/005-daily-planning/spec.md`

**Input**: Feature specification from `specs/005-daily-planning/spec.md`

## Summary

Delivers the **mouse-free daily loop** — the **Today** and **Upcoming** read views, task **priorities** (P0–P3), and a full keyboard-driven **task editor** — so the user can review, triage, reprioritize, reschedule, and complete the day's work entirely from the keyboard (US-02, SC-001). The user opens Today (`G T`) to see tasks due today (incl. overdue incomplete), grouped by project and sorted by priority (AS-01/AS-02); toggles done with `Space` (AS-03), sets priority with `1`-`4` (AS-04), reschedules with `T` typing a Polish phrase that disappears the task from Today (AS-05), and opens the editor with `E`, saving with `Ctrl+Enter` or discarding with `Esc` (AS-06/07/08). The slice also wires `G I` (Inbox) and `G U` (Upcoming, next-7-days grouped by day) navigation (US-08.AS-01/AS-02).

This is the **first slice that computes "same calendar day in `Europe/Warsaw`" server-side** — the responsibility slice 003 explicitly handed forward ("NodaTime arrives in slice 005 … the first slice that computes same calendar day in Europe/Warsaw server-side (Today/Upcoming)", slice-003 plan Complexity Tracking). It therefore **OWNS Principle X / FR-092 on the server for the first time**: it introduces **NodaTime** for Warsaw day-boundary computation (research R1) and the zone-aware Today/Upcoming filtering (R6). It also **activates** the two reserved, already-mapped Task columns — `priority` and `description` — that slice 002 set up as forward-compatible nullable columns (R2/R3).

This slice realizes the **personal-visibility / ownership** authorization branch in full (FR-065 ownership half, FR-068 deny-by-default) and structures the Today/Upcoming query handlers and the editor command handlers as a **dispatch-by-visibility seam**. ✅ **The membership arm is now REALIZED — research open-question #1 (the BLOCKER) was resolved via option (a): slice 007 (project-sharing-membership) is sequenced before slice 005 (rebased), so the `ProjectMembership` substrate + the `ResolveEffectiveRole`/`RequireRole` policy are in the tree.** The shared-project membership + role branch (FR-066/FR-067) and its two SC-016 deny tests (viewer-mutation-deny → 403, non-member-read-deny → absent/404) are therefore realized in this slice — the read queries surface tasks in shared projects the caller is a current member of, and the write commands (including the now-membership-aware `SetTaskDone`) dispatch on the containing project's visibility via a new `TaskAccessGuards` helper. The arm filled the seam with **no query/command reshape**, as designed. See Complexity Tracking (the blocker row, now resolved) and `tasks.md` Phase 6 (UNBLOCKED).

Unlike slice 004, this slice adds **no entity and no EF migration** (priority/description activate pre-existing mapped columns; NodaTime is boundary-computation only, no column remap, no Npgsql plugin → unchanged model snapshot). Consequently **FR-051 backup-before-migration is a named no-op here** — the slice-003 posture, NOT the slice-004 LIVE posture (R9).

## Technical Context

**Language/Version**: TypeScript (strict) on Node.js 22 LTS / Next.js 15 (App Router), React 19; C# 13 on .NET 9 / ASP.NET Core.

**Primary Dependencies**:
- Backend (NEW this slice): **`NodaTime`** — adopted for **Warsaw calendar-day boundary computation ONLY** (`DateTimeZoneProviders.Tzdb["Europe/Warsaw"]`, `LocalDate`/`ZonedDateTime` → `Instant` → UTC `DateTime`), confined to the new `WarsawDayBounds` server seam and the Today/Upcoming query handlers (research R1). **`Npgsql.NodaTime` is deliberately NOT adopted** — adding it would remap the `timestamptz` columns from `DateTime` to `Instant`, change the EF model snapshot, and force a migration for zero behavioral gain (R1-a); the columns stay mapped to `DateTime` and NodaTime only derives the two UTC bounds the SQL range filter needs. Reuses Wolverine + Wolverine.Http, EF Core + Npgsql, `WolverineFx.FluentValidation` (new validators on the priority/reschedule/edit commands), the `ProblemDetailsMiddleware`. **No new error code** (R11).
- Frontend (NEW this slice): **none** — reuses TanStack Query v5 (optimistic priority/reschedule/edit mutations on the `['tasks','today']` / `['tasks','upcoming']` / `['tasks']` keys via the slice-002 `onMutate`/`onError`/`onSettled` recipe), Zod 3 (priority enum + edit-form validation), the slice-003 Polish date parser (`lib/dates.ts`) for the `T` reschedule, the existing `lib/timezone.ts` (the client mirror of the Warsaw boundary rule, FR-092), openapi-fetch + openapi-typescript (regen), and the slice-002/004 shortcut + single-key-suppression substrate (`useGlobalShortcuts`). No new dependency.

**Storage**: PostgreSQL 17. **NO migration this slice** (research R9, data-model §7): `priority`/`description` activate pre-existing nullable-text columns mapped in `TaskConfiguration` (created by the slice-002 `AddTasks` migration); NodaTime is boundary-computation only (no column remap, no Npgsql plugin) → the EF model snapshot is unchanged → no migration is generated. The optional Today/Upcoming range index (`(created_by, due_date) WHERE deleted_at IS NULL`) is **deferred** (R6-c, following slice-004's deferred-index precedent). **FR-051 is therefore a named no-op** — the slice-003 posture (Complexity Tracking).

**Testing**: xUnit (`Task.SetPriority` set/clear/closed-set guard, the reschedule path reusing the slice-003 due-date round-trip, `EditTask` whole-object replace); Testcontainers-Postgres integration (`GetTodayTasks`/`GetUpcomingTasks` membership — due-today, overdue-in-Today, tomorrow-in-Upcoming-not-Today, no-due-in-neither, done/cancelled-excluded — the R5 deterministic order incl. NULL-priority-last and date-only-as-start-of-day, a **DST-boundary** case guarding FR-092, and **per handler an allow + a deny** test); Vitest (priority/reschedule/edit optimistic surfaces + the client membership recompute using a frozen Warsaw clock + the same DST-boundary case for client/server parity); Playwright (E2E — AS-01..AS-08, US-08 AS-01/AS-02, and SC-008's WCAG 2.1 AA audit on Today + Upcoming). **The SC-016 viewer-deny / non-member-deny tests are tracked as BLOCKED** pending the `ProjectMembership` substrate (Complexity Tracking / research R13).

**Target Platform**: Linux containers (Docker Compose) on Hetzner VPS; host Caddy for TLS. No infra change.

**Project Type**: Web application (monorepo: Next.js frontend + ASP.NET Core API).

**Performance Goals**: priority / reschedule / toggle-done / edit paint optimistically <16 ms (SC-003, owned by slice 002), with the view-membership recompute done synchronously in-process via the client Warsaw boundary helper (R7); server single-entity writes p95 <200 ms; the Today/Upcoming reads filter a plain UTC `due_date` range over the per-user authz-scoped working set (the boundary math collapses to two UTC instants → zone-free SQL, R6).

**Constraints**: the Warsaw day boundary is computed in **one** server seam (`WarsawDayBounds`, R1) mirroring the client `lib/timezone.ts` (FR-092, applied identically client/server); DST handled by the tzdb library, never fixed-offset arithmetic; priority is a closed `{P0,P1,P2,P3}` token (NULL = unprioritized, sorts last) validated at both trust boundaries (R2); description is raw markdown **source**, stored verbatim, output-escaped on render — **no markdown renderer this slice** (R3/R12); the editor is a whole-object replace (atomic `Ctrl+Enter` / `Esc` discard, no partial commit, R4); authorization is deny-by-default ownership-branch only (the membership arm is a named seam — the BLOCKER); no new error code (R11); no migration (R9).

**Scale/Scope**: ~10 users; per-user accessible working set up to 10,000 tasks (the authz-scoped anchor, unchanged). This slice adds two read queries, three commands (+ reuse of `SetTaskDone`/`MoveTaskToProject`), the NodaTime boundary seam, and the Today/Upcoming/editor web surface — no new table.

## Constitution Check

*GATE: Re-evaluated against v4.0.0 for slice 005. PASS, with one open question. This slice **OWNS Principle X server-side for the first time** (Today/Upcoming computed against `Europe/Warsaw` via NodaTime) and exercises the **ownership** authorization branch (Principle IX) across the new read/write handlers with no migration (Principle VII / FR-051 a named no-op). One scope item is a surfaced open question, not a silent gap: the shared-project membership/role branch (FR-066/FR-067) and its two SC-016 deny tests are **not realizable against this slice's declared dependency set** (the `ProjectMembership` substrate is slice 007) — tracked as **open-question #1 (the BLOCKER)** in Complexity Tracking and carried in rows VIII and IX below.*

| # | Principle | Status | How Addressed |
|---|---|---|---|
| I | Keyboard-First | PASS | The entire daily loop is keyboard-driven: open Today (`G T`), Inbox (`G I`), Upcoming (`G U`); navigate; toggle done (`Space`); set priority (`1`-`4`); reschedule (`T`); edit (`E`); save (`Ctrl+Enter`); cancel (`Esc`) — realizing SC-001 (complete mouse-free workflow). Bindings ride the existing `useGlobalShortcuts` chord layer (R8); no new shortcut subsystem. Single-key suppression (FR-031) keeps `1`-`4`/`E`/`T` from hijacking the editor fields and the `T` date input. |
| II | Accessibility | PASS | The task editor and the `T` date input follow the **dialog focus contract** (initial focus, trap, dismiss on Esc, return focus to the originating task row — the `Esc`/`Ctrl+Enter` close paths of AS-07/AS-08; FR-101). Any server-initiated update or toast in Today/Upcoming (an optimistic-write rollback message, or a live patch on a shared-project row once that arm lands) routes through the **polite** ARIA live region without stealing focus (FR-101). Priority is never the sole signal (a P0–P3 label/text accompanies any color cue, FR-044 ≥4.5:1); no hover-only affordances (FR-046); transitions instant/<100 ms under `prefers-reduced-motion` (FR-047); no AT-binding collision (FR-045); correct roles/labels (FR-043). SC-008 audits Today + Upcoming at WCAG 2.1 AA. |
| III | Instant Response | PASS | `SetPriority`, `RescheduleDueDate`, `SetTaskDone`, and `EditTask` paint their optimistic result <16 ms (SC-003) via the slice-002 `onMutate`/`onError`/`onSettled` recipe (R7); a mutation that changes **view membership** (a reschedule to tomorrow removes the task from Today — AS-05's "disappears"; toggle-done removes it from both) recomputes membership locally using the **same Warsaw boundary helper** the server uses (`lib/timezone.ts`, the FR-092 identical-rule guarantee in action) so the optimistic patch equals the authoritative result. Server writes stay p95 <200 ms; rejection rolls the row back. Inbound real-time updates reconcile under last-write-wins and MUST NOT clobber a pending local optimistic edit. Skeletons permitted for the network-bound view load but never to mask a mutation (Principle IV). |
| IV | Minimalist UI | PASS | Today and Upcoming surface only what matters for triage (project/day groups, priority order, an overdue flag); the editor is one compact form. Server-rendered skeletons are permitted for the initial network-bound view load but MUST NOT mask a priority/edit/reschedule whose optimistic result can be shown immediately. Done/cancelled tasks are excluded by default (R5) — the triage views stay uncluttered. |
| V | Connected, Server-Authoritative | PASS | View rendering and all mutations flow through the C# API with PostgreSQL as the system of record. The **Warsaw day boundary and the Today/Upcoming filter are server-authoritative** (the server recomputes the boundary independently of the client; the client may not shift its own "today", R1-c). The app depends on no third-party runtime data service (SC-004, owned by slice 002); all traffic rides the slice-001 BFF proxy. |
| VI | Type Safety | PASS | TS strict + no `any`; C# nullable + analyzers-as-errors. The `priority` (P0–P3 enum) and `description` (markdown string) fields on `TaskResponse`, plus `SetPriorityRequest`/`RescheduleDueDateRequest`/`EditTaskRequest`/`TodayResponse`/`UpcomingResponse`, are generated from the OpenAPI contract (`pnpm gen:api`, CI-diff-gated). Runtime validation at both boundaries — Zod (priority enum, edit-form bounds) and FluentValidation (closed-set priority, the reused due-pairing/UTC/range rule, description length). No new `errorCode` → the `ERROR_UX satisfies Record<ErrorCode, ErrorUx>` map stays exhaustive with **no change** (R11). |
| VII | Data Integrity | PASS (**FR-051 no-op**) | **No migration this slice** (R9, data-model §7): `priority`/`description` activate pre-existing mapped columns; NodaTime is boundary-computation only (no column remap, no Npgsql plugin) → unchanged model snapshot; the range index is deferred. **FR-051 backup-before-migration is therefore a named no-op** — the slice-003 posture, NOT slice 004's LIVE posture (Complexity Tracking); the pre-migration backup hook stays in place for the next slice that DOES migrate. FR-049 recoverable errors (a bad priority/reschedule/over-long description surfaces a field-level 422 message; a rollback message on optimistic-write rejection); FR-050 structured logging (no title/description/owner in logs). The editor's `Esc` (AS-08) discards in-flight changes without committing partial state. |
| VIII | Test-First | PASS (1 set BLOCKED) | Red-Green-Refactor across tiers (R13). Every owned acceptance scenario is independently testable; the **DST-boundary** test on **both** tiers guards the FR-092 identical-client/server rule that R1/R7 depend on. Per Constitution VIII + the governance gate, **every** new data handler ships an **allow** AND a **deny** integration test through the real DB (the deny = a caller acting on another user's personal task → 404). ⚠ The two SC-016 shared-project deny tests (a **viewer** attempting a mutation; a **non-member** reading another project's task) are **BLOCKED** — they are unwritable until the `ProjectMembership` substrate exists (slice 007). This is open-question #1 (the BLOCKER), surfaced in Complexity Tracking, not faked. |
| IX | Authn/Authz | PASS (membership arm = open question) | Deny-by-default (FR-068), **dispatched by the containing resource's visibility** (FR-065), NOT a Tier A/B conjunction. The **ownership** branch is realized in full: personal/unprojected reads scoped `WHERE created_by = caller AND deleted_at IS NULL` (+ the R5/R6 view predicates); writes coerce no owner from the wire and resolve a foreign/absent/tombstoned id to **404** (existence not disclosed, R10). `createdBy` and assignee are **provenance only**, never a standalone grant (FR-066's applicable half). The handlers are a **dispatch-by-visibility seam**: the ownership arm is live; the **shared-project membership + role arm (FR-067: viewer=read, editor/owner=write) is a named, not-yet-realized branch** — ⚠ open-question #1 (the `ProjectMembership` substrate is slice 007; this plan does NOT pull it forward). Sessions/admission are slice 001 (assumed authenticated, admitted caller); this slice adds no new session/admission surface. |
| X | Time & Timezone | PASS (**OWNED here, server-side**) | **First slice to compute "same calendar day in `Europe/Warsaw`" server-side.** "Due date equal to today" and the next-7-days Upcoming window are computed against the single instance reference zone `Europe/Warsaw` via **NodaTime** in the `WarsawDayBounds` seam (R1), applied **identically on client and server** (the client mirror is `lib/timezone.ts`, R7) — FR-092. Today includes overdue incomplete and excludes done/cancelled; a due date carries the `has_time` flag (date-only vs date-time, used by the deterministic due-time ordering and the `T` reschedule, R5). **DST is handled by the tzdb library, never fixed-offset arithmetic** (the DST-boundary test on both tiers proves it, R13). Timestamps stay stored UTC (`timestamptz` over `DateTime` — NodaTime does NOT remap the columns, R1). Per-user zones OOS-19. |
| XI | Privacy | PASS | This slice renders and mutates existing task data and introduces **no new personal-data sink**; the account-deletion/erasure cascade and retention stance (FR-085/FR-086) are owned elsewhere. Today/Upcoming respect ownership scoping immediately; once the membership arm lands, a task in a project the caller has left/been removed from MUST NOT appear (the revoke-all rule, FR-066) — the seam is shaped for that without reshaping the query. No new retention surface. |
| XII | Security | PASS | The task `description` (markdown source, R3) and any `title` rendered in Today/Upcoming/the editor are **untrusted user-authored content** — React-escaped / output-sanitized on render, never `dangerouslySetInnerHTML`, no raw-HTML path (FR-099). Because R3 ships **no markdown renderer**, the description renders as escaped raw text (trivially safe); the decision is recorded so the next slice that *renders* a formatted description inherits the sanitize-on-render requirement explicitly (Constitution XII). Structured rejection logs carry `ErrorCode`/`Method`/`Path` only — never the title/description (FR-050). Slice-001 CSP / security headers + BFF→API signed carrier reused unchanged; **no new secrets** (FR-100). |

## Project Structure

### Documentation (this feature)

```text
specs/005-daily-planning/
├── plan.md              # This file
├── research.md          # Phase 0: design decisions (R1–R13) + the BLOCKER (open question #1)
├── data-model.md        # Phase 1: Task.priority/description activation + WarsawDayBounds + Today/Upcoming read models (no migration)
├── quickstart.md        # Phase 1: validation guide (Today/Upcoming, priority triage, the editor, nav, DST-boundary, authz)
├── contracts/
│   └── openapi.yaml     # Phase 1: API contract delta (today/upcoming reads + priority/due-date/edit mutations + TaskResponse delta)
└── tasks.md             # Phase 2 (/speckit-tasks — not yet created)
```

### Source Code (repository root)

Paths are **additive** over the slice-002/003/004 tree. **(NEW)** is net-new; **(MODIFY)** is a surgical change. **There is NO DB migration this slice** (priority/description activate pre-existing columns; NodaTime is boundary-computation only — no column remap, no Npgsql plugin, no snapshot delta). All endpoints land in the existing `TaskEndpoints.cs` (no new endpoint file — contrast slice-004's `ProjectEndpoints.cs`), since every operation is a task operation.

```text
apps/
├── api/                                          # ASP.NET Core 9 (C#, DDD, Wolverine)
│   ├── src/
│   │   ├── TaskFlow.Domain/
│   │   │   └── TaskManagement/
│   │   │       └── Task.cs                        # (MODIFY) add SetPriority(priority?, utcNow), Reschedule(dueDate?, dueHasTime?, utcNow),
│   │   │                                          #   and EditTask(title, description?, priority?, dueDate?, dueHasTime?, projectId?, utcNow)
│   │   │                                          #   — whole-object replace; reuses NormalizeTitle + the closed-set/pairing guards +
│   │   │                                          #   MoveToProject internally; each Touches (UpdatedAt + Version++). (R2/R3/R4)
│   │   ├── TaskFlow.Application/
│   │   │   ├── Time/
│   │   │   │   └── WarsawDayBounds.cs             # (NEW) the ONE server seam for the Warsaw calendar-day boundary (NodaTime):
│   │   │   │                                      #   StartOfTodayUtc / StartOfTomorrowUtc / StartOfDayPlusUtc(n) / WarsawLocalDate(utc).
│   │   │   │                                      #   LocalDate/ZonedDateTime → Instant → UTC DateTime; DST via tzdb (R1). Mirrors lib/timezone.ts.
│   │   │   └── TaskManagement/
│   │   │       ├── SetPriority.cs                 # (NEW) request DTO + command + validator (closed set {P0..P3}|null) + handler. PATCH /priority (R2/R4)
│   │   │       ├── RescheduleDueDate.cs           # (NEW) request DTO + command + validator (reuses the slice-003 due-pairing/UTC/range rule) + handler. PATCH /due-date (R4)
│   │   │       ├── EditTask.cs                    # (NEW) request DTO + command + validator (priority + due rule + description length) + handler. PATCH /edit (R4)
│   │   │       ├── TaskResponse.cs                # (MODIFY) add nullable priority + description + From(...) projection (R2/R3/R10)
│   │   │       ├── TodayResponse.cs               # (NEW) read model: { groups: [ { projectId?, tasks: [TaskResponse + isOverdue] } ] } (R5/R6)
│   │   │       ├── UpcomingResponse.cs            # (NEW) read model: { groups: [ { date "YYYY-MM-DD", tasks: [TaskResponse] } ] } (R5/R6)
│   │   │       └── Queries/
│   │   │           ├── GetTodayTasks.cs          # (NEW) owner-scoped, zone-aware (WarsawDayBounds) due-today + overdue; group by project; R5 order
│   │   │           └── GetUpcomingTasks.cs       # (NEW) owner-scoped, zone-aware next-7-days; group by Warsaw LocalDate; R5 order
│   │   ├── TaskFlow.Infrastructure/
│   │   │   └── Persistence/                       # (no change) TaskConfiguration already maps priority/description (string?) and the timestamptz columns
│   │   │                                          #   over DateTime — NO migration, NO Npgsql.NodaTime, NO column remap (data-model §3/§7)
│   │   └── TaskFlow.Api/
│   │       ├── Endpoints/
│   │       │   └── TaskEndpoints.cs              # (MODIFY) add GET /api/tasks/today, GET /api/tasks/upcoming, PATCH /api/tasks/{id}/priority,
│   │       │                                      #   PATCH /api/tasks/{id}/due-date, PATCH /api/tasks/{id}/edit → dispatch the queries/commands.
│   │       │                                      #   Register the literal /today + /upcoming routes so they WIN over /{id} (data-model §6 routing note).
│   │       └── OpenApi/
│   │           └── TaskFlowDocumentTransformer.cs # (MODIFY) stamp the new operationIds (getTodayTasks/getUpcomingTasks/setTaskPriority/
│   │                                              #   rescheduleTaskDueDate/editTask) + auto-insert their 401/404/409/422 responses.
│   │                                              #   NO ErrorCodes edit — no new errorCode (R11).
│   └── tests/
│       ├── TaskFlow.UnitTests/Domain/TaskManagement/
│       │   └── TaskTests.cs                       # (MODIFY) SetPriority (set/clear/closed-set guard), Reschedule (reuse slice-003 round-trip),
│       │                                          #   EditTask whole-object replace (R13)
│       └── TaskFlow.IntegrationTests/TaskManagement/
│           ├── GetTodayTasksTests.cs             # (NEW) due-today / overdue-in-Today / done-cancelled-excluded / no-due-excluded / group+order
│           │                                      #   (NULL-priority-last, date-only-as-start-of-day) / DST-boundary / ALLOW + DENY(404) (R13)
│           ├── GetUpcomingTasksTests.cs          # (NEW) tomorrow-in-Upcoming-not-Today / 7-day-window / group-by-Warsaw-day / order / DST / ALLOW + DENY
│           ├── SetPriorityTests.cs               # (NEW) set/clear + closed-set→422 + version_conflict + ALLOW + DENY(404)
│           ├── RescheduleDueDateTests.cs         # (NEW) reschedule + clear + pairing/UTC/range→422 + version_conflict + ALLOW + DENY(404)
│           └── EditTaskTests.cs                  # (NEW) whole-object replace + omitted-key→400(binding) + project move-check-on-change + version_conflict + ALLOW + DENY(404)
│                                                  #   ✅ SC-016 viewer-deny (403) / non-member-deny (404) REALIZED here + in SetTaskDoneSharedAuthzTests/GetToday/GetUpcoming (BLOCKER resolved: 007 first)
│
└── web/                                           # Next.js 15 (App Router, TS strict)
    ├── src/
    │   ├── lib/
    │   │   ├── timezone.ts                        # (REUSE) the client Warsaw boundary mirror (FR-092) — used for optimistic view membership (R7)
    │   │   ├── dates.ts                           # (REUSE) the slice-003 Polish parser — the `T` reschedule resolves the phrase client-side (R4/R8)
    │   │   ├── validation/
    │   │   │   └── task.ts                         # (MODIFY) add the priority enum + edit-form schema (description length, due pairing) (R2/R3)
    │   │   └── api/generated/schema.d.ts          # (REGEN) pnpm gen:api — gains today/upcoming ops + priority/due-date/edit ops + TaskResponse.priority/description
    │   ├── components/
    │   │   ├── tasks/
    │   │   │   ├── TodayView.tsx                  # (NEW) project-grouped, priority-sorted Today view; renders the overdue flag (R5/R6)
    │   │   │   ├── UpcomingView.tsx               # (NEW) day-grouped next-7-days view (R5/R6)
    │   │   │   ├── TaskEditor.tsx                 # (NEW) the `E` editor: title/description/priority/due/project; dialog focus contract;
    │   │   │   │                                  #   Ctrl+Enter save / Esc discard-all (AS-06/07/08; FR-101)
    │   │   │   ├── TaskRow.tsx                    # (MODIFY) render priority badge + overdue/due labels; `1`-`4`/`T`/`E`/`Space` on the selected row
    │   │   │   └── RescheduleInput.tsx            # (NEW) the `T` date input (parses the Polish phrase via lib/dates.ts; dialog focus contract)
    │   ├── hooks/
    │   │   ├── useTaskMutations.ts                # (MODIFY) add setPriority / rescheduleDueDate / editTask optimistic mutations + the
    │   │   │                                      #   client view-membership recompute (moves rows across today/upcoming/inbox keys) (R7)
    │   │   ├── useTodayTasks.ts                   # (NEW) query hook (['tasks','today'])
    │   │   ├── useUpcomingTasks.ts                # (NEW) query hook (['tasks','upcoming'])
    │   │   └── useGlobalShortcuts.ts             # (MODIFY) bind G T / G I / G U nav chords + `1`-`4`/`T`/`E` list verbs (single-key suppression in inputs, R8)
    │   └── app/(app)/
    │       ├── today/page.tsx                     # (NEW) the Today route (G T)
    │       └── upcoming/page.tsx                  # (NEW) the Upcoming route (G U); G I resolves to the slice-004 Inbox route
    └── tests/
        ├── unit/
        │   ├── task-mutations.test.ts            # (MODIFY) optimistic priority/reschedule/edit + the membership recompute (frozen Warsaw clock) + DST parity (R7/R13)
        │   └── today-upcoming.test.ts            # (NEW) group/order assembly (NULL-priority-last, date-only-as-start-of-day) from the read model
        └── e2e/
            └── daily-planning.spec.ts            # (NEW) AS-01..AS-08 (Today render/group/sort, Space/1/T/E/Ctrl+Enter/Esc), US-08 AS-01/AS-02 (G I/G U),
                                                   #   SC-008 WCAG 2.1 AA audit on Today + Upcoming
```

**Structure Decision**: Reuses the slice-002/003/004 monorepo layout and the proven `Task` vertical-slice conventions (command + validator + handler + read model + endpoint). The backend adds **no aggregate and no migration** — only new behavior on the existing `Task` aggregate (`SetPriority`/`Reschedule`/`EditTask`), three commands, two CQRS read queries + their projections, and the **`WarsawDayBounds` NodaTime seam** (the one place the server's Warsaw day boundary lives). All endpoints extend the existing `TaskEndpoints.cs`; the document transformer is modified to stamp the new operationIds (no `ErrorCodes` edit). The web adds the Today/Upcoming views + the editor + the reschedule input + two query hooks, modifying the task-mutations hook, `TaskRow`, the validation schema, and global shortcuts; it **reuses** `lib/timezone.ts` (the client boundary mirror) and `lib/dates.ts` (the slice-003 parser). The BFF proxy, authentication wiring, error pipeline, `version`/`version_conflict` machinery, and the slice-002 shortcut/suppression substrate are reused unchanged.

## Key Design Decisions

These summarize `research.md` (R1–R13). The **BLOCKER (open question #1)** governs R6/R10 and is tracked in Complexity Tracking.

### NodaTime for boundary computation only — no Npgsql plugin, no column remap (R1)
This slice OWNS introducing the server timezone library (the responsibility slice 003 handed forward). NodaTime computes DST-correct Warsaw day boundaries in the single `WarsawDayBounds` seam — `LocalDate`/`ZonedDateTime` → `Instant` → UTC `DateTime` — and nothing else. **`Npgsql.NodaTime` is deliberately NOT adopted** and the `timestamptz` columns stay mapped to `DateTime`: remapping to `Instant` would be a broad blast-radius change that **generates a migration** for zero behavioral gain (the storage is identical UTC instants either way). Keeping NodaTime to boundary-only is what preserves the **no-migration / FR-051-no-op** posture (R9). The boundary fact is server-authoritative; the client recomputes the same boundary (`lib/timezone.ts`) only for optimistic membership (R7), and the server recomputes it independently.

### Activate the reserved priority + description columns — zero migration (R2, R3)
`priority` activates as the closed token string `"P0".."P3"` (NULL = unprioritized), key-mapped `1`→P0 … `4`→P3 (P0 = highest, fixed by AS-04), validated at both tiers — the column is already `string?`, so no enum-column migration. `description` activates as raw markdown **source**, edited in the task editor, stored verbatim, **output-escaped on render with no markdown renderer this slice** (R12) — FR-002's `description` portion is realized (the field accepts markdown source) and Constitution XII is satisfied (a raw-text render is trivially safe); a renderer arrives with the first slice that *displays* a formatted description.

### Editor command surface: discrete quick-mutations + one combined `EditTask` (R4)
The single-key verbs are independent instant mutations: **`SetPriority`** (`1`-`4`, `PATCH /priority`), **`RescheduleDueDate`** (`T`, `PATCH /due-date` — the client parses the Polish phrase with the slice-003 parser and resolves the instant; the server re-validates it the slice-003 way; this realizes the reschedule slice 003 deferred), and the reused **`SetTaskDone`** (`Space`). The `E` editor is one combined **`EditTask`** (`PATCH /edit`) — a **whole-object replace** of the editable fields (every field a required key with a nullable value, the slice-004 anti-silent-null discipline), atomic on `Ctrl+Enter`, discarded entirely on `Esc`, reusing `MoveToProject` internally for the project field. The `/edit` path is the convention-consistent slot (bare `/{id}` is taken by the slice-002 create/delete; every per-field op is `/{id}/<field>`).

### Today vs Upcoming membership + deterministic order, server-side (R5, R6)
Both views exclude done/cancelled and are owner-scoped. **Today** = due-today-in-Warsaw OR overdue-incomplete (an `isOverdue` flag, "so nothing silently falls off the day"), grouped by project. **Upcoming** = the 7 calendar days after today (`[start of tomorrow-Warsaw, start of (today+8)-Warsaw)`), grouped by the Warsaw `LocalDate` of each task. The boundary is computed in C# → the SQL filters a **plain UTC `due_date` range** (zone-free). Order within a group: priority (P0 first, **NULL last**) → due time (date-only sorts as start-of-day) → createdAt → id — applied server-side so the client renders a ready-to-paint list.

### Both authorization arms realized via the dispatch-by-visibility seam (R10, BLOCKER resolved)
Every handler is deny-by-default, enforced at the handler (FR-068), dispatched by visibility (FR-065). The **ownership** arm (personal/Inbox: scoped reads, owner-coerced writes, foreign id → 404) AND the **shared-project membership + role** arm (FR-067: viewer=read, editor/owner=write) are both **realized** — slice 007 was sequenced first, so `TaskAccessGuards.LoadWritableTaskAsync` (writes) and `ListDueInRangeReadableAsync` (reads, `created_by = caller OR project_id ∈ ListProjectIdsForUserAsync`) fill the seam with no query/command reshape, exactly as the seam was designed. `SetTaskDone` is now membership-aware too (spec L127 deviation). The SC-016 viewer-deny (403) / non-member-deny (404/absent) tests are realized and green.

### No new error code (R11)
A bad priority / bad reschedule instant / over-long description → **422 `validation_failed`** (field-level message); a foreign/absent/tombstoned id → **404 `not_found`**; a stale token → **409 `version_conflict`** — all existing codes. The `ErrorCode` union + `ERROR_UX` map stay exhaustive with no change; no `TaskFlowDocumentTransformer` `ErrorCodes` edit.

## Complexity Tracking

| Item | Status this slice | Resolution & where it lands |
|------|-------------------|------------------------------|
| **Membership/role authorization branch (FR-066/FR-067) + its SC-016 deny tests** — ⚠ **open question #1 (the BLOCKER)** | **NOT REALIZABLE against this slice's declared dependency set; surfaced, not silently engineered away.** The spec mandates the membership branch as in-scope (spec L74, L123–128) with SC-016 deny tests (viewer-mutation-deny, non-member-read-deny, spec L172), but the `ProjectMembership` substrate **does not exist** (deferred to slice 007 by slice 004 R11) and slice 005's "Depends on" lists only 003+004 (spec L49–51). A view that "surfaces tasks in shared projects the caller is a current member of" is uncomputable without the membership set; the two named deny tests are unwritable against the current substrate. | This is a **sequencing/spec decision**, NOT settled in this plan and explicitly NOT scope creep: **(a) re-sequence** — land slice 007 before 005 (add it to "Depends on") so the membership branch + its deny tests are realizable; **or (b) amend slice 005's shared scope** — defer the shared half of Today/Upcoming (and the viewer-deny / non-member-deny portion of SC-016) to a post-007 slice. Until decided, this plan realizes the **ownership branch in full** and structures the handlers as a **dispatch-by-visibility seam** (research R6/R10) — under (b) the slice ships as-is; under (a) the membership arm fills the seam with no handler-shape change. **`ProjectMembership` is NOT designed or pulled forward here** (slice 007's owned scope; building it here is the scope creep YAGNI forbids). |
| **FR-051 backup-before-migration** (Constitution VII MUST) | **NAMED NO-OP — there is no schema change to back up before.** This slice introduces no EF migration: priority/description activate pre-existing mapped columns; NodaTime is boundary-computation only (no column remap, no `Npgsql.NodaTime`) → unchanged model snapshot; the Today/Upcoming range index is deferred. This is the **slice-003 posture**, NOT slice 004's LIVE posture. | No action this slice. The pre-migration backup hook stays in place for the next slice that DOES migrate. `research.md`, `plan.md`, and `data-model.md` all state FR-051 as a no-op — a divergence (one artifact claiming LIVE) is the failure mode to avoid. Verified by the absence of a new file under `Persistence/Migrations/` in this slice's diff. |
| **`Npgsql.NodaTime` plugin + `timestamptz` → `Instant` remap** | **Deliberately NOT adopted (named non-adoption).** NodaTime is introduced this slice, but narrowly — boundary computation only. Adopting the plugin would remap every timestamp property, change the model snapshot, and force a migration (flipping FR-051 to LIVE) for zero behavioral gain. | If a future slice needs NodaTime types at the persistence boundary, the plugin is an additive, contained adoption then. This slice keeps the columns mapped to `DateTime` (research R1-a / data-model §3). |
| **Markdown-to-HTML rendering of `description`** | **Deferred (no owned scenario displays a formatted description).** This slice stores/edits the markdown **source** as raw text; display is React-escaped (trivially safe). | The renderer + its sanitizer allow-list arrive with the first slice that has a scenario *displaying* a formatted description; the stored source is unchanged, so it is an additive, contained change. The Constitution XII row records the sanitize-on-render requirement explicitly so the next slice inherits it (research R3/R12). |
| **The optional Today/Upcoming range index** `(created_by, due_date) WHERE deleted_at IS NULL` | **Deferred (YAGNI), following slice-004's deferred-index precedent.** | Added later only if profiling warrants, as a contained additive migration — keeping it out preserves the zero-migration / FR-051-no-op posture this slice (research R6-c). |

> Of the above, **open question #1 (the BLOCKER) is RESOLVED** via option (a): slice 007 was sequenced before slice 005 (rebased), so the shared half (membership + role arm + the two SC-016 deny tests) is realized in this slice — no longer a gate. The remaining items are accepted, scoped deferrals — FR-051's no-op is correct (no migration), the plugin/renderer/index are scoped to their owning need, and none is an unjustified constitution violation.

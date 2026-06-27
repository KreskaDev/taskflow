# Research & Design Decisions: Daily Planning (slice 005)

**Input**: `spec.md`, `.specify/memory/constitution.md` (v4.0.0), `.specify/memory/product-vision.md`, and the slice-002/003/004 substrate.

This slice delivers the **mouse-free daily loop**: the **Today** and **Upcoming** read views, task **priorities** (P0–P3), a full keyboard-driven **task editor** (priority, description, reschedule due date, project), and the `G I` / `G U` / `G T` navigation. It is the **first slice that computes "same calendar day in `Europe/Warsaw`" server-side** — the responsibility slice 003 explicitly handed forward ("NodaTime arrives in slice 005, the first slice that computes same calendar day in Europe/Warsaw server-side (Today/Upcoming)", slice-003 plan Complexity Tracking). It therefore **owns introducing NodaTime** on the server (R1) and the server-side zone-aware Today/Upcoming filtering (R6).

The format mirrors slice 003/004: each decision is **Decision / Rationale / Alternatives considered**, cross-referenced from `plan.md` and `data-model.md`.

**Reference identity for examples**: an authenticated, admitted caller `U`. Unless a decision says otherwise, examples use personal-visibility, owner = `U` data — the only authorization branch this slice can fully realize in code (see the **blocker** below and R10).

The two repo facts that anchor everything (verified in code, not inferred):

1. **The `priority` and `description` columns already exist.** `tasks.description` and `tasks.priority` are mapped in `TaskConfiguration.cs` (`HasColumnName("description")` / `HasColumnName("priority")`, both nullable text) and the `Task` aggregate already declares `Description`/`Priority` properties (private setters, documented "Reserved (slice 005)"). → **No add-column migration to activate them** (R2/R3).
2. **The server stores `timestamptz` as a plain `DateTime`.** `tasks.due_date` is mapped `HasColumnType("timestamp with time zone")` over `Task.DueDate` (`DateTime?`), with **no** NodaTime. Today/Upcoming need a Warsaw-day boundary in C#; introducing NodaTime for **boundary computation only** (not remapping the columns) leaves the model snapshot unchanged → **no forced migration** (R1).

---

## ⚠ BLOCKER (open question #1) — the membership/role authorization branch (FR-066/FR-067) the spec mandates is not realizable in this slice's declared dependency set

**This must be resolved by a sequencing or spec decision before slice 005 can ship as specified. It is surfaced here, not silently engineered away.**

**The conflict, with citations:**

- Slice-005 **spec mandates the membership branch as in-scope**, not referenced-only:
  - "the Today and Upcoming views surface BOTH the caller's personal tasks AND tasks in shared projects the caller is a current member of … shared-project → membership + role" (spec L74, the dispatch-by-visibility note).
  - FR-065/FR-066/FR-067/FR-068 are reproduced in the **"realized in this slice"** Access-control block (spec L123–128), and FR-067 is stated operationally: "a viewer MAY see a shared task in Today/Upcoming but MUST be denied the priority/edit/reschedule/toggle-done mutations" (spec L127).
  - **SC-016 mandates the deny tests**: "every data handler … ships with both an allow test and a deny test, including a deny for a non-member reading another project's task and a deny for a viewer attempting a mutation (SC-016)" (spec L172, Principle VIII compliance).
- The **substrate does not exist and was deferred past this slice**:
  - Slice-004 **R11** realizes "**only** the `personal` value … The `shared` value, the `ProjectMembership` set, and the membership+role authorization branch (FR-066/FR-067) are **not** modeled here — they are slice 007" (004 research R11).
  - Slice-004 **plan Complexity Tracking** repeats: "Realized in **slice 007 (project-sharing-membership)** — … the `ProjectMembership` set + the membership+role authorization branch".
  - Code confirms it: there is **no `ProjectMembership` aggregate, no `shared` visibility realization**, and `Project.Visibility` has only `PersonalVisibility = "personal"` writable (`Project.cs` L33, L74).
- Slice-005's own **"Depends on"** lists only **slice 003 and slice 004** (spec L49–51) — **not** slice 007. So the spec asks this slice to authorize shared-project tasks by membership + role, deny a non-member read and a viewer mutation, and **test** both — using a `ProjectMembership` set that, by the dependency graph, does not yet exist.

**Why no "clever reading" dissolves it.** Slice 004 attached explicit scope notes deferring `shared` to 007; slice 005's spec carries **no** such deferral and affirmatively requires membership-aware Today/Upcoming plus the two named deny tests. A view that "surfaces tasks in shared projects the caller is a current member of" is uncomputable without the membership set. There is no interpretation under which the SC-016 viewer-deny / non-member-deny tests are writable against the current substrate.

**Recommendation (a sequencing/spec decision, NOT settled here, and explicitly NOT scope creep):**

1. **Do NOT design or pull `ProjectMembership` forward into slice 005.** That is slice 007's owned scope; building it here is exactly the scope creep the constitution's YAGNI discipline and the slice boundaries forbid.
2. The honest resolution is one of:
   - **(a) Re-sequence**: land **slice 007 before slice 005** (add slice 007 to slice 005's "Depends on"), so the membership branch and its deny tests are realizable; **or**
   - **(b) Amend slice 005's shared scope**: explicitly defer the shared-project half of Today/Upcoming (and the viewer-deny / non-member-deny portion of SC-016) to a post-007 slice, leaving slice 005 to realize the **ownership branch in full** plus the dispatch-by-visibility **seam** (R10).
3. Until that decision is made, this plan realizes the **ownership branch fully** and structures the Today/Upcoming query handlers as a **dispatch-by-visibility seam** with the membership branch a named, not-yet-realized arm (R10). Under option (b) the slice is shippable as-is; under option (a) the membership arm fills the seam with no handler-shape change.

This blocker governs R6 (read-query shape) and R10 (authorization) below; both are written to the seam so neither resolution forces a rewrite.

---

## R1 — Introduce **NodaTime server-side for boundary computation only**; do **not** adopt the Npgsql NodaTime plugin (no column remap, no migration)

**Decision**: Add the **NodaTime** package to `TaskFlow.Application` (and/or a small `TaskFlow.Domain` time seam) and use it to compute Warsaw calendar-day boundaries — `DateTimeZoneProviders.Tzdb["Europe/Warsaw"]`, `LocalDate`/`ZonedDateTime` → `Instant` → `DateTime` (UTC `Kind`). Do **NOT** add the `Npgsql.NodaTime` plugin and do **NOT** remap the `timestamptz` columns from `DateTime` to `Instant`. The `due_date`/`created_at`/`updated_at`/… columns stay mapped exactly as today (`timestamp with time zone` over `DateTime`); NodaTime is used **only** to derive the UTC instants that bound the SQL range filter (R6). A single server helper (e.g. `ReferenceZoneClock`/`WarsawDayBounds`) is the **one** place the boundary math lives, mirroring the client's `lib/timezone.ts` seam (FR-092, "applied identically on client and server").

**Rationale**: Slice 003 deferred the server timezone library to "slice 005 … the first slice that computes same calendar day in Europe/Warsaw server-side (Today/Upcoming)" (003 plan Complexity Tracking) — so this slice **owns** introducing it. NodaTime is the canonical .NET IANA-tzdb library and computes DST-correct day boundaries "by the timezone library, not fixed-offset arithmetic" (Constitution X / FR-092) — the exact guarantee the client already gets from `date-fns-tz`. Restricting it to **boundary computation** (not column mapping) is the minimal, YAGNI-correct adoption: the only thing this slice computes zone-dependently is `[startOfWarsawDay, endOfWindow)` as two UTC instants, after which the SQL is a plain UTC `due_date >= @lo AND due_date < @hi` range — **zone-free in the database**. Crucially, **not** remapping the columns means the EF model snapshot is unchanged, so **no migration is generated** by adopting NodaTime (verified: the columns are mapped to `DateTime` today; leaving that mapping untouched produces no snapshot delta). This keeps FR-051 a **named no-op** this slice (R9), consistent with slice 003's pattern and avoiding a column-rewrite migration on the hot `tasks` table.

**Alternatives considered**:
- **(a) Adopt `Npgsql.NodaTime` + remap `timestamptz` → `Instant`** — rejected. It is a broad blast-radius change (every timestamp property, every read model projection, every existing test that asserts `DateTime`) and it **generates a migration** (the column CLR-type/converter changes the snapshot) for zero behavioral gain — the storage is identical UTC instants either way. The boundary math does not need the columns to be `Instant`; it needs two UTC `DateTime` bounds, which the helper produces.
- **(b) Compute the Warsaw day boundary with `TimeZoneInfo.FindSystemTimeZoneById("Europe/Warsaw")`** (BCL, no new package) — rejected. The IANA id resolves cross-platform on .NET 6+, but the constitution names a **timezone library** for DST and FR-092 wants the **same** library semantics client/server; NodaTime's `LocalDate`-to-`Instant` model is far less error-prone than hand-rolling `TimeZoneInfo` offset lookups around the DST seam, and it is the idiomatic choice the constitution's "DST … handled by the timezone library" clause anticipates.
- **(c) Compute boundaries client-side and pass them as query parameters** — rejected. FR-092 requires the rule applied **identically on client and server**, and Principle IX requires the server to be authoritative over what a query returns; letting the client supply the day boundary would let a caller shift their own "today" and is not server-authoritative. The client computes the same boundary **for optimistic membership** (R7), but the server recomputes it independently.

---

## R2 — Priority model: activate the reserved `priority` column as a **`"P0".."P3"` string**, key `1`→P0 … `4`→P3, NULL = unprioritized

**Decision**: Activate the existing nullable `tasks.priority` column. The **wire + storage encoding is the lowercase-free token string `"P0"`, `"P1"`, `"P2"`, `"P3"`** (matching the column's existing `string?` CLR type — no enum-column migration), with **NULL meaning "no priority set"**. The keyboard mapping is **`1` → P0, `2` → P1, `3` → P2, `4` → P3** (P0 = highest/most urgent, per spec AS-04 "press `1` … priority changes to P0"). The `Task` aggregate gains a `SetPriority(string? priority, DateTime utcNow)` behavior (validates membership in the closed set `{P0,P1,P2,P3}` or null; bumps `Version`); a FluentValidation rule on the command + a Zod enum on the client enforce the closed set at both trust boundaries (Constitution VI). `TaskResponse` gains a nullable `priority` field.

**Rationale**: The column is already `string?` and documented "Reserved (slice 005) — priority P0–P3" (`Task.cs` L80–81), so a string token is the zero-migration activation (R3 precedent: slice 003 activated `due_date` the same way). `P0` is the conventional "highest" in this product's vision (FR-002 lists `P0/P1/P2/P3`), and the spec's AS-04 fixes `1`↔P0, so the `1..4` → `P0..P3` mapping is forced by the spec, not chosen. NULL-as-unset is necessary because not every task has a priority (capture in slice 002 set none), and it must sort **last** in the deterministic order (R5). The closed set validated on both tiers keeps a bad value a clean **422**, never a 500 or a poisoned row.

**Alternatives considered**:
- **(a) A real Postgres `enum` / check-constraint column** — rejected; it forces an `ALTER TYPE`/check-constraint **migration** to change the column the slice was handed pre-mapped, for a guarantee the boundary validators already provide. The string-token + validator approach mirrors how `status` is encoded (a wire string mapped in `TaskResponse.ToWireStatus`).
- **(b) An integer column `0..3`** — rejected; the column is `string?` today (changing it is a migration), and the product vision + spec speak in `P0..P3` tokens, so a string token keeps wire/spec/storage in one vocabulary.
- **(c) Make priority non-nullable with a default `P3`** — rejected; it would silently re-prioritize every existing unprioritized task and contradicts "NULL = unprioritized," which the Today sort depends on.

---

## R3 — Description: activate the reserved `description` column as **raw text**, edited in the task editor; **no markdown-to-HTML renderer this slice**

**Decision**: Activate the existing nullable `tasks.description` column as a **plain string** (`string?`, already mapped). The full task editor (R4) edits it as **multi-line text**; on save the `Task` aggregate stores it verbatim (trimmed; a length bound, e.g. ≤ a few KB, validated at both boundaries). **No markdown rendering pipeline is introduced this slice** — the description is stored and edited as raw text, and any *display* of it is **React-escaped / output-sanitized** (never `dangerouslySetInnerHTML`), per Constitution XII / FR-099. `TaskResponse` gains a nullable `description` field.

**Rationale — resolved against the spec, not assumed**: FR-002 types description as "description (markdown)" (spec L112), and Constitution XII names "task markdown descriptions" as untrusted content. **But the slice's owned acceptance scenarios require only editing**: AS-06 opens the editor "with the title field focused, allowing inline editing"; AS-07 saves with `Ctrl+Enter`; AS-08 discards with `Esc` (spec L83–85). **No owned scenario, and no success criterion, requires rendered markdown.** Therefore the defensible, YAGNI-correct position is: **store and edit the markdown source as raw text this slice; defer any markdown-to-HTML rendering** to whatever slice first has a scenario that *displays* a formatted description. This satisfies FR-002 (the field exists and accepts markdown source) and Constitution XII (the untrusted content is output-encoded on render — and a raw-text render is trivially safe) without pulling a sanitizer/renderer dependency forward with no scenario to justify it. The plan's Constitution XII row states this explicitly so a compliance reviewer reads it as a decision, not a gap.

**Alternatives considered**:
- **(a) Ship a markdown renderer (e.g. a sanitized `react-markdown`) this slice** — rejected (YAGNI); no owned scenario displays formatted description, so the renderer + its sanitizer allow-list + their tests would be dependency-and-test surface ahead of need. When a render scenario lands, the renderer is an additive, contained change (the stored source is unchanged).
- **(b) Constrain description to a "safe subset" at write time** — rejected; sanitizing **on render** (Constitution XII) is the correct boundary, and there is no render this slice, so write-time sanitization would mangle the user's source for no benefit. Storage stays verbatim; the render boundary stays escaped.

---

## R4 — Editor command surface: **discrete quick-mutations** for the single-key verbs (`SetPriority`, `RescheduleDueDate`, toggle-done) + **one combined `EditTask`** for the `E` editor (atomic save / discard)

**Decision**: Split the editor surface by interaction shape, following the slice-002 (per-field PATCH for single-key verbs) and slice-004 R4 (one combined `EditProject` for the form) precedents:

- **`SetPriority`** (new) — the `1`-`4` instant mutation (AS-04). `PATCH /api/tasks/{id}/priority`, body `{ priority: "P0".."P3" | null, version }`. One optimistic paint, one `version` round-trip.
- **`RescheduleDueDate`** (new) — the `T` reschedule (AS-05). `PATCH /api/tasks/{id}/due-date`, body `{ dueDate: <utc-instant> | null, dueHasTime: bool | null, version }`. The **client** parses the Polish phrase ("jutro") with the slice-003 parser and resolves the instant; the **server validates** the resolved instant (pairing invariant + UTC-kind + plausible range — the slice-003 `CreateTaskValidator` rules, reused) and stores it. This realizes the reschedule that slice 003 deferred ("reschedule = slice 005", 003 plan Key Design Decisions).
- **`SetTaskDone` (reuse)** — `Space` toggle-done (AS-03). The handler/command already exist from slice 002 (`SetTaskDone.cs`); this slice exercises them from the Today view (no new command).
- **`EditTask`** (new, combined) — the `E` editor (AS-06/07/08). `PATCH /api/tasks/{id}/edit`, body `{ title, description, priority, dueDate, dueHasTime, projectId, version }` — a **whole-object replace** of the editable fields, saved atomically on `Ctrl+Enter`, **discarded entirely on `Esc`** (no partial commit). Reusing the slice-004 R4 "whole-object replace, all editable fields required" shape avoids the silent-null footgun. The path is **`/api/tasks/{id}/edit`**, not bare `/api/tasks/{id}` — the bare path is already taken by the slice-002 create (`WolverinePut /api/tasks/{id}`) and delete (`WolverineDelete /api/tasks/{id}`), and every per-field mutation follows the verified `/api/tasks/{id}/<field>` convention (`/title`, `/status`, `/position`, `/project`), so `/edit` is the convention-consistent slot.

The `1`-`4` and `T` keys are **discrete instant mutations regardless of the editor** (AS-04/AS-05 fire from the list row, not inside the editor), so they get dedicated quick-commands even though `EditTask` can also change those fields. **Project reassignment inside `EditTask`** sets `tasks.project_id` — but note the existing **`MoveTaskToProject`** command (slice 004) already moves a task to a project via `M`; `EditTask` reuses `Task.MoveToProject` internally for the project field rather than duplicating the move logic.

**Rationale**: The single-key verbs (`1`-`4`, `T`, `Space`) are **independent instant mutations** painting <16 ms (Principle III / SC-003) — exactly the slice-002 per-field-PATCH pattern; folding them into a fat `EditTask` would force a full-object round-trip for a one-key action. The `E` editor is a **form** with atomic `Ctrl+Enter`-save / `Esc`-discard-all semantics (AS-07/AS-08) — exactly the slice-004 R4 combined-command pattern. Using both, each where its interaction shape fits, keeps the contract honest and the optimistic surfaces minimal. All artifacts (data-model, contracts, tasks) must agree on these names/paths/bodies.

**Alternatives considered**:
- **(a) One fat `EditTask` for everything, including `1`/`T`** — rejected; a single-key priority change would pay a whole-object round-trip and the optimistic patch would have to reconstruct the full editable object, contradicting the slice-002 per-field convention and the <16 ms budget's simplicity.
- **(b) Separate `SetDescription` / `SetTitle` quick-commands** — rejected (YAGNI); there is no single-key verb for description or title-only edit this slice (title inline-edit was superseded by the editor per spec L56), so those fields only ever change **inside** `EditTask`. No scenario edits description alone.
- **(c) A new `EditTask` that does NOT reuse `MoveToProject`** — rejected; it would duplicate the move semantics (and the both-ownership authorization, R10) that `MoveTaskToProject` already encodes.

---

## R5 — Today vs Upcoming membership & deterministic ordering (FR-022/FR-023, refining the spec's resolution)

**Decision**: Both views **exclude `done` and `cancelled`** tasks by default and are scoped to the caller's accessible set (R10). The **date predicate** is computed against the Warsaw day boundary (R1/R6):

- **Today** = tasks whose `due_date` falls on **today in Warsaw** `OR` are **overdue incomplete** (`due_date` strictly before the start of today-Warsaw, status not done/cancelled) — "so nothing silently falls off the day" (spec L72). Overdue tasks are **flagged overdue** in the read model.
- **Upcoming** = tasks whose `due_date` falls in the **next 7 days** — the window `[start of tomorrow-Warsaw, start of (today+8)-Warsaw)` — i.e. the 7 calendar days **after** today, grouped by day (FR-023). Today's tasks are **not** in Upcoming (a task due tomorrow appears in Upcoming, not Today — Constitution X). Tasks **with no `due_date` are in neither** view (they live in the Inbox / project lists).

**Deterministic order** (spec L72): **Today** groups by **project** (Inbox/unprojected as its own group), then within each group sorts by **priority (P0 first)**, then **due time**, then **created**. **NULL priority sorts LAST** within its group (after P3) — an unprioritized task is the lowest triage rank. For the **due-time** tie-breaker, a **date-only** task (`due_has_time = false`) sorts as **start-of-day** (it has no specific time), so date-only tasks precede same-day timed tasks; ties then break by `created_at` (then `id` for total determinism). **Upcoming** groups by **day** first, then applies the same priority → due-time → created order within each day.

**Rationale**: This is the slice-005 resolution of the product-vision "needs-more-info" item for these views (spec L72) and refines — without contradicting — the verbatim FR-022/FR-023. Excluding done/cancelled keeps the triage views focused on actionable work (Principle IV); including overdue-in-Today is explicit in the spec. NULL-priority-last is forced by R2 (NULL = unprioritized = lowest rank). Treating date-only as start-of-day for ordering is the natural reading of `has_time = false` and keeps the sort total and deterministic (every comparison resolves, ending at `id`). The window boundaries are half-open UTC instants from the R1 helper, so the DB filter is zone-free.

**Alternatives considered**:
- **(a) NULL priority sorts FIRST (as "P0-ish")** — rejected; an unset priority is the *absence* of urgency, not maximum urgency; sorting it first would bury the user's explicitly-P0 tasks.
- **(b) Include done tasks (struck through) in Today** — rejected; the spec says both views exclude done/cancelled by default, and a triage view cluttered with completed work fights Principle IV.
- **(c) Upcoming includes today** — rejected; Constitution X is explicit ("A task due tomorrow MUST appear in Upcoming, not Today"), so Today and Upcoming partition the timeline, they do not overlap.

---

## R6 — Read-query shape: **server-side, zone-aware filtering** over a plain UTC range; CQRS read projections; dispatch-by-visibility seam

**Decision**: Add two query handlers — **`GetTodayTasks`** (`GET /api/tasks/today`) and **`GetUpcomingTasks`** (`GET /api/tasks/upcoming`). Each:
1. Computes the Warsaw day boundary in C# via the R1 helper → two UTC `DateTime` bounds.
2. Issues an **owner-scoped** SQL query filtered on a **plain UTC `due_date` range** + status-exclusion + `deleted_at IS NULL` (the membership arm is the not-yet-realized seam, R10) — **the SQL is zone-free** (all zone math already collapsed to instants).
3. Projects to a read model: `TodayTaskResponse` / day-grouped `UpcomingTaskResponse` carrying the existing `TaskResponse` fields **+ an `isOverdue` flag** (Today) and the **group key** (projectId for Today, the Warsaw calendar date for Upcoming), applying the R5 deterministic order server-side.

The grouping (by project / by day) and ordering are computed **server-side** so the client renders a ready-to-paint list (the boundary fact is server-authoritative, R1-c). The client recomputes the same boundary only to decide **optimistic membership** on a local mutation (R7).

**Rationale**: FR-092 requires the membership rule applied **identically client/server** and Principle IX requires the server authoritative over query results — so the **filter and the day boundary live on the server**. Computing the boundary in C# and filtering on a **plain UTC range** keeps the database zone-agnostic (no `AT TIME ZONE` in SQL, no NodaTime plugin) — the cleanest, most testable shape and the reason R1 needs NodaTime only for the boundary. Server-side grouping/ordering means the client never re-derives the deterministic order (one source of truth for R5). Structuring both handlers as a **dispatch-by-visibility seam** (ownership arm realized; membership arm a named stub) means the R10/blocker resolution drops in without reshaping the query.

**Alternatives considered**:
- **(a) Client-side filtering** (fetch all tasks, filter "today" in the browser) — rejected; not server-authoritative, and at the 10k-per-user working-set anchor it ships far more rows than the view needs.
- **(b) `AT TIME ZONE 'Europe/Warsaw'` in the SQL predicate** — rejected; it pushes zone logic into the database (a second place the rule lives, risking client/server divergence) and defeats a plain-range index; computing the boundary once in C# is simpler and keeps the rule in the **one** shared seam (R1).
- **(c) A composite index `(created_by, due_date) WHERE deleted_at IS NULL`** to serve the range — **considered, deferred** (no migration this slice). Following slice-004's deferred-index precedent (004 data-model §1: optional index "only if … profiling warrants; not added pre-emptively (YAGNI)"), at the per-user authz-scoped working-set anchor a per-user scan is expected within the read budget. Adding the index later is a contained, additive migration if profiling warrants it — and keeping it out **preserves the zero-migration / FR-051-no-op posture** (R9).

---

## R7 — Optimistic UI: priority / reschedule / toggle-done paint <16 ms; view membership recomputed locally on mutation

**Decision**: `SetPriority`, `RescheduleDueDate`, `SetTaskDone`, and `EditTask` are TanStack Query optimistic mutations on the relevant cache keys (`['tasks','today']`, `['tasks','upcoming']`, `['tasks']` Inbox, `['projects']`) following the slice-002 `onMutate`/`onError`/`onSettled` recipe (snapshot → optimistic patch → rollback → settle). When a mutation changes a task's **view membership** — a reschedule to tomorrow removes it from Today and adds it to Upcoming (AS-05: "it disappears from Today view"); a toggle-done removes it from both — the client recomputes membership using the **same Warsaw boundary helper** (`lib/timezone.ts`, the client mirror of R1) so the optimistic patch matches what the server will return. A server rejection rolls the row back; the rollback message is announced via the polite live region (FR-101) without stealing focus.

**Rationale**: SC-003 (owned by slice 002) requires these mutations to paint within 16 ms; the boundary computation is synchronous in-process (no network), so optimistic membership is decidable locally. Using the same boundary helper client-side as the server uses (R1) is exactly the FR-092 "identical client/server" guarantee in action — the optimistic membership decision equals the authoritative one, so reconciliation is a no-op in the common case.

**Alternatives considered**:
- **(a) Refetch the view after every mutation instead of optimistic membership** — rejected; a refetch round-trip violates the <16 ms paint budget and the "instant" feel for a one-key action.
- **(b) Let the server decide membership and not recompute client-side** — rejected; the optimistic paint must happen *before* the server responds, so the client must compute membership; FR-092's identical-rule guarantee is what makes this safe.

---

## R8 — Navigation (`G T` / `G I` / `G U`) and single-key suppression: reuse the slice-002 shortcut substrate

**Decision**: Bind the **`G`-prefixed** navigation chords — `G T` (Today), `G I` (Inbox), `G U` (Upcoming) — in the existing global-shortcut layer (`useGlobalShortcuts`, extended in slice 004 for `M`). `G I` resolves to the **Inbox** route defined by slice 004 (`GET /api/tasks` narrowed to `project_id IS NULL`, 004 R6); `G T`/`G U` route to the new Today/Upcoming views (R6). The list single-key verbs (`1`-`4`, `T`, `E`, `Space`) and the editor shortcuts (`Ctrl+Enter`, `Esc`) ride the **same** suppression gate slice 002 built (FR-031): single-key shortcuts are inert while a text input (the editor fields, the `T` date input) is focused; only modifier chords (`Ctrl+Enter`) remain live during text entry (spec L131).

**Rationale**: The shortcut dispatcher, the `G`-chord grammar, and the single-key-suppression gate already exist (slices 002/004); this slice adds bindings, not machinery. Reusing the suppression gate is what lets `1`-`4`/`E`/`T` be single-key in the list yet not hijack typing inside the editor (FR-031, Constitution I/II). The editor and the `T` date input follow the slice-002/003 **dialog focus contract** (set initial focus, trap, Esc-dismiss, return focus to the originating row — spec L166/FR-101).

**Alternatives considered**: A new shortcut subsystem — rejected; the slice-002 substrate already covers chords + suppression + the focus contract.

---

## R9 — Migrations & FR-051: **no migration this slice → FR-051 is a named no-op** (like slice 003)

**Decision**: This slice introduces **no EF migration**. Priority and description activate **pre-existing mapped columns** (R2/R3); NodaTime is adopted for **boundary computation only**, not column remapping, so the model snapshot is unchanged (R1); the optional Today/Upcoming range index is **deferred** (R6-c). Consequently **FR-051 (backup-before-migration) is a named no-op this slice** — there is no schema change to back up before — exactly the slice-003 posture (003 plan Complexity Tracking), **not** the slice-004 LIVE posture. The plan's Constitution VII row and Complexity Tracking state this explicitly so a reviewer reads it as a decision, not a gap; verified by the absence of a new file under `Persistence/Migrations/` in this slice's diff.

**Rationale**: Every persistence change this slice needs is an activation of an already-migrated column or an in-process computation — the same forward-compat dividend slice 002 set up (reserved columns) and slice 003 spent for `due_date`. Keeping NodaTime to boundary-only (R1) is precisely what avoids a remap migration. **Consistency anchor**: `research.md`, `plan.md`, and `data-model.md` MUST all state FR-051 as a **no-op** this slice; a divergence (one artifact claiming LIVE) is the failure mode to avoid.

**Alternatives considered**:
- **(a) Add the `(created_by, due_date)` index now** (→ migration → FR-051 LIVE) — rejected (R6-c); premature without profiling, and it would flip FR-051 to LIVE for a perf change not yet justified.
- **(b) Remap columns to `Instant`** (→ migration) — rejected (R1-a); broad blast radius, no behavioral gain, flips FR-051 to LIVE needlessly.

---

## R10 — Authorization: **ownership branch realized in full**, deny-by-default, scoped queries, allow+deny tests; the membership arm is a **named seam** (governed by the blocker)

**Decision**: Every new query (`GetTodayTasks`, `GetUpcomingTasks`) and command (`SetPriority`, `RescheduleDueDate`, `EditTask`; `SetTaskDone`/`MoveTaskToProject` reused) is **deny-by-default and enforced at the handler** (FR-068). For **personal/unprojected** tasks — the branch this slice realizes — authorization dispatches on **ownership**: reads scoped `WHERE created_by = caller AND deleted_at IS NULL` (+ the R5/R6 view predicates), writes coerce no owner from the wire and resolve a foreign/absent/tombstoned id to **404** (existence not disclosed, the slice-002/003/004 posture). `createdBy` is **provenance only**, never a standalone grant (FR-066's already-applicable half). Per Constitution VIII + the governance gate, **every** new handler ships an **allow** and a **deny** integration test through the real DB (the deny = a caller acting on another user's personal task → 404).

The Today/Upcoming handlers and the mutation handlers are written as a **dispatch-by-visibility seam**: the **ownership arm is live**; the **shared-project membership + role arm (FR-067: viewer=read, editor/owner=write) is a named, not-yet-realized branch** pending the blocker's resolution. The SC-016 **viewer-deny** and **non-member-deny** tests are **unwritable until the `ProjectMembership` substrate exists (slice 007)** — see the blocker. Under blocker-resolution (a) they are written against the slice-007 substrate once sequenced before 005; under (b) they are deferred with the shared scope.

**Rationale**: This is the only authorization branch this slice **can** fully realize against the current substrate (R10's hands are tied by the blocker). The ownership coercion + scoped load is the proven slice-002/004 pattern, replicated per handler for uniformity, with 404-not-403 to avoid leaking existence. Writing the handlers as a **seam** means whichever way the blocker resolves, the membership arm fills in without reshaping the query or the command — no rework, and the spec's dispatch-by-visibility intent (spec L74) is structurally honored even while its shared half awaits its substrate.

**Alternatives considered**:
- **(a) Implement the membership branch now** (pull `ProjectMembership` forward) — rejected; that is slice 007's owned scope and the YAGNI/slice-boundary discipline forbids building it here (see the blocker, point 1).
- **(b) Drop the dispatch-by-visibility seam and write ownership-only handlers** — rejected; the seam costs nearly nothing and avoids a query/command reshape when the membership arm lands; it also keeps the code legibly matched to the spec's intent.

---

## R11 — Error contract: **no new error code** — reuse `validation_failed` / `not_found` / `version_conflict`

**Decision**: This slice adds **no** new `ErrorCode`. An out-of-set priority, a bad reschedule instant (pairing / non-UTC-kind / implausible range — the slice-003 rules reused), or an over-long description → **422 `validation_failed`** (field-level message in the ProblemDetails envelope); a foreign/absent/tombstoned task id or (when the membership arm lands) a non-member/insufficient-role access → **404 `not_found`** (ownership/visibility posture); a stale mutation token → **409 `version_conflict`**. The `ErrorCode` union and the `ERROR_UX satisfies Record<ErrorCode, ErrorUx>` map stay exhaustive **with no change** — no `TaskFlowDocumentTransformer` `ErrorCodes` edit.

**Rationale**: Mirrors slice 003/004's deliberate reuse. Every failure shape this slice introduces maps cleanly onto an existing code, and the 422 envelope's field-level `errors` carry the specific human messages (bad priority, "nie rozpoznano" for the reschedule, over-long description). No new code means no transformer edit and no `ERROR_UX` growth.

**Alternatives considered**: A `priority_invalid` / `description_too_long` code — rejected; the distinction the client needs lives in the 422 field-error text, not a new top-level code (the slice-003/004 precedent).

---

## R12 — Security: `description` and `title` are untrusted user content — output-encoded on render (FR-099 / Constitution XII)

**Decision**: The task `description` (markdown source, R3) and any `title` rendered in Today/Upcoming/the editor are **untrusted user-authored content** and are **React-escaped / output-sanitized** on render — never `dangerouslySetInnerHTML`, no raw-HTML path. Because R3 ships **no markdown renderer**, the description renders as escaped raw text (trivially safe); when a render slice arrives it MUST sanitize to a constrained safe subset (Constitution XII). Structured rejection logging (FR-050) carries `ErrorCode`/`Method`/`Path` only — never the title, description, or carrier. The slice-001 CSP / security headers + BFF→API signed carrier are reused unchanged; **no new secrets** (FR-100).

**Rationale**: Constitution XII names task markdown descriptions as the untrusted surface; with no renderer this slice the surface is closed by React's default escaping, and the decision is recorded so the next slice that *renders* description inherits the sanitize-on-render requirement explicitly. No free text from this flow reaches a raw-HTML sink.

**Alternatives considered**: Sanitize description at write time — rejected (R3-b); render-boundary encoding is the correct control, and there is no render this slice.

---

## R13 — Test surface (Constitution VIII)

**Decision**: Red-Green-Refactor across tiers:
- **Backend xUnit (domain)**: `Task.SetPriority` (set/clear/closed-set guard), the reschedule path (reuse slice-003 due-date round-trip), `EditTask` whole-object replace.
- **Backend integration (Testcontainers-Postgres)**: `GetTodayTasks`/`GetUpcomingTasks` membership (due-today, overdue-in-Today, tomorrow-in-Upcoming-not-Today, no-due-in-neither, done/cancelled-excluded), the R5 deterministic order (incl. NULL-priority-last, date-only-as-start-of-day), a **DST-boundary** case (a task right at the Warsaw DST seam lands in the correct day — guards R1/FR-092), and **per handler an allow + a deny** test (deny = another user's personal task → 404). **The SC-016 viewer-deny / non-member-deny tests are tracked as blocked** pending the membership substrate (the blocker).
- **Frontend Vitest**: priority/reschedule/edit optimistic surfaces + the client membership recompute (R7) using the frozen Warsaw clock; the same DST-boundary case client-side (FR-092 parity).
- **E2E Playwright**: AS-01..AS-08 (Today render/group/sort, `Space`/`1`/`T`/`E`/`Ctrl+Enter`/`Esc`), US-08 AS-01/AS-02 (`G I`/`G U`), and SC-008's WCAG 2.1 AA audit on the Today and Upcoming views.

**Rationale**: Each owned acceptance scenario is independently testable; the DST-boundary test on **both** tiers guards the FR-092 "identical client/server" guarantee that R1/R7 depend on; the allow+deny-per-handler gate is the governance requirement. The one honestly-unwritable test set (SC-016's shared-project denies) is surfaced as blocked, not faked.

**Alternatives considered**: Asserting the Today boundary against a fixed UTC offset — rejected; it would pass in winter and fail across the DST seam, the exact bug FR-092 forbids.

---

## Resolved unknowns summary

| Plan unknown | Resolved by |
|---|---|
| **Membership/role branch (FR-066/067) vs. the dependency set — the blocker** | **Open question #1** (surfaced, not resolved; governs R6/R10) |
| Introduce NodaTime + plugin? Where is "same Warsaw day" computed? | R1, R6 |
| Priority (P0–P3) model + key mapping + NULL handling | R2 |
| Description activation + markdown render scope | R3 |
| Editor command surface (reuse vs new; reschedule ownership) | R4 |
| Today vs Upcoming membership, overdue, 7-day window, ordering | R5 |
| Read-query shape (server-side zone-aware filtering) | R6 |
| Optimistic UI + client membership recompute | R7 |
| Navigation chords + single-key suppression | R8 |
| Migration + FR-051 status (no-op this slice) | R9 |
| Authorization (ownership realized; membership seam) | R10 |
| Error contract (no new code) | R11 |
| Security (untrusted description/title, no renderer) | R12 |
| Test surface incl. DST-boundary + blocked SC-016 denies | R13 |

# Tasks: Command Palette & Search

**Input**: Design documents from `/specs/013-command-palette-search/`
**Prerequisites**: plan.md, spec.md (+ Clarifications 2026-11-08), research.md (D1–D14), data-model.md, contracts/ (search-api.md, ui-palette.md), quickstart.md
**Tests**: INCLUDED — the spec mandates test-first (Constitution VIII); allow+deny matrix per contract (incl. the former-member deny); red repro before green per docs/bugfix-workflow.md
**Organization**: Two owned stories share one surface family — **US-04 Command Palette & Search** (the palette) and **US-08.AS-08** (the view filter). The foundational phase carries the server search endpoint; US-04 carries the palette; US-08 the filter hosts.

## Format: `[ID] [P?] [Story] Description`

## Path Conventions

Web monorepo per plan.md: `apps/web` (Next.js 15) + `apps/api` (.NET 9). Feature docs in `specs/013-command-palette-search/`.

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: No new dependencies; the regression-inventory rows that gate every UI behavior.

- [X] T001 [P] Append section „P. Command palette & search (slice 013)" to specs/019-ui-design-system/feature-inventory.md: new `INV-###` rows (continuing from INV-187) for every behavior in contracts/ui-palette.md — topbar „Szukaj…" opener present + in tab order on every view (no `Ctrl+K`), palette dialog focus contract (initial focus, trap, Esc returns focus — US-04.AS-06), combobox/listbox semantics + `↑/↓`/Enter, action catalogue for an empty query (Nowy task / Nowy projekt / Nowy cykl / Przejdź: …; no shortcut column — FR-033 deferred), task results by title AND description with location chip + navigation to `/?task=` or `/projects/<id>?task=` (AS-02/AS-03), „create project" alias → „Nowy projekt" executes (AS-04), project results + navigation (AS-05), archived suffix „(zarchiwizowany)" + inline „Przywróć" (US-10.AS-06), label result → query replaced by the label name, empty state „Brak wyników dla „…"", error state with „Spróbuj ponownie", LiveRegion „N wyników", view filter input on Inbox/Dziś/Nadchodzące/Przypisane/project List+Board/`/cycle` with diacritic-insensitive token matching + „Brak zadań pasujących do filtru" hint + „Wyczyść filtr" (US-08.AS-08), access scoping (another user's personal task / non-member / former member never listed — [C] via the integration matrix, [E] one UI fact) — each row: screen → Given/When/Then → outcome → realized FR/AS → covering level [C]/[E]/[A]/[V] (the inventory gate goes red now, green at slice exit)

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: The server search endpoint — scoped, ranked, capped — plus OpenAPI + typed client and the dedicated CI shard.

**⚠️ CRITICAL**: Blocks the palette (Phase 3); the view filter (Phase 4) is client-only and could start in parallel with T002–T008.

- [X] T002 [P] Author failing integration tests in apps/api/tests/TaskFlow.IntegrationTests/Search/SearchTestBase.cs (NEW namespace `TaskFlow.IntegrationTests.Search` — D13; extends the sharing base; helpers: `SearchAsync(token, q)` → `SearchBody`, seed task with description via AppDbContext, seed label for a user, bulk-insert N personal tasks for the characterization) + SearchMatchingTests.cs covering contracts/search-api.md §Matching: title and DESCRIPTION hits, case-insensitive, token-AND („kup mleko" yes / „kup ser" no), projects and labels by name, empty/whitespace `q` → three empty arrays, 201-char `q` → 422, `%`/`_` literal, ranking (exact → prefix → substring; done after open; archived project last), task cap 20 of 25
- [X] T003 [P] Author failing integration tests in apps/api/tests/TaskFlow.IntegrationTests/Search/SearchAccessTests.cs covering §Access: another user's personal task never returned; member (viewer) sees a shared project + its tasks incl. the owner's; non-member sees neither; former member (membership row deleted) sees neither even as creator/assignee; archived shared project still returned to members with its tasks; another user's label never returned; unauthenticated 401; plus SearchCharacterizationTests.cs: 10 000 personal tasks seeded by bulk insert → `q=mleko` responds within a 250 ms CI ceiling and logs the measured server time (SC-009 informational)
- [X] T004 Create apps/api/src/TaskFlow.Application/TaskManagement/SearchResponse.cs (`{ Tasks: TaskResponse[], Projects: ProjectResponse[], Labels: LabelResponse[] }`) and apps/api/src/TaskFlow.Application/TaskManagement/Queries/SearchTasksProjectsLabels.cs: query record `{ Q }`, FluentValidation (`Q` ≤ 200), handler per D1–D4 — tokenize (≤ 5 tokens, ≤ 100 chars, LIKE metachars escaped), resolve `members.ListProjectIdsForUserAsync(caller)` ONCE, call the three repository search methods, map through the existing `TaskResponse.From` (caller labels via `ListLabelIdsForTasksAsync`), `ProjectResponse`, `LabelResponse`; empty tokens → empty buckets without touching the DB
- [X] T005 Repository search methods: `ITaskRepository.SearchReadableAsync(caller, sharedProjectIds, tokens, limit)` (apps/api/src/TaskFlow.Application/TaskManagement/ITaskRepository.cs + apps/api/src/TaskFlow.Infrastructure/Persistence/TaskRepository.cs — `BuildReadableVisibilityPredicate` + `EF.Functions.ILike` per token over title/description, rank expression per data-model.md, open-before-done, `UpdatedAt` desc, `Take(limit)`), `IProjectRepository.SearchReadableAsync(caller, sharedProjectIds, tokens, limit)` (owned ∪ member ids, both archive states, archived last) in IProjectRepository.cs/ProjectRepository.cs, `ILabelRepository.SearchForOwnerAsync(owner, tokens, limit)` in Labels/ILabelRepository.cs + LabelRepository.cs; pattern escaping shared in a small `LikePatterns` helper (the `UserRepository` precedent)
- [X] T006 Endpoint + OpenAPI: new apps/api/src/TaskFlow.Api/Endpoints/SearchEndpoints.cs (`[WolverineGet("/api/search")]` with `q` query parameter → `bus.InvokeAsync<SearchResponse>`), behind the deny-by-default gate; extend apps/api/src/TaskFlow.Api/OpenApi/TaskFlowDocumentTransformer.cs with operationId `search` (path string EXACTLY `/api/search`) — no new error codes
- [X] T007 Add the dedicated CI shard in .github/workflows/ci.yml (D13): matrix entry `search` with filter `FullyQualifiedName~IntegrationTests.Search`, and append `&FullyQualifiedName!~IntegrationTests.Search` to the `identity-labels-infra` complement — every class still lands in exactly one shard
- [X] T008 Run `dotnet test` in apps/api — T002–T003 suites green (red→green), prior suites untouched; boot PG+API per quickstart.md, run `pnpm --dir apps/web gen:api`, commit the regenerated apps/web/src/lib/api/generated/schema.d.ts (`openapi-sync` guards it)

## Phase 3: User Story 4 — Command Palette & Search (Priority: P2) 🎯 MVP

**Goal**: From any view the user opens the palette with the topbar „Szukaj…" control, types to find tasks/projects/labels/actions, navigates with arrows + Enter (or click), and lands on the item or executes the action; archived projects can be restored from the result.

**Independent Test**: quickstart.md manual walk steps 1–6 + 8.

**Test-first (Constitution VIII)**: every unit-testable task authors its failing spec first; the E2E journey lands with the surfaces it drives.

- [X] T009 [P] [US4] Create apps/web/src/lib/palette.ts (pure — unit-tested first in apps/web/tests/unit/palette.test.ts): `PALETTE_ACTIONS` (D12 catalogue with Polish labels + English aliases, Lucide icon ids, kind `dialog:new-task | dialog:new-project | route`), `matchActions(q)` (client token-AND over label + aliases; empty `q` → all), `taskHref(task)` (`/?task=` vs `/projects/<pid>?task=`), `projectHref(project)`, `buildPaletteGroups(q, searchData)` → ordered groups Akcje → Zadania → Projekty → Etykiety with stable option ids (empty groups omitted), `normalizeQuery(q)` (trim/lower for the query key)
- [X] T010 [P] [US4] Create apps/web/src/hooks/useSearch.ts: `["search", normalizedQuery]` query via the typed client (`GET /api/search`), `enabled` only for a non-empty query, `placeholderData: keepPreviousData`, `staleTime` 5 s, FR-049 error mapping + `logError`; export `invalidateSearch(queryClient)` and wire it into apps/web/src/hooks/useMembershipMutations.ts leave/remove/unshare settle paths (D10); failing unit spec first in apps/web/tests/unit/use-search.test.ts (key shape, disabled on empty, invalidation helper)
- [X] T011 [US4] Create apps/web/src/components/search/CommandPalette.tsx (+ CommandPalette.module.css, tokens-only) per contracts/ui-palette.md: catalog `Dialog` „Szukaj i wykonaj", `role="combobox"` input (aria-expanded/controls/activedescendant), `role="listbox"` with `role="group"` sections and `role="option"` rows (task: title + location chip + due chip + „zrobione"; project: icon + name + „(zarchiwizowany)" + inline „Przywróć" via `useProjectMutations().unarchiveProject`; label: color dot + name; action: icon + label, NO shortcut column), `↑/↓` + Enter + click activation, Esc/overlay close, `EmptyState` „Brak wyników dla „<q>"", error row with „Spróbuj ponownie", polite LiveRegion „N wyników"/„Brak wyników"; `onNavigate(href)`, `onAction(id)`; failing unit spec first in apps/web/tests/unit/command-palette.test.tsx (groups order, arrow navigation + activedescendant, Enter activates, empty-query actions only, archived row + Przywróć, empty/error states, label row replaces the query)
- [X] T012 [US4] Opener + action wiring in apps/web/src/components/layout/Topbar.tsx: replace the disabled placeholder with the „Szukaj…" `<button aria-label="Szukaj" aria-haspopup="dialog">` (same tokens/layout), host `CommandPalette`, `GlobalCaptureDialog` (already) and `ProjectForm mode="create"` so the „Nowy task"/„Nowy projekt" actions open their dialogs AFTER the palette closes (focus hand-over), `router.push` for routes and task/project results; apps/web/src/components/cycles/CycleView.tsx opens its create dialog when `?nowy=1` is present and strips the flag; failing unit spec first in apps/web/tests/unit/topbar-search.test.tsx (button semantics, opens the dialog, focus returns to the button on Esc)
- [ ] T013 [US4] Author the E2E journey in apps/web/tests/e2e/search.spec.ts (`[INV-###]` tags, UI-driven, unique user per test × attempt): seed tasks (one with „mleko" only in the description), a project, an archived project, a label → topbar „Szukaj…" → palette with actions (AS-01) → type „mleko" → both tasks under Zadania (AS-02) → arrow + Enter → Inbox with the drawer open on that task (AS-03) → reopen, type „create project" → „Nowy projekt" → Enter → the project form (AS-04) → type the project name → Enter → project view (AS-05) → search the archived project → „(zarchiwizowany)" + „Przywróć" → click → restored (US-10.AS-06) → Esc returns focus to the opener (AS-06) → second user (non-member) searches the first user's shared task title → „Brak wyników" (access scoping, [E] fact)
- [ ] T014 [P] [US4] Add the palette (empty query = actions; populated query over seeded tasks/project/label) to the axe ×4-palette walk in apps/web/tests/e2e/axe.spec.ts — zero WCAG 2.1 AA violations; pointer parked before the audit
- [ ] T015 [P] [US4] Add [V] screens to apps/web/tests/e2e/visual.spec.ts (linux-gated as today): palette-empty and palette-populated (deterministic dateless seeds, query „za") × 4 palettes × 3 widths; baselines generate in T022 (the opener change also re-bakes the 5 base screens — expected)

## Phase 4: User Story 8 — Keyboard Navigation & Shortcuts, AS-08 (Priority: P1)

**Goal**: Every list view offers a visible „Filtruj…" input that narrows its loaded rows instantly, with a clear button and an empty-filter hint.

**Independent Test**: quickstart.md manual walk step 7.

- [X] T016 [P] [US8] Create apps/web/src/lib/filter.ts (pure — unit-tested first in apps/web/tests/unit/filter.test.ts): `normalizeForFilter(text)` (lowercase + NFD diacritic strip), `filterTasks(rows, q)` (token-AND over title + description; empty `q` → rows unchanged; stable order), facts: „sro" matches „Środa", „kup mleko" vs „Kup mleko", case-insensitive, description hit
- [X] T017 [P] [US8] Create apps/web/src/components/search/ViewFilterInput.tsx (+ .module.css): labelled input („Filtruj zadania w tym widoku", placeholder „Filtruj…", Lucide `Filter`) + `IconButton` „Wyczyść filtr" when non-empty; controlled `{ value, onChange }`; failing unit spec first in apps/web/tests/unit/view-filter-input.test.tsx
- [X] T018 [US8] Host the filter + apply `filterTasks` before grouping in: apps/web/src/app/(app)/page.tsx (Inbox — filtered rows feed the TaskList and selection), apps/web/src/components/tasks/DailyView.tsx (groups re-derived from filtered tasks; Dziś + Nadchodzące), apps/web/src/components/tasks/AssignedView.tsx, apps/web/src/app/(app)/projects/[id]/page.tsx (one input above `ViewModeSwitch`, applied to both GroupedTaskList and BoardView), apps/web/src/components/cycles/CycleView.tsx; render the „Brak zadań pasujących do filtru" hint with „Wyczyść filtr" when the filter empties a non-empty view (FR-110); failing unit specs first for the Inbox and project page hosts in apps/web/tests/unit/
- [ ] T019 [US8] Extend apps/web/tests/e2e/search.spec.ts with the filter facts: Inbox „sro" keeps only „Środa…" rows and the hint appears for „zzz"; clear restores; project Board columns collapse under a filter; Dziś filter; plus extend apps/web/tests/e2e/shortcut-removal.spec.ts with one fact that `/` and `Control+k` do nothing (no palette, no focus move — FR-027 stays deferred)

## Phase 5: Polish & Cross-Cutting Concerns

- [ ] T020 Run `pnpm --dir apps/web test` and drive the inventory-coverage gate green — every slice-013 `INV-###` row has an `[INV-###]`-tagged covering test and vice versa; reconcile as needed
- [ ] T021 [P] `pnpm --dir apps/web audit:dead` (zero dead modules) + hex audit clean (tokens-only in the new .module.css) + `pnpm --dir apps/web lint` + `typecheck`
- [ ] T022 Regenerate [V] baselines via apps/web/tests/e2e/update-visual-baselines.ps1 (docker; prereqs per the runbook: Debug API build, port 55432 free) — new palette baselines + the re-baked base screens committed for human approval per palette; unrelated baselines must NOT drift
- [ ] T023 Run the full suites per quickstart.md: web unit, `dotnet test` shard-by-shard (verify the new `search` shard partitions correctly — D13), full `pnpm --dir apps/web e2e` (incl. axe + shortcut-removal + tasks/daily/board/cycles/recurrence regression), `gen:api` diff clean
- [ ] T024 Walk quickstart.md manual steps 1–8 against the interactive stack and record the outcome in this file

## Dependencies & Execution Order

### Phase Dependencies

Setup (T001) → Foundational T002–T008 (server; blocks Phase 3) → US4 T009–T015 → US8 T016–T019 (client-only; may interleave with Phase 2) → Polish T020–T024.

### Within-Phase Notes

- T002–T003 (red suites) land BEFORE T004–T006 turn them green; T007 anytime before T008's full `dotnet test`; T008 last in phase 2.
- T009/T010 unblock T011; T012 needs T011; T013 needs T012; T014/T015 after T012.
- T016/T017 unblock T018; T019 needs T018.
- T022 after every pixel-affecting task; T023/T024 last.

### Parallel Opportunities

```text
# Phase 2 kickoff (after T001):
T002 ‖ T003                 # two red integration suites, separate files
# After T005: T006 ‖ T007
# Phase 3/4 kickoff (after T008; T016/T017 even earlier):
T009 ‖ T010 ‖ T016 ‖ T017
# After T012: T013 ‖ T014 ‖ T015 ; after T018: T019
```

## Implementation Strategy

### MVP = US-04 first

The palette (T011–T012) over the server endpoint is the first demoable increment; the view filter (Phase 4) is independent client-only work.

### Suggested local commit boundaries

`docs(013)` inventory rows (T001) → `test(013)` red suites (T002–T003) → `feat(013)` search query + repositories + endpoint (T004–T006) + `chore(013)` shard (T007) → `test(013)` green + gen:api (T008) → `feat(013)` web libs + hook (T009–T010) → `feat(013)` palette + opener (T011–T012) → `test(013)` E2E/axe/[V] (T013–T015) → `feat(013)` filter lib/component/hosts (T016–T018) → `test(013)` filter E2E + binding audit (T019) → polish commits (T020–T024).

# Research — Slice 013 (Command Palette & Search)

Phase-0 decisions. Spec-level ambiguities were resolved in the Clarifications session
(spec.md, 2026-11-08 — owner decision: visible-UI triggers only) and are referenced, not
re-decided. Code facts verified on branch `013-command-palette-search` (post-012 main).

## D1 — One server search endpoint, three access-scoped buckets; actions stay client-side

**Decision**: `GET /api/search?q=<text>` → `SearchResponse { tasks: TaskResponse[], projects:
ProjectResponse[], labels: LabelResponse[] }`, each bucket capped (tasks 20, projects 10, labels
10) and already ranked (D3). The **actions** bucket (fixed catalogue, Clarifications) is a
client-side constant — it carries no data, so it never crosses the API.

**Rationale**: one round-trip per keystroke keeps US-04.AS-02/AS-07 inside the 50 ms budget;
the buckets reuse the three existing read models, so the typed client needs no new entity
(the spec introduces none). Server-side scoping is where FR-065–068 are ENFORCED (spec IX).

**Alternatives**: three endpoints — rejected (three in-flight requests per keystroke, three
loading states); a client-side index over the loaded caches — rejected by Principle V and by
the spec's explicit "server search endpoint".

## D2 — Matching: case-insensitive, token-AND, substring (`ILIKE`), no extension

**Decision**: the query is trimmed, split on whitespace into ≤ 5 tokens (each ≤ 100 chars;
LIKE metacharacters escaped — the `UserRepository.FindByEmail` precedent); a candidate matches
when EVERY token is an `ILIKE '%token%'` match on the candidate's text: task → `title` OR
`description`; project → `name`; label → `name`. Empty/whitespace query → empty buckets (the
palette shows the action catalogue only). Diacritics are NOT folded server-side (PostgreSQL
`unaccent` is an extension; `ILIKE` already covers case — „Mleko"/„mleko").

**Rationale**: FR-032's "fuzzy" is satisfied at the product level by substring + ranking
(D3) over ≤ 10k rows per user: an `ILIKE` scan over the caller's access-scoped rows is well
inside 50 ms at ASM-10 scale (slice-002 EC-06 characterization); token-AND lets „mleko kup"
find „Kup mleko". No new PostgreSQL extension → no superuser/migration risk.

**Alternatives**: `pg_trgm` similarity + GIN index — real fuzziness (typos) but needs
`CREATE EXTENSION` in a migration and tuning of a similarity threshold; parked as the first
follow-up if SC-009 is ever missed. Client-side fuzzy over caches — rejected (D1).

## D3 — Ranking (server), fixed bucket order (client)

**Decision**: within a bucket the server orders: exact title/name (case-insensitive) → prefix
match → word-start match → substring; ties by `updated_at DESC` (tasks) / `name` (projects,
labels). Done/cancelled tasks sort after open ones. Buckets render in a fixed order: **Akcje →
Zadania → Projekty → Etykiety** (actions first because they are the only bucket present for an
empty query).

## D4 — Access scoping = the existing readable predicates, resolved once per request

**Decision**: tasks — `BuildReadableVisibilityPredicate(caller, sharedProjectIds)` (the
Today/Upcoming predicate: `created_by = caller OR project_id IN <member projects>`, as an
OR-of-equalities over the value-converted nullable FK — the slice-005 lesson), excluding
soft-deleted rows; projects — owned (`ListOwnedAsync`, both archive states) ∪ member projects
(`ListByIdsAsync` over `members.ListProjectIdsForUserAsync`, both archive states); labels —
`ListForOwnerAsync(caller)` (labels are per-user, slice 006). The member-project id set is
resolved ONCE per request and shared by the task and project buckets.

**Rationale**: verbatim FR-065/066/067 dispatch — ownership for personal items, current
membership for shared ones (viewer suffices: search is a read, FR-067). A former member's
project ids are simply absent from the set, so its tasks/projects can never match (FR-066).
The spec's "cached per session" accessible-project set is unnecessary at this scale: one
indexed membership lookup per request stays far inside the budget.

## D5 — Archived projects are results, with an inline unarchive (US-10.AS-06)

**Decision**: project results include archived projects (`archivedAt != null`) rendered with
the suffix „(zarchiwizowany)" and an inline „Przywróć" button that calls the existing
`unarchiveProject` mutation (`useProjectMutations`, the sidebar's AS-11 row precedent);
selecting the result itself navigates to `/projects/{id}`. Tasks of an archived project ARE
searchable (the project is still readable; the EC-12 posture of slice 011).

## D6 — Task result navigation = the task's view + the details drawer

**Decision**: a task result navigates to its location with the drawer open:
`/?task=<id>` for an unprojected task, `/projects/<projectId>?task=<id>` for a projected one
(the slice-019 `TaskDrawerHost` reads `?task=`; the List selects the row). The palette closes
first so focus lands in the drawer (FR-101: the drawer host owns focus on open).

## D7 — The palette is a catalog Dialog with combobox semantics

**Decision**: `CommandPalette` (components/search/) renders in the catalog `Dialog` (019 focus
contract: initial focus in the input, trap, Esc, focus return to the invoker). The input is a
`role="combobox"` with `aria-expanded`, `aria-controls` → `role="listbox"` of `role="option"`
rows grouped under `role="group"` headings (Akcje / Zadania / Projekty / Etykiety), `↑/↓` move
`aria-activedescendant`, Enter activates, Esc closes; every option is also a real click target
(FR-103/FR-046). The result list is announced through the existing polite `LiveRegion`
(„N wyników") as it changes — never by stealing focus (FR-101). No-results state uses
`EmptyState` („Brak wyników dla „…"" + hint „Spróbuj innego słowa"; FR-110).

## D8 — The opener: the topbar search box becomes a real control (no `Ctrl+K`)

**Decision**: the slice-019 placeholder (`role="searchbox" aria-disabled`) is replaced by a
`<button>` styled as the search field (icon + „Szukaj…" text, `aria-label="Szukaj"`,
`aria-haspopup="dialog"`), in the tab order on every view, opening the palette. No keyboard
binding is registered (Clarifications; FR-027 deferred, OOS-20); the binding-audit spec
(`shortcut-removal.spec.ts`) stays untouched.

## D9 — Client search state: TanStack Query keyed by the normalized query, no debounce

**Decision**: `useSearch(q)` → `["search", q.trim().toLowerCase()]`, `enabled: q.trim() !==
""`, `placeholderData: keepPreviousData` (the previous result set stays visible while the next
one loads — no flicker/skeleton between keystrokes, Principle IV), `staleTime` 5 s. No input
debounce (SC-003/50 ms posture); TanStack dedupes identical keys and the latest key wins. On
a 401/403/5xx the palette shows the FR-049 copy with a „Spróbuj ponownie" action and logs via
`logError` (FR-050).

## D10 — Access-loss invalidation (FR-095 / spec XI)

**Decision**: the `["search"]` key family is invalidated wherever the membership/sharing
mutations already invalidate projects/tasks (`useMembershipMutations` leave/remove/unshare and
the account-deletion path) and when the palette opens (`refetchOnMount`), so a now-inaccessible
item never surfaces from a stale cache. Remote (SignalR) revocation stays with slice 016.

## D11 — The view filter is a client-side narrowing of the loaded rows

**Decision** (Clarifications): `ViewFilterInput` (components/search/) — a labelled text input
„Filtruj…" with a clear button — hosted in the header of Inbox, Dziś/Nadchodzące
(`DailyView`), Przypisane (`AssignedView`), the project page (applies to List AND Board) and
`/cycle` (`CycleView`). `lib/filter.ts#filterTasks(rows, q)` keeps rows whose title (or
description) contains every token, case- and diacritic-insensitively (NFD strip — client-only,
cheap); group builders (status/priority/cycle, daily groups, board columns) run on the
filtered rows so empty groups/columns collapse as today. The filter is per-view component
state (not persisted, not in the URL); clearing restores the full list. „Brak pasujących
zadań" hint when the filter empties a non-empty view (FR-110).

**Rationale**: the rows are already access-scoped by the view's query; narrowing them locally
is instant and needs no endpoint. Selection/keyboard navigation operate on the filtered array.

## D12 — Action catalogue (fixed, client-side)

| id | label (match text incl. aliases) | executes |
|---|---|---|
| `new-task` | „Nowy task" / new task, create task | opens `GlobalCaptureDialog` |
| `new-project` | „Nowy projekt" / new project, create project | opens `ProjectForm mode="create"` |
| `new-cycle` | „Nowy cykl" / new cycle | navigates to `/cycle?nowy=1` — `CycleView` opens its create dialog on that flag |
| `go-inbox` … `go-settings` | „Przejdź: Inbox / Dziś / Nadchodzące / Przypisane / Cykl / Ustawienia" | `router.push` |

Each row shows the label only — no shortcut column (FR-033 deferred). Matching: token-AND
over label + aliases, same rule as D2 but client-side.

## D13 — OpenAPI, errors, shard

**Decision**: new operationId `search` on `GET /api/search` (query param `q`, max 200 chars →
`422 validation_failed` beyond; 401 under the gate); no new error code; `SearchResponse`
schema composed of the three existing response schemas. Integration tests in NEW namespace
`TaskFlow.IntegrationTests.Search` → new ci shard `search` (`FullyQualifiedName~IntegrationTests.Search`)
+ the `identity-labels-infra` complement gains `&FullyQualifiedName!~IntegrationTests.Search`.

## D14 — No migration, no new entity

**Decision**: no schema change (FR-051 posture untouched). If SC-009 is missed in the
characterization fact, the follow-up is the D2 alternative (`pg_trgm` GIN index), not a
different architecture.

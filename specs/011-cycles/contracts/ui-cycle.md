# Contract: Cycle UI — sidebar, Cycle view, picker, by-cycle grouping (slice 011)

UI-side contract (the 010 `ui-board-list.md` analogue). Polish copy only (019 D15);
tokens-only styling (FR-104, hex-audit); Lucide icons; every operation behind a visible
affordance (FR-103 — `#`/`G C` stay deferred, OOS-20); WCAG AA posture of 019/010 applies
(focus, contrast 4.5:1, no hover-only, reduced-motion, dialog focus contract, LiveRegion).
List rows follow the post-010 remediation: NO widget role may contain the rows' focusable
controls (grid/row/gridcell on list surfaces; list/listitem on cards — never listbox/option).

## Sidebar (FR-017)

- New entry **„Cykl"** between the daily views and PROJEKTY (Lucide `RefreshCw` or
  `IterationCw`), navigating to `/cycle`.
- With an active cycle: the entry shows the cycle's NAME (sanitized text). Overdue active
  cycle (end date past in Europe/Warsaw): a text badge **„po terminie"** (never color-only,
  FR-044).
- No active cycle: the entry reads „Cykl" and still navigates (the view's empty/planned state
  handles the rest). The entry is always present — FR-017's "visible" is unconditional.

## Cycle view — `/cycle` (single management surface; Clarifications)

- **Switcher**: all cycles ordered by `(startDate, createdAt, id)` with status suffix
  („aktywny" / „planowany" / „zamknięty"). Default selection: active → else next planned →
  else empty state (FR-110: hint + „Nowy cykl" action).
- **Metrics strip (FR-026 / US-05.AS-03)**: „X% ukończone" (done/total), „N dni pozostało"
  (Europe/Warsaw; overdue active: „0 dni (po terminie)"), and the per-status breakdown as
  labelled text counts (Backlog / Do zrobienia / W toku / Zrobione / Anulowane). Numbers are
  TEAM-WIDE (D10); zero-task cycle shows 0% + hint.
- **Task list**: caller-visible rows (`GET /api/cycles/{id}/tasks`) rendered with the standard
  row catalog (grid pattern); rows show the „przeniesione" (carried-over) text chip when
  `carriedOver` (visible, not color-only). EC-12: archived-project tasks appear here.
- **Lifecycle actions** (FR-020) as visible controls: „Nowy cykl" (always), „Edytuj",
  „Aktywuj" (planned only), „Zamknij cykl" (active only), „Usuń" (planned/closed only —
  destructive). Every dialog follows the 019 dialog focus contract.
- **Create/edit dialog**: name (pre-fill „Cykl N", editable), start/end date-only inputs
  (pre-fill: start = today Warsaw, end = start + the /settings duration preference — D8/D18);
  inline validation: name required, start < end. Server errors surface via FR-049 copy.
- **Close flow = the review (US-05.AS-04/05/06; Clarifications)**: „Zamknij cykl" opens a
  dialog listing the caller-visible INCOMPLETE tasks with a bulk choice —
  „Przenieś wszystkie do następnego cyklu" / „Przenieś wszystkie do backlogu" /
  „Zostaw w zamkniętym cyklu (oznacz „przeniesione")" — plus optional per-task overrides
  („obsłuż pojedynczo": a per-row select among the same three). Footer states that the bulk
  choice also applies to tasks of other users. Confirm commits close+rollover atomically;
  `no_next_cycle` renders the „Najpierw utwórz nowy cykl" prompt with a „Nowy cykl" action
  (US-05.AS-06). Result toast + polite announcement carries the returned counts.
- **Delete guards (US-05.AS-07 / EC-04 / FR-019/020)**: active → the „Usuń" item is OMITTED
  (never disabled — 010 boundary convention) and the server 422 is still mapped; non-empty
  planned/closed → FR-049 message „Cykl zawiera zadania…".
- **Overdue prompt**: an active cycle past its end date renders a banner „Cykl dobiegł końca —
  zamknij go" with the „Zamknij cykl" action (close stays manual).

## Task „⋯" menu + CyclePicker (US-05.AS-01/02)

- `buildMenuItems` gains **„Cykl…"** (between „Etykiety…" and „Przenieś do projektu…"), wired
  via `TaskRowActions.onOpenCycle` on every surface that wires labels today (Inbox, daily
  views, project List, Board card — the shared-menu contract keeps them in sync).
- `CyclePicker` dialog (PriorityPicker pattern): options = ALL cycles (D5 order, status
  suffix) + **„Bez cyklu"**; current assignment checked; selection issues the optimistic
  `setTaskCycle` and closes; Esc cancels, focus returns to the invoker.

## By-cycle grouping (FR-024 completion; US-03.AS-07 deferred dimension)

- `GroupByControl` gains the fourth toggle **„Cykl"** (same `aria-pressed` strip); persisted
  value `"cycle"` in the existing `taskflow.project-groupby.<projectId>` key.
- `buildProjectGroups(tasks, "cycle", cycles)`: groups ordered per D5, labelled by cycle name,
  **„Bez cyklu" LAST** (EC-10 naming — never „Backlog"); empty groups omitted (010 rule).

## /settings (FR-015 — D8)

- New labelled number field **„Domyślna długość cyklu (dni)"** (1..90) under the profile
  section, persisted via `PATCH /api/users/me/preferences`; save announces politely.

## Inventory & tests (D11-convention of 019)

Every behavior above gets an `INV-###` row in `specs/019-ui-design-system/feature-inventory.md`
(new slice-011 section) BEFORE its covering test; `[INV-###]` tags in titles. Coverage:
[C] cycle group builder, D5 ordering/tiebreaker, days-remaining math (Warsaw + DST across a
2-week span), menu builder with onOpenCycle, picker option building;
[E] cycle lifecycle journey (create → assign via menu → activate → metrics → overdue banner →
close with rollover → AS-06 prompt → delete guards), by-cycle grouping walk, sidebar entry
states, settings preference roundtrip;
[A] `/cycle` view (seeded, incl. picker + close dialog open) joins the axe ×4-palette walk;
[V] `/cycle` screens (empty + seeded × 4 palettes × 3 widths) join visual.spec with the
deterministic-seed rules (unique user per test × attempt). Date determinism: the seeded cycle's
dates are RELATIVE to the run day (start = today−7, end = today+7 in Warsaw) so
„7 dni pozostało" is a constant; the absolute date-range text (which would drift daily) is
hidden at screenshot time via the existing `visual.hide-dev-overlay.css` stylePath (the
dev-badge precedent) — pixels stay day-independent. Regeneration via the docker script.

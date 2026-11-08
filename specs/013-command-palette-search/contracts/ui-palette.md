# Contract: Command Palette & View Filter UI (slice 013)

UI-side contract (the 011 `ui-cycle.md` analogue). Polish copy only; tokens-only styling
(hex-audit); Lucide icons; every operation behind a visible affordance (Principle I — NO
`Ctrl+K`/`/` bindings, OOS-20; the binding-audit spec stays green); WCAG AA posture of
019/010/011/012 applies (focus, contrast 4.5:1, no hover-only, reduced-motion, dialog focus
contract, LiveRegion). List rows keep the post-010 grid/row/gridcell remediation.

## Opener — the topbar search control (D8)

- The slice-019 disabled placeholder becomes a real `<button>` in the tab order on EVERY
  signed-in view: Lucide `Search` icon + text „Szukaj…", `aria-label="Szukaj"`,
  `aria-haspopup="dialog"`. Click / Enter / Space opens the palette. Visually identical to the
  placeholder (same tokens), so existing [V] baselines do not drift except for the removed
  disabled styling (regenerate anyway — T0xx).

## Command palette dialog (US-04; D7)

- Catalog `Dialog` titled „Szukaj i wykonaj" (`aria-labelledby`), initial focus in the input,
  focus trapped, Esc closes and focus returns to the opener (US-04.AS-06), overlay click closes.
- Input: `role="combobox"`, `aria-label="Szukaj zadań, projektów, etykiet i akcji"`,
  placeholder „Wpisz, czego szukasz…", `aria-expanded`, `aria-controls` the listbox,
  `aria-activedescendant` follows `↑/↓`; Enter activates the active option; typing never loses
  focus.
- Results: `role="listbox"` with `role="group"` sections in fixed order **Akcje → Zadania →
  Projekty → Etykiety** (`aria-labelledby` on each group heading); sections with no rows are
  omitted. Each `role="option"` is a visible click target:
  - task → title (sanitized text), location chip („Inbox" / project name), due chip when due,
    „zrobione" marker when done; Enter/click → `/?task=<id>` or `/projects/<pid>?task=<id>`
    (US-04.AS-03) and the palette closes.
  - project → icon + name; archived → suffix „(zarchiwizowany)" + an inline „Przywróć" button
    (calls `unarchiveProject`; the row stays, suffix disappears on settle — US-10.AS-06);
    Enter/click → `/projects/<id>` (US-04.AS-05).
  - label → color dot + name; Enter/click → replaces the query with the label name (searches
    tasks by that text — labels have no page of their own; data-model.md).
  - action → Lucide icon + label; no shortcut column (FR-033 deferred). Enter/click executes
    (D12) and closes the palette; „Nowy task"/„Nowy projekt" open their dialogs AFTER the
    palette closes so the focus contract hands over cleanly.
- Empty query → only the Akcje group (the full catalogue). Non-empty query with zero rows in
  every bucket → `EmptyState` hint „Brak wyników dla „<q>"" + „Spróbuj innego słowa".
- Loading: previous results stay (keepPreviousData); a thin „Szukam…" status line (polite)
  replaces nothing. Error: FR-049 copy („Nie udało się wyszukać — spróbuj ponownie") with a
  „Spróbuj ponownie" button; `logError` (FR-050).
- LiveRegion (polite): „N wyników" / „Brak wyników" after each settled fetch (FR-101).
- All strings user-authored (titles, descriptions, names) render as text (FR-099).

## View filter (US-08.AS-08; D11)

- `ViewFilterInput`: labelled input „Filtruj" (`aria-label="Filtruj zadania w tym widoku"`,
  placeholder „Filtruj…", Lucide `Filter` icon) with an `IconButton` „Wyczyść filtr" when
  non-empty. Hosted in the header of: Inbox, Dziś, Nadchodzące, Przypisane, project page (List
  and Board share one input), `/cycle`.
- Matching: every whitespace token must occur in the task title (or description),
  case- and diacritic-insensitively („sr" matches „Środa"). Empty filter = full view.
- The filtered rows feed the view's existing grouping/columns; a group/column with no rows
  collapses exactly as it does for an empty dataset; when the filter empties a NON-empty view, a
  hint „Brak zadań pasujących do filtru" with „Wyczyść filtr" renders (FR-110). Selection moves
  to the first filtered row.
- Not persisted (component state); typing in it is ordinary text entry (FR-031 dormant).

## Inventory rows (019 gate)

New section **„P. Command palette & search (slice 013)"** in
`specs/019-ui-design-system/feature-inventory.md` (INV-188 onward): opener present/tab-order
everywhere, dialog focus contract, combobox/listbox semantics + arrow navigation, action
catalogue for an empty query, task/project/label result rendering and navigation, archived
suffix + „Przywróć", empty/error states, LiveRegion count, view filter on each host, filter
diacritics + empty hint, access scoping ([C] through the integration matrix) — each row:
screen → Given/When/Then → outcome → realized FR/AS → covering level [C]/[E]/[A]/[V].

## Determinism notes for [V] and E2E

- [V] seeds deterministic titles; the palette-open screen seeds three tasks, one project, one
  label and types „za" so every bucket renders; no dates in the result rows (dateless seeds).
- E2E uses the real BFF→API path; unique user per test × attempt; the 10k characterization
  lives in the integration suite, not E2E.

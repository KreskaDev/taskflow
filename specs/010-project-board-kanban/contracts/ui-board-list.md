# Contract: Project view UI — Board & groupable List (slice 010)

UI-side contract (the analogue of 019's `ui-theme.md`): what the project view MUST render and
how it MUST behave. Copy in Polish (019 D15); tokens-only styling (FR-104, hex-audit); Lucide
icons only (FR-105).

## Mode switch (US-03.AS-02, FR-103)

- Project view header gains a visible segmented control: **„Lista" | „Tablica"**
  (Lucide `List` / `LayoutGrid`), keyboard-operable (Tab + arrows/Enter per segmented-control
  ARIA pattern or two toggle buttons with `aria-pressed`).
- Last-used mode per project persists in `localStorage["taskflow.project-view.<projectId>"]`;
  first visit defaults to `list`. Stored value re-applies after mount (D8).

## Board (US-03.AS-03..06, FR-025, EC-11)

- Exactly four columns in order: **Backlog, Do zrobienia (todo), W toku (in_progress),
  Zrobione (done)** — from the single `BOARD_COLUMNS` map (`lib/board.ts`); `cancelled` never
  renders a column or card.
- Column: heading with label + live count („N zadań"), `role="listbox"`
  `aria-label="<label>, N zadań"`; empty column renders the catalog `EmptyState` (hint +
  action per FR-110 — the action is the inline add / global add path).
- Card: title (sanitized text — FR-099 posture unchanged: no raw HTML render path), optional
  due chip, priority chip, label chips, assignee avatars — catalog components only. Card
  exposes on hover/focus the quick-action zone and the full „⋯" menu (FR-108) built by the
  shared `buildMenuItems`, extended with:
  - „Przenieś w lewo" / „Przenieś w prawo" — mapped via `adjacentStatus`; the item is
    **omitted** at a boundary (Done has no right move — US-03.AS-05; Backlog no left).
- Drag-and-drop (dnd-kit): pointer (4px activation) + KeyboardSensor; drop on another column →
  optimistic status move (one PATCH, D6); DragOverlay in a portal at `var(--z-menu)`; Polish
  dnd announcements; drop animation disabled under `prefers-reduced-motion` (FR-047).
- Viewer role (shared project): board renders read-only — no drag activation, no move items in
  the menu (same posture as existing viewer-mode rows); server still enforces (403).
- Failed move: rollback + toast with retry (FR-049) via the established error surface; logged
  with structured context (FR-050, `logError`).

## Groupable List (US-03.AS-07, FR-024)

- The existing List gains a visible group-by control: **„Grupuj: Brak | Status | Priorytet"**
  (by-cycle appears only in slice 011). Persisted per project
  (`localStorage["taskflow.project-groupby.<projectId>"]`, default none).
- Grouped render reuses the DailyView grouped-listbox pattern: one `role="listbox"`,
  `role="group" aria-label` per group, flat index across groups, virtualization preserved or
  consciously bypassed exactly as DailyView does today.
- Status grouping: column order + „Anulowane" group last (cancelled visible HERE — EC-11
  counterpart); priority grouping: P0→P3, then „Bez priorytetu". Empty groups omitted.

## Inventory & tests (D11)

Every behavior above gets an `INV-###` row in `specs/019-ui-design-system/feature-inventory.md`
(new slice-010 section) BEFORE its covering test; tags `[INV-###]` in test titles. Coverage
levels: [C] `lib/board.ts` + group builders + persistence hook + menu builder; [E] board
journey (drag + menu move + boundary + mode persistence across reload + viewer read-only +
cancelled hidden); [A] axe on the Board view ×4 palettes joins `axe.spec.ts`.

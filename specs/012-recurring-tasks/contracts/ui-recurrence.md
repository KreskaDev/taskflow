# Contract: Recurrence UI — editor/drawer field, row indicator, completion feedback (slice 012)

UI-side contract (the 011 `ui-cycle.md` analogue). Polish copy only; tokens-only styling
(hex-audit); Lucide icons; every operation behind a visible affordance (Principle I — no
custom shortcuts, OOS-20); WCAG AA posture of 019/010/011 applies (focus, contrast 4.5:1, no
hover-only, reduced-motion, dialog focus contract, LiveRegion). List rows keep the post-010
grid/row/gridcell remediation.

## `RecurrenceField` (shared; D10) — hosted by `TaskEditor` AND `TaskDrawer`

- Labelled control **„Powtarzanie"** (a `<select>` in a `fieldRow` like „Priorytet"):
  „Nie powtarza się" (default) / „Codziennie" / „Co N dni" / „W wybrane dni tygodnia" /
  „Co miesiąc".
- Per-type inputs appear under the select, each labelled:
  - „Co N dni" → number input **„Co ile dni"** (2..365, default 2);
  - „W wybrane dni tygodnia" → seven labelled checkboxes **Pn Wt Śr Cz Pt So Nd** (ISO order;
    at least one required — inline message „Wybierz co najmniej jeden dzień" blocks save);
  - „Co miesiąc" → read-only text „co miesiąc, N. dnia" where N = the due date's Warsaw
    day-of-month at the moment the type is chosen (becomes `dayOfMonth`); an existing rule
    shows its stored anchor.
- Preview line **„Następne wystąpienie: dd.MM.yyyy"** (timed dues add ` HH:mm`) computed by
  `lib/recurrence.ts#nextOccurrence` from the task's CURRENT due (client mirror; the server is
  authoritative — D3/D13).
- **No due date** → the select is disabled with the visible hint **„Ustaw najpierw termin"**
  (recurrence requires a due date; the editor cannot set a due — that is the `T`/drawer
  reschedule).
- **Due-date guard (Clarifications Q4)** — in `TaskEditor`, checking „Usuń termin" while the
  rule is not „Nie powtarza się" shows the inline message **„Zadanie cykliczne musi mieć
  termin — wyłącz powtarzanie albo ustaw termin"** and disables „Zapisz"; in `TaskDrawer`,
  clearing the „Termin" input of a recurring task shows the same message and does not send
  the reschedule. The server `422 recurrence_requires_due_date` maps to the same copy (FR-049).
- Save paths: `TaskEditor` → `editTask` whole-object (`Ctrl+Enter`, includes `recurrenceRule`);
  `TaskDrawer` → `editTask` whole-object on change (same mutation; other fields passed through
  unchanged). Single-key shortcuts stay suppressed while any field is focused (FR-031).

## Row indicator (US-06.AS-01 — D12)

- `TaskRow` (Inbox, Today/Upcoming, project List, `/cycle`, Assigned) and `BoardCard` render a
  text chip **with the Lucide `Repeat` icon + `recurrenceLabel(rule)`** when `recurrenceRule`
  is present: „codziennie" / „co N dni" / weekday abbreviations joined by „, " („pn, śr") /
  „co miesiąc, N.". The icon is decorative (`aria-hidden`); the text carries the meaning
  (FR-044/FR-046 — never icon- or color-only).
- Chip order within the row: after the due chip, before the „przeniesione" chip.

## Completion feedback (D13 / FR-101)

- Completing a recurring instance whose due day is today or earlier (Warsaw): on the mutation's
  settle the lists refetch (successor row appears, adjacent after the completed row where the
  view shows it) and a toast **„Utworzono kolejne wystąpienie"** is shown + announced politely.
- Completing EARLY (due day in the future): no toast, no row — the server schedules the spawn
  (AS-04/EC-05); the UI says nothing extra (the completed row's existing feedback suffices).
- Reopening a completed recurring instance: the lists refetch; the removed successor
  disappears. No extra copy.
- Errors on the status write follow the existing FR-049 toast + rollback.

## Inventory rows (019 gate)

New section **„O. Recurring tasks (slice 012)"** in `specs/019-ui-design-system/feature-inventory.md`
(INV-176 onward): field states (disabled hint, each type's inputs, preview), guard messages
(editor + drawer), row chip on List/daily/Board, completion toast, early-completion silence,
reopen removal, cancelled no-spawn — each row screen → Given/When/Then → outcome → realized
FR/AS → covering level [C]/[E]/[A]/[V].

## Determinism notes for [V] and E2E

- [V] seeds use RELATIVE dates (due = today Warsaw) so the preview line is constant per
  capture day — the absolute date text is hidden via `visual.hide-dev-overlay.css`
  (the 011 convention); the chip text („codziennie", „co 3 dni") is date-free.
- E2E sets due dates through the API helper relative to today (Warsaw) and completes rows
  through the UI; the late-completion fact seeds a past due.

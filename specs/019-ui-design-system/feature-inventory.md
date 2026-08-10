# Feature Inventory — regression grid for slices 001–009 (slice 019, US2/T019)

**Date audited**: 2026-08-10 | **App state**: working tree at `bcf4c58` (pre-redesign), audited on the
RUNNING dev stack (PG :5432, API :4311, BFF :3000) with seeded owner/viewer accounts, cross-checked
against the 2026-08-09 survey in `research.md`, all 10 Playwright specs (62 tests) and all 25 Vitest
specs.

**Contract (S2.1–S2.4, D10)**: every user-visible shipped feature of slices 001–009 is a stable
`INV-###` row below. Each row MUST be covered by at least one automated test whose title carries the
`[INV-###]` tag; `apps/web/tests/unit/inventory-coverage.test.ts` parses this file and FAILS on any
uncovered row. Rows are behavior-first: where the pre-redesign trigger was a single-key shortcut,
the row states the BEHAVIOR and the post-redesign visible-control trigger (FR-111 removes the key,
not the capability — S2.3's sole exception is the shortcut system itself, inventoried in §K).

**Covering-test levels**: [C] Vitest component/unit · [E] Playwright E2E · [A] axe/keyboard a11y.

---

## A. Authentication, admission & session (slice 001/002)

| ID | Given / When / Then | Realizes | Level |
|---|---|---|---|
| INV-001 | Given an anonymous visitor on any protected route (`/`, `/today`, `/settings`, …), When the page loads, Then they are redirected to `/signin` which shows the TaskFlow brand heading and a "Zaloguj się przez Google" (Sign in with Google) affordance. | US-01 AS-03 | [E] |
| INV-002 | Given an admitted, email-verified Google account, When the user completes sign-in, Then an account is created (or matched) and they land in the workspace; Settings shows their Google display name and email. | US-01 AS-01/AS-04 | [E] |
| INV-003 | Given a NON-admitted Google identity, When they attempt sign-in, Then they return to `/signin?error=not_admitted` with a visible recoverable `role="alert"` message and NO account is created. | US-01 AS-01, FR-087 | [E] |
| INV-004 | Given an allowlisted address whose email is NOT verified, When they attempt sign-in, Then the same rejection alert appears and no account exists (verified-email is a hard gate). | FR-087 | [E] |
| INV-005 | Given a signed-in user, When they activate "Wyloguj" (Sign out), Then the session ends server-side, they land on `/signin`, and protected routes redirect again. | US-01 AS-02 | [E] |
| INV-006 | Given no session cookie, When any `/api/proxy/...` call is made, Then the API answers 401 with `errorCode: "unauthenticated"` (deny-by-default pipeline). | FR-068 | [E] |
| INV-007 | Given a signed-in user on Settings, When they activate "Delete account" and confirm "Permanently delete account" in the full-contract confirmation dialog, Then the account row is hard-deleted, the session ends, and re-signing-in with the same Google identity creates a FRESH account (new id, empty workspace). | US-05, FR-101 | [E] |
| INV-008 | Given the Delete-account confirmation dialog is open, When the user cancels (Cancel button or Esc), Then nothing is deleted and focus returns to the invoker. | FR-101 | [E] |

## B. App chrome & navigation (slices 001–008)

| ID | Given / When / Then | Realizes | Level |
|---|---|---|---|
| INV-010 | Given any signed-in screen, When it renders, Then the app chrome shows the TaskFlow brand (link to `/`), navigation to the workspace and Settings, and a sign-out control. | US-18 AS-04 (pre-form) | [E] |
| INV-011 | Given the sidebar, When it renders, Then the Inbox entry and the caller's ACTIVE projects render as clickable entries; clicking a project navigates to `/projects/{id}`. Pre-redesign gap (recorded as-is): Today/Upcoming/Assigned had NO sidebar entries — they were reachable only by URL or G-chords; post-redesign they are sidebar entries (FR-109). | FR-109, US-08 | [E] |
| INV-012 | Given the four primary views, When the user navigates to `/` , `/today`, `/upcoming`, `/assigned`, Then each renders its labelled heading + listbox: "Your workspace"/Inbox, "Dziś", "Nadchodzące", "Przypisane do mnie". (Pre-redesign trigger G+I/T/U/A chords — removed by FR-111; post-redesign trigger = sidebar entries.) | US-02/US-08 AS-01..03 | [E] |
| INV-013 | Given a project with children, When the sidebar renders, Then children nest exactly one level under their parent; an orphaned child (parent archived) is promoted to top-level rather than vanishing. | US-10 AS-02 | [C] |
| INV-014 | Given archived projects exist, When the sidebar renders, Then archived projects are ABSENT from the default tree and reachable only through the collapsed "Archived" disclosure, which expands (aria-expanded) to list them with an "Unarchive" action per row. | US-10 AS-05/AS-11 | [E] |
| INV-015 | Given the Archived disclosure with an archived project, When "Unarchive" is activated, Then the project returns to the active tree (and to `GET /api/projects`). | US-10 AS-11 | [E] |
| INV-016 | Given the sidebar project row, When it renders, Then the project's preset icon token (closed set: folder, inbox, briefcase, home, star, flag, bookmark, calendar, rocket, target, heart, tag) renders as its glyph and the project name is the accessible label; a shared project additionally shows a shared indicator. | US-10, FR-105 carve-out | [E] |
| INV-017 | Given a shared project the caller does not own, When the sidebar renders it, Then the same management affordances render as for owned projects (pre-redesign gap: Edit/Archive/Delete/Members buttons are NOT role-gated in the sidebar; the server denies the writes). Recorded as-is; the redesign preserves server-side denial and MAY improve gating (viewer-role affordance gaps preserved per S5.1). | FR-068 | [E] |

## C. Task capture & natural-language dates (slice 001/003)

| ID | Given / When / Then | Realizes | Level |
|---|---|---|---|
| INV-020 | Given the Inbox, When the user invokes task creation (pre-redesign: `C` key → dialog "Create task"; post-redesign: global "+ Nowy task" AND inline add), Then a title input receives focus immediately. | US-01 AS-01, FR-107 | [E] |
| INV-021 | Given the capture surface with a typed title, When the user confirms (Enter), Then the task paints OPTIMISTICALLY at the TOP of the Inbox (newest-first) before the server responds, persists across reload, and carries a client-generated UUIDv7 id. | US-01 AS-06, Constitution III | [E] |
| INV-022 | Given the capture surface, When the user cancels (Esc), Then no task is created and focus returns to the invoker. | US-01 AS-07, FR-030 | [E] |
| INV-023 | Given the capture input focused, When the user types characters that are (former) shortcut keys, Then the characters land in the field as text — no dialog, no command fires. | US-01 AS-09, FR-111 | [E] |
| INV-024 | Given a capture input ending in a Polish NL date phrase — `dziś`/`dzisiaj`, `jutro`, `pojutrze`, weekday name (`piątek`, `środa`, …), `za N dni`, `po HH`, `o HH[:MM]`, `DD.MM` — When the task is created, Then the phrase is STRIPPED from the title and the due date resolves in Europe/Warsaw (diacritic/case-insensitive; weekday = next strictly-future; past `DD.MM` rolls to next year; `o/po HH` sets `dueHasTime`). | US-03 AS-02..05 | [E]+[C] |
| INV-025 | Given an impossible date phrase (`30.02`), When the user confirms, Then NO task is created, the input retains its value, and a visible + announced error ("nie rozpoznano…") appears with the surface still open for correction. | EC-02, FR-006/FR-049 | [E] |
| INV-026 | Given a title whose trailing token is NOT a date (`Wersja 2.0`), When created, Then the full string is the title — no due date, no error. | US-03 guard | [E] |
| INV-027 | Given a due date on a task, When the row renders, Then the date shows as visible text `dd.MM.yyyy` (date-only) or `dd.MM.yyyy HH:mm` (`dueHasTime`), recovered to the Warsaw calendar day in both CET and CEST, with an sr-only "termin:" qualifier in the accessible name. | US-03, Principle X, FR-044 | [C] |

## D. Inbox list operations (slices 001–006)

| ID | Given / When / Then | Realizes | Level |
|---|---|---|---|
| INV-030 | Given a brand-new account, When the Inbox renders, Then an accessible empty state appears (hint + how to create the first task) and zero rows; the empty state never flashes while data is still loading. Pre-redesign copy references "press C" — replaced by action-button copy (FR-110/FR-111). | EC-01, FR-110 | [E] |
| INV-031 | Given Inbox tasks, When the list renders, Then it is a labelled `role="listbox"` of `role="option"` rows, each showing: status indicator, title, priority badge (when set, text `P0`–`P3`), label chips (name text), due date; done rows are visually distinguished (strikethrough/`data-status`) beyond color alone. | US-18 AS-02 (pre-form), FR-044 | [E]+[C] |
| INV-032 | Given the Inbox listbox focused, When the user presses ↑/↓, Then selection moves with `aria-selected` and listbox `aria-activedescendant` tracking it; default selection is the first row. (Composite-widget operability — RETAINED after FR-111 as roving selection inside the widget.) | FR-042/043, UIT-090..093 | [E] |
| INV-033 | Given a selected task, When the user toggles done (pre-redesign: Space; post-redesign: row checkbox + Space in-widget), Then the row flips done↔backlog OPTIMISTICALLY (visible before the PATCH lands) and persists across reload. | US-01, Constitution III | [E] |
| INV-034 | Given a selected task, When the user invokes rename (pre-redesign: `E` inline; post-redesign: row action/menu), Then an inline text input opens prefilled + focused; Enter commits (persists), Esc discards. | US-08, FR-030 | [E] |
| INV-035 | Given a rename that hits a 409 version conflict, When the client refetches, Then the typed title is re-applied exactly ONCE against the fresh version (no livelock, no lost input). | Constitution VII | [C] |
| INV-036 | Given a selected task, When the user deletes it (pre-redesign: Delete key, NO confirmation; post-redesign: "⋯" menu Delete item — still unconfirmed), Then the row disappears optimistically and stays gone across reload (server soft-delete). | US-08 | [E] |
| INV-037 | Given a delete that fails server-side (500), When the error lands, Then the row REAPPEARS at its original index (rollback-in-place) and the failure is announced via the persistent polite `role="status"` region with a human message ("Something went wrong…" pre-redesign; Polish equivalent post-D15). | FR-049, Constitution VII | [E] |
| INV-038 | Given at least two tasks, When the user reorders one (pre-redesign: Alt+↑/↓ chord — REMOVED; post-redesign: drag handle AND "Przenieś wyżej/niżej" menu items), Then the order changes optimistically, persists across reload via `PATCH /api/tasks/{id}/position` (fractional rank), and the URL is unchanged. | FR-102, S3.7 | [E] |
| INV-039 | Given a reorder that hits a 409, When retried, Then the new rank is recomputed from FRESH neighbour positions (never the stale rank); if the row was concurrently deleted the move is dropped without a retry write. | Constitution VII | [C] |
| INV-040 | Given a selected task, When the user invokes move-to-project (pre-redesign: `M`; post-redesign: "⋯" menu), Then a selector dialog lists "Inbox" + every active project; choosing one moves the task there optimistically (cross-cache), and the task LEAVES the Inbox list (FR-021 narrowing). | US-08 AS-05, FR-021 | [E] |
| INV-041 | Given ~60+ tasks (virtualized list), When the user scrolls past the selected row, Then the selected option stays mounted and addressable (`aria-activedescendant` never dangles) and selection keys still work. | Constitution Performance | [E] |
| INV-042 | Given any failed task mutation, When the error surfaces, Then the friendly FR-049 message (mapped per `errorCode`) is announced through the global MutationCache announcer into the persistent live region — no silent failure. | FR-049/FR-050 | [C] |

## E. Today / Upcoming (slices 003/005)

| ID | Given / When / Then | Realizes | Level |
|---|---|---|---|
| INV-050 | Given tasks due today and overdue tasks, When `/today` renders, Then BOTH appear ("Dziś" = due today + overdue); overdue rows carry a visible overdue marker ("zaległe") plus the date (color never the sole carrier); done tasks and dateless tasks are excluded. | US-02, FR-044 | [E]+[C] |
| INV-051 | Given Today rows across projects, When grouped, Then groups are by project with Inbox first; within a group rows sort by priority (P0 highest, NULL last); date-only dues sort before same-day timed dues. | US-02 | [C] |
| INV-052 | Given the Warsaw day boundary, When Today membership is computed, Then `23:30` Warsaw is IN today and `00:30` next Warsaw day is OUT — in both CET/CEST and across the DST seam (never fixed-offset arithmetic). | Principle X | [C] |
| INV-053 | Given tasks due in the next 7 Warsaw days, When `/upcoming` renders, Then they group by Warsaw LOCAL date ascending ("Nadchodzące"); today's and dateless tasks are excluded; Today/Upcoming form a strict partition. | US-08 AS-02 | [E]+[C] |
| INV-054 | Given a selected task in a daily view, When the user sets priority (pre-redesign: keys 1–4 → P0–P3; post-redesign: "⋯" menu / drawer picker), Then the visible priority badge updates optimistically and the group re-sorts in place. | US-05 AS-04 | [E] |
| INV-055 | Given a selected task in a daily view, When the user invokes reschedule (pre-redesign: `T`; post-redesign: menu/drawer date control), Then the "Zmień termin" input opens with initial focus, accepts the same Polish NL phrases, and on confirm the task's view membership recomputes optimistically (e.g. leaves Today when moved to tomorrow). | US-05 AS-05, FR-006 | [E] |
| INV-056 | Given a rejected reschedule phrase, When the user confirms it, Then the FR-006 error presentation appears (visible message, input retains value, no write). | FR-006/FR-049 | [E] |
| INV-057 | Given a selected task in a daily view, When the user opens the full editor (pre-redesign: `E`; post-redesign: edit affordance/drawer), Then a dialog presents Tytuł (focused), Opis, Priorytet (—brak—/P0–P3), Projekt, and "Usuń termin"; Ctrl+Enter saves (single `PATCH /edit`), Esc discards with no write. | US-05 AS-06..08, FR-030 | [E] |
| INV-058 | Given the editor form, When validated, Then: title trimmed non-empty ≤500; description ≤8000; priority ∈ {P0..P3, null}; `dueDate`/`dueHasTime` all-or-nothing. | FR-006 | [C] |

## F. Assignment & Assigned view (slice 008)

| ID | Given / When / Then | Realizes | Level |
|---|---|---|---|
| INV-060 | Given a SHARED-project task selected in a daily view, When the user opens assignment (pre-redesign: `A`; post-redesign: "⋯" menu "Przypisz" / drawer picker), Then a picker dialog lists project members as checkboxes; Ctrl+Enter commits the whole set (`PATCH /assignees`); reopening shows the persisted state checked. | US-13 AS-01/02 | [E] |
| INV-061 | Given a PERSONAL (Inbox) task, When the user invokes assignment, Then no picker is offered (assignment exists only in shared projects). | US-13 AS-04 | [E] |
| INV-062 | Given a task assigned to the caller, When `/assigned` renders, Then the task appears under "Przypisane do mnie" grouped by project — including dateless/far-future tasks (Assigned is not date-scoped), with assignee count/avatars on the row. | US-13 AS-03 | [E] |
| INV-063 | Given an assignee change, When it lands, Then rows repaint in place across Today/Upcoming/Assigned caches (a task living ONLY in Assigned still resolves — FR-071 regression). | FR-071 | [C] |

## G. Projects: CRUD, hierarchy, archive, delete (slice 004/007)

| ID | Given / When / Then | Realizes | Level |
|---|---|---|---|
| INV-070 | Given the sidebar, When "New project" is activated, Then a create dialog opens with: name input, color radio group (12 preset tokens), icon radio group (12 preset tokens), and a parent picker defaulting to top-level. | US-10 AS-01 | [E] |
| INV-071 | Given the create/edit form, When validated, Then: name trimmed non-empty ≤200; color/icon MUST be preset tokens (free-form rejected); parent must be an existing top-level project. | US-10, Principle XII | [C] |
| INV-072 | Given a parent project selected, When the child is created, Then it nests exactly one level under the parent in the sidebar; the parent picker NEVER offers a child (no grandchildren). | US-10 AS-02/AS-03 | [E] |
| INV-073 | Given a project, When edited (name/color/icon/parent) and saved, Then changes persist and reflect in the sidebar; re-parenting a top-level project under another top-level project is allowed. | US-10 AS-07/AS-08 | [E] |
| INV-074 | Given a re-parent that would create a grandchild, When selected in the edit form, Then an inline `role="status"` message explains the one-level rule and Save is DISABLED (client guard; server enforces too). | US-10 AS-09, FR-049 | [E] |
| INV-075 | Given a childless project, When "Archive" is activated from the sidebar, Then the project archives IMMEDIATELY (optimistic, no confirmation) and leaves the active tree. | US-10 AS-05 | [E] |
| INV-076 | Given a parent WITH children, When "Archive" is activated, Then a confirmation dialog offers the child disposition (promote to top-level / archive them too) before archiving. | US-10 AS-10 | [E] |
| INV-077 | Given a project holding tasks, When "Delete" is activated, Then a confirmation dialog shows the blast radius ("affects N tasks and M sub-projects") and offers the task disposition: move to Inbox / archive instead / delete them too; Cancel closes with no write. | US-10 AS-04, EC-03, FR-101 | [E] |
| INV-078 | Given a parent with children, When "Delete" is activated, Then the dialog additionally offers the child disposition (promote to top-level / delete them too). | US-10 AS-10 | [E] |
| INV-079 | Given the project view `/projects/{id}`, When it renders, Then the project name is the heading and its tasks list with per-task actions (pre-redesign: only "Move to another project" + "Komentarze" buttons — the row quick-action/checkbox set arrives with the redesign; recorded as-is). | US-10 | [E] |
| INV-080 | Given an archived or deleted project's id, When `/projects/{id}` is visited, Then the view degrades gracefully (generic "Project" heading, no crash; archived projects' comment affordances hidden). | FR-049 | [E] |

## H. Sharing & membership (slice 007/008)

| ID | Given / When / Then | Realizes | Level |
|---|---|---|---|
| INV-085 | Given a personal project, When the owner activates "Share" and confirms, Then the project becomes shared (indicator in sidebar) and the members dialog gains the invite form. | US-11 AS-01 | [E] |
| INV-086 | Given a shared project, When the owner invites an email with a role (Editor/Viewer; the writable vocabulary EXCLUDES owner), Then the member appears in the roster with a role badge; inviting an unknown email surfaces the FR-049 validation message; Invite stays disabled until the form is valid. | US-11 AS-02, FR-049 | [E] |
| INV-087 | Given the members dialog as OWNER, When it renders, Then: roster list (`aria-label "Project members"`), per-member role select + Remove button, invite form, "Transfer ownership", "Unshare project"; NEVER a "Leave project" (last-owner safeguard). | US-11/US-12 | [C]+[E] |
| INV-088 | Given the members dialog as NON-OWNER member, When it renders, Then: read-only roster with role badges, "Leave project", and NO invite form / role controls / Remove / Unshare. | US-12, FR-068 | [C]+[E] |
| INV-089 | Given a member, When the owner removes them (confirm dialog "Remove member"), Then the member loses ALL access — the project vanishes from their sidebar; a former member's API reads return 404. | US-11 AS-04 | [E] |
| INV-090 | Given a shared project, When the owner unshares (confirm dialog), Then the project round-trips to personal and "Share" is offered again. | US-11 AS-06 | [E] |
| INV-091 | Given role semantics, When a VIEWER interacts with a shared task, Then they can read but their writes are denied server-side (403/404 family) regardless of any visible affordance (deny-by-default; UI gating is convenience). | FR-065/FR-068 | [E] |
| INV-092 | Given a role change (Editor↔Viewer) via the roster select, When saved, Then the member's capabilities change accordingly (versioned write; roster refreshes). | US-12 | [E] |
| INV-093 | Given ownership transfer, When the owner transfers to a member (confirmed), Then roles swap accordingly and the project list refreshes. | US-12 | [E] |
| INV-094 | Given membership loss (removed/left), When the ex-member was assigned to tasks, Then their assignments are cleaned up server-side (assignment cleanup regression). | US-13 | [E] |

## I. Labels (slice 006)

| ID | Given / When / Then | Realizes | Level |
|---|---|---|---|
| INV-100 | Given a selected task, When the user opens the label selector (pre-redesign: `L`; post-redesign: "⋯" menu "Etykiety"), Then a dialog lists the caller's PER-USER label roster as keyboard-operable checkboxes seeded from the task's current labels. | US-08 AS-04, slice 006 | [E] |
| INV-101 | Given the selector's "Nowa etykieta…" input, When the user types a name and presses Enter, Then the label is created optimistically (trimmed, client-minted id) and inserted into the roster sorted by name. | slice 006 | [E]+[C] |
| INV-102 | Given a checkbox set change, When committed (Ctrl+Enter / Zapisz), Then the WHOLE set is written (`PATCH /labels`, versionless) and chips on the row update; unchecking + commit removes the chip; empty set clears all labels. | slice 006 | [E] |
| INV-103 | Given label chips on a row, When rendered, Then each chip shows the label NAME text (with its color dot) — never color alone. | FR-044 | [C] |
| INV-104 | Given a label deleted from the roster, When any view repaints, Then the deleted id is purged from `['tasks']`/`['today']`/`['upcoming']` caches so a later label write never resubmits it (422 guard). | Constitution VII | [C] |
| INV-105 | Given label validation, When names are entered, Then: trimmed, non-empty, bounded length; per-task label set bounded with duplicates rejected. | slice 006 | [C] |

## J. Comments (slice 009)

| ID | Given / When / Then | Realizes | Level |
|---|---|---|---|
| INV-110 | Given a shared-project task, When the user opens its detail surface (pre-redesign: "Komentarze:" button → modal dialog; post-redesign: task drawer), Then the comment thread renders chronologically with author name, avatar, relative timestamp (absolute Warsaw wall-clock in `title`), and markdown-rendered body; while loading, a loading indicator ("Wczytywanie komentarzy…") shows. | US-16, FR-106 | [E] |
| INV-111 | Given an empty thread, When it renders, Then the "Brak komentarzy." empty state shows. | US-16 | [E] |
| INV-112 | Given an editor/owner on a shared task, When they type a comment (≤4000, trimmed, non-empty) and submit ("Dodaj komentarz" / Ctrl+Enter), Then it appends OPTIMISTICALLY with their identity; empty/whitespace submits are rejected with "Komentarz nie może być pusty.". | US-16 AS-01, FR-049 | [E] |
| INV-113 | Given the composer, When "@ Wspomnij" is activated, Then a picker dialog lists project members; choosing one inserts a TYPED mention token (`@DisplayName` chip on render); a literal `@` typed in prose stays inert text (never scraped). | US-16 AS-02 | [E]+[C] |
| INV-114 | Given a comment authored by the current user, When rendered, Then Edytuj/Usuń buttons appear (keyboard-reachable); "Zapisz zmiany" commits an edit adding the "(edytowano)" marker; delete requires the "Usunąć komentarz?" confirmation dialog, then the comment disappears from the thread (server soft-delete; NO tombstone rendering is shipped — see Transfer notes). | US-16 AS-04 | [E] |
| INV-115 | Given a comment NOT authored by the current user (including the project OWNER), When rendered, Then NO edit/delete affordances appear and direct API writes are denied 403. | US-16, FR-068 | [E]+[C] |
| INV-116 | Given a VIEWER on a shared task, When the thread renders, Then they read the full thread but get NO composer (no textbox, no submit); a direct POST is 403. | US-16 AS-03, FR-068 | [E]+[C] |
| INV-117 | Given a hostile comment body (`<script>`, `<img onerror>`, `javascript:` link, raw HTML), When rendered, Then nothing executes and no unsafe element/attribute is produced, while the safe markdown subset (bold, code, lists, https links) renders as real elements. | Constitution XII, safeMarkdown | [E]+[C] |
| INV-118 | Given a comment mutation failure, When the error lands, Then the optimistic change rolls back (delete restores at original index) and the failure is announced. | FR-049 | [C] |

## K. Shortcut-system REMOVAL (FR-111 — S2.3's explicit exception; NEW post-redesign assertions)

| ID | Given / When / Then | Realizes | Level |
|---|---|---|---|
| INV-120 | Given a focused task list after the redesign, When the user presses any former bare shortcut key — C, E, M, L, T, A, 1–4, `?`, and the G+I/T/U/A chords — Then NOTHING happens: no action fires, no dialog opens, no errors. (Bindings deleted with `useGlobalShortcuts`.) | FR-111, S3.5 | [E] |
| INV-121 | Given a focused task list, When the user presses Delete, Then the task is NO LONGER deleted by the bare key; the keyboard-reachable delete lives in the row "⋯" menu. | FR-111, D5 | [E] |
| INV-122 | Given a focused task list, When the user presses Alt+↑/↓, Then no reorder fires (chord removed; keyboard-reachable reorder = "Przenieś wyżej/niżej" menu items; the binding itself stays deferred OOS-20). | FR-111, FR-102 | [E] |
| INV-123 | Given the application after the redesign, When audited, Then the shortcuts help overlay (`?` dialog "Keyboard shortcuts") no longer exists anywhere. | FR-111 | [E] |
| INV-124 | Given any text input, When the user types former shortcut characters (c, e, m, l, t, a, digits, ?), Then they land as literal characters (trivially true with no bindings — regression entry). | FR-111, S3.5 | [E] |
| INV-125 | Given editors after the redesign, When the user presses Ctrl+Enter / Esc inside them, Then save/cancel still work (FR-030 editing keys are NOT shortcuts and survive), and composite-widget keys (↑/↓ selection, Space toggle, Enter open) still work INSIDE the focused listbox. | FR-030, FR-042..047 | [E] |

## L. Cross-cutting states, a11y & performance regressions

| ID | Given / When / Then | Realizes | Level |
|---|---|---|---|
| INV-130 | Given any optimistic mutation, When triggered, Then the UI paints the result immediately (before server ack) and a failure rolls back to the snapshot — this contract holds for create, toggle, rename, delete, reorder, move, priority, reschedule, labels, assignees, comments, and project CRUD. | Constitution III/VII | [C] |
| INV-131 | Given the persistent `role="status"` live region (mounted from page load), When any announcement is made, Then text is INJECTED into the existing region (never visibility-toggled) and focus is never stolen. | FR-101 | [C] |
| INV-132 | Given every modal dialog (capture pre-redesign, editor, pickers, confirmations, project forms, members), When opened, Then the full dialog contract holds: initial focus inside, Esc closes, focus returns to the invoker. | FR-101 | [E]+[C] |
| INV-133 | Given the Settings screen, When rendered, Then the user's Google display name, email, and avatar (photo; initials fallback post-redesign per FR-105) show, plus the Delete account action. | US-05, FR-105 | [E] |
| INV-134 | Given the app's security headers, When responses are served, Then CSP includes `img-src` with `lh3.googleusercontent.com`, `frame-ancestors 'none'`, nosniff, DENY framing; production `script-src` never carries `unsafe-eval`. | Principle XII | [C] |
| INV-135 | Given state-changing requests, When they lack a same-origin Origin/Referer, Then the BFF rejects them (CSRF gate) — UI flows always pass. | Principle XII | [C] |
| INV-136 | Given the virtualized Inbox at scale, When rendered, Then rows virtualize (bounded DOM) and scrolling stays responsive — the redesigned rows keep working inside `@tanstack/react-virtual` (stacking-context regression guarded). | Constitution Performance | [E] |

## M. Project Board & groupable List (slice 010)

| ID | Given / When / Then | Realizes | Level |
|---|---|---|---|
| INV-140 | Given the project view `/projects/{id}`, When it renders, Then the header shows a visible, keyboard-operable segmented mode switch **„Lista" \| „Tablica"** (Lucide `List`/`LayoutGrid`); activating „Tablica" swaps the projection to the Board and „Lista" back — same data, no navigation. | US-03 AS-02, FR-103 | [E] |
| INV-141 | Given a project whose mode was switched to Tablica, When the user reloads, Then the Board renders again (per-project `localStorage` persistence); a DIFFERENT project still defaults to Lista; cleared storage/invalid stored value falls back to Lista. | US-03 AS-02 | [E]+[C] |
| INV-142 | Given a project with tasks in several statuses, When the Board renders, Then EXACTLY four columns appear in order Backlog, Do zrobienia, W toku, Zrobione — each a labelled `role="listbox"` with `aria-label` „<label>, N zadań" and a visible live count; cards show sanitized title, due/priority/label chips and assignee avatars on catalog components; an empty column renders the catalog EmptyState. | US-03 AS-03, FR-025, FR-110 | [E]+[A] |
| INV-143 | Given a project containing a `cancelled` task, When the Board renders, Then that task appears NOWHERE on the Board (no column, no card), while the List keeps it reachable. | EC-11 | [E]+[C] |
| INV-144 | Given a Board card, When the user drags it to another column (pointer or dnd-kit KeyboardSensor), Then the card paints in the target column OPTIMISTICALLY and exactly ONE `PATCH /api/tasks/{id}/status` is issued with the target column's status — `position` untouched. | US-03 AS-04, FR-025 | [E] |
| INV-145 | Given a Board card's „⋯" menu, When it opens, Then „Przenieś w lewo"/„Przenieś w prawo" move the card one column; at a boundary the item is OMITTED (Zrobione offers no right move, Backlog no left) — and the full standard action set stays available. | US-03 AS-05/AS-06, FR-108 | [E]+[C] |
| INV-146 | Given the List view, When the user sets „Grupuj: Status" via the visible group-by control, Then groups render in column order (Backlog, Do zrobienia, W toku, Zrobione) with „Anulowane" LAST when non-empty; empty groups are omitted; the grouped render keeps the single-listbox `role="group"` pattern with a flat index. | US-03 AS-07, FR-024 | [E]+[C] |
| INV-147 | Given the List view, When the user sets „Grupuj: Priorytet", Then groups render P0→P3 then „Bez priorytetu" last; „Brak" restores the flat list; „wg cyklu" is NOT offered (slice 011). | US-03 AS-07, FR-024 | [E]+[C] |
| INV-148 | Given a project whose group-by was set, When the user reloads, Then the grouping choice re-applies (per-project `localStorage`, default Brak). | US-03 AS-07 | [E]+[C] |
| INV-149 | Given a VIEWER on a shared project, When the Board renders, Then it is read-only: no drag activation and no move items in the card menu (server still denies a forged PATCH with 403). | FR-065/FR-068 | [E] |
| INV-150 | Given a column move whose PATCH fails server-side, When the error lands, Then the card returns to its source column (rollback) and the failure is announced via the established toast/live-region path with retry. | FR-049/FR-050 | [C] |

---

## Transfer notes (D11 — rows asserting UNSHIPPED capabilities move to their owning slices)

| UIT row | Assertion | Disposition |
|---|---|---|
| UIT-022 | topbar search focusable/functional | → slice 013. 019 ships the DISABLED placeholder only: rendered per mockup, excluded from tab order, announced unavailable (covered as a NEW capability test, not an inventory row). |
| UIT-035 | 10k-row virtualization 60fps/memory budget | → post-baseline [P] benchmark suite (INV-136 keeps the functional regression only). |
| UIT-045 (fan-out clause), UIT-112 | SignalR fan-out to a second session | → slice 016 (no realtime transport is integrated on either side today). The menu-action half of UIT-045 is covered by INV-054/INV-036 etc. |
| UIT-082 | 30 s undo window + "Cofnij" restore + fan-out | → slice 014 (undo) / 016 (fan-out). The Toast component ships the persistent-with-close variant (D11) so 014 restyles nothing; no shipped behavior asserts it yet. |
| UIT-064 | deleted comment renders a TOMBSTONE | **Spec-drift note**: tombstone rendering is NOT shipped behavior — audited on the running app and confirmed across the E2E + unit suites: a deleted comment disappears from the thread (`"Brak komentarzy."` when last); the "soft-delete/tombstone" of slice 009 is a SERVER-side property (idempotent delete, row retained). INV-114 covers the shipped behavior; tombstone RENDERING, if ever desired, is a future product decision — 019 must not invent it (S4.4 "byte-for-byte"). |

## Audit-observed pre-redesign gaps the redesign FIXES (not regressions; context for reviewers)

- No sidebar entries for Today/Upcoming/Assigned (INV-011) — FR-109 adds them with counts.
- No counts anywhere; `GET /api/views/counts` is NEW (FR-109) — covered by new-capability tests, not inventory rows.
- No "Duplikuj" anywhere; `POST /api/tasks/{id}/duplicate` is NEW (FR-112) — new-capability tests.
- Project view rows expose only Move + Komentarze (INV-079); the redesign brings the full row action set there.
- No visible reorder affordance (only the Alt+chord, INV-038/122); the redesign adds drag handle + menu items.
- Empty states reference keyboard shortcuts ("Naciśnij C…", `kbd` C) — replaced per FR-110/FR-111.
- `tf-visually-hidden` class used but undefined (live a11y bug) — fixed by T005's `.sr-only`.
- Sidebar management buttons not role-gated for non-owner members (INV-017) — server denies; redesign preserves denial and keeps viewer-affordance gaps per S5.1.
- Archive of a childless project fires with no confirmation (INV-075) — recorded as-is; the redesign keeps the same semantics (restyle only).
- UI copy is mixed Polish/English (headings "Your workspace"/"Settings" vs "Dziś") — normalized to Polish during the sweep (D15); inventory rows assert BEHAVIOR, not the English strings.

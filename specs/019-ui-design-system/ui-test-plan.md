# Plan testów UI — slice 019 (UI Design System) 

**Cel**: pełna lista tego, co musi zostać przetestowane w nowym UI. Dokument wejściowy dla
`/speckit-specify` / `/speckit-tasks` — tam każda pozycja zamieni się w test (Vitest dla
komponentów, Playwright dla E2E, axe dla a11y). Identyfikatory `UIT-###` służą do
odwołań w tasks.md.

**Poziomy testów** (zgodnie z Konstytucją VIII):
- **[C]** component — Vitest + Testing Library (izolowany komponent, jsdom)
- **[E]** E2E — Playwright przez realny stack (PG + API + BFF)
- **[A]** a11y — axe-core scan + testy klawiaturowe w Playwright
- **[V]** visual — screenshot testy Playwright (per paleta)
- **[P]** perf — benchmarki budżetów z konstytucji (osobny suite, po baseline)

Prototyp: `tests/mockup.spec.ts` (18 zielonych asercji na statycznym mockupie) pokrywa
podzbiór [C]/[E] i zostanie przepisany na realną aplikację przy implementacji.

---

## A. Tokeny i system 4 palet

- **UIT-001 [C]** Default motywu = `dark-cool`; brak klasy → SSR fallback renderuje się
  poprawnie (bez FOUC niestylowanych tokenów).
- **UIT-002 [C]** Przełączenie trybu (dark↔light) zmienia klasę composite na `<html>`
  i NIE resetuje palety; przełączenie palety nie resetuje trybu.
- **UIT-003 [E]** Dla każdej z 4 palet: kluczowe tokeny faktycznie się rozwiązują
  (tło listy, kolor akcentu, kolor tekstu — asercje na `getComputedStyle`).
- **UIT-004 [C]** Komponenty nie zawierają surowych hexów — lint/grep test na
  `#[0-9A-Fa-f]{3,8}` poza plikiem tokenów (wyjątki wymagają komentarza).
- **UIT-005 [C]** `color-scheme` ustawione per tryb (natywne kontrolki/scrollbary).
- **UIT-006 [A]** `prefers-reduced-motion: reduce` → przejścia ≤100ms/instant
  (Konstytucja II).
- **UIT-007 [V]** Screenshot każdego ekranu w 4 paletach — regresja wizualna
  (dark-cool, dark-warm, light-cool, light-warm).

## B. Kontrast i WCAG (per paleta — automatycznie)

- **UIT-010 [A]** axe-core scan każdego ekranu × 4 palety: zero violations poziomu AA.
- **UIT-011 [A]** Macierz kontrastów par tokenów (test tabelaryczny): tekst ≥4.5:1,
  elementy nietekstowe/obrysy kontrolek/ikony ≥3:1 — W TYM stany hover
  (accent-strong-hover, bg-hover) i selected (accent-soft).
- **UIT-012 [A]** Focus ring widoczny na KAŻDYM fokusowalnym elemencie, ≥3:1 do tła,
  nie zmienia kształtu okrągłych kontrolek.
- **UIT-013 [A]** Kolor nigdy nie jest jedynym nośnikiem informacji (overdue = kolor
  + data; priorytet = ikona; done = przekreślenie + znacznik).
- **UIT-014 [A]** Hit area każdej interaktywnej kontrolki ≥28px (checkbox — wzór
  padded hit area) / ≥32px (przyciski ikonowe).

## C. Chrome aplikacji: topbar + sidebar

- **UIT-020 [E]** Przycisk „+ Nowy task" istnieje, jest widoczny globalnie (każdy widok)
  i otwiera tworzenie taska (F1).
- **UIT-021 [E]** Pole quick-add inline istnieje w Inbox/projekcie/Today; Enter tworzy
  task optymistycznie (paint <16ms — asercja na natychmiastowe pojawienie wiersza).
- **UIT-022 [E]** Wyszukiwarka dostępna z topbaru; fokusowalna z klawiatury.
- **UIT-023 [C]** Collapse sidebaru: `aria-expanded` odzwierciedla stan, label
  przełącza się (Zwiń/Rozwiń), treść pozostaje osiągalna po zwinięciu.
- **UIT-024 [E]** Sidebar zawiera WSZYSTKIE widoki (Inbox/Today/Upcoming/Assigned)
  z ikonami i licznikami; liczniki zgodne z danymi z API.
- **UIT-025 [E]** Lista projektów w sidebarze; klik nawiguję do projektu; aktywna
  pozycja wyróżniona (accent + waga) i oznaczona `aria-current`.
- **UIT-026 [C]** Emoji dozwolone WYŁĄCZNIE jako opcjonalna ikonka projektu; chrome
  aplikacji używa Lucide (test: brak emoji w markup poza `proj-emoji`).
- **UIT-027 [E]** Przycisk „Nowy projekt" istnieje i otwiera tworzenie projektu.

## D. Lista tasków (wiersze, grupowanie, stany)

- **UIT-030 [E]** Wiersz renderuje komplet: checkbox, priorytet (gdy ustawiony),
  tytuł, etykiety, termin, awatary przypisanych.
- **UIT-031 [C]** Grupowanie (Zaległe/Dzisiaj/Później) z licznikami; suma liczników
  grup = licznik widoku.
- **UIT-032 [C]** Task zaległy: termin w kolorze danger; task zrobiony: przekreślenie
  + zielony znacznik; task bez priorytetu nie renderuje pustej ikony dla SR.
- **UIT-033 [E]** Checkbox done/undone działa optymistycznie i jest cofalny.
- **UIT-034 [E]** Pusty stan listy: hint + przycisk akcji (H2), bez wizardów.
- **UIT-035 [P]** Wirtualizacja: 10 000 tasków w widoku — scroll 60 fps, pamięć
  <300 MB (budżety konstytucji; osobny benchmark suite).
- **UIT-036 [E]** Tytuł wiersza jest przyciskiem otwierającym drawer (nie hover-only).

## E. Akcje na wierszu (pasek + menu „⋯")

- **UIT-040 [E]** Pasek akcji ujawnia się na hover ORAZ na fokus klawiaturowy;
  Tab i Shift+Tab przechodzą przez akcje w obu kierunkach (nie `display:none`).
- **UIT-041 [E]** KAŻDY wiersz ma „⋯" z kompletem akcji: done, edytuj, przenieś,
  priorytet, termin, etykiety, przypisz, duplikuj, usuń (F2).
- **UIT-042 [C]** Menu: `role="menu"`, strzałki ↑↓ + Home/End nawigują (z poprawnym
  zachowaniem bez fokusa), Esc/Tab/klik-poza/aktywacja pozycji zamykają.
- **UIT-043 [C]** `aria-expanded` na triggerze: true po otwarciu, false po każdej
  ścieżce zamknięcia; fokus wraca na trigger.
- **UIT-044 [C]** Menu pozycjonuje się w viewporcie (dolne wiersze → menu nie ucieka
  poza ekran; wymiary mierzone PO pokazaniu).
- **UIT-045 [E]** Akcja z menu faktycznie wykonuje operację (np. done) — asercja
  na zmianę stanu + fan-out do drugiej sesji (SignalR).

## F. Drawer szczegółów taska (E2)

- **UIT-050 [E]** Klik tytułu wiersza otwiera drawer z danymi TEGO taska.
- **UIT-051 [E]** Wszystkie pola edytowalne z draweru: status, priorytet, termin,
  etykiety, przypisani — każda zmiana optymistyczna + zapis przez API.
- **UIT-052 [C]** Drawer niemodalny: lista pozostaje interaktywna; X zamyka; Esc
  zamyka Z WYJĄTKIEM fokusa w polu tekstowym; fokus wraca do wywołującego.
- **UIT-053 [C]** Identyfikator taska w monospace (JetBrains Mono).
- **UIT-054 [E]** Deep link: URL draweru otwiera się bezpośrednio (nawigacja
  przeglądarki wstecz/dalej zachowuje stan listy).

## G. Komentarze (rendering — istniejąca funkcja slice 009 w nowym UI)

- **UIT-060 [E]** Komentarze renderują się w drawerze: poprawna liczba, kolejność
  chronologiczna, autor, timestamp, treść.
- **UIT-061 [C]** `@mention` wyróżniony kolorem akcentu; renderuje się jako token,
  nie zwykły tekst.
- **UIT-062 [E]** Markdown komentarza przechodzi sanitizację — payload XSS
  (`<script>`, `<img onerror>`) NIE wykonuje się i nie renderuje raw HTML
  (Konstytucja XII; regresja safeMarkdown z 009).
- **UIT-063 [E]** Composer: dodanie komentarza optymistyczne; `@` otwiera picker
  wzmianek; awatar autora poprawny (zdjęcie Google → fallback inicjały).
- **UIT-064 [E]** Komentarz usunięty (soft-delete) renderuje tombstone, nie znika
  z wątku (zgodnie z 009).
- **UIT-065 [C]** Awatary: deterministyczny kolor z userId WYŁĄCZNIE z palety
  AA-safe; fallback inicjałów gdy brak zdjęcia.

## H. Etykiety (wyświetlanie — istniejąca funkcja slice 006 w nowym UI)

- **UIT-070 [E]** Chip etykiety renderuje nazwę + kropkę w kolorze etykiety;
  kolory z ograniczonej, kontrastowej palety.
- **UIT-071 [C]** Tekst chipa spełnia 4.5:1 na tle wiersza I na tle wiersza
  selected (accent-soft) — w 4 paletach.
- **UIT-072 [E]** Dodanie/usunięcie etykiety z draweru (pill „+ dodaj") działa
  optymistycznie; lista etykiet jest per-user (scope z 006).
- **UIT-073 [C]** Długie nazwy etykiet: truncation z ellipsis, pełna nazwa
  dostępna (tooltip z odpowiednikiem fokusowym, nie hover-only).

## I. Modale, toasty, operacje destrukcyjne

- **UIT-080 [C]** Modal potwierdzenia: initial focus w dialogu, focus trap, Esc
  zamyka, fokus wraca do wywołującego (pełny kontrakt dialogowy — Konstytucja II).
- **UIT-081 [E]** Kopia modala usuwania pokazuje blast radius (co i ile zostanie
  usunięte, np. liczba komentarzy).
- **UIT-082 [E]** Po potwierdzeniu: toast z „Cofnij" widoczny przez pełne okno
  undo 30 s (Konstytucja VII); „Cofnij" przywraca task (fan-out do innych sesji).
- **UIT-083 [C]** Toast: `role="status"` w TRWAŁYM live regionie (treść
  wstrzykiwana), nie kradnie fokusa; po zamknięciu fokus nie ląduje na
  `display:none`; toasty informacyjne auto-dismiss 3–5 s.
- **UIT-084 [C]** Scrim modala 40–60% czerni + blur; klik w backdrop zamyka
  (z potwierdzeniem przy niezapisanych zmianach).

## J. Operowalność klawiaturowa (bez hotkeys — G1)

- **UIT-090 [E]** Podróż klawiaturowa end-to-end BEZ jednoznakowych skrótów:
  Tab-only od wejścia do: utworzenia taska, edycji, komentarza, usunięcia z undo.
- **UIT-091 [C]** Żadne jednoznakowe hotkeys nie są zarejestrowane (regresja
  usunięcia: naciśnięcie C/E/M/L/T/1-4 w liście NIE wykonuje akcji).
- **UIT-092 [C]** Kolejność Tab = kolejność wizualna; brak pułapek fokusa poza
  modalami.
- **UIT-093 [E]** Strzałki ↑↓ nawigują po liście tasków (roving tabindex — zakres
  implementacji), Enter otwiera drawer.

## K. Responsywność i layout (E3)

- **UIT-100 [V]** 1440px / 1024px / 768px: brak poziomego scrolla, drawer i sidebar
  zachowują się sensownie (≤1024px: drawer jako overlay — do decyzji w spec).
- **UIT-101 [C]** Z-index wg skali (sticky 10 / sidebar 20 / drawer 40 / modal 100 /
  toast 1000) — test porządku warstw menu nad drawerem itd.

## L. Budżety wydajności (Konstytucja III/Performance — osobny suite)

- **UIT-110 [P]** Optimistic paint <16 ms dla: done, quick-add, edycja pola.
- **UIT-111 [P]** FCP <1 s, TTI <2.5 s na zimnym starcie z ciepłym backendem.
- **UIT-112 [P]** Fan-out SignalR p95 <1000 ms commit-to-paint (2 sesje).

---

**Kryterium wyjścia slice'a**: wszystkie [C]/[E]/[A] zielone w CI; [V] baseline
zatwierdzony; [P] po ustanowieniu baseline'u benchmarków. Failing suite blokuje
merge (Konstytucja VIII).

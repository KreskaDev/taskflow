# Zbieranie wymagań: UI Design System + pełna obsługa przez UI (slice 019)

**Cel slice'a**: aplikacja ma wyglądać profesjonalnie i być W PEŁNI obsługiwalna przez widoczne
elementy UI (przyciski, menu, panele) — bez konieczności znajomości skrótów klawiszowych.
Hotkeys przestają być podstawowym sposobem obsługi (wracają później jako akceleratory).

**Status**: WYPEŁNIONE 2026-08-09. Odpowiedzi kluczowe (A1, B1/B2, E2, G1) pochodzą od
użytkownika; pozycje oznaczone `(default — do akceptacji)` przyjął agent zgodnie z regułą
„zdecyduj za mnie" i wymagają tylko sprzeciwu, nie potwierdzenia.

---

## A. Kierunek i inspiracje

**A1. Które aplikacje mają być wzorcem wyglądu?** (wybierz 1–2 główne)
- [x] Linear (ciemny, gęsty, minimalistyczny, szybki) ← **decyzja użytkownika**
- [ ] Todoist (jasny, przyjazny, dużo bieli, proste listy)
- [ ] Notion (neutralny, dokumentowy, dużo szarości)
- [ ] Things 3 (jasny, delikatny, dużo oddechu)
- [ ] Height / Asana (kolorowy, "produktowy")
- → inne / linki do screenów: —

**A2. Trzy przymiotniki, które mają opisywać UI**:
- → czysty, gęsty, profesjonalny *(default — do akceptacji; spójne z A1)*

**A3. Czego absolutnie NIE chcesz?**:
- → emoji jako ikony systemowe; niskokontrastowe „szare na szarym"; wielkie puste strony bez
  akcji; spinnery tam, gdzie optimistic UI może namalować wynik *(default — do akceptacji)*

## B. Ton wizualny

**B1. Motyw domyślny:**
- [ ] jasny (light)
- [x] ciemny (dark) ← **decyzja użytkownika, DOPRECYZOWANA**: system stylowania przenosimy
  z bloga kreskadev.github.io (ADR-041) — **2 tryby (dark/light) × 2 palety (cool blue /
  warm red)**, default `dark-cool` („black and blue"). Slice 019 dostarcza architekturę
  tokenów i wszystkie 4 palety; UI przełącznika + persystencja preferencji = slice 018.
- [ ] jasny + ciemny od razu w tym slice

**B2. Kolor akcentu (przyciski primary, focus, zaznaczenia):**
- [ ] fiolet/indygo (styl Linear) ← ~~pierwotna decyzja~~ ZMIENIONA po obejrzeniu mockupu
- [x] **niebieski z bloga**: `#5290BD` (dark-cool) / `#3A7194` (light-cool) ← **decyzja
  użytkownika** („odzwierciedlić styl bloga — black and blue"); paleta warm używa bordowego
  `#C97E87` / `#9D4754`. Pełna tabela tokenów: `design-brief.md`.
- [ ] czerwony/pomarańczowy (styl Todoist)
- [ ] zielony
- → własny (hex, jeśli masz): tokeny 1:1 z `globals.css` bloga (przyszła wspólna paczka
  design-tokenów dla wszystkich projektów użytkownika)

**B3. Gęstość interfejsu:**
- [x] gęsto (dużo wierszy na ekranie, mało paddingu — styl Linear) *(default — do akceptacji;
  wynika z A1)*
- [ ] średnio (default większości aplikacji)
- [ ] przestronnie (duże odstępy, styl Things)

## C. Typografia

**C1. Font:**
- [ ] Inter (standard produktowy, neutralny)
- [x] **Geist** — stack z bloga: Geist (sans/UI) + Instrument Serif (tylko brand) +
  JetBrains Mono (identyfikatory/kod); self-hosted przez `next/font` *(zmienione z defaultu
  Inter po decyzji „styl bloga")*
- [ ] systemowy stack (zero ładowania fontów)
- → inny: —

**C2. Rozmiar bazowy list/treści:**
- [x] 13px (gęsto, Linear) *(default — do akceptacji; spójne z B3; nagłówki/treści dłuższe ≥14px)*
- [ ] 14px (typowy SaaS)
- [ ] 15–16px (czytelnie, mniej na ekranie)

## D. Ikony i grafika

**D1. Obecnie ikony to emoji (📥 📁 👥). Docelowo:**
- [ ] biblioteka ikon wszędzie, emoji znikają całkowicie
- [x] ikony w chrome aplikacji (Lucide), emoji zostają tylko jako opcjonalne ikonki projektów
  *(default — do akceptacji; wzorzec Linear: systemowe ikony + emoji projektu jako wybór usera)*
- [ ] zostawić emoji (nie przeszkadzają)

**D2. Awatary użytkowników (przy komentarzach, przypisaniach):**
- [ ] inicjały w kolorowym kółku
- [x] zdjęcie z Google, fallback na inicjały *(default — do akceptacji; avatar URL już przychodzi
  z Google OAuth)*
- [ ] bez awatarów (sam tekst)

## E. Layout aplikacji

**E1. Struktura główna:**
- [ ] stały sidebar (jak teraz, dopracowany)
- [x] sidebar zwijany (przycisk collapse) *(default — do akceptacji; wzorzec Linear)*
- → inna: —

**E2. Szczegóły taska (edycja pól, komentarze) otwierają się jako:**
- [x] panel boczny z prawej (drawer — styl Linear/Height) ← **decyzja użytkownika**
- [ ] modal na środku (jak teraz)
- [ ] pełna strona taska

**E3. Docelowe urządzenia:**
- [ ] tylko desktop
- [x] desktop + sensowne zachowanie na tablecie/wąskim oknie *(default — do akceptacji; pełne
  RWD/mobile pozostaje OOS-03)*
- [ ] pełny responsive z myślą o telefonie

## F. Obsługa przez UI — kluczowa część tego slice'a

**F1. Tworzenie taska.** Gdzie mają być widoczne przyciski/pola „dodaj task"?
- [ ] stały przycisk "+ Nowy task" w górnym pasku
- [ ] pole "dodaj task" inline na dole/górze każdej listy
- [x] oba *(default — do akceptacji; globalny przycisk + inline w Inbox/projekt/Today)*
- → inne: —

**F2. Akcje na tasku — jak dostępne?** (zaznaczono kombinację)
- [x] pasek ikon akcji przy hover (z fokusem klawiaturowym jako odpowiednikiem) — tylko 2–3
  najczęstsze: done, edytuj, menu
- [x] menu kontekstowe "⋯" na każdym wierszu ze WSZYSTKIMI akcjami
- [x] panel szczegółów taska (drawer z E2) z pełną edycją wszystkich pól
- → priorytet kombinacji: drawer = pełna edycja; „⋯" = kompletność akcji na wierszu; hover-ikony =
  skrót do najczęstszych *(default — do akceptacji)*

**F3. Nawigacja między widokami (Inbox / Today / Upcoming / Assigned / projekty):**
- [x] wszystko w sidebarze jako klikalne pozycje z ikonami i licznikami (np. "Today (3)")
  *(default — do akceptacji)*
- [ ] górne taby
- → inne: —

**F4. Czego brakuje Ci NAJBARDZIEJ w obecnym UI?**
- → (brak odpowiedzi użytkownika — spec przyjmie audyt heurystyczny obecnego UI jako źródło
  braków: widoczność akcji „dodaj task", klikalne pola, potwierdzenia akcji)

## G. Los hotkeys

**G1. Co robimy ze skrótami w tym slice?**
- [ ] zostają, ale UI musi być samowystarczalne
- [ ] wyłączamy jednoznakowe skróty do czasu aż UI będzie kompletne
- [x] **usuwamy całość, wrócą osobnym slice'em** ← **decyzja użytkownika**

Konsekwencje (potwierdzone):
- Konstytucja: Zasada I „Keyboard-First" zostaje przeredagowana na **„UI-First"** — pełna obsługa
  przez widoczne elementy UI; system własnych skrótów (C/E/M/L/T/1-4, command palette jako
  wymóg…) przestaje być wymogiem konstytucyjnym i wraca w przyszłości osobnym slice'em jako
  akcelerator (opt-in).
- Dostępność klawiaturowa WCAG (Zasada II) ZOSTAJE: Tab/Shift+Tab, Enter/Space, Esc, strzałki
  w listach/menu, focus management, ARIA — to nie są „hotkeys", to operowalność.
- Kod istniejących skrótów jednoznakowych do usunięcia w ramach slice'a 019.

## H. Zakres pierwszego przejścia

**H1. Kolejność ekranów do przeprojektowania** (default przyjęty):
- [x] Workspace/Inbox — 1
- [x] Widok projektu — 2 (zaraz po nim wchodzi slice 010 board)
- [x] Today/Upcoming — 3
- [x] Panel szczegółów taska + komentarze — 4
- [x] Sign-in + Settings — 5

**H2. Puste stany i onboarding:**
- [x] tak, prosty hint + przycisk (bez wizardów — konstytucja IV zakazuje onboardingu)
  *(default — do akceptacji)*
- [ ] nie, minimalnie

## I. Praktyczne

**I1. Animacje/przejścia:** [x] subtelne (100–150ms, respektujące `prefers-reduced-motion`)
*(default — do akceptacji)*
**I2. Logo:** → „TaskFlow" tekstem wystarczy *(default — do akceptacji)*
**I3. Coś jeszcze:** → —

## J. Werdykt po przeglądzie mockupu (2026-08-09)

**J1. Ocena użytkownika:** mockup `mockup-inbox.html` **spełnia oczekiwania i wyznacza
kierunek** („dobry kierunek"), ale pokrywa tylko wycinek zakresu. Mockup pozostaje próbką
kierunku wizualnego — dalsze braki adresuje SPECYFIKACJA, nie kolejne iteracje mockupu
← **decyzja użytkownika**.

**J2. Braki do zaadresowania w `/speckit-specify`** (wszystkie zaznaczone przez użytkownika):

1. **Więcej ekranów** — widok projektu, Today/Upcoming, Assigned, panel Sign-in i Settings
   w nowym design systemie (kolejność z H1; board = slice 010, buduje się już na tokenach).
2. **Więcej komponentów** — katalog klocków design systemu: formularze, pickery (data,
   assignee, label), search, chipy, tabele/listy wariantowe, empty states.
3. **Więcej stanów i interakcji** — loading/skeleton (tylko genuine network-bound,
   konstytucja IV), stany błędów, offline/reconnect (konstytucja V), długie treści
   i overflow, drag&drop, wąskie okno (E3).
4. **Pełny drawer szczegółów** — komentarze/wzmianki (slice 009), aktywność, edycja
   wszystkich pól inline; drawer z E2/F2 jako pełnoprawna powierzchnia robocza.
5. **Inwentaryzacja feature'ów w formie testowalnej** ← **wymaganie użytkownika (kluczowe)**:
   spec MUSI wylistować KAŻDY istniejący feature aplikacji w formie pozwalającej napisać
   test UI (dany ekran → zachowanie → oczekiwany efekt), tak by kolejne prace (019 i późniejsze
   slice'y) nie mogły niezauważenie usunąć istniejących funkcji. `ui-test-plan.md`
   (UIT-001…UIT-112) jest zalążkiem ograniczonym do mockupu Inbox — inwentaryzacja ma objąć
   całą istniejącą powierzchnię aplikacji (slice'y 001–009) i stać się siatką regresji
   utrzymywaną przy każdym kolejnym slice.

---

**Co się stanie z odpowiedziami:** (1) poprawka konstytucji (`/speckit-constitution` — Zasada I),
(2) dopisek US/FR do product-vision (jedyny alokator ID), (3) `/speckit-specify` → plan → tasks →
implementacja tego slice'a PRZED 010, żeby board budował się już na nowym design systemie.
Wejścia do `/speckit-specify`: ten plik (§A–§J) + `design-brief.md` + `ui-test-plan.md` +
`mockup-inbox.html`.

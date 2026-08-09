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
- [x] ciemny (dark) ← **decyzja użytkownika** (light dojdzie w slice 018 — theming)
- [ ] jasny + ciemny od razu w tym slice

**B2. Kolor akcentu (przyciski primary, focus, zaznaczenia):**
- [x] fiolet/indygo (styl Linear) ← **decyzja użytkownika**
- [ ] niebieski (klasyczny produktowy)
- [ ] czerwony/pomarańczowy (styl Todoist)
- [ ] zielony
- → własny (hex, jeśli masz): — (dokładny odcień dobierze spec/plan z walidacją kontrastu WCAG)

**B3. Gęstość interfejsu:**
- [x] gęsto (dużo wierszy na ekranie, mało paddingu — styl Linear) *(default — do akceptacji;
  wynika z A1)*
- [ ] średnio (default większości aplikacji)
- [ ] przestronnie (duże odstępy, styl Things)

## C. Typografia

**C1. Font:**
- [x] Inter (standard produktowy, neutralny) *(default — do akceptacji; font Linear-podobny,
  self-hosted — zero zewnętrznych zależności runtime)*
- [ ] Geist (nowocześniejszy, "vercelowy")
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

---

**Co się stanie z odpowiedziami:** (1) poprawka konstytucji (`/speckit-constitution` — Zasada I),
(2) dopisek US/FR do product-vision (jedyny alokator ID), (3) `/speckit-specify` → plan → tasks →
implementacja tego slice'a PRZED 010, żeby board budował się już na nowym design systemie.

# Design Brief: TaskFlow UI Design System (slice 019)

**Źródła**: odpowiedzi z `visual-requirements.md` (decyzje użytkownika mają pierwszeństwo),
**system stylowania bloga kreskadev.github.io** (`app/globals.css`, ADR-039 token mapping +
ADR-041 dual-palette — wartości przeniesione 1:1) oraz skill `ui-ux-pro-max` (wytyczne UX
§1–§9, gęstość, stany interakcji). Ten dokument jest wejściem do `/speckit-specify` — spec
może doprecyzować, ale nie powinien łamać tych tokenów bez powodu.

## Kierunek

Gęstość i obsługa jak Linear; **tożsamość wizualna jak blog KreskaDev**: "black and blue" +
system **2 tryby (dark/light) × 2 palety (cool blue / warm red)**. Trzy przymiotniki: czysty,
gęsty, profesjonalny. Zakazane: emoji jako ikony systemowe, niskokontrastowe szare-na-szarym,
spinnery zamiast optimistic UI, dekoracyjne animacje.

**Cel długoterminowy (poza zakresem 019, wpływa na strukturę)**: użytkownik chce w przyszłości
wyodrębnić ten zestaw tokenów do wspólnej paczki („common") aktualizującej wszystkie jego
projekty (blog, TaskFlow, kolejne). Dlatego: (1) nazwy i wartości tokenów trzymamy **1:1
z blogiem** (`--color-bg-primary`, `--color-accent`, …), (2) wszystkie tokeny żyją w JEDNYM
pliku (`globals.css` / Tailwind `@theme`), (3) komponenty znają wyłącznie tokeny semantyczne —
ekstrakcja = przeniesienie jednego pliku. Nie budujemy paczki teraz (YAGNI, konstytucja).

## System palet (przeniesiony z bloga)

Klasa composite na `<html>` (`dark-cool` | `dark-warm` | `light-cool` | `light-warm`)
przełącza komplet tokenów; **default TaskFlow: `dark-cool`** (decyzja „ciemny"). Mechanizm
przełączania trybu/palety = zakres slice'a 018 (theming) — slice 019 dostarcza architekturę
tokenów i wszystkie 4 palety, 018 dodaje UI przełącznika + persystencję preferencji.
(Uwaga: `color-scheme` per tryb; SSR fallback na default przed hydracją, jak na blogu.)

Tokeny bazowe (nazwy per blog ADR-039; aliasy TaskFlow w nawiasach tam, gdzie mockup używa
skrótu):

| Token | dark-cool | dark-warm | light-cool | light-warm |
|---|---|---|---|---|
| `bg-primary` (bg-base) | `#181818` | `#1A1816` | `#FAFAFA` | `#FAF7F2` |
| `bg-secondary` (bg-elevated) | `#222222` | `#221F1C` | `#F2F2F2`* | `#F2EFE8`* |
| `text-primary` | `#E8E4DC` | `#E8E4DC` | `#2A2520` | `#2A2520` |
| `text-secondary` | `#B5B0A6` | `#B5B0A6` | `#5C5A55` | `#5C5A55` |
| `text-tertiary` (disabled/meta) | `#8A857C` | `#8A857C` | `#807B72` | `#807B72` |
| `accent` | `#5290BD` | `#C97E87` | `#3A7194` | `#9D4754` |
| `accent-hover` | `#7AAACF` | `#D9959C` | `#2D5E7E` | `#844050` |
| `accent-soft` | `#11212D` | `#2F1518` | `#E5EFF6` | `#F1E3E5` |
| `burgundy` (danger) | `#C97E87` | `#C97E87` | `#9D4754` | `#9D4754` |
| `green` (success) | `#7BA887` | `#7BA887` | `#4A7C57` | `#4A7C57` |
| `border` | `#383330` | `#383330` | `#E5E0D8` | `#E5E0D8` |
| `border-strong` | `#524C47` | `#524C47` | `#C9C0B0` | `#C9C0B0` |
| `surface-elevated` | `rgba(255,255,255,.06)` | j.w. | `rgba(0,0,0,.04)` | j.w. |

\* ROZSTRZYGNIĘTE (mockup v3): w trybie light powierzchnie podniesione (karty, drawer,
menu, modal) = `#FFFFFF` z borderem, a blogowe `bg-secondary` pełni rolę `bg-deep`
(tło chrome/topbar). W dark mapowanie bez zmian: `bg-secondary` = `bg-elevated`.

Rozszerzenia TaskFlow (aplikacja potrzebuje więcej niż blog; nazwy w konwencji bloga;
wartości zweryfikowane w review kontrastu 2026-08-09). Kandydaci do „common" przy ekstrakcji:

- `--color-bg-deep` — tło chrome (topbar) o pół tonu głębsze niż `bg-primary`:
  `#141414` / `#161412` (dark), w light = blogowe `bg-secondary`.
- `--color-bg-hover` `rgba(255,255,255,.05)` / `rgba(0,0,0,.045)`; `--color-bg-active`
  `rgba(255,255,255,.08)` / `rgba(0,0,0,.08)`.
- `--color-warning` `#C9B850` (dark) / **`#8F7F36`** (light — blogowe `#A89640` daje tylko
  2.8:1 na jasnym tle, poniżej 3:1 dla elementów niebędących tekstem).
- `--color-accent-strong` + `--color-accent-strong-hover` — tło primary buttonów;
  hover MUSI iść **ciemniej**, nie w `accent-hover` (w dark `accent-hover` to tint TEKSTU,
  jaśnieje — biały tekst na nim spada do 2.4–4.0:1): dark-cool `#356D97`→`#2D5E7E`,
  warm `#9D4754`→`#844050`, light-cool `#3A7194`→`#2D5E7E`.
- `--color-danger-strong` + `--color-danger-strong-hover` — tło destructive buttonów,
  osobno od accent (destructive jest bordowy w KAŻDEJ palecie, także cool): `#9D4754`→
  `#844050` we wszystkich 4 paletach (blogowe burgundy/light-warm accent-hover).
- Obrys kontrolek, których granica jest jedynym identyfikatorem (checkbox/radio):
  `--color-fg-disabled` (~4.2:1), NIE `border-strong` (~2:1 — łamie WCAG 1.4.11).
- `--color-selection` — w dark `accent-soft` jest niewidoczny na tle (1.05–1.08:1), więc
  selection = półprzezroczysty akcent `rgba(accent,.30)`; w light = `accent-soft`.
- `--avatar-*` — paleta awatarów ograniczona do wartości AA-safe dla białych inicjałów
  (≥4.5:1), np. `#356D97`, `#4A7C57`, `#A05A20`; deterministyczny wybór z userId losuje
  wyłącznie z tej listy.

Zasady: kontrast weryfikowany **osobno w każdej z 4 palet** (tekst ≥4.5:1, elementy
graficzne/focus ≥3:1, stany hover TEŻ); kolor funkcjonalny zawsze z ikoną/tekstem;
`::selection` na `--color-selection`; focus ring 2px `accent` + offset; scrim
modali/draweru 40–60% czerni + blur 4px (jak `::backdrop` bloga); menu kontekstowe
(`role="menu"` ⇒ obowiązkowa nawigacja strzałkami + `aria-expanded` na triggerze),
modale (native `<dialog>`: focus trap, Esc, powrót fokusa) i toasty (`role="status"`,
nie kradną fokusa) są częścią design systemu. Toasty informacyjne auto-dismiss 3–5s;
toast z przyciskiem **Cofnij** po operacji destrukcyjnej żyje przez całe okno undo
(30 s — Konstytucja VII) i ma przycisk zamknięcia.

## Typografia (stack bloga)

- **Fonty jak na blogu**: **Geist** (sans, cały UI) · **Instrument Serif** (display — TYLKO
  brand „TaskFlow" w sidebarze i ewentualnie sign-in; nie w UI) · **JetBrains Mono** (mono —
  identyfikatory TSK-…, inline code w komentarzach). Wszystkie **self-hosted przez
  `next/font`** — bez Google Fonts CDN (Konstytucja: CSP + brak zewnętrznych zależności
  runtime).
- **Skala**: 12px meta/etykiety · **13px UI i wiersze list (baza — gęściej niż 16px bloga,
  bo aplikacja, nie proza)** · 14px treść (opisy, komentarze) · 16/18/20px nagłówki. Liczby
  w kolumnach: tabular-nums.
- **Wagi**: 400 body, 500 etykiety/pozycje nav, 600 nagłówki/przyciski. Line-height ~1.5 dla
  treści, ciaśniej (1.3) w gęstych wierszach list.
- Detal z bloga do przeniesienia: akcentowy pasek `border-left: 2-3px accent` dla opisu
  taska / cytatów / bloków kodu.

## Layout i gęstość

- Spacing na siatce **4px**; gęsto: wiersz listy 32–36px, padding kart 12px.
- **Sidebar 240px, zwijany** (przycisk collapse) — nawigacja: Inbox / Today / Upcoming /
  Assigned / projekty, każda pozycja ikona + etykieta + licznik; aktywna pozycja wyraźnie
  podświetlona (accent + waga).
- **Drawer szczegółów taska z prawej, 420–480px** — pełna edycja pól + komentarze. Drawer
  jest **niemodalny**: Esc zamyka i fokus wraca do wywołującego, ale BEZ focus trapa — lista
  pozostaje interaktywna (pełny kontrakt dialogowy z Konstytucji II — initial focus, trap,
  Esc, powrót fokusa — obowiązuje modale: potwierdzenia, palety poleceń).
- Promienie: 6px kontrolki, 8px karty/drawer/menu (web — nie 16px z wariantu mobile).
- Z-index skala: 0 / 10 (sticky) / 20 (sidebar) / 40 (drawer) / 60 (menu, popover) /
  100 (modal) / 1000 (toast).
- Nie przyciemniać `accent-soft` w light-warm — ikona warning na wierszu selected
  przechodzi 3:1 z małym zapasem (3.21:1).
- Hover na powierzchni elevated rozjaśnia tło ⇒ tekst akcentowy/danger na niej idzie
  równolegle w wariant hover (`accent-hover`, `--color-danger-hover` `#D9959C` dark /
  `#844050` light), inaczej dark-cool spada poniżej 4.5:1.
- Desktop + sensowne zachowanie od ~768px wzwyż; bez poziomego scrolla.

## Ikony i grafika

- **Lucide** wszędzie w chrome aplikacji; rozmiar 14–16px (gęste wiersze) / 18–20px
  (nagłówki), stroke 1.5–2 spójnie, jeden styl (outline). Emoji WYŁĄCZNIE jako opcjonalne ikonki projektów.
- Ikony-przyciski zawsze z `aria-label`; hit area ≥ 32px (desktop, wskaźnik precyzyjny)
  mimo mniejszej ikony.
- Awatary: zdjęcie z Google, fallback inicjały w kolorowym kółku (kolor deterministyczny
  z userId).

## Interakcje i motion

- Hover: `cursor-pointer` + zmiana tła (`--bg-hover`) na wszystkim klikalnym; ale ŻADNA
  informacja/akcja nie może być dostępna wyłącznie przez hover (odpowiednik focus/menu).
- Przejścia: **100–150ms ease-out** (wejścia), wyjścia krótsze (~100ms); tylko orientacja
  i potwierdzenie, zero dekoracji; `prefers-reduced-motion` → natychmiastowe (<100ms).
- Optimistic UI maluje wynik od razu (Konstytucja III); skeleton tylko dla prawdziwych
  network-bound loadów; toasty auto-dismiss 3–5s, `aria-live="polite"`, nie kradną fokusa.
- Disabled: opacity ~0.5 + `cursor-not-allowed` + atrybut semantyczny.

## Obsługa przez UI (F1–F3)

- Tworzenie taska: globalny przycisk „+ Nowy task" w górnym pasku ORAZ pole inline w listach.
- Akcje na wierszu: hover-pasek 2–3 najczęstszych (done, edytuj, „⋯") + menu „⋯" ze wszystkimi
  akcjami + drawer jako pełna edycja. Menu „⋯" osiągalne z klawiatury (Tab/Enter).
- Puste stany: hint + przycisk akcji (bez wizardów).
- Hotkeys jednoznakowe usunięte (decyzja G1); zostaje standardowa operowalność klawiaturowa
  WCAG: Tab/Shift+Tab, Enter/Space, Esc, strzałki w menu (mockup demonstruje) oraz w listach
  (roving tabindex — zakres implementacji, nie mockupu).
- Toast: tekst wstrzykiwany do TRWAŁEGO live regionu (`role="status"` istniejącego od
  załadowania strony — toggle `display` z gotową treścią bywa nieodczytywany przez SR);
  po zamknięciu toastu fokus nie może wylądować na elemencie `display:none`.

## Checklist przed oddaniem każdego ekranu (z skilla, dostosowane)

- [ ] zero emoji jako ikon; jeden zestaw Lucide, spójny stroke
- [ ] kontrast: tekst ≥4.5:1, duży tekst/ikony ≥3:1 — zweryfikowane na CIEMNYM tle
- [ ] widoczny focus ring na każdym fokusowalnym elemencie; tab order = visual order
- [ ] hover + active + disabled zdefiniowane; `cursor-pointer` na klikalnych
- [ ] nic dostępnego wyłącznie hoverem; menu i tooltips mają odpowiednik focus/klawiatura
- [ ] `prefers-reduced-motion` respektowane; animacje transform/opacity only
- [ ] semantyczne tokeny — zero surowych hexów w komponentach
- [ ] wiersze list wirtualizowane przy dużych zbiorach (budżet 60fps z konstytucji)

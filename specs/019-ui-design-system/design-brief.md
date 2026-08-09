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

\* w aplikacji (gęste panele, drawer) karty w trybie light mogą używać `#FFFFFF` z borderem —
do rozstrzygnięcia w spec; blog używa `bg-secondary` jako tła kart.

Rozszerzenia TaskFlow (aplikacja potrzebuje więcej niż blog; nazwy w konwencji bloga):
`--color-bg-hover` `rgba(255,255,255,.05)` / `rgba(0,0,0,.045)`, `--color-bg-active`
`rgba(255,255,255,.08)` / `rgba(0,0,0,.08)`, `--color-warning` `#C9B850` (dark) / `#A89640`
(light), `--color-accent-strong` `#356D97` (tło primary button w dark-cool — `#5290BD`
z białym tekstem nie domyka 4.5:1; w light accent-strong = accent). Kandydaci do „common"
przy ekstrakcji.

Zasady: kontrast weryfikowany **osobno w każdej z 4 palet**; kolor funkcjonalny zawsze
z ikoną/tekstem; `::selection` na `accent-soft`; focus ring 2px `accent` + offset; scrim
modali/draweru 40–60% czerni + blur 4px (jak `::backdrop` bloga).

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
- **Drawer szczegółów taska z prawej, 420–480px** — pełna edycja pól + komentarze; Esc zamyka,
  focus trap, powrót fokusa do wywołującego (kontrakt dialogowy z Konstytucji II).
- Promienie: 6px kontrolki, 8px karty/drawer/menu (web — nie 16px z wariantu mobile).
- Z-index skala: 0 / 10 (sticky) / 20 (sidebar) / 40 (drawer) / 100 (modal) / 1000 (toast).
- Desktop + sensowne zachowanie od ~768px wzwyż; bez poziomego scrolla.

## Ikony i grafika

- **Lucide** wszędzie w chrome aplikacji; rozmiar 16px (gęste wiersze) / 18–20px (nagłówki),
  stroke 1.5–2 spójnie, jeden styl (outline). Emoji WYŁĄCZNIE jako opcjonalne ikonki projektów.
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
  WCAG: Tab/Shift+Tab, Enter/Space, Esc, strzałki w listach i menu.

## Checklist przed oddaniem każdego ekranu (z skilla, dostosowane)

- [ ] zero emoji jako ikon; jeden zestaw Lucide, spójny stroke
- [ ] kontrast: tekst ≥4.5:1, duży tekst/ikony ≥3:1 — zweryfikowane na CIEMNYM tle
- [ ] widoczny focus ring na każdym fokusowalnym elemencie; tab order = visual order
- [ ] hover + active + disabled zdefiniowane; `cursor-pointer` na klikalnych
- [ ] nic dostępnego wyłącznie hoverem; menu i tooltips mają odpowiednik focus/klawiatura
- [ ] `prefers-reduced-motion` respektowane; animacje transform/opacity only
- [ ] semantyczne tokeny — zero surowych hexów w komponentach
- [ ] wiersze list wirtualizowane przy dużych zbiorach (budżet 60fps z konstytucji)

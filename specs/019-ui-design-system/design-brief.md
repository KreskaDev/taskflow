# Design Brief: TaskFlow UI Design System (slice 019)

**Źródła**: odpowiedzi z `visual-requirements.md` (decyzje użytkownika mają pierwszeństwo) +
skill `ui-ux-pro-max` (styl "Modern Dark", palety dark/indygo, pairing "Minimal Swiss",
wytyczne UX §1–§9). Ten dokument jest wejściem do `/speckit-specify` — spec może doprecyzować,
ale nie powinien łamać tych tokenów bez powodu.

## Kierunek

Linear-like: ciemny, gęsty, minimalistyczny, profesjonalny. Trzy przymiotniki: **czysty,
gęsty, profesjonalny**. Zakazane: emoji jako ikony systemowe, niskokontrastowe szare-na-szarym,
spinnery zamiast optimistic UI, dekoracyjne animacje.

## Tokeny kolorów (dark, motyw domyślny)

Semantyczne tokeny CSS (Tailwind theme), nigdy surowe hexy w komponentach. Wartości bazowe —
do finalnej walidacji kontrastu WCAG w implementacji:

| Token | Wartość | Uwagi |
|---|---|---|
| `--bg-deep` | `#050506` | tło aplikacji (nie czysta czerń — OLED smear) |
| `--bg-base` | `#0A0A0C` | tło paneli / list |
| `--bg-elevated` | `#141416` | karty, drawer, popovery, menu |
| `--bg-hover` | `rgba(255,255,255,0.05)` | hover wiersza/pozycji |
| `--bg-active` | `rgba(255,255,255,0.08)` | stan pressed / selected |
| `--fg-primary` | `#EDEDEF` | tekst główny (kontrast ≥ 12:1) |
| `--fg-secondary` | `#8A8F98` | tekst drugorzędny (≈5.9:1 na `--bg-base` — AA ✓) |
| `--fg-disabled` | `#5A5F66` | tylko elementy disabled (zwolnione z AA) |
| `--accent` | `#5E6AD2` | indygo Linear — primary buttons, zaznaczenia |
| `--accent-hover` | `#6E79D6` | |
| `--on-accent` | `#FFFFFF` | zweryfikować 4.5:1 na `--accent`; przy zawodzie przyciemnić accent |
| `--focus-ring` | `#5E6AD2` | ring 2px + offset, na KAŻDYM fokusowalnym elemencie |
| `--border` | `rgba(255,255,255,0.08)` | separatory, obrysy kart |
| `--border-strong` | `rgba(255,255,255,0.14)` | inputy, aktywne obrysy |
| `--danger` | `#F87171` (tekst) / `#DC2626` (przycisk destructive) | + ikona/tekst, nigdy sam kolor |
| `--success` | `#4ADE80` | potwierdzenia |
| `--warning` | `#FBBF24` | |

Zasady: stany hover/pressed/disabled rozróżnialne wizualnie; kolor funkcjonalny zawsze z ikoną
lub tekstem; bordery widoczne na każdym tle; scrim modali/draweru 40–60% czerni.

## Typografia

- **Font**: Inter (pairing "Minimal Swiss" — jedna rodzina, hierarchia wagami).
  **Self-hosted przez `next/font`** — bez importu z Google Fonts CDN (Konstytucja: brak
  zewnętrznych zależności runtime + CSP).
- **Skala**: 12px meta/etykiety · **13px UI i wiersze list (baza)** · 14px treść
  (opisy, komentarze) · 16/18/20px nagłówki. Liczby w kolumnach: tabular-nums.
- **Wagi**: 400 body, 500 etykiety/pozycje nav, 600 nagłówki/przyciski. Line-height ~1.5 dla
  treści, ciaśniej (1.3) w gęstych wierszach list.

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

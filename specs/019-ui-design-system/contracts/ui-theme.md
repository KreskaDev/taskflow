# Contract: Theme & Design-System Consumption

**Realizes**: FR-104/FR-105 architecture (Stories 1, 5); normative token values live in
`../design-brief.md` — this contract fixes the *mechanical* interface other slices (010,
013, 014, 017, 018) build against.

## Token source

- Single file: `apps/web/src/app/tokens.css`. It is the ONLY file in the repository
  allowed to contain raw color values (hex/rgb/oklch). Enforced by the hex-audit test
  (UIT-004): `#[0-9A-Fa-f]{3,8}` anywhere else in `apps/web/src` fails CI (exceptions
  require an inline justification comment recognized by the audit).
- Token names 1:1 with the KreskaDev blog (`--color-bg-primary`, `--color-accent`, …)
  plus the documented app extensions and scale tokens (spacing/radii/z-index/type/motion)
  from `design-brief.md`. Extraction to a future shared package = moving this one file.

## Palette switching

- Composite class on `<html>`: `dark-cool` | `dark-warm` | `light-cool` | `light-warm`.
  Exactly one is present; the server renders `class="dark-cool"` (hard default, slice
  018 owns switching/persistence). `:root` re-declares the `dark-cool` set so a missing
  class still renders correctly pre-hydration (S1.2/UIT-001).
- `color-scheme: dark | light` follows the mode half of the class (native controls,
  scrollbars — UIT-005).
- Tests force palettes by swapping the class only — no other hook exists or may be
  introduced (UIT-002/003/007 rely on this).

## Component consumption rules

- Components style via co-located CSS Modules consuming ONLY semantic tokens
  (`var(--color-…)`, scale tokens). No raw colors, no cross-component selector reach-ins.
- Global CSS is limited to `tokens.css` + a minimal `globals.css` base/reset
  (box-sizing, body, `:focus-visible`, `::selection`, `.sr-only`, reduced-motion guard).
- Z-index only from the token scale: sticky 10 / sidebar 20 / drawer 40 / menu-popover
  60 / modal 100 / toast 1000 (UIT-101).
- Interaction-state contracts (hover/active/focus/disabled, per-palette contrast, ARIA)
  per component are enumerated in the spec's Component catalog and `design-brief.md`
  (menus: `role="menu"` + arrow/Home/End + `aria-expanded`; modals: full dialog focus
  contract; drawer: non-modal, Esc-with-field-exception; toasts: persistent
  `role="status"` live region, informational auto-dismiss 3–5 s, undo-capable variant
  persists with explicit close).

## Fonts

- CSS variables `--font-sans` (Geist), `--font-display` (Instrument Serif — brand
  wordmark only), `--font-mono` (JetBrains Mono) are provided by `next/font` in the
  root layout and referenced by `tokens.css`. Self-hosted at build time; `font-src
  'self'` stays intact — no runtime font CDN may be introduced.

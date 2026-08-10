// @vitest-environment node
/**
 * Token contrast matrix (T023, slice 019 — S1.4, UIT-011, D12) [INV-131-adjacent].
 *
 * Parses tokens.css (the single token source) and computes WCAG 2.1 contrast ratios for
 * every normative pairing IN EACH of the four palettes: text ≥4.5:1, non-text/control
 * boundaries ≥3:1 — including the documented trap rows (design-brief.md): white label on
 * accent-strong-hover, light warning #8F7F36, dark selection translucency composited
 * over bg-primary, checkbox outline on fg-disabled, accent/danger text on HOVERED
 * elevated surfaces. A designer edit to tokens.css fails here before it ships.
 */
import { readFileSync } from "node:fs";
import { resolve } from "node:path";
import { describe, expect, it } from "vitest";

const TOKENS_PATH = resolve(__dirname, "../../src/app/tokens.css");
const PALETTES = ["dark-cool", "dark-warm", "light-cool", "light-warm"] as const;
type Palette = (typeof PALETTES)[number];

type Rgb = { r: number; g: number; b: number };
type Rgba = Rgb & { a: number };

function parseColor(raw: string): Rgba {
  const value = raw.trim();
  const hex = /^#([0-9a-f]{6})$/i.exec(value);
  if (hex) {
    const n = parseInt(hex[1]!, 16);
    return { r: (n >> 16) & 255, g: (n >> 8) & 255, b: n & 255, a: 1 };
  }
  const rgba = /^rgba?\(\s*([\d.]+)\s*,\s*([\d.]+)\s*,\s*([\d.]+)\s*(?:,\s*([\d.]+)\s*)?\)$/i.exec(
    value,
  );
  if (rgba) {
    return {
      r: Number(rgba[1]),
      g: Number(rgba[2]),
      b: Number(rgba[3]),
      a: rgba[4] === undefined ? 1 : Number(rgba[4]),
    };
  }
  throw new Error(`Unparseable color value: "${value}"`);
}

/** Alpha-composite `fg` over an OPAQUE `bg`. */
function composite(fg: Rgba, bg: Rgb): Rgb {
  return {
    r: fg.r * fg.a + bg.r * (1 - fg.a),
    g: fg.g * fg.a + bg.g * (1 - fg.a),
    b: fg.b * fg.a + bg.b * (1 - fg.a),
  };
}

function luminance({ r, g, b }: Rgb): number {
  const channel = (c: number) => {
    const s = c / 255;
    return s <= 0.03928 ? s / 12.92 : Math.pow((s + 0.055) / 1.055, 2.4);
  };
  return 0.2126 * channel(r) + 0.7152 * channel(g) + 0.0722 * channel(b);
}

function ratio(a: Rgb, b: Rgb): number {
  const l1 = luminance(a);
  const l2 = luminance(b);
  const [hi, lo] = l1 >= l2 ? [l1, l2] : [l2, l1];
  return (hi + 0.05) / (lo + 0.05);
}

/** Extracts the `--name: value;` custom-property map of one palette block. */
function paletteTokens(css: string, palette: Palette): Map<string, string> {
  const block = new RegExp(
    palette === "dark-cool" ? String.raw`:root,\s*html\.dark-cool\s*\{([\s\S]*?)\}` : String.raw`html\.${palette}\s*\{([\s\S]*?)\}`,
  ).exec(css);
  if (!block) throw new Error(`Palette block not found: ${palette}`);
  const map = new Map<string, string>();
  for (const m of block[1]!.matchAll(/--([\w-]+)\s*:\s*([^;]+);/g)) {
    map.set(m[1]!, m[2]!.trim());
  }
  return map;
}

const css = readFileSync(TOKENS_PATH, "utf8");

/** Scale/avatar tokens live on the shared `:root` scale block (before the palettes). */
const rootScale = paletteTokens(css, "dark-cool");
const scaleBlock = /:root\s*\{([\s\S]*?)\}/.exec(css)![1]!;
const avatarTokens = new Map<string, string>();
for (const m of scaleBlock.matchAll(/--(avatar-[a-z])\s*:\s*([^;]+);/g)) {
  avatarTokens.set(m[1]!, m[2]!.trim());
}

interface Row {
  name: string;
  /** foreground token */
  fg: string;
  /** background token(s): the LAST is the opaque base; earlier layers composite over it. */
  bgLayers: string[];
  min: 4.5 | 3;
}

/** Normative pairings — the design's actual token usages + the trap rows. */
const ROWS: Row[] = [
  // Core text on every surface tier
  { name: "text-primary on bg-primary", fg: "color-text-primary", bgLayers: ["color-bg-primary"], min: 4.5 },
  { name: "text-primary on bg-elevated", fg: "color-text-primary", bgLayers: ["color-bg-elevated"], min: 4.5 },
  { name: "text-primary on bg-deep", fg: "color-text-primary", bgLayers: ["color-bg-deep"], min: 4.5 },
  { name: "text-secondary on bg-primary", fg: "color-text-secondary", bgLayers: ["color-bg-primary"], min: 4.5 },
  { name: "text-secondary on bg-elevated", fg: "color-text-secondary", bgLayers: ["color-bg-elevated"], min: 4.5 },
  { name: "text-secondary on bg-deep", fg: "color-text-secondary", bgLayers: ["color-bg-deep"], min: 4.5 },
  // Tertiary = the DISABLED/decorative tier (the approved mockup exposes only
  // fg-primary/fg-secondary/fg-disabled — tertiary ≡ fg-disabled, ~4.2:1 by design).
  // Meaningful meta text MUST use text-secondary; disabled text is WCAG-exempt from
  // 4.5:1, so the normative bar here is the 3:1 non-text/boundary threshold.
  { name: "text-tertiary (disabled tier) on bg-primary", fg: "color-text-tertiary", bgLayers: ["color-bg-primary"], min: 3 },

  // Buttons — incl. the accent-strong-hover trap (white label must survive hover)
  { name: "on-accent on accent-strong", fg: "color-on-accent", bgLayers: ["color-accent-strong"], min: 4.5 },
  { name: "on-accent on accent-strong-hover (TRAP)", fg: "color-on-accent", bgLayers: ["color-accent-strong-hover"], min: 4.5 },
  { name: "on-accent on danger-strong", fg: "color-on-accent", bgLayers: ["color-danger-strong"], min: 4.5 },
  { name: "on-accent on danger-strong-hover", fg: "color-on-accent", bgLayers: ["color-danger-strong-hover"], min: 4.5 },

  // Accent / danger text on plain and HOVERED elevated surfaces (design-brief trap:
  // the hover variant must be used on hovered surfaces — these rows pin BOTH sides)
  { name: "accent text on bg-primary", fg: "color-accent", bgLayers: ["color-bg-primary"], min: 4.5 },
  { name: "accent text on bg-elevated", fg: "color-accent", bgLayers: ["color-bg-elevated"], min: 4.5 },
  { name: "accent-hover text on hovered elevated (TRAP)", fg: "color-accent-hover", bgLayers: ["color-bg-hover", "color-bg-elevated"], min: 4.5 },
  { name: "danger text on bg-primary", fg: "color-danger", bgLayers: ["color-bg-primary"], min: 4.5 },
  { name: "danger text on bg-elevated", fg: "color-danger", bgLayers: ["color-bg-elevated"], min: 4.5 },
  { name: "danger-hover text on hovered elevated (TRAP)", fg: "color-danger-hover", bgLayers: ["color-bg-hover", "color-bg-elevated"], min: 4.5 },

  // Selection — dark uses translucent accent BECAUSE accent-soft is invisible there;
  // text must stay readable on the composited selection surface
  { name: "text-primary on selection over bg-primary (TRAP)", fg: "color-text-primary", bgLayers: ["color-selection", "color-bg-primary"], min: 4.5 },

  // Success/status colors as text
  { name: "green text on bg-primary", fg: "color-green", bgLayers: ["color-bg-primary"], min: 4.5 },

  // Non-text (≥3:1): focus ring, control boundaries, warning icons
  { name: "focus ring (accent) on bg-primary", fg: "color-accent", bgLayers: ["color-bg-primary"], min: 3 },
  { name: "focus ring (accent) on bg-deep", fg: "color-accent", bgLayers: ["color-bg-deep"], min: 3 },
  { name: "focus ring (accent) on bg-elevated", fg: "color-accent", bgLayers: ["color-bg-elevated"], min: 3 },
  { name: "checkbox outline (fg-disabled) on bg-primary (TRAP)", fg: "color-fg-disabled", bgLayers: ["color-bg-primary"], min: 3 },
  { name: "checkbox outline (fg-disabled) on bg-elevated (TRAP)", fg: "color-fg-disabled", bgLayers: ["color-bg-elevated"], min: 3 },
  { name: "warning icon on bg-primary (TRAP: light #8F7F36)", fg: "color-warning", bgLayers: ["color-bg-primary"], min: 3 },
  { name: "warning icon on bg-elevated", fg: "color-warning", bgLayers: ["color-bg-elevated"], min: 3 },
];

describe.each(PALETTES)("token contrast matrix — %s (S1.4, ×4 palettes)", (palette) => {
  const tokens = paletteTokens(css, palette);

  function resolveToken(name: string): Rgba {
    const raw = tokens.get(name) ?? rootScale.get(name);
    if (!raw) throw new Error(`Token --${name} missing in ${palette}`);
    return parseColor(raw);
  }

  /** Composite the layer stack down to an opaque background. */
  function resolveBackground(layers: string[]): Rgb {
    const base = resolveToken(layers[layers.length - 1]!);
    if (base.a !== 1) throw new Error(`Base layer must be opaque: ${layers[layers.length - 1]!}`);
    let bg: Rgb = base;
    for (let i = layers.length - 2; i >= 0; i--) {
      bg = composite(resolveToken(layers[i]!), bg);
    }
    return bg;
  }

  it.each(ROWS)("$name ≥ $min:1", ({ fg, bgLayers, min }) => {
    const background = resolveBackground(bgLayers);
    const foreground = composite(resolveToken(fg), background);
    const r = ratio(foreground, background);
    expect(r, `ratio ${r.toFixed(2)}:1 in ${palette}`).toBeGreaterThanOrEqual(min);
  });

  it("every palette declares the COMPLETE color token set (no fallthrough to :root)", () => {
    const reference = paletteTokens(css, "dark-cool");
    for (const key of reference.keys()) {
      if (key.startsWith("color-")) {
        expect(tokens.has(key), `--${key} missing in ${palette}`).toBe(true);
      }
    }
  });
});

describe("avatar palette (FR-105 — AA-safe for white initials in every context)", () => {
  it.each([...avatarTokens.entries()])("white initials on --%s ≥ 4.5:1", (_name, value) => {
    const bg = parseColor(value);
    expect(bg.a).toBe(1);
    const r = ratio({ r: 255, g: 255, b: 255 }, bg);
    expect(r, `ratio ${r.toFixed(2)}:1`).toBeGreaterThanOrEqual(4.5);
  });
});

// @vitest-environment node
/**
 * Raw-color (hex) audit (T024, slice 019 — UIT-004, S5.6, contracts/ui-theme.md).
 *
 * tokens.css is the ONLY file allowed to contain raw color values. This test greps
 * `#[0-9A-Fa-f]{3,8}` across apps/web/src/** (TS/TSX/CSS) and fails on any match
 * outside it. Recognized exemptions:
 *   1. globals.css lines BELOW the `LEGACY — delete in T064` marker (T005's retained
 *      pre-redesign rules; T064 deletes the block AND this exemption together);
 *   2. an inline `hex-audit-exempt:` justification comment on the same line
 *      (contracts/ui-theme.md — currently unused; the LEGACY block is the only
 *      allowed exemption until T064).
 */
import { readdirSync, readFileSync, statSync } from "node:fs";
import { join, relative, resolve } from "node:path";
import { describe, expect, it } from "vitest";

const SRC_DIR = resolve(__dirname, "../../src");
const TOKENS_FILE = resolve(SRC_DIR, "app/tokens.css");
const GLOBALS_FILE = resolve(SRC_DIR, "app/globals.css");
const LEGACY_MARKER = "LEGACY — delete in T064";

/** Hex color literal. The trailing boundary keeps 9+ hex-char ids (UUID chunks in
 * comments) out; CSS ids/urls do not appear in source. */
const HEX_PATTERN = /#[0-9A-Fa-f]{3,8}\b/g;

function walk(dir: string): string[] {
  const files: string[] = [];
  for (const entry of readdirSync(dir)) {
    const full = join(dir, entry);
    if (statSync(full).isDirectory()) {
      // The generated client is not authored UI code (and contains no colors anyway).
      if (entry === "generated") continue;
      files.push(...walk(full));
    } else if (/\.(ts|tsx|css)$/.test(entry)) {
      files.push(full);
    }
  }
  return files;
}

describe("hex audit — zero raw colors outside tokens.css (UIT-004)", () => {
  it("no source or stylesheet outside tokens.css contains a raw hex color", () => {
    const offenders: string[] = [];

    for (const file of walk(SRC_DIR)) {
      if (file === TOKENS_FILE) continue;

      const lines = readFileSync(file, "utf8").split(/\r?\n/);
      let inLegacyBlock = false;

      lines.forEach((line, index) => {
        if (file === GLOBALS_FILE && line.includes(LEGACY_MARKER)) {
          inLegacyBlock = true; // runs to EOF — T064 deletes the whole tail
        }
        if (inLegacyBlock) return;
        if (line.includes("hex-audit-exempt:")) return;

        const matches = line.match(HEX_PATTERN);
        if (matches) {
          offenders.push(
            `${relative(SRC_DIR, file)}:${index + 1} → ${matches.join(", ")}`,
          );
        }
      });
    }

    expect(offenders, `Raw hex colors outside tokens.css:\n${offenders.join("\n")}`).toEqual([]);
  });
});

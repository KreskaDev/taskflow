// @vitest-environment node
/**
 * Inventory-coverage gate (T020, slice 019 — S2.2/S2.4, D10).
 *
 * Parses `specs/019-ui-design-system/feature-inventory.md` for every stable `INV-###`
 * row and scans ALL web test sources (unit + e2e) for `[INV-###]` title tags. Any
 * inventory entry without at least one covering tag FAILS this test — which runs under
 * `pnpm --dir apps/web test`, so the `web-quality` CI job carries the regression grid
 * on every future merge (the grid is a living artifact — Constitution VIII).
 */
import { readdirSync, readFileSync, statSync } from "node:fs";
import { join, resolve } from "node:path";
import { describe, expect, it } from "vitest";

const WEB_DIR = resolve(__dirname, "../..");
const INVENTORY_PATH = resolve(WEB_DIR, "../../specs/019-ui-design-system/feature-inventory.md");
const TEST_ROOTS = [resolve(WEB_DIR, "tests/unit"), resolve(WEB_DIR, "tests/e2e")];

function listTestSources(dir: string): string[] {
  const entries = readdirSync(dir);
  const files: string[] = [];
  for (const entry of entries) {
    const full = join(dir, entry);
    if (statSync(full).isDirectory()) {
      files.push(...listTestSources(full));
    } else if (/\.(ts|tsx)$/.test(entry)) {
      files.push(full);
    }
  }
  return files;
}

describe("regression inventory coverage (S2.2)", () => {
  const inventory = readFileSync(INVENTORY_PATH, "utf8");

  // Inventory rows: table lines beginning with `| INV-###` (the Transfer-notes section
  // references UIT ids, never INV table rows, so this shape is unambiguous).
  const declared = [...inventory.matchAll(/^\| (INV-\d{3}) \|/gmu)].map((m) => m[1]!);

  it("the inventory declares a non-trivial set of stable rows", () => {
    expect(declared.length).toBeGreaterThan(50);
    expect(new Set(declared).size).toBe(declared.length); // ids are unique
  });

  it("every INV-### row has at least one [INV-###]-tagged covering test", () => {
    const sources = TEST_ROOTS.flatMap(listTestSources)
      .map((file) => readFileSync(file, "utf8"))
      .join("\n");

    const covered = new Set([...sources.matchAll(/\[(INV-\d{3})\]/gu)].map((m) => m[1]!));
    const uncovered = declared.filter((id) => !covered.has(id));

    expect(
      uncovered,
      `Uncovered inventory entries (add an [INV-###]-tagged test for each):\n${uncovered.join(", ")}`,
    ).toEqual([]);
  });

  it("no test tag references a nonexistent inventory row (tag typo guard)", () => {
    const sources = TEST_ROOTS.flatMap(listTestSources)
      .map((file) => readFileSync(file, "utf8"))
      .join("\n");
    const declaredSet = new Set(declared);
    const unknown = [...sources.matchAll(/\[(INV-\d{3})\]/gu)]
      .map((m) => m[1]!)
      .filter((id) => !declaredSet.has(id));
    expect([...new Set(unknown)]).toEqual([]);
  });
});

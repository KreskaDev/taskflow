import { describe, expect, it } from "vitest";

import { filterTasks, filterTokens, isFilterActive, normalizeForFilter } from "@/lib/filter";

/**
 * The view filter pure module (slice 013, T016 — contracts/ui-palette.md §View filter): diacritic- and
 * case-insensitive token-AND over title + description; a blank filter is the identity. [INV-198]
 */

const rows = [
  { id: "1", title: "Środa — spotkanie", description: null },
  { id: "2", title: "Kup mleko", description: "pamiętaj o laktozie" },
  { id: "3", title: "Łódź wyjazd", description: "bilety" },
];

describe("normalizeForFilter", () => {
  it("lower-cases and strips Polish diacritics incl. ł", () => {
    expect(normalizeForFilter("Środa Łódź ŻÓŁĆ")).toBe("sroda lodz zolc");
  });
});

describe("filterTokens", () => {
  it("splits on whitespace, normalizes and drops blanks", () => {
    expect(filterTokens("  Kup   MLEKO ")).toEqual(["kup", "mleko"]);
    expect(filterTokens("   ")).toEqual([]);
  });
});

describe("filterTasks [INV-198]", () => {
  it("a blank filter returns the SAME array (identity, no re-render churn)", () => {
    expect(filterTasks(rows, "")).toBe(rows);
    expect(isFilterActive("")).toBe(false);
  });

  it("matches diacritic-insensitively: „sro” keeps „Środa”", () => {
    expect(filterTasks(rows, "sro").map((r) => r.id)).toEqual(["1"]);
    expect(filterTasks(rows, "lodz").map((r) => r.id)).toEqual(["3"]);
  });

  it("is case-insensitive and token-AND over title + description", () => {
    expect(filterTasks(rows, "KUP mleko").map((r) => r.id)).toEqual(["2"]);
    expect(filterTasks(rows, "mleko laktoz").map((r) => r.id)).toEqual(["2"]); // description counts
    expect(filterTasks(rows, "mleko bilety")).toEqual([]);
  });

  it("preserves the input order", () => {
    expect(filterTasks(rows, "a").map((r) => r.id)).toEqual(["1", "2", "3"]);
  });
});

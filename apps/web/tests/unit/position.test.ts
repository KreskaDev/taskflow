// @vitest-environment node
import { describe, expect, it } from "vitest";
import { between } from "@/lib/position";

/**
 * Fractional rank helper (T022/T023, FR-102, data-model.md "Reorder / `position` strategy").
 *
 * The list is sorted ascending by `position` under byte/code-unit ordering
 * (Postgres COLLATE "C" on the server; JS string `<` on the client — these
 * coincide for the pinned alphabet). Top-is-lowest: the newest-first seed
 * prepends, so a new task's rank is `between(null, head)` and must sort BEFORE
 * `head`. The pinned alphabet is the standard fractional-indexing base-62 set
 * (`[0-9A-Za-z]`), identical on client and server.
 */
const RANK = /^[0-9A-Za-z]+$/;

describe("position.between", () => {
  it("between(null, null) returns a valid rank for the empty-list first task", () => {
    const seed = between(null, null);
    expect(seed).toMatch(RANK);
  });

  it("between(null, head) sorts strictly BEFORE head (newest-first prepend)", () => {
    const head = between(null, null);
    const prepended = between(null, head);
    expect(prepended).toMatch(RANK);
    // Code-unit order: the prepended rank must come first so the new task is at the top.
    expect(prepended < head).toBe(true);
  });

  it("between(a, b) is strictly between a and b under code-unit ordering", () => {
    const a = between(null, null);
    const b = between(a, null);
    const mid = between(a, b);
    expect(mid).toMatch(RANK);
    expect(a < mid).toBe(true);
    expect(mid < b).toBe(true);
  });

  it("is monotonic: repeated prepends stay strictly descending and ordered", () => {
    const ranks: string[] = [];
    let head: string | null = null;
    for (let i = 0; i < 50; i++) {
      const next = between(null, head);
      expect(next).toMatch(RANK);
      ranks.push(next);
      head = next;
    }
    // Each prepend sorts before the previous head — strictly descending in insertion order.
    for (let i = 1; i < ranks.length; i++) {
      const curr = ranks[i];
      const prev = ranks[i - 1];
      expect(curr !== undefined && prev !== undefined && curr < prev).toBe(true);
    }
    // Sorting ascending yields the reverse of insertion order with no collisions.
    const sorted = [...ranks].sort();
    expect(sorted).toEqual([...ranks].reverse());
    expect(new Set(ranks).size).toBe(ranks.length);
  });

  it("between repeated insertions in the same gap stays strictly ordered (no exhaustion)", () => {
    let lo = between(null, null);
    const hi = between(lo, null);
    let prev = lo;
    for (let i = 0; i < 50; i++) {
      const mid = between(lo, hi);
      expect(mid).toMatch(RANK);
      expect(lo < mid).toBe(true);
      expect(mid < hi).toBe(true);
      expect(prev < mid).toBe(true);
      prev = mid;
      lo = mid;
    }
  });
});

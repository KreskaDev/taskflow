import { BASE_62_DIGITS, generateKeyBetween } from "fractional-indexing";

/**
 * Fractional rank ("position") helper (FR-102, data-model.md "Reorder / `position` strategy").
 *
 * The task list is sorted ascending by `position` under byte/code-unit ordering —
 * `ORDER BY position, id` with Postgres `COLLATE "C"` on the server, and JS string
 * comparison on the client. Top-is-lowest: the newest-first seed prepends, so a new
 * task's rank is `between(null, head)` (which sorts BEFORE `head`), and a reorder is
 * `between(left, right)` over its in-memory neighbours. Writing one row is O(1) — no
 * whole-list renumber, and a rank string has no exhaustion ceiling (it grows a character).
 *
 * Alphabet — **pinned identically on client and server** to {@link BASE_62_DIGITS}
 * (`0-9A-Za-z`, ascending charcode order). This is the standard fractional-indexing
 * alphabet; the library's integer-part header structurally requires the uppercase range
 * (e.g. `between(null, "a0")` → `"Zz"`), so a lowercase-only alphabet is not possible.
 * Because the alphabet is in ascending charcode order it coincides exactly with the
 * server's byte-ordinal `COLLATE "C"`, keeping client and server order identical. The
 * client is the sole rank generator; the server validates the format (`[0-9A-Za-z]+`)
 * and is the sole writer under the `version` guard, but never generates ranks.
 */
export const POSITION_ALPHABET = BASE_62_DIGITS;

/**
 * Computes a fractional rank strictly between `left` and `right` under code-unit ordering.
 *
 * Either neighbour may be `null`: `between(null, null)` seeds the empty list, `between(null, head)`
 * prepends (newest-first), and `between(tail, null)` appends. The result is strictly greater than
 * `left` (when non-null) and strictly less than `right` (when non-null), and is monotonic — distinct
 * calls in distinct gaps never collide.
 *
 * @param left  The lower neighbour's rank, or `null` for the start of the list.
 * @param right The upper neighbour's rank, or `null` for the end of the list.
 * @returns A new rank string drawn from {@link POSITION_ALPHABET}.
 */
export function between(left: string | null, right: string | null): string {
  return generateKeyBetween(left, right, POSITION_ALPHABET);
}

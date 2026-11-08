/**
 * The view filter (slice 013, US-08.AS-08 reinterpreted — contracts/ui-palette.md, research D11): a
 * client-side narrowing of the rows a list view already holds. Pure and synchronous; the hosts pass the
 * filtered rows into their existing grouping/columns so empty groups collapse as they do today.
 */

/** Lower-cases and strips diacritics (NFD → remove combining marks): „Środa" → "sroda", „Łódź" → "lodz". */
export function normalizeForFilter(text: string): string {
  return text
    .normalize("NFD")
    .replace(/[̀-ͯ]/g, "")
    .replace(/ł/g, "l")
    .replace(/Ł/g, "L")
    .toLowerCase();
}

/** Whitespace tokens of a filter string, normalized; empty for a blank filter. */
export function filterTokens(filter: string): string[] {
  return filter
    .split(/\s+/)
    .map((t) => normalizeForFilter(t.trim()))
    .filter((t) => t.length > 0);
}

interface FilterableTask {
  title: string;
  description?: string | null;
}

/**
 * Keeps the rows whose title (or description) contains EVERY token of `filter`, case- and
 * diacritic-insensitively; a blank filter returns the input array unchanged (same reference — no re-render churn).
 */
export function filterTasks<T extends FilterableTask>(rows: readonly T[], filter: string): T[] {
  const tokens = filterTokens(filter);
  if (tokens.length === 0) return rows as T[];
  return rows.filter((row) => {
    const haystack = normalizeForFilter(`${row.title} ${row.description ?? ""}`);
    return tokens.every((token) => haystack.includes(token));
  });
}

/** True when a non-blank filter is active. */
export function isFilterActive(filter: string): boolean {
  return filterTokens(filter).length > 0;
}

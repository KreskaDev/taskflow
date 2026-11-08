// @vitest-environment node
import { QueryClient } from "@tanstack/react-query";
import { describe, expect, it, vi } from "vitest";

import { invalidateSearch, SEARCH_QUERY_KEY, searchQueryKey } from "@/hooks/useSearch";

/**
 * The search query's key shape and the access-loss invalidation helper (slice 013, T010 — research D9/D10).
 * [INV-197]
 */
describe("useSearch keys + invalidation", () => {
  it("keys by the normalized (trimmed, case-folded) query under the ['search'] family", () => {
    expect(searchQueryKey("  Mleko ")).toEqual(["search", "mleko"]);
    expect(searchQueryKey("MLEKO")).toEqual(searchQueryKey("mleko"));
  });

  it("invalidateSearch drops the whole family", async () => {
    const queryClient = new QueryClient();
    queryClient.setQueryData(searchQueryKey("a"), { tasks: [], projects: [], labels: [] });
    queryClient.setQueryData(searchQueryKey("b"), { tasks: [], projects: [], labels: [] });
    const spy = vi.spyOn(queryClient, "invalidateQueries");

    await invalidateSearch(queryClient);

    expect(spy).toHaveBeenCalledWith({ queryKey: SEARCH_QUERY_KEY });
    expect(queryClient.getQueryState(searchQueryKey("a"))?.isInvalidated).toBe(true);
    expect(queryClient.getQueryState(searchQueryKey("b"))?.isInvalidated).toBe(true);
  });
});

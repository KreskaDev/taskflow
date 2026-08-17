// @vitest-environment node
import { QueryClient } from "@tanstack/react-query";
import { beforeEach, describe, expect, it, vi } from "vitest";

import type { components } from "@/lib/api/generated/schema";
import {
  activateCycleMutationOptions,
  closeCycleAnnouncement,
  closeCycleMutationOptions,
  createCycleMutationOptions,
  CYCLES_QUERY_KEY,
  cycleTasksQueryKey,
  deleteCycleMutationOptions,
  editCycleMutationOptions,
} from "@/hooks/useCycles";

type CycleResponse = components["schemas"]["CycleResponse"];

vi.mock("@/lib/api/client", async (importOriginal) => {
  const actual = await importOriginal<typeof import("@/lib/api/client")>();
  return {
    ...actual,
    apiClient: { GET: vi.fn(), PUT: vi.fn(), PATCH: vi.fn(), DELETE: vi.fn() },
  };
});

const { apiClient } = await import("@/lib/api/client");
const patchSpy = apiClient.PATCH as unknown as ReturnType<typeof vi.fn>;
const deleteSpy = apiClient.DELETE as unknown as ReturnType<typeof vi.fn>;

function cycle(overrides: Partial<CycleResponse> & Pick<CycleResponse, "id">): CycleResponse {
  return {
    id: overrides.id,
    name: overrides.name ?? "Cykl",
    startDate: overrides.startDate ?? "2026-01-05T00:00:00Z",
    endDate: overrides.endDate ?? "2026-01-19T00:00:00Z",
    status: overrides.status ?? "planned",
    version: overrides.version ?? 0,
    createdAt: overrides.createdAt ?? "2026-01-01T00:00:00Z",
    metrics: overrides.metrics ?? {
      total: 0,
      done: 0,
      breakdown: { backlog: 0, todo: 0, in_progress: 0, done: 0, cancelled: 0 },
    },
  };
}

let queryClient: QueryClient;
beforeEach(() => {
  vi.clearAllMocks();
  queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
});

/**
 * The slice-011 cycle lifecycle recipes (T015): optimistic ['cycles'] patch → rollback on error →
 * settle invalidations. Tested as pure option factories (no React render), the repo convention.
 */
describe("createCycleMutationOptions — optimistic D5-ordered insert [INV-165]", () => {
  it("inserts the planned placeholder in D5 order and rolls back on error", async () => {
    const existing = cycle({ id: "b", startDate: "2026-02-01T00:00:00Z" });
    queryClient.setQueryData(CYCLES_QUERY_KEY, [existing]);
    const options = createCycleMutationOptions(queryClient);

    const context = await options.onMutate({
      id: "a",
      name: "Nowy",
      startDate: "2026-01-05T00:00:00Z",
      endDate: "2026-01-19T00:00:00Z",
    });

    const optimistic = queryClient.getQueryData<CycleResponse[]>(CYCLES_QUERY_KEY)!;
    expect(optimistic.map((c) => c.id), "earlier start sorts first (D5)").toEqual(["a", "b"]);
    expect(optimistic[0]).toMatchObject({ status: "planned", version: 0, metrics: { total: 0 } });

    options.onError(new Error("boom"), { id: "a", name: "Nowy", startDate: "", endDate: "" }, context);
    expect(queryClient.getQueryData<CycleResponse[]>(CYCLES_QUERY_KEY)).toEqual([existing]);
  });
});

describe("editCycleMutationOptions", () => {
  it("re-stamps name/dates optimistically and re-sorts by the new start date", async () => {
    queryClient.setQueryData(CYCLES_QUERY_KEY, [
      cycle({ id: "a", name: "A", startDate: "2026-01-05T00:00:00Z" }),
      cycle({ id: "b", name: "B", startDate: "2026-02-01T00:00:00Z" }),
    ]);
    const options = editCycleMutationOptions(queryClient);

    await options.onMutate({
      id: "a",
      name: "A po zmianie",
      startDate: "2026-03-01T00:00:00Z",
      endDate: "2026-03-15T00:00:00Z",
      version: 0,
    });

    const list = queryClient.getQueryData<CycleResponse[]>(CYCLES_QUERY_KEY)!;
    expect(list.map((c) => c.id), "the moved start re-sorts the list").toEqual(["b", "a"]);
    expect(list[1]!.name).toBe("A po zmianie");
  });
});

describe("activateCycleMutationOptions [INV-167]", () => {
  it("flips the target to active optimistically and rolls back when the server refuses", async () => {
    const planned = cycle({ id: "a", status: "planned" });
    queryClient.setQueryData(CYCLES_QUERY_KEY, [planned]);
    const options = activateCycleMutationOptions(queryClient);

    const context = await options.onMutate({ id: "a", version: 0 });
    expect(queryClient.getQueryData<CycleResponse[]>(CYCLES_QUERY_KEY)![0]!.status).toBe("active");

    // e.g. 409 cycle_active_conflict — the single-active refusal restores the snapshot.
    options.onError(new Error("Inny cykl jest już aktywny — najpierw go zamknij."), { id: "a", version: 0 }, context);
    expect(queryClient.getQueryData<CycleResponse[]>(CYCLES_QUERY_KEY)![0]!.status).toBe("planned");
  });

  it("issues exactly one PATCH to /activate", async () => {
    patchSpy.mockResolvedValueOnce({ data: cycle({ id: "a", status: "active", version: 1 }), error: undefined });
    const options = activateCycleMutationOptions(queryClient);

    await options.mutationFn({ id: "a", version: 0 });

    expect(patchSpy).toHaveBeenCalledTimes(1);
    expect(patchSpy).toHaveBeenCalledWith("/api/cycles/{id}/activate", {
      params: { path: { id: "a" } },
      body: { version: 0 },
    });
  });
});

describe("closeCycleMutationOptions — the D4 one-command review commit [INV-168]", () => {
  it("sends the bulk choice + overrides in ONE PATCH and surfaces the counts", async () => {
    const closed = cycle({ id: "a", status: "closed", version: 2 });
    patchSpy.mockResolvedValueOnce({
      data: { cycle: closed, rolledToNext: 3, rolledToBacklog: 1, kept: 2 },
      error: undefined,
    });
    const options = closeCycleMutationOptions(queryClient);

    const result = await options.mutationFn({
      id: "a",
      rollover: "next",
      overrides: [{ taskId: "t1", choice: "backlog" }],
      version: 1,
    });

    expect(patchSpy).toHaveBeenCalledTimes(1);
    expect(patchSpy).toHaveBeenCalledWith("/api/cycles/{id}/close", {
      params: { path: { id: "a" } },
      body: { rollover: "next", overrides: [{ taskId: "t1", choice: "backlog" }], version: 1 },
    });
    expect(result.rolledToNext).toBe(3);
  });

  it("marks the cycle closed optimistically and rolls back on no_next_cycle [INV-169]", async () => {
    queryClient.setQueryData(CYCLES_QUERY_KEY, [cycle({ id: "a", status: "active" })]);
    const options = closeCycleMutationOptions(queryClient);

    const context = await options.onMutate({ id: "a", rollover: "next", version: 1 });
    expect(queryClient.getQueryData<CycleResponse[]>(CYCLES_QUERY_KEY)![0]!.status).toBe("closed");

    options.onError(new Error("Brak następnego cyklu — najpierw utwórz nowy cykl."), { id: "a", version: 1 }, context);
    expect(
      queryClient.getQueryData<CycleResponse[]>(CYCLES_QUERY_KEY)![0]!.status,
      "AS-06: nothing closes when no planned cycle exists",
    ).toBe("active");
  });
});

describe("closeCycleAnnouncement — the FR-101 counts copy [INV-168]", () => {
  const closed = cycle({ id: "a", status: "closed" });

  it("names each non-zero count in Polish", () => {
    expect(closeCycleAnnouncement({ cycle: closed, rolledToNext: 3, rolledToBacklog: 1, kept: 2 })).toBe(
      "Cykl zamknięty — przeniesione do następnego cyklu: 3, przeniesione do backlogu: 1, pozostawione jako „przeniesione”: 2.",
    );
  });

  it("degrades to the pure-close copy at zero counts", () => {
    expect(closeCycleAnnouncement({ cycle: closed, rolledToNext: 0, rolledToBacklog: 0, kept: 0 })).toBe(
      "Cykl zamknięty.",
    );
  });
});

describe("deleteCycleMutationOptions [INV-170]", () => {
  it("removes the row optimistically, carries the OCC version in the query, and rolls back a refusal", async () => {
    const active = cycle({ id: "a", status: "active", version: 1 });
    queryClient.setQueryData(CYCLES_QUERY_KEY, [active]);
    const options = deleteCycleMutationOptions(queryClient);

    const context = await options.onMutate({ id: "a", version: 1 });
    expect(queryClient.getQueryData<CycleResponse[]>(CYCLES_QUERY_KEY)).toEqual([]);

    // 422 cycle_active_delete_forbidden — the AS-07 refusal restores the row.
    options.onError(new Error("Cyklu nie można usunąć — najpierw go zamknij."), { id: "a", version: 1 }, context);
    expect(queryClient.getQueryData<CycleResponse[]>(CYCLES_QUERY_KEY)).toEqual([active]);

    deleteSpy.mockResolvedValueOnce({ error: undefined });
    await options.mutationFn({ id: "a", version: 1 });
    expect(deleteSpy).toHaveBeenCalledTimes(1);
    expect(deleteSpy).toHaveBeenCalledWith("/api/cycles/{id}", {
      params: { path: { id: "a" }, query: { version: 1 } },
    });
  });
});

describe("cycleTasksQueryKey", () => {
  it("is the per-cycle ['cycle-tasks', id] key the prefix invalidations cover", () => {
    expect(cycleTasksQueryKey("c1")).toEqual(["cycle-tasks", "c1"]);
  });
});

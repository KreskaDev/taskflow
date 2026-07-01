// @vitest-environment node
import { QueryClient } from "@tanstack/react-query";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import {
  createLabelMutationOptions,
  deleteLabelMutationOptions,
  LABELS_QUERY_KEY,
  type LabelResponse,
} from "@/hooks/useLabels";
import { TASKS_QUERY_KEY } from "@/hooks/useTasks";
import { TODAY_QUERY_KEY } from "@/hooks/useTodayTasks";
import { UPCOMING_QUERY_KEY } from "@/hooks/useUpcomingTasks";

/**
 * The optimistic label create/delete recipes (slice 006). Pins the cache reconciliation in particular: a
 * label DELETE cascades server-side to every task application, so onSettled must invalidate EVERY task cache
 * that can hold the label — the Inbox (`['tasks']`, prefix-covering `['tasks','assigned']`) AND the separate
 * grouped Today/Upcoming caches — else a deleted id lingers in those caches and a later setTaskLabels
 * re-submits it (server 422). This is the regression guard for that bug.
 */
vi.mock("@/lib/api/client", async (importOriginal) => {
  const actual = await importOriginal<typeof import("@/lib/api/client")>();
  return {
    ...actual,
    apiClient: { GET: vi.fn(), PUT: vi.fn(), PATCH: vi.fn(), DELETE: vi.fn() },
  };
});

const { apiClient } = await import("@/lib/api/client");
const putSpy = apiClient.PUT as unknown as ReturnType<typeof vi.fn>;
const deleteSpy = apiClient.DELETE as unknown as ReturnType<typeof vi.fn>;

const LABEL_ID = "11111111-1111-7111-8111-111111111111";

beforeEach(() => {
  putSpy.mockReset();
  deleteSpy.mockReset();
});

afterEach(() => {
  vi.restoreAllMocks();
});

describe("createLabel — PUT /api/labels/{id}, optimistic roster insert", () => {
  it("mutationFn PUTs the client id with the {name, color} body", async () => {
    putSpy.mockResolvedValue({ data: { id: LABEL_ID, name: "Urgent", color: "red" }, error: undefined });
    await createLabelMutationOptions(new QueryClient()).mutationFn({ id: LABEL_ID, name: "Urgent", color: "red" });
    const [path, init] = putSpy.mock.calls[0]!;
    expect(path).toBe("/api/labels/{id}");
    expect((init as { params: { path: { id: string } } }).params.path.id).toBe(LABEL_ID);
    expect((init as { body: Record<string, unknown> }).body).toEqual({ name: "Urgent", color: "red" });
  });

  it("onMutate inserts the placeholder (same client id) into the roster, sorted by name; onError rolls back", async () => {
    const qc = new QueryClient();
    qc.setQueryData<LabelResponse[]>(LABELS_QUERY_KEY, [{ id: "z", name: "Work", color: null }]);
    const opts = createLabelMutationOptions(qc);

    const context = await opts.onMutate({ id: LABEL_ID, name: "Admin", color: null });
    const after = qc.getQueryData<LabelResponse[]>(LABELS_QUERY_KEY)!;
    expect(after.map((l) => l.name)).toEqual(["Admin", "Work"]); // localeCompare sort
    expect(after.find((l) => l.id === LABEL_ID)).toBeTruthy();

    opts.onError(new Error("boom"), { id: LABEL_ID, name: "Admin", color: null }, context);
    expect(qc.getQueryData<LabelResponse[]>(LABELS_QUERY_KEY)).toEqual([{ id: "z", name: "Work", color: null }]);
  });
});

describe("deleteLabel — DELETE /api/labels/{id}, cascade-aware settle", () => {
  it("onMutate removes the label from the roster; onError restores it", async () => {
    const qc = new QueryClient();
    qc.setQueryData<LabelResponse[]>(LABELS_QUERY_KEY, [
      { id: LABEL_ID, name: "Urgent", color: "red" },
      { id: "other", name: "Work", color: null },
    ]);
    const opts = deleteLabelMutationOptions(qc);

    const context = await opts.onMutate({ id: LABEL_ID });
    expect(qc.getQueryData<LabelResponse[]>(LABELS_QUERY_KEY)).toEqual([{ id: "other", name: "Work", color: null }]);

    opts.onError(new Error("boom"), { id: LABEL_ID }, context);
    expect(qc.getQueryData<LabelResponse[]>(LABELS_QUERY_KEY)).toHaveLength(2);
  });

  it("onSettled invalidates the roster AND every task cache the FK cascade touched (tasks + today + upcoming)", () => {
    const qc = new QueryClient();
    const spy = vi.spyOn(qc, "invalidateQueries");

    deleteLabelMutationOptions(qc).onSettled();

    expect(spy).toHaveBeenCalledWith({ queryKey: LABELS_QUERY_KEY });
    expect(spy).toHaveBeenCalledWith({ queryKey: TASKS_QUERY_KEY });
    // The bug guard: the grouped daily caches are NOT prefix-covered by ['tasks'] and MUST be invalidated,
    // else a deleted label's id lingers there and a later setTaskLabels re-submits it → 422.
    expect(spy).toHaveBeenCalledWith({ queryKey: TODAY_QUERY_KEY });
    expect(spy).toHaveBeenCalledWith({ queryKey: UPCOMING_QUERY_KEY });
  });

  it("mutationFn surfaces the mapped error message on failure", async () => {
    deleteSpy.mockResolvedValue({ error: { errorCode: "not_found" } });
    await expect(deleteLabelMutationOptions(new QueryClient()).mutationFn({ id: LABEL_ID })).rejects.toThrow();
  });
});

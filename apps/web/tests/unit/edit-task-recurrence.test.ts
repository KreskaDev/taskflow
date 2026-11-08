// @vitest-environment node
import { QueryClient } from "@tanstack/react-query";
import { afterEach, describe, expect, it, vi } from "vitest";

import type { components } from "@/lib/api/generated/schema";
import { editTaskMutationOptions, type EditTaskVariables } from "@/hooks/useTaskMutations";

type TaskResponse = components["schemas"]["TaskResponse"];

vi.mock("@/lib/api/client", async (importOriginal) => {
  const actual = await importOriginal<typeof import("@/lib/api/client")>();
  return {
    ...actual,
    apiClient: { GET: vi.fn(), PUT: vi.fn(), PATCH: vi.fn(), DELETE: vi.fn() },
  };
});

import { apiClient } from "@/lib/api/client";

/**
 * The whole-object edit carries the recurrence rule (slice 012, T017 — contracts/recurrence-api.md D11):
 * the PATCH body sends `recurrenceRule` (null for a one-off task) and the optimistic paint re-stamps it so the
 * row chip updates within the frame. [INV-176]
 */

afterEach(() => vi.clearAllMocks());

function makeTask(overrides: Partial<TaskResponse> = {}): TaskResponse {
  return {
    id: "t1",
    title: "Podlej kwiaty",
    status: "backlog",
    position: "a0",
    version: 3,
    createdAt: "2026-06-01T10:00:00Z",
    updatedAt: "2026-06-01T10:00:00Z",
    completedAt: null,
    dueDate: "2026-06-09T22:00:00Z",
    dueHasTime: false,
    assignees: [],
    labels: [],
    cycleId: null,
    carriedOver: false,
    ...overrides,
  } as TaskResponse;
}

function variables(recurrenceRule: EditTaskVariables["recurrenceRule"]): EditTaskVariables {
  return {
    id: "t1",
    title: "Podlej kwiaty",
    description: null,
    priority: null,
    dueDate: "2026-06-09T22:00:00Z",
    dueHasTime: false,
    projectId: null,
    recurrenceRule,
    version: 3,
  };
}

describe("editTaskMutationOptions — recurrence rides the whole-object replace", () => {
  it("mutationFn PATCH body carries recurrenceRule (a rule)", async () => {
    const patch = vi.mocked(apiClient.PATCH);
    patch.mockResolvedValueOnce({ data: makeTask({ version: 4 }), error: undefined } as never);
    const options = editTaskMutationOptions(new QueryClient());

    await options.mutationFn!(variables({ type: "weekdays", weekdays: [1, 3] }));

    expect(patch).toHaveBeenCalledWith(
      "/api/tasks/{id}/edit",
      expect.objectContaining({
        body: expect.objectContaining({ recurrenceRule: { type: "weekdays", weekdays: [1, 3] }, version: 3 }),
      }),
    );
  });

  it("mutationFn PATCH body carries recurrenceRule: null for a one-off task (the server REQUIRES the field)", async () => {
    const patch = vi.mocked(apiClient.PATCH);
    patch.mockResolvedValueOnce({ data: makeTask({ version: 4 }), error: undefined } as never);
    const options = editTaskMutationOptions(new QueryClient());

    await options.mutationFn!(variables(null));

    const body = (patch.mock.calls[0]![1] as { body: Record<string, unknown> }).body;
    expect("recurrenceRule" in body).toBe(true);
    expect(body.recurrenceRule).toBeNull();
  });

  it("onMutate paints the new rule on the cached row optimistically", async () => {
    const queryClient = new QueryClient();
    queryClient.setQueryData(["tasks"], [makeTask()]);
    const options = editTaskMutationOptions(queryClient);

    await options.onMutate!(variables({ type: "daily" }));

    const row = queryClient.getQueryData<TaskResponse[]>(["tasks"])![0]!;
    expect((row as TaskResponse & { recurrenceRule?: unknown }).recurrenceRule).toEqual({ type: "daily" });
  });
});

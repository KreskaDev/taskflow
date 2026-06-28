// @vitest-environment node
import { QueryClient } from "@tanstack/react-query";
import { afterEach, describe, expect, it, vi } from "vitest";
import type { components } from "@/lib/api/generated/schema";
import { TASKS_QUERY_KEY } from "@/hooks/useTasks";
import { TODAY_QUERY_KEY } from "@/hooks/useTodayTasks";
import { UPCOMING_QUERY_KEY } from "@/hooks/useUpcomingTasks";
import {
  rescheduleDueDateMutationOptions,
  setPriorityMutationOptions,
  setTaskAssigneesMutationOptions,
  toggleDoneMutationOptions,
} from "@/hooks/useTaskMutations";

type TaskResponse = components["schemas"]["TaskResponse"];
type TodayResponse = components["schemas"]["TodayResponse"];
type UpcomingResponse = components["schemas"]["UpcomingResponse"];

vi.mock("@/lib/api/client", async (importOriginal) => {
  const actual = await importOriginal<typeof import("@/lib/api/client")>();
  return { ...actual, apiClient: { GET: vi.fn(), PUT: vi.fn(), PATCH: vi.fn(), DELETE: vi.fn() } };
});

const { apiClient } = await import("@/lib/api/client");
const patchSpy = apiClient.PATCH as unknown as ReturnType<typeof vi.fn>;

afterEach(() => vi.clearAllMocks());

/** Frozen clock so the client membership recompute is deterministic (2026-06-27 12:00Z). */
function freezeNow() {
  vi.useFakeTimers();
  vi.setSystemTime(new Date("2026-06-27T12:00:00Z"));
}
afterEach(() => vi.useRealTimers());

function row(overrides: Partial<TaskResponse> & Pick<TaskResponse, "id">): TaskResponse {
  return {
    id: overrides.id,
    title: overrides.title ?? "T",
    status: overrides.status ?? "backlog",
    position: overrides.position ?? "a0",
    version: overrides.version ?? 0,
    createdAt: overrides.createdAt ?? "2026-06-01T00:00:00Z",
    updatedAt: overrides.updatedAt ?? "2026-06-01T00:00:00Z",
    completedAt: overrides.completedAt ?? null,
    dueDate: overrides.dueDate ?? null,
    dueHasTime: overrides.dueHasTime ?? null,
    projectId: overrides.projectId ?? null,
    priority: overrides.priority ?? null,
    description: overrides.description ?? null,
    assignees: overrides.assignees ?? [],
  };
}

function todayCache(tasks: TaskResponse[]): TodayResponse {
  return { groups: [{ projectId: null, tasks: tasks.map((t) => ({ ...t, isOverdue: false })) }] };
}

describe("set-priority optimistic surface", () => {
  it("re-sorts the Today group in place (priority does not change membership)", async () => {
    freezeNow();
    const qc = new QueryClient();
    const a = row({ id: "a", dueDate: "2026-06-27T10:00:00Z", dueHasTime: true, priority: "P3" });
    const b = row({ id: "b", dueDate: "2026-06-27T11:00:00Z", dueHasTime: true, priority: "P1" });
    qc.setQueryData(TODAY_QUERY_KEY, todayCache([a, b]));

    const opts = setPriorityMutationOptions(qc);
    await opts.onMutate({ id: "a", priority: "P0", version: 0 });

    const today = qc.getQueryData<TodayResponse>(TODAY_QUERY_KEY)!;
    expect(today.groups[0]!.tasks.map((t) => t.id)).toEqual(["a", "b"]); // a now P0 → sorts first
    expect(today.groups[0]!.tasks[0]!.priority).toBe("P0");
  });

  it("onSettled writes the server row (fresh version) back so a rapid second op isn't a stale 409", async () => {
    freezeNow();
    const qc = new QueryClient();
    const a = row({ id: "a", dueDate: "2026-06-27T10:00:00Z", dueHasTime: true, priority: "P3", version: 0 });
    qc.setQueryData(TODAY_QUERY_KEY, todayCache([a]));

    const opts = setPriorityMutationOptions(qc);
    const server = row({ id: "a", dueDate: "2026-06-27T10:00:00Z", dueHasTime: true, priority: "P0", version: 1 });
    await opts.onSettled(server, null, { id: "a", priority: "P0", version: 0 }, undefined as never);

    const stored = qc.getQueryData<TodayResponse>(TODAY_QUERY_KEY)!.groups[0]!.tasks.find((t) => t.id === "a")!;
    expect(stored.version).toBe(1); // fresh version, not the stale optimistic 0
    expect(stored.priority).toBe("P0");
  });

  it("rolls back the Today cache on error", async () => {
    freezeNow();
    const qc = new QueryClient();
    const a = row({ id: "a", dueDate: "2026-06-27T10:00:00Z", dueHasTime: true, priority: "P3" });
    qc.setQueryData(TODAY_QUERY_KEY, todayCache([a]));

    const opts = setPriorityMutationOptions(qc);
    const ctx = await opts.onMutate({ id: "a", priority: "P0", version: 0 });
    opts.onError(new Error("boom"), { id: "a", priority: "P0", version: 0 }, ctx);

    expect(qc.getQueryData<TodayResponse>(TODAY_QUERY_KEY)!.groups[0]!.tasks[0]!.priority).toBe("P3");
  });
});

describe("reschedule optimistic membership recompute", () => {
  it("a reschedule to tomorrow removes the row from Today and adds it to Upcoming", async () => {
    freezeNow();
    const qc = new QueryClient();
    const a = row({ id: "a", dueDate: "2026-06-27T10:00:00Z", dueHasTime: true });
    qc.setQueryData(TODAY_QUERY_KEY, todayCache([a]));
    qc.setQueryData<UpcomingResponse>(UPCOMING_QUERY_KEY, { groups: [] });

    const opts = rescheduleDueDateMutationOptions(qc);
    await opts.onMutate({ id: "a", dueDate: "2026-06-28T10:00:00Z", dueHasTime: true, version: 0 });

    expect(qc.getQueryData<TodayResponse>(TODAY_QUERY_KEY)!.groups.flatMap((g) => g.tasks)).toHaveLength(0);
    const upcoming = qc.getQueryData<UpcomingResponse>(UPCOMING_QUERY_KEY)!;
    expect(upcoming.groups[0]!.date).toBe("2026-06-28");
    expect(upcoming.groups[0]!.tasks[0]!.id).toBe("a");
  });
});

describe("toggle-done optimistic membership removal", () => {
  it("marking done removes the row from both Today and Upcoming", async () => {
    freezeNow();
    const qc = new QueryClient();
    const a = row({ id: "a", dueDate: "2026-06-27T10:00:00Z", dueHasTime: true });
    qc.setQueryData(TODAY_QUERY_KEY, todayCache([a]));
    qc.setQueryData(TASKS_QUERY_KEY, [a]);

    const opts = toggleDoneMutationOptions(qc);
    await opts.onMutate({ id: "a", status: "done", version: 0 });

    expect(qc.getQueryData<TodayResponse>(TODAY_QUERY_KEY)!.groups.flatMap((g) => g.tasks)).toHaveLength(0);
  });
});

describe("set-assignees optimistic surface (slice 008)", () => {
  it("patches the task's assignees in place across the Today cache", async () => {
    freezeNow();
    const qc = new QueryClient();
    const a = row({ id: "a", dueDate: "2026-06-27T10:00:00Z", dueHasTime: true, projectId: "p1", assignees: [] });
    qc.setQueryData(TODAY_QUERY_KEY, todayCache([a]));

    const opts = setTaskAssigneesMutationOptions(qc);
    await opts.onMutate({ id: "a", assigneeIds: ["u1", "u2"], version: 0 });

    const stored = qc.getQueryData<TodayResponse>(TODAY_QUERY_KEY)!.groups[0]!.tasks.find((t) => t.id === "a")!;
    expect(stored.assignees).toEqual(["u1", "u2"]);
  });

  it("set-assignees PATCHes assigneeIds + version", async () => {
    patchSpy.mockResolvedValue({ data: row({ id: "a", assignees: ["u1"], version: 1 }), error: undefined });
    const qc = new QueryClient();
    await setTaskAssigneesMutationOptions(qc).mutationFn({ id: "a", assigneeIds: ["u1"], version: 0 });
    expect(patchSpy).toHaveBeenCalledWith("/api/tasks/{id}/assignees", {
      params: { path: { id: "a" } },
      body: { assigneeIds: ["u1"], version: 0 },
    });
  });
});

describe("request bodies carry the right fields", () => {
  it("set-priority PATCHes priority + version", async () => {
    patchSpy.mockResolvedValue({ data: row({ id: "a", priority: "P0", version: 1 }), error: undefined });
    const qc = new QueryClient();
    await setPriorityMutationOptions(qc).mutationFn({ id: "a", priority: "P0", version: 0 });
    expect(patchSpy).toHaveBeenCalledWith("/api/tasks/{id}/priority", {
      params: { path: { id: "a" } },
      body: { priority: "P0", version: 0 },
    });
  });

  it("reschedule PATCHes the due pair + version", async () => {
    patchSpy.mockResolvedValue({ data: row({ id: "a", version: 1 }), error: undefined });
    const qc = new QueryClient();
    await rescheduleDueDateMutationOptions(qc).mutationFn({
      id: "a",
      dueDate: "2026-06-28T10:00:00Z",
      dueHasTime: true,
      version: 0,
    });
    expect(patchSpy).toHaveBeenCalledWith("/api/tasks/{id}/due-date", {
      params: { path: { id: "a" } },
      body: { dueDate: "2026-06-28T10:00:00Z", dueHasTime: true, version: 0 },
    });
  });
});

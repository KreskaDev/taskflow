// @vitest-environment node
import { QueryClient } from "@tanstack/react-query";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import { membersKey } from "@/hooks/useProjectMembers";
import { ACTIVE_PROJECTS_KEY } from "@/hooks/useProjects";
import {
  changeMemberRoleMutationOptions,
  inviteMemberMutationOptions,
  leaveProjectMutationOptions,
  removeMemberMutationOptions,
  shareProjectMutationOptions,
  transferOwnershipMutationOptions,
  unshareProjectMutationOptions,
} from "@/hooks/useMembershipMutations";

/**
 * Non-optimistic membership mutations (slice 007, T040, RED → drives T042; research R12). Pins the
 * confirmation-gated, invalidate-on-settle recipe: there is NO onMutate snapshot / onError rollback — each
 * mutationFn is the observable wire surface and onSettled invalidates the roster (and the sidebar list on
 * share/unshare/transfer/leave). Errors surface via the global mapError message.
 */
vi.mock("@/lib/api/client", async (importOriginal) => {
  const actual = await importOriginal<typeof import("@/lib/api/client")>();
  return {
    ...actual,
    apiClient: { GET: vi.fn(), PUT: vi.fn(), POST: vi.fn(), PATCH: vi.fn(), DELETE: vi.fn() },
  };
});

const { apiClient } = await import("@/lib/api/client");
const postSpy = apiClient.POST as unknown as ReturnType<typeof vi.fn>;
const patchSpy = apiClient.PATCH as unknown as ReturnType<typeof vi.fn>;
const deleteSpy = apiClient.DELETE as unknown as ReturnType<typeof vi.fn>;

const PID = "11111111-1111-7111-8111-111111111111";
const UID = "22222222-2222-7222-8222-222222222222";

beforeEach(() => {
  postSpy.mockReset();
  patchSpy.mockReset();
  deleteSpy.mockReset();
});

afterEach(() => {
  vi.restoreAllMocks();
});

describe("shareProject — PATCH /share, invalidates roster + project lists on settle", () => {
  it("mutationFn PATCHes /share with the version body", async () => {
    patchSpy.mockResolvedValue({ data: { id: PID, visibility: "shared" }, error: undefined });
    await shareProjectMutationOptions(new QueryClient()).mutationFn({ id: PID, version: 2 });
    const [path, init] = patchSpy.mock.calls[0]!;
    expect(path).toBe("/api/projects/{id}/share");
    expect((init as { params: { path: { id: string } } }).params.path.id).toBe(PID);
    expect((init as { body: Record<string, unknown> }).body).toEqual({ version: 2 });
  });

  it("onSettled invalidates BOTH the roster key and the active projects list", async () => {
    const qc = new QueryClient();
    const spy = vi.spyOn(qc, "invalidateQueries");
    await shareProjectMutationOptions(qc).onSettled(undefined, null, { id: PID, version: 2 });
    expect(spy).toHaveBeenCalledWith({ queryKey: membersKey(PID) });
    expect(spy).toHaveBeenCalledWith({ queryKey: ACTIVE_PROJECTS_KEY });
  });

  it("mutationFn surfaces the mapped error message on failure", async () => {
    patchSpy.mockResolvedValue({ data: undefined, error: { errorCode: "forbidden" } });
    await expect(shareProjectMutationOptions(new QueryClient()).mutationFn({ id: PID, version: 2 })).rejects.toThrow();
  });
});

describe("unshareProject — PATCH /unshare", () => {
  it("mutationFn PATCHes /unshare with the version body", async () => {
    patchSpy.mockResolvedValue({ data: { id: PID, visibility: "personal" }, error: undefined });
    await unshareProjectMutationOptions(new QueryClient()).mutationFn({ id: PID, version: 5 });
    const [path, init] = patchSpy.mock.calls[0]!;
    expect(path).toBe("/api/projects/{id}/unshare");
    expect((init as { body: Record<string, unknown> }).body).toEqual({ version: 5 });
  });
});

describe("inviteMember — POST /members, invalidates only the roster", () => {
  it("mutationFn POSTs the email/role/version body", async () => {
    postSpy.mockResolvedValue({ data: { userId: UID, displayName: "B", role: "editor", isOwner: false }, error: undefined });
    await inviteMemberMutationOptions(new QueryClient()).mutationFn({ id: PID, email: "b@x.com", role: "editor", version: 3 });
    const [path, init] = postSpy.mock.calls[0]!;
    expect(path).toBe("/api/projects/{id}/members");
    expect((init as { body: Record<string, unknown> }).body).toEqual({ email: "b@x.com", role: "editor", version: 3 });
  });

  it("onSettled invalidates the roster but NOT the project list", async () => {
    const qc = new QueryClient();
    const spy = vi.spyOn(qc, "invalidateQueries");
    await inviteMemberMutationOptions(qc).onSettled(undefined, null, { id: PID, email: "b@x.com", role: "editor", version: 3 });
    expect(spy).toHaveBeenCalledWith({ queryKey: membersKey(PID) });
    expect(spy).not.toHaveBeenCalledWith({ queryKey: ACTIVE_PROJECTS_KEY });
  });

  it("surfaces the FR-049 message on an unknown-email 422", async () => {
    postSpy.mockResolvedValue({ data: undefined, error: { errorCode: "validation_failed" } });
    await expect(
      inviteMemberMutationOptions(new QueryClient()).mutationFn({ id: PID, email: "nobody@x.com", role: "editor", version: 0 }),
    ).rejects.toThrow();
  });
});

describe("changeMemberRole — PATCH /members/{userId}", () => {
  it("mutationFn PATCHes the role/version body with both path params", async () => {
    patchSpy.mockResolvedValue({ data: { userId: UID, displayName: "B", role: "viewer", isOwner: false }, error: undefined });
    await changeMemberRoleMutationOptions(new QueryClient()).mutationFn({ id: PID, userId: UID, role: "viewer", version: 4 });
    const [path, init] = patchSpy.mock.calls[0]!;
    expect(path).toBe("/api/projects/{id}/members/{userId}");
    expect((init as { params: { path: { id: string; userId: string } } }).params.path).toEqual({ id: PID, userId: UID });
    expect((init as { body: Record<string, unknown> }).body).toEqual({ role: "viewer", version: 4 });
  });
});

describe("removeMember / leaveProject — DELETE with version as a QUERY param (no body)", () => {
  it("removeMember DELETEs /members/{userId}?version=", async () => {
    deleteSpy.mockResolvedValue({ data: undefined, error: undefined });
    await removeMemberMutationOptions(new QueryClient()).mutationFn({ id: PID, userId: UID, version: 6 });
    const [path, init] = deleteSpy.mock.calls[0]!;
    expect(path).toBe("/api/projects/{id}/members/{userId}");
    expect((init as { params: { path: Record<string, string>; query: Record<string, unknown> } }).params).toEqual({
      path: { id: PID, userId: UID },
      query: { version: 6 },
    });
  });

  it("leaveProject DELETEs /membership?version= and invalidates the project list (R10)", async () => {
    deleteSpy.mockResolvedValue({ data: undefined, error: undefined });
    const qc = new QueryClient();
    const spy = vi.spyOn(qc, "invalidateQueries");
    await leaveProjectMutationOptions(qc).mutationFn({ id: PID, version: 1 });
    const [path, init] = deleteSpy.mock.calls[0]!;
    expect(path).toBe("/api/projects/{id}/membership");
    expect((init as { params: { query: Record<string, unknown> } }).params.query).toEqual({ version: 1 });

    await leaveProjectMutationOptions(qc).onSettled(undefined, null, { id: PID, version: 1 });
    expect(spy).toHaveBeenCalledWith({ queryKey: ACTIVE_PROJECTS_KEY });
  });

  it("removeMember surfaces the mapped last_owner message on a 409", async () => {
    deleteSpy.mockResolvedValue({ error: { errorCode: "last_owner" } });
    await expect(
      removeMemberMutationOptions(new QueryClient()).mutationFn({ id: PID, userId: UID, version: 0 }),
    ).rejects.toThrow();
  });
});

describe("transferOwnership — PATCH /owner", () => {
  it("mutationFn PATCHes the userId/version body and invalidates the project list", async () => {
    patchSpy.mockResolvedValue({ data: { id: PID, role: "editor" }, error: undefined });
    const qc = new QueryClient();
    const spy = vi.spyOn(qc, "invalidateQueries");
    await transferOwnershipMutationOptions(qc).mutationFn({ id: PID, userId: UID, version: 9 });
    const [path, init] = patchSpy.mock.calls[0]!;
    expect(path).toBe("/api/projects/{id}/owner");
    expect((init as { body: Record<string, unknown> }).body).toEqual({ userId: UID, version: 9 });

    await transferOwnershipMutationOptions(qc).onSettled(undefined, null, { id: PID, userId: UID, version: 9 });
    expect(spy).toHaveBeenCalledWith({ queryKey: ACTIVE_PROJECTS_KEY });
  });
});

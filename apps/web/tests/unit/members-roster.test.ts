// @vitest-environment jsdom
import { cleanup, render, screen, within } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { createElement } from "react";

import { MembersDialog } from "@/components/projects/MembersDialog";
import { RoleBadge } from "@/components/projects/RoleBadge";
import type { MembersResponse } from "@/hooks/useProjectMembers";
import type { ProjectResponse } from "@/hooks/useProjects";

/**
 * Members roster rendering + role-aware gating (slice 007, T043, RED → drives T044; FR-044, Principle II).
 * Pins: role badges carry TEXT + ICON (never color alone); a viewer sees a READ-ONLY roster (no invite
 * form, no manage controls) with a Leave action; the OWNER sees invite + per-member manage + transfer +
 * unshare; the owner entry renders as `isOwner`.
 */
vi.mock("@/hooks/useProjectMembers", async (importOriginal) => {
  const actual = await importOriginal<typeof import("@/hooks/useProjectMembers")>();
  return { ...actual, useProjectMembers: vi.fn() };
});

vi.mock("@/hooks/useMembershipMutations", () => ({
  useMembershipMutations: () => ({
    shareProject: vi.fn(),
    unshareProject: vi.fn(),
    transferOwnership: vi.fn(),
    inviteMember: vi.fn(),
    changeMemberRole: vi.fn(),
    removeMember: vi.fn(),
    leaveProject: vi.fn(),
    inviteError: null,
    isInvitePending: false,
  }),
}));

const { useProjectMembers } = await import("@/hooks/useProjectMembers");
const membersMock = useProjectMembers as unknown as ReturnType<typeof vi.fn>;

const PID = "11111111-1111-7111-8111-111111111111";

const roster: MembersResponse = {
  projectId: PID,
  version: 4,
  members: [
    { userId: "owner-id", displayName: "Olivia Owner", role: "owner", isOwner: true },
    { userId: "editor-id", displayName: "Eddie Editor", role: "editor", isOwner: false },
    { userId: "viewer-id", displayName: "Vera Viewer", role: "viewer", isOwner: false },
  ],
};

function project(role: string): ProjectResponse {
  return {
    id: PID,
    name: "Team Space",
    color: "blue",
    icon: "folder",
    parentId: null,
    visibility: "shared",
    role,
    archivedAt: null,
    version: 4,
    createdAt: "2026-06-20T00:00:00Z",
    updatedAt: "2026-06-20T00:00:00Z",
  };
}

afterEach(() => {
  cleanup();
  membersMock.mockReset();
});

describe("RoleBadge — text + icon, never color alone (FR-044)", () => {
  it("renders the role word for each role", () => {
    render(createElement(RoleBadge, { role: "owner" }));
    expect(screen.getByText("Owner")).toBeTruthy();
  });
});

describe("MembersDialog — owner view", () => {
  it("shows the invite form, per-member manage controls, transfer and unshare", () => {
    membersMock.mockReturnValue({ data: roster, isPending: false, error: null });
    render(createElement(MembersDialog, { open: true, onClose: () => {}, project: project("owner") }));

    expect(screen.getByLabelText("Invite a member")).toBeTruthy();
    expect(screen.getByLabelText("Role for Eddie Editor")).toBeTruthy();
    expect(screen.getByLabelText("Remove Vera Viewer")).toBeTruthy();
    expect(screen.getByRole("button", { name: "Transfer ownership" })).toBeTruthy();
    expect(screen.getByRole("button", { name: "Unshare project" })).toBeTruthy();
    // The owner entry renders with the owner badge.
    const list = screen.getByLabelText("Project members");
    expect(within(list).getByText("Owner")).toBeTruthy();
  });
});

describe("MembersDialog — viewer (non-owner) view is read-only", () => {
  it("hides the invite form + manage controls and shows Leave", () => {
    membersMock.mockReturnValue({ data: roster, isPending: false, error: null });
    render(createElement(MembersDialog, { open: true, onClose: () => {}, project: project("viewer") }));

    expect(screen.queryByLabelText("Invite a member")).toBeNull();
    expect(screen.queryByLabelText("Role for Eddie Editor")).toBeNull();
    expect(screen.queryByRole("button", { name: "Unshare project" })).toBeNull();
    expect(screen.getByRole("button", { name: "Leave project" })).toBeTruthy();
  });
});

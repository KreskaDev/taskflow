"use client";

import { type QueryClient, useMutation, useQueryClient } from "@tanstack/react-query";

import { apiClient, mapError, type ProblemDetails } from "@/lib/api/client";
import type { MemberResponse } from "@/hooks/useProjectMembers";
import { membersKey } from "@/hooks/useProjectMembers";
import { ACTIVE_PROJECTS_KEY, ARCHIVED_PROJECTS_KEY, type ProjectResponse } from "@/hooks/useProjects";
import type { MembershipRole } from "@/lib/validation/membership";

/**
 * The NON-optimistic, confirmation-gated membership mutation family (slice 007, T042; research R12,
 * FR-064). A deliberate departure from the slice-002/004 optimistic + undo recipe: these high-consequence,
 * hard-to-reverse actions take effect ONLY on the confirmed server round-trip, so there is NO `onMutate`
 * snapshot, NO `onError` rollback, and NO undo toast — each mutation simply INVALIDATES the
 * `['projects', id, 'members']` roster key on settle (and the `['projects']` list on share/unshare/transfer,
 * which change visibility/role/owner). Errors (`last_owner` / `forbidden` / `validation_failed` /
 * `version_conflict`) surface via the global `mapError` message (FR-049).
 */

/** Re-invalidates the roster on settle; visibility/owner-changing ops also refresh the sidebar lists. */
async function invalidateRoster(queryClient: QueryClient, projectId: string, alsoProjectLists: boolean): Promise<void> {
  await queryClient.invalidateQueries({ queryKey: membersKey(projectId) });
  if (alsoProjectLists) {
    await queryClient.invalidateQueries({ queryKey: ACTIVE_PROJECTS_KEY });
    await queryClient.invalidateQueries({ queryKey: ARCHIVED_PROJECTS_KEY });
  }
}

function throwMapped(error: unknown): never {
  const errorCode = (error as ProblemDetails | undefined)?.errorCode;
  throw new Error(mapError(errorCode).message);
}

/* ─────────────────────────────── share / unshare ─────────────────────────────── */

export interface VisibilityVariables {
  id: string;
  version: number;
}

interface ProjectMutationOptions<V> {
  mutationFn: (variables: V) => Promise<ProjectResponse>;
  onSettled: (data: ProjectResponse | undefined, error: Error | null, variables: V) => Promise<void>;
}

export function shareProjectMutationOptions(queryClient: QueryClient): ProjectMutationOptions<VisibilityVariables> {
  return {
    mutationFn: async ({ id, version }): Promise<ProjectResponse> => {
      const { data, error } = await apiClient.PATCH("/api/projects/{id}/share", {
        params: { path: { id } },
        body: { version },
      });
      if (error || !data) throwMapped(error);
      return data;
    },
    onSettled: async (_d, _e, { id }) => invalidateRoster(queryClient, id, true),
  };
}

export function unshareProjectMutationOptions(queryClient: QueryClient): ProjectMutationOptions<VisibilityVariables> {
  return {
    mutationFn: async ({ id, version }): Promise<ProjectResponse> => {
      const { data, error } = await apiClient.PATCH("/api/projects/{id}/unshare", {
        params: { path: { id } },
        body: { version },
      });
      if (error || !data) throwMapped(error);
      return data;
    },
    onSettled: async (_d, _e, { id }) => invalidateRoster(queryClient, id, true),
  };
}

/* ─────────────────────────────── transfer ownership ─────────────────────────────── */

export interface TransferVariables {
  id: string;
  userId: string;
  version: number;
}

export function transferOwnershipMutationOptions(queryClient: QueryClient): ProjectMutationOptions<TransferVariables> {
  return {
    mutationFn: async ({ id, userId, version }): Promise<ProjectResponse> => {
      const { data, error } = await apiClient.PATCH("/api/projects/{id}/owner", {
        params: { path: { id } },
        body: { userId, version },
      });
      if (error || !data) throwMapped(error);
      return data;
    },
    onSettled: async (_d, _e, { id }) => invalidateRoster(queryClient, id, true),
  };
}

/* ─────────────────────────────── invite ─────────────────────────────── */

export interface InviteVariables {
  id: string;
  email: string;
  role: MembershipRole;
  version: number;
}

interface MemberMutationOptions<V> {
  mutationFn: (variables: V) => Promise<MemberResponse>;
  onSettled: (data: MemberResponse | undefined, error: Error | null, variables: V) => Promise<void>;
}

export function inviteMemberMutationOptions(queryClient: QueryClient): MemberMutationOptions<InviteVariables> {
  return {
    mutationFn: async ({ id, email, role, version }): Promise<MemberResponse> => {
      const { data, error } = await apiClient.POST("/api/projects/{id}/members", {
        params: { path: { id } },
        body: { email, role, version },
      });
      if (error || !data) throwMapped(error);
      return data;
    },
    onSettled: async (_d, _e, { id }) => invalidateRoster(queryClient, id, false),
  };
}

/* ─────────────────────────────── change role ─────────────────────────────── */

export interface ChangeRoleVariables {
  id: string;
  userId: string;
  role: MembershipRole;
  version: number;
}

export function changeMemberRoleMutationOptions(queryClient: QueryClient): MemberMutationOptions<ChangeRoleVariables> {
  return {
    mutationFn: async ({ id, userId, role, version }): Promise<MemberResponse> => {
      const { data, error } = await apiClient.PATCH("/api/projects/{id}/members/{userId}", {
        params: { path: { id, userId } },
        body: { role, version },
      });
      if (error || !data) throwMapped(error);
      return data;
    },
    onSettled: async (_d, _e, { id }) => invalidateRoster(queryClient, id, false),
  };
}

/* ─────────────────────────────── remove / leave ─────────────────────────────── */

export interface RemoveVariables {
  id: string;
  userId: string;
  version: number;
}

interface VoidMutationOptions<V> {
  mutationFn: (variables: V) => Promise<void>;
  onSettled: (data: void | undefined, error: Error | null, variables: V) => Promise<void>;
}

export function removeMemberMutationOptions(queryClient: QueryClient): VoidMutationOptions<RemoveVariables> {
  return {
    mutationFn: async ({ id, userId, version }): Promise<void> => {
      const { error } = await apiClient.DELETE("/api/projects/{id}/members/{userId}", {
        params: { path: { id, userId }, query: { version } },
      });
      if (error) throwMapped(error);
    },
    onSettled: async (_d, _e, { id }) => invalidateRoster(queryClient, id, false),
  };
}

export function leaveProjectMutationOptions(queryClient: QueryClient): VoidMutationOptions<VisibilityVariables> {
  return {
    mutationFn: async ({ id, version }): Promise<void> => {
      const { error } = await apiClient.DELETE("/api/projects/{id}/membership", {
        params: { path: { id }, query: { version } },
      });
      if (error) throwMapped(error);
    },
    // Leaving a project removes it from the caller's sidebar list too (R10).
    onSettled: async (_d, _e, { id }) => invalidateRoster(queryClient, id, true),
  };
}

/* ───────────────────────────────── hook wrapper ────────────────────────────────── */

/**
 * "use client" hook exposing the seven non-optimistic, confirmation-gated membership mutations for the
 * sharing/members dialogs. Each takes effect on the confirmed round-trip and invalidates on settle (no
 * snapshot/rollback, no undo). The dialogs supply the current `version` (from the roster or the project).
 */
export function useMembershipMutations() {
  const queryClient = useQueryClient();

  const shareMutation = useMutation(shareProjectMutationOptions(queryClient));
  const unshareMutation = useMutation(unshareProjectMutationOptions(queryClient));
  const transferMutation = useMutation(transferOwnershipMutationOptions(queryClient));
  const inviteMutation = useMutation(inviteMemberMutationOptions(queryClient));
  const changeRoleMutation = useMutation(changeMemberRoleMutationOptions(queryClient));
  const removeMutation = useMutation(removeMemberMutationOptions(queryClient));
  const leaveMutation = useMutation(leaveProjectMutationOptions(queryClient));

  return {
    shareProject: (id: string, version: number) => shareMutation.mutate({ id, version }),
    unshareProject: (id: string, version: number) => unshareMutation.mutate({ id, version }),
    transferOwnership: (id: string, userId: string, version: number) => transferMutation.mutate({ id, userId, version }),
    inviteMember: (id: string, email: string, role: MembershipRole, version: number) =>
      inviteMutation.mutate({ id, email, role, version }),
    changeMemberRole: (id: string, userId: string, role: MembershipRole, version: number) =>
      changeRoleMutation.mutate({ id, userId, role, version }),
    removeMember: (id: string, userId: string, version: number) => removeMutation.mutate({ id, userId, version }),
    leaveProject: (id: string, version: number) => leaveMutation.mutate({ id, version }),
    inviteError: inviteMutation.error?.message ?? null,
    isInvitePending: inviteMutation.isPending,
  };
}

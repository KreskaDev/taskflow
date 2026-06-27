"use client";

import { type QueryClient, useMutation, useQueryClient } from "@tanstack/react-query";

import { apiClient, mapError, type ProblemDetails } from "@/lib/api/client";
import { newProjectId } from "@/lib/id";
import {
  type ChildDisposition,
  createProjectSchema,
  editProjectSchema,
  type TaskDisposition,
} from "@/lib/validation/project";
import {
  ACTIVE_PROJECTS_KEY,
  ARCHIVED_PROJECTS_KEY,
  type ProjectResponse,
} from "@/hooks/useProjects";

export { ACTIVE_PROJECTS_KEY, ARCHIVED_PROJECTS_KEY } from "@/hooks/useProjects";

/**
 * Optimistic project mutations (T026; research R15). Five pure TanStack-Query options factories
 * mirroring the slice-002 `createTaskMutationOptions` recipe altitude: `onMutate` cancels +
 * snapshots + patches the flat `['projects']` list in place, `onError` rolls the snapshot back,
 * `onSettled` invalidates. There is NO once-only 409 reapply (that was a US8 task-specific recipe);
 * a 409/422 here is a plain rollback + the FR-049 message surfaced through the global announcer,
 * with the authoritative re-validation reconciled by the `onSettled` invalidate.
 *
 * Cache shape: `['projects']` = the flat ACTIVE list (Sidebar assembles the tree at render, R16);
 * `['projects','archived']` = the archived listing (R8). Archive optimistically removes from active;
 * unarchive removes from archived; both invalidate BOTH keys so the moved row reconciles either side.
 */

/** Context handed from every recipe's `onMutate` to `onError`/`onSettled` — the pre-mutation snapshot. */
export interface ProjectMutationContext {
  previousActive: ProjectResponse[] | undefined;
  previousArchived: ProjectResponse[] | undefined;
}

function snapshot(queryClient: QueryClient): ProjectMutationContext {
  return {
    previousActive: queryClient.getQueryData<ProjectResponse[]>(ACTIVE_PROJECTS_KEY),
    previousArchived: queryClient.getQueryData<ProjectResponse[]>(ARCHIVED_PROJECTS_KEY),
  };
}

/** Restores both project lists to the pre-mutation snapshot (the shared rollback). */
function rollback(queryClient: QueryClient, context: ProjectMutationContext | undefined): void {
  queryClient.setQueryData<ProjectResponse[] | undefined>(ACTIVE_PROJECTS_KEY, context?.previousActive);
  queryClient.setQueryData<ProjectResponse[] | undefined>(ARCHIVED_PROJECTS_KEY, context?.previousArchived);
}

/** Reconciles with server truth on BOTH keys regardless of success/failure. */
async function settle(queryClient: QueryClient): Promise<void> {
  await queryClient.invalidateQueries({ queryKey: ACTIVE_PROJECTS_KEY });
  await queryClient.invalidateQueries({ queryKey: ARCHIVED_PROJECTS_KEY });
}

/* ───────────────────────────────── CREATE (PUT) ────────────────────────────────── */

export interface CreateProjectVariables {
  id: string;
  name: string;
  color: string;
  icon: string;
  parentId?: string | null;
}

interface CreateProjectOptions {
  mutationFn: (variables: CreateProjectVariables) => Promise<ProjectResponse>;
  onMutate: (variables: CreateProjectVariables) => Promise<ProjectMutationContext>;
  onError: (error: Error, variables: CreateProjectVariables, context: ProjectMutationContext | undefined) => Promise<void>;
  onSettled: (
    data: ProjectResponse | undefined,
    error: Error | null,
    variables: CreateProjectVariables,
    context: ProjectMutationContext | undefined,
  ) => Promise<void>;
}

export function createProjectMutationOptions(queryClient: QueryClient): CreateProjectOptions {
  return {
    mutationFn: async ({ id, name, color, icon, parentId }: CreateProjectVariables): Promise<ProjectResponse> => {
      const { data, error } = await apiClient.PUT("/api/projects/{id}", {
        params: { path: { id } },
        body: { name, color, icon, parentId: parentId ?? null },
      });
      if (error || !data) {
        const errorCode = (error as ProblemDetails | undefined)?.errorCode;
        throw new Error(mapError(errorCode).message);
      }
      return data;
    },

    onMutate: async (variables: CreateProjectVariables): Promise<ProjectMutationContext> => {
      await queryClient.cancelQueries({ queryKey: ACTIVE_PROJECTS_KEY });
      const context = snapshot(queryClient);

      const now = new Date().toISOString();
      const optimistic: ProjectResponse = {
        id: variables.id,
        name: variables.name,
        color: variables.color,
        icon: variables.icon,
        parentId: variables.parentId ?? null,
        visibility: "personal",
        archivedAt: null,
        version: 0,
        createdAt: now,
        updatedAt: now,
      };

      queryClient.setQueryData<ProjectResponse[]>(ACTIVE_PROJECTS_KEY, (old) => [...(old ?? []), optimistic]);
      return context;
    },

    onError: async (_error, _variables, context): Promise<void> => {
      rollback(queryClient, context);
    },

    onSettled: async (): Promise<void> => {
      await settle(queryClient);
    },
  };
}

/* ───────────────────────────────── EDIT (PATCH) ────────────────────────────────── */

export interface EditProjectVariables {
  id: string;
  name: string;
  color: string;
  icon: string;
  parentId: string | null;
  version: number;
}

interface EditProjectOptions {
  mutationFn: (variables: EditProjectVariables) => Promise<ProjectResponse>;
  onMutate: (variables: EditProjectVariables) => Promise<ProjectMutationContext>;
  onError: (error: Error, variables: EditProjectVariables, context: ProjectMutationContext | undefined) => Promise<void>;
  onSettled: (
    data: ProjectResponse | undefined,
    error: Error | null,
    variables: EditProjectVariables,
    context: ProjectMutationContext | undefined,
  ) => Promise<void>;
}

export function editProjectMutationOptions(queryClient: QueryClient): EditProjectOptions {
  return {
    mutationFn: async ({ id, name, color, icon, parentId, version }: EditProjectVariables): Promise<ProjectResponse> => {
      const { data, error } = await apiClient.PATCH("/api/projects/{id}", {
        params: { path: { id } },
        body: { name, color, icon, parentId, version },
      });
      if (error || !data) {
        const errorCode = (error as ProblemDetails | undefined)?.errorCode;
        throw new Error(mapError(errorCode).message);
      }
      return data;
    },

    onMutate: async (variables: EditProjectVariables): Promise<ProjectMutationContext> => {
      await queryClient.cancelQueries({ queryKey: ACTIVE_PROJECTS_KEY });
      const context = snapshot(queryClient);

      queryClient.setQueryData<ProjectResponse[]>(ACTIVE_PROJECTS_KEY, (old) =>
        (old ?? []).map((p) =>
          p.id === variables.id
            ? { ...p, name: variables.name, color: variables.color, icon: variables.icon, parentId: variables.parentId }
            : p,
        ),
      );
      return context;
    },

    onError: async (_error, _variables, context): Promise<void> => {
      rollback(queryClient, context);
    },

    onSettled: async (): Promise<void> => {
      await settle(queryClient);
    },
  };
}

/* ──────────────────────────────── ARCHIVE (PATCH) ──────────────────────────────── */

export interface ArchiveProjectVariables {
  id: string;
  version: number;
  childDisposition?: ChildDisposition;
}

interface ArchiveProjectOptions {
  mutationFn: (variables: ArchiveProjectVariables) => Promise<ProjectResponse>;
  onMutate: (variables: ArchiveProjectVariables) => Promise<ProjectMutationContext>;
  onError: (error: Error, variables: ArchiveProjectVariables, context: ProjectMutationContext | undefined) => Promise<void>;
  onSettled: (
    data: ProjectResponse | undefined,
    error: Error | null,
    variables: ArchiveProjectVariables,
    context: ProjectMutationContext | undefined,
  ) => Promise<void>;
}

export function archiveProjectMutationOptions(queryClient: QueryClient): ArchiveProjectOptions {
  return {
    mutationFn: async ({ id, version, childDisposition }: ArchiveProjectVariables): Promise<ProjectResponse> => {
      const { data, error } = await apiClient.PATCH("/api/projects/{id}/archive", {
        params: { path: { id } },
        body: childDisposition ? { version, childDisposition } : { version },
      });
      if (error || !data) {
        const errorCode = (error as ProblemDetails | undefined)?.errorCode;
        throw new Error(mapError(errorCode).message);
      }
      return data;
    },

    onMutate: async (variables: ArchiveProjectVariables): Promise<ProjectMutationContext> => {
      await queryClient.cancelQueries({ queryKey: ACTIVE_PROJECTS_KEY });
      const context = snapshot(queryClient);

      // Archiving HIDES the project from the default (active) tree (AS-05). If childDisposition is
      // cascade, the children share the parent's fate — drop the whole subtree from the active list.
      queryClient.setQueryData<ProjectResponse[]>(ACTIVE_PROJECTS_KEY, (old) =>
        (old ?? []).filter((p) => {
          if (p.id === variables.id) return false;
          if (variables.childDisposition === "cascade" && p.parentId === variables.id) return false;
          return true;
        }),
      );
      return context;
    },

    onError: async (_error, _variables, context): Promise<void> => {
      rollback(queryClient, context);
    },

    onSettled: async (): Promise<void> => {
      await settle(queryClient);
    },
  };
}

/* ─────────────────────────────── UNARCHIVE (PATCH) ─────────────────────────────── */

export interface UnarchiveProjectVariables {
  id: string;
  version: number;
}

interface UnarchiveProjectOptions {
  mutationFn: (variables: UnarchiveProjectVariables) => Promise<ProjectResponse>;
  onMutate: (variables: UnarchiveProjectVariables) => Promise<ProjectMutationContext>;
  onError: (error: Error, variables: UnarchiveProjectVariables, context: ProjectMutationContext | undefined) => Promise<void>;
  onSettled: (
    data: ProjectResponse | undefined,
    error: Error | null,
    variables: UnarchiveProjectVariables,
    context: ProjectMutationContext | undefined,
  ) => Promise<void>;
}

export function unarchiveProjectMutationOptions(queryClient: QueryClient): UnarchiveProjectOptions {
  return {
    mutationFn: async ({ id, version }: UnarchiveProjectVariables): Promise<ProjectResponse> => {
      const { data, error } = await apiClient.PATCH("/api/projects/{id}/unarchive", {
        params: { path: { id } },
        body: { version },
      });
      if (error || !data) {
        const errorCode = (error as ProblemDetails | undefined)?.errorCode;
        throw new Error(mapError(errorCode).message);
      }
      return data;
    },

    onMutate: async (variables: UnarchiveProjectVariables): Promise<ProjectMutationContext> => {
      await queryClient.cancelQueries({ queryKey: ARCHIVED_PROJECTS_KEY });
      const context = snapshot(queryClient);

      // Unarchiving removes the row from the archived listing; `onSettled` re-fetches the active
      // tree (the server restores it top-level when its parent is still archived, R9).
      queryClient.setQueryData<ProjectResponse[]>(ARCHIVED_PROJECTS_KEY, (old) =>
        (old ?? []).filter((p) => p.id !== variables.id),
      );
      return context;
    },

    onError: async (_error, _variables, context): Promise<void> => {
      rollback(queryClient, context);
    },

    onSettled: async (): Promise<void> => {
      await settle(queryClient);
    },
  };
}

/* ──────────────────────────────── DELETE (DELETE) ──────────────────────────────── */

export interface DeleteProjectVariables {
  id: string;
  version: number;
  taskDisposition?: TaskDisposition;
  childDisposition?: ChildDisposition;
}

interface DeleteProjectOptions {
  mutationFn: (variables: DeleteProjectVariables) => Promise<void>;
  onMutate: (variables: DeleteProjectVariables) => Promise<ProjectMutationContext>;
  onError: (error: Error, variables: DeleteProjectVariables, context: ProjectMutationContext | undefined) => Promise<void>;
  onSettled: (
    data: void | undefined,
    error: Error | null,
    variables: DeleteProjectVariables,
    context: ProjectMutationContext | undefined,
  ) => Promise<void>;
}

export function deleteProjectMutationOptions(queryClient: QueryClient): DeleteProjectOptions {
  return {
    mutationFn: async ({ id, version, taskDisposition, childDisposition }: DeleteProjectVariables): Promise<void> => {
      const query: { version: number; taskDisposition?: string; childDisposition?: string } = { version };
      if (taskDisposition) query.taskDisposition = taskDisposition;
      if (childDisposition) query.childDisposition = childDisposition;

      const { error } = await apiClient.DELETE("/api/projects/{id}", {
        params: { path: { id }, query },
      });
      if (error) {
        const errorCode = (error as ProblemDetails | undefined)?.errorCode;
        throw new Error(mapError(errorCode).message);
      }
    },

    onMutate: async (variables: DeleteProjectVariables): Promise<ProjectMutationContext> => {
      await queryClient.cancelQueries({ queryKey: ACTIVE_PROJECTS_KEY });
      const context = snapshot(queryClient);

      // Optimistically remove the deleted project (and a cascaded subtree) from the active tree.
      queryClient.setQueryData<ProjectResponse[]>(ACTIVE_PROJECTS_KEY, (old) =>
        (old ?? []).filter((p) => {
          if (p.id === variables.id) return false;
          if (variables.childDisposition === "cascade" && p.parentId === variables.id) return false;
          return true;
        }),
      );
      return context;
    },

    onError: async (_error, _variables, context): Promise<void> => {
      rollback(queryClient, context);
    },

    onSettled: async (): Promise<void> => {
      await settle(queryClient);
    },
  };
}

/* ─────────────────── Client-side one-level-nesting prevention (R15) ─────────────────── */

/** The FR-049 recoverable messages for the two one-level-nesting failure shapes (AS-03/AS-09). */
export const PARENT_IS_CHILD_MESSAGE =
  "That project is already nested. Projects can be nested only one level deep.";
export const PROJECT_HAS_CHILDREN_MESSAGE =
  "This project has sub-projects, so it can't become a sub-project itself (one level of nesting only).";

/**
 * Computes the one-level-nesting prevention message client-side from the loaded flat tree (R15),
 * so AS-03/AS-09 paint instantly with no round-trip; the server re-validates authoritatively (R3).
 * Returns `null` when the (re-)parent is legal. Two failure shapes:
 *   1. the chosen `parentId` is ITSELF a child (its `parentId` is non-null) → grandchild;
 *   2. the project being parented HAS children → its children would land at depth 2.
 */
export function nestingPreventionMessage(
  projects: ProjectResponse[],
  projectId: string,
  parentId: string | null,
): string | null {
  if (parentId === null) return null; // top-level is always allowed.

  const parent = projects.find((p) => p.id === parentId);
  if (parent && parent.parentId !== null) {
    return PARENT_IS_CHILD_MESSAGE;
  }

  const hasChildren = projects.some((p) => p.parentId === projectId);
  if (hasChildren) {
    return PROJECT_HAS_CHILDREN_MESSAGE;
  }

  return null;
}

/* ───────────────────────────────── hook wrapper ────────────────────────────────── */

/**
 * "use client" hook wrapper. `createProject`/`editProject` validate at the trust boundary
 * (Constitution VI) before driving their optimistic recipes; `archive`/`unarchive`/`delete` carry
 * the row's current `version` (looked up from the active cache) + caller-chosen dispositions.
 */
export function useProjectMutations() {
  const queryClient = useQueryClient();

  const createMutation = useMutation<ProjectResponse, Error, CreateProjectVariables, ProjectMutationContext>(
    createProjectMutationOptions(queryClient),
  );
  const editMutation = useMutation<ProjectResponse, Error, EditProjectVariables, ProjectMutationContext>(
    editProjectMutationOptions(queryClient),
  );
  const archiveMutation = useMutation<ProjectResponse, Error, ArchiveProjectVariables, ProjectMutationContext>(
    archiveProjectMutationOptions(queryClient),
  );
  const unarchiveMutation = useMutation<ProjectResponse, Error, UnarchiveProjectVariables, ProjectMutationContext>(
    unarchiveProjectMutationOptions(queryClient),
  );
  const deleteMutation = useMutation<void, Error, DeleteProjectVariables, ProjectMutationContext>(
    deleteProjectMutationOptions(queryClient),
  );

  const activeProjects = (): ProjectResponse[] =>
    queryClient.getQueryData<ProjectResponse[]>(ACTIVE_PROJECTS_KEY) ?? [];

  const createProject = (input: { name: string; color: string; icon: string; parentId?: string | null }): void => {
    const parsed = createProjectSchema.parse(input);
    createMutation.mutate({
      id: newProjectId(),
      name: parsed.name,
      color: parsed.color,
      icon: parsed.icon,
      parentId: parsed.parentId ?? null,
    });
  };

  const editProject = (
    id: string,
    input: { name: string; color: string; icon: string; parentId: string | null },
  ): void => {
    const row = activeProjects().find((p) => p.id === id);
    if (!row) return;
    const parsed = editProjectSchema.parse({ ...input, version: row.version });
    editMutation.mutate({
      id,
      name: parsed.name,
      color: parsed.color,
      icon: parsed.icon,
      parentId: parsed.parentId,
      version: parsed.version,
    });
  };

  const archiveProject = (id: string, childDisposition?: ChildDisposition): void => {
    const row = activeProjects().find((p) => p.id === id);
    if (!row) return;
    archiveMutation.mutate({ id, version: row.version, childDisposition });
  };

  const unarchiveProject = (id: string, version: number): void => {
    unarchiveMutation.mutate({ id, version });
  };

  const deleteProject = (
    id: string,
    options?: { taskDisposition?: TaskDisposition; childDisposition?: ChildDisposition },
  ): void => {
    const row = activeProjects().find((p) => p.id === id);
    if (!row) return;
    deleteMutation.mutate({
      id,
      version: row.version,
      taskDisposition: options?.taskDisposition,
      childDisposition: options?.childDisposition,
    });
  };

  return { createProject, editProject, archiveProject, unarchiveProject, deleteProject };
}

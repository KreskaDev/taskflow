"use client";

import { type QueryClient, useMutation, useQueryClient } from "@tanstack/react-query";

import { apiClient, mapError, type ProblemDetails } from "@/lib/api/client";
import { commentsKey, type CommentListResponse, type CommentMention, type CommentResponse } from "@/hooks/useComments";

export { commentsKey } from "@/hooks/useComments";
import { commentSchema } from "@/lib/validation/comment";
import { newTaskId } from "@/lib/id";

/**
 * The OPTIMISTIC comment mutation family (slice 009, T033; R14, Constitution III) — the slice-002/004
 * recipe (cancel → snapshot → optimistic paint → rollback → invalidate) against the single
 * `['tasks', taskId, 'comments']` key, deliberately unlike slice-007's non-optimistic membership: post
 * APPENDS an own row, edit RE-STAMPS body/mentions/editedAt in place (whole replace), delete REMOVES the
 * row — each painting within one animation frame, the server reconciling under LAST-WRITE-WINS (no
 * `version` on any wire call, no 409 — R2). On error the snapshot rolls back and the FR-049 message
 * surfaces through the global MutationCache announcer.
 *
 * Reconciliation contract recorded for slice 016 (R13): an inbound REMOTE comment must NOT clobber a
 * pending local optimistic edit — live transport must merge around unsettled mutations on this key.
 */

/** The one snapshot shape all three recipes share: the pre-mutation thread. */
export interface CommentThreadContext {
  previousThread: CommentListResponse | undefined;
}

/* ─────────────────────────────── POST ─────────────────────────────── */

export interface PostCommentVariables {
  taskId: string;
  /** Client-minted placeholder id for the optimistic row (the server mints the real UUIDv7 — R1). */
  optimisticId: string;
  body: string;
  /** The typed @mention token set the wire carries (R6). */
  mentionedUserIds: string[];
  /** The caller's identity, for painting the optimistic row (the server stays authoritative). */
  authorId: string;
  authorDisplayName: string;
  /** The resolved mention chips for the optimistic paint (from the picker's roster rows). */
  mentions: CommentMention[];
}

interface PostCommentOptions {
  mutationFn: (variables: PostCommentVariables) => Promise<CommentResponse>;
  onMutate: (variables: PostCommentVariables) => Promise<CommentThreadContext>;
  onError: (error: Error, variables: PostCommentVariables, context: CommentThreadContext | undefined) => void;
  onSettled: (
    data: CommentResponse | undefined,
    error: Error | null,
    variables: PostCommentVariables,
    context: CommentThreadContext | undefined,
  ) => Promise<void>;
}

/**
 * Optimistic POST recipe: appends the caller's comment to the thread tail (chronological — a fresh post
 * is always newest) with `canEdit: true`, then reconciles with the server row on settle.
 */
export function postCommentMutationOptions(queryClient: QueryClient): PostCommentOptions {
  return {
    mutationFn: async ({ taskId, body, mentionedUserIds }: PostCommentVariables): Promise<CommentResponse> => {
      const { data, error } = await apiClient.POST("/api/tasks/{taskId}/comments", {
        params: { path: { taskId } },
        body: { body, mentionedUserIds },
      });
      if (error || !data) {
        const errorCode = (error as ProblemDetails | undefined)?.errorCode;
        throw new Error(mapError(errorCode).message);
      }
      return data;
    },

    onMutate: async (variables: PostCommentVariables): Promise<CommentThreadContext> => {
      const key = commentsKey(variables.taskId);
      await queryClient.cancelQueries({ queryKey: key });
      const previousThread = queryClient.getQueryData<CommentListResponse>(key);

      const optimistic: CommentResponse = {
        id: variables.optimisticId,
        taskId: variables.taskId,
        authorId: variables.authorId,
        authorDisplayName: variables.authorDisplayName,
        body: variables.body,
        mentions: variables.mentions,
        createdAt: new Date().toISOString(),
        editedAt: null,
        canEdit: true,
      };

      queryClient.setQueryData<CommentListResponse>(key, (old) => ({
        taskId: variables.taskId,
        comments: [...(old?.comments ?? []), optimistic],
      }));

      return { previousThread };
    },

    onError: (_error, variables, context): void => {
      queryClient.setQueryData<CommentListResponse | undefined>(
        commentsKey(variables.taskId),
        context?.previousThread,
      );
    },

    onSettled: async (data, _error, variables): Promise<void> => {
      // On success, swap the optimistic placeholder for the server row (real id + resolved names)
      // synchronously, then reconcile with server truth.
      if (data) {
        queryClient.setQueryData<CommentListResponse>(commentsKey(variables.taskId), (old) =>
          old == null
            ? old
            : {
                ...old,
                comments: old.comments.map((c) => (c.id === variables.optimisticId ? data : c)),
              },
        );
      }
      await queryClient.invalidateQueries({ queryKey: commentsKey(variables.taskId) });
    },
  };
}

/* ─────────────────────────────── EDIT ─────────────────────────────── */

export interface EditCommentVariables {
  taskId: string;
  commentId: string;
  /** The WHOLE replacement body (LWW — R2). */
  body: string;
  /** The WHOLE replacement mention token set (anti-silent-null — R6). */
  mentionedUserIds: string[];
  /** The resolved mention chips for the optimistic paint. */
  mentions: CommentMention[];
}

interface EditCommentOptions {
  mutationFn: (variables: EditCommentVariables) => Promise<CommentResponse>;
  onMutate: (variables: EditCommentVariables) => Promise<CommentThreadContext>;
  onError: (error: Error, variables: EditCommentVariables, context: CommentThreadContext | undefined) => void;
  onSettled: (
    data: CommentResponse | undefined,
    error: Error | null,
    variables: EditCommentVariables,
    context: CommentThreadContext | undefined,
  ) => Promise<void>;
}

/**
 * Optimistic EDIT recipe: re-stamps body + mention set + `editedAt` on the target row in place (the
 * whole-replace posture), rolling back on error. LWW — the server response is simply the newest truth.
 */
export function editCommentMutationOptions(queryClient: QueryClient): EditCommentOptions {
  return {
    mutationFn: async ({ commentId, body, mentionedUserIds }: EditCommentVariables): Promise<CommentResponse> => {
      const { data, error } = await apiClient.PATCH("/api/comments/{commentId}", {
        params: { path: { commentId } },
        body: { body, mentionedUserIds },
      });
      if (error || !data) {
        const errorCode = (error as ProblemDetails | undefined)?.errorCode;
        throw new Error(mapError(errorCode).message);
      }
      return data;
    },

    onMutate: async (variables: EditCommentVariables): Promise<CommentThreadContext> => {
      const key = commentsKey(variables.taskId);
      await queryClient.cancelQueries({ queryKey: key });
      const previousThread = queryClient.getQueryData<CommentListResponse>(key);

      queryClient.setQueryData<CommentListResponse>(key, (old) =>
        old == null
          ? old
          : {
              ...old,
              comments: old.comments.map((c) =>
                c.id === variables.commentId
                  ? {
                      ...c,
                      body: variables.body,
                      mentions: variables.mentions,
                      editedAt: new Date().toISOString(),
                    }
                  : c,
              ),
            },
      );

      return { previousThread };
    },

    onError: (_error, variables, context): void => {
      queryClient.setQueryData<CommentListResponse | undefined>(
        commentsKey(variables.taskId),
        context?.previousThread,
      );
    },

    onSettled: async (data, _error, variables): Promise<void> => {
      if (data) {
        queryClient.setQueryData<CommentListResponse>(commentsKey(variables.taskId), (old) =>
          old == null
            ? old
            : { ...old, comments: old.comments.map((c) => (c.id === data.id ? data : c)) },
        );
      }
      await queryClient.invalidateQueries({ queryKey: commentsKey(variables.taskId) });
    },
  };
}

/* ─────────────────────────────── DELETE ─────────────────────────────── */

export interface DeleteCommentVariables {
  taskId: string;
  commentId: string;
}

interface DeleteCommentOptions {
  mutationFn: (variables: DeleteCommentVariables) => Promise<void>;
  onMutate: (variables: DeleteCommentVariables) => Promise<CommentThreadContext>;
  onError: (error: Error, variables: DeleteCommentVariables, context: CommentThreadContext | undefined) => void;
  onSettled: (
    data: void | undefined,
    error: Error | null,
    variables: DeleteCommentVariables,
    context: CommentThreadContext | undefined,
  ) => Promise<void>;
}

/**
 * Optimistic DELETE recipe: removes the row so the comment leaves the thread within one frame (AS-04 —
 * the server soft-deletes with the 30s reaper window behind it, R5); a rolled-back delete reappears at
 * its original index. Version-free and idempotent — no 409 path.
 */
export function deleteCommentMutationOptions(queryClient: QueryClient): DeleteCommentOptions {
  return {
    mutationFn: async ({ commentId }: DeleteCommentVariables): Promise<void> => {
      const { error } = await apiClient.DELETE("/api/comments/{commentId}", {
        params: { path: { commentId } },
      });
      if (error) {
        const errorCode = (error as ProblemDetails | undefined)?.errorCode;
        throw new Error(mapError(errorCode).message);
      }
    },

    onMutate: async (variables: DeleteCommentVariables): Promise<CommentThreadContext> => {
      const key = commentsKey(variables.taskId);
      await queryClient.cancelQueries({ queryKey: key });
      const previousThread = queryClient.getQueryData<CommentListResponse>(key);

      queryClient.setQueryData<CommentListResponse>(key, (old) =>
        old == null
          ? old
          : { ...old, comments: old.comments.filter((c) => c.id !== variables.commentId) },
      );

      return { previousThread };
    },

    onError: (_error, variables, context): void => {
      // Restore the full ordering — the removed row reappears at its original index.
      queryClient.setQueryData<CommentListResponse | undefined>(
        commentsKey(variables.taskId),
        context?.previousThread,
      );
    },

    onSettled: async (_data, _error, variables): Promise<void> => {
      await queryClient.invalidateQueries({ queryKey: commentsKey(variables.taskId) });
    },
  };
}

/* ─────────────────────────────── HOOK WRAPPER ─────────────────────────────── */

/** The caller identity + resolved mention chips the wrapper stamps onto the optimistic paints. */
export interface CommentAuthor {
  userId: string;
  displayName: string;
}

/**
 * "use client" hook wrapper for one task's thread. `postComment`/`editComment` validate at the trust
 * boundary (Constitution VI, `commentSchema`) and resolve the optimistic mention chips from the picker's
 * roster rows; `deleteComment` drives the optimistic remove. The caller identity comes from the session
 * (for the optimistic row only — the server resolves the real author from `ICurrentUser`, FR-068).
 */
export function useCommentMutations(taskId: string, author: CommentAuthor) {
  const queryClient = useQueryClient();
  const postMutation = useMutation<CommentResponse, Error, PostCommentVariables, CommentThreadContext>(
    postCommentMutationOptions(queryClient),
  );
  const editMutation = useMutation<CommentResponse, Error, EditCommentVariables, CommentThreadContext>(
    editCommentMutationOptions(queryClient),
  );
  const deleteMutation = useMutation<void, Error, DeleteCommentVariables, CommentThreadContext>(
    deleteCommentMutationOptions(queryClient),
  );

  const postComment = (body: string, mentions: CommentMention[]): void => {
    const parsed = commentSchema.parse({
      body,
      mentionedUserIds: mentions.map((m) => m.userId).filter((id): id is string => id != null),
    });
    postMutation.mutate({
      taskId,
      optimisticId: newTaskId(),
      body: parsed.body,
      mentionedUserIds: parsed.mentionedUserIds,
      authorId: author.userId,
      authorDisplayName: author.displayName,
      mentions,
    });
  };

  const editComment = (commentId: string, body: string, mentions: CommentMention[]): void => {
    const parsed = commentSchema.parse({
      body,
      mentionedUserIds: mentions.map((m) => m.userId).filter((id): id is string => id != null),
    });
    editMutation.mutate({
      taskId,
      commentId,
      body: parsed.body,
      mentionedUserIds: parsed.mentionedUserIds,
      mentions,
    });
  };

  const deleteComment = (commentId: string): void => {
    deleteMutation.mutate({ taskId, commentId });
  };

  return { postComment, editComment, deleteComment };
}

// @vitest-environment node
import { QueryClient } from "@tanstack/react-query";
import { beforeEach, describe, expect, it, vi } from "vitest";

import type { components } from "@/lib/api/generated/schema";
import {
  type CommentThreadContext,
  commentsKey,
  type DeleteCommentVariables,
  deleteCommentMutationOptions,
  type EditCommentVariables,
  editCommentMutationOptions,
  type PostCommentVariables,
  postCommentMutationOptions,
} from "@/hooks/useCommentMutations";

/**
 * The OPTIMISTIC comment mutation family (T031, RED — covers T032/T033; slice 009, R14, Constitution III).
 * Comments are the one slice-009 mutation family the spec routes to the slice-002/004 optimistic +
 * rollback recipe (deliberately unlike slice-007's non-optimistic membership): post APPENDS an optimistic
 * row, edit RE-STAMPS body/mentions/editedAt in place, delete REMOVES the row — each against the single
 * `['tasks', taskId, 'comments']` key, snapshotting in `onMutate`, ROLLING BACK on error, and reconciling
 * with server truth on settle. LWW: no version rides on any wire call (R2).
 */

type CommentResponse = components["schemas"]["CommentResponse"];
type CommentListResponse = components["schemas"]["CommentListResponse"];

vi.mock("@/lib/api/client", async (importOriginal) => {
  const actual = await importOriginal<typeof import("@/lib/api/client")>();
  return {
    ...actual,
    apiClient: {
      GET: vi.fn(),
      POST: vi.fn(),
      PATCH: vi.fn(),
      DELETE: vi.fn(),
    },
  };
});

const { apiClient } = await import("@/lib/api/client");
const postSpy = apiClient.POST as unknown as ReturnType<typeof vi.fn>;
const patchSpy = apiClient.PATCH as unknown as ReturnType<typeof vi.fn>;
const deleteSpy = apiClient.DELETE as unknown as ReturnType<typeof vi.fn>;

const TASK_ID = "aaaaaaaa-aaaa-7aaa-8aaa-aaaaaaaaaaaa";
const CALLER = "bbbbbbbb-bbbb-7bbb-8bbb-bbbbbbbbbbbb";
const OTHER = "cccccccc-cccc-7ccc-8ccc-cccccccccccc";

function makeComment(overrides: Partial<CommentResponse> & Pick<CommentResponse, "id">): CommentResponse {
  return {
    taskId: TASK_ID,
    authorId: OTHER,
    authorDisplayName: "Someone Else",
    body: "seed comment",
    mentions: [],
    createdAt: "2026-07-01T10:00:00.000Z",
    editedAt: null,
    canEdit: false,
    ...overrides,
  };
}

function seedThread(): CommentListResponse {
  return {
    taskId: TASK_ID,
    comments: [
      makeComment({ id: "11111111-1111-7111-8111-111111111111" }),
      makeComment({
        id: "22222222-2222-7222-8222-222222222222",
        authorId: CALLER,
        authorDisplayName: "The Caller",
        body: "my own comment",
        canEdit: true,
      }),
    ],
  };
}

function primedClient(thread: CommentListResponse): QueryClient {
  const queryClient = new QueryClient();
  queryClient.setQueryData<CommentListResponse>(commentsKey(TASK_ID), thread);
  return queryClient;
}

function threadOf(queryClient: QueryClient): CommentListResponse | undefined {
  return queryClient.getQueryData<CommentListResponse>(commentsKey(TASK_ID));
}

beforeEach(() => {
  postSpy.mockReset();
  patchSpy.mockReset();
  deleteSpy.mockReset();
});

/* ─────────────────────────────── POST ─────────────────────────────── */

describe("postCommentMutationOptions", () => {
  const variables: PostCommentVariables = {
    taskId: TASK_ID,
    optimisticId: "99999999-9999-7999-8999-999999999999",
    body: "a fresh comment",
    mentionedUserIds: [OTHER],
    authorId: CALLER,
    authorDisplayName: "The Caller",
    mentions: [{ userId: OTHER, displayName: "Someone Else" }],
  };

  it("onMutate appends an optimistic own row (canEdit=true) within the same tick", async () => {
    const queryClient = primedClient(seedThread());
    const options = postCommentMutationOptions(queryClient);

    const context = await options.onMutate(variables);

    const thread = threadOf(queryClient);
    expect(thread?.comments).toHaveLength(3);
    const optimistic = thread?.comments[2];
    expect(optimistic?.id).toBe(variables.optimisticId);
    expect(optimistic?.body).toBe("a fresh comment");
    expect(optimistic?.authorId).toBe(CALLER);
    expect(optimistic?.canEdit).toBe(true);
    expect(optimistic?.editedAt).toBeNull();
    expect(optimistic?.mentions).toEqual([{ userId: OTHER, displayName: "Someone Else" }]);
    expect(context.previousThread?.comments).toHaveLength(2);
  });

  it("mutationFn POSTs the body + typed mention tokens to the taskId-scoped route", async () => {
    const queryClient = primedClient(seedThread());
    const options = postCommentMutationOptions(queryClient);
    postSpy.mockResolvedValue({ data: makeComment({ id: variables.optimisticId }), error: undefined });

    await options.mutationFn(variables);

    expect(postSpy).toHaveBeenCalledWith("/api/tasks/{taskId}/comments", {
      params: { path: { taskId: TASK_ID } },
      body: { body: "a fresh comment", mentionedUserIds: [OTHER] },
    });
  });

  it("onError rolls the optimistic append back", async () => {
    const queryClient = primedClient(seedThread());
    const options = postCommentMutationOptions(queryClient);

    const context = await options.onMutate(variables);
    expect(threadOf(queryClient)?.comments).toHaveLength(3);

    options.onError(new Error("nope"), variables, context);

    expect(threadOf(queryClient)?.comments).toHaveLength(2);
  });

  it("onSettled invalidates the thread key (reconcile with server truth)", async () => {
    const queryClient = primedClient(seedThread());
    const invalidate = vi.spyOn(queryClient, "invalidateQueries");
    const options = postCommentMutationOptions(queryClient);

    await options.onSettled(undefined, null, variables, undefined);

    expect(invalidate).toHaveBeenCalledWith({ queryKey: commentsKey(TASK_ID) });
  });
});

/* ─────────────────────────────── EDIT ─────────────────────────────── */

describe("editCommentMutationOptions", () => {
  const variables: EditCommentVariables = {
    taskId: TASK_ID,
    commentId: "22222222-2222-7222-8222-222222222222",
    body: "my own comment, revised",
    mentionedUserIds: [],
    mentions: [],
  };

  it("onMutate re-stamps body + mentions + editedAt on the target row in place (whole replace)", async () => {
    const queryClient = primedClient(seedThread());
    const options = editCommentMutationOptions(queryClient);

    const context = await options.onMutate(variables);

    const row = threadOf(queryClient)?.comments.find((c) => c.id === variables.commentId);
    expect(row?.body).toBe("my own comment, revised");
    expect(row?.mentions).toEqual([]);
    expect(row?.editedAt).not.toBeNull();
    // The sibling row is untouched.
    expect(threadOf(queryClient)?.comments[0]?.body).toBe("seed comment");
    expect(context.previousThread?.comments[1]?.body).toBe("my own comment");
  });

  it("mutationFn PATCHes the flat commentId-scoped route with the whole replacement (no version — LWW)", async () => {
    const queryClient = primedClient(seedThread());
    const options = editCommentMutationOptions(queryClient);
    patchSpy.mockResolvedValue({ data: makeComment({ id: variables.commentId }), error: undefined });

    await options.mutationFn(variables);

    expect(patchSpy).toHaveBeenCalledWith("/api/comments/{commentId}", {
      params: { path: { commentId: variables.commentId } },
      body: { body: "my own comment, revised", mentionedUserIds: [] },
    });
  });

  it("onError rolls the edit back to the snapshot", async () => {
    const queryClient = primedClient(seedThread());
    const options = editCommentMutationOptions(queryClient);

    const context = await options.onMutate(variables);
    options.onError(new Error("nope"), variables, context);

    const row = threadOf(queryClient)?.comments.find((c) => c.id === variables.commentId);
    expect(row?.body).toBe("my own comment");
    expect(row?.editedAt).toBeNull();
  });
});

/* ─────────────────────────────── DELETE ─────────────────────────────── */

describe("deleteCommentMutationOptions", () => {
  const variables: DeleteCommentVariables = {
    taskId: TASK_ID,
    commentId: "22222222-2222-7222-8222-222222222222",
  };

  it("onMutate optimistically removes the row (it leaves the thread within one frame)", async () => {
    const queryClient = primedClient(seedThread());
    const options = deleteCommentMutationOptions(queryClient);

    const context = await options.onMutate(variables);

    expect(threadOf(queryClient)?.comments.map((c) => c.id)).toEqual([
      "11111111-1111-7111-8111-111111111111",
    ]);
    expect(context.previousThread?.comments).toHaveLength(2);
  });

  it("mutationFn DELETEs the flat commentId-scoped route (no version — idempotent soft-delete)", async () => {
    const queryClient = primedClient(seedThread());
    const options = deleteCommentMutationOptions(queryClient);
    deleteSpy.mockResolvedValue({ data: undefined, error: undefined });

    await options.mutationFn(variables);

    expect(deleteSpy).toHaveBeenCalledWith("/api/comments/{commentId}", {
      params: { path: { commentId: variables.commentId } },
    });
  });

  it("onError restores the removed row at its original index", async () => {
    const queryClient = primedClient(seedThread());
    const options = deleteCommentMutationOptions(queryClient);

    const context = await options.onMutate(variables);
    options.onError(new Error("nope"), variables, context);

    expect(threadOf(queryClient)?.comments.map((c) => c.id)).toEqual([
      "11111111-1111-7111-8111-111111111111",
      "22222222-2222-7222-8222-222222222222",
    ]);
  });

  it("onSettled invalidates the thread key", async () => {
    const queryClient = primedClient(seedThread());
    const invalidate = vi.spyOn(queryClient, "invalidateQueries");
    const options = deleteCommentMutationOptions(queryClient);

    await options.onSettled(undefined, null, variables, undefined);

    expect(invalidate).toHaveBeenCalledWith({ queryKey: commentsKey(TASK_ID) });
  });
});

/** The context type is shared across the three recipes — one snapshot shape (compile-time pin). */
const _contextPin: CommentThreadContext = { previousThread: undefined };
void _contextPin;

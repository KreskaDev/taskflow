import { z } from "zod";

/**
 * The maximum comment body length in characters (slice 009, FR-098) — the client mirror of the frozen
 * T002 surface (`Comment.MaxBodyLength = 4000` on the API side, enforced there by FluentValidation + the
 * EF CHECK backstop). Keep the two in lockstep.
 */
export const MAX_COMMENT_LENGTH = 4000;

/** The mention-set bound, mirroring the server's `PostCommentValidator.MaxMentions` (ASM-10). */
export const MAX_COMMENT_MENTIONS = 50;

/**
 * Comment-payload validation (slice 009, Constitution VI "Zod at every trust boundary"). Mirrors the
 * server's `PostCommentValidator`/`EditCommentValidator`: `body` trimmed → non-empty, ≤
 * {@link MAX_COMMENT_LENGTH}; `mentionedUserIds` is the TYPED @mention token set (uuids — never scraped
 * from prose, R6), bounded, defaulting to empty. Mention candidacy (current-members-only) is the
 * server-side cross-row rule — the picker only offers members, so the client schema stays shape-only.
 */
export const commentSchema = z.object({
  body: z.string().trim().min(1).max(MAX_COMMENT_LENGTH),
  mentionedUserIds: z.array(z.string().uuid()).max(MAX_COMMENT_MENTIONS).default([]),
});

/** The post-comment payload (`POST /api/tasks/{taskId}/comments`). */
export type PostCommentInput = z.infer<typeof commentSchema>;

/** The edit-comment payload (`PATCH /api/comments/{commentId}`) — the same whole-replace shape. */
export type EditCommentInput = z.infer<typeof commentSchema>;

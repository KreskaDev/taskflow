"use client";

import { useState } from "react";

import { CommentComposer } from "@/components/tasks/CommentComposer";
import { CommentItem } from "@/components/tasks/CommentItem";
import { DeleteCommentDialog } from "@/components/tasks/DeleteCommentDialog";
import type { CommentMention, CommentResponse } from "@/hooks/useComments";
import type { MemberResponse } from "@/hooks/useProjectMembers";

interface CommentThreadProps {
  taskId: string;
  /** The chronological live thread (soft-deleted rows never arrive — the server filters them, R5). */
  comments: CommentResponse[];
  /** The caller's effective role on the parent shared project ("owner" | "editor" | "viewer" | null). */
  role: string | null;
  /** Mention candidates — the CURRENT members roster (slice 007, R6). */
  members: MemberResponse[];
  onPost: (body: string, mentions: CommentMention[]) => void;
  onEdit: (commentId: string, body: string, mentions: CommentMention[]) => void;
  onDelete: (commentId: string) => void;
}

/**
 * The comment thread (slice 009, T037; AS-01..AS-04, R14/R15). Chronological; a VIEWER is presented with
 * NO composer (AS-03 — gated on the read-model role; the server stays authoritative, a direct post is
 * 403, FR-068). Editing swaps the item for a pre-seeded composer (whole-body + whole-mention-set replace);
 * deleting confirms via the FR-101 {@link DeleteCommentDialog} and paints optimistically (the comment
 * leaves the thread within one frame — the parent's optimistic mutation family, Constitution III).
 */
export function CommentThread({
  taskId,
  comments,
  role,
  members,
  onPost,
  onEdit,
  onDelete,
}: CommentThreadProps) {
  const [editingId, setEditingId] = useState<string | null>(null);
  const [deletingId, setDeletingId] = useState<string | null>(null);

  const canCompose = role === "owner" || role === "editor";

  return (
    // No own aria-label: the drawer's comment SECTION (DrawerCommentSection) is the single
    // "Komentarze" landmark — a second nested one would duplicate the region name (T053).
    <section className="tf-comment-thread">
      {comments.length === 0 ? (
        <p className="tf-comment-thread__empty">Brak komentarzy.</p>
      ) : (
        <ol className="tf-comment-thread__list">
          {comments.map((comment) => (
            <li key={comment.id} className="tf-comment-thread__item">
              {editingId === comment.id ? (
                <CommentComposer
                  members={members}
                  initialBody={comment.body}
                  initialMentions={comment.mentions}
                  submitLabel="Zapisz zmiany"
                  onSubmit={(body, mentions) => {
                    onEdit(comment.id, body, mentions);
                    setEditingId(null);
                  }}
                  onCancel={() => setEditingId(null)}
                />
              ) : (
                <CommentItem
                  comment={comment}
                  onStartEdit={() => setEditingId(comment.id)}
                  onRequestDelete={() => setDeletingId(comment.id)}
                />
              )}
            </li>
          ))}
        </ol>
      )}

      {canCompose ? (
        <CommentComposer
          key={taskId}
          members={members}
          submitLabel="Dodaj komentarz"
          onSubmit={onPost}
        />
      ) : null}

      <DeleteCommentDialog
        open={deletingId != null}
        onClose={() => setDeletingId(null)}
        onConfirm={() => {
          if (deletingId != null) onDelete(deletingId);
        }}
      />
    </section>
  );
}

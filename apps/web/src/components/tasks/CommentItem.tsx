"use client";

import { Avatar } from "@/components/ui/Avatar";
import type { CommentResponse } from "@/hooks/useComments";
import { SafeMarkdown } from "@/lib/markdown/safeMarkdown";
import { formatInReferenceZone } from "@/lib/timezone";

interface CommentItemProps {
  comment: CommentResponse;
  /** Begin editing this comment (only offered when `canEdit`). */
  onStartEdit: () => void;
  /** Ask to delete this comment (opens the confirm dialog; only offered when `canEdit`). */
  onRequestDelete: () => void;
}

/**
 * A single thread entry (slice 009, T037; AS-01/AS-04, R8/R10/R15). The body renders EXCLUSIVELY through
 * {@link SafeMarkdown} (the stored-XSS render boundary, FR-098); the author display name is React-escaped
 * text and is already tombstone-safe ("Deleted user") from the read model (R11). The timestamp shows a
 * relative label with the absolute Europe/Warsaw wall-clock on `title` (R10 — the existing `timezone.ts`
 * util, no new date library). The edit/delete affordances are real, keyboard-reachable buttons (FR-046 —
 * never hover-only), offered only when `canEdit` (a UI convenience — the author-only server gate stays
 * authoritative, FR-068). @mentions render as chips from the TYPED token set, never scraped from prose (R6).
 */
export function CommentItem({ comment, onStartEdit, onRequestDelete }: CommentItemProps) {
  const created = new Date(comment.createdAt);
  const absolute = formatInReferenceZone(created, "dd.MM.yyyy HH:mm");

  return (
    <article className="tf-comment" aria-label={`Komentarz: ${comment.authorDisplayName}`}>
      <header className="tf-comment__header">
        {/* Identity as an avatar wherever authorship shows (FR-105, T026). The API exposes
            no avatarUrl for other users, so this renders the deterministic initials
            fallback; a null authorId (deleted user) keys the tombstone-safe bucket. */}
        <Avatar
          userId={comment.authorId ?? "deleted-user"}
          displayName={comment.authorDisplayName}
          size="md"
        />{" "}
        <span className="tf-comment__author">{comment.authorDisplayName}</span>{" "}
        <time className="tf-comment__time" dateTime={comment.createdAt} title={absolute}>
          {relativeLabel(created)}
        </time>
        {comment.editedAt != null ? (
          <span
            className="tf-comment__edited"
            title={formatInReferenceZone(new Date(comment.editedAt), "dd.MM.yyyy HH:mm")}
          >
            {" "}
            (edytowano)
          </span>
        ) : null}
      </header>

      <div className="tf-comment__body">
        <SafeMarkdown body={comment.body} />
      </div>

      {comment.mentions.length > 0 ? (
        <ul className="tf-comment__mentions" aria-label="Wzmianki">
          {comment.mentions.map((m, index) => (
            <li key={m.userId ?? `tombstone-${index}`} className="tf-comment__mention">
              @{m.displayName}
            </li>
          ))}
        </ul>
      ) : null}

      {comment.canEdit ? (
        <div className="tf-comment__actions">
          <button type="button" className="tf-button tf-button--secondary" onClick={onStartEdit}>
            Edytuj
          </button>
          <button type="button" className="tf-button tf-button--secondary" onClick={onRequestDelete}>
            Usuń
          </button>
        </div>
      ) : null}
    </article>
  );
}

/**
 * A coarse relative label ("przed chwilą" / "5 min temu" / "3 godz. temu"), falling back to the absolute
 * Warsaw date for older comments (R10). Coarse buckets keep the label stable between renders — the exact
 * instant always rides on `title`.
 */
function relativeLabel(created: Date, now: Date = new Date()): string {
  const minutes = Math.floor((now.getTime() - created.getTime()) / 60_000);
  if (minutes < 1) return "przed chwilą";
  if (minutes < 60) return `${minutes} min temu`;
  const hours = Math.floor(minutes / 60);
  if (hours < 24) return `${hours} godz. temu`;
  return formatInReferenceZone(created, "dd.MM.yyyy");
}

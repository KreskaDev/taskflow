"use client";

import { useState } from "react";
import { X } from "lucide-react";

import { MentionPicker } from "@/components/tasks/MentionPicker";
import type { CommentMention } from "@/hooks/useComments";
import type { MemberResponse } from "@/hooks/useProjectMembers";
import { commentSchema, MAX_COMMENT_LENGTH } from "@/lib/validation/comment";

interface CommentComposerProps {
  /** Mention candidates — the CURRENT members of the task's shared project (slice-007 roster, R6). */
  members: MemberResponse[];
  /** Seed body when editing an existing comment (empty for a fresh post). */
  initialBody?: string;
  /** Seed mention chips when editing (the whole set is replaced on save — R6). */
  initialMentions?: CommentMention[];
  /** The submit affordance label ("Dodaj komentarz" / "Zapisz zmiany"). */
  submitLabel: string;
  /** Commit the composed body + typed mention set. */
  onSubmit: (body: string, mentions: CommentMention[]) => void;
  /** Cancel an in-progress edit (absent for the fresh-post composer). */
  onCancel?: () => void;
}

/**
 * The comment composer (slice 009, T038; AS-01/AS-02, R6/R8/R14). A plain `<textarea>`; since slice 019
 * removed the single-key shortcut system entirely (FR-111), typing any character here is never hijacked
 * by design — there are no bare-key bindings left to suppress. Submits on
 * Ctrl+Enter or the button. Validation (trim → non-empty, ≤ 4000) runs at the trust boundary with an
 * actionable FR-049 message; the server re-validates authoritatively (422). @mentions are added ONLY via
 * the {@link MentionPicker}'s typed tokens — a literal `@` typed in prose stays inert text (R6).
 */
export function CommentComposer({
  members,
  initialBody = "",
  initialMentions = [],
  submitLabel,
  onSubmit,
  onCancel,
}: CommentComposerProps) {
  const [body, setBody] = useState(initialBody);
  const [mentions, setMentions] = useState<CommentMention[]>(initialMentions);
  const [pickerOpen, setPickerOpen] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const submit = (): void => {
    const candidate = {
      body,
      mentionedUserIds: mentions.map((m) => m.userId).filter((id): id is string => id != null),
    };
    const parsed = commentSchema.safeParse(candidate);
    if (!parsed.success) {
      setError(
        body.trim().length === 0
          ? "Komentarz nie może być pusty."
          : `Komentarz może mieć najwyżej ${MAX_COMMENT_LENGTH} znaków.`,
      );
      return;
    }
    setError(null);
    onSubmit(parsed.data.body, mentions);
    setBody("");
    setMentions([]);
  };

  const removeMention = (userId: string | null | undefined): void => {
    setMentions((prev) => prev.filter((m) => m.userId !== userId));
  };

  return (
    <div className="tf-comment-composer">
      <label className="tf-field">
        <span className="tf-sr-only">Treść komentarza</span>
        <textarea
          className="tf-comment-composer__input"
          value={body}
          maxLength={MAX_COMMENT_LENGTH + 1}
          rows={3}
          placeholder="Napisz komentarz… (Ctrl+Enter, aby wysłać)"
          onChange={(event) => setBody(event.target.value)}
          onKeyDown={(event) => {
            if (event.key === "Enter" && (event.ctrlKey || event.metaKey)) {
              event.preventDefault();
              submit();
            }
          }}
        />
      </label>

      {error != null ? (
        <p role="alert" className="tf-comment-composer__error">
          {error}
        </p>
      ) : null}

      {mentions.length > 0 ? (
        <ul className="tf-comment-composer__mentions" aria-label="Wybrane wzmianki">
          {mentions.map((m, index) => (
            <li key={m.userId ?? `tombstone-${index}`} className="tf-comment-composer__mention">
              @{m.displayName}
              <button
                type="button"
                className="tf-button tf-button--secondary"
                aria-label={`Usuń wzmiankę ${m.displayName}`}
                onClick={() => removeMention(m.userId)}
              >
                <X size={14} strokeWidth={1.75} aria-hidden="true" />
              </button>
            </li>
          ))}
        </ul>
      ) : null}

      <div className="tf-comment-composer__actions">
        <button
          type="button"
          className="tf-button tf-button--secondary"
          aria-haspopup="dialog"
          onClick={() => setPickerOpen(true)}
        >
          @ Wspomnij
        </button>
        <button type="button" className="tf-button" onClick={submit}>
          {submitLabel}
        </button>
        {onCancel ? (
          <button type="button" className="tf-button tf-button--secondary" onClick={onCancel}>
            Anuluj
          </button>
        ) : null}
      </div>

      <MentionPicker
        open={pickerOpen}
        members={members}
        chosen={mentions.map((m) => m.userId).filter((id): id is string => id != null)}
        onClose={() => setPickerOpen(false)}
        onPick={(member) =>
          setMentions((prev) =>
            prev.some((m) => m.userId === member.userId)
              ? prev
              : [...prev, { userId: member.userId, displayName: member.displayName }],
          )
        }
      />
    </div>
  );
}

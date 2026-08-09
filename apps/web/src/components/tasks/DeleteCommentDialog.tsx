"use client";

import { Dialog } from "@/components/ui/Dialog";

const TITLE_ID = "delete-comment-title";
const DESCRIPTION_ID = "delete-comment-description";

interface DeleteCommentDialogProps {
  open: boolean;
  /** Dismiss without deleting (Esc / overlay / Anuluj) — returns focus to the invoking button. */
  onClose: () => void;
  /** Confirm the delete (the parent drives the optimistic soft-delete). */
  onConfirm: () => void;
}

/**
 * The delete-comment confirmation (slice 009, T038; AS-04, FR-101). A modal {@link Dialog} — initial
 * focus, trap, Esc-dismiss, focus return — fully keyboard-operable (Constitution I/II). Confirming
 * soft-deletes on the server (the 30s `ReapDeletedComment` undo substrate, R5; the user-facing restore is
 * the slice-014 seam, so no undo toast is offered here — matching task-delete's shipped scope).
 */
export function DeleteCommentDialog({ open, onClose, onConfirm }: DeleteCommentDialogProps) {
  if (!open) return null;

  return (
    <Dialog open={open} onClose={onClose} titleId={TITLE_ID} descriptionId={DESCRIPTION_ID}>
      <h2 id={TITLE_ID} className="tf-dialog__title">
        Usunąć komentarz?
      </h2>
      <p id={DESCRIPTION_ID}>Komentarz zniknie z wątku.</p>
      <div className="tf-dialog__actions">
        <button
          type="button"
          className="tf-button"
          onClick={() => {
            onConfirm();
            onClose();
          }}
        >
          Usuń
        </button>
        <button type="button" className="tf-button tf-button--secondary" onClick={onClose}>
          Anuluj (Esc)
        </button>
      </div>
    </Dialog>
  );
}

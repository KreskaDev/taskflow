"use client";

import { Avatar } from "@/components/ui/Avatar";
import { Button } from "@/components/ui/Button";
import { Dialog, DialogActions, DialogTitle } from "@/components/ui/Dialog";
import dialogStyles from "@/components/ui/Dialog.module.css";
import type { MemberResponse } from "@/hooks/useProjectMembers";

const TITLE_ID = "mention-picker-title";

interface MentionPickerProps {
  /** Whether the picker is open (the composer's @ trigger opened it). */
  open: boolean;
  /** Mention candidates — the CURRENT members of the task's shared project (the slice-007 roster, R6). */
  members: MemberResponse[];
  /** The already-chosen mention user ids (rendered disabled — the set is de-duplicated). */
  chosen: string[];
  /** Dismiss without picking (Esc / overlay click) — returns focus to the composer. */
  onClose: () => void;
  /** Pick one member to mention (the composer adds the typed token + chip). */
  onPick: (member: MemberResponse) => void;
}

/**
 * The @mention picker (slice 009, T038; AS-02, R6/R14). A modal {@link Dialog} (FR-101 focus contract)
 * listing the shared project's CURRENT members — sourced from the EXISTING slice-007
 * `GET /api/projects/{id}/members` roster; this slice adds no candidate-lookup endpoint. Picking adds a
 * TYPED User-id token (never prose parsing — a literal `@` typed in the composer is inert text). Names
 * are React-escaped text (FR-099); keyboard-operable buttons (FR-046).
 */
export function MentionPicker({ open, members, chosen, onClose, onPick }: MentionPickerProps) {
  if (!open) return null;

  return (
    <Dialog open={open} onClose={onClose} titleId={TITLE_ID}>
      <DialogTitle id={TITLE_ID}>Wspomnij osobę</DialogTitle>

      {members.length === 0 ? (
        <p className={dialogStyles.muted}>Brak członków do wspomnienia.</p>
      ) : (
        <ul className={dialogStyles.optionList}>
          {members.map((m) => {
            const alreadyChosen = chosen.includes(m.userId);
            return (
              <li key={m.userId}>
                <Button
                  variant="secondary"
                  disabled={alreadyChosen}
                  onClick={() => {
                    onPick(m);
                    onClose();
                  }}
                >
                  <Avatar userId={m.userId} displayName={m.displayName} size="sm" />
                  {" @"}
                  {m.displayName}
                  {m.isOwner ? <span className="sr-only"> (właściciel)</span> : null}
                  {alreadyChosen ? <span className="sr-only"> (już wspomniano)</span> : null}
                </Button>
              </li>
            );
          })}
        </ul>
      )}

      <DialogActions>
        <Button variant="secondary" onClick={onClose}>
          Anuluj (Esc)
        </Button>
      </DialogActions>
    </Dialog>
  );
}

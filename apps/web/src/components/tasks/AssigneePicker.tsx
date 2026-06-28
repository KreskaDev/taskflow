"use client";

import { useState } from "react";

import { Dialog } from "@/components/ui/Dialog";
import { useProjectMembers } from "@/hooks/useProjectMembers";

const TITLE_ID = "assignee-picker-title";

interface AssigneePickerProps {
  /** Whether the picker is open (the `A` key opened it for the selected shared-project task). */
  open: boolean;
  /** The task's project id (the roster source). */
  projectId: string;
  /** The task's current assignee ids (seeds the checked set). */
  current: string[];
  /** Dismiss without saving (Esc / overlay click) — returns focus to the originating row. */
  onClose: () => void;
  /** Commit the chosen assignee set (the parent calls `setTaskAssignees`). */
  onSubmit: (assigneeIds: string[]) => void;
}

/**
 * The `A` assignee picker (slice 008, AS-01/AS-02). A modal {@link Dialog} (FR-101 focus contract) listing
 * the shared project's members (the slice-007 roster) as keyboard-operable checkboxes; the owner and every
 * member are assignable (a member may assign themselves). Member names are React-escaped text (FR-099), never
 * avatar-color alone (FR-044). Saving commits the whole set via `setTaskAssignees`; Esc discards. The
 * membership-validity rule is enforced server-side (the picker only offers current members).
 */
export function AssigneePicker({ open, projectId, current, onClose, onSubmit }: AssigneePickerProps) {
  const { data, isPending, isError } = useProjectMembers(projectId, open);
  const [selected, setSelected] = useState<Set<string>>(() => new Set(current));

  const toggle = (userId: string) =>
    setSelected((prev) => {
      const next = new Set(prev);
      if (next.has(userId)) next.delete(userId);
      else next.add(userId);
      return next;
    });

  const save = () => onSubmit([...selected]);

  if (!open) return null;

  return (
    <Dialog open={open} onClose={onClose} titleId={TITLE_ID}>
      <div
        onKeyDown={(event) => {
          if (event.key === "Enter" && (event.ctrlKey || event.metaKey)) {
            event.preventDefault();
            save();
          }
        }}
      >
        <h2 id={TITLE_ID} className="tf-dialog__title">
          Przypisz osoby
        </h2>

        {isError ? (
          <p role="alert" className="tf-reschedule-input__error">
            Nie udało się wczytać listy członków.
          </p>
        ) : isPending ? (
          <p className="tf-daily-view__empty">Wczytywanie…</p>
        ) : (
          <ul className="tf-assignee-picker__list">
            {(data?.members ?? []).map((m) => (
              <li key={m.userId} className="tf-assignee-picker__item">
                <label className="tf-field tf-field--inline">
                  <input
                    type="checkbox"
                    checked={selected.has(m.userId)}
                    onChange={() => toggle(m.userId)}
                  />
                  <span>
                    {m.displayName}
                    {m.isOwner ? <span className="tf-sr-only"> (właściciel)</span> : null}
                  </span>
                </label>
              </li>
            ))}
          </ul>
        )}

        <div className="tf-dialog__actions">
          <button type="button" className="tf-button" onClick={save}>
            Zapisz (Ctrl+Enter)
          </button>
          <button type="button" className="tf-button tf-button--secondary" onClick={onClose}>
            Anuluj (Esc)
          </button>
        </div>
      </div>
    </Dialog>
  );
}

"use client";

import { useRef, useState } from "react";

import { Dialog } from "@/components/ui/Dialog";
import { useLabelMutations, useLabelRoster } from "@/hooks/useLabels";
import { labelNameSchema } from "@/lib/validation/label";

const TITLE_ID = "label-selector-title";

interface LabelSelectorProps {
  /** Whether the selector is open (the `L` key opened it for the selected task). */
  open: boolean;
  /** The task's current CALLER-scoped label ids (seeds the checked set). */
  current: string[];
  /** Dismiss without saving (Esc / overlay click) — returns focus to the originating row. */
  onClose: () => void;
  /** Commit the chosen label set (the parent calls `setTaskLabels`). */
  onSubmit: (labelIds: string[]) => void;
}

/**
 * The `L` label selector (slice 006, US-08.AS-04). A modal {@link Dialog} (FR-101 focus contract: initial
 * focus, trap, Esc, return focus) listing the caller's labels (the per-user roster) as keyboard-operable
 * checkboxes, plus a type-to-create input. Toggling builds the desired set locally; typing a new name + Enter
 * creates the label (client-id idempotent PUT) and adds it to the set; Ctrl+Enter (or Save) commits the whole
 * set via `setTaskLabels`. Label NAMES are React-escaped text (FR-099); the preset color is decorative
 * (`data-color`), never the sole carrier (FR-044). A per-label Delete removes it from the caller's roster
 * (full CRUD; the server cascade clears its applications).
 */
export function LabelSelector({ open, current, onClose, onSubmit }: LabelSelectorProps) {
  const { data, isPending, isError } = useLabelRoster();
  const { createLabel, deleteLabel } = useLabelMutations();
  const [selected, setSelected] = useState<Set<string>>(() => new Set(current));
  const [draft, setDraft] = useState("");
  const createInputRef = useRef<HTMLInputElement>(null);

  const removeFromSelected = (id: string) =>
    setSelected((prev) => {
      const next = new Set(prev);
      next.delete(id);
      return next;
    });

  const toggle = (id: string) =>
    setSelected((prev) => {
      const next = new Set(prev);
      if (next.has(id)) next.delete(id);
      else next.add(id);
      return next;
    });

  const save = () => onSubmit([...selected]);

  const createFromDraft = async () => {
    const parsed = labelNameSchema.safeParse(draft);
    if (!parsed.success) return; // empty after trim: no-op, stay in the input
    const created = await createLabel(parsed.data);
    setSelected((prev) => new Set(prev).add(created.id));
    setDraft("");
  };

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
          Etykiety
        </h2>

        {isError ? (
          <p role="alert" className="tf-reschedule-input__error">
            Nie udało się wczytać etykiet.
          </p>
        ) : isPending ? (
          <p className="tf-daily-view__empty">Wczytywanie…</p>
        ) : (
          <ul className="tf-label-selector__list">
            {(data ?? []).map((label) => (
              <li key={label.id} className="tf-label-selector__item">
                <label className="tf-field tf-field--inline">
                  <input
                    type="checkbox"
                    checked={selected.has(label.id)}
                    onChange={() => toggle(label.id)}
                  />
                  <span className="tf-label-chip" data-color={label.color ?? undefined}>
                    {label.name}
                  </span>
                </label>
                <button
                  type="button"
                  className="tf-button tf-button--secondary"
                  aria-label={`Usuń etykietę ${label.name}`}
                  onClick={() => {
                    // Prune the id from the pending set so Save can't commit a just-deleted label (→ 422),
                    // and move focus to a stable element inside the dialog — the deleted row unmounts, so
                    // without this focus would fall to <body> and break the FR-101 focus trap.
                    removeFromSelected(label.id);
                    createInputRef.current?.focus();
                    deleteLabel(label.id).catch(() => {}); // the global announcer surfaces any error (FR-049)
                  }}
                >
                  Usuń
                </button>
              </li>
            ))}
          </ul>
        )}

        <div className="tf-field">
          <label htmlFor="label-create" className="tf-sr-only">
            Nowa etykieta
          </label>
          <input
            ref={createInputRef}
            id="label-create"
            type="text"
            className="tf-input"
            placeholder="Nowa etykieta…"
            maxLength={50}
            value={draft}
            onChange={(event) => setDraft(event.target.value)}
            onKeyDown={(event) => {
              // Plain Enter creates the draft; Ctrl/Meta+Enter is the Save chord (let it bubble to the
              // wrapper) — without this guard Ctrl+Enter would BOTH create and save, applying the set
              // before the new label's create lands.
              if (event.key === "Enter" && !event.ctrlKey && !event.metaKey) {
                event.preventDefault();
                createFromDraft().catch(() => {}); // the global announcer surfaces any error (FR-049)
              }
            }}
          />
        </div>

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

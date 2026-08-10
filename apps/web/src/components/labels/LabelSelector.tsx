"use client";

import { useRef, useState } from "react";

import { Button } from "@/components/ui/Button";
import { Dialog, DialogActions, DialogTitle } from "@/components/ui/Dialog";
import dialogStyles from "@/components/ui/Dialog.module.css";
import { Input } from "@/components/ui/Input";
import { useLabelMutations, useLabelRoster } from "@/hooks/useLabels";
import { labelNameSchema } from "@/lib/validation/label";
import styles from "./LabelSelector.module.css";

const TITLE_ID = "label-selector-title";

interface LabelSelectorProps {
  /** Whether the selector is open (the row menu's "Etykiety…" opened it). */
  open: boolean;
  /** The task's current CALLER-scoped label ids (seeds the checked set). */
  current: string[];
  /** Dismiss without saving (Esc / overlay click) — returns focus to the invoker. */
  onClose: () => void;
  /** Commit the chosen label set (the parent calls `setTaskLabels`). */
  onSubmit: (labelIds: string[]) => void;
}

/**
 * The label selector (slice 006, US-08.AS-04; migrated to the catalog in slice 019 — T061).
 * A modal {@link Dialog} (FR-101 focus contract) listing the caller's labels (the per-user
 * roster) as keyboard-operable checkboxes, plus a type-to-create input. Toggling builds the
 * desired set locally; typing a new name + Enter creates the label (client-id idempotent PUT)
 * and adds it to the set; Ctrl+Enter (or Save) commits the whole set via `setTaskLabels`.
 * Label NAMES are React-escaped text (FR-099); the preset color is decorative (`data-color`),
 * never the sole carrier (FR-044). A per-label Delete removes it from the caller's roster
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
        <DialogTitle id={TITLE_ID}>Etykiety</DialogTitle>

        {isError ? (
          <p role="alert" className={dialogStyles.dialogError}>
            Nie udało się wczytać etykiet.
          </p>
        ) : isPending ? (
          <p className={dialogStyles.muted}>Wczytywanie…</p>
        ) : (
          <ul className={dialogStyles.optionList}>
            {(data ?? []).map((label) => (
              <li key={label.id} className={styles.item}>
                <label className={dialogStyles.inlineRow}>
                  <input
                    type="checkbox"
                    checked={selected.has(label.id)}
                    onChange={() => toggle(label.id)}
                  />
                  <span className={styles.chip} data-color={label.color ?? undefined}>
                    {label.name}
                  </span>
                </label>
                <Button
                  variant="secondary"
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
                </Button>
              </li>
            ))}
          </ul>
        )}

        <label className={dialogStyles.fieldRow} htmlFor="label-create">
          <span className="sr-only">Nowa etykieta</span>
          <Input
            ref={createInputRef}
            id="label-create"
            type="text"
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
        </label>

        <DialogActions>
          <Button onClick={save}>Zapisz (Ctrl+Enter)</Button>
          <Button variant="secondary" onClick={onClose}>
            Anuluj (Esc)
          </Button>
        </DialogActions>
      </div>
    </Dialog>
  );
}

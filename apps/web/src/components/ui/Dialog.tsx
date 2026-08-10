"use client";

import { type ReactNode, useCallback, useEffect, useRef } from "react";
import styles from "./Dialog.module.css";

/**
 * The dialog's heading — carries the id the Dialog's `aria-labelledby` points at.
 * One catalog style for every dialog title (replaces the legacy `tf-dialog__title`).
 */
export function DialogTitle({ id, children }: { id: string; children: ReactNode }) {
  return (
    <h2 id={id} className={styles.title}>
      {children}
    </h2>
  );
}

/** The dialog's trailing action row (confirm/cancel — replaces `tf-dialog__actions`). */
export function DialogActions({ children }: { children: ReactNode }) {
  return <div className={styles.actions}>{children}</div>;
}

/** A labelled radio/checkbox choice group (disposition prompts — AS-10/EC-03 dialogs). */
export function DialogChoices({ legend, children }: { legend: ReactNode; children: ReactNode }) {
  return (
    <fieldset className={styles.choices}>
      <legend>{legend}</legend>
      {children}
    </fieldset>
  );
}

interface DialogProps {
  open: boolean;
  onClose: () => void;
  /** id of the element labelling the dialog (its title). */
  titleId: string;
  /** id of the element describing the dialog (optional). */
  descriptionId?: string;
  children: ReactNode;
}

const FOCUSABLE =
  'a[href], button:not([disabled]), textarea:not([disabled]), input:not([disabled]), select:not([disabled]), [tabindex]:not([tabindex="-1"])';

/**
 * Modal dialog implementing the FR-101 focus contract (Constitution II):
 * sets initial focus into the dialog, traps focus, dismisses on Esc, and returns
 * focus to the invoking element on close.
 */
export function Dialog({ open, onClose, titleId, descriptionId, children }: DialogProps) {
  const dialogRef = useRef<HTMLDivElement>(null);
  const invokerRef = useRef<HTMLElement | null>(null);

  const focusable = useCallback((): HTMLElement[] => {
    const root = dialogRef.current;
    if (!root) return [];
    return Array.from(root.querySelectorAll<HTMLElement>(FOCUSABLE));
  }, []);

  useEffect(() => {
    if (!open) return;

    // Remember the invoker so focus can return to it on close.
    invokerRef.current = document.activeElement as HTMLElement | null;

    // Initial focus into the dialog (first focusable, else the dialog itself).
    const elements = focusable();
    (elements[0] ?? dialogRef.current)?.focus();

    return () => {
      invokerRef.current?.focus();
    };
  }, [open, focusable]);

  const onKeyDown = useCallback(
    (event: React.KeyboardEvent<HTMLDivElement>) => {
      if (event.key === "Escape") {
        event.stopPropagation();
        onClose();
        return;
      }

      if (event.key !== "Tab") return;

      const elements = focusable();
      if (elements.length === 0) {
        event.preventDefault();
        return;
      }

      const first = elements[0]!;
      const last = elements[elements.length - 1]!;
      const active = document.activeElement;

      if (event.shiftKey && active === first) {
        event.preventDefault();
        last.focus();
      } else if (!event.shiftKey && active === last) {
        event.preventDefault();
        first.focus();
      }
    },
    [focusable, onClose],
  );

  if (!open) return null;

  return (
    <div className={styles.overlay} role="presentation" onClick={onClose}>
      <div
        ref={dialogRef}
        className={styles.dialog}
        role="dialog"
        aria-modal="true"
        aria-labelledby={titleId}
        aria-describedby={descriptionId}
        tabIndex={-1}
        onKeyDown={onKeyDown}
        onClick={(e) => e.stopPropagation()}
      >
        {children}
      </div>
    </div>
  );
}

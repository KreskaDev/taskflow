"use client";

import { useEffect, useRef, type ReactNode } from "react";
import { X } from "lucide-react";
import { IconButton } from "@/components/ui/IconButton";
import styles from "./Drawer.module.css";

interface DrawerProps {
  open: boolean;
  /** Close + return focus to the invoker (the trigger that opened the drawer). */
  onClose: () => void;
  /** id of the element labelling the drawer (its title). */
  titleId: string;
  children: ReactNode;
}

/** Is the event target a text field whose own Esc semantics win (FR-030)? */
function isTextField(target: EventTarget | null): boolean {
  if (!(target instanceof HTMLElement)) return false;
  if (target instanceof HTMLInputElement || target instanceof HTMLTextAreaElement) return true;
  if (target.hasAttribute("contenteditable") && target.getAttribute("contenteditable") !== "false") {
    return true;
  }
  return target.getAttribute("role") === "textbox";
}

/**
 * The catalog drawer (slice 019, T050 — D8, S4.2, FR-047): a right-side NON-MODAL panel
 * (420–480px docked; ≤1024px it overlays the list via CSS). NO focus trap — the list
 * behind stays fully interactive. Esc closes and returns focus to the invoker EXCEPT
 * while a text field inside is focused: the field's own Esc (cancel the edit — FR-030)
 * wins, and the drawer stays open. z-index from the token scale (40); transitions are
 * instant under `prefers-reduced-motion` (the global guard).
 */
export function Drawer({ open, onClose, titleId, children }: DrawerProps) {
  const invokerRef = useRef<HTMLElement | null>(null);

  useEffect(() => {
    if (!open) return;
    invokerRef.current = document.activeElement as HTMLElement | null;
    return () => {
      // Focus return on close — but never steal focus back if the user moved on to the
      // (still interactive) list in the meantime and focus is alive elsewhere.
      const active = document.activeElement;
      if (active === null || active === document.body) {
        invokerRef.current?.focus();
      }
    };
  }, [open]);

  if (!open) return null;

  const onKeyDown = (event: React.KeyboardEvent<HTMLElement>) => {
    if (event.key !== "Escape") return;
    if (isTextField(event.target)) {
      // The focused field's Esc cancels the FIELD edit first (FR-030); the drawer
      // consumes nothing and stays open.
      return;
    }
    event.stopPropagation();
    onClose();
  };

  return (
    // Non-modal complementary region — NOT role="dialog" with aria-modal, and no focus
    // trap: the surrounding page stays in the accessibility tree and interactive (S4.2).
    <aside
      className={styles.drawer}
      role="complementary"
      aria-labelledby={titleId}
      onKeyDown={onKeyDown}
    >
      <IconButton aria-label="Zamknij panel" className={styles.close} onClick={onClose}>
        <X size={16} strokeWidth={1.75} aria-hidden="true" />
      </IconButton>
      {children}
    </aside>
  );
}

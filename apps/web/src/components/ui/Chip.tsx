import type { ReactNode } from "react";
import styles from "./Chip.module.css";

interface ChipProps {
  /** The chip text — the NAME carries the meaning, never color alone (FR-044). */
  label: string;
  /** Optional label-color token rendered as a decorative dot (label variant). */
  color?: string | null;
  /** Optional leading slot (assignee variant: an Avatar). */
  leading?: ReactNode;
  /** When provided, renders the keyboard-operable remove affordance (FR-046). */
  onRemove?: () => void;
  /** Accessible name for the remove button — REQUIRED when onRemove is set. */
  removeLabel?: string;
  className?: string;
}

/**
 * Catalog chip (T011): label and assignee variants. Long values truncate with
 * ellipsis (`.text`); removal is a real button (keyboard equivalent for free).
 */
export function Chip({ label, color, leading, onRemove, removeLabel, className }: ChipProps) {
  return (
    <span className={[styles.chip, className].filter(Boolean).join(" ")}>
      {leading}
      {color ? <span className={styles.dot} data-chip-dot data-color={color} aria-hidden="true" /> : null}
      <span className={styles.text}>{label}</span>
      {onRemove ? (
        <button
          type="button"
          className={styles.remove}
          aria-label={removeLabel ?? `Usuń ${label}`}
          onClick={onRemove}
        >
          <svg viewBox="0 0 12 12" width="10" height="10" aria-hidden="true">
            <path d="M2 2l8 8M10 2l-8 8" stroke="currentColor" strokeWidth="1.5" />
          </svg>
        </button>
      ) : null}
    </span>
  );
}

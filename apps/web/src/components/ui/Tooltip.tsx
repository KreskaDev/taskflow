"use client";

import { useId, useState, type ReactNode } from "react";
import styles from "./Tooltip.module.css";

interface TooltipProps {
  /** The full value to reveal (S5.4 truncated-value reveal). */
  label: string;
  /** The (typically truncated) trigger content. */
  children: ReactNode;
  className?: string;
}

/**
 * Catalog tooltip equivalent (T018) — focus-triggered reveal, NEVER hover-only
 * (FR-046): the trigger is keyboard-reachable (tabIndex=0) and the content shows on
 * focus as well as hover; Esc dismisses. Used for truncated-value reveal (S5.4).
 */
export function Tooltip({ label, children, className }: TooltipProps) {
  const [open, setOpen] = useState(false);
  const tooltipId = useId();

  return (
    <span
      className={[styles.trigger, className].filter(Boolean).join(" ")}
      tabIndex={0}
      aria-describedby={open ? tooltipId : undefined}
      onFocus={() => setOpen(true)}
      onBlur={() => setOpen(false)}
      onMouseEnter={() => setOpen(true)}
      onMouseLeave={() => setOpen(false)}
      onKeyDown={(event) => {
        if (event.key === "Escape") setOpen(false);
      }}
    >
      {children}
      {open ? (
        <span id={tooltipId} role="tooltip" className={styles.tooltip}>
          {label}
        </span>
      ) : null}
    </span>
  );
}

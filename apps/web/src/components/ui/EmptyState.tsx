import type { ReactNode } from "react";
import styles from "./EmptyState.module.css";

interface EmptyStateProps {
  /** Short explanatory hint (no onboarding wizards — FR-110/Principle IV). */
  hint: string;
  /** The relevant action button for this surface (optional for hint-only states). */
  action?: ReactNode;
  className?: string;
}

/**
 * Catalog empty state (T016): hint + action button. Rendered ONLY when the view is
 * confirmed empty — never while data is still loading (spec Edge Cases).
 */
export function EmptyState({ hint, action, className }: EmptyStateProps) {
  return (
    <div className={[styles.emptyState, className].filter(Boolean).join(" ")}>
      <p className={styles.hint}>{hint}</p>
      {action ? <div className={styles.action}>{action}</div> : null}
    </div>
  );
}

import type { ReactNode } from "react";
import styles from "./ErrorState.module.css";

interface ErrorStateProps {
  /** The clear, human failure message (FR-049). */
  message: string;
  /** The in-place recovery affordance (retry Button etc.) — always offered (S5.3). */
  action?: ReactNode;
}

/**
 * Catalog error state (S5.3, FR-049): a failure surfaced as a clear message plus an
 * in-place recovery action, announced via `role="alert"`. The EmptyState's counterpart
 * for the error branch of a network-bound view.
 */
export function ErrorState({ message, action }: ErrorStateProps) {
  return (
    <div className={styles.error} role="alert">
      <p className={styles.message}>{message}</p>
      {action ? <div className={styles.action}>{action}</div> : null}
    </div>
  );
}

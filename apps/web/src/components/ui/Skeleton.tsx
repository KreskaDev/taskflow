import styles from "./Skeleton.module.css";

interface SkeletonProps {
  /** `row` — list-row placeholder(s); `block` — a rectangular content placeholder. */
  variant: "row" | "block";
  /** Row variant only: how many placeholder rows to render. */
  count?: number;
  className?: string;
}

/**
 * Catalog skeleton (T015) — ONLY for genuine network-bound loads, never for
 * optimistically paintable content (S5.2). Decorative: hidden from AT. The shimmer
 * is CSS-only and disabled under `prefers-reduced-motion` by the global guard.
 */
export function Skeleton({ variant, count = 1, className }: SkeletonProps) {
  return (
    <div
      className={[styles.skeleton, styles[variant], className].filter(Boolean).join(" ")}
      aria-hidden="true"
    >
      {variant === "row"
        ? Array.from({ length: count }, (_, i) => (
            <div key={i} className={styles.rowItem} data-skeleton-row />
          ))
        : null}
    </div>
  );
}

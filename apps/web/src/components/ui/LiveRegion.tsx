import type { ReactNode } from "react";

interface LiveRegionProps {
  /** "polite" by default; "assertive" only for genuinely urgent direct-to-user messages. */
  politeness?: "polite" | "assertive";
  children?: ReactNode;
}

/**
 * ARIA live region for conveying server-initiated updates and toasts to assistive
 * technology without stealing focus (Constitution II "Status messages"). Polite by
 * default and coalesced by the caller so output stays usable under fan-out.
 */
export function LiveRegion({ politeness = "polite", children }: LiveRegionProps) {
  return (
    <div className="tf-sr-only" role="status" aria-live={politeness} aria-atomic="true">
      {children}
    </div>
  );
}

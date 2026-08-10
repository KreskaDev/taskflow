"use client";

import { Chip } from "@/components/ui/Chip";
import { useLabelRoster } from "@/hooks/useLabels";
import styles from "./LabelChips.module.css";

/**
 * Renders a task's CALLER-scoped label chips by NAME (slice 006, R6/R11; on the catalog
 * {@link Chip} since slice 019 — T061). Resolves the caller's label ids to names/colors from
 * the `['labels']` roster (a shared, deduped query — many rows read it from cache). The NAME
 * carries the meaning and is React-escaped (FR-099); the preset color is the Chip's decorative
 * dot, NEVER the sole carrier (FR-044). An id not yet in the roster (an optimistic create
 * still settling) is skipped rather than rendered as a raw id.
 */
export function LabelChips({ labelIds }: { labelIds: string[] }) {
  const { data } = useLabelRoster();

  if (labelIds.length === 0) {
    return null;
  }

  const byId = new Map((data ?? []).map((label) => [label.id, label]));
  const resolved = labelIds.map((id) => byId.get(id)).filter((label) => label !== undefined);
  if (resolved.length === 0) {
    return null;
  }

  return (
    <span className={styles.chips}>
      <span className="sr-only">etykiety: </span>
      {resolved.map((label) => (
        <Chip key={label.id} label={label.name} color={label.color} />
      ))}
    </span>
  );
}

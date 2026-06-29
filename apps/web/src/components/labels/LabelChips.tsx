"use client";

import { useLabelRoster } from "@/hooks/useLabels";

/**
 * Renders a task's CALLER-scoped label chips by NAME (slice 006, R6/R11). Resolves the caller's label ids to
 * names/colors from the `['labels']` roster (a shared, deduped query — many rows read it from cache). The
 * NAME carries the meaning and is React-escaped (FR-099); the preset color is a decorative `data-color` hook,
 * NEVER the sole carrier (FR-044). An id not yet in the roster (an optimistic create still settling) is
 * skipped rather than rendered as a raw id.
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
    <span className="tf-task-row__labels">
      <span className="tf-sr-only">etykiety: </span>
      {resolved.map((label) => (
        <span key={label.id} className="tf-label-chip" data-color={label.color ?? undefined}>
          {label.name}
        </span>
      ))}
    </span>
  );
}

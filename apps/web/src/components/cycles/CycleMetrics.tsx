"use client";

import { daysRemainingLabel, formatCycleRange, percentDone, type CycleResponse } from "@/lib/cycles";
import styles from "./CycleMetrics.module.css";

/** The per-status breakdown vocabulary — the task-status labels the rest of the app uses. */
const BREAKDOWN_LABELS: [key: keyof CycleResponse["metrics"]["breakdown"], label: string][] = [
  ["backlog", "Backlog"],
  ["todo", "Do zrobienia"],
  ["in_progress", "W toku"],
  ["done", "Zrobione"],
  ["cancelled", "Anulowane"],
];

/**
 * The FR-026 metrics strip (slice 011, T018 — US-05.AS-03): „X% ukończone"
 * (done over non-cancelled total — D6), the Warsaw days-remaining label (overdue active:
 * „0 dni (po terminie)"), and the per-status breakdown as labelled TEXT counts. All numbers are
 * TEAM-WIDE (D10 — they include other users' tasks); a task-less cycle reads 0% + hint. The
 * absolute date range carries `data-visual-hide` so [V] baselines stay day-independent.
 */
export function CycleMetrics({ cycle, now }: { cycle: CycleResponse; now: Date }) {
  const { metrics } = cycle;
  return (
    <div className={styles.strip} data-testid="cycle-metrics">
      <p className={styles.headline}>
        <span className={styles.percent}>{percentDone(metrics)}% ukończone</span>
        <span className={styles.days}>{daysRemainingLabel(cycle.endDate, now)}</span>
        <span className={styles.range} data-visual-hide="">
          {formatCycleRange(cycle.startDate, cycle.endDate)}
        </span>
      </p>
      {metrics.total === 0 ? (
        <p className={styles.hint}>Ten cykl nie ma jeszcze zadań — przypisz je z menu „⋯” zadania.</p>
      ) : (
        <dl className={styles.breakdown}>
          {BREAKDOWN_LABELS.map(([key, label]) => (
            <div key={key} className={styles.stat}>
              <dt>{label}</dt>
              <dd>{metrics.breakdown[key]}</dd>
            </div>
          ))}
        </dl>
      )}
    </div>
  );
}

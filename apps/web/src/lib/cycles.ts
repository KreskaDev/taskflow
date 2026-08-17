import { differenceInCalendarDays } from "date-fns";

import type { components } from "@/lib/api/generated/schema";
import type { ProjectGroup } from "@/lib/board";
import { toReferenceZone } from "@/lib/timezone";

export type CycleResponse = components["schemas"]["CycleResponse"];
type TaskResponse = components["schemas"]["TaskResponse"];

/**
 * The cycles pure module (slice 011, T014 — contracts/ui-cycle.md). The single source of the D5
 * ordering, the Warsaw days-remaining math (FR-092), the Polish status labels, the by-cycle List
 * groups (FR-024 completion, EC-10 naming) and the CyclePicker options — the view, the sidebar,
 * the picker and their tests all read from here.
 */

/** The Polish status suffix vocabulary (D12/D13). */
const STATUS_LABELS: Record<string, string> = {
  planned: "planowany",
  active: "aktywny",
  closed: "zamknięty",
};

/** The Polish label of a cycle status token („aktywny" / „planowany" / „zamknięty"). */
export function cycleStatusLabel(status: string): string {
  return STATUS_LABELS[status] ?? status;
}

/**
 * The D5 deterministic order every surface agrees on: `(startDate, createdAt, id)`. The server
 * list already arrives in this order — client sorting is defensive (e.g. after an optimistic
 * insert), which is exactly why `createdAt` is exposed on the wire.
 */
export function orderCycles(cycles: CycleResponse[]): CycleResponse[] {
  return [...cycles].sort((a, b) => {
    if (a.startDate !== b.startDate) return a.startDate < b.startDate ? -1 : 1;
    if (a.createdAt !== b.createdAt) return a.createdAt < b.createdAt ? -1 : 1;
    return a.id < b.id ? -1 : a.id > b.id ? 1 : 0;
  });
}

/**
 * Full Warsaw calendar days from the day containing `now` to the day containing `endDate`
 * (FR-092): computed on the REFERENCE-zone calendar via date-fns `differenceInCalendarDays` over
 * zoned wall-clock fields — never fixed-offset arithmetic, so a DST seam inside a 2-week cycle
 * cannot skew the count. Negative when the end day is already past.
 */
export function daysRemaining(endDate: string, now: Date): number {
  return differenceInCalendarDays(toReferenceZone(new Date(endDate)), toReferenceZone(now));
}

/**
 * Whether the cycle's end has passed in Warsaw (the overdue fact for an ACTIVE cycle — close
 * stays manual, the server never auto-transitions). End dates use the Warsaw-midnight date-only
 * convention (D18), so "the end date passed" = the end day's midnight is behind us: `days <= 0`.
 */
export function isCycleOverdue(endDate: string, now: Date): boolean {
  return daysRemaining(endDate, now) <= 0;
}

/**
 * The metrics-strip days label with Polish verb agreement: „1 dzień pozostał" / „2 dni pozostały"
 * / „7 dni pozostało"; an already-ended cycle reads „0 dni (po terminie)" (contract ui-cycle.md).
 */
export function daysRemainingLabel(endDate: string, now: Date): string {
  const days = daysRemaining(endDate, now);
  if (days <= 0) return "0 dni (po terminie)";
  if (days === 1) return "1 dzień pozostał";
  // Polish paucal: 2–4 (but not 12–14) agree as „pozostały"; the rest as „pozostało".
  const mod10 = days % 10;
  const mod100 = days % 100;
  const paucal = mod10 >= 2 && mod10 <= 4 && (mod100 < 12 || mod100 > 14);
  return `${days} dni ${paucal ? "pozostały" : "pozostało"}`;
}

/**
 * Percentage of the cycle's outstanding work that is done (data-model.md D6):
 * `done / max(1, total − cancelled)` — cancelled tasks are not outstanding work, so they never
 * deflate progress; a task-less cycle reads 0%.
 */
export function percentDone(metrics: CycleResponse["metrics"]): number {
  const denominator = metrics.total - metrics.breakdown.cancelled;
  if (denominator <= 0) return 0;
  return Math.round((metrics.done / denominator) * 100);
}

/** Ascending code-unit position comparator with the id tiebreak (the server's ORDER BY position, id). */
function comparePosition(a: TaskResponse, b: TaskResponse): number {
  if (a.position !== b.position) return a.position < b.position ? -1 : 1;
  return a.id < b.id ? -1 : a.id > b.id ? 1 : 0;
}

/**
 * Builds the List's by-cycle groups (FR-024 completion, US-03.AS-07's deferred dimension):
 * groups in the D5 cycle order labelled by the cycle NAME, with the no-cycle group labelled
 * **„Bez cyklu" LAST** — never „Backlog", which would collide with the task status (EC-10).
 * Empty groups are omitted (the slice-010 rule); within-group order preserves the flat list's
 * position rank. A task referencing an unknown cycle id (a torn cache mid-refetch) falls into
 * „Bez cyklu" rather than vanishing.
 */
export function buildCycleGroups(tasks: TaskResponse[], cycles: CycleResponse[]): ProjectGroup[] {
  const flat = [...tasks].sort(comparePosition);
  const ordered = orderCycles(cycles);
  const known = new Set(ordered.map((c) => c.id));

  const groups: ProjectGroup[] = ordered.map((cycle) => ({
    key: `cycle:${cycle.id}`,
    label: cycle.name,
    tasks: flat.filter((t) => t.cycleId === cycle.id),
  }));

  groups.push({
    key: "cycle:none",
    label: "Bez cyklu",
    tasks: flat.filter((t) => t.cycleId == null || !known.has(t.cycleId)),
  });

  return groups.filter((g) => g.tasks.length > 0);
}

/** One CyclePicker option (US-05.AS-01): `id === null` is the „Bez cyklu" clear option. */
export interface CyclePickerOption {
  id: string | null;
  label: string;
  checked: boolean;
}

/**
 * Builds the CyclePicker options (D12, Clarifications): ALL cycles — active, planned AND closed —
 * in D5 order with the Polish status suffix, plus „Bez cyklu" to clear; the task's current
 * assignment is checked (no assignment checks „Bez cyklu").
 */
export function buildCyclePickerOptions(
  cycles: CycleResponse[],
  currentCycleId: string | null,
): CyclePickerOption[] {
  const options: CyclePickerOption[] = orderCycles(cycles).map((cycle) => ({
    id: cycle.id,
    label: `${cycle.name} (${cycleStatusLabel(cycle.status)})`,
    checked: cycle.id === currentCycleId,
  }));
  options.push({
    id: null,
    label: "Bez cyklu",
    checked: currentCycleId === null || !cycles.some((c) => c.id === currentCycleId),
  });
  return options;
}

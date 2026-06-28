import type { components } from "@/lib/api/generated/schema";
import {
  referenceDateKey,
  startOfReferenceDayPlusUtc,
  startOfReferenceTodayUtc,
  startOfReferenceTomorrowUtc,
} from "@/lib/timezone";

type TaskResponse = components["schemas"]["TaskResponse"];
type TodayTaskResponse = components["schemas"]["TodayTaskResponse"];
type TodayResponse = components["schemas"]["TodayResponse"];
type UpcomingResponse = components["schemas"]["UpcomingResponse"];

/**
 * The client mirror of the server's Today/Upcoming membership + R5 order (slice 005, R5/R7). Used to
 * recompute the grouped view caches optimistically after a mutation so the optimistic paint EQUALS the
 * authoritative result (FR-092 identical-rule guarantee) — a reschedule-to-tomorrow drops the row out of
 * Today, a toggle-done drops it from both, a priority change re-sorts it. The boundary math uses the same
 * Warsaw helpers (`lib/timezone.ts`) the server uses.
 */

/** A task is in a triage view only when it is active (not done/cancelled) and carries a due date. */
function isActiveWithDue(task: TaskResponse): boolean {
  return task.status !== "done" && task.status !== "cancelled" && task.dueDate != null;
}

/** Today = due-today-in-Warsaw OR overdue-incomplete (due before start-of-tomorrow, no lower bound). */
export function isInToday(task: TaskResponse, now: Date): boolean {
  if (!isActiveWithDue(task)) return false;
  return new Date(task.dueDate as string) < startOfReferenceTomorrowUtc(now);
}

/** Upcoming = the 7 calendar days after today: [start of tomorrow-Warsaw, start of (today+8)-Warsaw). */
export function isInUpcoming(task: TaskResponse, now: Date): boolean {
  if (!isActiveWithDue(task)) return false;
  const due = new Date(task.dueDate as string);
  return due >= startOfReferenceTomorrowUtc(now) && due < startOfReferenceDayPlusUtc(now, 8);
}

/** Priority sort rank; null (unprioritized) ranks LAST (R2/R5). */
function priorityRank(priority: TaskResponse["priority"]): number {
  switch (priority) {
    case "P0":
      return 0;
    case "P1":
      return 1;
    case "P2":
      return 2;
    case "P3":
      return 3;
    default:
      return 4;
  }
}

/** The R5 within-group comparator: priority (P0 first, null last) → due time → createdAt → id. */
function compareR5(a: TaskResponse, b: TaskResponse): number {
  const byPriority = priorityRank(a.priority) - priorityRank(b.priority);
  if (byPriority !== 0) return byPriority;
  // ISO-8601 UTC strings sort lexicographically in chronological order (date-only is stored at
  // start-of-day, so it precedes same-day timed tasks — R5).
  const aDue = a.dueDate ?? "";
  const bDue = b.dueDate ?? "";
  if (aDue !== bDue) return aDue < bDue ? -1 : 1;
  if (a.createdAt !== b.createdAt) return a.createdAt < b.createdAt ? -1 : 1;
  return a.id < b.id ? -1 : a.id > b.id ? 1 : 0;
}

/** Builds the grouped Today response from a flat task set (group by project, Inbox first, R5 order). */
export function buildTodayGroups(tasks: TaskResponse[], now: Date): TodayResponse {
  const startOfToday = startOfReferenceTodayUtc(now);
  const members = tasks.filter((t) => isInToday(t, now));

  const byProject = new Map<string | null, TodayTaskResponse[]>();
  for (const task of members) {
    const key = task.projectId ?? null;
    const row: TodayTaskResponse = { ...task, isOverdue: new Date(task.dueDate as string) < startOfToday };
    const bucket = byProject.get(key);
    if (bucket) bucket.push(row);
    else byProject.set(key, [row]);
  }

  const groups = [...byProject.entries()]
    .sort(([a], [b]) => {
      // Inbox (null) first, then projects ascending by id.
      if (a === null) return b === null ? 0 : -1;
      if (b === null) return 1;
      return a < b ? -1 : a > b ? 1 : 0;
    })
    .map(([projectId, rows]) => ({ projectId, tasks: [...rows].sort(compareR5) }));

  return { groups };
}

/** Builds the grouped Upcoming response from a flat task set (group by Warsaw day, ascending, R5 order). */
export function buildUpcomingGroups(tasks: TaskResponse[], now: Date): UpcomingResponse {
  const members = tasks.filter((t) => isInUpcoming(t, now));

  const byDay = new Map<string, TaskResponse[]>();
  for (const task of members) {
    const key = referenceDateKey(new Date(task.dueDate as string));
    const bucket = byDay.get(key);
    if (bucket) bucket.push(task);
    else byDay.set(key, [task]);
  }

  const groups = [...byDay.entries()]
    .sort(([a], [b]) => (a < b ? -1 : a > b ? 1 : 0))
    .map(([date, rows]) => ({ date, tasks: [...rows].sort(compareR5) }));

  return { groups };
}

/** Flattens a grouped Today response back to its bare task rows (drops `isOverdue` — recomputed on rebuild). */
export function flattenToday(today: TodayResponse | undefined): TaskResponse[] {
  if (!today) return [];
  return today.groups.flatMap((g) =>
    g.tasks.map(({ isOverdue: _isOverdue, ...task }) => task as TaskResponse),
  );
}

/** Flattens a grouped Upcoming response back to its bare task rows. */
export function flattenUpcoming(upcoming: UpcomingResponse | undefined): TaskResponse[] {
  if (!upcoming) return [];
  return upcoming.groups.flatMap((g) => g.tasks);
}

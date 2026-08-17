import type { components } from "@/lib/api/generated/schema";
import { priorityRank } from "@/lib/dailyViews";

type TaskResponse = components["schemas"]["TaskResponse"];

/**
 * The Board/grouping pure module (slice 010, T011 — data-model.md "Column ↔ status mapping",
 * FR-024/FR-025). The SINGLE source of the column↔status map and the Polish column labels:
 * the Board UI, the menu move actions and their tests all read from here. `cancelled` is
 * NEVER a column (EC-11) — it renders only in the List (the „Anulowane” group).
 */

/** The four column statuses, in strip order. `cancelled` is deliberately not one of them. */
export type BoardStatus = "backlog" | "todo" | "in_progress" | "done";

export interface BoardColumnSpec {
  status: BoardStatus;
  label: string;
}

/** The column strip (FR-025): order and Polish labels — one map for UI, moves and tests. */
export const BOARD_COLUMNS: readonly BoardColumnSpec[] = [
  { status: "backlog", label: "Backlog" },
  { status: "todo", label: "Do zrobienia" },
  { status: "in_progress", label: "W toku" },
  { status: "done", label: "Zrobione" },
];

export interface BoardColumn extends BoardColumnSpec {
  tasks: TaskResponse[];
}

/** Ascending code-unit position comparator with the id tiebreak (the server's ORDER BY position, id). */
function comparePosition(a: TaskResponse, b: TaskResponse): number {
  if (a.position !== b.position) return a.position < b.position ? -1 : 1;
  return a.id < b.id ? -1 : a.id > b.id ? 1 : 0;
}

/**
 * Distributes a project's tasks into the four columns (US-03.AS-03). `cancelled` tasks are
 * filtered out entirely (EC-11); within a column the order is the position rank — the same
 * order the flat List renders (D6: Board moves never touch `position`).
 */
export function buildBoardColumns(tasks: TaskResponse[]): BoardColumn[] {
  return BOARD_COLUMNS.map((column) => ({
    ...column,
    tasks: tasks.filter((t) => t.status === column.status).sort(comparePosition),
  }));
}

/**
 * The neighbouring column's status for a menu move (US-03.AS-05/AS-06), or `null` at a
 * boundary — the caller OMITS (not disables) the matching menu item: Zrobione offers no
 * right move, Backlog no left. A non-column status (`cancelled`) has no neighbours.
 */
export function adjacentStatus(status: string, direction: "left" | "right"): BoardStatus | null {
  const index = BOARD_COLUMNS.findIndex((c) => c.status === status);
  if (index < 0) return null;
  const neighbour = BOARD_COLUMNS[direction === "left" ? index - 1 : index + 1];
  return neighbour?.status ?? null;
}

/** The List's group-by choices (FR-024; „cycle” completed the set in slice 011). */
export type ProjectGroupBy = "none" | "status" | "priority" | "cycle";

export interface ProjectGroup {
  /** Stable render key (e.g. `status:todo`, `priority:none`). */
  key: string;
  label: string;
  tasks: TaskResponse[];
}

/** The status-group order: the column strip, then „Anulowane” LAST (EC-11 counterpart). */
const STATUS_GROUPS: readonly { status: string; label: string }[] = [
  ...BOARD_COLUMNS,
  { status: "cancelled", label: "Anulowane" },
];

/** The priority-group order: P0→P3, then „Bez priorytetu” last (reusing the R5 rank). */
const PRIORITY_GROUP_LABELS = ["P0", "P1", "P2", "P3", "Bez priorytetu"] as const;

/**
 * Builds the List's groups (FR-024, US-03.AS-07). Within-group order preserves the FLAT
 * list's order (position rank — this is a data view, not a board), empty groups are
 * omitted. `none` degrades to one flat group so callers can treat the result uniformly.
 */
export function buildProjectGroups(tasks: TaskResponse[], groupBy: ProjectGroupBy): ProjectGroup[] {
  const flat = [...tasks].sort(comparePosition);

  if (groupBy === "status") {
    return STATUS_GROUPS.map(({ status, label }) => ({
      key: `status:${status}`,
      label,
      tasks: flat.filter((t) => t.status === status),
    })).filter((g) => g.tasks.length > 0);
  }

  if (groupBy === "priority") {
    return PRIORITY_GROUP_LABELS.map((label, rank) => ({
      key: `priority:${rank < 4 ? label : "none"}`,
      label,
      tasks: flat.filter((t) => priorityRank(t.priority) === rank),
    })).filter((g) => g.tasks.length > 0);
  }

  return [{ key: "all", label: "Wszystkie zadania", tasks: flat }];
}

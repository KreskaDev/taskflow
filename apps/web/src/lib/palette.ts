import type { components } from "@/lib/api/generated/schema";
import { filterTokens, normalizeForFilter } from "@/lib/filter";

type TaskResponse = components["schemas"]["TaskResponse"];
type ProjectResponse = components["schemas"]["ProjectResponse"];
type LabelResponse = components["schemas"]["LabelResponse"];

/**
 * The command palette's pure model (slice 013 — contracts/ui-palette.md, research D3/D6/D12): the fixed
 * action catalogue (client-side, no shortcut labels — FR-033 deferred), client-side action matching, the
 * result → navigation targets, and the grouped option list the dialog renders in the fixed order
 * Akcje → Zadania → Projekty → Etykiety.
 */

export type PaletteActionKind = "dialog:new-task" | "dialog:new-project" | "route";

export interface PaletteAction {
  id: string;
  /** Polish label rendered in the list. */
  label: string;
  /** Extra match text (English aliases from product-vision, e.g. „create project"). */
  aliases: readonly string[];
  /** Lucide icon identifier the dialog maps to a component. */
  icon: "plus" | "folder-plus" | "refresh-cw" | "inbox" | "calendar" | "calendar-clock" | "user-check" | "settings";
  kind: PaletteActionKind;
  /** For `route` actions — the destination. */
  href?: string;
}

/** The fixed catalogue (D12). Order = display order for an empty query. */
export const PALETTE_ACTIONS: readonly PaletteAction[] = Object.freeze([
  { id: "new-task", label: "Nowy task", aliases: ["new task", "create task", "dodaj zadanie", "nowe zadanie"], icon: "plus", kind: "dialog:new-task" },
  { id: "new-project", label: "Nowy projekt", aliases: ["new project", "create project", "dodaj projekt"], icon: "folder-plus", kind: "dialog:new-project" },
  { id: "new-cycle", label: "Nowy cykl", aliases: ["new cycle", "create cycle", "sprint"], icon: "refresh-cw", kind: "route", href: "/cycle?nowy=1" },
  { id: "go-inbox", label: "Przejdź: Inbox", aliases: ["go inbox", "skrzynka"], icon: "inbox", kind: "route", href: "/" },
  { id: "go-today", label: "Przejdź: Dziś", aliases: ["go today", "dzisiaj"], icon: "calendar", kind: "route", href: "/today" },
  { id: "go-upcoming", label: "Przejdź: Nadchodzące", aliases: ["go upcoming"], icon: "calendar-clock", kind: "route", href: "/upcoming" },
  { id: "go-assigned", label: "Przejdź: Przypisane", aliases: ["go assigned", "assigned to me", "moje"], icon: "user-check", kind: "route", href: "/assigned" },
  { id: "go-cycle", label: "Przejdź: Cykl", aliases: ["go cycle", "current cycle"], icon: "refresh-cw", kind: "route", href: "/cycle" },
  { id: "go-settings", label: "Przejdź: Ustawienia", aliases: ["go settings", "settings", "preferences", "konto"], icon: "settings", kind: "route", href: "/settings" },
] satisfies PaletteAction[]);

/** The query key component: trimmed, case-folded (the server folds case the same way). */
export function normalizeQuery(q: string): string {
  return q.trim().toLowerCase();
}

/** Client-side token-AND match over label + aliases (same rule as the server's, diacritic-insensitive). Empty query → all. */
export function matchActions(q: string): PaletteAction[] {
  const tokens = filterTokens(q);
  if (tokens.length === 0) return [...PALETTE_ACTIONS];
  return PALETTE_ACTIONS.filter((action) => {
    const haystack = normalizeForFilter([action.label, ...action.aliases].join(" "));
    return tokens.every((token) => haystack.includes(token));
  });
}

/** A task result opens its location with the drawer (D6). */
export function taskHref(task: Pick<TaskResponse, "id" | "projectId">): string {
  return task.projectId ? `/projects/${task.projectId}?task=${task.id}` : `/?task=${task.id}`;
}

export function projectHref(project: Pick<ProjectResponse, "id">): string {
  return `/projects/${project.id}`;
}

export type PaletteOption =
  | { kind: "action"; id: string; action: PaletteAction }
  | { kind: "task"; id: string; task: TaskResponse }
  | { kind: "project"; id: string; project: ProjectResponse }
  | { kind: "label"; id: string; label: LabelResponse };

export interface PaletteGroup {
  key: "actions" | "tasks" | "projects" | "labels";
  heading: string;
  options: PaletteOption[];
}

export interface PaletteSearchData {
  tasks: readonly TaskResponse[];
  projects: readonly ProjectResponse[];
  labels: readonly LabelResponse[];
}

/**
 * The grouped option list for the dialog: Akcje → Zadania → Projekty → Etykiety; groups without rows are
 * omitted; option ids are stable (`<kind>:<id>`) so `aria-activedescendant` survives re-renders.
 */
export function buildPaletteGroups(q: string, data: PaletteSearchData | undefined): PaletteGroup[] {
  const groups: PaletteGroup[] = [];
  const actions = matchActions(q);
  if (actions.length > 0) {
    groups.push({ key: "actions", heading: "Akcje", options: actions.map((action) => ({ kind: "action", id: `action:${action.id}`, action })) });
  }
  if (data && filterTokens(q).length > 0) {
    if (data.tasks.length > 0) {
      groups.push({ key: "tasks", heading: "Zadania", options: data.tasks.map((task) => ({ kind: "task", id: `task:${task.id}`, task })) });
    }
    if (data.projects.length > 0) {
      groups.push({ key: "projects", heading: "Projekty", options: data.projects.map((project) => ({ kind: "project", id: `project:${project.id}`, project })) });
    }
    if (data.labels.length > 0) {
      groups.push({ key: "labels", heading: "Etykiety", options: data.labels.map((label) => ({ kind: "label", id: `label:${label.id}`, label })) });
    }
  }
  return groups;
}

/** Flattens the groups to the keyboard-navigable option order. */
export function flattenOptions(groups: readonly PaletteGroup[]): PaletteOption[] {
  return groups.flatMap((g) => g.options);
}

/** Total result count for the live-region announcement (actions excluded — they are always there). */
export function resultCount(data: PaletteSearchData | undefined): number {
  if (!data) return 0;
  return data.tasks.length + data.projects.length + data.labels.length;
}

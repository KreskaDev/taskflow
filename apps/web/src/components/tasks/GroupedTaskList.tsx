"use client";

import { TaskRow, taskOptionId, type TaskRowActions } from "@/components/tasks/TaskRow";
import type { TaskResponse } from "@/hooks/useTasks";
import type { ProjectGroup, ProjectGroupBy } from "@/lib/board";
import { listboxKeyDown } from "@/lib/listboxKeys";
import styles from "./GroupedTaskList.module.css";

interface GroupedTaskListProps {
  /** The `buildProjectGroups` output (FR-024) — rendered as-is, one group block each. */
  groups: ProjectGroup[];
  /** The selected (active) option as a FLAT index across all groups — the parent owns it. */
  selectedIndex: number;
  onSelectedIndexChange: (index: number) => void;
  /** Space on the listbox — toggle the selected task (composite-widget key, D5). */
  onToggleSelected?: () => void;
  /** Enter on the listbox — open the selected task's details. */
  onActivateSelected?: () => void;
  /** Builds the per-row operation set (quick actions + the complete „⋯” menu, FR-108). */
  rowActions?: (task: TaskResponse, index: number) => TaskRowActions;
  /** The id of the task in inline-rename mode (the List's edit affordance stays available grouped). */
  renamingId?: string | null;
  onCommitRename?: (title: string) => void;
  onCancelRename?: () => void;
}

/**
 * The groupable project List (slice 010, T015 — FR-024, D9, US-03.AS-07): the DailyView
 * grouped-grid pattern generalized to the project view's status/priority groupings
 * (roles remediated from listbox/option post-010 — see TaskList). ONE `role="grid"`
 * holds DOM focus; each group is a `role="rowgroup"` with its visible heading mirrored
 * into `aria-label`; selection is a FLAT `aria-activedescendant` index across groups
 * (↑/↓ walk group boundaries transparently). Virtualization is consciously bypassed
 * exactly as DailyView does — grouped project lists render at daily-view scale.
 */
export function GroupedTaskList({
  groups,
  selectedIndex,
  onSelectedIndexChange,
  onToggleSelected,
  onActivateSelected,
  rowActions,
  renamingId = null,
  onCommitRename,
  onCancelRename,
}: GroupedTaskListProps) {
  const flat = groups.flatMap((g) => g.tasks);
  const hasSelection = selectedIndex >= 0 && selectedIndex < flat.length;

  return (
    <div
      role="grid"
      tabIndex={0}
      aria-label="Zadania"
      aria-activedescendant={hasSelection ? taskOptionId(flat[selectedIndex]!.id) : undefined}
      className={styles.list}
      onKeyDown={listboxKeyDown({
        count: flat.length,
        selectedIndex,
        onSelectedIndexChange,
        onToggleSelected,
        onActivateSelected,
      })}
    >
      {groups.map((group) => (
        <div key={group.key} role="rowgroup" aria-label={group.label} className={styles.group}>
          <div className={styles.groupHeading} aria-hidden="true">
            {group.label}
          </div>
          {group.tasks.map((task) => {
            const index = flat.findIndex((t) => t.id === task.id);
            return (
              <TaskRow
                key={task.id}
                task={task}
                selected={index === selectedIndex}
                isRenaming={task.id === renamingId}
                onCommitRename={onCommitRename ?? (() => undefined)}
                onCancelRename={onCancelRename ?? (() => undefined)}
                onSelect={() => onSelectedIndexChange(index)}
                actions={rowActions?.(task, index)}
                style={{}}
              />
            );
          })}
        </div>
      ))}
    </div>
  );
}

const GROUP_BY_OPTIONS: readonly { value: ProjectGroupBy; label: string }[] = [
  { value: "none", label: "Brak" },
  { value: "status", label: "Status" },
  { value: "priority", label: "Priorytet" },
];

/**
 * The visible group-by control (US-03.AS-07): „Grupuj: Brak | Status | Priorytet” as
 * `aria-pressed` toggle buttons (each an ordinary tab stop — keyboard-operable without a
 * composite-widget pattern). „wg cyklu” deliberately does NOT render — it arrives with
 * slice 011 (spec scope note).
 */
export function GroupByControl({
  value,
  onChange,
}: {
  value: ProjectGroupBy;
  onChange: (groupBy: ProjectGroupBy) => void;
}) {
  return (
    <div className={styles.groupBy}>
      <span className={styles.groupByLabel} id="project-groupby-label">
        Grupuj:
      </span>
      {GROUP_BY_OPTIONS.map((option) => (
        <button
          key={option.value}
          type="button"
          className={styles.groupByOption}
          aria-pressed={option.value === value}
          onClick={() => onChange(option.value)}
        >
          {option.label}
        </button>
      ))}
    </div>
  );
}

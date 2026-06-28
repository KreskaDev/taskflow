"use client";

import { useEffect, useMemo, useState } from "react";
import { useRouter } from "next/navigation";

import { ProjectSelector } from "@/components/projects/ProjectSelector";
import { ShortcutsHelp } from "@/components/tasks/ShortcutsHelp";
import { TaskCapture } from "@/components/tasks/TaskCapture";
import { TaskList } from "@/components/tasks/TaskList";
import { useGlobalShortcuts } from "@/hooks/useGlobalShortcuts";
import { useTaskMutations } from "@/hooks/useTaskMutations";
import { useTasks } from "@/hooks/useTasks";

/**
 * Workspace home — the app-shell ORCHESTRATOR (T058, EC-01). It owns ALL keyboard-surface
 * state (selection, capture/help overlays, which row is inline-renaming), wires the single
 * global shortcut gate ({@link useGlobalShortcuts}, T054) to that state + the task mutations
 * ({@link useTaskMutations}, T057), and renders the controlled capture surface, help overlay
 * and (selection-/rename-)controlled {@link TaskList}.
 *
 * Region branching (FR-049): when the query FAILED we show an accessible error alert with a
 * retry (distinct from an empty inbox so a load failure is never mistaken for "no tasks");
 * when the query RESOLVED with zero rows we show a quiet, accessible empty-Inbox hint (press
 * `C`) — not an onboarding wizard or modal (Constitution IV); while loading we fall through
 * to {@link TaskList}, which shares the single `['tasks']` query so the read is deduped.
 *
 * Bare `C` is no longer owned by {@link TaskCapture} — the gate owns it (and its FR-031/AS-09
 * text-field suppression), so capture/help are CONTROLLED via `open`/`onClose`.
 */
export default function WorkspaceHome() {
  const { data, isPending, isError, error, refetch } = useTasks();
  const tasks = useMemo(() => data ?? [], [data]);
  const isEmpty = !isPending && !isError && tasks.length === 0;

  const { renameTask, setTaskDone, reorderTask, deleteTask, moveTaskToProject } = useTaskMutations();
  const router = useRouter();

  // The page OWNS the entire keyboard surface state. `selectedIndex` indexes `tasks`;
  // `captureOpen`/`helpOpen` drive the two controlled overlays; `renamingId` is the id of
  // the row in inline-rename mode (or null).
  const [selectedIndex, setSelectedIndex] = useState(0);
  const [captureOpen, setCaptureOpen] = useState(false);
  const [helpOpen, setHelpOpen] = useState(false);
  const [renamingId, setRenamingId] = useState<string | null>(null);
  // The id of the task whose move-to-project selector is open (or null). The selector reads the
  // live task by id so a concurrent list change can't strand a stale row reference.
  const [movingId, setMovingId] = useState<string | null>(null);

  // Keep `selectedIndex` in range as the list shrinks (delete) or grows. A fixed index would
  // otherwise dangle past the end after a delete and select nothing — clamp to the last row.
  useEffect(() => {
    setSelectedIndex((i) => Math.min(i, Math.max(0, tasks.length - 1)));
  }, [tasks.length]);

  // The handler set is memoized because `useGlobalShortcuts` re-subscribes its document
  // listener whenever the reference changes. The operate handlers close over `tasks` and
  // `selectedIndex`, so both are in the deps — re-subscribing per change is cheap and keeps
  // every handler reading the CURRENT selection/list (no stale closures). Every operate
  // handler guards against no/invalid selection because the gate fires globally (even on an
  // empty Inbox or while an overlay is open).
  const shortcutHandlers = useMemo(
    () => ({
      onCapture: () => setCaptureOpen(true),
      onHelp: () => setHelpOpen(true),
      onMoveUp: () => setSelectedIndex((i) => Math.max(0, i - 1)),
      onMoveDown: () => setSelectedIndex((i) => Math.min(tasks.length - 1, i + 1)),

      // Space — toggle the selected task done↔backlog (desired-state, idempotent under retry).
      onToggle: () => {
        const sel = tasks[selectedIndex];
        if (!sel) return;
        setTaskDone(sel.id, sel.status !== "done");
      },

      // E — enter inline-rename on the selected row (the row renders the autofocused input).
      onRename: () => {
        const sel = tasks[selectedIndex];
        if (!sel) return;
        setRenamingId(sel.id);
      },

      // M — open the move-to-project selector for the selected task (US-08.AS-05, R7). The
      // selector lists the Inbox + every owned project; the actual move fires on selection.
      onMove: () => {
        const sel = tasks[selectedIndex];
        if (!sel) return;
        setMovingId(sel.id);
      },

      // Del — soft-delete the selected task (optimistic remove + rollback-in-place; the
      // FR-049 failure announcement is the global MutationCache announcer — no bespoke toast).
      onDelete: () => {
        const sel = tasks[selectedIndex];
        if (!sel) return;
        deleteTask(sel.id);
      },

      // Alt+↑ — reorder the selected task UP one rank (FROZEN R18 chord). The list is ascending
      // by `position` (top-is-lowest), so moving up means landing between the row two above
      // (its new upper neighbour, smaller rank) and the row one above (its new lower neighbour).
      // `reorderTask` recomputes `between()` from the FRESH neighbour ranks, so we pass ids.
      onReorderUp: () => {
        const sel = tasks[selectedIndex];
        if (!sel || selectedIndex < 1) return;
        const aboveId = tasks[selectedIndex - 2]?.id ?? null;
        const belowId = tasks[selectedIndex - 1]!.id;
        reorderTask(sel.id, aboveId, belowId);
        setSelectedIndex(selectedIndex - 1);
      },

      // Alt+↓ — reorder the selected task DOWN one rank. It lands between the row one below
      // (its new upper neighbour) and the row two below (its new lower neighbour, may be the tail).
      onReorderDown: () => {
        const sel = tasks[selectedIndex];
        if (!sel || selectedIndex > tasks.length - 2) return;
        const aboveId = tasks[selectedIndex + 1]!.id;
        const belowId = tasks[selectedIndex + 2]?.id ?? null;
        reorderTask(sel.id, aboveId, belowId);
        setSelectedIndex(selectedIndex + 1);
      },

      // G-chord navigation (slice 005, US-08): G I → Inbox (here), G T → Today, G U → Upcoming.
      onGoInbox: () => router.push("/"),
      onGoToday: () => router.push("/today"),
      onGoUpcoming: () => router.push("/upcoming"),
      onGoAssigned: () => router.push("/assigned"),
    }),
    [tasks, selectedIndex, setTaskDone, deleteTask, reorderTask, router],
  );
  useGlobalShortcuts(shortcutHandlers);

  // Inline-rename commit/cancel (driven by the row's input; T058). Commit re-stamps the
  // validated title via the optimistic rename recipe (server 422 surfaces through the global
  // announcer); both close edit mode so {@link TaskList} returns focus to the listbox.
  const commitRename = (title: string) => {
    if (renamingId !== null) {
      renameTask(renamingId, title);
    }
    setRenamingId(null);
  };
  const cancelRename = () => setRenamingId(null);

  return (
    <section aria-labelledby="workspace-heading" className="tf-workspace">
      <h1 id="workspace-heading">Your workspace</h1>
      <TaskCapture open={captureOpen} onClose={() => setCaptureOpen(false)} />
      <ShortcutsHelp open={helpOpen} onClose={() => setHelpOpen(false)} />
      <ProjectSelector
        open={movingId !== null}
        onClose={() => setMovingId(null)}
        task={tasks.find((t) => t.id === movingId)}
        // These are Inbox rows (project_id IS NULL), so the source is the Inbox (fromProjectId=null);
        // null target = stay/return to Inbox, a project id moves it there (R6/R7).
        onSelect={(projectId) => {
          if (movingId !== null) moveTaskToProject(movingId, projectId, null);
        }}
      />

      {isError ? (
        // `role="alert"` announces the load failure assertively (it's a direct response to
        // the user's own navigation), distinct from the quiet empty-inbox hint. The retry
        // button re-runs the single `['tasks']` query (FR-049).
        <div role="alert" className="tf-workspace__error">
          <p>We couldn&apos;t load your tasks. {error.message}</p>
          <button type="button" className="tf-button" onClick={() => void refetch()}>
            Retry
          </button>
        </div>
      ) : isEmpty ? (
        <p className="tf-workspace__empty">
          Your Inbox is empty. Press <kbd>C</kbd> to create your first task.
        </p>
      ) : (
        <TaskList
          selectedIndex={selectedIndex}
          onSelectedIndexChange={setSelectedIndex}
          renamingId={renamingId}
          onCommitRename={commitRename}
          onCancelRename={cancelRename}
        />
      )}
    </section>
  );
}

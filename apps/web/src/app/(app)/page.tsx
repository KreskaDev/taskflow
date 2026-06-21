"use client";

import { TaskCapture } from "@/components/tasks/TaskCapture";
import { TaskList } from "@/components/tasks/TaskList";
import { useTasks } from "@/hooks/useTasks";

/**
 * Workspace home — the live task surface (T040, EC-01). Mounts the `C` capture surface
 * unconditionally so the create shortcut works even before the first task exists, then
 * branches the list region: when the query FAILED we show an accessible error alert with
 * a retry affordance (FIX 4; FR-049) — distinct from an empty inbox so a load failure is
 * never mistaken for "no tasks". When the query has RESOLVED with zero rows we show a quiet,
 * accessible empty-Inbox hint (press `C` to create) — not an onboarding wizard or modal
 * (Constitution IV). While loading we fall through to {@link TaskList}, which shares the
 * single `['tasks']` query so this extra read is deduped, not a second request.
 */
export default function WorkspaceHome() {
  const { data, isPending, isError, error, refetch } = useTasks();
  const isEmpty = !isPending && !isError && (data?.length ?? 0) === 0;

  return (
    <section aria-labelledby="workspace-heading" className="tf-workspace">
      <h1 id="workspace-heading">Your workspace</h1>
      <TaskCapture />
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
        <TaskList />
      )}
    </section>
  );
}

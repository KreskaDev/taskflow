"use client";

import { Dialog } from "@/components/ui/Dialog";
import { useProjects, type ProjectResponse } from "@/hooks/useProjects";

const TITLE_ID = "project-selector-title";

interface ProjectSelectorProps {
  open: boolean;
  onClose: () => void;
  /**
   * The task being moved (for the accessible heading + to mark its current placement). When the
   * task already lives in a project, that option is marked `aria-current` so the user can see where
   * it is now; choosing it is a harmless no-op the parent may short-circuit.
   */
  task?: { id: string; title: string; projectId?: string | null };
  /**
   * Commit the move: `projectId = null` moves the task to the Inbox, a project id moves it there.
   * The parent owns the `useTaskMutations().moveTaskToProject` call + closing the dialog.
   */
  onSelect: (projectId: string | null) => void;
}

/**
 * The `M` move-to-project selector (T040; FR-021/FR-101, research R7/R16). A modal dialog (the
 * FR-101 focus contract — initial focus, trap, Esc-to-dismiss, return focus — is owned by
 * {@link Dialog}) listing the Inbox plus every OWNED, non-archived project as keyboard-reachable
 * buttons. Choosing one moves the selected task there (the Inbox option clears `project_id`,
 * R6/R7). Archived projects are excluded — a task is never moved into a hidden destination.
 *
 * Each option carries a visible icon glyph + the project NAME (never color alone, FR-044); the
 * decorative color swatch is `aria-hidden`. The project name is rendered as a React text node so
 * it is escaped (FR-099 — no `dangerouslySetInnerHTML`). The list is gated on `open` so every open
 * is a fresh mount with focus placed on the first option by the Dialog.
 */
export function ProjectSelector({ open, onClose, task, onSelect }: ProjectSelectorProps) {
  const { data: projects } = useProjects();

  if (!open) return null;

  // Only OWNED, non-archived projects are valid destinations (R7); the flat list is already
  // owner-scoped + tombstone-excluded by `GET /api/projects` (R8).
  const destinations: ProjectResponse[] = (projects ?? []).filter((p) => p.archivedAt == null);

  const choose = (projectId: string | null) => {
    onSelect(projectId);
    onClose();
  };

  const currentProjectId = task?.projectId ?? null;

  return (
    <Dialog open onClose={onClose} titleId={TITLE_ID}>
      <h2 id={TITLE_ID}>{task ? `Move "${task.title}" to…` : "Move to…"}</h2>

      <ul className="tf-project-selector__list" role="list">
        <li>
          <button
            type="button"
            className="tf-project-selector__option"
            aria-current={currentProjectId === null ? "true" : undefined}
            onClick={() => choose(null)}
          >
            <span className="tf-project-selector__icon" aria-hidden="true">
              📥
            </span>
            Inbox
          </button>
        </li>
        {destinations.map((p) => (
          <li key={p.id}>
            <button
              type="button"
              className="tf-project-selector__option"
              aria-current={currentProjectId === p.id ? "true" : undefined}
              onClick={() => choose(p.id)}
            >
              <span className="tf-sidebar__swatch" aria-hidden="true" data-color={p.color} />
              <span className="tf-project-selector__icon" aria-hidden="true">
                {p.icon}
              </span>
              {p.name}
            </button>
          </li>
        ))}
      </ul>
    </Dialog>
  );
}

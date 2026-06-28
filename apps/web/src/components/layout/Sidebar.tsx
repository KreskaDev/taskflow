"use client";

import Link from "next/link";
import { useState } from "react";

import { ArchiveProjectDialog } from "@/components/projects/ArchiveProjectDialog";
import { DeleteProjectDialog } from "@/components/projects/DeleteProjectDialog";
import { MembersDialog } from "@/components/projects/MembersDialog";
import { ProjectForm } from "@/components/projects/ProjectForm";
import { ShareProjectDialog } from "@/components/projects/ShareProjectDialog";
import { useProjectTasks } from "@/hooks/useProjectTasks";
import { useArchivedProjects, useProjects, type ProjectResponse } from "@/hooks/useProjects";
import { useProjectMutations } from "@/hooks/useProjectMutations";

/** A node in the assembled one-level sidebar tree: a project plus its (at most one level) children. */
export interface ProjectTreeNode {
  project: ProjectResponse;
  children: ProjectTreeNode[];
}

/**
 * Assembles the one-level parent/child tree from the flat `GET /api/projects` list (research R16).
 * Pure (no React) so the grouping logic is unit-testable (mirrors `createGlobalShortcutsListener`).
 *
 * - Archived rows are dropped from the default tree (defensive; the active query already excludes
 *   them, R8).
 * - A row whose `parentId` references a project NOT in the (active) list is an orphan and is
 *   promoted to top-level, so a child of an archived/absent parent still renders (never vanishes).
 * - Nesting is capped at one level by construction: only top-level rows become roots, and only their
 *   direct children are nested; a (malformed) grandchild would surface top-level, never at depth 2.
 */
export function buildProjectTree(flat: ProjectResponse[]): ProjectTreeNode[] {
  const active = flat.filter((p) => p.archivedAt == null);
  const byId = new Set(active.map((p) => p.id));

  const roots: ProjectResponse[] = [];
  const childrenByParent = new Map<string, ProjectResponse[]>();

  for (const project of active) {
    const parentId = project.parentId;
    if (parentId != null && byId.has(parentId)) {
      const siblings = childrenByParent.get(parentId) ?? [];
      siblings.push(project);
      childrenByParent.set(parentId, siblings);
    } else {
      // top-level, or an orphan whose parent is absent → promoted to top-level.
      roots.push(project);
    }
  }

  return roots.map((project) => ({
    project,
    children: (childrenByParent.get(project.id) ?? []).map((child) => ({ project: child, children: [] })),
  }));
}

/**
 * The left sidebar (T028/T049/T050; Constitution IV; research R8/R16). The Inbox entry (FR-021), the
 * one-level project tree assembled by {@link buildProjectTree}, a "New project" control, a minimal
 * keyboard-reachable "Archived" disclosure (unarchive, AS-11/R8), and — per project — Edit / Archive /
 * Delete affordances that open the {@link ProjectForm} (edit) and {@link DeleteProjectDialog}
 * (dispositions). Each project row links to its task VIEW (`/projects/[id]`).
 *
 * Preset color is NEVER the sole signal (FR-044): each project shows its icon glyph + name text, the
 * color is only a small decorative swatch. Names are React-escaped on render (FR-099). No hover-only
 * affordances (FR-046) — every control is a real, keyboard-reachable button.
 */
export function Sidebar() {
  const { data: projects } = useProjects();
  const active = projects ?? [];
  const tree = buildProjectTree(active);
  const { archiveProject } = useProjectMutations();

  const [showArchived, setShowArchived] = useState(false);
  const [formOpen, setFormOpen] = useState(false);
  const [editing, setEditing] = useState<ProjectResponse | null>(null);
  const [deleting, setDeleting] = useState<ProjectResponse | null>(null);
  const [archiving, setArchiving] = useState<ProjectResponse | null>(null);
  const [sharing, setSharing] = useState<ProjectResponse | null>(null);
  const [managing, setManaging] = useState<ProjectResponse | null>(null);

  // A personal project is shared via the confirm dialog; a shared project opens its members roster (the
  // hub for invite / role / transfer / remove / leave / unshare). Keyboard-reachable per FR-001/I.
  const onShareOrManage = (project: ProjectResponse) => {
    if (project.visibility === "shared") {
      setManaging(project);
    } else {
      setSharing(project);
    }
  };

  const activeChildCount = (project: ProjectResponse) =>
    active.filter((p) => p.parentId === project.id).length;

  // Archiving a parent that still has children PROMPTS for the child disposition (AS-10: archive, like
  // delete, asks cascade vs orphan-to-top + states blast radius); a childless project archives directly.
  const onArchive = (project: ProjectResponse) => {
    if (activeChildCount(project) > 0) {
      setArchiving(project);
    } else {
      archiveProject(project.id);
    }
  };

  return (
    <nav className="tf-sidebar" aria-label="Projects">
      <ul className="tf-sidebar__list">
        <li>
          <Link className="tf-sidebar__inbox" href="/">
            <span className="tf-sidebar__icon" aria-hidden="true">
              {iconGlyph("inbox")}
            </span>
            Inbox
          </Link>
        </li>
      </ul>

      <div className="tf-sidebar__section">
        <h2 className="tf-sidebar__heading" id="tf-sidebar-projects-heading">
          Projects
        </h2>
        <button
          type="button"
          className="tf-button tf-button--secondary"
          onClick={() => setFormOpen(true)}
        >
          New project
        </button>

        <ul className="tf-sidebar__tree" aria-labelledby="tf-sidebar-projects-heading">
          {tree.map((node) => (
            <li key={node.project.id}>
              <ProjectRow
                project={node.project}
                onEdit={() => setEditing(node.project)}
                onArchive={() => onArchive(node.project)}
                onDelete={() => setDeleting(node.project)}
                onShareOrManage={() => onShareOrManage(node.project)}
              />
              {node.children.length > 0 ? (
                <ul className="tf-sidebar__children">
                  {node.children.map((child) => (
                    <li key={child.project.id}>
                      <ProjectRow
                        project={child.project}
                        onEdit={() => setEditing(child.project)}
                        onArchive={() => onArchive(child.project)}
                        onDelete={() => setDeleting(child.project)}
                        onShareOrManage={() => onShareOrManage(child.project)}
                      />
                    </li>
                  ))}
                </ul>
              ) : null}
            </li>
          ))}
        </ul>
      </div>

      <div className="tf-sidebar__section">
        <button
          type="button"
          className="tf-sidebar__disclosure"
          aria-expanded={showArchived}
          onClick={() => setShowArchived((v) => !v)}
        >
          {showArchived ? "▾" : "▸"} Archived
        </button>
        {showArchived ? <ArchivedList /> : null}
      </div>

      <ProjectForm open={formOpen} onClose={() => setFormOpen(false)} mode="create" />
      <ProjectForm
        open={editing !== null}
        onClose={() => setEditing(null)}
        mode="edit"
        project={editing ?? undefined}
      />
      {deleting ? (
        <DeleteProjectFlow project={deleting} activeProjects={active} onClose={() => setDeleting(null)} />
      ) : null}
      {archiving ? (
        <ArchiveProjectDialog
          open
          onClose={() => setArchiving(null)}
          project={archiving}
          childCount={activeChildCount(archiving)}
        />
      ) : null}
      {sharing ? (
        <ShareProjectDialog open onClose={() => setSharing(null)} project={sharing} />
      ) : null}
      {managing ? (
        <MembersDialog open onClose={() => setManaging(null)} project={managing} />
      ) : null}
    </nav>
  );
}

/**
 * A single project entry: a link to its task view (icon + name + decorative swatch — color is never
 * the sole cue, FR-044; the name is React-escaped, FR-099) plus the Edit / Archive / Delete action
 * buttons. The buttons are siblings of (never nested in) the link, and each carries the project name
 * in its accessible label so AT users hear which project an action targets (FR-043).
 */
function ProjectRow({
  project,
  onEdit,
  onArchive,
  onDelete,
  onShareOrManage,
}: {
  project: ProjectResponse;
  onEdit: () => void;
  onArchive: () => void;
  onDelete: () => void;
  onShareOrManage: () => void;
}) {
  const isShared = project.visibility === "shared";
  // The caller's effective role (R17). On a personal project (always owner) sharing is offered; on a shared
  // project the owner sees "Members" (manage) while a non-owner member sees "Members" (read-only + leave).
  const shareLabel = isShared ? "Members" : "Share";

  return (
    <div className="tf-sidebar__project-row">
      <Link className="tf-sidebar__project" href={`/projects/${project.id}`} data-color={project.color}>
        <span className="tf-sidebar__icon" aria-hidden="true">
          {iconGlyph(project.icon)}
        </span>
        <span className="tf-sidebar__swatch" aria-hidden="true" data-color={project.color} />
        <span className="tf-sidebar__name">{project.name}</span>
        {isShared ? (
          // Shared-visibility indicator: TEXT label (never color/icon alone, FR-044).
          <span className="tf-sidebar__shared-indicator" data-testid="shared-indicator">
            <span aria-hidden="true">👥</span>
            <span className="tf-visually-hidden">Shared project</span>
          </span>
        ) : null}
      </Link>
      <span className="tf-sidebar__project-actions">
        <button
          type="button"
          className="tf-icon-button"
          aria-label={`${shareLabel === "Members" ? "Manage members of" : "Share"} ${project.name}`}
          onClick={onShareOrManage}
        >
          {shareLabel}
        </button>
        <button type="button" className="tf-icon-button" aria-label={`Edit ${project.name}`} onClick={onEdit}>
          Edit
        </button>
        <button type="button" className="tf-icon-button" aria-label={`Archive ${project.name}`} onClick={onArchive}>
          Archive
        </button>
        <button type="button" className="tf-icon-button" aria-label={`Delete ${project.name}`} onClick={onDelete}>
          Delete
        </button>
      </span>
    </div>
  );
}

/**
 * Resolves the project's live task count + child count, then renders {@link DeleteProjectDialog} so
 * its blast radius (Principle VII) and the task/child disposition prompts (FR-014/EC-03/AS-10) are
 * accurate. Mounted only while a project is pending deletion, so the task query runs on demand.
 */
function DeleteProjectFlow({
  project,
  activeProjects,
  onClose,
}: {
  project: ProjectResponse;
  activeProjects: ProjectResponse[];
  onClose: () => void;
}) {
  const { data: tasks, isPending } = useProjectTasks(project.id);
  const taskCount = tasks?.length ?? 0;
  const childCount = activeProjects.filter((p) => p.parentId === project.id).length;
  return (
    <DeleteProjectDialog
      open
      onClose={onClose}
      project={project}
      taskCount={taskCount}
      childCount={childCount}
      // Until the task count resolves, the disposition prompt may be incomplete — gate confirm so a
      // delete is never sent with a missing task disposition (which the server 422s, R5).
      busy={isPending}
    />
  );
}

/** The lazily-loaded archived listing behind the disclosure, each row offering unarchive (AS-11). */
function ArchivedList() {
  const { data: archived, isPending } = useArchivedProjects(true);
  const { unarchiveProject } = useProjectMutations();

  if (isPending) {
    return <p className="tf-sidebar__archived-empty">Loading…</p>;
  }
  if (!archived || archived.length === 0) {
    return <p className="tf-sidebar__archived-empty">No archived projects.</p>;
  }

  return (
    <ul className="tf-sidebar__archived">
      {archived.map((project) => (
        <li key={project.id} className="tf-sidebar__archived-row">
          <span className="tf-sidebar__icon" aria-hidden="true">
            {iconGlyph(project.icon)}
          </span>
          <span className="tf-sidebar__name">{project.name}</span>
          <button
            type="button"
            className="tf-button tf-button--secondary"
            onClick={() => unarchiveProject(project.id, project.version)}
          >
            Unarchive
          </button>
        </li>
      ))}
    </ul>
  );
}

/**
 * Maps a preset icon token to a glyph. Decorative (the name text is the meaning, FR-044), so the
 * glyphs are aria-hidden at the call sites; an unknown token falls back to a folder glyph.
 */
function iconGlyph(icon: string): string {
  const glyphs: Record<string, string> = {
    folder: "📁",
    inbox: "📥",
    briefcase: "💼",
    home: "🏠",
    star: "⭐",
    flag: "🚩",
    bookmark: "🔖",
    calendar: "📅",
    rocket: "🚀",
    target: "🎯",
    heart: "❤️",
    tag: "🏷️",
  };
  return glyphs[icon] ?? glyphs.folder!;
}

"use client";

import Link from "next/link";
import { usePathname } from "next/navigation";
import {
  Calendar,
  CalendarClock,
  ChevronDown,
  ChevronRight,
  Inbox,
  Plus,
  UserCheck,
} from "lucide-react";
import { useState } from "react";

import { ArchiveProjectDialog } from "@/components/projects/ArchiveProjectDialog";
import { DeleteProjectDialog } from "@/components/projects/DeleteProjectDialog";
import { MembersDialog } from "@/components/projects/MembersDialog";
import { ProjectForm } from "@/components/projects/ProjectForm";
import { ShareProjectDialog } from "@/components/projects/ShareProjectDialog";
import { Menu } from "@/components/ui/Menu";
import { useProjectTasks } from "@/hooks/useProjectTasks";
import { useArchivedProjects, useProjects, type ProjectResponse } from "@/hooks/useProjects";
import { useProjectMutations } from "@/hooks/useProjectMutations";
import { useViewCounts } from "@/hooks/useViewCounts";
import styles from "./Sidebar.module.css";

/** A node in the assembled one-level sidebar tree: a project plus its (at most one level) children. */
export interface ProjectTreeNode {
  project: ProjectResponse;
  children: ProjectTreeNode[];
}

/**
 * Assembles the one-level parent/child tree from the flat `GET /api/projects` list (research R16).
 * Pure (no React) so the grouping logic is unit-testable.
 *
 * - Archived rows are dropped from the default tree (defensive; the active query already excludes
 *   them, R8).
 * - A row whose `parentId` references a project NOT in the (active) list is an orphan and is
 *   promoted to top-level, so a child of an archived/absent parent still renders (never vanishes).
 * - Nesting is capped at one level by construction.
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
      roots.push(project);
    }
  }

  return roots.map((project) => ({
    project,
    children: (childrenByParent.get(project.id) ?? []).map((child) => ({ project: child, children: [] })),
  }));
}

/** The primary views (FR-109): every one a clickable entry with an icon and a live count. */
const PRIMARY_VIEWS = [
  { href: "/", label: "Inbox", icon: Inbox, countKey: "inbox" },
  { href: "/today", label: "Dziś", icon: Calendar, countKey: "today" },
  { href: "/upcoming", label: "Nadchodzące", icon: CalendarClock, countKey: "upcoming" },
  { href: "/assigned", label: "Przypisane", icon: UserCheck, countKey: "assigned" },
] as const;

/**
 * The left sidebar (rebuilt in slice 019, T039 — FR-109, US-18.AS-04, UIT-023/024/025).
 * ALL primary views (Inbox / Dziś / Nadchodzące / Przypisane) and the caller's projects
 * render as clickable entries with Lucide icons and authorization-scoped incomplete counts
 * from `GET /api/views/counts`; the active entry carries `aria-current`. Project emoji
 * icons stay (the FR-105 carve-out: user-chosen project icons). Collapse lives in the
 * shell (client state, not persisted).
 */
export function Sidebar() {
  const pathname = usePathname();
  const { data: projects } = useProjects();
  const { data: counts } = useViewCounts();
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

  const projectCount = (projectId: string): number | undefined =>
    counts?.projects.find((p) => p.projectId === projectId)?.count;

  const onShareOrManage = (project: ProjectResponse) => {
    if (project.visibility === "shared") {
      setManaging(project);
    } else {
      setSharing(project);
    }
  };

  const activeChildCount = (project: ProjectResponse) =>
    active.filter((p) => p.parentId === project.id).length;

  // Archiving a parent that still has children PROMPTS for the child disposition (AS-10);
  // a childless project archives directly (pre-redesign semantics preserved — INV-075).
  const onArchive = (project: ProjectResponse) => {
    if (activeChildCount(project) > 0) {
      setArchiving(project);
    } else {
      archiveProject(project.id);
    }
  };

  return (
    <nav className={styles.sidebar} aria-label="Projects">
      <ul className={styles.viewList}>
        {PRIMARY_VIEWS.map(({ href, label, icon: Icon, countKey }) => {
          const count = counts?.[countKey];
          const isActive = pathname === href;
          return (
            <li key={href}>
              <Link
                className={styles.viewEntry}
                href={href}
                aria-current={isActive ? "page" : undefined}
              >
                <span className={styles.icon} aria-hidden="true">
                  <Icon size={16} strokeWidth={1.75} />
                </span>
                <span className={styles.name}>{label}</span>
                {count !== undefined && count > 0 ? (
                  <span className={styles.count}>
                    <span className="sr-only">zadań: </span>
                    {count}
                  </span>
                ) : null}
              </Link>
            </li>
          );
        })}
      </ul>

      <div className={styles.section}>
        <div className={styles.sectionHeader}>
          <h2 className={styles.heading} id="tf-sidebar-projects-heading">
            Projects
          </h2>
          <button type="button" className={styles.newProject} onClick={() => setFormOpen(true)}>
            <Plus size={14} strokeWidth={1.75} aria-hidden="true" />
            New project
          </button>
        </div>

        <ul className={`${styles.tree} tf-sidebar__tree`} aria-labelledby="tf-sidebar-projects-heading">
          {tree.map((node) => (
            <li key={node.project.id}>
              <ProjectRow
                project={node.project}
                count={projectCount(node.project.id)}
                activePath={pathname}
                onEdit={() => setEditing(node.project)}
                onArchive={() => onArchive(node.project)}
                onDelete={() => setDeleting(node.project)}
                onShareOrManage={() => onShareOrManage(node.project)}
              />
              {node.children.length > 0 ? (
                <ul className={`${styles.children} tf-sidebar__children`}>
                  {node.children.map((child) => (
                    <li key={child.project.id}>
                      <ProjectRow
                        project={child.project}
                        count={projectCount(child.project.id)}
                        activePath={pathname}
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

      <div className={styles.section}>
        <button
          type="button"
          className={styles.disclosure}
          aria-expanded={showArchived}
          onClick={() => setShowArchived((v) => !v)}
        >
          <span aria-hidden="true">
            {showArchived ? (
              <ChevronDown size={14} strokeWidth={1.75} />
            ) : (
              <ChevronRight size={14} strokeWidth={1.75} />
            )}
          </span>{" "}
          Archived
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
 * A single project entry: a link to its task view (user-chosen emoji icon + name + count;
 * color never the sole cue, FR-044) plus a "⋯" menu with the management actions. The menu
 * items keep the pre-redesign affordance set for every member (server-side denial stays
 * authoritative — INV-017 preserved as-is).
 */
function ProjectRow({
  project,
  count,
  activePath,
  onEdit,
  onArchive,
  onDelete,
  onShareOrManage,
}: {
  project: ProjectResponse;
  count: number | undefined;
  activePath: string | null;
  onEdit: () => void;
  onArchive: () => void;
  onDelete: () => void;
  onShareOrManage: () => void;
}) {
  const isShared = project.visibility === "shared";
  const href = `/projects/${project.id}`;

  return (
    <div className={styles.projectRow}>
      <Link
        className={styles.viewEntry}
        href={href}
        data-color={project.color}
        aria-current={activePath === href ? "page" : undefined}
      >
        <span className={styles.icon} aria-hidden="true">
          {iconGlyph(project.icon)}
        </span>
        <span className={styles.name}>{project.name}</span>
        {isShared ? (
          <span className={styles.sharedIndicator} data-testid="shared-indicator">
            <span aria-hidden="true">
              <UserCheck size={12} strokeWidth={1.75} />
            </span>
            <span className="sr-only">Shared project</span>
          </span>
        ) : null}
        {count !== undefined && count > 0 ? (
          <span className={styles.count}>
            <span className="sr-only">zadań: </span>
            {count}
          </span>
        ) : null}
      </Link>
      <span className={styles.projectActions}>
        <Menu
          triggerLabel={`Akcje projektu ${project.name}`}
          menuLabel={`Akcje projektu ${project.name}`}
          triggerContent={<span aria-hidden="true">⋯</span>}
          triggerClassName={styles.projectMenuTrigger}
          items={[
            { id: "members", label: isShared ? "Members" : "Share", onSelect: onShareOrManage },
            { id: "edit", label: "Edit", onSelect: onEdit },
            { id: "archive", label: "Archive", onSelect: onArchive },
            { id: "delete", label: "Delete", onSelect: onDelete, destructive: true },
          ]}
        />
      </span>
    </div>
  );
}

/**
 * Resolves the project's live task count + child count, then renders {@link DeleteProjectDialog} so
 * its blast radius (Principle VII) and the disposition prompts (FR-014/EC-03/AS-10) are accurate.
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
      busy={isPending}
    />
  );
}

/** The lazily-loaded archived listing behind the disclosure, each row offering unarchive (AS-11). */
function ArchivedList() {
  const { data: archived, isPending } = useArchivedProjects(true);
  const { unarchiveProject } = useProjectMutations();

  if (isPending) {
    return <p className={styles.archivedEmpty}>Loading…</p>;
  }
  if (!archived || archived.length === 0) {
    return <p className={styles.archivedEmpty}>No archived projects.</p>;
  }

  return (
    <ul className={styles.archived}>
      {archived.map((project) => (
        <li key={project.id} className={`${styles.archivedRow} tf-sidebar__archived-row`}>
          <span className={styles.icon} aria-hidden="true">
            {iconGlyph(project.icon)}
          </span>
          <span className={styles.name}>{project.name}</span>
          <button
            type="button"
            className={styles.unarchive}
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
 * Maps a preset icon token to a glyph. Decorative (the name text is the meaning, FR-044) —
 * glyphs are aria-hidden at the call sites. Emoji here are the USER-CHOSEN project icons
 * (the FR-105 carve-out); app chrome uses Lucide.
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

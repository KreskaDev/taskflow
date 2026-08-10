"use client";

import { useEffect, useState } from "react";
import { formatInReferenceZone } from "@/lib/timezone";

import { LabelChips } from "@/components/labels/LabelChips";
import { LabelSelector } from "@/components/labels/LabelSelector";
import { ProjectSelector } from "@/components/projects/ProjectSelector";
import { AssigneePicker } from "@/components/tasks/AssigneePicker";
import { CommentThread } from "@/components/tasks/CommentThread";
import { PriorityPicker } from "@/components/tasks/PriorityPicker";
import { Avatar } from "@/components/ui/Avatar";
import { Button } from "@/components/ui/Button";
import { Checkbox } from "@/components/ui/Checkbox";
import { DateInput } from "@/components/ui/DateInput";
import { Textarea } from "@/components/ui/Textarea";
import { useCommentMutations } from "@/hooks/useCommentMutations";
import { useComments } from "@/hooks/useComments";
import { useDuplicateTask } from "@/hooks/useDuplicateTask";
import { useProjectMembers } from "@/hooks/useProjectMembers";
import { useProjects } from "@/hooks/useProjects";
import { useSession } from "@/hooks/useSession";
import { useTaskMutations } from "@/hooks/useTaskMutations";
import type { TaskResponse } from "@/hooks/useTasks";
import { taskTitleSchema } from "@/lib/validation/task";
import styles from "./TaskDrawer.module.css";

export const TASK_DRAWER_TITLE_ID = "task-drawer-title";

interface TaskDrawerProps {
  task: TaskResponse;
  /** Close the drawer (clears the `?task=` param — T052). */
  onClose: () => void;
}

/**
 * The task detail drawer content (slice 019, T051 — FR-106, US-18.AS-03): EVERY field
 * directly editable inline over the existing optimistic mutation hooks — title,
 * description, status, priority / project / label / assignee pickers re-skinned on
 * catalog components, due date via the catalog {@link DateInput} (Polish NL parse
 * unchanged) — plus, for shared-project tasks, the slice-009 comment thread (T053).
 */
export function TaskDrawer({ task, onClose }: TaskDrawerProps) {
  const {
    renameTask,
    setTaskDone,
    setTaskPriority,
    rescheduleTask,
    editTask,
    setTaskLabels,
    setTaskAssignees,
    moveTaskToProject,
    deleteTask,
  } = useTaskMutations();
  const { duplicateTask } = useDuplicateTask();
  const { data: projects } = useProjects();

  const [titleDraft, setTitleDraft] = useState(task.title);
  const [descriptionDraft, setDescriptionDraft] = useState(task.description ?? "");
  const [priorityOpen, setPriorityOpen] = useState(false);
  const [labelsOpen, setLabelsOpen] = useState(false);
  const [assignOpen, setAssignOpen] = useState(false);
  const [moveOpen, setMoveOpen] = useState(false);

  // Re-seed drafts when the drawer switches tasks (deep link / another row opened).
  useEffect(() => {
    setTitleDraft(task.title);
    setDescriptionDraft(task.description ?? "");
  }, [task.id, task.title, task.description]);

  const project = task.projectId != null ? (projects ?? []).find((p) => p.id === task.projectId) : undefined;
  const isShared = project?.visibility === "shared";
  const done = task.status === "done";

  const commitTitle = () => {
    const parsed = taskTitleSchema.safeParse(titleDraft);
    if (!parsed.success || parsed.data === task.title) {
      setTitleDraft(task.title);
      return;
    }
    renameTask(task.id, parsed.data);
  };

  const commitDescription = () => {
    const next = descriptionDraft.trim().length === 0 ? null : descriptionDraft;
    if ((task.description ?? null) === next) return;
    editTask(task.id, {
      title: task.title,
      description: next,
      priority: (task.priority ?? null) as "P0" | "P1" | "P2" | "P3" | null,
      dueDate: task.dueDate ? new Date(task.dueDate) : null,
      dueHasTime: task.dueDate ? (task.dueHasTime ?? false) : null,
      projectId: task.projectId ?? null,
    });
  };

  return (
    <div className={styles.drawer}>
      <header className={styles.header}>
        <Checkbox
          aria-label={done ? "Oznacz jako niezrobione" : "Oznacz jako zrobione"}
          checked={done}
          onChange={() => setTaskDone(task.id, !done)}
        />
        <input
          id={TASK_DRAWER_TITLE_ID}
          className={styles.title}
          aria-label="Tytuł zadania"
          value={titleDraft}
          maxLength={500}
          onChange={(e) => setTitleDraft(e.target.value)}
          onBlur={commitTitle}
          onKeyDown={(e) => {
            if (e.key === "Enter") {
              e.preventDefault();
              commitTitle();
            }
            if (e.key === "Escape") {
              // Field-level Esc cancels the EDIT (FR-030); the Drawer stays open.
              e.preventDefault();
              e.stopPropagation();
              setTitleDraft(task.title);
            }
          }}
        />
      </header>

      <p className={styles.taskId}>
        <span className="sr-only">identyfikator: </span>
        {task.id}
      </p>

      <dl className={styles.fields}>
        <div className={styles.field}>
          <dt>Priorytet</dt>
          <dd>
            <Button variant="secondary" onClick={() => setPriorityOpen(true)}>
              {task.priority ?? "— brak —"}
            </Button>
          </dd>
        </div>

        <div className={styles.field}>
          <dt>Projekt</dt>
          <dd>
            <Button variant="secondary" onClick={() => setMoveOpen(true)}>
              {project?.name ?? "Inbox"}
            </Button>
          </dd>
        </div>

        <div className={styles.field}>
          <dt>
            <label htmlFor="drawer-due">Termin</label>
          </dt>
          <dd className={styles.dueField}>
            {task.dueDate ? (
              <p className={styles.currentDue}>
                {formatInReferenceZone(new Date(task.dueDate), task.dueHasTime ? "dd.MM.yyyy HH:mm" : "dd.MM.yyyy")}
              </p>
            ) : null}
            <DateInput
              id="drawer-due"
              label="Nowy termin (np. jutro, piątek, 30.06)"
              onCommit={(dueDate, dueHasTime) => rescheduleTask(task.id, dueDate, dueHasTime)}
            />
          </dd>
        </div>

        <div className={styles.field}>
          <dt>Etykiety</dt>
          <dd className={styles.chipRow}>
            <LabelChips labelIds={task.labels} />
            <Button variant="secondary" onClick={() => setLabelsOpen(true)}>
              Zmień etykiety
            </Button>
          </dd>
        </div>

        {task.projectId != null ? (
          <div className={styles.field}>
            <dt>Przypisani</dt>
            <dd className={styles.chipRow}>
              <AssigneeAvatars projectId={task.projectId} assigneeIds={task.assignees} />
              <Button variant="secondary" onClick={() => setAssignOpen(true)}>
                Zmień przypisania
              </Button>
            </dd>
          </div>
        ) : null}
      </dl>

      <div className={styles.field}>
        <label htmlFor="drawer-description" className={styles.sectionLabel}>
          Opis
        </label>
        <Textarea
          id="drawer-description"
          aria-label="Opis"
          placeholder="Dodaj opis…"
          value={descriptionDraft}
          onChange={(e) => setDescriptionDraft(e.target.value)}
          onBlur={commitDescription}
          onKeyDown={(e) => {
            if (e.key === "Enter" && (e.ctrlKey || e.metaKey)) {
              e.preventDefault();
              commitDescription();
            }
            if (e.key === "Escape") {
              e.preventDefault();
              e.stopPropagation();
              setDescriptionDraft(task.description ?? "");
            }
          }}
        />
      </div>

      <div className={styles.actions}>
        <Button variant="secondary" onClick={() => duplicateTask(task)}>
          Duplikuj
        </Button>
        <Button
          variant="danger"
          onClick={() => {
            deleteTask(task.id);
            onClose();
          }}
        >
          Usuń
        </Button>
      </div>

      {isShared && task.projectId != null ? (
        <DrawerCommentSection taskId={task.id} projectId={task.projectId} role={project?.role ?? null} />
      ) : null}

      <PriorityPicker
        open={priorityOpen}
        current={task.priority}
        onClose={() => setPriorityOpen(false)}
        onSelect={(priority) => setTaskPriority(task.id, priority)}
      />
      {labelsOpen ? (
        <LabelSelector
          open
          current={task.labels}
          onClose={() => setLabelsOpen(false)}
          onSubmit={(ids) => {
            setTaskLabels(task.id, ids);
            setLabelsOpen(false);
          }}
        />
      ) : null}
      {assignOpen && task.projectId != null ? (
        <AssigneePicker
          open
          projectId={task.projectId}
          current={task.assignees}
          onClose={() => setAssignOpen(false)}
          onSubmit={(ids) => {
            setTaskAssignees(task.id, ids);
            setAssignOpen(false);
          }}
        />
      ) : null}
      <ProjectSelector
        open={moveOpen}
        onClose={() => setMoveOpen(false)}
        task={task}
        onSelect={(projectId) => {
          moveTaskToProject(task.id, projectId, task.projectId ?? null);
        }}
      />
    </div>
  );
}

/** Assignee identities as avatars (FR-105) resolved from the project roster. */
function AssigneeAvatars({ projectId, assigneeIds }: { projectId: string; assigneeIds: string[] }) {
  const { data: roster } = useProjectMembers(projectId, assigneeIds.length > 0);
  if (assigneeIds.length === 0) {
    return <span className={styles.emptyValue}>—</span>;
  }
  return (
    <span className={styles.avatarRow}>
      {assigneeIds.map((id) => {
        const member = roster?.members.find((m) => m.userId === id);
        return (
          <Avatar key={id} userId={id} displayName={member?.displayName ?? "?"} size="sm" />
        );
      })}
    </span>
  );
}

/**
 * The slice-009 comment thread inside the drawer (T053): chronological order, Avatar
 * authors, @mention chips, safeMarkdown sanitization, viewer sees the thread but NO
 * composer — behavior byte-for-byte (S4.4).
 */
function DrawerCommentSection({
  taskId,
  projectId,
  role,
}: {
  taskId: string;
  projectId: string;
  role: string | null;
}) {
  const session = useSession();
  const thread = useComments(taskId, true);
  const roster = useProjectMembers(projectId, true);
  const user = session.data?.user;
  const mutations = useCommentMutations(taskId, {
    userId: user?.id ?? "",
    displayName: user?.displayName ?? "",
  });

  return (
    <section className={styles.comments} aria-label="Komentarze">
      <h3 className={styles.sectionLabel}>Komentarze</h3>
      {thread.isError ? (
        <p role="alert">Nie udało się wczytać komentarzy.</p>
      ) : thread.isPending ? (
        <p>Wczytywanie komentarzy…</p>
      ) : (
        <CommentThread
          taskId={taskId}
          comments={thread.data.comments}
          role={role}
          members={roster.data?.members ?? []}
          onPost={mutations.postComment}
          onEdit={mutations.editComment}
          onDelete={mutations.deleteComment}
        />
      )}
    </section>
  );
}

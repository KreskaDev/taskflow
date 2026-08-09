"use client";

import { Dialog } from "@/components/ui/Dialog";
import { CommentThread } from "@/components/tasks/CommentThread";
import { useComments } from "@/hooks/useComments";
import { useCommentMutations } from "@/hooks/useCommentMutations";
import { useProjectMembers } from "@/hooks/useProjectMembers";
import { useSession } from "@/hooks/useSession";

const TITLE_ID = "task-detail-title";

interface TaskDetailPanelProps {
  /** Whether the panel is open (the thread is fetched LAZILY — only once opened, R14). */
  open: boolean;
  onClose: () => void;
  /** The task whose detail (and thread) is shown. */
  taskId: string;
  taskTitle: string;
  /** The task's SHARED project id (personal/Inbox tasks never open this surface — no comment thread). */
  projectId: string;
  /** The caller's effective role on the project, from the project read model ("owner"|"editor"|"viewer"). */
  role: string | null;
}

/**
 * The task detail panel (slice 009, T037; R14) — the NEW surface hosting the comment thread + composer
 * (no task-detail/drawer component existed before this slice). A modal {@link Dialog} (FR-101 focus
 * contract). The thread query and the members roster are both LAZY (`enabled: open`); mutations ride the
 * optimistic `useCommentMutations` family (Constitution III). Slice 016 later wires live inbound comments
 * into this surface — the recorded reconciliation contract: an inbound remote comment must NOT clobber a
 * pending local optimistic edit (R13).
 */
export function TaskDetailPanel({ open, onClose, taskId, taskTitle, projectId, role }: TaskDetailPanelProps) {
  const session = useSession();
  const thread = useComments(taskId, open);
  const roster = useProjectMembers(projectId, open);
  const user = session.data?.user;
  const mutations = useCommentMutations(taskId, {
    userId: user?.id ?? "",
    displayName: user?.displayName ?? "",
  });

  if (!open) return null;

  return (
    <Dialog open={open} onClose={onClose} titleId={TITLE_ID}>
      <div className="tf-task-detail">
        <h2 id={TITLE_ID} className="tf-dialog__title">
          {taskTitle}
        </h2>

        {thread.isError ? (
          <p role="alert" className="tf-comment-thread__error">
            Nie udało się wczytać komentarzy.
          </p>
        ) : thread.isPending ? (
          <p className="tf-comment-thread__empty">Wczytywanie komentarzy…</p>
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

        <div className="tf-dialog__actions">
          <button type="button" className="tf-button tf-button--secondary" onClick={onClose}>
            Zamknij (Esc)
          </button>
        </div>
      </div>
    </Dialog>
  );
}

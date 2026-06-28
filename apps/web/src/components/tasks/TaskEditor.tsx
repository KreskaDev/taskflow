"use client";

import { useState } from "react";

import { Dialog } from "@/components/ui/Dialog";
import type { components } from "@/lib/api/generated/schema";
import { useProjects } from "@/hooks/useProjects";
import type { Priority } from "@/lib/validation/task";

type TaskResponse = components["schemas"]["TaskResponse"];

const TITLE_ID = "task-editor-title";

export interface TaskEditorFields {
  title: string;
  description: string | null;
  priority: Priority;
  dueDate: Date | null;
  dueHasTime: boolean | null;
  projectId: string | null;
}

interface TaskEditorProps {
  /** Whether the editor is open (the `E` key opened it for the selected task). */
  open: boolean;
  /** The task being edited — seeds the form fields. */
  task: TaskResponse;
  /** Discard all changes and close (Esc / overlay click) — returns focus to the originating row. */
  onClose: () => void;
  /** Save the whole-object replace (`Ctrl+Enter`). The parent calls `editTask` and closes. */
  onSave: (fields: TaskEditorFields) => void;
}

/**
 * The `E` task editor (slice 005, AS-06/07/08). A modal {@link Dialog} (FR-101 focus contract: initial
 * focus on the title field, trap, Esc dismiss, return focus to the originating row). Edits title (focused),
 * description, priority, project, and the due date (clearable; the canonical due-change UX is the `T`
 * reschedule, so the editor preserves the existing instant unless cleared). `Ctrl+Enter` saves the whole
 * object atomically; `Esc` discards entirely (no request sent). Single-key shortcuts are suppressed while a
 * field is focused (FR-031). The description is plain text — React-escaped on render, no markdown renderer
 * this slice (FR-099/R3).
 */
export function TaskEditor({ open, task, onClose, onSave }: TaskEditorProps) {
  const { data: projects } = useProjects();
  const [title, setTitle] = useState(task.title);
  const [description, setDescription] = useState(task.description ?? "");
  const [priority, setPriority] = useState<Priority>((task.priority as Priority) ?? null);
  const [clearDue, setClearDue] = useState(false);
  const [projectId, setProjectId] = useState<string | null>(task.projectId ?? null);

  const onKeyDown = (event: React.KeyboardEvent<HTMLDivElement>) => {
    // Ctrl+Enter (or Cmd+Enter) saves the whole object atomically (AS-07).
    if (event.key === "Enter" && (event.ctrlKey || event.metaKey)) {
      event.preventDefault();
      save();
    }
  };

  const save = () => {
    onSave({
      title: title.trim(),
      description: description.trim().length === 0 ? null : description.trim(),
      priority,
      // Due editing is owned by the `T` reschedule; the editor preserves the existing instant unless cleared.
      dueDate: clearDue ? null : task.dueDate ? new Date(task.dueDate) : null,
      dueHasTime: clearDue ? null : task.dueHasTime ?? null,
      projectId,
    });
  };

  if (!open) return null;

  return (
    <Dialog open={open} onClose={onClose} titleId={TITLE_ID}>
      <div onKeyDown={onKeyDown}>
        <h2 id={TITLE_ID} className="tf-dialog__title">
          Edytuj zadanie
        </h2>

        <label className="tf-field">
          <span className="tf-field__label">Tytuł</span>
          <input
            type="text"
            className="tf-field__input"
            // Initial focus lands here (the first focusable in the dialog) — AS-06 "title field focused".
            autoFocus
            maxLength={500}
            value={title}
            onChange={(event) => setTitle(event.target.value)}
          />
        </label>

        <label className="tf-field">
          <span className="tf-field__label">Opis</span>
          <textarea
            className="tf-field__input"
            rows={4}
            maxLength={8000}
            value={description}
            onChange={(event) => setDescription(event.target.value)}
          />
        </label>

        <label className="tf-field">
          <span className="tf-field__label">Priorytet</span>
          <select
            className="tf-field__input"
            value={priority ?? ""}
            onChange={(event) => setPriority((event.target.value || null) as Priority)}
          >
            <option value="">— brak —</option>
            <option value="P0">P0</option>
            <option value="P1">P1</option>
            <option value="P2">P2</option>
            <option value="P3">P3</option>
          </select>
        </label>

        <label className="tf-field">
          <span className="tf-field__label">Projekt</span>
          <select
            className="tf-field__input"
            value={projectId ?? ""}
            onChange={(event) => setProjectId(event.target.value || null)}
          >
            <option value="">Inbox</option>
            {(projects ?? []).map((p) => (
              <option key={p.id} value={p.id}>
                {p.name}
              </option>
            ))}
          </select>
        </label>

        {task.dueDate ? (
          <label className="tf-field tf-field--inline">
            <input type="checkbox" checked={clearDue} onChange={(event) => setClearDue(event.target.checked)} />
            <span>Usuń termin</span>
          </label>
        ) : null}

        <div className="tf-dialog__actions">
          <button type="button" className="tf-button" onClick={save}>
            Zapisz (Ctrl+Enter)
          </button>
          <button type="button" className="tf-button tf-button--secondary" onClick={onClose}>
            Anuluj (Esc)
          </button>
        </div>
      </div>
    </Dialog>
  );
}

"use client";

import { useState } from "react";

import { Button } from "@/components/ui/Button";
import { Dialog, DialogActions, DialogTitle } from "@/components/ui/Dialog";
import dialogStyles from "@/components/ui/Dialog.module.css";
import { Input } from "@/components/ui/Input";
import { useProjects, type ProjectResponse } from "@/hooks/useProjects";
import { nestingPreventionMessage, useProjectMutations } from "@/hooks/useProjectMutations";
import { PROJECT_COLORS, PROJECT_ICONS } from "@/lib/projectPresets";
import { createProjectSchema, editProjectSchema } from "@/lib/validation/project";
import styles from "./ProjectForm.module.css";

const TITLE_ID = "project-form-title";
const NAME_ID = "project-form-name";
const NESTING_ERROR_ID = "project-form-nesting-error";

interface ProjectFormProps {
  open: boolean;
  onClose: () => void;
  /** `create` mints a new project; `edit` whole-object-replaces the loaded `project` (R4). */
  mode: "create" | "edit";
  /** The project being edited (required for `mode="edit"`); seeds the form fields. */
  project?: ProjectResponse;
}

/**
 * The project create/edit form (T029; FR-031/FR-044/FR-101, research R10/R15). A modal dialog
 * (FR-101 focus contract via {@link Dialog}: initial focus, trap, Esc, return focus) with a name
 * input, a preset COLOR picker, a preset ICON picker, and a parent selector. Preset color is NEVER
 * the sole signal (FR-044) — the icon picker + the labelled name carry the meaning, color is a
 * decorative swatch. The name input is a native `<input>`, so the global single-key gate already
 * suppresses bare keys (`C`/`E`/etc.) while it holds focus (FR-031) — no extra code needed.
 *
 * The one-level-nesting prevention message (AS-03/AS-09, R15) is computed client-side from the
 * loaded tree via {@link nestingPreventionMessage} and shown inline, blocking submit, with no
 * round-trip; the server re-validates authoritatively (R3).
 */
export function ProjectForm({ open, onClose, mode, project }: ProjectFormProps) {
  // Render the stateful body only while open, keyed on the target, so every open is a FRESH mount
  // with re-seeded fields — a successful create/edit + close never leaves stale values behind on
  // the next open (mirrors TaskCapture's clear-on-close discipline). The Dialog owns the FR-101
  // focus contract; gating the body inside it keeps the dialog chrome stable.
  if (!open) return null;
  return (
    <ProjectFormBody
      key={`${mode}:${project?.id ?? "new"}`}
      onClose={onClose}
      mode={mode}
      project={project}
    />
  );
}

function ProjectFormBody({ onClose, mode, project }: Omit<ProjectFormProps, "open">) {
  const { data: projects } = useProjects();
  const { createProject, editProject } = useProjectMutations();

  const [name, setName] = useState(project?.name ?? "");
  const [color, setColor] = useState<string>(project?.color ?? PROJECT_COLORS[0]);
  const [icon, setIcon] = useState<string>(project?.icon ?? PROJECT_ICONS[0]);
  const [parentId, setParentId] = useState<string | null>(project?.parentId ?? null);

  // Candidate parents: top-level projects other than the one being edited (one-level rule, R15).
  const parentChoices = (projects ?? []).filter(
    (p) => p.parentId == null && p.id !== project?.id && p.archivedAt == null,
  );

  const nestingError = project
    ? nestingPreventionMessage(projects ?? [], project.id, parentId)
    : // On create the new id is not yet in the tree; only the "parent is a child" shape can apply.
      nestingPreventionMessage(projects ?? [], "", parentId);

  const submit = () => {
    if (nestingError) return; // client-side guard (R15); the button is also disabled.

    if (mode === "edit" && project) {
      const parsed = editProjectSchema.safeParse({ name, color, icon, parentId, version: project.version });
      if (!parsed.success) return; // empty/invalid name → no-op, stay open.
      editProject(project.id, { name: parsed.data.name, color: parsed.data.color, icon: parsed.data.icon, parentId: parsed.data.parentId });
    } else {
      const parsed = createProjectSchema.safeParse({ name, color, icon, parentId: parentId ?? undefined });
      if (!parsed.success) return;
      createProject({ name: parsed.data.name, color: parsed.data.color, icon: parsed.data.icon, parentId: parsed.data.parentId ?? null });
    }
    onClose();
  };

  return (
    <Dialog open onClose={onClose} titleId={TITLE_ID}>
      <DialogTitle id={TITLE_ID}>{mode === "edit" ? "Edit project" : "New project"}</DialogTitle>

      <label className={dialogStyles.fieldRow} htmlFor={NAME_ID}>
        <span>Name</span>
        <Input
          id={NAME_ID}
          type="text"
          aria-label="Project name"
          maxLength={200}
          value={name}
          onChange={(event) => setName(event.target.value)}
        />
      </label>

      <fieldset className={styles.colors}>
        <legend>Color</legend>
        {PROJECT_COLORS.map((c) => (
          <label key={c} className={styles.option}>
            <input
              type="radio"
              name="project-color"
              value={c}
              checked={color === c}
              onChange={() => setColor(c)}
            />
            <span className={styles.swatch} aria-hidden="true" data-color={c} />
            <span>{c}</span>
          </label>
        ))}
      </fieldset>

      <fieldset className={styles.icons}>
        <legend>Icon</legend>
        {PROJECT_ICONS.map((i) => (
          <label key={i} className={styles.option}>
            <input
              type="radio"
              name="project-icon"
              value={i}
              checked={icon === i}
              onChange={() => setIcon(i)}
            />
            <span>{i}</span>
          </label>
        ))}
      </fieldset>

      <label className={dialogStyles.fieldRow} htmlFor="project-form-parent">
        <span>Parent project</span>
        <select
          id="project-form-parent"
          value={parentId ?? ""}
          onChange={(event) => setParentId(event.target.value === "" ? null : event.target.value)}
        >
          <option value="">No parent (top-level)</option>
          {parentChoices.map((p) => (
            <option key={p.id} value={p.id}>
              {p.name}
            </option>
          ))}
        </select>
      </label>

      <p id={NESTING_ERROR_ID} className={styles.status} role="status" aria-live="polite">
        {nestingError ?? ""}
      </p>

      <DialogActions>
        <Button variant="secondary" onClick={onClose}>
          Cancel
        </Button>
        <Button onClick={submit} disabled={nestingError !== null}>
          {mode === "edit" ? "Save" : "Create"}
        </Button>
      </DialogActions>
    </Dialog>
  );
}

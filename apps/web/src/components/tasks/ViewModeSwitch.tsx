"use client";

import { LayoutGrid, List } from "lucide-react";

import type { ProjectViewMode } from "@/hooks/usePersistedProjectView";
import styles from "./ViewModeSwitch.module.css";

/**
 * The visible project-view mode switch (slice 010, T019 — US-03.AS-02, FR-103):
 * „Lista” | „Tablica” as two `aria-pressed` toggle buttons (each an ordinary tab stop —
 * keyboard-operable without a composite-widget pattern; contracts/ui-board-list.md allows
 * exactly this shape). Lucide `List`/`LayoutGrid` glyphs; the TEXT carries the meaning.
 */
export function ViewModeSwitch({
  mode,
  onChange,
}: {
  mode: ProjectViewMode;
  onChange: (mode: ProjectViewMode) => void;
}) {
  return (
    <div className={styles.switch}>
      <button
        type="button"
        className={styles.option}
        aria-pressed={mode === "list"}
        onClick={() => onChange("list")}
      >
        <List size={14} strokeWidth={1.75} aria-hidden="true" />
        Lista
      </button>
      <button
        type="button"
        className={styles.option}
        aria-pressed={mode === "board"}
        onClick={() => onChange("board")}
      >
        <LayoutGrid size={14} strokeWidth={1.75} aria-hidden="true" />
        Tablica
      </button>
    </div>
  );
}

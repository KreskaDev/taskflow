"use client";

import { useCallback, useEffect, useState } from "react";

import type { ProjectGroupBy } from "@/lib/board";

/** The project view's two projections (US-03.AS-02). */
export type ProjectViewMode = "list" | "board";

const MODE_VALUES: readonly ProjectViewMode[] = ["list", "board"];
const GROUP_BY_VALUES: readonly ProjectGroupBy[] = ["none", "status", "priority", "cycle"];

const modeKey = (projectId: string): string => `taskflow.project-view.${projectId}`;
const groupByKey = (projectId: string): string => `taskflow.project-groupby.${projectId}`;

/** Reads a persisted value, degrading to the default on absence, invalidity, or a blocked storage. */
function readStored<T extends string>(key: string, allowed: readonly T[], fallback: T): T {
  try {
    const raw = window.localStorage.getItem(key);
    return allowed.includes(raw as T) ? (raw as T) : fallback;
  } catch {
    return fallback; // Storage unavailable (privacy mode / SSR) — the defaults stand.
  }
}

/** Persists a value, ignoring storage failures (the in-memory state still drives the UI). */
function writeStored(key: string, value: string): void {
  try {
    window.localStorage.setItem(key, value);
  } catch {
    // Best-effort persistence only — device-local presentation state (D8).
  }
}

/**
 * Per-project view mode + group-by persistence (slice 010, research D8/D9 — US-03.AS-02/AS-07).
 * Device-local presentation state over `localStorage` — NOT system-of-record data (Principle V
 * governs data; this is the slice-018 theming posture). Hydration-safe: state initializes to
 * the defaults (`list`/`none`) and an effect swaps to the stored value after mount, so SSR and
 * the first client render agree (a brief default flash is the accepted cost).
 */
export function usePersistedProjectView(projectId: string): {
  mode: ProjectViewMode;
  setMode: (mode: ProjectViewMode) => void;
  groupBy: ProjectGroupBy;
  setGroupBy: (groupBy: ProjectGroupBy) => void;
} {
  const [mode, setModeState] = useState<ProjectViewMode>("list");
  const [groupBy, setGroupByState] = useState<ProjectGroupBy>("none");

  useEffect(() => {
    setModeState(readStored(modeKey(projectId), MODE_VALUES, "list"));
    setGroupByState(readStored(groupByKey(projectId), GROUP_BY_VALUES, "none"));
  }, [projectId]);

  const setMode = useCallback(
    (next: ProjectViewMode): void => {
      setModeState(next);
      writeStored(modeKey(projectId), next);
    },
    [projectId],
  );

  const setGroupBy = useCallback(
    (next: ProjectGroupBy): void => {
      setGroupByState(next);
      writeStored(groupByKey(projectId), next);
    },
    [projectId],
  );

  return { mode, setMode, groupBy, setGroupBy };
}

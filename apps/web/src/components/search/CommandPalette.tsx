"use client";

import { useEffect, useId, useMemo, useState } from "react";
import {
  Calendar,
  CalendarClock,
  FolderPlus,
  Inbox,
  type LucideIcon,
  Plus,
  RefreshCw,
  Search,
  Settings,
  Tag,
  UserCheck,
} from "lucide-react";

import { Button } from "@/components/ui/Button";
import { Dialog, DialogTitle } from "@/components/ui/Dialog";
import { EmptyState } from "@/components/ui/EmptyState";
import { LiveRegion } from "@/components/ui/LiveRegion";
import { useProjects } from "@/hooks/useProjects";
import { useProjectMutations } from "@/hooks/useProjectMutations";
import { useSearch } from "@/hooks/useSearch";
import { formatDueDate } from "@/components/tasks/TaskRow";
import {
  buildPaletteGroups,
  flattenOptions,
  type PaletteAction,
  type PaletteOption,
  projectHref,
  resultCount,
  taskHref,
} from "@/lib/palette";
import styles from "./CommandPalette.module.css";

const TITLE_ID = "command-palette-title";

export const PALETTE_TITLE = "Szukaj i wykonaj";
export const PALETTE_INPUT_LABEL = "Szukaj zadań, projektów, etykiet i akcji";
export const PALETTE_ERROR = "Nie udało się wyszukać — spróbuj ponownie";

const ICONS: Record<PaletteAction["icon"], LucideIcon> = {
  plus: Plus,
  "folder-plus": FolderPlus,
  "refresh-cw": RefreshCw,
  inbox: Inbox,
  calendar: Calendar,
  "calendar-clock": CalendarClock,
  "user-check": UserCheck,
  settings: Settings,
};

interface CommandPaletteProps {
  open: boolean;
  /** Close without acting (Esc / overlay) — focus returns to the opener (FR-101). */
  onClose: () => void;
  /** Navigate to a result's location (the palette closes first). */
  onNavigate: (href: string) => void;
  /** Execute a dialog-opening action AFTER the palette closes (focus hand-over). */
  onAction: (action: PaletteAction) => void;
}

/**
 * The command palette (slice 013, US-04 — contracts/ui-palette.md, research D7): a catalog {@link Dialog}
 * hosting a `combobox` input over a grouped `listbox` (Akcje → Zadania → Projekty → Etykiety). `↑/↓` move
 * `aria-activedescendant`, Enter activates, every option is a click target (FR-103/FR-046); Esc/overlay close
 * (US-04.AS-06). Results come from {@link useSearch} (server-scoped, FR-065–068); the action catalogue is
 * client-side and shows NO shortcut labels (FR-033 deferred). Result counts are announced politely (FR-101).
 */
export function CommandPalette({ open, onClose, onNavigate, onAction }: CommandPaletteProps) {
  const id = useId();
  const listboxId = `${id}-listbox`;
  const [query, setQuery] = useState("");
  const [activeIndex, setActiveIndex] = useState(0);
  const search = useSearch(query);
  const { data: projects } = useProjects();
  const { unarchiveProject } = useProjectMutations();

  const groups = useMemo(() => buildPaletteGroups(query, search.data), [query, search.data]);
  const options = useMemo(() => flattenOptions(groups), [groups]);
  const active = options[Math.min(activeIndex, Math.max(options.length - 1, 0))];

  // Reset the draft on every open so a stale query never greets the next invocation.
  useEffect(() => {
    if (open) {
      setQuery("");
      setActiveIndex(0);
    }
  }, [open]);

  useEffect(() => {
    setActiveIndex(0);
  }, [query]);

  const activate = (option: PaletteOption | undefined) => {
    if (!option) return;
    switch (option.kind) {
      case "action":
        onClose();
        onAction(option.action);
        return;
      case "task":
        onClose();
        onNavigate(taskHref(option.task));
        return;
      case "project":
        onClose();
        onNavigate(projectHref(option.project));
        return;
      case "label":
        // Labels have no page — search tasks by the label's name instead (Clarifications 2026-11-08).
        setQuery(option.label.name);
        return;
    }
  };

  const onKeyDown = (event: React.KeyboardEvent<HTMLInputElement>) => {
    if (event.key === "ArrowDown") {
      event.preventDefault();
      setActiveIndex((i) => (options.length === 0 ? 0 : (i + 1) % options.length));
    } else if (event.key === "ArrowUp") {
      event.preventDefault();
      setActiveIndex((i) => (options.length === 0 ? 0 : (i - 1 + options.length) % options.length));
    } else if (event.key === "Enter") {
      event.preventDefault();
      activate(active);
    }
  };

  if (!open) return null;

  const trimmed = query.trim();
  const count = resultCount(search.data);
  const announcement = trimmed.length === 0 ? "" : search.isError ? PALETTE_ERROR : search.isFetching && !search.data ? "Szukam…" : count === 0 ? "Brak wyników" : `${String(count)} wyników`;
  const noResults = trimmed.length > 0 && !search.isError && !search.isFetching && search.data !== undefined && groups.length === 0;
  const projectName = (projectId: string | null | undefined) =>
    projectId ? (projects ?? []).find((p) => p.id === projectId)?.name ?? "Projekt" : "Inbox";

  return (
    <Dialog open={open} onClose={onClose} titleId={TITLE_ID} size="wide">
      <div className={styles.palette}>
        <DialogTitle id={TITLE_ID}>{PALETTE_TITLE}</DialogTitle>

        <div className={styles.inputRow}>
          <Search size={16} strokeWidth={1.75} aria-hidden="true" />
          <input
            className={styles.input}
            type="text"
            role="combobox"
            aria-label={PALETTE_INPUT_LABEL}
            aria-expanded={options.length > 0}
            aria-controls={listboxId}
            aria-activedescendant={active ? `${id}-${active.id}` : undefined}
            aria-autocomplete="list"
            autoComplete="off"
            placeholder="Wpisz, czego szukasz…"
            value={query}
            onChange={(event) => setQuery(event.target.value)}
            onKeyDown={onKeyDown}
          />
        </div>

        <LiveRegion politeness="polite">{announcement}</LiveRegion>

        {search.isError ? (
          <div className={styles.error} role="alert">
            <span>{PALETTE_ERROR}</span>
            <Button variant="secondary" onClick={() => void search.refetch()}>
              Spróbuj ponownie
            </Button>
          </div>
        ) : null}

        {noResults ? (
          <EmptyState hint={`Brak wyników dla „${trimmed}”`} action={<span className={styles.hint}>Spróbuj innego słowa</span>} />
        ) : null}

        <div id={listboxId} role="listbox" aria-label="Wyniki" className={styles.listbox}>
          {groups.map((group) => (
            <div key={group.key} role="group" aria-labelledby={`${id}-${group.key}-heading`} className={styles.group}>
              <div id={`${id}-${group.key}-heading`} className={styles.groupHeading}>
                {group.heading}
              </div>
              {group.options.map((option) => {
                const isActive = active?.id === option.id;
                return (
                  <div
                    key={option.id}
                    id={`${id}-${option.id}`}
                    role="option"
                    aria-selected={isActive}
                    className={[styles.option, isActive ? styles.active : null].filter(Boolean).join(" ")}
                    onMouseEnter={() => setActiveIndex(options.indexOf(option))}
                    onClick={() => activate(option)}
                  >
                    {renderOption(option, projectName, (project) => unarchiveProject(project.id, project.version))}
                  </div>
                );
              })}
            </div>
          ))}
        </div>
      </div>
    </Dialog>
  );
}

function renderOption(
  option: PaletteOption,
  projectName: (projectId: string | null | undefined) => string,
  onUnarchive: (project: { id: string; version: number }) => void,
) {
  switch (option.kind) {
    case "action": {
      const Icon = ICONS[option.action.icon];
      return (
        <>
          <Icon size={14} strokeWidth={1.75} aria-hidden="true" />
          <span className={styles.label}>{option.action.label}</span>
        </>
      );
    }
    case "task":
      return (
        <>
          <span className={styles.label}>{option.task.title}</span>
          <span className={styles.meta}>
            <span className="sr-only">lokalizacja: </span>
            {projectName(option.task.projectId)}
          </span>
          {option.task.dueDate ? (
            <span className={styles.meta}>
              <span className="sr-only">termin: </span>
              {formatDueDate(option.task.dueDate, option.task.dueHasTime)}
            </span>
          ) : null}
          {option.task.status === "done" ? <span className={styles.meta}>zrobione</span> : null}
        </>
      );
    case "project": {
      const archived = option.project.archivedAt != null;
      return (
        <>
          <span className={styles.label}>
            {option.project.name}
            {archived ? <span className={styles.meta}> (zarchiwizowany)</span> : null}
          </span>
          {archived ? (
            <Button
              variant="secondary"
              onClick={(event) => {
                event.stopPropagation();
                onUnarchive({ id: option.project.id, version: option.project.version });
              }}
            >
              Przywróć
            </Button>
          ) : null}
        </>
      );
    }
    case "label":
      return (
        <>
          <Tag size={14} strokeWidth={1.75} aria-hidden="true" />
          <span className={styles.label}>{option.label.name}</span>
          <span className={styles.meta}>Szukaj zadań z etykietą</span>
        </>
      );
  }
}

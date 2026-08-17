"use client";

import { useEffect, useMemo, useState } from "react";

import { CloseCycleDialog, type CloseCycleConfirm } from "@/components/cycles/CloseCycleDialog";
import { CycleFormDialog, type CycleFormFields } from "@/components/cycles/CycleFormDialog";
import { CycleMetrics } from "@/components/cycles/CycleMetrics";
import { TaskRow, taskOptionId } from "@/components/tasks/TaskRow";
import { Button } from "@/components/ui/Button";
import { EmptyState } from "@/components/ui/EmptyState";
import { Skeleton } from "@/components/ui/Skeleton";
import { useToast } from "@/components/ui/Toast";
import { useCycles, useCycleTasks, useCycleMutations } from "@/hooks/useCycles";
import { useMe } from "@/hooks/useMe";
import { useArchivedProjects, useProjects } from "@/hooks/useProjects";
import { mapError } from "@/lib/api/client";
import {
  cycleFormPrefill,
  cycleStatusLabel,
  defaultCycleSelection,
  isCycleOverdue,
  orderCycles,
  type CycleResponse,
} from "@/lib/cycles";
import { listboxKeyDown } from "@/lib/listboxKeys";
import styles from "./CycleView.module.css";

type FormState = { mode: "create" } | { mode: "edit"; cycle: CycleResponse } | null;

/**
 * The `/cycle` management surface (slice 011, T018 — US-05.AS-03, FR-020/FR-026; the SINGLE
 * cycle-management surface per Clarifications): D5-ordered switcher with status suffixes
 * (default: active → next planned → FR-110 empty state), the FR-026 metrics strip, the
 * caller-visible task rows (grid-pattern row catalog; EC-12 archived-project rows included),
 * and the lifecycle affordances — every operation a VISIBLE control (FR-103). „Usuń" stays
 * visible on an active cycle and refuses with the AS-07 message (client guard; server 422
 * backstop). An overdue active cycle renders the manual-close banner (the server never
 * auto-transitions).
 */
export function CycleView() {
  const { data: cycles, isPending } = useCycles();
  const { data: me } = useMe();
  const { data: projects } = useProjects();
  // EC-12: archived-project tasks stay visible here — resolve their project chip names too.
  const { data: archivedProjects } = useArchivedProjects(true);
  const { createCycle, editCycle, activateCycle, closeCycle, deleteCycle } = useCycleMutations();
  const { push } = useToast();

  const list = useMemo(() => orderCycles(cycles ?? []), [cycles]);
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const currentId =
    selectedId !== null && list.some((c) => c.id === selectedId) ? selectedId : defaultCycleSelection(list);
  const current = list.find((c) => c.id === currentId) ?? null;

  const { data: tasks, isPending: tasksPending } = useCycleTasks(currentId);
  const [form, setForm] = useState<FormState>(null);
  const [closing, setClosing] = useState(false);
  const [rowIndex, setRowIndex] = useState(0);

  const rows = tasks ?? [];
  useEffect(() => {
    setRowIndex((i) => Math.min(i, Math.max(0, rows.length - 1)));
  }, [rows.length]);

  const projectName = (projectId: string | null | undefined): string | null => {
    if (projectId == null) return null;
    return (
      projects?.find((p) => p.id === projectId)?.name ??
      archivedProjects?.find((p) => p.id === projectId)?.name ??
      null
    );
  };

  const now = new Date();
  const overdue = current !== null && current.status === "active" && isCycleOverdue(current.endDate, now);
  const nextPlanned = list.find((c) => c.status === "planned" && c.id !== current?.id) ?? null;

  const formInitial: CycleFormFields =
    form?.mode === "edit"
      ? { name: form.cycle.name, startDate: form.cycle.startDate, endDate: form.cycle.endDate }
      : cycleFormPrefill(list, me?.cycleDefaultDurationDays ?? 14, now);

  const submitForm = async (fields: CycleFormFields): Promise<void> => {
    try {
      if (form?.mode === "edit") {
        await editCycle({ id: form.cycle.id, version: form.cycle.version, ...fields });
      } else {
        const created = await createCycle(fields.name, fields.startDate, fields.endDate);
        setSelectedId(created.id);
      }
      setForm(null);
    } catch {
      // FR-049: the failure toast + announcement come from the global mutation announcer.
    }
  };

  const onActivate = async (cycle: CycleResponse): Promise<void> => {
    try {
      await activateCycle(cycle.id, cycle.version);
    } catch {
      /* announced globally */
    }
  };

  const onDelete = async (cycle: CycleResponse): Promise<void> => {
    // US-05.AS-07: the control stays VISIBLE on an active cycle; invoking it is PREVENTED
    // WITH A MESSAGE (client guard — the server 422 is the backstop).
    if (cycle.status === "active") {
      push(mapError("cycle_active_delete_forbidden").message, { variant: "error" });
      return;
    }
    try {
      await deleteCycle(cycle.id, cycle.version);
      setSelectedId(null);
    } catch {
      /* announced globally (e.g. cycle_not_empty) */
    }
  };

  const onCloseConfirm = async (choice: CloseCycleConfirm): Promise<void> => {
    if (!current) return;
    try {
      await closeCycle({ id: current.id, version: current.version, ...choice });
      setClosing(false);
    } catch {
      /* announced globally */
    }
  };

  const newCycleButton = (
    <Button variant="primary" onClick={() => setForm({ mode: "create" })}>
      Nowy cykl
    </Button>
  );

  if (isPending) {
    return (
      <div className={styles.view}>
        <h1 className={styles.heading}>Cykl</h1>
        <Skeleton variant="row" count={4} />
      </div>
    );
  }

  return (
    <div className={styles.view}>
      <h1 className={styles.heading}>Cykl</h1>

      {list.length === 0 ? (
        <EmptyState
          hint="Nie masz jeszcze żadnego cyklu. Utwórz pierwszy, aby planować pracę w iteracjach."
          action={newCycleButton}
        />
      ) : (
        <>
          <div className={styles.toolbar}>
            <label className={styles.switcher}>
              <span className={styles.switcherLabel}>Wybrany cykl</span>
              <select
                className={styles.switcherSelect}
                value={currentId ?? ""}
                onChange={(e) => setSelectedId(e.target.value)}
              >
                {currentId === null ? (
                  <option value="" disabled>
                    — wybierz cykl —
                  </option>
                ) : null}
                {list.map((cycle) => (
                  <option key={cycle.id} value={cycle.id}>
                    {cycle.name} ({cycleStatusLabel(cycle.status)})
                  </option>
                ))}
              </select>
            </label>
            <div className={styles.actions}>
              {newCycleButton}
              {current ? (
                <>
                  <Button variant="secondary" onClick={() => setForm({ mode: "edit", cycle: current })}>
                    Edytuj
                  </Button>
                  {current.status === "planned" ? (
                    <Button variant="secondary" onClick={() => onActivate(current)}>
                      Aktywuj
                    </Button>
                  ) : null}
                  {current.status === "active" ? (
                    <Button variant="secondary" onClick={() => setClosing(true)}>
                      Zamknij cykl
                    </Button>
                  ) : null}
                  <Button variant="danger" onClick={() => onDelete(current)}>
                    Usuń
                  </Button>
                </>
              ) : null}
            </div>
          </div>

          {current === null ? (
            <EmptyState hint="Wybierz cykl z listy lub utwórz nowy." action={null} />
          ) : (
            <>
              {overdue ? (
                <div className={styles.overdueBanner}>
                  <p className={styles.overdueText}>Cykl dobiegł końca — zamknij go.</p>
                  <Button variant="secondary" onClick={() => setClosing(true)}>
                    Zamknij cykl
                  </Button>
                </div>
              ) : null}

              <CycleMetrics cycle={current} now={now} />

              {tasksPending ? (
                <Skeleton variant="row" count={3} />
              ) : rows.length === 0 ? (
                <p className={styles.noTasks}>Brak widocznych zadań w tym cyklu.</p>
              ) : (
                <div
                  role="grid"
                  tabIndex={0}
                  aria-label={`Zadania cyklu ${current.name}`}
                  aria-activedescendant={rows[rowIndex] ? taskOptionId(rows[rowIndex]!.id) : undefined}
                  className={styles.taskList}
                  onKeyDown={listboxKeyDown({
                    count: rows.length,
                    selectedIndex: rowIndex,
                    onSelectedIndexChange: setRowIndex,
                  })}
                >
                  {rows.map((task, index) => (
                    <TaskRow
                      key={task.id}
                      task={task}
                      selected={index === rowIndex}
                      isRenaming={false}
                      onCommitRename={() => undefined}
                      onCancelRename={() => undefined}
                      onSelect={() => setRowIndex(index)}
                      projectName={projectName(task.projectId)}
                      style={{}}
                    />
                  ))}
                </div>
              )}
            </>
          )}
        </>
      )}

      {form !== null ? (
        <CycleFormDialog
          open
          mode={form.mode}
          initial={formInitial}
          onClose={() => setForm(null)}
          onSubmit={submitForm}
        />
      ) : null}

      {closing && current !== null ? (
        <CloseCycleDialog
          open
          cycle={current}
          tasks={rows}
          nextCycleName={nextPlanned?.name ?? null}
          onClose={() => setClosing(false)}
          onConfirm={onCloseConfirm}
          onCreateCycle={() => {
            setClosing(false);
            setForm({ mode: "create" });
          }}
        />
      ) : null}
    </div>
  );
}

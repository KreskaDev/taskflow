"use client";

import { useEffect, useId, useState } from "react";

import { Button } from "@/components/ui/Button";
import { Dialog, DialogActions, DialogChoices, DialogTitle } from "@/components/ui/Dialog";
import type { components } from "@/lib/api/generated/schema";
import styles from "./CloseCycleDialog.module.css";

type TaskResponse = components["schemas"]["TaskResponse"];
type CycleResponse = components["schemas"]["CycleResponse"];
type RolloverChoice = "next" | "backlog" | "keep";

export interface CloseCycleConfirm {
  /** Absent when the cycle has no incomplete tasks — a pure close. */
  rollover?: RolloverChoice;
  /** Only the rows DIVERGING from the bulk choice; the bulk default covers the rest (D4). */
  overrides?: { taskId: string; choice: RolloverChoice }[];
}

interface CloseCycleDialogProps {
  open: boolean;
  /** The active cycle being closed. */
  cycle: CycleResponse;
  /** The cycle's caller-visible rows (`GET /api/cycles/{id}/tasks`); the dialog keeps the incomplete ones. */
  tasks: TaskResponse[];
  /** The next planned cycle's name (D5 order), or null — the AS-06 client guard input. */
  nextCycleName: string | null;
  onClose: () => void;
  /** Commits close + rollover atomically (ONE transactional PATCH). */
  onConfirm: (choice: CloseCycleConfirm) => void;
  /** The AS-06 prompt's „Nowy cykl" action. */
  onCreateCycle: () => void;
}

const CHOICE_LABELS: Record<RolloverChoice, string> = {
  next: "Przenieś do następnego cyklu",
  backlog: "Przenieś do backlogu",
  keep: "Zostaw w zamkniętym cyklu („przeniesione”)",
};

/**
 * The close review (slice 011, T019 — US-05.AS-04/05/06, D4): closing is EXCLUSIVELY manual and
 * this review IS the close flow (Clarifications). It lists the caller-visible INCOMPLETE tasks,
 * takes one bulk three-way rollover choice (+ optional per-task overrides via „obsłuż
 * pojedynczo"), notes that the bulk choice also covers other users' tasks, and commits close +
 * rollover as one transaction. Choosing „next" with NO planned cycle renders the AS-06
 * „Najpierw utwórz nowy cykl" prompt client-side (the server's `no_next_cycle` 422 is the
 * backstop) — nothing closes until a new cycle exists or another choice is made.
 */
export function CloseCycleDialog({
  open,
  cycle,
  tasks,
  nextCycleName,
  onClose,
  onConfirm,
  onCreateCycle,
}: CloseCycleDialogProps) {
  const titleId = useId();
  const [bulk, setBulk] = useState<RolloverChoice>("next");
  const [perTask, setPerTask] = useState(false);
  const [overrides, setOverrides] = useState<Record<string, RolloverChoice>>({});
  const [noNextPrompt, setNoNextPrompt] = useState(false);

  // Reset the review state each time the dialog (re)opens.
  useEffect(() => {
    if (!open) return;
    setBulk("next");
    setPerTask(false);
    setOverrides({});
    setNoNextPrompt(false);
  }, [open]);

  if (!open) return null;

  const incomplete = tasks.filter((t) => t.status !== "done" && t.status !== "cancelled");

  const confirm = (): void => {
    if (incomplete.length === 0) {
      onConfirm({ rollover: undefined, overrides: undefined });
      return;
    }
    const overrideList = incomplete
      .filter((t) => overrides[t.id] !== undefined && overrides[t.id] !== bulk)
      .map((t) => ({ taskId: t.id, choice: overrides[t.id]! }));
    // AS-06 client guard: any effective „next" (bulk or override) needs a planned cycle.
    const needsNext =
      incomplete.some((t) => (overrides[t.id] ?? bulk) === "next");
    if (needsNext && nextCycleName === null) {
      setNoNextPrompt(true);
      return;
    }
    onConfirm({ rollover: bulk, overrides: overrideList.length > 0 ? overrideList : undefined });
  };

  return (
    <Dialog open={open} onClose={onClose} titleId={titleId}>
      <DialogTitle id={titleId}>Zamknij cykl „{cycle.name}”</DialogTitle>

      {incomplete.length === 0 ? (
        <p className={styles.pureClose}>
          Wszystkie zadania w tym cyklu są zakończone — cykl zostanie zamknięty.
        </p>
      ) : (
        <>
          <DialogChoices legend={`Co zrobić z niedokończonymi zadaniami (${incomplete.length})?`}>
            {(Object.keys(CHOICE_LABELS) as RolloverChoice[]).map((choice) => (
              <label key={choice} className={styles.choice}>
                <input
                  type="radio"
                  name="close-cycle-rollover"
                  value={choice}
                  checked={bulk === choice}
                  onChange={() => setBulk(choice)}
                />
                <span>
                  {choice === "next"
                    ? `Przenieś wszystkie do następnego cyklu${nextCycleName ? ` („${nextCycleName}”)` : ""}`
                    : choice === "backlog"
                      ? "Przenieś wszystkie do backlogu"
                      : "Zostaw w zamkniętym cyklu (oznacz „przeniesione”)"}
                </span>
              </label>
            ))}
          </DialogChoices>

          {perTask ? (
            <ul className={styles.taskList}>
              {incomplete.map((task) => (
                <li key={task.id} className={styles.taskRow}>
                  <span className={styles.taskTitle}>{task.title}</span>
                  <select
                    className={styles.overrideSelect}
                    aria-label={task.title}
                    value={overrides[task.id] ?? bulk}
                    onChange={(e) =>
                      setOverrides((old) => ({ ...old, [task.id]: e.target.value as RolloverChoice }))
                    }
                  >
                    {(Object.keys(CHOICE_LABELS) as RolloverChoice[]).map((choice) => (
                      <option key={choice} value={choice}>
                        {CHOICE_LABELS[choice]}
                      </option>
                    ))}
                  </select>
                </li>
              ))}
            </ul>
          ) : (
            <>
              <ul className={styles.taskList}>
                {incomplete.map((task) => (
                  <li key={task.id} className={styles.taskRow}>
                    <span className={styles.taskTitle}>{task.title}</span>
                  </li>
                ))}
              </ul>
              <Button variant="secondary" onClick={() => setPerTask(true)}>
                Obsłuż pojedynczo
              </Button>
            </>
          )}

          <p className={styles.footerNote}>
            Wybór zbiorczy dotyczy także zadań innych użytkowników w tym cyklu.
          </p>
        </>
      )}

      {noNextPrompt ? (
        <div className={styles.noNextPrompt} role="alert">
          <p className={styles.noNextText}>Najpierw utwórz nowy cykl, aby przenieść do niego zadania.</p>
          <Button variant="primary" onClick={onCreateCycle}>
            Nowy cykl
          </Button>
        </div>
      ) : null}

      <DialogActions>
        <Button variant="secondary" onClick={onClose}>
          Anuluj
        </Button>
        <Button variant="danger" onClick={confirm}>
          Zamknij cykl
        </Button>
      </DialogActions>
    </Dialog>
  );
}

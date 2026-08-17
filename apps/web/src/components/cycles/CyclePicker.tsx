"use client";

import { Button } from "@/components/ui/Button";
import { Dialog } from "@/components/ui/Dialog";
import { useCycles } from "@/hooks/useCycles";
import { buildCyclePickerOptions } from "@/lib/cycles";
import styles from "./CyclePicker.module.css";

const TITLE_ID = "cycle-picker-title";

interface CyclePickerProps {
  open: boolean;
  /** The task's current assignment; null = the cycle backlog. */
  current: string | null;
  onClose: () => void;
  /** Commits the assignment (null = „Bez cyklu" clears it) — the optimistic setTaskCycle. */
  onSelect: (cycleId: string | null) => void;
}

/**
 * Cycle picker (slice 011 — the menu's „Cykl…" surface, US-05.AS-01/02; the PriorityPicker
 * pattern). A small modal Dialog (full FR-101 focus contract: initial focus inside, focus trap,
 * Esc dismiss, focus returns to the invoker) listing ALL cycles — active, planned AND closed
 * (Clarifications) — in D5 order with the Polish status suffix, plus „Bez cyklu" to clear; the
 * current assignment is marked. Selection commits optimistically and closes.
 */
export function CyclePicker({ open, current, onClose, onSelect }: CyclePickerProps) {
  const { data: cycles } = useCycles();
  if (!open) return null;

  const options = buildCyclePickerOptions(cycles ?? [], current);

  return (
    <Dialog open={open} onClose={onClose} titleId={TITLE_ID}>
      <h2 id={TITLE_ID} className={styles.title}>
        Cykl
      </h2>
      <ul className={styles.list}>
        {options.map((option) => (
          <li key={option.id ?? "none"}>
            <Button
              variant="secondary"
              className={styles.option}
              aria-pressed={option.checked}
              onClick={() => {
                onSelect(option.id);
                onClose();
              }}
            >
              {option.label}
            </Button>
          </li>
        ))}
      </ul>
    </Dialog>
  );
}

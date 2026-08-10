"use client";

import { Dialog } from "@/components/ui/Dialog";
import { Button } from "@/components/ui/Button";
import type { Priority } from "@/lib/validation/task";
import styles from "./PriorityPicker.module.css";

const TITLE_ID = "priority-picker-title";

const OPTIONS: { value: Priority; label: string }[] = [
  { value: "P0", label: "P0 — najwyższy" },
  { value: "P1", label: "P1" },
  { value: "P2", label: "P2" },
  { value: "P3", label: "P3" },
  { value: null, label: "Bez priorytetu" },
];

interface PriorityPickerProps {
  open: boolean;
  current: string | null | undefined;
  onClose: () => void;
  onSelect: (priority: Priority) => void;
}

/**
 * Priority picker (slice 019 — the menu's "Priorytet…" surface, replacing the removed
 * `1`–`4` bindings; FR-103/FR-108). A small modal Dialog (full FR-101 focus contract)
 * listing the closed P0–P3 set + clear; selection commits optimistically and closes.
 */
export function PriorityPicker({ open, current, onClose, onSelect }: PriorityPickerProps) {
  if (!open) return null;
  return (
    <Dialog open={open} onClose={onClose} titleId={TITLE_ID}>
      <h2 id={TITLE_ID} className={styles.title}>
        Priorytet
      </h2>
      <ul className={styles.list}>
        {OPTIONS.map((option) => (
          <li key={option.value ?? "none"}>
            <Button
              variant="secondary"
              className={styles.option}
              aria-pressed={(current ?? null) === option.value}
              onClick={() => {
                onSelect(option.value);
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

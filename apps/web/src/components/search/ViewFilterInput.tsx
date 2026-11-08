"use client";

import { Filter, X } from "lucide-react";

import { IconButton } from "@/components/ui/IconButton";
import styles from "./ViewFilterInput.module.css";

export const VIEW_FILTER_LABEL = "Filtruj zadania w tym widoku";
export const VIEW_FILTER_CLEAR = "Wyczyść filtr";
export const VIEW_FILTER_EMPTY_HINT = "Brak zadań pasujących do filtru";

interface ViewFilterInputProps {
  value: string;
  onChange: (value: string) => void;
  /** Distinguishes several hosts on one page for the label/id pairing. */
  id?: string;
}

/**
 * The visible per-view filter (slice 013, US-08.AS-08 reinterpreted — contracts/ui-palette.md §View filter):
 * a labelled text input that narrows the hosting view's loaded rows (see `lib/filter.ts`) plus a clear button
 * once something is typed. Ordinary text entry — no `/` binding exists (FR-027 deferred, OOS-20).
 */
export function ViewFilterInput({ value, onChange, id = "view-filter" }: ViewFilterInputProps) {
  return (
    <div className={styles.filter}>
      <Filter size={14} strokeWidth={1.75} aria-hidden="true" />
      <input
        id={id}
        className={styles.input}
        type="text"
        aria-label={VIEW_FILTER_LABEL}
        placeholder="Filtruj…"
        autoComplete="off"
        value={value}
        onChange={(event) => onChange(event.target.value)}
      />
      {value.length > 0 ? (
        <IconButton aria-label={VIEW_FILTER_CLEAR} onClick={() => onChange("")}>
          <X size={14} strokeWidth={1.75} aria-hidden="true" />
        </IconButton>
      ) : null}
    </div>
  );
}

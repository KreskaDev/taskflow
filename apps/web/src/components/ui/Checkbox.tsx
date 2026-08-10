"use client";

import { useEffect, useRef, type InputHTMLAttributes } from "react";
import styles from "./Checkbox.module.css";

interface CheckboxProps extends InputHTMLAttributes<HTMLInputElement> {
  /** Tri-state support (e.g. group headers) — reflected on the element property. */
  indeterminate?: boolean;
}

/**
 * Catalog checkbox (T010): a REAL `<input type="checkbox">` (native keyboard toggle,
 * Space semantics) inside a 32px padded hit-area label. The visible outline sits on
 * `--color-fg-disabled` (~4.2:1) — NEVER `border-strong` (~2:1, breaks WCAG 1.4.11;
 * design-brief trap).
 */
export function Checkbox({ indeterminate = false, className, ...rest }: CheckboxProps) {
  const inputRef = useRef<HTMLInputElement>(null);

  useEffect(() => {
    if (inputRef.current) {
      inputRef.current.indeterminate = indeterminate;
    }
  }, [indeterminate]);

  return (
    <label className={[styles.hitArea, className].filter(Boolean).join(" ")}>
      <input ref={inputRef} type="checkbox" className={styles.input} {...rest} />
      <span className={styles.box} aria-hidden="true" />
    </label>
  );
}

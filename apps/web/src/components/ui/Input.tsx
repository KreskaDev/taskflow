"use client";

import { forwardRef, useId, type InputHTMLAttributes } from "react";
import styles from "./Input.module.css";

interface InputProps extends InputHTMLAttributes<HTMLInputElement> {
  /** FR-006/FR-049 error presentation: visible message + aria-invalid + describedby. */
  error?: string;
}

/**
 * Catalog text input (T009). Full state set (hover/focus/disabled/error) on semantic
 * tokens; the error message renders in place below the field and is announced via
 * `aria-describedby` (FR-006 pattern unchanged).
 */
export const Input = forwardRef<HTMLInputElement, InputProps>(function Input(
  { error, className, ...rest },
  ref,
) {
  const errorId = useId();
  const classes = [styles.input, error ? styles.invalid : null, className]
    .filter(Boolean)
    .join(" ");
  return (
    <>
      <input
        ref={ref}
        className={classes}
        aria-invalid={error ? true : undefined}
        aria-describedby={error ? errorId : undefined}
        {...rest}
      />
      {error ? (
        <p id={errorId} className={styles.error}>
          {error}
        </p>
      ) : null}
    </>
  );
});

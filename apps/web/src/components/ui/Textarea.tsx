"use client";

import { forwardRef, useId, type TextareaHTMLAttributes } from "react";
import styles from "./Textarea.module.css";

interface TextareaProps extends TextareaHTMLAttributes<HTMLTextAreaElement> {
  /** FR-006/FR-049 error presentation: visible message + aria-invalid + describedby. */
  error?: string;
}

/**
 * Catalog multiline input (T009). Same state/error contract as {@link Input}.
 */
export const Textarea = forwardRef<HTMLTextAreaElement, TextareaProps>(function Textarea(
  { error, className, ...rest },
  ref,
) {
  const errorId = useId();
  const classes = [styles.textarea, error ? styles.invalid : null, className]
    .filter(Boolean)
    .join(" ");
  return (
    <>
      <textarea
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

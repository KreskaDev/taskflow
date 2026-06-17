import type { ButtonHTMLAttributes } from "react";

export type ButtonVariant = "primary" | "secondary" | "danger";

interface ButtonProps extends ButtonHTMLAttributes<HTMLButtonElement> {
  variant?: ButtonVariant;
}

/**
 * Accessible button primitive. Defaults `type="button"` (so it never submits a form
 * unexpectedly), inherits the global visible-focus indicator (FR-042), and meets the
 * ≥4.5:1 contrast baseline (FR-044) via the variant classes.
 */
export function Button({ variant = "primary", className, type, ...rest }: ButtonProps) {
  const classes = ["tf-button", `tf-button--${variant}`, className].filter(Boolean).join(" ");
  return <button type={type ?? "button"} className={classes} {...rest} />;
}

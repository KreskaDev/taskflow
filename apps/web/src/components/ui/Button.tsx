import type { ButtonHTMLAttributes } from "react";
import styles from "./Button.module.css";

export type ButtonVariant = "primary" | "secondary" | "danger";

interface ButtonProps extends ButtonHTMLAttributes<HTMLButtonElement> {
  variant?: ButtonVariant;
}

/**
 * Catalog button (T007). Defaults `type="button"` (never submits a form unexpectedly),
 * inherits the global visible-focus indicator (FR-042). Primary sits on `accent-strong`
 * (hover DARKER via `accent-strong-hover` — never `accent-hover`, the design-brief trap);
 * destructive on `danger-strong`; semantic tokens only (FR-104).
 */
export function Button({ variant = "primary", className, type, ...rest }: ButtonProps) {
  const classes = [styles.button, styles[variant], className].filter(Boolean).join(" ");
  return <button type={type ?? "button"} className={classes} {...rest} />;
}

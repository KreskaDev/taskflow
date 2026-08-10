import type { ButtonHTMLAttributes } from "react";
import styles from "./IconButton.module.css";

interface IconButtonProps extends ButtonHTMLAttributes<HTMLButtonElement> {
  /** Icon-only controls MUST carry an accessible name (FR-043, design-brief). */
  "aria-label": string;
}

/**
 * Icon-only button with a ≥32px hit area around a 14–16px Lucide glyph (FR-105,
 * design-brief). The `aria-label` prop is REQUIRED at the type level — an icon-only
 * control without a name cannot compile.
 */
export function IconButton({ className, type, ...rest }: IconButtonProps) {
  const classes = [styles.iconButton, className].filter(Boolean).join(" ");
  return <button type={type ?? "button"} className={classes} {...rest} />;
}

import type { ReactNode } from "react";
import styles from "./auth.module.css";

/** Centered layout for unauthenticated surfaces (sign-in) — slice 019 (T059). */
export default function AuthLayout({ children }: { children: ReactNode }) {
  return <main className={styles.layout}>{children}</main>;
}

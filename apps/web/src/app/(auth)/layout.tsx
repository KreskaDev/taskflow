import type { ReactNode } from "react";

/** Centered layout for unauthenticated surfaces (sign-in). */
export default function AuthLayout({ children }: { children: ReactNode }) {
  return <main className="tf-auth-layout">{children}</main>;
}

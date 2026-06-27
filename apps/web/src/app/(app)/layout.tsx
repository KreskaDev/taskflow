import Link from "next/link";
import type { ReactNode } from "react";

import { Sidebar } from "@/components/layout/Sidebar";

/**
 * Authenticated app shell (T044, slice-004 T028). A minimal header (no onboarding wizard, tooltips,
 * or modal interruptions — Constitution IV) with navigation and a keyboard-operable sign-out, plus
 * the left {@link Sidebar} (Inbox + the one-level project tree + an Archived disclosure, R8/R16).
 * Sign-out is a plain form POST to the BFF route so it works without client JavaScript
 * (Constitution I, FR-054).
 */
export default function AppLayout({ children }: { children: ReactNode }) {
  return (
    <div className="tf-app">
      <header className="tf-app__header">
        <Link className="tf-app__brand" href="/">
          TaskFlow
        </Link>
        <nav className="tf-app__nav" aria-label="Primary">
          <Link href="/">Workspace</Link>
          <Link href="/settings">Settings</Link>
          <form method="post" action="/api/auth/signout">
            <button type="submit" className="tf-button tf-button--secondary">
              Sign out
            </button>
          </form>
        </nav>
      </header>
      <div className="tf-app__body">
        <Sidebar />
        <main className="tf-app__main">{children}</main>
      </div>
    </div>
  );
}

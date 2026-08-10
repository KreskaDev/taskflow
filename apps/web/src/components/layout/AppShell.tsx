"use client";

import { Suspense, useState, type ReactNode } from "react";

import { Sidebar } from "@/components/layout/Sidebar";
import { Topbar } from "@/components/layout/Topbar";
import { TaskDrawerHost } from "@/components/tasks/TaskDrawerHost";
import styles from "./AppShell.module.css";

/**
 * The authenticated app shell (slice 019, T037): a grid of Topbar + collapsible Sidebar +
 * main content + the drawer host slot (the non-modal task drawer portals here, T052).
 * Sidebar collapse is CLIENT state (not persisted this slice — data-model.md);
 * `aria-expanded` on the toggle reflects it (UIT-023). 768–1024px: the grid keeps a
 * single scrollable main column and the drawer overlays instead of docking (S5.5).
 */
export function AppShell({ children }: { children: ReactNode }) {
  const [sidebarCollapsed, setSidebarCollapsed] = useState(false);

  return (
    <div className={[styles.shell, sidebarCollapsed ? styles.collapsed : null].filter(Boolean).join(" ")}>
      <Topbar
        sidebarCollapsed={sidebarCollapsed}
        onToggleSidebar={() => setSidebarCollapsed((v) => !v)}
      />
      <div className={styles.sidebar} hidden={sidebarCollapsed}>
        <Sidebar />
      </div>
      <main className={styles.main}>{children}</main>
      {/* Drawer host (T052): the deep-linkable task drawer renders at the shell level so
          it works on every listing route. Suspense wraps useSearchParams (Next 15). */}
      <Suspense fallback={null}>
        <TaskDrawerHost />
      </Suspense>
    </div>
  );
}

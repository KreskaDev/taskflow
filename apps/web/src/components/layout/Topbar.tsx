"use client";

import { useState } from "react";
import Link from "next/link";
import { PanelLeft, Plus, Search } from "lucide-react";

import { Avatar } from "@/components/ui/Avatar";
import { Button } from "@/components/ui/Button";
import { IconButton } from "@/components/ui/IconButton";
import { GlobalCaptureDialog } from "@/components/tasks/TaskCapture";
import { useSession } from "@/hooks/useSession";
import styles from "./Topbar.module.css";

interface TopbarProps {
  sidebarCollapsed: boolean;
  onToggleSidebar: () => void;
}

/**
 * The app top bar (slice 019, T036 — FR-107, US-18.AS-01): sidebar collapse toggle, the
 * persistent global "+ Nowy task" (context per the 2026-08-09 clarification, resolved by
 * {@link GlobalCaptureDialog}), the SEARCH PLACEHOLDER, and the session identity.
 *
 * The search field is a DISABLED placeholder (spec Provenance FR-032/034 carve-out):
 * rendered per the mockup, excluded from the tab order, and announced as unavailable —
 * function arrives with slice 013.
 */
export function Topbar({ sidebarCollapsed, onToggleSidebar }: TopbarProps) {
  const [captureOpen, setCaptureOpen] = useState(false);
  const { data: session } = useSession();
  const user = session?.authenticated ? session.user : undefined;

  return (
    <header className={styles.topbar}>
      <IconButton
        aria-label={sidebarCollapsed ? "Rozwiń panel boczny" : "Zwiń panel boczny"}
        aria-expanded={!sidebarCollapsed}
        onClick={onToggleSidebar}
      >
        <PanelLeft size={16} strokeWidth={1.75} aria-hidden="true" />
      </IconButton>

      <Link className={styles.brand} href="/">
        TaskFlow
      </Link>

      <div
        className={styles.search}
        role="searchbox"
        aria-label="Szukaj (wkrótce)"
        aria-disabled="true"
      >
        <Search size={14} strokeWidth={1.75} aria-hidden="true" />
        <input
          type="text"
          placeholder="Szukaj…"
          disabled
          tabIndex={-1}
          aria-hidden="true"
        />
      </div>

      <Button className={styles.newTask} onClick={() => setCaptureOpen(true)}>
        <Plus size={15} strokeWidth={2} aria-hidden="true" />
        Nowy task
      </Button>

      {user ? (
        <Link href="/settings" className={styles.identity} aria-label={`Konto: ${user.displayName}`}>
          <Avatar userId={user.id} displayName={user.displayName} avatarUrl={user.avatarUrl} size="lg" />
        </Link>
      ) : null}

      <GlobalCaptureDialog open={captureOpen} onClose={() => setCaptureOpen(false)} />
    </header>
  );
}

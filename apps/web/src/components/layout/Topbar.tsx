"use client";

import { useState } from "react";
import Link from "next/link";
import { useRouter } from "next/navigation";
import { useQueryClient } from "@tanstack/react-query";
import { PanelLeft, Plus, Search } from "lucide-react";

import { Avatar } from "@/components/ui/Avatar";
import { Button } from "@/components/ui/Button";
import { IconButton } from "@/components/ui/IconButton";
import { ProjectForm } from "@/components/projects/ProjectForm";
import { CommandPalette } from "@/components/search/CommandPalette";
import { GlobalCaptureDialog } from "@/components/tasks/TaskCapture";
import { invalidateSearch } from "@/hooks/useSearch";
import type { PaletteAction } from "@/lib/palette";
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
  const [paletteOpen, setPaletteOpen] = useState(false);
  const [projectFormOpen, setProjectFormOpen] = useState(false);
  const { data: session } = useSession();
  const user = session?.authenticated ? session.user : undefined;
  const router = useRouter();
  const queryClient = useQueryClient();

  // slice 013 (D8/D10): the visible opener; every open drops cached results so revoked items never resurface.
  const openPalette = () => {
    void invalidateSearch(queryClient);
    setPaletteOpen(true);
  };

  // Dialog-opening actions run AFTER the palette has closed so the FR-101 focus hand-over is clean (D12).
  const runAction = (action: PaletteAction) => {
    switch (action.kind) {
      case "dialog:new-task":
        setCaptureOpen(true);
        return;
      case "dialog:new-project":
        setProjectFormOpen(true);
        return;
      case "route":
        if (action.href) router.push(action.href);
        return;
    }
  };

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

      {/* slice 013 (US-04.AS-01 reinterpreted, D8): the palette opener — a real button, in the tab order, no Ctrl+K. */}
      <button
        type="button"
        className={styles.search}
        aria-label="Szukaj"
        aria-haspopup="dialog"
        aria-expanded={paletteOpen}
        onClick={openPalette}
      >
        <Search size={14} strokeWidth={1.75} aria-hidden="true" />
        <span className={styles.searchText}>Szukaj…</span>
      </button>

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
      <ProjectForm open={projectFormOpen} onClose={() => setProjectFormOpen(false)} mode="create" />
      <CommandPalette
        open={paletteOpen}
        onClose={() => setPaletteOpen(false)}
        onNavigate={(href) => router.push(href)}
        onAction={runAction}
      />
    </header>
  );
}

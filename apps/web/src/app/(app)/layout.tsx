import type { ReactNode } from "react";

import { AppShell } from "@/components/layout/AppShell";

/**
 * Authenticated app shell route layout (rebuilt in slice 019, T037): Topbar (global
 * "+ Nowy task" + disabled search placeholder) + collapsible Sidebar + main + the drawer
 * host, all in {@link AppShell} (client — collapse/capture state). No onboarding wizards,
 * tooltips, or modal interruptions (Constitution IV).
 */
export default function AppLayout({ children }: { children: ReactNode }) {
  return <AppShell>{children}</AppShell>;
}

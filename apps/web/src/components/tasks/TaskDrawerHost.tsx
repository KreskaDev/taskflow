"use client";

import { usePathname, useRouter, useSearchParams } from "next/navigation";
import { useIsFetching } from "@tanstack/react-query";

import { TaskDrawer, TASK_DRAWER_TITLE_ID } from "@/components/tasks/TaskDrawer";
import { Button } from "@/components/ui/Button";
import { Drawer } from "@/components/ui/Drawer";
import { Skeleton } from "@/components/ui/Skeleton";
import { useTaskLookup } from "@/hooks/useTaskLookup";

/**
 * The URL-driven drawer host (slice 019, T052 — D8, S4.3): `?task=<taskId>` on any of the
 * five listing routes opens the non-modal drawer on that task; `router.push` keeps deep
 * links and back/forward working while list state is preserved. An invalid/inaccessible
 * id renders the drawer's ERROR state with a recovery action (FR-049).
 */
export function TaskDrawerHost() {
  const router = useRouter();
  const pathname = usePathname();
  const searchParams = useSearchParams();
  const taskId = searchParams.get("task");
  const task = useTaskLookup(taskId);
  const fetching = useIsFetching();

  if (taskId === null) return null;

  const close = () => {
    const next = new URLSearchParams(searchParams.toString());
    next.delete("task");
    const query = next.toString();
    router.push(query.length > 0 ? `${pathname}?${query}` : pathname);
  };

  return (
    <Drawer open onClose={close} titleId={TASK_DRAWER_TITLE_ID}>
      {task ? (
        <TaskDrawer task={task} onClose={close} />
      ) : fetching > 0 ? (
        // The route's listing is still loading — a genuine network-bound wait (S5.2).
        <Skeleton variant="block" />
      ) : (
        <div role="alert">
          <h2 id={TASK_DRAWER_TITLE_ID}>Nie znaleziono zadania</h2>
          <p>To zadanie nie istnieje albo nie masz do niego dostępu.</p>
          <Button variant="secondary" onClick={close}>
            Zamknij panel
          </Button>
        </div>
      )}
    </Drawer>
  );
}

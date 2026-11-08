import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { cleanup, fireEvent, render, screen } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";

const { push } = vi.hoisted(() => ({ push: vi.fn() }));

vi.mock("next/navigation", () => ({
  useRouter: () => ({ push, replace: vi.fn() }),
  usePathname: () => "/",
  useSearchParams: () => new URLSearchParams(),
}));
vi.mock("@/hooks/useSession", () => ({
  useSession: () => ({ data: { authenticated: true, user: { id: "u1", displayName: "Test", avatarUrl: null } } }),
}));
vi.mock("@/hooks/useProjects", () => ({
  useProjects: () => ({ data: [] }),
}));
vi.mock("@/hooks/useProjectMutations", () => ({
  useProjectMutations: () => ({ unarchiveProject: vi.fn(), createProject: vi.fn() }),
}));
vi.mock("@/hooks/useSearch", () => ({
  useSearch: () => ({ data: undefined, isError: false, isFetching: false, refetch: vi.fn() }),
  invalidateSearch: vi.fn(async () => {}),
}));
vi.mock("@/components/tasks/TaskCapture", () => ({
  GlobalCaptureDialog: ({ open }: { open: boolean }) => (open ? <div role="dialog" aria-label="Nowy task (global)" /> : null),
}));
vi.mock("@/components/projects/ProjectForm", () => ({
  ProjectForm: ({ open }: { open: boolean }) => (open ? <div role="dialog" aria-label="Nowy projekt (form)" /> : null),
}));

import { Topbar } from "@/components/layout/Topbar";

/**
 * The topbar opener (slice 013, T012 — contracts/ui-palette.md §Opener): a real button in the tab order
 * (no `Ctrl+K`), opens the palette, Esc returns focus to it; actions execute after the palette closes.
 * [INV-188] [INV-189] [INV-193]
 */

afterEach(() => {
  cleanup();
  vi.clearAllMocks();
});

function renderTopbar() {
  render(
    <QueryClientProvider client={new QueryClient()}>
      <Topbar sidebarCollapsed={false} onToggleSidebar={() => {}} />
    </QueryClientProvider>,
  );
}

describe("Topbar search opener [INV-188] [INV-189]", () => {
  it("renders „Szukaj” as a focusable button with dialog popup semantics and opens the palette", () => {
    renderTopbar();
    const opener = screen.getByRole("button", { name: "Szukaj" });
    expect(opener.getAttribute("aria-haspopup")).toBe("dialog");
    expect(opener.tabIndex).toBe(0);
    expect(screen.queryByRole("searchbox")).toBeNull(); // the disabled placeholder is gone
    fireEvent.click(opener);
    expect(screen.getByRole("dialog", { name: "Szukaj i wykonaj" })).toBeTruthy();
  });

  it("Ctrl+K and / do nothing (FR-027 stays deferred)", () => {
    renderTopbar();
    fireEvent.keyDown(document.body, { key: "k", ctrlKey: true });
    fireEvent.keyDown(document.body, { key: "/" });
    expect(screen.queryByRole("dialog")).toBeNull();
  });

  it("Esc closes the palette and focus returns to the opener", () => {
    renderTopbar();
    const opener = screen.getByRole("button", { name: "Szukaj" });
    opener.focus();
    fireEvent.click(opener);
    fireEvent.keyDown(screen.getByRole("combobox"), { key: "Escape" });
    expect(screen.queryByRole("dialog", { name: "Szukaj i wykonaj" })).toBeNull();
    expect(document.activeElement).toBe(opener);
  });

  it("the „Nowy projekt” action closes the palette and opens the project form; a route action pushes [INV-193]", () => {
    renderTopbar();
    fireEvent.click(screen.getByRole("button", { name: "Szukaj" }));
    fireEvent.click(screen.getByRole("option", { name: "Nowy projekt" }));
    expect(screen.queryByRole("dialog", { name: "Szukaj i wykonaj" })).toBeNull();
    expect(screen.getByRole("dialog", { name: "Nowy projekt (form)" })).toBeTruthy();

    fireEvent.click(screen.getByRole("button", { name: "Szukaj" }));
    fireEvent.click(screen.getByRole("option", { name: "Przejdź: Dziś" }));
    expect(push).toHaveBeenCalledWith("/today");
  });
});

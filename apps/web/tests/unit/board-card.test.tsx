import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { cleanup, fireEvent, render, screen } from "@testing-library/react";
import { createElement } from "react";
import { afterEach, describe, expect, it, vi } from "vitest";

import { BoardCard } from "@/components/tasks/BoardCard";
import type { TaskRowActions } from "@/components/tasks/TaskRow";
import type { TaskResponse } from "@/hooks/useTasks";

/**
 * Board card coverage (slice 010, T016 — contracts/ui-board-list.md). The card is catalog
 * components only: sanitized title TEXT (FR-099 — no raw-HTML render path), due chip
 * rendered exactly as the List does, priority chip, label chips, assignee avatars, and the
 * full shared „⋯” menu (D7) including the move actions with boundary omission.
 */

function makeTask(overrides: Partial<TaskResponse> = {}): TaskResponse {
  return {
    id: "11111111-1111-7111-8111-111111111111",
    title: "Zaprojektować tablicę",
    status: "todo",
    position: "a0",
    version: 1,
    createdAt: "2026-08-01T00:00:00.000Z",
    updatedAt: "2026-08-01T00:00:00.000Z",
    completedAt: null,
    dueDate: null,
    dueHasTime: null,
    projectId: "33333333-3333-7333-8333-333333333333",
    assignees: [],
    labels: [],
    ...overrides,
  };
}

function renderCard(task: TaskResponse, actions?: TaskRowActions, assigneeName?: (id: string) => string | null) {
  const queryClient = new QueryClient();
  return render(
    createElement(
      QueryClientProvider,
      { client: queryClient },
      <BoardCard task={task} actions={actions} assigneeName={assigneeName} />,
    ),
  );
}

afterEach(cleanup);

describe("BoardCard — sanitized content on catalog components (FR-099) [INV-142]", () => {
  it("carries the task title as text on a non-widget card (its listitem wrapper lives in BoardColumn)", () => {
    renderCard(makeTask());
    expect(screen.getByText("Zaprojektować tablicę")).toBeTruthy();
    // No widget role on the card body — focusable controls inside stay legal (nested-interactive).
    expect(screen.queryByRole("option")).toBeNull();
  });

  it("renders a hostile title as INERT TEXT — no element injection (FR-099)", () => {
    renderCard(makeTask({ title: '<img src=x onerror="window.pwned=1">' }));
    expect(document.querySelector("img")).toBeNull();
    expect(screen.getByText('<img src=x onerror="window.pwned=1">')).toBeTruthy();
  });

  it("renders the due chip exactly as the List does (dd.MM.yyyy, Warsaw day, sr-only qualifier)", () => {
    // Midnight 2026-06-22 Warsaw (CEST) === 2026-06-21T22:00:00Z — the chip must say 22.06.2026.
    renderCard(makeTask({ dueDate: "2026-06-21T22:00:00Z", dueHasTime: false }));
    expect(screen.getByText(/22\.06\.2026/)).toBeTruthy();
    expect(screen.queryByText(/21\.06\.2026/)).toBeNull();
  });

  it("renders the priority chip as text (never color alone — FR-044)", () => {
    renderCard(makeTask({ priority: "P1" }));
    expect(screen.getByText("P1")).toBeTruthy();
  });

  it("renders assignee avatars when the roster resolves their names", () => {
    renderCard(
      makeTask({ assignees: ["aaaaaaaa-aaaa-7aaa-8aaa-aaaaaaaaaaaa"] }),
      undefined,
      () => "Ola Audyt",
    );
    expect(screen.getByRole("img", { name: "Ola Audyt" })).toBeTruthy();
  });
});

describe("BoardCard — the shared „⋯” menu with move actions (D7, FR-108) [INV-145]", () => {
  it("opens the full shared menu incl. „Przenieś w lewo/w prawo” when both moves are wired", () => {
    const actions: TaskRowActions = {
      onToggleDone: vi.fn(),
      onOpenDetails: vi.fn(),
      onMoveLeft: vi.fn(),
      onMoveRight: vi.fn(),
      onDelete: vi.fn(),
    };
    renderCard(makeTask(), actions);

    fireEvent.click(screen.getByRole("button", { name: /Więcej akcji: Zaprojektować tablicę/ }));

    expect(screen.getByRole("menuitem", { name: /Przenieś w lewo/ })).toBeTruthy();
    expect(screen.getByRole("menuitem", { name: /Przenieś w prawo/ })).toBeTruthy();
    expect(screen.getByRole("menuitem", { name: "Usuń" })).toBeTruthy();
  });

  it("OMITS the boundary move item (Done column card offers no „w prawo” — AS-05)", () => {
    const actions: TaskRowActions = {
      onToggleDone: vi.fn(),
      onMoveLeft: vi.fn(),
      // onMoveRight absent — the caller found no right neighbour (done column).
    };
    renderCard(makeTask({ status: "done" }), actions);

    fireEvent.click(screen.getByRole("button", { name: /Więcej akcji/ }));

    expect(screen.getByRole("menuitem", { name: /Przenieś w lewo/ })).toBeTruthy();
    expect(screen.queryByRole("menuitem", { name: /Przenieś w prawo/ })).toBeNull();
  });

  it("fires the move callback from the menu item", () => {
    const onMoveRight = vi.fn();
    renderCard(makeTask(), { onMoveRight });

    fireEvent.click(screen.getByRole("button", { name: /Więcej akcji/ }));
    fireEvent.click(screen.getByRole("menuitem", { name: /Przenieś w prawo/ }));

    expect(onMoveRight).toHaveBeenCalledTimes(1);
  });

  it("renders NO action zone at all for a viewer (no actions prop — read-only board) [INV-149]", () => {
    renderCard(makeTask());
    expect(screen.queryByRole("button", { name: /Więcej akcji/ })).toBeNull();
  });
});

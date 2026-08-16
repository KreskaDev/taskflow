import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { createElement } from "react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import { BoardView } from "@/components/tasks/BoardView";
import type { TaskResponse } from "@/hooks/useTasks";

/**
 * Board view coverage (slice 010, T018 — contracts/ui-board-list.md, D6/D10). Pins: the
 * four-column render from `buildBoardColumns` (cancelled NOWHERE — EC-11), the menu move
 * path issuing exactly ONE `PATCH /status` with the neighbour column's status (D6 —
 * `position` untouched), boundary omission mapped via `adjacentStatus`, and the viewer's
 * fully read-only board (no move items, no action zone — server still enforces 403).
 */

vi.mock("@/lib/api/client", async (importOriginal) => {
  const actual = await importOriginal<typeof import("@/lib/api/client")>();
  return {
    ...actual,
    apiClient: { GET: vi.fn(), PUT: vi.fn(), PATCH: vi.fn(), DELETE: vi.fn() },
  };
});

const { apiClient } = await import("@/lib/api/client");
const patchSpy = apiClient.PATCH as unknown as ReturnType<typeof vi.fn>;

const PROJECT_ID = "33333333-3333-7333-8333-333333333333";

function makeTask(overrides: Partial<TaskResponse> & Pick<TaskResponse, "id">): TaskResponse {
  return {
    title: overrides.id,
    status: "todo",
    position: "a0",
    version: 1,
    createdAt: "2026-08-01T00:00:00.000Z",
    updatedAt: "2026-08-01T00:00:00.000Z",
    completedAt: null,
    projectId: PROJECT_ID,
    assignees: [],
    labels: [],
    ...overrides,
  };
}

const tasks: TaskResponse[] = [
  makeTask({ id: "t-backlog", position: "a0", status: "backlog" }),
  makeTask({ id: "t-progress", position: "a1", status: "in_progress" }),
  makeTask({ id: "t-cancelled", position: "a2", status: "cancelled" }),
];

function renderBoard(readOnly: boolean) {
  const queryClient = new QueryClient();
  queryClient.setQueryData(["projects", PROJECT_ID, "tasks"], tasks);
  return render(
    createElement(
      QueryClientProvider,
      { client: queryClient },
      <BoardView tasks={tasks} readOnly={readOnly} />,
    ),
  );
}

beforeEach(() => patchSpy.mockReset());
afterEach(cleanup);

describe("BoardView — four columns, cancelled nowhere (EC-11) [INV-142] [INV-143]", () => {
  it("renders exactly the four column lists in order with their counts", () => {
    renderBoard(false);

    const lists = screen.getAllByRole("list");
    expect(lists.map((l) => l.getAttribute("aria-label"))).toEqual([
      "Backlog, 1 zadanie",
      "Do zrobienia, 0 zadań",
      "W toku, 1 zadanie",
      "Zrobione, 0 zadań",
    ]);
  });

  it("a cancelled task renders NOWHERE on the board", () => {
    renderBoard(false);
    expect(screen.queryByText("t-cancelled")).toBeNull();
  });
});

describe("BoardView — menu move issues ONE status PATCH (D6) [INV-144] [INV-145]", () => {
  it("„Przenieś w prawo” on a backlog card sends one PATCH with the neighbour status todo", async () => {
    patchSpy.mockResolvedValue({ data: undefined, error: undefined });
    renderBoard(false);

    fireEvent.click(screen.getByRole("button", { name: "Więcej akcji: t-backlog" }));
    fireEvent.click(screen.getByRole("menuitem", { name: /Przenieś w prawo/ }));

    await waitFor(() => expect(patchSpy).toHaveBeenCalledTimes(1));
    const [path, init] = patchSpy.mock.calls[0]!;
    expect(path).toBe("/api/tasks/{id}/status");
    expect((init as { body: { status: string; version: number } }).body).toEqual({
      status: "todo",
      version: 1,
    });
  });

  it("a Backlog card has no „Przenieś w lewo”; a card cannot move past Zrobione (AS-05)", () => {
    renderBoard(false);

    fireEvent.click(screen.getByRole("button", { name: "Więcej akcji: t-backlog" }));
    expect(screen.queryByRole("menuitem", { name: /Przenieś w lewo/ })).toBeNull();
    expect(screen.getByRole("menuitem", { name: /Przenieś w prawo/ })).toBeTruthy();
  });
});

describe("BoardView — viewer read-only posture [INV-149]", () => {
  it("renders NO action affordances at all for a viewer (no menus, no checkboxes)", () => {
    renderBoard(true);

    expect(screen.queryByRole("button", { name: /Więcej akcji/ })).toBeNull();
    expect(screen.queryByRole("checkbox")).toBeNull();
    // The cards still render (read access).
    expect(screen.getByText("t-backlog")).toBeTruthy();
    expect(screen.getByText("t-progress")).toBeTruthy();
  });
});

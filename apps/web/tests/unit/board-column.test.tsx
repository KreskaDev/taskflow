import { DndContext } from "@dnd-kit/core";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { cleanup, fireEvent, render, screen } from "@testing-library/react";
import { createElement, type ReactNode } from "react";
import { afterEach, describe, expect, it } from "vitest";

import { BoardColumn } from "@/components/tasks/BoardColumn";
import { taskOptionId } from "@/components/tasks/TaskRow";
import type { TaskResponse } from "@/hooks/useTasks";

/**
 * Board column coverage (slice 010, T017 — research D10, FR-044/FR-110). The column is a
 * labelled `role="listbox"` (`aria-label` = „<label>, N zadań” — count included), its
 * heading TEXT carries the status (never color alone), arrow navigation lives INSIDE the
 * column via the shared listboxKeys, Tab leaves the widget (one tab stop per column), and
 * an empty column renders the catalog EmptyState with hint + action.
 */

function makeTask(overrides: Partial<TaskResponse> & Pick<TaskResponse, "id">): TaskResponse {
  return {
    title: overrides.id,
    status: "todo",
    position: "a0",
    version: 0,
    createdAt: "2026-08-01T00:00:00.000Z",
    updatedAt: "2026-08-01T00:00:00.000Z",
    completedAt: null,
    assignees: [],
    labels: [],
    ...overrides,
  };
}

function renderColumn(tasks: TaskResponse[], emptyAction?: ReactNode) {
  const queryClient = new QueryClient();
  return render(
    createElement(
      QueryClientProvider,
      { client: queryClient },
      <DndContext>
        <BoardColumn status="todo" label="Do zrobienia" tasks={tasks} emptyAction={emptyAction} />
      </DndContext>,
    ),
  );
}

afterEach(cleanup);

describe("BoardColumn — labelled listbox with a live count (D10, FR-044) [INV-142]", () => {
  it("renders the heading text with the Polish label and the visible count", () => {
    renderColumn([makeTask({ id: "c1", position: "a0" }), makeTask({ id: "c2", position: "a1" })]);

    // Status conveyed by heading TEXT (FR-044) + a visible count next to it.
    expect(screen.getByText("Do zrobienia")).toBeTruthy();
    expect(screen.getByText(/2 zadania/)).toBeTruthy();
  });

  it("exposes role=listbox with the count-bearing accessible name and a single tab stop", () => {
    renderColumn([makeTask({ id: "c1", position: "a0" })]);

    const listbox = screen.getByRole("listbox", { name: "Do zrobienia, 1 zadanie" });
    expect(listbox.getAttribute("tabindex")).toBe("0");
  });

  it("pluralizes the Polish count (1 zadanie / 2 zadania / 5 zadań)", () => {
    renderColumn(
      ["a", "b", "c", "d", "e"].map((id, i) => makeTask({ id, position: `a${i}` })),
    );
    expect(screen.getByRole("listbox", { name: "Do zrobienia, 5 zadań" })).toBeTruthy();
  });

  it("moves the active card with arrow keys INSIDE the column (listboxKeys reuse)", () => {
    renderColumn([makeTask({ id: "c1", position: "a0" }), makeTask({ id: "c2", position: "a1" })]);
    const listbox = screen.getByRole("listbox");

    expect(listbox.getAttribute("aria-activedescendant")).toBe(taskOptionId("c1"));
    fireEvent.keyDown(listbox, { key: "ArrowDown" });
    expect(listbox.getAttribute("aria-activedescendant")).toBe(taskOptionId("c2"));
  });

  it("renders the catalog EmptyState with hint + action in an empty column (FR-110) [INV-142]", () => {
    renderColumn([], <button type="button">Dodaj zadanie</button>);

    expect(screen.getByText("Brak zadań w tej kolumnie.")).toBeTruthy();
    expect(screen.getByRole("button", { name: "Dodaj zadanie" })).toBeTruthy();
  });
});

import { DndContext } from "@dnd-kit/core";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { cleanup, render, screen } from "@testing-library/react";
import { createElement, type ReactNode } from "react";
import { afterEach, describe, expect, it } from "vitest";

import { BoardColumn } from "@/components/tasks/BoardColumn";
import type { TaskResponse } from "@/hooks/useTasks";

/**
 * Board column coverage (slice 010, T017 — FR-044/FR-110). The column is a labelled
 * `role="list"` (`aria-label` = „<label>, N zadań” — count included) whose cards are
 * `listitem`s (their controls are ordinary tab stops — an option role would forbid
 * focusable children, axe `nested-interactive`), its heading TEXT carries the status
 * (never color alone), and an empty column renders the catalog EmptyState with hint +
 * action OUTSIDE the list element (axe `aria-required-children`).
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

describe("BoardColumn — labelled list with a live count (D10, FR-044) [INV-142]", () => {
  it("renders the heading text with the Polish label and the visible count", () => {
    renderColumn([makeTask({ id: "c1", position: "a0" }), makeTask({ id: "c2", position: "a1" })]);

    // Status conveyed by heading TEXT (FR-044) + a visible count next to it.
    expect(screen.getByText("Do zrobienia")).toBeTruthy();
    expect(screen.getByText(/2 zadania/)).toBeTruthy();
  });

  it("exposes role=list with the count-bearing accessible name and listitem cards", () => {
    renderColumn([makeTask({ id: "c1", position: "a0" })]);

    const list = screen.getByRole("list", { name: "Do zrobienia, 1 zadanie" });
    expect(screen.getAllByRole("listitem")).toHaveLength(1);
    // The list itself is not a widget — no tab stop, no active-descendant management.
    expect(list.getAttribute("tabindex")).toBeNull();
  });

  it("pluralizes the Polish count (1 zadanie / 2 zadania / 5 zadań)", () => {
    renderColumn(
      ["a", "b", "c", "d", "e"].map((id, i) => makeTask({ id, position: `a${i}` })),
    );
    expect(screen.getByRole("list", { name: "Do zrobienia, 5 zadań" })).toBeTruthy();
  });

  it("renders the catalog EmptyState with hint + action in an empty column (FR-110) [INV-142]", () => {
    renderColumn([], <button type="button">Dodaj zadanie</button>);

    expect(screen.getByText("Brak zadań w tej kolumnie.")).toBeTruthy();
    expect(screen.getByRole("button", { name: "Dodaj zadanie" })).toBeTruthy();
  });

  it("keeps the EmptyState OUTSIDE the list element (axe aria-required-children) [INV-142]", () => {
    renderColumn([], <button type="button">Dodaj zadanie</button>);

    const list = screen.getByRole("list", { name: "Do zrobienia, 0 zadań" });
    const action = screen.getByRole("button", { name: "Dodaj zadanie" });
    expect(list.contains(action)).toBe(false);
  });
});

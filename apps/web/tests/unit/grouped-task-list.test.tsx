import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { cleanup, fireEvent, render, screen } from "@testing-library/react";
import { createElement, useState } from "react";
import { afterEach, describe, expect, it, vi } from "vitest";

import { GroupByControl, GroupedTaskList } from "@/components/tasks/GroupedTaskList";
import { taskOptionId } from "@/components/tasks/TaskRow";
import type { TaskResponse } from "@/hooks/useTasks";
import { buildProjectGroups } from "@/lib/board";

/**
 * The groupable project List (slice 010, T015 — FR-024, D9, US-03.AS-07; roles remediated
 * to grid/rowgroup/row post-010). Pins the DailyView-proven grouped-grid contract on the
 * new component: ONE `role="grid"`, `role="rowgroup"` + `aria-label` per group, a FLAT
 * selection index across groups — plus the visible group-by control („Grupuj: Brak |
 * Status | Priorytet”; „wg cyklu” arrives with slice 011 and MUST NOT render here).
 */

function makeTask(overrides: Partial<TaskResponse> & Pick<TaskResponse, "id">): TaskResponse {
  return {
    title: overrides.id,
    status: "backlog",
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

const tasks: TaskResponse[] = [
  makeTask({ id: "t-todo", position: "a0", status: "todo" }),
  makeTask({ id: "t-done", position: "a1", status: "done" }),
  makeTask({ id: "t-cancelled", position: "a2", status: "cancelled" }),
];

function Harness() {
  const [selectedIndex, setSelectedIndex] = useState(0);
  return (
    <GroupedTaskList
      groups={buildProjectGroups(tasks, "status")}
      selectedIndex={selectedIndex}
      onSelectedIndexChange={setSelectedIndex}
    />
  );
}

function renderGrouped() {
  const queryClient = new QueryClient();
  return render(createElement(QueryClientProvider, { client: queryClient }, <Harness />));
}

afterEach(cleanup);

describe("GroupedTaskList — grouped grid with a flat index (FR-024, D9) [INV-146]", () => {
  it("renders ONE grid containing a role=rowgroup with an accessible label per group", () => {
    renderGrouped();

    expect(screen.getAllByRole("grid")).toHaveLength(1);
    expect(screen.getByRole("rowgroup", { name: "Do zrobienia" })).toBeTruthy();
    expect(screen.getByRole("rowgroup", { name: "Zrobione" })).toBeTruthy();
    expect(screen.getByRole("rowgroup", { name: "Anulowane" })).toBeTruthy();
  });

  it("renders cancelled tasks inside the „Anulowane” group (visible HERE, hidden on the Board)", () => {
    renderGrouped();

    const cancelled = screen.getByRole("rowgroup", { name: "Anulowane" });
    expect(cancelled.textContent).toContain("t-cancelled");
  });

  it("moves selection ACROSS group boundaries with one flat arrow-key index", () => {
    renderGrouped();
    const grid = screen.getByRole("grid");

    // Flat order over status groups: t-todo (Do zrobienia), t-done (Zrobione), t-cancelled (Anulowane).
    expect(grid.getAttribute("aria-activedescendant")).toBe(taskOptionId("t-todo"));

    fireEvent.keyDown(grid, { key: "ArrowDown" });
    expect(grid.getAttribute("aria-activedescendant")).toBe(taskOptionId("t-done"));

    fireEvent.keyDown(grid, { key: "ArrowDown" });
    expect(grid.getAttribute("aria-activedescendant")).toBe(taskOptionId("t-cancelled"));
  });
});

describe("GroupByControl — the visible „Grupuj” control (US-03.AS-07) [INV-146] [INV-147]", () => {
  afterEach(cleanup);

  it("renders the three choices as toggle buttons with aria-pressed reflecting the value", () => {
    render(<GroupByControl value="status" onChange={() => {}} />);

    expect(screen.getByRole("button", { name: "Brak" }).getAttribute("aria-pressed")).toBe("false");
    expect(screen.getByRole("button", { name: "Status" }).getAttribute("aria-pressed")).toBe("true");
    expect(screen.getByRole("button", { name: "Priorytet" }).getAttribute("aria-pressed")).toBe("false");
  });

  it("reports the chosen grouping through onChange", () => {
    const onChange = vi.fn();
    render(<GroupByControl value="none" onChange={onChange} />);

    fireEvent.click(screen.getByRole("button", { name: "Priorytet" }));
    expect(onChange).toHaveBeenCalledWith("priority");
  });

  it("does NOT offer „wg cyklu” (by-cycle waits for slice 011)", () => {
    render(<GroupByControl value="none" onChange={() => {}} />);

    expect(screen.queryByRole("button", { name: /cykl/i })).toBeNull();
  });
});

import { cleanup, fireEvent, render, screen } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";

import { CloseCycleDialog } from "@/components/cycles/CloseCycleDialog";
import type { components } from "@/lib/api/generated/schema";

type TaskResponse = components["schemas"]["TaskResponse"];
type CycleResponse = components["schemas"]["CycleResponse"];

/**
 * The close review (slice 011, T019 — US-05.AS-04/05/06): lists the caller-visible INCOMPLETE
 * tasks, offers the three-way bulk rollover (+ optional per-task overrides), states that the bulk
 * choice also covers other users' tasks, commits atomically, and renders the AS-06
 * „Najpierw utwórz nowy cykl" prompt CLIENT-SIDE when „next" is chosen with no planned cycle.
 */

function makeCycle(overrides: Partial<CycleResponse> = {}): CycleResponse {
  return {
    id: "c-active",
    name: "Sprint 7",
    startDate: "2026-01-05T00:00:00Z",
    endDate: "2026-01-19T00:00:00Z",
    status: "active",
    version: 3,
    createdAt: "2026-01-01T00:00:00Z",
    metrics: { total: 0, done: 0, breakdown: { backlog: 0, todo: 0, in_progress: 0, done: 0, cancelled: 0 } },
    ...overrides,
  };
}

function makeTask(overrides: Partial<TaskResponse> & Pick<TaskResponse, "id" | "title" | "status">): TaskResponse {
  return {
    position: "a0",
    version: 0,
    createdAt: "2026-01-01T00:00:00Z",
    updatedAt: "2026-01-01T00:00:00Z",
    completedAt: null,
    assignees: [],
    labels: [],
    cycleId: "c-active",
    carriedOver: false,
    ...overrides,
  };
}

const TASKS: TaskResponse[] = [
  makeTask({ id: "t1", title: "Niedokończone A", status: "todo" }),
  makeTask({ id: "t2", title: "Niedokończone B", status: "in_progress" }),
  makeTask({ id: "t3", title: "Skończone", status: "done" }),
  makeTask({ id: "t4", title: "Anulowane", status: "cancelled" }),
];

function renderDialog(
  overrides: Partial<{
    tasks: TaskResponse[];
    nextCycleName: string | null;
    onConfirm: (v: { rollover?: "next" | "backlog" | "keep"; overrides?: { taskId: string; choice: "next" | "backlog" | "keep" }[] }) => void;
    onCreateCycle: () => void;
  }> = {},
) {
  const onConfirm = overrides.onConfirm ?? vi.fn();
  const onCreateCycle = overrides.onCreateCycle ?? vi.fn();
  render(
    <CloseCycleDialog
      open
      cycle={makeCycle()}
      tasks={overrides.tasks ?? TASKS}
      nextCycleName={overrides.nextCycleName === undefined ? "Sprint 8" : overrides.nextCycleName}
      onClose={() => {}}
      onConfirm={onConfirm}
      onCreateCycle={onCreateCycle}
    />,
  );
  return { onConfirm, onCreateCycle };
}

afterEach(cleanup);

describe("CloseCycleDialog [INV-168] [INV-169]", () => {
  it("lists ONLY the incomplete caller-visible tasks (done/cancelled excluded) (AS-04)", () => {
    renderDialog();

    expect(screen.getByText("Niedokończone A")).toBeTruthy();
    expect(screen.getByText("Niedokończone B")).toBeTruthy();
    expect(screen.queryByText("Skończone")).toBeNull();
    expect(screen.queryByText("Anulowane")).toBeNull();
  });

  it("offers the three-way bulk choice and states the team-wide footer note (AS-05)", () => {
    renderDialog();

    expect(screen.getByRole("radio", { name: /Przenieś wszystkie do następnego cyklu/ })).toBeTruthy();
    expect(screen.getByRole("radio", { name: /Przenieś wszystkie do backlogu/ })).toBeTruthy();
    expect(screen.getByRole("radio", { name: /Zostaw w zamkniętym cyklu/ })).toBeTruthy();
    expect(screen.getByText(/dotyczy także zadań innych użytkowników/)).toBeTruthy();
  });

  it("confirms the bulk default with no overrides (one atomic commit)", () => {
    const onConfirm = vi.fn();
    renderDialog({ onConfirm });

    fireEvent.click(screen.getByRole("radio", { name: /Przenieś wszystkie do backlogu/ }));
    fireEvent.click(screen.getByRole("button", { name: "Zamknij cykl" }));

    expect(onConfirm).toHaveBeenCalledTimes(1);
    expect(onConfirm).toHaveBeenCalledWith({ rollover: "backlog", overrides: undefined });
  });

  it("„obsłuż pojedynczo” reveals per-task selects; only rows diverging from the bulk land as overrides", () => {
    const onConfirm = vi.fn();
    renderDialog({ onConfirm });

    fireEvent.click(screen.getByRole("button", { name: /obsłuż pojedynczo/i }));
    const selects = screen.getAllByRole("combobox", { name: /Niedokończone/ });
    expect(selects).toHaveLength(2);

    fireEvent.change(selects[1]!, { target: { value: "keep" } });
    fireEvent.click(screen.getByRole("button", { name: "Zamknij cykl" }));

    expect(onConfirm).toHaveBeenCalledWith({
      rollover: "next",
      overrides: [{ taskId: "t2", choice: "keep" }],
    });
  });

  it("renders the AS-06 prompt CLIENT-SIDE when „next” is chosen with no planned cycle — nothing commits", () => {
    const onConfirm = vi.fn();
    const onCreateCycle = vi.fn();
    renderDialog({ nextCycleName: null, onConfirm, onCreateCycle });

    fireEvent.click(screen.getByRole("button", { name: "Zamknij cykl" }));

    expect(onConfirm).not.toHaveBeenCalled();
    expect(screen.getByText(/Najpierw utwórz nowy cykl/)).toBeTruthy();

    fireEvent.click(screen.getByRole("button", { name: "Nowy cykl" }));
    expect(onCreateCycle).toHaveBeenCalledTimes(1);
  });

  it("with ZERO incomplete tasks it is a pure close — no rollover choice, confirm sends no rollover", () => {
    const onConfirm = vi.fn();
    renderDialog({ tasks: [TASKS[2]!, TASKS[3]!], nextCycleName: null, onConfirm });

    expect(screen.queryByRole("radio")).toBeNull();
    fireEvent.click(screen.getByRole("button", { name: "Zamknij cykl" }));

    expect(onConfirm).toHaveBeenCalledWith({ rollover: undefined, overrides: undefined });
  });
});

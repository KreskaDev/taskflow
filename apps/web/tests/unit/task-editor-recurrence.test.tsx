import { cleanup, fireEvent, render, screen } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";

vi.mock("@/hooks/useProjects", () => ({
  useProjects: () => ({ data: [], isPending: false, isError: false }),
}));

import { RECURRENCE_DUE_GUARD, TaskEditor, type TaskEditorFields } from "@/components/tasks/TaskEditor";
import type { components } from "@/lib/api/generated/schema";

type TaskResponse = components["schemas"]["TaskResponse"];

/**
 * The `E` editor hosts the shared „Powtarzanie" field (slice 012, T017 — contracts/ui-recurrence.md): the rule
 * rides the whole-object save, and clearing the due of a recurring task is BLOCKED with the inline guard
 * (Clarifications Q4) — „Zapisz" disabled, Ctrl+Enter inert, nothing sent. [INV-176] [INV-181]
 */

afterEach(cleanup);

function makeTask(overrides: Partial<TaskResponse> = {}): TaskResponse {
  return {
    id: "11111111-1111-1111-1111-111111111111",
    title: "Podlej kwiaty",
    status: "backlog",
    position: "a0",
    version: 1,
    createdAt: "2026-06-01T10:00:00Z",
    updatedAt: "2026-06-01T10:00:00Z",
    completedAt: null,
    // 2026-06-10 00:00 Europe/Warsaw (date-only)
    dueDate: "2026-06-09T22:00:00Z",
    dueHasTime: false,
    assignees: [],
    labels: [],
    cycleId: null,
    carriedOver: false,
    ...overrides,
  } as TaskResponse;
}

function renderEditor(task: TaskResponse) {
  const onSave = vi.fn<(fields: TaskEditorFields) => void>();
  render(<TaskEditor open task={task} onClose={() => {}} onSave={onSave} />);
  return { onSave };
}

describe("TaskEditor — recurrence field [INV-176]", () => {
  it("renders „Powtarzanie” seeded from the task and saves the rule with the whole object", () => {
    const { onSave } = renderEditor(makeTask({ recurrenceRule: { type: "every_n_days", interval: 3 } } as Partial<TaskResponse>));
    expect((screen.getByRole("combobox", { name: "Powtarzanie" }) as HTMLSelectElement).value).toBe("every_n_days");

    fireEvent.change(screen.getByRole("combobox", { name: "Powtarzanie" }), { target: { value: "daily" } });
    fireEvent.click(screen.getByRole("button", { name: "Zapisz (Ctrl+Enter)" }));

    expect(onSave).toHaveBeenCalledTimes(1);
    expect(onSave.mock.calls[0]![0].recurrenceRule).toEqual({ type: "daily" });
    expect(onSave.mock.calls[0]![0].dueDate?.toISOString()).toBe("2026-06-09T22:00:00.000Z");
  });

  it("„W wybrane dni tygodnia” keeps its checkboxes while no day is chosen (the host echoes null) and saves the picked days", () => {
    const { onSave } = renderEditor(makeTask());
    fireEvent.change(screen.getByRole("combobox", { name: "Powtarzanie" }), { target: { value: "weekdays" } });
    expect(screen.getByRole("checkbox", { name: "poniedziałek" })).toBeTruthy();
    expect(screen.getByRole("button", { name: "Zapisz (Ctrl+Enter)" })).toBeDisabled();

    fireEvent.click(screen.getByRole("checkbox", { name: "poniedziałek" }));
    fireEvent.click(screen.getByRole("checkbox", { name: "środa" }));
    fireEvent.click(screen.getByRole("button", { name: "Zapisz (Ctrl+Enter)" }));

    expect(onSave.mock.calls[0]![0].recurrenceRule).toEqual({ type: "weekdays", weekdays: [1, 3] });
  });

  it("a non-recurring task saves recurrenceRule: null", () => {
    const { onSave } = renderEditor(makeTask());
    fireEvent.click(screen.getByRole("button", { name: "Zapisz (Ctrl+Enter)" }));
    expect(onSave.mock.calls[0]![0].recurrenceRule).toBeNull();
  });

  it("is disabled with the hint when the task has no due date", () => {
    renderEditor(makeTask({ dueDate: null, dueHasTime: null }));
    expect(screen.getByRole("combobox", { name: "Powtarzanie" })).toBeDisabled();
    expect(screen.getByText("Ustaw najpierw termin")).toBeTruthy();
  });
});

describe("TaskEditor — the due-date guard [INV-181]", () => {
  it("checking „Usuń termin” while a rule is set shows the guard, disables „Zapisz” and ignores Ctrl+Enter", () => {
    const { onSave } = renderEditor(makeTask({ recurrenceRule: { type: "daily" } } as Partial<TaskResponse>));

    fireEvent.click(screen.getByRole("checkbox", { name: "Usuń termin" }));

    expect(screen.getByRole("alert")).toHaveTextContent(RECURRENCE_DUE_GUARD);
    const save = screen.getByRole("button", { name: "Zapisz (Ctrl+Enter)" });
    expect(save).toBeDisabled();
    fireEvent.keyDown(screen.getByRole("dialog"), { key: "Enter", ctrlKey: true });
    expect(onSave).not.toHaveBeenCalled();
  });

  it("switching to „Nie powtarza się” lifts the guard — clearing due AND rule together saves", () => {
    const { onSave } = renderEditor(makeTask({ recurrenceRule: { type: "daily" } } as Partial<TaskResponse>));
    fireEvent.click(screen.getByRole("checkbox", { name: "Usuń termin" }));
    expect(screen.getByRole("alert")).toBeTruthy();

    fireEvent.change(screen.getByRole("combobox", { name: "Powtarzanie" }), { target: { value: "none" } });

    expect(screen.queryByRole("alert")).toBeNull();
    fireEvent.click(screen.getByRole("button", { name: "Zapisz (Ctrl+Enter)" }));
    expect(onSave).toHaveBeenCalledTimes(1);
    expect(onSave.mock.calls[0]![0]).toMatchObject({ dueDate: null, dueHasTime: null, recurrenceRule: null });
  });
});

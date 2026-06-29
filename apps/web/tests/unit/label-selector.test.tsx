import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { cleanup, fireEvent, render, screen } from "@testing-library/react";
import { createElement, type ReactNode } from "react";
import { afterEach, describe, expect, it, vi } from "vitest";

// vi.hoisted so the spy exists before the hoisted vi.mock factory runs.
const { createLabel } = vi.hoisted(() => ({
  createLabel: vi.fn(async (name: string) => ({ id: "new-1", name, color: null })),
}));

vi.mock("@/hooks/useLabels", () => ({
  useLabelRoster: () => ({
    data: [
      { id: "l1", name: "Urgent", color: "red" },
      { id: "l2", name: "Home", color: null },
    ],
    isPending: false,
    isError: false,
  }),
  useLabelMutations: () => ({ createLabel, updateLabel: vi.fn(), deleteLabel: vi.fn() }),
}));

import { LabelSelector } from "@/components/labels/LabelSelector";

afterEach(() => {
  cleanup();
  vi.clearAllMocks();
});

function renderSelector(current: string[], onSubmit: (ids: string[]) => void): void {
  const queryClient = new QueryClient();
  const tree: ReactNode = createElement(
    QueryClientProvider,
    { client: queryClient },
    <LabelSelector open current={current} onClose={() => {}} onSubmit={onSubmit} />,
  );
  render(tree);
}

describe("LabelSelector", () => {
  it("lists the caller's labels by name as keyboard-operable checkboxes", () => {
    renderSelector([], vi.fn());

    expect(screen.getByText("Urgent")).toBeTruthy();
    expect(screen.getByText("Home")).toBeTruthy();
    expect(screen.getAllByRole("checkbox")).toHaveLength(2);
  });

  it("seeds the checked set from `current`, toggles, and commits the whole set on Save", () => {
    const onSubmit = vi.fn();
    renderSelector(["l1"], onSubmit);

    const checkboxes = screen.getAllByRole("checkbox");
    expect((checkboxes[0] as HTMLInputElement).checked).toBe(true); // l1 seeded
    fireEvent.click(checkboxes[1]!); // toggle l2 on

    fireEvent.click(screen.getByRole("button", { name: /Zapisz/ }));

    expect(onSubmit).toHaveBeenCalledTimes(1);
    expect(new Set(onSubmit.mock.calls[0]![0] as string[])).toEqual(new Set(["l1", "l2"]));
  });

  it("type-to-create calls createLabel with the typed name", async () => {
    renderSelector([], vi.fn());

    const input = screen.getByPlaceholderText("Nowa etykieta…");
    fireEvent.change(input, { target: { value: "  Quick  " } });
    fireEvent.keyDown(input, { key: "Enter" });

    await vi.waitFor(() => expect(createLabel).toHaveBeenCalledWith("Quick"));
  });
});

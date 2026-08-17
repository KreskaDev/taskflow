import { cleanup, fireEvent, render, screen } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";

vi.mock("@/hooks/useCycles", () => ({
  useCycles: () => ({
    data: [
      {
        id: "c2",
        name: "Cykl 2",
        startDate: "2026-02-01T00:00:00Z",
        endDate: "2026-02-15T00:00:00Z",
        status: "planned",
        version: 0,
        createdAt: "2026-01-02T00:00:00Z",
        metrics: { total: 0, done: 0, breakdown: { backlog: 0, todo: 0, in_progress: 0, done: 0, cancelled: 0 } },
      },
      {
        id: "c1",
        name: "Cykl 1",
        startDate: "2026-01-05T00:00:00Z",
        endDate: "2026-01-19T00:00:00Z",
        status: "active",
        version: 1,
        createdAt: "2026-01-01T00:00:00Z",
        metrics: { total: 0, done: 0, breakdown: { backlog: 0, todo: 0, in_progress: 0, done: 0, cancelled: 0 } },
      },
      {
        id: "c0",
        name: "Cykl 0",
        startDate: "2025-12-01T00:00:00Z",
        endDate: "2025-12-15T00:00:00Z",
        status: "closed",
        version: 2,
        createdAt: "2025-11-20T00:00:00Z",
        metrics: { total: 0, done: 0, breakdown: { backlog: 0, todo: 0, in_progress: 0, done: 0, cancelled: 0 } },
      },
    ],
    isPending: false,
    isError: false,
  }),
}));

import { CyclePicker } from "@/components/cycles/CyclePicker";

afterEach(cleanup);

/**
 * CyclePicker (slice 011, T016 — US-05.AS-01/02, D12): the „Cykl…" dialog lists ALL cycles —
 * active, planned AND closed (Clarifications) — in D5 order with the Polish status suffix, plus
 * „Bez cyklu" to clear; the current assignment is marked; selection commits and closes.
 */
describe("CyclePicker [INV-173]", () => {
  it("lists ALL cycles (closed included) in D5 order with status suffixes, then „Bez cyklu” last", () => {
    render(<CyclePicker open current={null} onClose={() => {}} onSelect={() => {}} />);

    const labels = screen.getAllByRole("button").map((b) => b.textContent);
    expect(labels).toEqual([
      "Cykl 0 (zamknięty)",
      "Cykl 1 (aktywny)",
      "Cykl 2 (planowany)",
      "Bez cyklu",
    ]);
  });

  it("marks the task's current assignment (and only it)", () => {
    render(<CyclePicker open current="c1" onClose={() => {}} onSelect={() => {}} />);

    const pressed = screen
      .getAllByRole("button")
      .filter((b) => b.getAttribute("aria-pressed") === "true")
      .map((b) => b.textContent);
    expect(pressed).toEqual(["Cykl 1 (aktywny)"]);
  });

  it("marks „Bez cyklu” when the task has no assignment", () => {
    render(<CyclePicker open current={null} onClose={() => {}} onSelect={() => {}} />);

    const pressed = screen
      .getAllByRole("button")
      .filter((b) => b.getAttribute("aria-pressed") === "true")
      .map((b) => b.textContent);
    expect(pressed).toEqual(["Bez cyklu"]);
  });

  it("selecting a cycle fires onSelect(cycleId) and closes (AS-01)", () => {
    const onSelect = vi.fn();
    const onClose = vi.fn();
    render(<CyclePicker open current={null} onClose={onClose} onSelect={onSelect} />);

    fireEvent.click(screen.getByRole("button", { name: "Cykl 2 (planowany)" }));

    expect(onSelect).toHaveBeenCalledWith("c2");
    expect(onClose).toHaveBeenCalledTimes(1);
  });

  it("selecting „Bez cyklu” fires onSelect(null) — the clear path (AS-02)", () => {
    const onSelect = vi.fn();
    const onClose = vi.fn();
    render(<CyclePicker open current="c1" onClose={onClose} onSelect={onSelect} />);

    fireEvent.click(screen.getByRole("button", { name: "Bez cyklu" }));

    expect(onSelect).toHaveBeenCalledWith(null);
    expect(onClose).toHaveBeenCalledTimes(1);
  });

  it("renders nothing when closed", () => {
    const { container } = render(
      <CyclePicker open={false} current={null} onClose={() => {}} onSelect={() => {}} />,
    );
    expect(container.innerHTML).toBe("");
  });
});

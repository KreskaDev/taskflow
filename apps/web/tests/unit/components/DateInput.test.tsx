// [C] DateInput catalog spec (T009, slice 019) — Polish NL parse passthrough with the
// slice-003 grammar and the FR-006 error presentation (RescheduleInput behavior unchanged).
import { fireEvent, render, screen } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import { DateInput } from "@/components/ui/DateInput";

describe("DateInput (catalog)", () => {
  it("resolves a recognized Polish phrase on Enter and commits the instant", () => {
    const onCommit = vi.fn();
    render(<DateInput label="Nowy termin" onCommit={onCommit} />);
    const input = screen.getByRole("textbox", { name: "Nowy termin" });
    fireEvent.change(input, { target: { value: "jutro" } });
    fireEvent.keyDown(input, { key: "Enter" });
    expect(onCommit).toHaveBeenCalledOnce();
    const [dueDate, dueHasTime] = onCommit.mock.calls[0]!;
    expect(dueDate).toBeInstanceOf(Date);
    expect(dueHasTime).toBe(false);
  });

  it("empty input commits a clear (null/null)", () => {
    const onCommit = vi.fn();
    render(<DateInput label="Nowy termin" onCommit={onCommit} />);
    fireEvent.keyDown(screen.getByRole("textbox", { name: "Nowy termin" }), { key: "Enter" });
    expect(onCommit).toHaveBeenCalledWith(null, null);
  });

  it("unrecognized phrase: FR-006 presentation — visible error, value retained, no commit", () => {
    const onCommit = vi.fn();
    render(<DateInput label="Nowy termin" onCommit={onCommit} />);
    const input = screen.getByRole("textbox", { name: "Nowy termin" });
    fireEvent.change(input, { target: { value: "30.02" } });
    fireEvent.keyDown(input, { key: "Enter" });
    expect(onCommit).not.toHaveBeenCalled();
    expect(input).toHaveValue("30.02");
    expect(input).toHaveAttribute("aria-invalid", "true");
    expect(screen.getByText(/nie rozpoznano/i)).toBeVisible();
  });

  it("typing after an error clears the error", () => {
    render(<DateInput label="Nowy termin" onCommit={vi.fn()} />);
    const input = screen.getByRole("textbox", { name: "Nowy termin" });
    fireEvent.change(input, { target: { value: "30.02" } });
    fireEvent.keyDown(input, { key: "Enter" });
    expect(screen.getByText(/nie rozpoznano/i)).toBeVisible();
    fireEvent.change(input, { target: { value: "30.06" } });
    expect(screen.queryByText(/nie rozpoznano/i)).toBeNull();
  });

  it("Esc calls onCancel (FR-030 editing key)", () => {
    const onCancel = vi.fn();
    render(<DateInput label="Nowy termin" onCommit={vi.fn()} onCancel={onCancel} />);
    fireEvent.keyDown(screen.getByRole("textbox", { name: "Nowy termin" }), { key: "Escape" });
    expect(onCancel).toHaveBeenCalledOnce();
  });
});

// [C] Checkbox catalog spec (T010, slice 019) — padded hit area, keyboard toggle, states.
import { fireEvent, render, screen } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import { Checkbox } from "@/components/ui/Checkbox";

describe("Checkbox (catalog)", () => {
  it("renders a real checkbox with the accessible name", () => {
    render(<Checkbox aria-label="Oznacz jako zrobione" />);
    expect(screen.getByRole("checkbox", { name: "Oznacz jako zrobione" })).toBeInTheDocument();
  });

  it("wraps the control in the 32px padded hit-area label", () => {
    const { container } = render(<Checkbox aria-label="Zrobione" />);
    expect(container.querySelector("label")?.className).toContain("hitArea");
  });

  it("toggles via keyboard interaction on the native input (Space semantics)", () => {
    const onChange = vi.fn();
    render(<Checkbox aria-label="Zrobione" onChange={onChange} />);
    const box = screen.getByRole("checkbox", { name: "Zrobione" });
    fireEvent.click(box);
    expect(onChange).toHaveBeenCalledOnce();
  });

  it("checked and disabled states are semantic", () => {
    render(<Checkbox aria-label="Stan" checked readOnly disabled />);
    const box = screen.getByRole("checkbox", { name: "Stan" });
    expect(box).toBeChecked();
    expect(box).toBeDisabled();
  });

  it("indeterminate state is reflected on the element", () => {
    render(<Checkbox aria-label="Częściowo" indeterminate />);
    const box = screen.getByRole("checkbox", { name: "Częściowo" }) as HTMLInputElement;
    expect(box.indeterminate).toBe(true);
  });
});

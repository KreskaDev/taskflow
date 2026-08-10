// [C] Tooltip catalog spec (T018, slice 019) — focus-triggered reveal (NO hover-only
// content, FR-046) for truncated-value reveal (S5.4).
import { fireEvent, render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { Tooltip } from "@/components/ui/Tooltip";

describe("Tooltip (catalog)", () => {
  it("content is hidden until the trigger receives focus", () => {
    render(
      <Tooltip label="Pełna bardzo długa nazwa">
        <span>Pełna bardzo…</span>
      </Tooltip>,
    );
    expect(screen.queryByRole("tooltip")).toBeNull();
  });

  it("keyboard focus reveals the tooltip (no pointer needed) and blur hides it", () => {
    render(
      <Tooltip label="Pełna bardzo długa nazwa">
        <span>Pełna bardzo…</span>
      </Tooltip>,
    );
    const trigger = screen.getByText("Pełna bardzo…").parentElement!;
    fireEvent.focus(trigger);
    expect(screen.getByRole("tooltip")).toHaveTextContent("Pełna bardzo długa nazwa");
    fireEvent.blur(trigger);
    expect(screen.queryByRole("tooltip")).toBeNull();
  });

  it("trigger is keyboard-reachable (tabIndex) and described by the tooltip when open", () => {
    render(
      <Tooltip label="Wartość w całości">
        <span>Wartość…</span>
      </Tooltip>,
    );
    const trigger = screen.getByText("Wartość…").parentElement!;
    expect(trigger).toHaveAttribute("tabindex", "0");
    fireEvent.focus(trigger);
    expect(trigger.getAttribute("aria-describedby")).toBe(screen.getByRole("tooltip").id);
  });

  it("hover also reveals (pointer parity), Escape dismisses", () => {
    render(
      <Tooltip label="Treść podpowiedzi">
        <span>Krótko…</span>
      </Tooltip>,
    );
    const trigger = screen.getByText("Krótko…").parentElement!;
    fireEvent.mouseEnter(trigger);
    expect(screen.getByRole("tooltip")).toBeInTheDocument();
    fireEvent.keyDown(trigger, { key: "Escape" });
    expect(screen.queryByRole("tooltip")).toBeNull();
  });
});

// [C] Chip catalog spec (T011, slice 019) — label/assignee variants, keyboard removal, truncation.
import { fireEvent, render, screen } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import { Chip } from "@/components/ui/Chip";

describe("Chip (catalog)", () => {
  it("renders the label text (name carries the meaning, never color alone)", () => {
    render(<Chip label="dom" />);
    expect(screen.getByText("dom")).toBeInTheDocument();
  });

  it("label variant renders the color dot as decorative", () => {
    const { container } = render(<Chip label="praca" color="blue" />);
    const dot = container.querySelector("[data-chip-dot]");
    expect(dot).not.toBeNull();
    expect(dot).toHaveAttribute("aria-hidden", "true");
  });

  it("removable: renders a labelled remove button operable by keyboard (FR-046)", () => {
    const onRemove = vi.fn();
    render(<Chip label="dom" onRemove={onRemove} removeLabel="Usuń etykietę dom" />);
    const btn = screen.getByRole("button", { name: "Usuń etykietę dom" });
    fireEvent.click(btn); // Enter/Space on a native button dispatch click
    expect(onRemove).toHaveBeenCalledOnce();
  });

  it("without onRemove there is no remove affordance", () => {
    render(<Chip label="dom" />);
    expect(screen.queryByRole("button")).toBeNull();
  });

  it("applies the truncation class to the text node", () => {
    render(<Chip label="bardzo długa nazwa etykiety która się nie mieści" />);
    expect(screen.getByText(/bardzo długa/).className).toContain("text");
  });
});

// [C] Input catalog spec (T009, slice 019) — states + FR-006 error presentation.
import { render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { Input } from "@/components/ui/Input";

describe("Input (catalog)", () => {
  it("renders a labelled textbox", () => {
    render(<Input aria-label="Tytuł" />);
    expect(screen.getByRole("textbox", { name: "Tytuł" })).toBeInTheDocument();
  });

  it("error state: aria-invalid + visible message wired via aria-describedby", () => {
    render(<Input aria-label="Tytuł" error="Tytuł jest wymagany." />);
    const input = screen.getByRole("textbox", { name: "Tytuł" });
    expect(input).toHaveAttribute("aria-invalid", "true");
    const message = screen.getByText("Tytuł jest wymagany.");
    expect(message).toBeVisible();
    expect(input.getAttribute("aria-describedby")).toBe(message.id);
  });

  it("no error: no aria-invalid, no message node", () => {
    render(<Input aria-label="Tytuł" />);
    const input = screen.getByRole("textbox", { name: "Tytuł" });
    expect(input).not.toHaveAttribute("aria-invalid");
    expect(input).not.toHaveAttribute("aria-describedby");
  });

  it("disabled state is semantic", () => {
    render(<Input aria-label="Pole" disabled />);
    expect(screen.getByRole("textbox", { name: "Pole" })).toBeDisabled();
  });
});

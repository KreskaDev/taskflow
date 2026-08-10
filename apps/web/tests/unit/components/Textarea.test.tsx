// [C] Textarea catalog spec (T009, slice 019) — states + FR-006 error presentation.
import { render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { Textarea } from "@/components/ui/Textarea";

describe("Textarea (catalog)", () => {
  it("renders a labelled multiline textbox", () => {
    render(<Textarea aria-label="Opis" />);
    expect(screen.getByRole("textbox", { name: "Opis" })).toBeInTheDocument();
  });

  it("error state: aria-invalid + visible message wired via aria-describedby", () => {
    render(<Textarea aria-label="Opis" error="Opis jest za długi." />);
    const field = screen.getByRole("textbox", { name: "Opis" });
    expect(field).toHaveAttribute("aria-invalid", "true");
    const message = screen.getByText("Opis jest za długi.");
    expect(message).toBeVisible();
    expect(field.getAttribute("aria-describedby")).toBe(message.id);
  });

  it("disabled state is semantic", () => {
    render(<Textarea aria-label="Opis" disabled />);
    expect(screen.getByRole("textbox", { name: "Opis" })).toBeDisabled();
  });
});

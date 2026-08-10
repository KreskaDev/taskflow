// [C] IconButton catalog spec (T008, slice 019) — ≥32px hit area + required aria-label.
import { fireEvent, render, screen } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import { IconButton } from "@/components/ui/IconButton";

describe("IconButton (catalog)", () => {
  it("exposes the required aria-label as its accessible name", () => {
    render(
      <IconButton aria-label="Więcej akcji">
        <svg aria-hidden="true" />
      </IconButton>,
    );
    expect(screen.getByRole("button", { name: "Więcej akcji" })).toBeInTheDocument();
  });

  it("carries the ≥32px hit-area class (icon smaller, target padded)", () => {
    render(<IconButton aria-label="Zamknij">×</IconButton>);
    expect(screen.getByRole("button", { name: "Zamknij" }).className).toContain("iconButton");
  });

  it("defaults type=button and fires onClick", () => {
    const onClick = vi.fn();
    render(
      <IconButton aria-label="Akcja" onClick={onClick}>
        i
      </IconButton>,
    );
    const btn = screen.getByRole("button", { name: "Akcja" });
    expect(btn).toHaveAttribute("type", "button");
    fireEvent.click(btn);
    expect(onClick).toHaveBeenCalledOnce();
  });

  it("disabled: semantic attribute, no click", () => {
    const onClick = vi.fn();
    render(
      <IconButton aria-label="Nieaktywny" disabled onClick={onClick}>
        i
      </IconButton>,
    );
    const btn = screen.getByRole("button", { name: "Nieaktywny" });
    expect(btn).toBeDisabled();
    fireEvent.click(btn);
    expect(onClick).not.toHaveBeenCalled();
  });
});

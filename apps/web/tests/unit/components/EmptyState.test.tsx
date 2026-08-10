// [C] EmptyState catalog spec (T016, slice 019) — short hint + action slot, no wizards.
import { fireEvent, render, screen } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import { Button } from "@/components/ui/Button";
import { EmptyState } from "@/components/ui/EmptyState";

describe("EmptyState (catalog)", () => {
  it("renders the hint text", () => {
    render(<EmptyState hint="Twój Inbox jest pusty." />);
    expect(screen.getByText("Twój Inbox jest pusty.")).toBeVisible();
  });

  it("renders the action slot and the action is operable", () => {
    const onClick = vi.fn();
    render(
      <EmptyState
        hint="Brak zadań na dziś."
        action={<Button onClick={onClick}>Dodaj zadanie</Button>}
      />,
    );
    fireEvent.click(screen.getByRole("button", { name: "Dodaj zadanie" }));
    expect(onClick).toHaveBeenCalledOnce();
  });

  it("renders without an action (hint-only surfaces)", () => {
    render(<EmptyState hint="Brak komentarzy." />);
    expect(screen.queryByRole("button")).toBeNull();
  });
});

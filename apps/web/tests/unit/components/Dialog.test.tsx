// [C] Dialog catalog spec (T013, slice 019) — the FULL FR-101 focus contract:
// initial focus, trap, Esc dismiss, focus return to the invoker.
import { fireEvent, render, screen } from "@testing-library/react";
import { useState } from "react";
import { describe, expect, it } from "vitest";
import { Dialog } from "@/components/ui/Dialog";

function Host() {
  const [open, setOpen] = useState(false);
  return (
    <>
      <button type="button" onClick={() => setOpen(true)}>
        Otwórz
      </button>
      <Dialog open={open} onClose={() => setOpen(false)} titleId="t">
        <h2 id="t">Potwierdzenie</h2>
        <button type="button">Pierwszy</button>
        <button type="button">Drugi</button>
      </Dialog>
    </>
  );
}

describe("Dialog (catalog) [INV-132]", () => {
  it("sets initial focus into the dialog on open", () => {
    render(<Host />);
    const invoker = screen.getByRole("button", { name: "Otwórz" });
    invoker.focus();
    fireEvent.click(invoker);
    expect(screen.getByRole("dialog")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Pierwszy" })).toHaveFocus();
  });

  it("traps focus: Tab from the last element wraps to the first, Shift+Tab wraps back", () => {
    render(<Host />);
    fireEvent.click(screen.getByRole("button", { name: "Otwórz" }));
    const dialog = screen.getByRole("dialog");
    const first = screen.getByRole("button", { name: "Pierwszy" });
    const last = screen.getByRole("button", { name: "Drugi" });

    last.focus();
    fireEvent.keyDown(dialog, { key: "Tab" });
    expect(first).toHaveFocus();

    first.focus();
    fireEvent.keyDown(dialog, { key: "Tab", shiftKey: true });
    expect(last).toHaveFocus();
  });

  it("Esc closes and focus returns to the invoker", () => {
    render(<Host />);
    const invoker = screen.getByRole("button", { name: "Otwórz" });
    invoker.focus();
    fireEvent.click(invoker);
    fireEvent.keyDown(screen.getByRole("dialog"), { key: "Escape" });
    expect(screen.queryByRole("dialog")).toBeNull();
    expect(invoker).toHaveFocus();
  });

  it("is labelled by its title (aria-labelledby + aria-modal)", () => {
    render(<Host />);
    fireEvent.click(screen.getByRole("button", { name: "Otwórz" }));
    const dialog = screen.getByRole("dialog", { name: "Potwierdzenie" });
    expect(dialog).toHaveAttribute("aria-modal", "true");
  });
});

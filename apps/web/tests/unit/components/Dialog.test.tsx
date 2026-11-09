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
  it("size=\"wide\" widens the surface (the palette's content must not overflow the 28rem default)", () => {
    render(
      <Dialog open onClose={() => {}} titleId="w" size="wide">
        <h2 id="w">Szeroki</h2>
      </Dialog>,
    );
    expect(screen.getByRole("dialog").className).toContain("dialogWide");
  });

  it("renders through a portal on document.body — a host inside a stacking context (sticky topbar) must not trap the overlay under the sidebar", () => {
    render(
      <div data-testid="host" style={{ position: "sticky", zIndex: 10 }}>
        <Dialog open onClose={() => {}} titleId="p">
          <h2 id="p">Portal</h2>
        </Dialog>
      </div>,
    );
    const dialog = screen.getByRole("dialog");
    expect(screen.getByTestId("host").contains(dialog)).toBe(false);
    expect(dialog.closest("body")).toBe(document.body);
  });

  it("defaults to the standard width (no wide class)", () => {
    render(<Host />);
    fireEvent.click(screen.getByRole("button", { name: "Otwórz" }));
    expect(screen.getByRole("dialog").className).not.toContain("dialogWide");
  });

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

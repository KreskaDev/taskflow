// [C] Drawer catalog spec (T050, slice 019) — non-modal semantics, Esc-with-field-exception
// (S4.2, FR-030), focus return to the invoker.
import { fireEvent, render, screen } from "@testing-library/react";
import { useState } from "react";
import { describe, expect, it } from "vitest";
import { Drawer } from "@/components/ui/Drawer";

function Host() {
  const [open, setOpen] = useState(false);
  return (
    <>
      <button type="button" onClick={() => setOpen(true)}>
        Otwórz szczegóły
      </button>
      <button type="button">Lista za drawerem</button>
      <Drawer open={open} onClose={() => setOpen(false)} titleId="d-title">
        <h2 id="d-title">Zadanie</h2>
        <input aria-label="Tytuł zadania" defaultValue="Coś" />
        <button type="button">Akcja w drawerze</button>
      </Drawer>
    </>
  );
}

describe("Drawer (catalog)", () => {
  it("is NON-modal: no dialog role, no aria-modal, page content stays reachable", () => {
    render(<Host />);
    fireEvent.click(screen.getByRole("button", { name: "Otwórz szczegóły" }));
    expect(screen.queryByRole("dialog")).toBeNull();
    const region = screen.getByRole("complementary", { name: "Zadanie" });
    expect(region).not.toHaveAttribute("aria-modal");
    // The list behind is still in the tree and focusable — no trap.
    const behind = screen.getByRole("button", { name: "Lista za drawerem" });
    behind.focus();
    expect(behind).toHaveFocus();
  });

  it("Esc on non-field content closes and returns focus to the invoker", () => {
    render(<Host />);
    const invoker = screen.getByRole("button", { name: "Otwórz szczegóły" });
    invoker.focus();
    fireEvent.click(invoker);
    const inside = screen.getByRole("button", { name: "Akcja w drawerze" });
    inside.focus();
    fireEvent.keyDown(inside, { key: "Escape" });
    expect(screen.queryByRole("complementary")).toBeNull();
    expect(invoker).toHaveFocus();
  });

  it("Esc while a TEXT FIELD inside is focused does NOT close (the field edit cancels first — FR-030)", () => {
    render(<Host />);
    fireEvent.click(screen.getByRole("button", { name: "Otwórz szczegóły" }));
    const field = screen.getByRole("textbox", { name: "Tytuł zadania" });
    field.focus();
    fireEvent.keyDown(field, { key: "Escape" });
    expect(screen.getByRole("complementary")).toBeInTheDocument();
  });

  it("offers an explicit close affordance", () => {
    render(<Host />);
    fireEvent.click(screen.getByRole("button", { name: "Otwórz szczegóły" }));
    fireEvent.click(screen.getByRole("button", { name: "Zamknij panel" }));
    expect(screen.queryByRole("complementary")).toBeNull();
  });
});

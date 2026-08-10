// [C] Menu catalog spec (T012, slice 019) — full keyboard-navigation + ARIA contract:
// role="menu", arrow/Home/End nav, aria-expanded on the trigger, Esc + focus return,
// activation closes, destructive styling.
import { fireEvent, render, screen } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import { Menu, type MenuItemSpec } from "@/components/ui/Menu";

function makeItems(overrides?: Partial<Record<string, () => void>>): MenuItemSpec[] {
  return [
    { id: "edit", label: "Edytuj", onSelect: overrides?.edit ?? vi.fn() },
    { id: "dup", label: "Duplikuj", onSelect: overrides?.dup ?? vi.fn() },
    { id: "del", label: "Usuń", onSelect: overrides?.del ?? vi.fn(), destructive: true },
  ];
}

function renderMenu(items = makeItems()) {
  render(<Menu items={items} triggerLabel="Więcej akcji" triggerContent="⋯" />);
  return screen.getByRole("button", { name: "Więcej akcji" });
}

describe("Menu (catalog)", () => {
  it("trigger carries aria-haspopup and aria-expanded=false when closed", () => {
    const trigger = renderMenu();
    expect(trigger).toHaveAttribute("aria-haspopup", "menu");
    expect(trigger).toHaveAttribute("aria-expanded", "false");
    expect(screen.queryByRole("menu")).toBeNull();
  });

  it("opening shows role=menu with menuitems, focuses the first item, trigger aria-expanded=true", () => {
    const trigger = renderMenu();
    fireEvent.click(trigger);
    expect(trigger).toHaveAttribute("aria-expanded", "true");
    const menu = screen.getByRole("menu");
    const items = screen.getAllByRole("menuitem");
    expect(menu).toBeInTheDocument();
    expect(items).toHaveLength(3);
    expect(items[0]).toHaveFocus();
  });

  it("ArrowDown/ArrowUp move focus with wrap-around; Home/End jump", () => {
    const trigger = renderMenu();
    fireEvent.click(trigger);
    const items = screen.getAllByRole("menuitem");
    const menu = screen.getByRole("menu");

    fireEvent.keyDown(menu, { key: "ArrowDown" });
    expect(items[1]).toHaveFocus();
    fireEvent.keyDown(menu, { key: "End" });
    expect(items[2]).toHaveFocus();
    fireEvent.keyDown(menu, { key: "ArrowDown" }); // wrap to first
    expect(items[0]).toHaveFocus();
    fireEvent.keyDown(menu, { key: "ArrowUp" }); // wrap to last
    expect(items[2]).toHaveFocus();
    fireEvent.keyDown(menu, { key: "Home" });
    expect(items[0]).toHaveFocus();
  });

  it("Esc closes and returns focus to the trigger (aria-expanded back to false)", () => {
    const trigger = renderMenu();
    fireEvent.click(trigger);
    fireEvent.keyDown(screen.getByRole("menu"), { key: "Escape" });
    expect(screen.queryByRole("menu")).toBeNull();
    expect(trigger).toHaveAttribute("aria-expanded", "false");
    expect(trigger).toHaveFocus();
  });

  it("activating an item fires onSelect, closes, and returns focus to the trigger", () => {
    const dup = vi.fn();
    const trigger = renderMenu(makeItems({ dup }));
    fireEvent.click(trigger);
    fireEvent.click(screen.getByRole("menuitem", { name: "Duplikuj" }));
    expect(dup).toHaveBeenCalledOnce();
    expect(screen.queryByRole("menu")).toBeNull();
    expect(trigger).toHaveFocus();
  });

  it("destructive item carries the destructive styling hook", () => {
    const trigger = renderMenu();
    fireEvent.click(trigger);
    expect(screen.getByRole("menuitem", { name: "Usuń" }).className).toContain("destructive");
  });

  it("disabled item is exposed via aria-disabled and does not fire", () => {
    const onSelect = vi.fn();
    render(
      <Menu
        items={[{ id: "x", label: "Niedostępne", onSelect, disabled: true }]}
        triggerLabel="Akcje"
        triggerContent="⋯"
      />,
    );
    fireEvent.click(screen.getByRole("button", { name: "Akcje" }));
    const item = screen.getByRole("menuitem", { name: "Niedostępne" });
    expect(item).toHaveAttribute("aria-disabled", "true");
    fireEvent.click(item);
    expect(onSelect).not.toHaveBeenCalled();
  });

  it("click outside closes the menu", () => {
    const trigger = renderMenu();
    fireEvent.click(trigger);
    expect(screen.getByRole("menu")).toBeInTheDocument();
    fireEvent.pointerDown(document.body);
    expect(screen.queryByRole("menu")).toBeNull();
  });
});

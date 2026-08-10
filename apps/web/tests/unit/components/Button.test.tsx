// [C] Button catalog spec (T007, slice 019) — variants + states + disabled semantics.
import { fireEvent, render, screen } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import { Button } from "@/components/ui/Button";

describe("Button (catalog)", () => {
  it("renders the primary variant by default with the module classes", () => {
    render(<Button>Zapisz</Button>);
    const btn = screen.getByRole("button", { name: "Zapisz" });
    expect(btn.className).toContain("button");
    expect(btn.className).toContain("primary");
  });

  it("renders secondary and danger variants", () => {
    render(
      <>
        <Button variant="secondary">Anuluj</Button>
        <Button variant="danger">Usuń</Button>
      </>,
    );
    expect(screen.getByRole("button", { name: "Anuluj" }).className).toContain("secondary");
    expect(screen.getByRole("button", { name: "Usuń" }).className).toContain("danger");
  });

  it("defaults type=button so it never submits a form implicitly", () => {
    render(<Button>OK</Button>);
    expect(screen.getByRole("button", { name: "OK" })).toHaveAttribute("type", "button");
  });

  it("disabled: semantic attribute present, click does not fire", () => {
    const onClick = vi.fn();
    render(
      <Button disabled onClick={onClick}>
        Nieaktywny
      </Button>,
    );
    const btn = screen.getByRole("button", { name: "Nieaktywny" });
    expect(btn).toBeDisabled();
    fireEvent.click(btn);
    expect(onClick).not.toHaveBeenCalled();
  });

  it("merges a caller-provided className", () => {
    render(<Button className="extra">X</Button>);
    expect(screen.getByRole("button", { name: "X" }).className).toContain("extra");
  });
});

import { cleanup, fireEvent, render, screen } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";

import { VIEW_FILTER_CLEAR, VIEW_FILTER_LABEL, ViewFilterInput } from "@/components/search/ViewFilterInput";

/**
 * The visible per-view filter control (slice 013, T017 — contracts/ui-palette.md §View filter). [INV-198]
 */

afterEach(cleanup);

describe("ViewFilterInput [INV-198]", () => {
  it("renders a labelled text input and no clear button while empty", () => {
    render(<ViewFilterInput value="" onChange={() => {}} />);
    const input = screen.getByRole("textbox", { name: VIEW_FILTER_LABEL }) as HTMLInputElement;
    expect(input.placeholder).toBe("Filtruj…");
    expect(screen.queryByRole("button", { name: VIEW_FILTER_CLEAR })).toBeNull();
  });

  it("reports typed text and clears through the visible button", () => {
    const onChange = vi.fn();
    render(<ViewFilterInput value="sro" onChange={onChange} />);
    fireEvent.change(screen.getByRole("textbox", { name: VIEW_FILTER_LABEL }), { target: { value: "srod" } });
    expect(onChange).toHaveBeenCalledWith("srod");
    fireEvent.click(screen.getByRole("button", { name: VIEW_FILTER_CLEAR }));
    expect(onChange).toHaveBeenCalledWith("");
  });
});

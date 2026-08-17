import { cleanup, fireEvent, render, screen } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";

const { setCycleDefaultDuration } = vi.hoisted(() => ({
  setCycleDefaultDuration: vi.fn(async () => {}),
}));

vi.mock("@/hooks/useMe", () => ({
  useMe: () => ({
    data: {
      id: "u1",
      email: "user@example.com",
      displayName: "User",
      avatarUrl: null,
      createdAt: "2026-01-01T00:00:00Z",
      cycleDefaultDurationDays: 14,
    },
    isPending: false,
    isError: false,
  }),
  usePreferencesMutation: () => ({ setCycleDefaultDuration }),
}));

import { CycleDurationField } from "@/components/cycles/CycleDurationField";

afterEach(() => {
  cleanup();
  vi.clearAllMocks();
});

/**
 * The /settings FR-015 preference field (slice 011, T021 — D8): a labelled number input
 * „Domyślna długość cyklu (dni)" (1..90) seeded from `GET /api/users/me`, persisted via the
 * preferences mutation (which announces the save politely — FR-101).
 */
describe("CycleDurationField [INV-175]", () => {
  it("renders the labelled number input seeded from the profile with the 1..90 bounds", () => {
    render(<CycleDurationField />);

    const input = screen.getByLabelText("Domyślna długość cyklu (dni)") as HTMLInputElement;
    expect(input.value).toBe("14");
    expect(input.getAttribute("min")).toBe("1");
    expect(input.getAttribute("max")).toBe("90");
  });

  it("persists a changed value through the preferences mutation on save", () => {
    render(<CycleDurationField />);

    fireEvent.change(screen.getByLabelText("Domyślna długość cyklu (dni)"), { target: { value: "21" } });
    fireEvent.click(screen.getByRole("button", { name: "Zapisz" }));

    expect(setCycleDefaultDuration).toHaveBeenCalledWith(21);
  });

  it("refuses an out-of-range value with an inline error — nothing persists (1..90, D8)", () => {
    render(<CycleDurationField />);

    fireEvent.change(screen.getByLabelText("Domyślna długość cyklu (dni)"), { target: { value: "120" } });
    fireEvent.click(screen.getByRole("button", { name: "Zapisz" }));

    expect(setCycleDefaultDuration).not.toHaveBeenCalled();
    expect(screen.getByText(/od 1 do 90/)).toBeTruthy();
  });
});

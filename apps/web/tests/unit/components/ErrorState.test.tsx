// @vitest-environment jsdom
/**
 * [C] ErrorState (S5.3, FR-049): a failure renders as an announced alert with the
 * message and an in-place recovery action.
 */
import { cleanup, render, screen } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";

import { Button } from "@/components/ui/Button";
import { ErrorState } from "@/components/ui/ErrorState";

afterEach(cleanup);

describe("ErrorState (catalog)", () => {
  it("announces the failure via role=alert with the message text", () => {
    render(<ErrorState message="Nie udało się wczytać widoku." />);
    expect(screen.getByRole("alert")).toHaveTextContent("Nie udało się wczytać widoku.");
  });

  it("renders the recovery action inside the alert and it stays operable", async () => {
    const retry = vi.fn();
    render(
      <ErrorState
        message="Nie udało się wczytać widoku."
        action={<Button onClick={retry}>Spróbuj ponownie</Button>}
      />,
    );
    const button = screen.getByRole("button", { name: "Spróbuj ponownie" });
    button.click();
    expect(retry).toHaveBeenCalledTimes(1);
  });

  it("omits the action container when no action is provided", () => {
    render(<ErrorState message="Błąd." />);
    expect(screen.queryByRole("button")).toBeNull();
  });
});

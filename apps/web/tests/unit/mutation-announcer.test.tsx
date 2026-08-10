// @vitest-environment jsdom
/**
 * [C] Global mutation-error announcer (FR-049/FR-050 — [INV-042]): ANY failed mutation's
 * friendly message is announced through the app-wide MutationCache hook into the
 * persistent polite live region — no silent failure — and the FR-050 structured
 * diagnostic record is logged alongside it (T062).
 */
import { cleanup, render, screen, waitFor } from "@testing-library/react";
import { useMutation } from "@tanstack/react-query";
import { afterEach, describe, expect, it, vi } from "vitest";

import { Providers } from "@/app/providers";

afterEach(() => {
  cleanup();
  vi.restoreAllMocks();
});

function FailingMutationProbe() {
  const mutation = useMutation({
    mutationKey: ["task", "rename"],
    mutationFn: async () => {
      throw new Error("Nie udało się zapisać zmiany.");
    },
  });
  return (
    <button type="button" onClick={() => mutation.mutate()}>
      fire
    </button>
  );
}

describe("global MutationCache announcer (FR-049) [INV-042]", () => {
  it("announces the failed mutation's friendly message via the polite status region and logs FR-050", async () => {
    const logSpy = vi.spyOn(console, "error").mockImplementation(() => {});
    render(
      <Providers>
        <FailingMutationProbe />
      </Providers>,
    );

    screen.getByRole("button", { name: "fire" }).click();

    // The persistent role="status" live region receives the message text (FR-101:
    // announced without stealing focus).
    await waitFor(() => {
      const statuses = screen.getAllByRole("status");
      expect(statuses.some((s) => s.textContent?.includes("Nie udało się zapisać zmiany."))).toBe(
        true,
      );
    });

    // The FR-050 structured record went to the diagnostic trail (T062).
    const logged = logSpy.mock.calls.find((c) => c[0] === "[taskflow]");
    expect(logged).toBeTruthy();
    expect(logged![1]).toMatchObject({ severity: "error", operation: "task.rename" });
  });
});

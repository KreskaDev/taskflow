// [C] Toast catalog spec (T014, slice 019) — persistent role="status" region (text
// injected, never visibility-toggled), informational auto-dismiss 4 s (3–5 s tolerance),
// undo-capable variant persisting with explicit close, close never strands focus.
import { act, fireEvent, render, screen } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { ToastProvider, useToast } from "@/components/ui/Toast";

function Trigger({ message, persistent }: { message: string; persistent?: boolean }) {
  const { push } = useToast();
  return (
    <button
      type="button"
      onClick={() => push(message, persistent ? { durationMs: null } : undefined)}
    >
      Pokaż
    </button>
  );
}

describe("Toast (catalog)", () => {
  beforeEach(() => {
    vi.useFakeTimers();
  });
  afterEach(() => {
    vi.useRealTimers();
  });

  it("mounts a persistent status region BEFORE any toast exists; text is injected into it", () => {
    render(
      <ToastProvider>
        <Trigger message="Zapisano" />
      </ToastProvider>,
    );
    const regions = screen.getAllByRole("status");
    expect(regions.length).toBeGreaterThan(0);
    expect(regions.some((r) => r.textContent === "")).toBe(true);

    fireEvent.click(screen.getByRole("button", { name: "Pokaż" }));
    expect(screen.getAllByRole("status").some((r) => r.textContent?.includes("Zapisano"))).toBe(
      true,
    );
  });

  it("informational toast auto-dismisses after ~4 s (within the 3–5 s tolerance)", () => {
    render(
      <ToastProvider>
        <Trigger message="Informacja" />
      </ToastProvider>,
    );
    fireEvent.click(screen.getByRole("button", { name: "Pokaż" }));
    // The text appears in the visual toast AND the persistent announcer region.
    expect(screen.getAllByText("Informacja").length).toBeGreaterThan(0);

    act(() => {
      vi.advanceTimersByTime(2900);
    });
    // Not dismissed before 3 s — the VISUAL toast (inside the aria-hidden node) survives.
    expect(
      screen.getAllByText("Informacja").some((el) => el.closest("[aria-hidden='true']") !== null),
    ).toBe(true);

    act(() => {
      vi.advanceTimersByTime(2200);
    });
    // Gone by 5.1 s — no visual toast remains.
    expect(
      screen.queryAllByText("Informacja").filter((el) => el.closest("[aria-hidden='true']") !== null),
    ).toHaveLength(0);
  });

  it("undo-capable variant (durationMs null) persists past 30 s and offers explicit close", () => {
    render(
      <ToastProvider>
        <Trigger message="Usunięto zadanie" persistent />
      </ToastProvider>,
    );
    fireEvent.click(screen.getByRole("button", { name: "Pokaż" }));
    act(() => {
      vi.advanceTimersByTime(31_000);
    });
    // Persists the full undo window (the visual toast is aria-hidden by the
    // single-announcer design, so role queries must include hidden nodes).
    const close = screen.getByRole("button", { name: "Zamknij powiadomienie", hidden: true });
    expect(close).toBeInTheDocument();

    fireEvent.click(close);
    expect(
      screen
        .queryAllByText("Usunięto zadanie")
        .filter((el) => el.closest("[aria-hidden='true']") !== null),
    ).toHaveLength(0);
  });

  it("closing a toast never leaves focus on a hidden element", () => {
    render(
      <ToastProvider>
        <Trigger message="Chwilowe" persistent />
      </ToastProvider>,
    );
    fireEvent.click(screen.getByRole("button", { name: "Pokaż" }));
    const close = screen.getByRole("button", { name: "Zamknij powiadomienie", hidden: true });
    close.focus();
    fireEvent.click(close);
    const active = document.activeElement as HTMLElement | null;
    expect(active).not.toBeNull();
    expect(document.body.contains(active)).toBe(true);
    expect(active?.closest("[aria-hidden='true']")).toBeNull();
  });
});

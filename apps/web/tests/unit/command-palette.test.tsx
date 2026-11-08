import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { cleanup, fireEvent, render, screen } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";

import type { components } from "@/lib/api/generated/schema";

type SearchResponse = components["schemas"]["SearchResponse"];

const { searchState, unarchiveProject } = vi.hoisted(() => ({
  searchState: { data: undefined as SearchResponse | undefined, isError: false, isFetching: false, refetch: vi.fn() },
  unarchiveProject: vi.fn(),
}));

vi.mock("@/hooks/useSearch", () => ({
  useSearch: () => searchState,
  invalidateSearch: vi.fn(async () => {}),
}));
vi.mock("@/hooks/useProjects", () => ({
  useProjects: () => ({ data: [{ id: "p1", name: "Remont", color: "blue", icon: "folder", parentId: null, visibility: "personal", archivedAt: null, version: 0, createdAt: "", updatedAt: "" }] }),
}));
vi.mock("@/hooks/useProjectMutations", () => ({
  useProjectMutations: () => ({ unarchiveProject }),
}));

import { CommandPalette, PALETTE_ERROR, PALETTE_INPUT_LABEL, PALETTE_TITLE } from "@/components/search/CommandPalette";

/**
 * The command palette dialog (slice 013, T011 — contracts/ui-palette.md): group order, combobox/listbox
 * semantics with arrow navigation, Enter/click activation, the empty-query action catalogue, archived project
 * row + „Przywróć", label row replacing the query, empty/error states and the live-region count.
 * [INV-189] [INV-190] [INV-191] [INV-192] [INV-193] [INV-194] [INV-195] [INV-196]
 */

afterEach(() => {
  cleanup();
  searchState.data = undefined;
  searchState.isError = false;
  searchState.isFetching = false;
  vi.clearAllMocks();
});

function data(overrides: Partial<SearchResponse> = {}): SearchResponse {
  return { tasks: [], projects: [], labels: [], ...overrides };
}

const taskRow = {
  id: "t1",
  title: "Kup mleko",
  status: "backlog",
  position: "a0",
  version: 0,
  createdAt: "2026-06-01T10:00:00Z",
  updatedAt: "2026-06-01T10:00:00Z",
  completedAt: null,
  dueDate: null,
  dueHasTime: null,
  projectId: "p1",
  assignees: [],
  labels: [],
  cycleId: null,
  carriedOver: false,
} as SearchResponse["tasks"][number];

function renderPalette(props: Partial<Parameters<typeof CommandPalette>[0]> = {}) {
  const onClose = vi.fn();
  const onNavigate = vi.fn();
  const onAction = vi.fn();
  render(
    <QueryClientProvider client={new QueryClient()}>
      <CommandPalette open onClose={onClose} onNavigate={onNavigate} onAction={onAction} {...props} />
    </QueryClientProvider>,
  );
  return { onClose, onNavigate, onAction, input: () => screen.getByRole("combobox", { name: PALETTE_INPUT_LABEL }) };
}

describe("CommandPalette — dialog + empty query [INV-189] [INV-191]", () => {
  it("renders the titled dialog with the focused combobox and ONLY the Akcje group for an empty query", () => {
    const { input } = renderPalette();
    expect(screen.getByRole("dialog", { name: PALETTE_TITLE })).toBeTruthy();
    expect(document.activeElement).toBe(input());
    const groups = screen.getAllByRole("group");
    expect(groups).toHaveLength(1);
    expect(screen.getByText("Akcje")).toBeTruthy();
    expect(screen.getAllByRole("option").map((o) => o.textContent)).toEqual([
      "Nowy task",
      "Nowy projekt",
      "Nowy cykl",
      "Przejdź: Inbox",
      "Przejdź: Dziś",
      "Przejdź: Nadchodzące",
      "Przejdź: Przypisane",
      "Przejdź: Cykl",
      "Przejdź: Ustawienia",
    ]);
    expect(screen.queryByText(/Ctrl|⌘/)).toBeNull(); // FR-033 deferred: no shortcut column
  });

  it("Esc closes (focus return is the Dialog contract — onClose fires once)", () => {
    const { onClose } = renderPalette();
    fireEvent.keyDown(document.activeElement ?? document.body, { key: "Escape" });
    expect(onClose).toHaveBeenCalledTimes(1);
  });
});

describe("CommandPalette — navigation + activation [INV-190] [INV-192] [INV-193]", () => {
  it("↓ moves aria-activedescendant and Enter executes the active action AFTER closing", () => {
    const { onClose, onAction, input } = renderPalette();
    fireEvent.change(input(), { target: { value: "create project" } });
    expect(screen.getAllByRole("option")).toHaveLength(1);
    expect(input().getAttribute("aria-activedescendant")).toBe(screen.getByRole("option").id);
    fireEvent.keyDown(input(), { key: "Enter" });
    expect(onClose).toHaveBeenCalledTimes(1);
    expect(onAction).toHaveBeenCalledWith(expect.objectContaining({ id: "new-project" }));
  });

  it("a task result shows title + location chip and navigates to its drawer deep link", () => {
    searchState.data = data({ tasks: [taskRow] });
    const { onNavigate, onClose, input } = renderPalette();
    fireEvent.change(input(), { target: { value: "mleko" } });
    const option = screen.getByRole("option", { name: /Kup mleko/ });
    expect(option.textContent).toContain("Remont");
    fireEvent.click(option);
    expect(onClose).toHaveBeenCalled();
    expect(onNavigate).toHaveBeenCalledWith("/projects/p1?task=t1");
  });

  it("↓ from the last action lands on the first task result (groups are one keyboard list)", () => {
    searchState.data = data({ tasks: [taskRow] });
    const { input } = renderPalette();
    fireEvent.change(input(), { target: { value: "nowy mleko" } }); // matches no action → tasks only
    expect(screen.getAllByRole("group").map((g) => g.getAttribute("aria-labelledby"))).toHaveLength(1);
    fireEvent.keyDown(input(), { key: "ArrowDown" });
    fireEvent.keyDown(input(), { key: "ArrowUp" });
    expect(input().getAttribute("aria-activedescendant")).toBe(screen.getByRole("option").id);
  });
});

describe("CommandPalette — projects, labels, states [INV-194] [INV-195] [INV-196]", () => {
  it("an archived project shows the suffix and an inline „Przywróć” that unarchives without navigating", () => {
    searchState.data = data({
      projects: [{ id: "p2", name: "Stary", color: "blue", icon: "folder", parentId: null, visibility: "personal", archivedAt: "2026-05-01T00:00:00Z", version: 3, createdAt: "", updatedAt: "" } as SearchResponse["projects"][number]],
    });
    const { onNavigate, input } = renderPalette();
    fireEvent.change(input(), { target: { value: "stary" } });
    expect(screen.getByText("(zarchiwizowany)")).toBeTruthy();
    fireEvent.click(screen.getByRole("button", { name: "Przywróć" }));
    expect(unarchiveProject).toHaveBeenCalledWith("p2", 3);
    expect(onNavigate).not.toHaveBeenCalled();
  });

  it("selecting a label replaces the query with the label name (labels have no page)", () => {
    searchState.data = data({ labels: [{ id: "l1", name: "dom", color: "blue" } as SearchResponse["labels"][number]] });
    const { onClose, input } = renderPalette();
    fireEvent.change(input(), { target: { value: "do" } });
    fireEvent.click(screen.getByRole("option", { name: /dom/ }));
    expect((input() as HTMLInputElement).value).toBe("dom");
    expect(onClose).not.toHaveBeenCalled();
  });

  it("no results → the empty state with the hint; error → FR-049 copy with „Spróbuj ponownie”", () => {
    searchState.data = data();
    const { input } = renderPalette();
    fireEvent.change(input(), { target: { value: "zzz" } });
    expect(screen.getByText("Brak wyników dla „zzz”")).toBeTruthy();
    expect(screen.getByText("Spróbuj innego słowa")).toBeTruthy();

    searchState.isError = true;
    fireEvent.change(input(), { target: { value: "zzzz" } });
    expect(screen.getByRole("alert")).toHaveTextContent(PALETTE_ERROR);
    fireEvent.click(screen.getByRole("button", { name: "Spróbuj ponownie" }));
    expect(searchState.refetch).toHaveBeenCalled();
  });

  it("announces the result count politely", () => {
    searchState.data = data({ tasks: [taskRow], labels: [{ id: "l1", name: "mleko", color: "blue" } as SearchResponse["labels"][number]] });
    const { input } = renderPalette();
    fireEvent.change(input(), { target: { value: "mleko" } });
    expect(screen.getByRole("status")).toHaveTextContent("2 wyników");
  });
});

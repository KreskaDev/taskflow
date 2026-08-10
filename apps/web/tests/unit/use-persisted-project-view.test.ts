// @vitest-environment jsdom
import { act, cleanup, renderHook } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it } from "vitest";

import { usePersistedProjectView } from "@/hooks/usePersistedProjectView";

/**
 * Per-project view-mode + group-by persistence (slice 010, T012 — research D8,
 * US-03.AS-02/AS-07). Pins the hook's contract: `localStorage`-backed
 * (`taskflow.project-view.<id>` / `taskflow.project-groupby.<id>`), defaults
 * `list`/`none`, hydration-safe (default first, stored value applied in an effect
 * after mount), invalid stored values fall back to the defaults.
 */

const PROJECT_A = "aaaaaaaa-aaaa-7aaa-8aaa-aaaaaaaaaaaa";
const PROJECT_B = "bbbbbbbb-bbbb-7bbb-8bbb-bbbbbbbbbbbb";

beforeEach(() => localStorage.clear());
afterEach(cleanup);

describe("usePersistedProjectView — per-project mode persistence (D8) [INV-141]", () => {
  it("defaults to 'list' on first visit (no stored value)", () => {
    const { result } = renderHook(() => usePersistedProjectView(PROJECT_A));
    expect(result.current.mode).toBe("list");
  });

  it("re-applies the stored mode after mount (the hydration-safe swap)", () => {
    localStorage.setItem(`taskflow.project-view.${PROJECT_A}`, "board");
    const { result } = renderHook(() => usePersistedProjectView(PROJECT_A));
    expect(result.current.mode).toBe("board");
  });

  it("setMode persists the choice under the project-scoped key", () => {
    const { result } = renderHook(() => usePersistedProjectView(PROJECT_A));
    act(() => result.current.setMode("board"));
    expect(result.current.mode).toBe("board");
    expect(localStorage.getItem(`taskflow.project-view.${PROJECT_A}`)).toBe("board");
  });

  it("persistence is PER PROJECT — a different project still defaults to 'list'", () => {
    localStorage.setItem(`taskflow.project-view.${PROJECT_A}`, "board");
    const { result } = renderHook(() => usePersistedProjectView(PROJECT_B));
    expect(result.current.mode).toBe("list");
  });

  it("an invalid stored value falls back to the default 'list'", () => {
    localStorage.setItem(`taskflow.project-view.${PROJECT_A}`, "kanban3d");
    const { result } = renderHook(() => usePersistedProjectView(PROJECT_A));
    expect(result.current.mode).toBe("list");
  });

  it("switching projectId re-reads the target project's stored mode", () => {
    localStorage.setItem(`taskflow.project-view.${PROJECT_A}`, "board");
    const { result, rerender } = renderHook(({ id }) => usePersistedProjectView(id), {
      initialProps: { id: PROJECT_B },
    });
    expect(result.current.mode).toBe("list");
    rerender({ id: PROJECT_A });
    expect(result.current.mode).toBe("board");
  });
});

describe("usePersistedProjectView — per-project group-by persistence (D9) [INV-148]", () => {
  it("defaults to 'none' on first visit", () => {
    const { result } = renderHook(() => usePersistedProjectView(PROJECT_A));
    expect(result.current.groupBy).toBe("none");
  });

  it("re-applies a stored grouping after mount and keeps it project-scoped", () => {
    localStorage.setItem(`taskflow.project-groupby.${PROJECT_A}`, "status");
    const { result } = renderHook(() => usePersistedProjectView(PROJECT_A));
    expect(result.current.groupBy).toBe("status");

    const other = renderHook(() => usePersistedProjectView(PROJECT_B));
    expect(other.result.current.groupBy).toBe("none");
  });

  it("setGroupBy persists under the project-scoped key", () => {
    const { result } = renderHook(() => usePersistedProjectView(PROJECT_A));
    act(() => result.current.setGroupBy("priority"));
    expect(result.current.groupBy).toBe("priority");
    expect(localStorage.getItem(`taskflow.project-groupby.${PROJECT_A}`)).toBe("priority");
  });

  it("an invalid stored grouping falls back to 'none'", () => {
    localStorage.setItem(`taskflow.project-groupby.${PROJECT_A}`, "cycle");
    const { result } = renderHook(() => usePersistedProjectView(PROJECT_A));
    expect(result.current.groupBy).toBe("none");
  });
});

import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { cleanup, render, screen } from "@testing-library/react";
import { createElement } from "react";
import { afterEach, describe, expect, it } from "vitest";

import { buildMenuItems, TaskRow } from "@/components/tasks/TaskRow";
import type { TaskResponse } from "@/hooks/useTasks";

/**
 * Component coverage for TaskRow's due-date label (T018) and its a11y-name composition.
 *
 * The slice-002 listbox fixtures carry no `dueDate`, so the due branch — and the bug where
 * the bare trailing date joined the option's accessible name with no qualifier — was
 * previously invisible to tests. These cases pin:
 *   1. FR-043 — a due-bearing option's accessible name carries a "termin:" qualifier before
 *      the date (never a bare trailing number), while the decorative ✓/○ glyph stays out.
 *   2. FR-046 — the date renders as visible text (not hover-only).
 *   3. R9 — a date-only `dueDate` (midnight Warsaw → UTC instant) recovers the correct
 *      Warsaw calendar day for display rather than landing a day early, in summer AND winter
 *      (proving Warsaw-zone recovery, not a fixed-offset slip).
 *   4. The date-only vs date-time format split keyed on `dueHasTime`.
 */

function makeTask(overrides: Partial<TaskResponse> = {}): TaskResponse {
  return {
    id: "11111111-1111-1111-1111-111111111111",
    title: "Kupic mleko",
    status: "todo",
    position: "a0",
    version: 1,
    createdAt: "2026-06-21T10:00:00Z",
    updatedAt: "2026-06-21T10:00:00Z",
    completedAt: null,
    dueDate: null,
    dueHasTime: null,
    assignees: [],
    labels: [],
    ...overrides,
  };
}

function renderRow(task: TaskResponse) {
  // TaskRow renders LabelChips, which reads the ['labels'] roster via TanStack Query — wrap in a client.
  const queryClient = new QueryClient();
  return render(
    createElement(
      QueryClientProvider,
      { client: queryClient },
      <TaskRow
        task={task}
        selected={false}
        isRenaming={false}
        onCommitRename={() => {}}
        onCancelRename={() => {}}
        style={{}}
      />,
    ),
  );
}

afterEach(cleanup);

describe("TaskRow due-date label [INV-027]", () => {
  it("a dateless row's accessible name is exactly the title (no qualifier, no glyph)", () => {
    renderRow(makeTask({ dueDate: null, dueHasTime: null }));
    expect(screen.getByRole("option", { name: "Kupic mleko" })).toBeTruthy();
  });

  it("a due-bearing row's accessible name carries a 'termin:' qualifier before the date", () => {
    // Date-only summer instant: midnight 2026-06-22 Warsaw (CEST, +02:00) === 22:00:00Z on
    // 2026-06-21. The displayed calendar day MUST be the 22nd (R9), never the 21st.
    renderRow(
      makeTask({ dueDate: "2026-06-21T22:00:00Z", dueHasTime: false }),
    );
    // Teeth: under the pre-fix code the name was "Kupic mleko 22.06.2026" (no qualifier),
    // so this regex would not match. The decorative glyph is aria-hidden and excluded.
    expect(
      screen.getByRole("option", { name: /Kupic mleko termin: 22\.06\.2026/ }),
    ).toBeTruthy();
  });

  it("renders the date-only label on the correct Warsaw day in summer (CEST, R9)", () => {
    renderRow(
      makeTask({ dueDate: "2026-06-21T22:00:00Z", dueHasTime: false }),
    );
    // Visible date text (FR-046). Regex because the sr-only "termin:" sibling means no
    // single node's text equals the date exactly.
    expect(screen.getByText(/22\.06\.2026/)).toBeTruthy();
    expect(screen.queryByText(/21\.06\.2026/)).toBeNull();
  });

  it("renders the date-only label on the correct Warsaw day in winter (CET, no fixed-offset slip)", () => {
    // Midnight 2026-01-15 Warsaw (CET, +01:00) === 23:00:00Z on 2026-01-14. A fixed +02:00
    // offset would slip to the 14th — this proves true Warsaw-zone recovery.
    renderRow(
      makeTask({ dueDate: "2026-01-14T23:00:00Z", dueHasTime: false }),
    );
    expect(screen.getByText(/15\.01\.2026/)).toBeTruthy();
    expect(screen.queryByText(/14\.01\.2026/)).toBeNull();
  });

  it("includes the clock time only when dueHasTime is true (format split)", () => {
    // 2026-06-21T15:00:00Z === 17:00 Warsaw (CEST). With time the label is dd.MM.yyyy HH:mm.
    renderRow(
      makeTask({ dueDate: "2026-06-21T15:00:00Z", dueHasTime: true }),
    );
    expect(screen.getByText(/21\.06\.2026 17:00/)).toBeTruthy();
  });

  it("omits the clock time for a date-only due (no HH:mm)", () => {
    renderRow(
      makeTask({ dueDate: "2026-06-21T22:00:00Z", dueHasTime: false }),
    );
    const due = screen.getByText(/22\.06\.2026/);
    expect(due.textContent).not.toMatch(/\d{2}:\d{2}/);
  });
});

describe("buildMenuItems — the shared „⋯” action architecture gains column moves (slice 010, D7) [INV-145]", () => {
  const noop = () => {};
  /** The full pre-010 standard action set, all wired. */
  function allStandardActions() {
    return {
      onToggleDone: noop,
      onEdit: noop,
      onOpenPriority: noop,
      onOpenReschedule: noop,
      onOpenLabels: noop,
      onOpenMove: noop,
      onOpenAssign: noop,
      onDuplicate: noop,
      onOpenDetails: noop,
      onMoveUp: noop,
      onMoveDown: noop,
      onDelete: noop,
    };
  }

  it("renders „Przenieś w lewo” / „Przenieś w prawo” when both move callbacks are wired", () => {
    const items = buildMenuItems(makeTask(), { ...allStandardActions(), onMoveLeft: noop, onMoveRight: noop });

    const ids = items.map((i) => i.id);
    expect(ids).toContain("move-left");
    expect(ids).toContain("move-right");
  });

  it("OMITS (never disables) the boundary move — Zrobione has no right item, Backlog no left (AS-05)", () => {
    // The caller maps presence via adjacentStatus: at a boundary the callback is simply absent.
    const rightOnly = buildMenuItems(makeTask({ status: "backlog" }), { ...allStandardActions(), onMoveRight: noop });
    expect(rightOnly.map((i) => i.id)).not.toContain("move-left");
    expect(rightOnly.map((i) => i.id)).toContain("move-right");
    expect(rightOnly.every((i) => i.disabled !== true)).toBe(true);

    const leftOnly = buildMenuItems(makeTask({ status: "done" }), { ...allStandardActions(), onMoveLeft: noop });
    expect(leftOnly.map((i) => i.id)).not.toContain("move-right");
    expect(leftOnly.map((i) => i.id)).toContain("move-left");
  });

  it("keeps the FULL standard action set intact alongside the move items (FR-108)", () => {
    const items = buildMenuItems(makeTask(), { ...allStandardActions(), onMoveLeft: noop, onMoveRight: noop });

    const ids = items.map((i) => i.id);
    for (const id of [
      "toggle", "edit", "priority", "due", "labels", "move", "assign",
      "duplicate", "details", "move-up", "move-down", "delete",
    ]) {
      expect(ids).toContain(id);
    }
  });

  it("renders no move items when neither callback is wired (viewer read-only board posture)", () => {
    const items = buildMenuItems(makeTask(), allStandardActions());

    const ids = items.map((i) => i.id);
    expect(ids).not.toContain("move-left");
    expect(ids).not.toContain("move-right");
  });
});

import { cleanup, render, screen, within } from "@testing-library/react";
import { createElement } from "react";
import { afterEach, describe, expect, it, vi } from "vitest";

import { CommentThread } from "@/components/tasks/CommentThread";
import type { CommentResponse } from "@/hooks/useComments";
import { formatInReferenceZone } from "@/lib/timezone";

/**
 * Component coverage for the comment thread surface (T036, RED — covers T037/T038; slice 009, AS-03,
 * FR-046/FR-068, R10/R14/R15). Pins:
 *   1. AS-03 — a VIEWER sees the full thread but NO composer (gated on the read-model role; the server
 *      stays authoritative — disabling this gate still yields a server 403).
 *   2. The author's edit/delete affordances are `canEdit`-gated (UI convenience only, FR-068) and are
 *      real buttons (keyboard-reachable, not hover-only — FR-046).
 *   3. Timestamps render against Europe/Warsaw via the EXISTING `timezone.ts` util (R10 — no new date
 *      library): the absolute Warsaw wall-clock rides on the row so the displayed time is zone-correct.
 *   4. The "edited" affordance shows exactly when `editedAt` is set (FR-073).
 *   5. The body renders through the safeMarkdown boundary (the safe subset renders as elements).
 */

const TASK_ID = "aaaaaaaa-aaaa-7aaa-8aaa-aaaaaaaaaaaa";

function makeComment(overrides: Partial<CommentResponse> & Pick<CommentResponse, "id">): CommentResponse {
  return {
    taskId: TASK_ID,
    authorId: "bbbbbbbb-bbbb-7bbb-8bbb-bbbbbbbbbbbb",
    authorDisplayName: "Editor E",
    body: "A **bold** remark",
    mentions: [],
    createdAt: "2026-07-01T10:00:00.000Z",
    editedAt: null,
    canEdit: false,
    ...overrides,
  };
}

function renderThread(
  comments: CommentResponse[],
  role: string | null,
  handlers: Partial<{
    onPost: (body: string, mentions: unknown[]) => void;
    onEdit: (id: string, body: string, mentions: unknown[]) => void;
    onDelete: (id: string) => void;
  }> = {},
) {
  return render(
    createElement(CommentThread, {
      taskId: TASK_ID,
      comments,
      role,
      members: [],
      onPost: handlers.onPost ?? vi.fn(),
      onEdit: handlers.onEdit ?? vi.fn(),
      onDelete: handlers.onDelete ?? vi.fn(),
    }),
  );
}

afterEach(cleanup);

describe("CommentThread — AS-03 viewer read-only gating [INV-113] [INV-115] [INV-116]", () => {
  it("a viewer sees the full thread but NO composer", () => {
    renderThread([makeComment({ id: "11111111-1111-7111-8111-111111111111" })], "viewer");

    expect(screen.getByText("Editor E")).toBeTruthy();
    expect(screen.queryByRole("textbox")).toBeNull();
  });

  it("an editor sees the composer", () => {
    renderThread([], "editor");
    expect(screen.getByRole("textbox")).toBeTruthy();
  });

  it("an owner sees the composer", () => {
    renderThread([], "owner");
    expect(screen.getByRole("textbox")).toBeTruthy();
  });
});

describe("CommentItem — canEdit-gated affordances (FR-068/FR-046)", () => {
  it("the author's own comment carries keyboard-reachable edit + delete buttons", () => {
    renderThread([makeComment({ id: "11111111-1111-7111-8111-111111111111", canEdit: true })], "editor");

    expect(screen.getByRole("button", { name: /edytuj/i })).toBeTruthy();
    expect(screen.getByRole("button", { name: /usuń/i })).toBeTruthy();
  });

  it("someone else's comment carries no edit/delete affordance", () => {
    renderThread([makeComment({ id: "11111111-1111-7111-8111-111111111111", canEdit: false })], "editor");

    expect(screen.queryByRole("button", { name: /edytuj/i })).toBeNull();
    expect(screen.queryByRole("button", { name: /usuń/i })).toBeNull();
  });
});

describe("CommentItem — Warsaw-referenced time (R10) + the edited affordance", () => {
  it("the timestamp carries the absolute Europe/Warsaw wall-clock from timezone.ts", () => {
    // 2026-07-01T10:00:00Z is 12:00 Warsaw (CEST, +02:00) — the displayed absolute time must be
    // the Warsaw wall-clock, proving the render rides timezone.ts, not the raw UTC instant.
    renderThread([makeComment({ id: "11111111-1111-7111-8111-111111111111" })], "viewer");

    const expected = formatInReferenceZone(new Date("2026-07-01T10:00:00.000Z"), "dd.MM.yyyy HH:mm");
    expect(expected).toContain("12:00");
    const time = screen.getByTitle(expected);
    expect(time).toBeTruthy();
  });

  it("shows the edited affordance exactly when editedAt is set", () => {
    renderThread(
      [
        makeComment({ id: "11111111-1111-7111-8111-111111111111", editedAt: null }),
        makeComment({
          id: "22222222-2222-7222-8222-222222222222",
          editedAt: "2026-07-01T11:00:00.000Z",
        }),
      ],
      "viewer",
    );

    const items = screen.getAllByRole("article");
    expect(within(items[0]!).queryByText(/edytowano/i)).toBeNull();
    expect(within(items[1]!).getByText(/edytowano/i)).toBeTruthy();
  });
});

describe("CommentItem — the body renders through the safeMarkdown boundary", () => {
  it("renders the safe markdown subset as elements", () => {
    const { container } = renderThread(
      [makeComment({ id: "11111111-1111-7111-8111-111111111111", body: "A **bold** remark" })],
      "viewer",
    );
    expect(container.querySelector("strong")?.textContent).toBe("bold");
  });

  it("renders the typed mention tokens as chips (never scraped from prose — R6)", () => {
    renderThread(
      [
        makeComment({
          id: "11111111-1111-7111-8111-111111111111",
          mentions: [{ userId: "cccccccc-cccc-7ccc-8ccc-cccccccccccc", displayName: "Viewer V" }],
        }),
      ],
      "viewer",
    );
    expect(screen.getByText("@Viewer V")).toBeTruthy();
  });
});

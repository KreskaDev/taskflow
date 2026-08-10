// @vitest-environment node
import { describe, expect, it } from "vitest";

import {
  commentSchema,
  MAX_COMMENT_LENGTH,
  MAX_COMMENT_MENTIONS,
} from "@/lib/validation/comment";

/**
 * Comment-payload validation (T029, RED — covers T030; slice 009, FR-098, Constitution VI "Zod at every
 * trust boundary"). Mirrors the server-side `PostCommentValidator`/`EditCommentValidator` pair: `body` is
 * trimmed, must be non-empty after trimming, and may be at most `MAX_COMMENT_LENGTH` (4000 — the frozen
 * T002 surface); `mentionedUserIds` is a bounded uuid array (the TYPED @mention token set, R6 — never
 * scraped from prose) defaulting to empty. The mention-candidacy (current-members-only) check is a
 * server-side cross-row rule — the client picker only offers members, so the schema stays shape-only.
 */
describe("commentSchema [INV-112]", () => {
  const viewer = "33333333-3333-7333-8333-333333333333";

  it("freezes the T002 content-safety surface at 4000", () => {
    expect(MAX_COMMENT_LENGTH).toBe(4000);
  });

  it("accepts a plain body and defaults the mention set to empty", () => {
    const parsed = commentSchema.parse({ body: "Looks good to me" });
    expect(parsed.body).toBe("Looks good to me");
    expect(parsed.mentionedUserIds).toEqual([]);
  });

  it("trims the body before validating", () => {
    const parsed = commentSchema.parse({ body: "  padded  " });
    expect(parsed.body).toBe("padded");
  });

  it("rejects an empty body", () => {
    expect(() => commentSchema.parse({ body: "" })).toThrow();
  });

  it("rejects a whitespace-only body (trim → empty)", () => {
    expect(() => commentSchema.parse({ body: "   \n\t " })).toThrow();
  });

  it("accepts a body of exactly MAX_COMMENT_LENGTH and rejects one char over", () => {
    expect(commentSchema.parse({ body: "x".repeat(MAX_COMMENT_LENGTH) }).body).toHaveLength(
      MAX_COMMENT_LENGTH,
    );
    expect(() => commentSchema.parse({ body: "x".repeat(MAX_COMMENT_LENGTH + 1) })).toThrow();
  });

  it("accepts a typed uuid mention set", () => {
    const parsed = commentSchema.parse({ body: "ping", mentionedUserIds: [viewer] });
    expect(parsed.mentionedUserIds).toEqual([viewer]);
  });

  it("rejects a non-uuid mention token (the typed-token shape, R6)", () => {
    expect(() =>
      commentSchema.parse({ body: "ping", mentionedUserIds: ["@viewer-from-prose"] }),
    ).toThrow();
  });

  it("bounds the mention set", () => {
    const tooMany = Array.from({ length: MAX_COMMENT_MENTIONS + 1 }, (_, i) =>
      `00000000-0000-7000-8000-${String(i).padStart(12, "0")}`,
    );
    expect(() => commentSchema.parse({ body: "ping", mentionedUserIds: tooMany })).toThrow();
  });
});

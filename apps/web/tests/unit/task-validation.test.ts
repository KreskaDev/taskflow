// @vitest-environment node
import { describe, expect, it } from "vitest";
import { taskTitleSchema } from "@/lib/validation/task";

/**
 * Task title validation (T024/T025, Constitution VI "Zod at every trust boundary").
 * Mirrors the server FluentValidation rule: a title is trimmed, must be non-empty,
 * and may be at most 500 characters long.
 */
describe("task title validation", () => {
  it("rejects a title that is empty after trimming", () => {
    expect(taskTitleSchema.safeParse("   ").success).toBe(false);
  });

  it("accepts a 500-character title", () => {
    const title = "a".repeat(500);
    expect(taskTitleSchema.safeParse(title).success).toBe(true);
  });

  it("rejects a 501-character title", () => {
    const title = "a".repeat(501);
    expect(taskTitleSchema.safeParse(title).success).toBe(false);
  });
});

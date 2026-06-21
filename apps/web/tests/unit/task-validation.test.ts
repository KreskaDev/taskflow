// @vitest-environment node
import { describe, expect, it } from "vitest";
import { createTaskSchema, taskTitleSchema } from "@/lib/validation/task";

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

/**
 * Create-payload validation (T013/T014, research R8 pairing invariant).
 * `createTaskSchema` mirrors the server pairing rule: `dueDate` and `dueHasTime`
 * are both present (a due date) or both absent (a dateless task) — never one without
 * the other (`dueHasTime` is meaningless without a date; a date without `dueHasTime`
 * is ambiguous). The resolved `dueDate` is a `Date` (the parser's resolved UTC instant).
 */
describe("create task validation (dueDate/dueHasTime pairing)", () => {
  it("accepts both present (a due date)", () => {
    const result = createTaskSchema.safeParse({
      title: "Kupic mleko",
      dueDate: new Date("2026-06-21T15:00:00Z"),
      dueHasTime: true,
    });
    expect(result.success).toBe(true);
  });

  it("accepts both absent (a dateless task)", () => {
    const result = createTaskSchema.safeParse({ title: "Kupic mleko" });
    expect(result.success).toBe(true);
  });

  it("rejects dueDate set without dueHasTime", () => {
    const result = createTaskSchema.safeParse({
      title: "Kupic mleko",
      dueDate: new Date("2026-06-21T15:00:00Z"),
    });
    expect(result.success).toBe(false);
  });

  it("rejects dueHasTime set without dueDate", () => {
    const result = createTaskSchema.safeParse({
      title: "Kupic mleko",
      dueHasTime: true,
    });
    expect(result.success).toBe(false);
  });
});

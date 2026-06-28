// @vitest-environment node
import { describe, expect, it } from "vitest";
import {
  createTaskSchema,
  editTaskSchema,
  prioritySchema,
  taskTitleSchema,
} from "@/lib/validation/task";

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

/**
 * Priority + editor-form validation (slice 005, T020; Constitution VI). Mirrors the server closed-set
 * priority rule, the ≤ 8000 description bound, and the slice-003 due-date pairing invariant.
 */
describe("task priority validation", () => {
  it.each(["P0", "P1", "P2", "P3"])("accepts the closed-set token %s", (token) => {
    expect(prioritySchema.safeParse(token).success).toBe(true);
  });

  it("accepts null (unprioritized)", () => {
    expect(prioritySchema.safeParse(null).success).toBe(true);
  });

  it.each(["p0", "P4", "high", ""])("rejects the out-of-set value %s", (value) => {
    expect(prioritySchema.safeParse(value).success).toBe(false);
  });
});

describe("task editor-form validation", () => {
  const valid = {
    title: "Edited",
    description: "details",
    priority: "P1" as const,
    dueDate: new Date("2026-07-01T09:00:00Z"),
    dueHasTime: true,
    projectId: null,
  };

  it("accepts a fully-populated whole-object replace", () => {
    expect(editTaskSchema.safeParse(valid).success).toBe(true);
  });

  it("accepts the nullable fields as null", () => {
    expect(
      editTaskSchema.safeParse({
        title: "Edited",
        description: null,
        priority: null,
        dueDate: null,
        dueHasTime: null,
        projectId: null,
      }).success,
    ).toBe(true);
  });

  it("rejects a description longer than 8000 chars", () => {
    expect(editTaskSchema.safeParse({ ...valid, description: "x".repeat(8001) }).success).toBe(false);
  });

  it("rejects an out-of-set priority", () => {
    expect(editTaskSchema.safeParse({ ...valid, priority: "P9" }).success).toBe(false);
  });

  it("rejects a half-set due pair (date without flag)", () => {
    expect(editTaskSchema.safeParse({ ...valid, dueHasTime: null }).success).toBe(false);
  });

  it("rejects a half-set due pair (flag without date)", () => {
    expect(editTaskSchema.safeParse({ ...valid, dueDate: null }).success).toBe(false);
  });
});

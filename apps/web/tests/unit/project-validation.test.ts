// @vitest-environment node
import { describe, expect, it } from "vitest";
import { PROJECT_COLORS, PROJECT_ICONS } from "@/lib/projectPresets";
import {
  childDispositionSchema,
  createProjectSchema,
  editProjectSchema,
  taskDispositionSchema,
} from "@/lib/validation/project";

/**
 * Project validation (T023, RED — drives T024; Constitution VI "Zod at every trust boundary",
 * research R4/R10). Mirrors the server-side FluentValidation rules + the OpenAPI contract shapes:
 *   - `name` trimmed, non-empty, ≤ 200 chars (API `MaxNameLength = 200` — NOT the task 500).
 *   - `color`/`icon` are the FROZEN preset ENUMS (ASM-04, R10) — a value outside the set fails.
 *   - `parentId` is OPTIONAL on create (absent = top-level) but REQUIRED-BUT-NULLABLE on edit
 *     (R4 whole-object replace: a name-only edit MUST re-send the current parent, never silently
 *     un-parent — an OMITTED `parentId` on edit is a failure, an explicit `null` is top-level).
 *   - the disposition enums (R5): task = cascade | move_to_inbox | archive_with_tasks,
 *     child = cascade | orphan_to_top.
 */

const VALID_PARENT = "11111111-1111-7111-8111-111111111111";

describe("createProjectSchema — name bounds", () => {
  it("rejects a name that is empty after trimming", () => {
    const result = createProjectSchema.safeParse({ name: "   ", color: "blue", icon: "folder" });
    expect(result.success).toBe(false);
  });

  it("accepts a 200-character name", () => {
    const result = createProjectSchema.safeParse({
      name: "a".repeat(200),
      color: "blue",
      icon: "folder",
    });
    expect(result.success).toBe(true);
  });

  it("rejects a 201-character name", () => {
    const result = createProjectSchema.safeParse({
      name: "a".repeat(201),
      color: "blue",
      icon: "folder",
    });
    expect(result.success).toBe(false);
  });
});

describe("createProjectSchema — color/icon preset enums (R10/ASM-04)", () => {
  it("accepts every frozen preset color", () => {
    for (const color of PROJECT_COLORS) {
      expect(createProjectSchema.safeParse({ name: "P", color, icon: "folder" }).success).toBe(true);
    }
  });

  it("accepts every frozen preset icon", () => {
    for (const icon of PROJECT_ICONS) {
      expect(createProjectSchema.safeParse({ name: "P", color: "blue", icon }).success).toBe(true);
    }
  });

  it("rejects a free-form color outside the preset set", () => {
    const result = createProjectSchema.safeParse({ name: "P", color: "#ff0000", icon: "folder" });
    expect(result.success).toBe(false);
  });

  it("rejects a free-form icon outside the preset set", () => {
    const result = createProjectSchema.safeParse({ name: "P", color: "blue", icon: "skull" });
    expect(result.success).toBe(false);
  });
});

describe("createProjectSchema — parentId OPTIONAL on create", () => {
  it("accepts an absent parentId (a top-level project)", () => {
    const result = createProjectSchema.safeParse({ name: "P", color: "blue", icon: "folder" });
    expect(result.success).toBe(true);
  });

  it("accepts an explicit uuid parentId (a child project)", () => {
    const result = createProjectSchema.safeParse({
      name: "P",
      color: "blue",
      icon: "folder",
      parentId: VALID_PARENT,
    });
    expect(result.success).toBe(true);
  });

  it("rejects a non-uuid parentId", () => {
    const result = createProjectSchema.safeParse({
      name: "P",
      color: "blue",
      icon: "folder",
      parentId: "not-a-uuid",
    });
    expect(result.success).toBe(false);
  });
});

describe("editProjectSchema — parentId REQUIRED-but-nullable (R4 whole-object replace)", () => {
  it("FAILS when parentId is OMITTED (the silent-un-parent footgun the contract forbids)", () => {
    // Teeth: `.optional()`/`.nullish()` here would PASS this and silently un-parent on a
    // name-only edit. The key MUST be present (`.nullable()`), so an omitted parentId is a failure.
    const result = editProjectSchema.safeParse({ name: "P", color: "blue", icon: "folder", version: 3 });
    expect(result.success).toBe(false);
  });

  it("PASSES with an explicit parentId: null (re-sent top-level)", () => {
    const result = editProjectSchema.safeParse({
      name: "P",
      color: "blue",
      icon: "folder",
      parentId: null,
      version: 3,
    });
    expect(result.success).toBe(true);
  });

  it("PASSES with an explicit uuid parentId (re-parent / re-sent parent)", () => {
    const result = editProjectSchema.safeParse({
      name: "P",
      color: "blue",
      icon: "folder",
      parentId: VALID_PARENT,
      version: 3,
    });
    expect(result.success).toBe(true);
  });

  it("requires a numeric version (optimistic concurrency token)", () => {
    const result = editProjectSchema.safeParse({
      name: "P",
      color: "blue",
      icon: "folder",
      parentId: null,
    });
    expect(result.success).toBe(false);
  });
});

describe("disposition enums (R5)", () => {
  it("taskDisposition accepts cascade | move_to_inbox | archive_with_tasks", () => {
    expect(taskDispositionSchema.safeParse("cascade").success).toBe(true);
    expect(taskDispositionSchema.safeParse("move_to_inbox").success).toBe(true);
    expect(taskDispositionSchema.safeParse("archive_with_tasks").success).toBe(true);
  });

  it("taskDisposition rejects an unknown token", () => {
    expect(taskDispositionSchema.safeParse("orphan_to_top").success).toBe(false);
    expect(taskDispositionSchema.safeParse("delete_everything").success).toBe(false);
  });

  it("childDisposition accepts cascade | orphan_to_top", () => {
    expect(childDispositionSchema.safeParse("cascade").success).toBe(true);
    expect(childDispositionSchema.safeParse("orphan_to_top").success).toBe(true);
  });

  it("childDisposition rejects an unknown token", () => {
    expect(childDispositionSchema.safeParse("move_to_inbox").success).toBe(false);
  });
});

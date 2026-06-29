// @vitest-environment node
import { describe, expect, it } from "vitest";

import {
  labelColorSchema,
  labelNameSchema,
  labelSetSchema,
  MAX_LABEL_NAME_LENGTH,
  MAX_LABELS_PER_TASK,
} from "@/lib/validation/label";

describe("labelNameSchema", () => {
  it("trims and accepts a non-empty name", () => {
    expect(labelNameSchema.parse("  Urgent  ")).toBe("Urgent");
  });

  it("rejects an empty / whitespace-only name", () => {
    expect(labelNameSchema.safeParse("   ").success).toBe(false);
  });

  it("rejects a name longer than the max", () => {
    expect(labelNameSchema.safeParse("x".repeat(MAX_LABEL_NAME_LENGTH + 1)).success).toBe(false);
  });
});

describe("labelColorSchema", () => {
  it("accepts a preset token string", () => {
    expect(labelColorSchema.parse("red")).toBe("red");
  });

  it("accepts null (optional color)", () => {
    expect(labelColorSchema.parse(null)).toBeNull();
  });
});

describe("labelSetSchema", () => {
  const uuid = "11111111-1111-1111-1111-111111111111";

  it("accepts a set of uuids", () => {
    expect(labelSetSchema.parse([uuid])).toEqual([uuid]);
  });

  it("accepts an empty set (clears the caller's labels)", () => {
    expect(labelSetSchema.parse([])).toEqual([]);
  });

  it("rejects duplicates", () => {
    expect(labelSetSchema.safeParse([uuid, uuid]).success).toBe(false);
  });

  it("rejects a non-uuid id", () => {
    expect(labelSetSchema.safeParse(["not-a-uuid"]).success).toBe(false);
  });

  it("rejects more than the max", () => {
    const ids = Array.from({ length: MAX_LABELS_PER_TASK + 1 }, (_, i) =>
      `${i.toString(16).padStart(8, "0")}-1111-1111-1111-111111111111`,
    );
    expect(labelSetSchema.safeParse(ids).success).toBe(false);
  });
});

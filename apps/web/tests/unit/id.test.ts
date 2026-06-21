// @vitest-environment node
import { describe, expect, it } from "vitest";
import { newTaskId } from "@/lib/id";

/**
 * Client-side task id minter (T020/T021). Task ids are UUIDv7: time-ordered and
 * client-mintable so an optimistic capture carries its own stable identity. The
 * version nibble MUST be 7 — the 13th hex character of the canonical
 * 8-4-4-4-12 string (first character of the third group). This guards against a
 * regression to crypto.randomUUID(), which emits a v4 (version nibble 4).
 */
describe("newTaskId", () => {
  // 8-4-4-4-12 canonical UUID, with the version nibble pinned to 7.
  const UUID_V7 = /^[0-9a-f]{8}-[0-9a-f]{4}-7[0-9a-f]{3}-[0-9a-f]{4}-[0-9a-f]{12}$/;

  it("returns a canonical UUIDv7 string (version nibble === 7)", () => {
    const id = newTaskId();
    expect(id).toMatch(UUID_V7);
    // The 13th character of the canonical string is the version nibble.
    expect(id.replace(/-/g, "")[12]).toBe("7");
  });

  it("mints a distinct id on each call", () => {
    expect(newTaskId()).not.toBe(newTaskId());
  });
});

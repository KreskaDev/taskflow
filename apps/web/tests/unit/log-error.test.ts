// @vitest-environment node
/**
 * logError (T062 — FR-050): every surfaced failure logs one structured record with
 * severity, operation, and details.
 */
import { afterEach, describe, expect, it, vi } from "vitest";

import { logError } from "@/lib/logError";

afterEach(() => {
  vi.restoreAllMocks();
});

describe("logError (FR-050 structured logging)", () => {
  it("logs error severity via console.error with the full structured shape", () => {
    const spy = vi.spyOn(console, "error").mockImplementation(() => {});
    logError({
      severity: "error",
      operation: "task.rename",
      error: new Error("boom"),
      details: { taskId: "t-1" },
    });
    expect(spy).toHaveBeenCalledTimes(1);
    const [tag, record] = spy.mock.calls[0]! as [string, Record<string, unknown>];
    expect(tag).toBe("[taskflow]");
    expect(record).toMatchObject({
      severity: "error",
      operation: "task.rename",
      details: { taskId: "t-1" },
    });
    expect(record.error).toMatchObject({ name: "Error", message: "boom" });
    expect(typeof record.at).toBe("string");
  });

  it("routes warning severity through console.warn", () => {
    const warn = vi.spyOn(console, "warn").mockImplementation(() => {});
    const error = vi.spyOn(console, "error").mockImplementation(() => {});
    logError({ severity: "warning", operation: "views.counts.fetch", error: "stale" });
    expect(warn).toHaveBeenCalledTimes(1);
    expect(error).not.toHaveBeenCalled();
  });

  it("serializes non-Error payloads without throwing", () => {
    const spy = vi.spyOn(console, "error").mockImplementation(() => {});
    logError({ severity: "critical", operation: "app.boot", error: { code: 500, body: { x: 1 } } });
    const [, record] = spy.mock.calls[0]! as [string, Record<string, unknown>];
    expect(record.error).toEqual({ code: 500, body: { x: 1 } });
  });
});

// @vitest-environment node
/**
 * [C] CSRF gate (slice 001 `isCsrfSafe`, tagged for the slice-019 inventory —
 * [INV-135], Principle XII): state-changing requests without a same-origin
 * Origin/Referer are rejected; UI flows (same-origin) always pass; safe methods
 * bypass the gate entirely.
 */
import { afterEach, beforeEach, describe, expect, it } from "vitest";

import { isCsrfSafe } from "@/lib/auth/csrf";

const APP = "http://localhost:3000";

function req(method: string, headers: Record<string, string> = {}): Request {
  return new Request(`${APP}/api/auth/signout`, { method, headers });
}

describe("isCsrfSafe — the BFF CSRF gate (FR-089) [INV-135]", () => {
  beforeEach(() => {
    process.env.APP_URL = APP;
  });
  afterEach(() => {
    delete process.env.APP_URL;
  });

  it("lets safe methods through without any Origin/Referer", () => {
    expect(isCsrfSafe(req("GET"))).toBe(true);
    expect(isCsrfSafe(req("HEAD"))).toBe(true);
    expect(isCsrfSafe(req("OPTIONS"))).toBe(true);
  });

  it("accepts a state-changing request with the same-origin Origin (the UI flow)", () => {
    expect(isCsrfSafe(req("POST", { origin: APP }))).toBe(true);
  });

  it("accepts the Referer fallback when Origin is absent but same-origin", () => {
    expect(isCsrfSafe(req("POST", { referer: `${APP}/settings` }))).toBe(true);
  });

  it("rejects a cross-origin Origin", () => {
    expect(isCsrfSafe(req("POST", { origin: "https://evil.example" }))).toBe(false);
  });

  it("rejects a cross-origin Referer fallback and a malformed Referer", () => {
    expect(isCsrfSafe(req("DELETE", { referer: "https://evil.example/x" }))).toBe(false);
    expect(isCsrfSafe(req("DELETE", { referer: "not-a-url" }))).toBe(false);
  });

  it("rejects a state-changing request with NEITHER header", () => {
    expect(isCsrfSafe(req("POST"))).toBe(false);
  });
});

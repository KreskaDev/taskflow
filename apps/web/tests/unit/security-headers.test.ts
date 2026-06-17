// @vitest-environment node
import { afterEach, describe, expect, it, vi } from "vitest";
import nextConfig from "../../next.config";

/**
 * Regression lock for the BFF security-header baseline (FR-099, Constitution XII). The CSP must be
 * present on BFF-served HTML — this is the surface a browser actually renders (e.g. the Google
 * profile fields on /settings). The dev-mode e2e run proves the (dev) CSP doesn't break hydration;
 * these tests assert the header is configured at all AND — the security-relevant part — that the
 * PRODUCTION CSP excludes `'unsafe-eval'` (which `next dev` needs for HMR but must never ship).
 */
async function cspFor(nodeEnv: string): Promise<string> {
  vi.stubEnv("NODE_ENV", nodeEnv);
  const groups = await nextConfig.headers!();
  const group = groups.find((g) => g.source === "/:path*");
  const csp = new Map(group!.headers.map((h) => [h.key, h.value])).get("Content-Security-Policy");
  expect(csp, "Content-Security-Policy must be present on BFF responses").toBeDefined();
  return csp!;
}

describe("BFF security headers (next.config)", () => {
  afterEach(() => {
    vi.unstubAllEnvs();
  });

  it("emits a Content-Security-Policy plus the standard headers over /:path*", async () => {
    expect(nextConfig.headers).toBeDefined();
    const groups = await nextConfig.headers!();

    const group = groups.find((g) => g.source === "/:path*");
    expect(group, "a header group covering /:path* must exist").toBeDefined();

    const headerMap = new Map(group!.headers.map((h) => [h.key, h.value]));

    const csp = headerMap.get("Content-Security-Policy");
    expect(csp, "Content-Security-Policy must be present on BFF responses").toBeDefined();
    // The Google avatar host must be allowed — /settings renders <img src={avatarUrl}>.
    expect(csp).toContain("img-src");
    expect(csp).toContain("lh3.googleusercontent.com");
    expect(csp).toContain("frame-ancestors 'none'");

    expect(headerMap.get("X-Content-Type-Options")).toBe("nosniff");
    expect(headerMap.get("X-Frame-Options")).toBe("DENY");
    expect(headerMap.get("Referrer-Policy")).toBe("strict-origin-when-cross-origin");
  });

  it("the PRODUCTION script-src never carries 'unsafe-eval' (the property FR-099 governs)", async () => {
    const csp = await cspFor("production");
    expect(csp).toContain("script-src 'self' 'unsafe-inline'");
    expect(csp, "production must never ship 'unsafe-eval'").not.toContain("'unsafe-eval'");
  });

  it("the development script-src adds 'unsafe-eval' (next dev HMR/React Refresh) — dev only", async () => {
    const csp = await cspFor("development");
    expect(csp).toContain("'unsafe-eval'");
  });
});

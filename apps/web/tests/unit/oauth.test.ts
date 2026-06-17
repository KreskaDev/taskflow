// @vitest-environment node
import { createHash } from "node:crypto";
import { afterEach, beforeEach, describe, expect, it } from "vitest";
import {
  buildAuthorizationUrl,
  createPkcePair,
  openTransaction,
  randomToken,
  sealTransaction,
} from "@/lib/auth/oauth";

describe("PKCE", () => {
  it("derives the challenge as the base64url S256 hash of the verifier (RFC 7636)", () => {
    const { verifier, challenge } = createPkcePair();
    const expected = createHash("sha256").update(verifier).digest().toString("base64url");
    expect(challenge).toBe(expected);
  });

  it("produces URL-safe values with no padding", () => {
    const { verifier, challenge } = createPkcePair();
    expect(verifier).toMatch(/^[A-Za-z0-9_-]+$/);
    expect(challenge).toMatch(/^[A-Za-z0-9_-]+$/);
  });

  it("produces a fresh verifier each call", () => {
    expect(createPkcePair().verifier).not.toBe(createPkcePair().verifier);
  });
});

describe("randomToken", () => {
  it("is URL-safe and unique", () => {
    const a = randomToken();
    const b = randomToken();
    expect(a).toMatch(/^[A-Za-z0-9_-]+$/);
    expect(a).not.toBe(b);
  });
});

describe("buildAuthorizationUrl", () => {
  const original = process.env.GOOGLE_CLIENT_ID;
  beforeEach(() => {
    process.env.GOOGLE_CLIENT_ID = "test-client-id";
    process.env.GOOGLE_AUTH_URI = "https://idp.test/authorize";
  });
  afterEach(() => {
    process.env.GOOGLE_CLIENT_ID = original;
    delete process.env.GOOGLE_AUTH_URI;
  });

  it("carries PKCE, state, nonce, and the openid scope", () => {
    const url = new URL(
      buildAuthorizationUrl({
        state: "state-123",
        nonce: "nonce-456",
        codeChallenge: "challenge-789",
        redirectUri: "https://app.test/api/auth/callback",
      }),
    );

    expect(url.origin + url.pathname).toBe("https://idp.test/authorize");
    expect(url.searchParams.get("client_id")).toBe("test-client-id");
    expect(url.searchParams.get("response_type")).toBe("code");
    expect(url.searchParams.get("scope")).toBe("openid email profile");
    expect(url.searchParams.get("code_challenge")).toBe("challenge-789");
    expect(url.searchParams.get("code_challenge_method")).toBe("S256");
    expect(url.searchParams.get("state")).toBe("state-123");
    expect(url.searchParams.get("nonce")).toBe("nonce-456");
    expect(url.searchParams.get("redirect_uri")).toBe("https://app.test/api/auth/callback");
  });
});

describe("OAuth transaction seal/open", () => {
  const original = process.env.SESSION_SECRET;
  beforeEach(() => {
    process.env.SESSION_SECRET = "a-test-session-secret-value-0123456789";
  });
  afterEach(() => {
    process.env.SESSION_SECRET = original;
  });

  it("round-trips the state, nonce, and verifier", async () => {
    const sealed = await sealTransaction({ state: "s", nonce: "n", codeVerifier: "v" });
    const opened = await openTransaction(sealed);
    expect(opened).toEqual({ state: "s", nonce: "n", codeVerifier: "v" });
  });

  it("rejects a tampered token", async () => {
    const sealed = await sealTransaction({ state: "s", nonce: "n", codeVerifier: "v" });
    await expect(openTransaction(sealed + "x")).rejects.toThrow();
  });

  it("rejects a token sealed under a different secret", async () => {
    const sealed = await sealTransaction({ state: "s", nonce: "n", codeVerifier: "v" });
    process.env.SESSION_SECRET = "a-totally-different-secret-value-987654321";
    await expect(openTransaction(sealed)).rejects.toThrow();
  });
});

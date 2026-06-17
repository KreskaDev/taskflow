// @vitest-environment node
import { afterEach, beforeEach, describe, expect, it } from "vitest";
import { assertAdmissionConfigured, isAdmitted } from "@/lib/auth/admission";

/**
 * Admission gate (T041, FR-087): an admitted sign-in requires a verified email AND either an
 * allowlist match or a Workspace `hd` match. An unverified email is never admitted, even on an
 * allowlist match. If neither allowlist nor hd is configured, the BFF must fail fast at startup.
 */
describe("admission gate", () => {
  const original = { emails: process.env.ADMISSION_EMAILS, hd: process.env.ADMISSION_HD };

  beforeEach(() => {
    delete process.env.ADMISSION_EMAILS;
    delete process.env.ADMISSION_HD;
  });

  afterEach(() => {
    process.env.ADMISSION_EMAILS = original.emails;
    process.env.ADMISSION_HD = original.hd;
  });

  it("admits a verified email on the allowlist", () => {
    process.env.ADMISSION_EMAILS = "ada@example.com, grace@example.com";
    expect(isAdmitted({ email: "ada@example.com", emailVerified: true })).toBe(true);
  });

  it("matches the allowlist case-insensitively and ignores surrounding whitespace", () => {
    process.env.ADMISSION_EMAILS = "  Ada@Example.com  ";
    expect(isAdmitted({ email: "ADA@example.COM", emailVerified: true })).toBe(true);
  });

  it("admits a verified email whose hd matches the configured Workspace domain", () => {
    process.env.ADMISSION_HD = "example.com";
    expect(isAdmitted({ email: "anyone@example.com", emailVerified: true, hd: "example.com" })).toBe(true);
  });

  it("rejects an account that is neither on the allowlist nor in the hd domain", () => {
    process.env.ADMISSION_EMAILS = "ada@example.com";
    process.env.ADMISSION_HD = "example.com";
    expect(isAdmitted({ email: "intruder@evil.com", emailVerified: true, hd: "evil.com" })).toBe(false);
  });

  it("rejects an unverified email even when it is on the allowlist (FR-087)", () => {
    process.env.ADMISSION_EMAILS = "ada@example.com";
    expect(isAdmitted({ email: "ada@example.com", emailVerified: false })).toBe(false);
  });

  it("rejects an unverified email even when the hd matches", () => {
    process.env.ADMISSION_HD = "example.com";
    expect(isAdmitted({ email: "ada@example.com", emailVerified: false, hd: "example.com" })).toBe(false);
  });

  it("does not admit on hd when the hd claim is absent", () => {
    process.env.ADMISSION_HD = "example.com";
    expect(isAdmitted({ email: "ada@example.com", emailVerified: true })).toBe(false);
  });

  it("throws at startup when neither ADMISSION_EMAILS nor ADMISSION_HD is configured", () => {
    expect(() => assertAdmissionConfigured()).toThrow();
  });

  it("passes the startup check when only the allowlist is configured", () => {
    process.env.ADMISSION_EMAILS = "ada@example.com";
    expect(() => assertAdmissionConfigured()).not.toThrow();
  });

  it("passes the startup check when only the hd is configured", () => {
    process.env.ADMISSION_HD = "example.com";
    expect(() => assertAdmissionConfigured()).not.toThrow();
  });

  it("treats whitespace-only configuration as unconfigured", () => {
    process.env.ADMISSION_EMAILS = "   ";
    process.env.ADMISSION_HD = "   ";
    expect(() => assertAdmissionConfigured()).toThrow();
  });
});

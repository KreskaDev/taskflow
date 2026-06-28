import { describe, expect, it } from "vitest";

import {
  changeRoleSchema,
  inviteSchema,
  membershipRoleSchema,
  transferSchema,
} from "@/lib/validation/membership";

/**
 * Membership validation (slice 007, T038, RED → drives T039; research R2/R4). Pins the writable role
 * vocabulary (`editor | viewer`, `owner` REJECTED) and the three payload shapes.
 */
describe("membershipRoleSchema — the writable role vocabulary (R2)", () => {
  it("accepts editor and viewer", () => {
    expect(membershipRoleSchema.parse("editor")).toBe("editor");
    expect(membershipRoleSchema.parse("viewer")).toBe("viewer");
  });

  it("REJECTS owner — it is not a representable writable role", () => {
    expect(membershipRoleSchema.safeParse("owner").success).toBe(false);
  });

  it("rejects an arbitrary string", () => {
    expect(membershipRoleSchema.safeParse("admin").success).toBe(false);
  });
});

describe("inviteSchema", () => {
  it("accepts a well-formed email + assignable role + version", () => {
    const parsed = inviteSchema.parse({ email: "  Person@Example.com  ", role: "editor", version: 3 });
    expect(parsed.email).toBe("Person@Example.com"); // trimmed
    expect(parsed.role).toBe("editor");
    expect(parsed.version).toBe(3);
  });

  it("rejects a malformed email", () => {
    expect(inviteSchema.safeParse({ email: "not-an-email", role: "viewer", version: 0 }).success).toBe(false);
  });

  it("rejects an owner role", () => {
    expect(inviteSchema.safeParse({ email: "a@b.com", role: "owner", version: 0 }).success).toBe(false);
  });

  it("rejects an over-long email (> 320)", () => {
    const long = `${"a".repeat(320)}@b.com`;
    expect(inviteSchema.safeParse({ email: long, role: "editor", version: 0 }).success).toBe(false);
  });
});

describe("changeRoleSchema", () => {
  it("accepts an assignable role + version", () => {
    expect(changeRoleSchema.parse({ role: "viewer", version: 7 })).toEqual({ role: "viewer", version: 7 });
  });

  it("rejects owner", () => {
    expect(changeRoleSchema.safeParse({ role: "owner", version: 1 }).success).toBe(false);
  });
});

describe("transferSchema", () => {
  it("accepts a uuid target + version", () => {
    const parsed = transferSchema.parse({ userId: "11111111-1111-7111-8111-111111111111", version: 2 });
    expect(parsed.userId).toBe("11111111-1111-7111-8111-111111111111");
  });

  it("rejects a non-uuid target", () => {
    expect(transferSchema.safeParse({ userId: "nope", version: 0 }).success).toBe(false);
  });
});

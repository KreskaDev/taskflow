import { SignJWT } from "jose";

/**
 * Mints the short-lived BFF→API identity carrier (research R3, ADR-0004). HMAC-SHA256,
 * 60-second expiry, minted per request by the proxy. The contract here MUST match the
 * API's validation and the integration tests' helper (C# `BffCarrierToken`):
 * issuer `taskflow-bff`, audience `taskflow-api`, claims `sub`/`email`/`name`/`iat`/`exp`.
 */
const ISSUER = "taskflow-bff";
const AUDIENCE = "taskflow-api";
const EXPIRY = "60s";

export interface CarrierClaims {
  /** TaskFlow user id, or the Google subject id on the bootstrap ensure call. */
  sub: string;
  /** Optional: present on the ensure call; omitted on proxy calls (API resolves identity from `sub`). */
  email?: string;
  name?: string;
}

function getSigningKey(): Uint8Array {
  const key = process.env.JWT_SIGNING_KEY;
  if (!key) {
    throw new Error("JWT_SIGNING_KEY is not configured.");
  }
  return new TextEncoder().encode(key);
}

export async function mintCarrierToken(claims: CarrierClaims): Promise<string> {
  const payload: Record<string, string> = {};
  if (claims.email !== undefined) {
    payload["email"] = claims.email;
  }
  if (claims.name !== undefined) {
    payload["name"] = claims.name;
  }

  return new SignJWT(payload)
    .setProtectedHeader({ alg: "HS256" })
    .setSubject(claims.sub)
    .setIssuer(ISSUER)
    .setAudience(AUDIENCE)
    .setIssuedAt()
    .setExpirationTime(EXPIRY)
    .sign(getSigningKey());
}

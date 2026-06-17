import { randomUUID } from "node:crypto";
import { SignJWT } from "jose";
import { Client } from "pg";

/**
 * Seeding helpers for the seeded-session E2E (AS-02/03/04). They reach the running .NET API to
 * bootstrap a real TaskFlow user (so we get a real UserId GUID) and insert a session row directly,
 * letting the spec set the cookie and drive the browser through the real proxy→API path WITHOUT the
 * OAuth dance. The carrier minted here mirrors token.ts (the browser path still exercises the real
 * token.ts on every proxied call — this mint only bootstraps the account).
 */

const ISSUER = "taskflow-bff";
const AUDIENCE = "taskflow-api";

export interface UserProfile {
  id: string;
  email: string;
  displayName: string;
  avatarUrl?: string;
  createdAt: string;
}

async function mintCarrier(claims: { sub: string; email?: string; name?: string }): Promise<string> {
  const key = new TextEncoder().encode(process.env.JWT_SIGNING_KEY);
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
    .setExpirationTime("60s")
    .sign(key);
}

/** Bootstraps (or matches) a user via the real API and returns the profile (incl. the TaskFlow id). */
export async function ensureUser(identity: {
  sub: string;
  email: string;
  name: string;
  picture?: string;
}): Promise<UserProfile> {
  const token = await mintCarrier({ sub: identity.sub, email: identity.email, name: identity.name });
  const res = await fetch(new URL("/api/users/ensure", process.env.API_INTERNAL_URL), {
    method: "POST",
    headers: { "content-type": "application/json", authorization: `Bearer ${token}` },
    body: JSON.stringify({
      googleSubjectId: identity.sub,
      email: identity.email,
      displayName: identity.name,
      ...(identity.picture ? { avatarUrl: identity.picture } : {}),
    }),
  });
  if (!res.ok) {
    throw new Error(`ensureUser failed (${String(res.status)}): ${await res.text()}`);
  }
  return (await res.json()) as UserProfile;
}

/** Inserts a valid (non-expired, non-idle) session row for the given user and returns its id. */
export async function insertSession(userId: string): Promise<string> {
  const id = randomUUID();
  const client = new Client({ connectionString: process.env.DATABASE_URL });
  await client.connect();
  try {
    await client.query(
      `INSERT INTO sessions (id, user_id, expires_at) VALUES ($1, $2, now() + interval '1 hour')`,
      [id, userId],
    );
  } finally {
    await client.end();
  }
  return id;
}

/** Returns true if a User row exists for the given Google subject id (admission "no account" check). */
export async function userExistsByGoogleSub(googleSubjectId: string): Promise<boolean> {
  const client = new Client({ connectionString: process.env.DATABASE_URL });
  await client.connect();
  try {
    const result = await client.query(
      `SELECT 1 FROM users WHERE google_subject_id = $1`,
      [googleSubjectId],
    );
    return (result.rowCount ?? 0) > 0;
  } finally {
    await client.end();
  }
}

/** Points the fake IdP at the identity the NEXT browser sign-in will mint. */
export async function setNextIdentity(identity: {
  sub: string;
  email: string;
  emailVerified: boolean;
  name: string;
  picture?: string;
  hd?: string;
}): Promise<void> {
  const res = await fetch(`${process.env.GOOGLE_ISSUER as string}/__identity`, {
    method: "POST",
    headers: { "content-type": "application/json" },
    body: JSON.stringify(identity),
  });
  if (!res.ok) {
    throw new Error(`setNextIdentity failed (${String(res.status)})`);
  }
}

/** Reads a session row's invalidation flag (used to assert server-side sign-out). */
export async function isSessionInvalidated(sessionId: string): Promise<boolean | null> {
  const client = new Client({ connectionString: process.env.DATABASE_URL });
  await client.connect();
  try {
    const result = await client.query<{ is_invalidated: boolean }>(
      `SELECT is_invalidated FROM sessions WHERE id = $1`,
      [sessionId],
    );
    return result.rows[0]?.is_invalidated ?? null;
  } finally {
    await client.end();
  }
}

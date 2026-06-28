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

/**
 * Returns the TaskFlow UserId for a Google subject id, or null if no row exists. Unlike
 * `userExistsByGoogleSub` this exposes the id, so a delete-roundtrip spec can prove the re-created
 * account is a NEW id (not a restored row).
 */
export async function getUserIdByGoogleSub(googleSubjectId: string): Promise<string | null> {
  const client = new Client({ connectionString: process.env.DATABASE_URL });
  await client.connect();
  try {
    const result = await client.query<{ id: string }>(
      `SELECT id FROM users WHERE google_subject_id = $1`,
      [googleSubjectId],
    );
    return result.rows[0]?.id ?? null;
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

/**
 * A thin authenticated API client bound to ONE TaskFlow user, for SEEDING slice-004 preconditions
 * through the REAL .NET API — the same posture as {@link ensureUser}: this is test INFRASTRUCTURE
 * that establishes server state, NOT a substitute for the app's own UI/proxy path (the browser still
 * drives the real BFF→proxy→API path for every assertion). It mints a short-lived carrier per call
 * and talks to `API_INTERNAL_URL` directly.
 *
 * IMPORTANT — `taskFlowUserId` is the TaskFlow USER id (the GUID returned by {@link ensureUser}),
 * NOT the Google subject id. The ownership-scoped endpoints resolve the caller from the carrier's
 * `sub` claim, which production's BFF token mint sets to the TaskFlow user id (a GUID) — the bootstrap
 * `/api/users/ensure` call is the ONLY path where `sub` is the Google subject id. Minting `sub` with a
 * Google sub here would yield "No authenticated TaskFlow user id" (500), so we mint the GUID.
 *
 * Used by the projects E2E to: create + nest projects, create tasks, move a task into a project (the
 * `M` mechanic's server side), and archive a project — so the spec can assert the WIRED UI surfaces
 * (the sidebar tree, the Archived disclosure's unarchive, the narrowed Inbox) against real data,
 * independently of the UI triggers that are not yet integrated (the T041/T028 orchestration gap).
 */
export function apiAs(taskFlowUserId: string): {
  createProject: (input: { name: string; color: string; icon: string; parentId?: string | null }) => Promise<{ id: string; version: number; parentId: string | null }>;
  createTask: (input: { title: string; position: string; dueDate?: string; dueHasTime?: boolean }) => Promise<{ id: string; version: number }>;
  moveTask: (taskId: string, projectId: string | null, version: number) => Promise<void>;
  archiveProject: (projectId: string, version: number, childDisposition?: "cascade" | "orphan_to_top") => Promise<void>;
  shareProject: (projectId: string, version: number) => Promise<{ version: number }>;
  inviteMember: (projectId: string, email: string, role: "editor" | "viewer", version: number) => Promise<{ userId: string }>;
} {
  const base = process.env.API_INTERNAL_URL as string;

  async function authedFetch(path: string, init: { method: string; body?: unknown }): Promise<Response> {
    const token = await mintCarrier({ sub: taskFlowUserId });
    const headers: Record<string, string> = { authorization: `Bearer ${token}` };
    let body: string | undefined;
    if (init.body !== undefined) {
      headers["content-type"] = "application/json";
      body = JSON.stringify(init.body);
    }
    return fetch(new URL(path, base), { method: init.method, headers, body });
  }

  return {
    async createProject(input) {
      const id = randomUUID();
      const res = await authedFetch(`/api/projects/${id}`, {
        method: "PUT",
        body: { name: input.name, color: input.color, icon: input.icon, parentId: input.parentId ?? null },
      });
      if (!res.ok) throw new Error(`createProject failed (${String(res.status)}): ${await res.text()}`);
      const project = (await res.json()) as { id: string; version: number; parentId: string | null };
      return project;
    },
    async createTask(input) {
      const id = randomUUID();
      const body: Record<string, unknown> = { title: input.title, position: input.position };
      // slice 005: seed a due date so the task lands in Today/Upcoming (the create contract pairs them).
      if (input.dueDate !== undefined) {
        body.dueDate = input.dueDate;
        body.dueHasTime = input.dueHasTime ?? true;
      }
      const res = await authedFetch(`/api/tasks/${id}`, { method: "PUT", body });
      if (!res.ok) throw new Error(`createTask failed (${String(res.status)}): ${await res.text()}`);
      const task = (await res.json()) as { id: string; version: number };
      return task;
    },
    async moveTask(taskId, projectId, version) {
      const res = await authedFetch(`/api/tasks/${taskId}/project`, {
        method: "PATCH",
        body: { projectId, version },
      });
      if (!res.ok) throw new Error(`moveTask failed (${String(res.status)}): ${await res.text()}`);
    },
    async archiveProject(projectId, version, childDisposition) {
      const res = await authedFetch(`/api/projects/${projectId}/archive`, {
        method: "PATCH",
        body: childDisposition ? { version, childDisposition } : { version },
      });
      if (!res.ok) throw new Error(`archiveProject failed (${String(res.status)}): ${await res.text()}`);
    },
    async shareProject(projectId, version) {
      const res = await authedFetch(`/api/projects/${projectId}/share`, { method: "PATCH", body: { version } });
      if (!res.ok) throw new Error(`shareProject failed (${String(res.status)}): ${await res.text()}`);
      return (await res.json()) as { version: number };
    },
    async inviteMember(projectId, email, role, version) {
      const res = await authedFetch(`/api/projects/${projectId}/members`, {
        method: "POST",
        body: { email, role, version },
      });
      if (!res.ok) throw new Error(`inviteMember failed (${String(res.status)}): ${await res.text()}`);
      return (await res.json()) as { userId: string };
    },
  };
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

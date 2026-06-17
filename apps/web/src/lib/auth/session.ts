import { randomUUID } from "node:crypto";
import { getPool } from "@/lib/db";

/**
 * BFF session lifecycle over the Postgres `sessions` table (data-model.md, R10).
 * Sessions have a server-enforced absolute lifetime and an idle timeout, are
 * invalidated server-side on sign-out, and a fresh id is issued at OAuth completion
 * (rotation / fixation defense — Constitution IX).
 */

export interface SessionRecord {
  id: string;
  userId: string;
  createdAt: Date;
  lastAccessedAt: Date;
  expiresAt: Date;
  isInvalidated: boolean;
}

interface SessionRow {
  id: string;
  user_id: string;
  created_at: Date;
  last_accessed_at: Date;
  expires_at: Date;
  is_invalidated: boolean;
}

const HOUR_MS = 60 * 60 * 1000;

function absoluteLifetimeMs(): number {
  const hours = Number(process.env.SESSION_ABSOLUTE_LIFETIME_HOURS ?? "168");
  return (Number.isFinite(hours) ? hours : 168) * HOUR_MS;
}

function idleTimeoutMs(): number {
  const hours = Number(process.env.SESSION_IDLE_TIMEOUT_HOURS ?? "24");
  return (Number.isFinite(hours) ? hours : 24) * HOUR_MS;
}

function toRecord(row: SessionRow): SessionRecord {
  return {
    id: row.id,
    userId: row.user_id,
    createdAt: row.created_at,
    lastAccessedAt: row.last_accessed_at,
    expiresAt: row.expires_at,
    isInvalidated: row.is_invalidated,
  };
}

/** Creates a new session with a cryptographically random id (UUIDv4) and absolute expiry. */
export async function createSession(userId: string): Promise<SessionRecord> {
  const id = randomUUID();
  const expiresAt = new Date(Date.now() + absoluteLifetimeMs());

  const result = await getPool().query<SessionRow>(
    `INSERT INTO sessions (id, user_id, expires_at)
     VALUES ($1, $2, $3)
     RETURNING id, user_id, created_at, last_accessed_at, expires_at, is_invalidated`,
    [id, userId, expiresAt],
  );

  const row = result.rows[0];
  if (!row) {
    throw new Error("Failed to create session.");
  }
  return toRecord(row);
}

/**
 * Reads a session and validates it (exists, not invalidated, not past absolute
 * expiry, not idle-timed-out). On success, touches `last_accessed_at`. Returns
 * `null` for any invalid/missing session so callers deny-by-default.
 */
export async function getValidSession(sessionId: string): Promise<SessionRecord | null> {
  const result = await getPool().query<SessionRow>(
    `SELECT id, user_id, created_at, last_accessed_at, expires_at, is_invalidated
     FROM sessions WHERE id = $1`,
    [sessionId],
  );

  const row = result.rows[0];
  if (!row || row.is_invalidated) {
    return null;
  }

  const now = Date.now();
  const expired = row.expires_at.getTime() <= now;
  const idle = row.last_accessed_at.getTime() + idleTimeoutMs() <= now;
  if (expired || idle) {
    return null;
  }

  const touched = await getPool().query<SessionRow>(
    `UPDATE sessions SET last_accessed_at = now()
     WHERE id = $1
     RETURNING id, user_id, created_at, last_accessed_at, expires_at, is_invalidated`,
    [sessionId],
  );

  const updated = touched.rows[0];
  return updated ? toRecord(updated) : null;
}

/** Invalidates a session server-side (sign-out, FR-054). Idempotent. */
export async function invalidateSession(sessionId: string): Promise<void> {
  await getPool().query(`UPDATE sessions SET is_invalidated = true WHERE id = $1`, [sessionId]);
}

import { assertAdmissionConfigured } from "@/lib/auth/admission";
import { getPool } from "@/lib/db";

/**
 * Node-runtime startup work, isolated in its own module so the Node-only `pg` dependency is never
 * pulled into the edge bundle of `instrumentation.ts` (which is also compiled for the edge runtime
 * because middleware runs there). Imported only under the `NEXT_RUNTIME === "nodejs"` guard.
 *
 * The DDL mirrors sql/001-sessions.sql (the canonical, ops-readable copy); keep the two in sync. The
 * FK to users(id) requires the EF migration (T016) to have run first — in Docker Compose, `web`
 * depends on `api`, which migrates on startup.
 */
const SESSION_DDL = `
CREATE TABLE IF NOT EXISTS sessions (
    id               uuid        PRIMARY KEY,
    user_id          uuid        NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    created_at       timestamptz NOT NULL DEFAULT now(),
    last_accessed_at timestamptz NOT NULL DEFAULT now(),
    expires_at       timestamptz NOT NULL,
    is_invalidated   boolean     NOT NULL DEFAULT false
);
CREATE INDEX IF NOT EXISTS ix_sessions_user_id ON sessions (user_id);
`;

export async function registerNode(): Promise<void> {
  // Fail fast before serving any request if admission is unconfigured (FR-087).
  assertAdmissionConfigured();

  // Ensure the BFF-owned session table exists (T024).
  await getPool().query(SESSION_DDL);
}

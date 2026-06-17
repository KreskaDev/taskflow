import { dirname, resolve } from "node:path";
import { fileURLToPath } from "node:url";

const here = dirname(fileURLToPath(import.meta.url));

/** apps/web */
export const WEB_DIR = resolve(here, "../../..");
/** repo root */
export const REPO_ROOT = resolve(WEB_DIR, "../..");
/** Built .NET API entry assembly (run via `dotnet <dll>` — a single, cleanly-killable process). */
export const API_DLL = resolve(
  REPO_ROOT,
  "apps/api/src/TaskFlow.Api/bin/Debug/net9.0/TaskFlow.Api.dll",
);
/** Next.js CLI entry, invoked via `node <entry> dev` to avoid the pnpm/.cmd shell wrapper. */
export const NEXT_BIN = resolve(WEB_DIR, "node_modules/next/dist/bin/next");

/** PIDs of the harness-managed processes, written by global-setup and read by global-teardown. */
export const STATE_FILE = resolve(here, "..", ".e2e-state.json");
/** Disposable Postgres container name. */
export const PG_CONTAINER = "taskflow-e2e-pg";
/** Host port the disposable Postgres is published on (non-default to avoid local collisions). */
export const PG_PORT = 55432;

/** Canonical session-table DDL (mirrors apps/web/sql/001-sessions.sql); created eagerly by the
 * harness once the API migration has created `users`, so seeding never races the BFF's startup hook. */
export const SESSION_DDL = `
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

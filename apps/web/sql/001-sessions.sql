-- BFF-owned session store (data-model.md "Session"). Created on BFF startup by
-- src/instrumentation.ts via CREATE TABLE IF NOT EXISTS. This file is the canonical,
-- human-readable DDL; the instrumentation hook runs the identical statements
-- (keep the two in sync).
--
-- The FK to users(id) is ON DELETE CASCADE: when the .NET API hard-deletes a User
-- row (account deletion, FR-085), Postgres removes that user's sessions automatically,
-- so the API never touches this BFF-owned table. Requires the EF migration (which
-- creates users) to have run first.

CREATE TABLE IF NOT EXISTS sessions (
    id               uuid        PRIMARY KEY,
    user_id          uuid        NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    created_at       timestamptz NOT NULL DEFAULT now(),
    last_accessed_at timestamptz NOT NULL DEFAULT now(),
    expires_at       timestamptz NOT NULL,
    is_invalidated   boolean     NOT NULL DEFAULT false
);

CREATE INDEX IF NOT EXISTS ix_sessions_user_id ON sessions (user_id);

# Data Model: Accounts & Auth (001)

## Entities

### User (ENT-06) — Aggregate Root

The identity anchor for the entire system. Every later entity's `createdBy`, `ownerId`, assignees, and `ProjectMembership` reference this aggregate.

| Field | Type | Constraints | Notes |
|---|---|---|---|
| `id` | `uuid` (UUIDv7) | PK | Application-generated |
| `google_subject_id` | `text` | UNIQUE, NOT NULL | Google `sub` claim; immutable identity anchor |
| `email` | `text` | UNIQUE, NOT NULL | From Google profile; updated on re-sign-in |
| `display_name` | `text` | NOT NULL | From Google profile; updated on re-sign-in |
| `avatar_url` | `text` | NULL | From Google profile; updated on re-sign-in |
| `created_at` | `timestamptz` | NOT NULL, DEFAULT now() | UTC |
| `updated_at` | `timestamptz` | NOT NULL, DEFAULT now() | UTC |

**Indexes**:
- `ix_users_google_subject_id` — UNIQUE on `google_subject_id`
- `ix_users_email` — UNIQUE on `email`

**Domain rules**:
- `google_subject_id` is immutable after creation
- `email`, `display_name`, `avatar_url` are updated from Google profile on each sign-in
- Account deletion is a **hard-delete**: the User row is removed entirely (there is no soft-delete flag), its sessions are purged, and the freed Google identity means a later sign-in by the same account creates a brand-new empty account (per spec Clarifications 2026-06-17)
- Admission (enforced in the BFF before User creation) requires the Google id_token `email_verified` claim to be `true`; `email_verified` is checked, not stored

**Tombstone identity**: A well-known seeded row (`id = 00000000-0000-0000-0000-000000000000`, `display_name = "Deleted User"`) used by later slices' cascade handlers to anonymize `createdBy`/assignee/comment-author references on account deletion.

### Session — BFF-Managed Entity

Managed by the Next.js BFF (not by the .NET API). Same Postgres database; created on BFF startup via `CREATE TABLE IF NOT EXISTS`.

| Field | Type | Constraints | Notes |
|---|---|---|---|
| `id` | `uuid` (UUIDv4) | PK | Cryptographically random session token |
| `user_id` | `uuid` | FK -> users(id) ON DELETE CASCADE, NOT NULL | Cascade removes the user's sessions when the user row is hard-deleted |
| `created_at` | `timestamptz` | NOT NULL, DEFAULT now() | Session start |
| `last_accessed_at` | `timestamptz` | NOT NULL, DEFAULT now() | Updated on each valid request |
| `expires_at` | `timestamptz` | NOT NULL | Absolute expiry (default: created_at + 7d) |
| `is_invalidated` | `boolean` | NOT NULL, DEFAULT false | Set on sign-out |

**Indexes**:
- `ix_sessions_user_id` on `user_id` (supports the ON DELETE CASCADE and per-user lookups)

**Idle timeout**: Computed as `last_accessed_at + idle_timeout > now()` (default 24h); not stored.

## State Transitions

### User Lifecycle

```
[Non-existent] --EnsureUser (first sign-in)--> [Active]
[Active] --EnsureUser (returning sign-in)--> [Active] (profile refreshed)
[Active] --DeleteAccount--> [Removed] (row hard-deleted, sessions purged, cascade event dispatched)
```

### Session Lifecycle

```
[Non-existent] --OAuth callback--> [Active]
[Active] --Valid request--> [Active] (last_accessed_at touched)
[Active] --Sign-out--> [Invalidated]
[Active] --Absolute expiry--> [Expired] (rejected on next check)
[Active] --Idle timeout--> [Expired] (rejected on next check)
[Active] --Re-sign-in--> [Invalidated] (new session created = rotation)
[Active] --Account deletion--> [Purged] (deleted with the user)
```

## Migration Plan

### EF Core Initial Migration (.NET API)

Creates the `users` table and seeds the tombstone identity. This is the schema source of truth per Constitution VI.

### BFF Session Table (Next.js)

Created on BFF startup via `CREATE TABLE IF NOT EXISTS`. FK to `users(id)` requires the EF Core migration to have run first. Startup order in Docker Compose handles this (`web` depends on `api`; `api` runs migrations on startup). The FK is declared `ON DELETE CASCADE` so hard-deleting a user removes their session rows without the .NET API touching the BFF-owned `sessions` table.

### Cleanup

A periodic job (or on-access check) reaps expired/invalidated sessions older than a retention window (e.g., 30 days).

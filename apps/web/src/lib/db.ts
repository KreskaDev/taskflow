import { Pool } from "pg";

/**
 * Singleton node-postgres pool for the BFF session store (research R2). The BFF
 * touches exactly one table (sessions) with a handful of operations, so raw `pg`
 * is used rather than an ORM. Module caching keeps a single pool per server process.
 */
let pool: Pool | undefined;

export function getPool(): Pool {
  if (!pool) {
    const connectionString = process.env.DATABASE_URL;
    if (!connectionString) {
      throw new Error("DATABASE_URL is not configured.");
    }
    pool = new Pool({ connectionString });
  }
  return pool;
}

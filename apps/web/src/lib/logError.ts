/**
 * Structured client-side error logging (slice 019 T062 — FR-050, S5.3).
 *
 * Every surfaced failure logs ONE structured record with severity, the attempted
 * operation, and diagnostic details — the FR-050 shape — so a report of "it failed"
 * is diagnosable from the console. The user-facing announcement (FR-049) is separate:
 * the global mutation announcer / inline alerts own the human message; this helper
 * owns the diagnostic trail.
 */

export type ErrorSeverity = "warning" | "error" | "critical";

export interface LogErrorEntry {
  /** How bad it is: `warning` recoverable/retryable, `error` failed operation, `critical` app-level. */
  severity: ErrorSeverity;
  /** The attempted operation, dot-scoped (e.g. "task.rename", "views.counts.fetch"). */
  operation: string;
  /** The underlying failure — an Error, a response body, or a plain description. */
  error: unknown;
  /** Optional extra context (ids, parameters) — never secrets. */
  details?: Record<string, unknown>;
}

/** Serializes the failure into a console-stable record (Error → name/message/stack). */
function serializeError(error: unknown): Record<string, unknown> | string {
  if (error instanceof Error) {
    return { name: error.name, message: error.message, stack: error.stack };
  }
  if (typeof error === "string") return error;
  try {
    return JSON.parse(JSON.stringify(error)) as Record<string, unknown>;
  } catch {
    return String(error);
  }
}

/** Logs a structured failure record (FR-050: severity + context + details). */
export function logError({ severity, operation, error, details }: LogErrorEntry): void {
  const record = {
    severity,
    operation,
    error: serializeError(error),
    ...(details ? { details } : {}),
    at: new Date().toISOString(),
  };
  if (severity === "warning") {
    console.warn("[taskflow]", record);
  } else {
    console.error("[taskflow]", record);
  }
}

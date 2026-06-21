import createClient from "openapi-fetch";
import type { components, paths } from "@/lib/api/generated/schema";

/**
 * Type-safe API client (research R5). The browser always talks to the BFF proxy
 * (`/api/proxy/...`), never the API directly; the proxy attaches the identity
 * carrier and forwards the path verbatim to the internal API.
 */
export const API_PROXY_BASE_URL = "/api/proxy";

export const apiClient = createClient<paths>({ baseUrl: API_PROXY_BASE_URL });

export type ProblemDetails = components["schemas"]["ProblemDetails"];

/** How the client should react to a given error code (FR-049). */
export interface ErrorUx {
  message: string;
  /** When true, the error means the session is gone — route the user to sign-in. */
  redirectToSignIn: boolean;
}

/**
 * ADR-0009 error-code → message/redirect table (T032). The stable `errorCode` enum
 * drives a single, consistent error-UX mapping rather than parsing HTTP status alone.
 */
const ERROR_UX: Record<string, ErrorUx> = {
  unauthenticated: { message: "Your session has ended. Please sign in again.", redirectToSignIn: true },
  not_admitted: { message: "Your account is not authorized to access TaskFlow.", redirectToSignIn: false },
  forbidden: { message: "You don't have access to this resource.", redirectToSignIn: false },
  validation_failed: { message: "Some fields need attention.", redirectToSignIn: false },
  not_found: { message: "We couldn't find what you were looking for.", redirectToSignIn: false },
  conflict_lww: { message: "Your change conflicted with a newer update. Review and retry.", redirectToSignIn: false },
  version_conflict: { message: "This item was changed elsewhere. We've reloaded the latest version — review it and retry.", redirectToSignIn: false },
  last_owner: { message: "You can't remove the last owner of a shared project.", redirectToSignIn: false },
  internal_error: { message: "Something went wrong. Please try again.", redirectToSignIn: false },
};

const FALLBACK: ErrorUx = ERROR_UX["internal_error"]!;

/** Maps a ProblemDetails `errorCode` to its user-facing message and redirect behavior. */
export function mapError(errorCode: string | undefined): ErrorUx {
  if (errorCode && errorCode in ERROR_UX) {
    return ERROR_UX[errorCode]!;
  }
  return FALLBACK;
}

/**
 * CSRF defense for state-changing requests (research R4, FR-089). Pairs with the
 * `SameSite=Lax` session cookie: SameSite blocks the cookie on cross-site
 * subrequests, and this Origin check is defense-in-depth for state-changing
 * methods on a single-origin app. Simpler than a double-submit token and equally
 * effective here.
 */

/** The session cookie name (HttpOnly, Secure in prod, SameSite=Lax, Path=/). */
export const SESSION_COOKIE_NAME = "taskflow_session";

const SAFE_METHODS = new Set(["GET", "HEAD", "OPTIONS"]);

/**
 * Returns true if the request is allowed past the CSRF gate. Safe methods always
 * pass; state-changing methods must carry an `Origin` (or `Referer` fallback) whose
 * origin matches the app's own origin (`APP_URL`).
 */
export function isCsrfSafe(request: Request): boolean {
  if (SAFE_METHODS.has(request.method)) {
    return true;
  }

  const appOrigin = process.env.APP_URL;
  if (!appOrigin) {
    throw new Error("APP_URL is not configured.");
  }

  const expected = new URL(appOrigin).origin;

  const origin = request.headers.get("origin");
  if (origin) {
    return origin === expected;
  }

  // Referer fallback for the rare client that omits Origin on a same-origin request.
  const referer = request.headers.get("referer");
  if (referer) {
    try {
      return new URL(referer).origin === expected;
    } catch {
      return false;
    }
  }

  // State-changing request with neither header → reject.
  return false;
}

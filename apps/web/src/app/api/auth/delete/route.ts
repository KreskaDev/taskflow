import { type NextRequest, NextResponse } from "next/server";
import { SESSION_COOKIE_NAME, isCsrfSafe } from "@/lib/auth/csrf";
import { getValidSession } from "@/lib/auth/session";
import { deleteAccount } from "@/lib/api/internal";

/**
 * Account deletion (FR-049, SC-017): a state-changing POST that asks the API to hard-delete the
 * user. On success the API's `users` hard-delete cascades to the BFF `sessions` rows
 * (ON DELETE CASCADE), so the session is already purged server-side — we do NOT call
 * invalidateSession (the row is gone); we only clear the cookie and 303-redirect to sign-in.
 * On failure we leave the cookie intact so a failed delete never strands a logged-out-but-
 * undeleted user, and surface a recoverable error on /settings.
 */
export const dynamic = "force-dynamic";

export async function POST(request: NextRequest): Promise<Response> {
  if (!isCsrfSafe(request)) {
    return new Response("Cross-origin request rejected.", { status: 403 });
  }

  const sessionId = request.cookies.get(SESSION_COOKIE_NAME)?.value;
  if (!sessionId) {
    // No session → effectively already signed out.
    return NextResponse.redirect(new URL("/signin", request.nextUrl.origin), 303);
  }

  const session = await getValidSession(sessionId);
  if (!session) {
    return NextResponse.redirect(new URL("/signin", request.nextUrl.origin), 303);
  }

  const ok = await deleteAccount(session.userId);

  if (ok) {
    const response = NextResponse.redirect(new URL("/signin", request.nextUrl.origin), 303);
    response.cookies.delete(SESSION_COOKIE_NAME);
    return response;
  }

  // A failed delete must not strand a logged-out-but-undeleted user → keep the cookie.
  return NextResponse.redirect(
    new URL("/settings?error=delete_failed", request.nextUrl.origin),
    303,
  );
}

import { type NextRequest, NextResponse } from "next/server";
import { SESSION_COOKIE_NAME, isCsrfSafe } from "@/lib/auth/csrf";
import { invalidateSession } from "@/lib/auth/session";

/**
 * Sign-out (FR-054): server-side session invalidation + cookie clear. A state-changing POST, so it
 * passes the Origin CSRF gate. 303-redirects to the sign-in page so a plain form submit (no JS)
 * works keyboard-first.
 */
export const dynamic = "force-dynamic";

export async function POST(request: NextRequest): Promise<Response> {
  if (!isCsrfSafe(request)) {
    return new Response("Cross-origin request rejected.", { status: 403 });
  }

  const sessionId = request.cookies.get(SESSION_COOKIE_NAME)?.value;
  if (sessionId) {
    await invalidateSession(sessionId);
  }

  const response = NextResponse.redirect(new URL("/signin", request.nextUrl.origin), 303);
  response.cookies.delete(SESSION_COOKIE_NAME);
  return response;
}

import { type NextRequest, NextResponse } from "next/server";
import { SESSION_COOKIE_NAME } from "@/lib/auth/csrf";

/**
 * Deny-by-default route guard (T046, FR-055, AS-03): a request to any protected app page without a
 * session cookie is redirected to the sign-in page. This is a coarse edge-side presence check;
 * authoritative validation (expiry / idle / server-side invalidation) happens in the proxy and API.
 *
 * The matcher excludes the sign-in page (else it would loop), all `/api/*` routes (which enforce
 * their own auth), and Next.js internals/static assets.
 */
export function middleware(request: NextRequest): NextResponse {
  if (request.cookies.has(SESSION_COOKIE_NAME)) {
    return NextResponse.next();
  }
  return NextResponse.redirect(new URL("/signin", request.nextUrl.origin), 307);
}

export const config = {
  matcher: ["/((?!signin|api|_next/static|_next/image|favicon.ico).*)"],
};

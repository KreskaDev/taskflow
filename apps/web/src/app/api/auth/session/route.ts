import { type NextRequest, NextResponse } from "next/server";
import { fetchProfile } from "@/lib/api/internal";
import { SESSION_COOKIE_NAME } from "@/lib/auth/csrf";
import { getValidSession } from "@/lib/auth/session";

/**
 * Current session info for the client (`useSession`). Validates the session server-side and returns
 * the user's profile (fetched from the API with a minted carrier) — never any token. Deny-by-default:
 * any missing/invalid session reports `{ authenticated: false }`.
 */
export const dynamic = "force-dynamic";

export async function GET(request: NextRequest): Promise<Response> {
  const sessionId = request.cookies.get(SESSION_COOKIE_NAME)?.value;
  if (!sessionId) {
    return NextResponse.json({ authenticated: false });
  }

  const session = await getValidSession(sessionId);
  if (!session) {
    return NextResponse.json({ authenticated: false });
  }

  const profile = await fetchProfile(session.userId);
  if (!profile) {
    return NextResponse.json({ authenticated: false });
  }

  return NextResponse.json({
    authenticated: true,
    user: {
      id: profile.id,
      email: profile.email,
      displayName: profile.displayName,
      ...(profile.avatarUrl ? { avatarUrl: profile.avatarUrl } : {}),
    },
  });
}

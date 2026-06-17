import { type NextRequest, NextResponse } from "next/server";
import { ensureUser } from "@/lib/api/internal";
import { isAdmitted } from "@/lib/auth/admission";
import { SESSION_COOKIE_NAME } from "@/lib/auth/csrf";
import {
  OAUTH_TX_COOKIE,
  OAuthError,
  exchangeCodeForTokens,
  openTransaction,
  redirectUri,
  validateIdToken,
} from "@/lib/auth/oauth";
import { createSession } from "@/lib/auth/session";

/**
 * OAuth callback (FR-088, FR-090, AS-01). Validates `state` against the sealed transaction (the
 * OAuth-flow CSRF defense — so the proxy's Origin check does NOT apply to this top-level GET),
 * exchanges the code with the PKCE verifier, validates the id_token (incl. nonce), enforces
 * admission BEFORE creating any account, ensures the user, and rotates in a fresh session.
 */
export const dynamic = "force-dynamic";

function backToSignin(request: NextRequest, error: string): NextResponse {
  const url = new URL("/signin", request.nextUrl.origin);
  url.searchParams.set("error", error);
  const response = NextResponse.redirect(url, 303);
  response.cookies.delete(OAUTH_TX_COOKIE);
  return response;
}

export async function GET(request: NextRequest): Promise<Response> {
  const params = request.nextUrl.searchParams;
  const code = params.get("code");
  const state = params.get("state");
  const sealed = request.cookies.get(OAUTH_TX_COOKIE)?.value;

  if (!code || !state || !sealed) {
    return backToSignin(request, "oauth_failed");
  }

  try {
    const transaction = await openTransaction(sealed);
    if (state !== transaction.state) {
      return backToSignin(request, "oauth_failed");
    }

    const { idToken } = await exchangeCodeForTokens({
      code,
      codeVerifier: transaction.codeVerifier,
      redirectUri: redirectUri(),
    });
    const identity = await validateIdToken(idToken, { nonce: transaction.nonce });

    // Admission BEFORE ensure: a non-admitted sign-in must create no account (FR-087, AS-01).
    const admitted = isAdmitted({
      email: identity.email,
      emailVerified: identity.emailVerified,
      ...(identity.hd ? { hd: identity.hd } : {}),
    });
    if (!admitted) {
      return backToSignin(request, "not_admitted");
    }

    const profile = await ensureUser({
      sub: identity.sub,
      email: identity.email,
      name: identity.name,
      ...(identity.picture ? { picture: identity.picture } : {}),
    });

    // Seed the session with the TaskFlow UserId (a GUID) — never the Google subject id — because the
    // proxy later mints carriers with sub = session.userId and the API parses it as a UserId.
    const session = await createSession(profile.id);

    const response = NextResponse.redirect(new URL("/", request.nextUrl.origin), 303);
    response.cookies.set(SESSION_COOKIE_NAME, session.id, {
      httpOnly: true,
      secure: process.env.NODE_ENV === "production",
      sameSite: "lax",
      path: "/",
      expires: session.expiresAt,
    });
    response.cookies.delete(OAUTH_TX_COOKIE);
    return response;
  } catch (error) {
    if (error instanceof OAuthError) {
      return backToSignin(request, "oauth_failed");
    }
    throw error;
  }
}

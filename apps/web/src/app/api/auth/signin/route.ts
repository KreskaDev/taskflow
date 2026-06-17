import { NextResponse } from "next/server";
import {
  OAUTH_TX_COOKIE,
  buildAuthorizationUrl,
  createPkcePair,
  randomToken,
  redirectUri,
  sealTransaction,
} from "@/lib/auth/oauth";

/**
 * Initiates Google OAuth (FR-090): generates `state`, `nonce`, and a PKCE pair, seals them into a
 * short-lived encrypted `taskflow_oauth` cookie (SameSite=Lax so it survives the top-level redirect
 * back from Google), and 302-redirects the browser to the IdP authorization endpoint.
 */
export const dynamic = "force-dynamic";

export async function GET(): Promise<Response> {
  const state = randomToken();
  const nonce = randomToken();
  const { verifier, challenge } = createPkcePair();

  const sealed = await sealTransaction({ state, nonce, codeVerifier: verifier });
  const authorizationUrl = buildAuthorizationUrl({
    state,
    nonce,
    codeChallenge: challenge,
    redirectUri: redirectUri(),
  });

  const response = NextResponse.redirect(authorizationUrl, 302);
  response.cookies.set(OAUTH_TX_COOKIE, sealed, {
    httpOnly: true,
    secure: process.env.NODE_ENV === "production",
    sameSite: "lax",
    path: "/",
    maxAge: 600,
  });
  return response;
}

import { createHash, randomBytes } from "node:crypto";
import { EncryptJWT, createRemoteJWKSet, jwtDecrypt, jwtVerify } from "jose";

/**
 * Google OAuth helpers (T040, FR-090): PKCE, `state`, `nonce`, code exchange, and id_token
 * validation (signature via JWKS + issuer/audience/nonce/exp), extracting the `email_verified`
 * and `hd` claims the admission gate (T041) needs.
 *
 * Every IdP endpoint is env-configurable (with Google production defaults) so the sign-in E2E can
 * point the BFF at a fake IdP and exercise a real signature round-trip in CI.
 */

const GOOGLE_DEFAULTS = {
  authUri: "https://accounts.google.com/o/oauth2/v2/auth",
  tokenUri: "https://oauth2.googleapis.com/token",
  jwksUri: "https://www.googleapis.com/oauth2/v3/certs",
  // Google has historically presented its issuer both with and without the scheme.
  issuers: ["https://accounts.google.com", "accounts.google.com"],
} as const;

/** The OAuth callback path the BFF listens on; the IdP redirects the browser here. */
export const CALLBACK_PATH = "/api/auth/callback";

/** Cookie carrying the in-flight OAuth transaction between `/signin` and `/callback`. */
export const OAUTH_TX_COOKIE = "taskflow_oauth";

/** Thrown for any recoverable OAuth failure; the auth routes map it to a user-facing error. */
export class OAuthError extends Error {}

function required(name: string): string {
  const value = process.env[name];
  if (!value) {
    throw new OAuthError(`${name} is not configured.`);
  }
  return value;
}

function clientId(): string {
  return required("GOOGLE_CLIENT_ID");
}

function authUri(): string {
  return process.env.GOOGLE_AUTH_URI ?? GOOGLE_DEFAULTS.authUri;
}

function tokenUri(): string {
  return process.env.GOOGLE_TOKEN_URI ?? GOOGLE_DEFAULTS.tokenUri;
}

function jwksUri(): string {
  return process.env.GOOGLE_JWKS_URI ?? GOOGLE_DEFAULTS.jwksUri;
}

function issuers(): string[] {
  const configured = process.env.GOOGLE_ISSUER;
  return configured ? [configured] : [...GOOGLE_DEFAULTS.issuers];
}

/** The absolute redirect URI registered with the IdP (derived from APP_URL). */
export function redirectUri(): string {
  const appUrl = required("APP_URL");
  return new URL(CALLBACK_PATH, appUrl).toString();
}

function base64url(buffer: Buffer): string {
  return buffer.toString("base64url");
}

/** A cryptographically random URL-safe token for `state` / `nonce`. */
export function randomToken(): string {
  return base64url(randomBytes(32));
}

/** Generates a PKCE pair: a random verifier and its S256 challenge (RFC 7636). */
export function createPkcePair(): { verifier: string; challenge: string } {
  const verifier = base64url(randomBytes(32));
  const challenge = base64url(createHash("sha256").update(verifier).digest());
  return { verifier, challenge };
}

/** Builds the IdP authorization URL initiating the Authorization Code + PKCE flow. */
export function buildAuthorizationUrl(params: {
  state: string;
  nonce: string;
  codeChallenge: string;
  redirectUri: string;
}): string {
  const url = new URL(authUri());
  url.searchParams.set("client_id", clientId());
  url.searchParams.set("redirect_uri", params.redirectUri);
  url.searchParams.set("response_type", "code");
  url.searchParams.set("scope", "openid email profile");
  url.searchParams.set("code_challenge", params.codeChallenge);
  url.searchParams.set("code_challenge_method", "S256");
  url.searchParams.set("state", params.state);
  url.searchParams.set("nonce", params.nonce);
  url.searchParams.set("access_type", "online");
  url.searchParams.set("prompt", "select_account");
  return url.toString();
}

/** Exchanges an authorization code (with the PKCE verifier) for tokens; returns the id_token. */
export async function exchangeCodeForTokens(params: {
  code: string;
  codeVerifier: string;
  redirectUri: string;
}): Promise<{ idToken: string }> {
  const body = new URLSearchParams({
    grant_type: "authorization_code",
    code: params.code,
    client_id: clientId(),
    client_secret: required("GOOGLE_CLIENT_SECRET"),
    redirect_uri: params.redirectUri,
    code_verifier: params.codeVerifier,
  });

  const response = await fetch(tokenUri(), {
    method: "POST",
    headers: { "content-type": "application/x-www-form-urlencoded" },
    body,
  });
  if (!response.ok) {
    throw new OAuthError(`Token exchange failed (${String(response.status)}).`);
  }

  const json = (await response.json()) as { id_token?: unknown };
  if (typeof json.id_token !== "string") {
    throw new OAuthError("Token response did not include an id_token.");
  }
  return { idToken: json.id_token };
}

/** The validated identity extracted from the Google id_token. */
export interface GoogleIdentity {
  sub: string;
  email: string;
  emailVerified: boolean;
  name: string;
  picture?: string;
  hd?: string;
}

// Module-scoped JWKS, keyed by URI so jose's kid-cache is reused across requests (and the fake-IdP
// override is picked up if the env differs from a prior process state).
let jwksCache: { uri: string; set: ReturnType<typeof createRemoteJWKSet> } | undefined;
function getJwks(): ReturnType<typeof createRemoteJWKSet> {
  const uri = jwksUri();
  if (!jwksCache || jwksCache.uri !== uri) {
    jwksCache = { uri, set: createRemoteJWKSet(new URL(uri)) };
  }
  return jwksCache.set;
}

/**
 * Verifies the id_token signature (JWKS), issuer, audience, expiry, and nonce, then projects the
 * admission-relevant claims. Throws {@link OAuthError} on any failure.
 */
export async function validateIdToken(
  idToken: string,
  opts: { nonce: string; now?: Date },
): Promise<GoogleIdentity> {
  let payload;
  try {
    ({ payload } = await jwtVerify(idToken, getJwks(), {
      issuer: issuers(),
      audience: clientId(),
      ...(opts.now ? { currentDate: opts.now } : {}),
    }));
  } catch (cause) {
    throw new OAuthError("id_token validation failed.", { cause });
  }

  if (payload["nonce"] !== opts.nonce) {
    throw new OAuthError("id_token nonce mismatch.");
  }

  const sub = payload.sub;
  const email = payload["email"];
  if (typeof sub !== "string" || typeof email !== "string") {
    throw new OAuthError("id_token is missing a sub or email claim.");
  }

  const name = payload["name"];
  const picture = payload["picture"];
  const hd = payload["hd"];

  return {
    sub,
    email,
    emailVerified: payload["email_verified"] === true,
    name: typeof name === "string" && name.length > 0 ? name : email,
    ...(typeof picture === "string" ? { picture } : {}),
    ...(typeof hd === "string" ? { hd } : {}),
  };
}

/** The in-flight OAuth transaction persisted across the redirect to the IdP. */
export interface OAuthTransaction {
  state: string;
  nonce: string;
  codeVerifier: string;
}

function transactionKey(): Uint8Array {
  // A 32-byte content-encryption key derived from SESSION_SECRET (dir / A256GCM).
  return createHash("sha256").update(required("SESSION_SECRET")).digest();
}

/** Seals the OAuth transaction into an encrypted token for the `taskflow_oauth` cookie. */
export function sealTransaction(transaction: OAuthTransaction): Promise<string> {
  return new EncryptJWT({
    state: transaction.state,
    nonce: transaction.nonce,
    codeVerifier: transaction.codeVerifier,
  })
    .setProtectedHeader({ alg: "dir", enc: "A256GCM" })
    .setIssuedAt()
    .setExpirationTime("10m")
    .encrypt(transactionKey());
}

/** Opens and validates a sealed OAuth transaction; throws {@link OAuthError} if tampered/expired. */
export async function openTransaction(token: string): Promise<OAuthTransaction> {
  let payload;
  try {
    ({ payload } = await jwtDecrypt(token, transactionKey()));
  } catch (cause) {
    throw new OAuthError("OAuth transaction is invalid or expired.", { cause });
  }

  const { state, nonce, codeVerifier } = payload as Record<string, unknown>;
  if (typeof state !== "string" || typeof nonce !== "string" || typeof codeVerifier !== "string") {
    throw new OAuthError("OAuth transaction payload is malformed.");
  }
  return { state, nonce, codeVerifier };
}

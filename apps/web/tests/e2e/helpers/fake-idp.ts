import { createHash } from "node:crypto";
import { type IncomingMessage, type Server, ServerResponse, createServer } from "node:http";
import { SignJWT, calculateJwkThumbprint, exportJWK, generateKeyPair } from "jose";

/**
 * Minimal Google-shaped OpenID Connect IdP for the AS-01 sign-in E2E. It implements just enough of
 * the Authorization-Code + PKCE flow to exercise the BFF's oauth.ts end-to-end against a REAL RS256
 * signature + JWKS round-trip (the only path that covers exchangeCodeForTokens + validateIdToken):
 *
 *   GET  /auth        authorization endpoint — redirects straight back to redirect_uri with a code
 *   POST /token       code → RS256 id_token (PKCE verifier checked for real)
 *   GET  /jwks        the public signing key (createRemoteJWKSet fetches this)
 *   POST /__identity  test control: set the identity the NEXT sign-in will mint
 *
 * The BFF points at it via the GOOGLE_* env overrides. Tests run serially (workers:1), so a single
 * "next identity" slot is race-free.
 */

export interface FakeIdentity {
  sub: string;
  email: string;
  emailVerified: boolean;
  name: string;
  picture?: string;
  hd?: string;
}

interface PendingCode {
  nonce: string;
  codeChallenge: string;
  identity: FakeIdentity;
}

function base64urlSha256(input: string): string {
  return createHash("sha256").update(input).digest("base64url");
}

function readBody(req: IncomingMessage): Promise<string> {
  return new Promise((resolve, reject) => {
    let data = "";
    req.on("data", (chunk) => (data += chunk));
    req.on("end", () => resolve(data));
    req.on("error", reject);
  });
}

function json(res: ServerResponse, status: number, body: unknown): void {
  const payload = JSON.stringify(body);
  res.writeHead(status, { "content-type": "application/json" });
  res.end(payload);
}

export async function startFakeIdp(
  port: number,
  issuer: string,
  audience: string,
): Promise<Server> {
  const { publicKey, privateKey } = await generateKeyPair("RS256");
  const jwk = await exportJWK(publicKey);
  const kid = await calculateJwkThumbprint(jwk);
  jwk.kid = kid;
  jwk.alg = "RS256";
  jwk.use = "sig";

  let nextIdentity: FakeIdentity | undefined;
  const codes = new Map<string, PendingCode>();
  let counter = 0;

  async function mintIdToken(pending: PendingCode): Promise<string> {
    const id = pending.identity;
    const claims: Record<string, unknown> = {
      email: id.email,
      email_verified: id.emailVerified,
      name: id.name,
      nonce: pending.nonce,
    };
    if (id.picture) {
      claims["picture"] = id.picture;
    }
    if (id.hd) {
      claims["hd"] = id.hd;
    }
    return new SignJWT(claims)
      .setProtectedHeader({ alg: "RS256", kid })
      .setIssuer(issuer)
      .setAudience(audience)
      .setSubject(id.sub)
      .setIssuedAt()
      .setExpirationTime("1h")
      .sign(privateKey);
  }

  const server = createServer((req, res) => {
    void (async () => {
      try {
        const url = new URL(req.url ?? "/", `http://localhost:${String(port)}`);

        if (req.method === "GET" && url.pathname === "/jwks") {
          json(res, 200, { keys: [jwk] });
          return;
        }

        if (req.method === "POST" && url.pathname === "/__identity") {
          nextIdentity = JSON.parse(await readBody(req)) as FakeIdentity;
          json(res, 200, { ok: true });
          return;
        }

        if (req.method === "GET" && url.pathname === "/auth") {
          const state = url.searchParams.get("state");
          const nonce = url.searchParams.get("nonce");
          const redirectUri = url.searchParams.get("redirect_uri");
          const codeChallenge = url.searchParams.get("code_challenge");
          if (!state || !nonce || !redirectUri || !codeChallenge) {
            json(res, 400, { error: "invalid_request" });
            return;
          }
          if (!nextIdentity) {
            json(res, 400, { error: "no identity configured for this sign-in" });
            return;
          }
          counter += 1;
          const code = `code-${String(counter)}`;
          codes.set(code, { nonce, codeChallenge, identity: nextIdentity });

          const back = new URL(redirectUri);
          back.searchParams.set("code", code);
          back.searchParams.set("state", state);
          res.writeHead(302, { location: back.toString() });
          res.end();
          return;
        }

        if (req.method === "POST" && url.pathname === "/token") {
          const form = new URLSearchParams(await readBody(req));
          const code = form.get("code") ?? "";
          const verifier = form.get("code_verifier") ?? "";
          const pending = codes.get(code);
          if (!pending) {
            json(res, 400, { error: "invalid_grant" });
            return;
          }
          codes.delete(code);
          // Verify PKCE for real (S256), exercising the BFF's verifier/challenge pair.
          if (base64urlSha256(verifier) !== pending.codeChallenge) {
            json(res, 400, { error: "invalid_grant", error_description: "PKCE mismatch" });
            return;
          }
          const idToken = await mintIdToken(pending);
          json(res, 200, {
            access_token: "fake-access-token",
            token_type: "Bearer",
            expires_in: 3600,
            id_token: idToken,
          });
          return;
        }

        json(res, 404, { error: "not_found" });
      } catch (err) {
        json(res, 500, { error: String(err) });
      }
    })();
  });

  await new Promise<void>((resolve) => server.listen(port, "127.0.0.1", resolve));
  return server;
}

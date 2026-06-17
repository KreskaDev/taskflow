import { cookies } from "next/headers";
import { SESSION_COOKIE_NAME, isCsrfSafe } from "@/lib/auth/csrf";
import { getValidSession } from "@/lib/auth/session";
import { mintCarrierToken } from "@/lib/auth/token";

/**
 * Authenticated BFF→API proxy (plan "BFF-to-API Proxy", FR-091). For each request:
 * 1. CSRF gate (Origin check) on state-changing methods.
 * 2. Read the `taskflow_session` cookie and validate the session in Postgres
 *    (exists / not invalidated / not expired / not idle), touching `last_accessed_at`.
 * 3. Mint a 60-second HMAC carrier (sub = user id) and forward to `API_INTERNAL_URL`.
 * 4. Relay the API response. Deny-by-default: no/invalid session → 401.
 */
export const dynamic = "force-dynamic";

function problem(status: number, errorCode: string, title: string): Response {
  return new Response(
    JSON.stringify({
      type: `https://taskflow.example/errors/${errorCode}`,
      title,
      status,
      errorCode,
    }),
    { status, headers: { "content-type": "application/problem+json" } },
  );
}

async function handle(request: Request, params: Promise<{ path: string[] }>): Promise<Response> {
  if (!isCsrfSafe(request)) {
    return problem(403, "forbidden", "Cross-origin request rejected.");
  }

  const sessionId = (await cookies()).get(SESSION_COOKIE_NAME)?.value;
  if (!sessionId) {
    return problem(401, "unauthenticated", "Authentication required.");
  }

  const session = await getValidSession(sessionId);
  if (!session) {
    return problem(401, "unauthenticated", "Session is invalid or expired.");
  }

  const apiBase = process.env.API_INTERNAL_URL;
  if (!apiBase) {
    throw new Error("API_INTERNAL_URL is not configured.");
  }

  // The captured path is forwarded verbatim to the API (it already includes the
  // leading `api/` segment, e.g. /api/proxy/api/users/me → API /api/users/me).
  const { path } = await params;
  const target = new URL(`/${path.join("/")}`, apiBase);
  target.search = new URL(request.url).search;

  const token = await mintCarrierToken({ sub: session.userId });

  const headers = new Headers(request.headers);
  headers.set("authorization", `Bearer ${token}`);
  // Never leak the browser session cookie or the public Host to the internal API.
  headers.delete("cookie");
  headers.delete("host");

  const init: RequestInit = { method: request.method, headers, redirect: "manual" };
  if (request.method !== "GET" && request.method !== "HEAD") {
    init.body = await request.arrayBuffer();
  }

  const apiResponse = await fetch(target, init);

  return new Response(apiResponse.body, {
    status: apiResponse.status,
    statusText: apiResponse.statusText,
    headers: new Headers(apiResponse.headers),
  });
}

export function GET(request: Request, ctx: { params: Promise<{ path: string[] }> }): Promise<Response> {
  return handle(request, ctx.params);
}
export function POST(request: Request, ctx: { params: Promise<{ path: string[] }> }): Promise<Response> {
  return handle(request, ctx.params);
}
export function PUT(request: Request, ctx: { params: Promise<{ path: string[] }> }): Promise<Response> {
  return handle(request, ctx.params);
}
export function PATCH(request: Request, ctx: { params: Promise<{ path: string[] }> }): Promise<Response> {
  return handle(request, ctx.params);
}
export function DELETE(request: Request, ctx: { params: Promise<{ path: string[] }> }): Promise<Response> {
  return handle(request, ctx.params);
}

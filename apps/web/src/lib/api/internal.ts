import { mintCarrierToken } from "@/lib/auth/token";

/**
 * Server-to-server calls from the BFF to the internal API, used by the auth routes (which run
 * outside the browser proxy path). Each call mints a fresh 60-second BFF carrier — the same
 * `token.ts` minting the proxy uses — so these paths exercise the real carrier↔API contract.
 */

export interface UserProfile {
  id: string;
  email: string;
  displayName: string;
  avatarUrl?: string;
  createdAt: string;
}

function apiBase(): string {
  const base = process.env.API_INTERNAL_URL;
  if (!base) {
    throw new Error("API_INTERNAL_URL is not configured.");
  }
  return base;
}

/**
 * Bootstrap the account during OAuth callback (FR-052). The carrier `sub` is the Google subject id
 * (the API reads the identity from the body, not the token); the returned profile `id` is the
 * TaskFlow UserId that seeds the session.
 */
export async function ensureUser(identity: {
  sub: string;
  email: string;
  name: string;
  picture?: string;
}): Promise<UserProfile> {
  const token = await mintCarrierToken({ sub: identity.sub, email: identity.email, name: identity.name });

  const response = await fetch(new URL("/api/users/ensure", apiBase()), {
    method: "POST",
    headers: { "content-type": "application/json", authorization: `Bearer ${token}` },
    body: JSON.stringify({
      googleSubjectId: identity.sub,
      email: identity.email,
      displayName: identity.name,
      ...(identity.picture ? { avatarUrl: identity.picture } : {}),
    }),
  });

  if (!response.ok) {
    throw new Error(`ensureUser failed (${String(response.status)}).`);
  }
  return (await response.json()) as UserProfile;
}

/**
 * Reads the current user's profile by TaskFlow id (carrier `sub` = the user id). Returns `null`
 * when the API denies the carrier or the row no longer exists (e.g. a hard-deleted account).
 */
export async function fetchProfile(userId: string): Promise<UserProfile | null> {
  const token = await mintCarrierToken({ sub: userId });

  const response = await fetch(new URL("/api/users/me", apiBase()), {
    headers: { authorization: `Bearer ${token}` },
  });

  if (!response.ok) {
    return null;
  }
  return (await response.json()) as UserProfile;
}

/**
 * Hard-deletes the current user's account (FR-049, SC-017). The carrier `sub` is the TaskFlow id
 * (me-scoped); the API returns 204 on success, so `response.ok` is the success signal. The API's
 * hard-delete cascades to the BFF's `sessions` rows (ON DELETE CASCADE) — the caller clears the
 * cookie but never touches the session table.
 */
export async function deleteAccount(userId: string): Promise<boolean> {
  const token = await mintCarrierToken({ sub: userId });

  try {
    const response = await fetch(new URL("/api/users/me", apiBase()), {
      method: "DELETE",
      headers: { authorization: `Bearer ${token}` },
    });
    return response.ok;
  } catch {
    // A transport failure reaching the internal API is a recoverable delete failure (FR-049): return
    // false so the route redirects to /settings?error=delete_failed rather than throwing a 500. (A
    // missing JWT_SIGNING_KEY still throws from mintCarrierToken above — a config fault, not a
    // recoverable user error.)
    return false;
  }
}

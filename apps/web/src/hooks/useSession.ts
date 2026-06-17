"use client";

import { useQuery } from "@tanstack/react-query";

export interface SessionUser {
  id: string;
  email: string;
  displayName: string;
  avatarUrl?: string;
}

export interface SessionInfo {
  authenticated: boolean;
  user?: SessionUser;
}

/**
 * Client-side session state. Reads the BFF's `/api/auth/session` endpoint (added in
 * T042) via TanStack Query so the UI can react to sign-in/out without exposing any
 * token to client JavaScript.
 */
export function useSession() {
  return useQuery<SessionInfo>({
    queryKey: ["session"],
    queryFn: async (): Promise<SessionInfo> => {
      const response = await fetch("/api/auth/session");
      if (!response.ok) {
        return { authenticated: false };
      }
      return (await response.json()) as SessionInfo;
    },
  });
}

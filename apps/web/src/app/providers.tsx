"use client";

import { MutationCache, QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { type ReactNode, useState } from "react";

import { ToastProvider, useToast } from "@/components/ui/Toast";

/**
 * App-wide client providers. TanStack Query backs optimistic mutations and cached
 * reads (Constitution III). A single QueryClient is created per browser session.
 *
 * `ToastProvider` wraps the query layer so the global mutation-error announcer
 * (below) can reach the shared `LiveRegion` via the stable `useToast().push`.
 */
export function Providers({ children }: { children: ReactNode }) {
  return (
    <ToastProvider>
      <QueryProvider>{children}</QueryProvider>
    </ToastProvider>
  );
}

/**
 * Builds the per-session `QueryClient` with a global `MutationCache` error announcer
 * (FIX 1; FR-049 "no silent failures", FR-101 "announce without stealing focus").
 *
 * Why here and not in each mutation's `onError`: every mutation (US1 create today,
 * US8's rename/toggle/reorder/delete tomorrow) inherits a single, consistent failure
 * announcement. The optimistic recipe still rolls the row back in its own `onError`;
 * this only surfaces the user-facing message.
 *
 * Why `error.message` is announced verbatim (NOT re-mapped through `mapError`): the
 * `mutationFn` already throws `new Error(mapError(errorCode).message)` at the trust
 * boundary, so by the time the error reaches here the friendly text IS `error.message`
 * and the machine-readable `errorCode` is gone. Re-mapping `error.message` would always
 * fall through to the generic fallback. `push` routes the text through the existing
 * `ToastProvider` → polite `LiveRegion` (no new toast system), so it is announced
 * politely without stealing focus.
 *
 * The GET-list error is deliberately NOT announced here (no `QueryCache` announcer):
 * the workspace page owns an accessible inline alert + retry for it, so adding a toast
 * would double-announce. `MutationCache` is the load-bearing inheritance piece.
 */
function QueryProvider({ children }: { children: ReactNode }) {
  const { push } = useToast();
  const [queryClient] = useState(
    () =>
      new QueryClient({
        mutationCache: new MutationCache({
          onError: (error) => {
            push(error.message, { variant: "error" });
          },
        }),
      }),
  );

  return <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>;
}

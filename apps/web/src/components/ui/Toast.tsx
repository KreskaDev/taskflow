"use client";

import {
  createContext,
  useCallback,
  useContext,
  useEffect,
  useMemo,
  useReducer,
  useRef,
  type ReactNode,
} from "react";
import { LiveRegion } from "@/components/ui/LiveRegion";

type ToastVariant = "info" | "error" | "success";

interface ToastProps {
  message: string;
  variant?: ToastVariant;
  onDismiss?: () => void;
}

/**
 * Presentational toast (FR-101). **Silent by design**: this node carries no
 * `role="status"`/`aria-live` — assistive-technology announcement is owned by the
 * single `LiveRegion` rendered by `ToastViewport` below.
 *
 * ── Single-announcer decision (FR-101 double-announcement hazard) ──────────────
 * Toast text is routed through the shared `LiveRegion` (`role=status`,
 * `aria-live=polite`) — NOT through a per-toast live region. Two reasons:
 *   1. A polite live region only reliably announces when it is already mounted
 *      *before* its text changes. A queue that mounts a fresh announcing node per
 *      message arrives with content already in it and may be skipped by the SR.
 *      One persistent region fed the active text is the reliable shape.
 *   2. Routing through both the visual toast AND a live region double-announces.
 * So the visual toasts are marked `aria-hidden` and the `LiveRegion` is the sole
 * announcer. (The app-level `<LiveRegion />` in `layout.tsx` is left unfed; this
 * provider owns its own region — never feed two regions, that is the footgun.)
 */
export function Toast({ message, variant = "info", onDismiss }: ToastProps) {
  return (
    <div className={`tf-toast tf-toast--${variant}`} aria-hidden="true">
      <span>{message}</span>
      {onDismiss ? (
        <button type="button" className="tf-toast__dismiss" aria-label="Dismiss notification" onClick={onDismiss}>
          {"×"}
        </button>
      ) : null}
    </div>
  );
}

// ── Queue + auto-dismiss + coalescing layer ─────────────────────────────────────

/** How long a toast stays visible before it auto-dismisses (ms). */
const AUTO_DISMISS_MS = 5000;

interface ToastOptions {
  variant?: ToastVariant;
  /** Override the default auto-dismiss duration (ms). `null` disables auto-dismiss. */
  durationMs?: number | null;
}

interface ToastItem {
  id: number;
  message: string;
  variant: ToastVariant;
  durationMs: number | null;
  /** Bumped when an identical message coalesces, so the live region re-announces. */
  count: number;
}

interface ToastContextValue {
  /** Enqueue a toast. Identical-text toasts coalesce into the existing one. */
  push: (message: string, options?: ToastOptions) => void;
  /** Dismiss a specific toast by id. */
  dismiss: (id: number) => void;
}

const ToastContext = createContext<ToastContextValue | null>(null);

type ToastAction =
  | { type: "push"; message: string; variant: ToastVariant; durationMs: number | null }
  | { type: "dismiss"; id: number };

interface ToastState {
  toasts: ToastItem[];
  nextId: number;
}

function toastReducer(state: ToastState, action: ToastAction): ToastState {
  switch (action.type) {
    case "push": {
      // Coalescing: an identical (message + variant) toast already on screen is
      // re-used — bump its count (which mutates the announced string so a polite
      // live region re-reads it) and re-arm its timer via a fresh id-stable item.
      const existing = state.toasts.find((t) => t.message === action.message && t.variant === action.variant);
      if (existing) {
        return {
          ...state,
          toasts: state.toasts.map((t) => (t === existing ? { ...t, count: t.count + 1 } : t)),
        };
      }
      const item: ToastItem = {
        id: state.nextId,
        message: action.message,
        variant: action.variant,
        durationMs: action.durationMs,
        count: 1,
      };
      return { toasts: [...state.toasts, item], nextId: state.nextId + 1 };
    }
    case "dismiss":
      return { ...state, toasts: state.toasts.filter((t) => t.id !== action.id) };
  }
}

/**
 * Provides the toast queue. Mounts the visual viewport plus the single polite
 * `LiveRegion` announcer. Coalesces identical messages and auto-dismisses each
 * toast; never steals focus (FR-101).
 */
export function ToastProvider({ children }: { children: ReactNode }) {
  const [state, dispatch] = useReducer(toastReducer, { toasts: [], nextId: 1 });

  const push = useCallback((message: string, options?: ToastOptions) => {
    dispatch({
      type: "push",
      message,
      variant: options?.variant ?? "info",
      durationMs: options?.durationMs === undefined ? AUTO_DISMISS_MS : options.durationMs,
    });
  }, []);

  const dismiss = useCallback((id: number) => {
    dispatch({ type: "dismiss", id });
  }, []);

  const value = useMemo<ToastContextValue>(() => ({ push, dismiss }), [push, dismiss]);

  return (
    <ToastContext.Provider value={value}>
      {children}
      <ToastViewport toasts={state.toasts} onDismiss={dismiss} />
    </ToastContext.Provider>
  );
}

/** Access the toast queue. Throws if used outside a `ToastProvider`. */
export function useToast(): ToastContextValue {
  const ctx = useContext(ToastContext);
  if (ctx === null) {
    throw new Error("useToast must be used within a ToastProvider");
  }
  return ctx;
}

function ToastViewport({ toasts, onDismiss }: { toasts: ToastItem[]; onDismiss: (id: number) => void }) {
  // The single announcer: the most-recent toast's text, with a coalesce count so
  // a repeated identical message still re-reads (polite regions skip unchanged text).
  const latest = toasts[toasts.length - 1];
  const announced = latest ? (latest.count > 1 ? `${latest.message} (${latest.count})` : latest.message) : "";

  return (
    <div className="tf-toast-viewport">
      {toasts.map((toast) => (
        <AutoDismiss key={toast.id} toast={toast} onDismiss={onDismiss}>
          <Toast
            message={toast.count > 1 ? `${toast.message} (${toast.count})` : toast.message}
            variant={toast.variant}
            onDismiss={() => onDismiss(toast.id)}
          />
        </AutoDismiss>
      ))}
      <LiveRegion politeness="polite">{announced}</LiveRegion>
    </div>
  );
}

/** Arms an auto-dismiss timer for one toast; re-arms when the toast's count changes. */
function AutoDismiss({
  toast,
  onDismiss,
  children,
}: {
  toast: ToastItem;
  onDismiss: (id: number) => void;
  children: ReactNode;
}) {
  const onDismissRef = useRef(onDismiss);
  onDismissRef.current = onDismiss;

  useEffect(() => {
    if (toast.durationMs === null) return;
    const handle: ReturnType<typeof setTimeout> = setTimeout(() => {
      onDismissRef.current(toast.id);
    }, toast.durationMs);
    return () => {
      clearTimeout(handle);
    };
    // Re-arm on coalesce (count bump) so a re-announced toast gets a fresh window.
  }, [toast.id, toast.durationMs, toast.count]);

  return <>{children}</>;
}

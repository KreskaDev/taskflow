interface ToastProps {
  message: string;
  variant?: "info" | "error" | "success";
  onDismiss?: () => void;
}

/**
 * Presentational toast that is also announced to assistive technology
 * (`role="status"`, polite live region — Constitution II). Toast state management
 * (queueing, auto-dismiss) is layered on in a later slice; this is the a11y baseline.
 */
export function Toast({ message, variant = "info", onDismiss }: ToastProps) {
  return (
    <div className={`tf-toast tf-toast--${variant}`} role="status" aria-live="polite" aria-atomic="true">
      <span>{message}</span>
      {onDismiss ? (
        <button type="button" className="tf-toast__dismiss" aria-label="Dismiss notification" onClick={onDismiss}>
          {"×"}
        </button>
      ) : null}
    </div>
  );
}

"use client";

import { useState } from "react";
import { Button } from "@/components/ui/Button";
import { Dialog } from "@/components/ui/Dialog";

/**
 * Account-deletion control (T053, FR-049). The trigger opens a modal that states the full blast
 * radius (account + all data, permanent, irreversible) and confirms via a REAL native form POST to
 * /api/auth/delete — the platform submit carries the Origin header that satisfies the route's CSRF
 * gate (no client fetch), mirroring the sign-out form in the app shell.
 *
 * NO-JS: the Dialog is client-rendered (`if (!open) return null`), so the open gesture requires JS
 * and the confirm form is unreachable without it. This is accepted (delete-requires-JS);
 * sign-out remains the no-JS-safe control (minimal-shell philosophy).
 */
export function DeleteAccountDialog() {
  const [open, setOpen] = useState(false);

  return (
    <div className="tf-delete-account">
      <Button variant="danger" onClick={() => setOpen(true)}>
        Delete account
      </Button>

      <Dialog
        open={open}
        onClose={() => setOpen(false)}
        titleId="delete-account-title"
        descriptionId="delete-account-desc"
      >
        <h2 id="delete-account-title">Delete account</h2>
        <p id="delete-account-desc">
          This permanently and irreversibly deletes your account and ALL of its data. This cannot
          be undone.
        </p>
        <div className="tf-dialog__actions">
          <Button variant="secondary" onClick={() => setOpen(false)}>
            Cancel
          </Button>
          <form method="post" action="/api/auth/delete">
            <button type="submit" className="tf-button tf-button--danger">
              Permanently delete account
            </button>
          </form>
        </div>
      </Dialog>
    </div>
  );
}

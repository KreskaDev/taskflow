"use client";

import { useState } from "react";
import { Button } from "@/components/ui/Button";
import { Dialog, DialogActions, DialogTitle } from "@/components/ui/Dialog";

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
    <div>
      <Button variant="danger" onClick={() => setOpen(true)}>
        Usuń konto
      </Button>

      <Dialog
        open={open}
        onClose={() => setOpen(false)}
        titleId="delete-account-title"
        descriptionId="delete-account-desc"
      >
        <DialogTitle id="delete-account-title">Usuń konto</DialogTitle>
        <p id="delete-account-desc">
          To trwale i nieodwracalnie usuwa Twoje konto i WSZYSTKIE jego dane. Tej operacji nie
          można cofnąć.
        </p>
        <DialogActions>
          <Button variant="secondary" onClick={() => setOpen(false)}>
            Anuluj
          </Button>
          <form method="post" action="/api/auth/delete">
            <Button type="submit" variant="danger">
              Trwale usuń konto
            </Button>
          </form>
        </DialogActions>
      </Dialog>
    </div>
  );
}

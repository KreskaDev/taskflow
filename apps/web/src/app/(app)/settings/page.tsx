"use client";

import { useEffect, useState } from "react";
import { useSession } from "@/hooks/useSession";
import { Avatar } from "@/components/ui/Avatar";
import { Button } from "@/components/ui/Button";
import { DeleteAccountDialog } from "@/components/ui/DeleteAccountDialog";

/**
 * Settings / profile (T045, US-11.AS-04). Displays the Google display name and avatar from the
 * validated session. All fields render as React text/attribute content (output-encoded — FR-056),
 * never raw HTML. The account-deletion control (T053) is wired in here in Phase 4.
 */
export default function SettingsPage() {
  const { data, isLoading } = useSession();

  // A failed account deletion (T052) redirects here with `?error=delete_failed`; surface it as a
  // recoverable, announced message (FR-049). Read client-side from the URL to keep this a plain
  // client page (no useSearchParams Suspense boundary needed).
  const [deleteFailed, setDeleteFailed] = useState(false);
  useEffect(() => {
    setDeleteFailed(new URLSearchParams(window.location.search).get("error") === "delete_failed");
  }, []);

  return (
    <section aria-labelledby="settings-heading" className="tf-settings">
      <h1 id="settings-heading">Settings</h1>

      {deleteFailed ? (
        <p className="tf-settings__error" role="alert">
          Account deletion failed. Please try again.
        </p>
      ) : null}

      {isLoading ? (
        <p className="tf-settings__status" role="status">
          Loading your profile…
        </p>
      ) : data?.authenticated && data.user ? (
        <div className="tf-profile">
          <Avatar
            userId={data.user.id}
            displayName={data.user.displayName}
            avatarUrl={data.user.avatarUrl}
            size="lg"
            className="tf-profile__avatar"
          />
          <dl className="tf-profile__fields">
            <dt>Name</dt>
            <dd className="tf-profile__name">{data.user.displayName}</dd>
            <dt>Email</dt>
            <dd className="tf-profile__email">{data.user.email}</dd>
          </dl>
          {/* Sign-out (INV-005; re-homed here from the pre-019 header during the shell
              rebuild): a plain form POST to the BFF route — works without client JS
              (Constitution I, FR-054). */}
          <form method="post" action="/api/auth/signout">
            <Button type="submit" variant="secondary">
              Wyloguj
            </Button>
          </form>
          <DeleteAccountDialog />
        </div>
      ) : (
        <p className="tf-settings__status" role="status">
          You are not signed in.
        </p>
      )}
    </section>
  );
}

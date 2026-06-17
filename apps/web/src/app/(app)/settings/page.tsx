"use client";

import { useEffect, useState } from "react";
import { useSession } from "@/hooks/useSession";
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
          {data.user.avatarUrl ? (
            // eslint-disable-next-line @next/next/no-img-element -- external Google avatar; next/image remote config is deferred to a later slice.
            <img
              className="tf-profile__avatar"
              src={data.user.avatarUrl}
              alt=""
              width={64}
              height={64}
            />
          ) : null}
          <dl className="tf-profile__fields">
            <dt>Name</dt>
            <dd className="tf-profile__name">{data.user.displayName}</dd>
            <dt>Email</dt>
            <dd className="tf-profile__email">{data.user.email}</dd>
          </dl>
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

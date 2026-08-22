"use client";

import { useEffect, useState } from "react";
import { useSession } from "@/hooks/useSession";
import { Avatar } from "@/components/ui/Avatar";
import { Button } from "@/components/ui/Button";
import { CycleDurationField } from "@/components/cycles/CycleDurationField";
import { DeleteAccountDialog } from "@/components/ui/DeleteAccountDialog";
import styles from "./settings.module.css";

/**
 * Settings / profile (T045, US-11.AS-04; migrated in slice 019 — T060, S5.1). Displays the
 * Google display name and avatar from the validated session. All fields render as React
 * text/attribute content (output-encoded — FR-056), never raw HTML.
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
    <section aria-labelledby="settings-heading" className={styles.settings}>
      <h1 id="settings-heading" className={styles.heading}>
        Ustawienia
      </h1>

      {deleteFailed ? (
        <p className={styles.error} role="alert">
          Usunięcie konta nie powiodło się. Spróbuj ponownie.
        </p>
      ) : null}

      {isLoading ? (
        <p className={styles.status} role="status">
          Wczytywanie profilu…
        </p>
      ) : data?.authenticated && data.user ? (
        <div className={styles.profile}>
          <Avatar
            userId={data.user.id}
            displayName={data.user.displayName}
            avatarUrl={data.user.avatarUrl}
            size="lg"
          />
          <dl className={styles.fields}>
            <dt>Imię i nazwisko</dt>
            <dd>{data.user.displayName}</dd>
            <dt>E-mail</dt>
            <dd>{data.user.email}</dd>
          </dl>
          {/* Sign-out (INV-005; re-homed here from the pre-019 header during the shell
              rebuild): a plain form POST to the BFF route — works without client JS
              (Constitution I, FR-054). */}
          {/* Slice 011 (FR-015/D8): the per-user default cycle duration under the profile section. */}
          <CycleDurationField />
          <form method="post" action="/api/auth/signout">
            <Button type="submit" variant="secondary">
              Wyloguj
            </Button>
          </form>
          <DeleteAccountDialog />
        </div>
      ) : (
        <p className={styles.status} role="status">
          Nie jesteś zalogowany(-a).
        </p>
      )}
    </section>
  );
}

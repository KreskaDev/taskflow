import { SignInButton } from "@/components/auth/SignInButton";
import styles from "../auth.module.css";

/**
 * Sign-in page (migrated in slice 019, T059 — S5.1): the Instrument Serif brand wordmark
 * (its sole sanctioned use, design-brief) + the Google entry point; a recoverable error
 * from a failed/non-admitted attempt is surfaced as a clear, announced message (FR-049).
 */
const ERROR_MESSAGES: Record<string, string> = {
  not_admitted: "To konto nie ma dostępu do TaskFlow.",
  oauth_failed: "Logowanie nie powiodło się. Spróbuj ponownie.",
};

export default async function SignInPage({
  searchParams,
}: {
  searchParams: Promise<{ error?: string }>;
}) {
  const { error } = await searchParams;
  const message = error ? (ERROR_MESSAGES[error] ?? ERROR_MESSAGES["oauth_failed"]) : undefined;

  return (
    <section className={styles.card} aria-labelledby="signin-heading">
      <h1 id="signin-heading" className={styles.wordmark}>
        TaskFlow
      </h1>
      <p className={styles.subtitle}>Zaloguj się do swojego obszaru roboczego.</p>

      {message ? (
        <p className={styles.error} role="alert">
          {message}
        </p>
      ) : null}

      <SignInButton />
    </section>
  );
}

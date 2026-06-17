import { SignInButton } from "@/components/auth/SignInButton";

/**
 * Sign-in page (T043, US-11). Shows the Google sign-in entry point and any recoverable error from a
 * failed/non-admitted attempt (FR-049): a non-admitted account or a verification/OAuth failure is
 * surfaced as a clear, announced message with no account created.
 */
const ERROR_MESSAGES: Record<string, string> = {
  not_admitted: "Your account is not authorized to access TaskFlow.",
  oauth_failed: "Sign-in could not be completed. Please try again.",
};

export default async function SignInPage({
  searchParams,
}: {
  searchParams: Promise<{ error?: string }>;
}) {
  const { error } = await searchParams;
  const message = error ? (ERROR_MESSAGES[error] ?? ERROR_MESSAGES["oauth_failed"]) : undefined;

  return (
    <section className="tf-signin" aria-labelledby="signin-heading">
      <h1 id="signin-heading">TaskFlow</h1>
      <p className="tf-signin__subtitle">Sign in to your workspace.</p>

      {message ? (
        <p className="tf-signin__error" role="alert">
          {message}
        </p>
      ) : null}

      <SignInButton />
    </section>
  );
}

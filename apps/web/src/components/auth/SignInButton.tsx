/**
 * Initiates Google sign-in. A plain anchor to the BFF's `/api/auth/signin` GET route (which
 * redirects to Google), so it works keyboard-first with no client JavaScript (Constitution I).
 * Styled as the primary button; inherits the global visible-focus indicator (FR-042).
 */
export function SignInButton() {
  return (
    <a className="tf-button tf-button--primary tf-signin-button" href="/api/auth/signin">
      Sign in with Google
    </a>
  );
}

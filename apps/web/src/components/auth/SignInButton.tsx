import styles from "./SignInButton.module.css";

/**
 * Initiates Google sign-in. A plain anchor to the BFF's `/api/auth/signin` GET route
 * (redirects to Google), so it works keyboard-first with no client JavaScript
 * (Constitution I). Styled as the catalog primary button (slice 019, T059).
 */
export function SignInButton() {
  return (
    <a className={styles.button} href="/api/auth/signin">
      Zaloguj się przez Google
    </a>
  );
}

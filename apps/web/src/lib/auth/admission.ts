/**
 * Admission gate (T041, FR-087). Sign-up is admission-gated: a sign-in is admitted only when the
 * Google id_token's email is verified AND the account is either on the email allowlist
 * (`ADMISSION_EMAILS`) or in the configured Workspace hosted-domain (`ADMISSION_HD`, the id_token
 * `hd` claim). An unverified email is never admitted, even on an allowlist match.
 *
 * Configuration is read at call time (not module load) so a verified email cannot be admitted by a
 * stale snapshot, and so the startup guard reflects the live environment.
 */

/** The admission-relevant subset of the validated Google id_token claims. */
export interface IdTokenAdmissionClaims {
  email: string;
  emailVerified: boolean;
  /** Google Workspace hosted-domain claim; present only for Workspace accounts. */
  hd?: string;
}

function allowlist(): ReadonlySet<string> {
  const raw = process.env.ADMISSION_EMAILS ?? "";
  return new Set(
    raw
      .split(",")
      .map((entry) => entry.trim().toLowerCase())
      .filter((entry) => entry.length > 0),
  );
}

function admissionHd(): string | undefined {
  const hd = process.env.ADMISSION_HD?.trim();
  return hd && hd.length > 0 ? hd : undefined;
}

/** Returns true if the id_token identifies an admitted account (FR-087). */
export function isAdmitted(claims: IdTokenAdmissionClaims): boolean {
  // An unverified email is never admitted, regardless of allowlist / hd match.
  if (claims.emailVerified !== true) {
    return false;
  }

  const email = claims.email.trim().toLowerCase();
  if (allowlist().has(email)) {
    return true;
  }

  const hd = admissionHd();
  if (hd !== undefined && claims.hd === hd) {
    return true;
  }

  return false;
}

/**
 * Startup fail-fast guard (FR-087): if neither an email allowlist nor a Workspace hosted-domain is
 * configured, the BFF must refuse to start rather than boot in an open or an all-denying state.
 * Wired into `instrumentation.ts` (the Next.js `register()` hook).
 */
export function assertAdmissionConfigured(): void {
  const hasEmails = (process.env.ADMISSION_EMAILS?.trim().length ?? 0) > 0;
  const hasHd = (process.env.ADMISSION_HD?.trim().length ?? 0) > 0;

  if (!hasEmails && !hasHd) {
    throw new Error(
      "Admission is unconfigured: set ADMISSION_EMAILS and/or ADMISSION_HD (FR-087). " +
        "Refusing to start in an open or an all-denying state.",
    );
  }
}

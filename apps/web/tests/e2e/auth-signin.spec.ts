import { expect, test } from "@playwright/test";
import { setNextIdentity, userExistsByGoogleSub } from "./helpers/seed";

/**
 * US1 sign-in E2E (US-11.AS-01) against a fake Google IdP. This is the only path that drives the
 * full OAuth Authorization-Code + PKCE dance through the BFF's oauth.ts —
 * exchangeCodeForTokens + validateIdToken with a REAL RS256 signature + JWKS round-trip and real
 * PKCE verification — plus the admission gate (FR-087) before any account is created.
 */

const ADMITTED_EMAIL = process.env.ADMISSION_EMAILS as string;

test.describe("US1 sign-in via OAuth (AS-01)", () => {
  test("AS-01: an admitted, email-verified account signs in → account created, lands in workspace", async ({
    browser,
  }) => {
    const sub = "google-as01-admitted";
    await setNextIdentity({
      sub,
      email: ADMITTED_EMAIL,
      emailVerified: true,
      name: "Alan Turing",
      picture: "https://avatars.test/alan.png",
    });

    const context = await browser.newContext();
    const page = await context.newPage();
    await page.goto("/signin");
    await page.getByRole("link", { name: /sign in with google/i }).click();

    // Through the IdP and back to the callback, landing in the workspace.
    await page.waitForURL("http://localhost:3000/");
    await expect(page.getByRole("heading", { name: "Your workspace" })).toBeVisible();

    // Account was created.
    expect(await userExistsByGoogleSub(sub)).toBe(true);

    // And the profile reflects the Google identity (proves the session carries the TaskFlow id).
    await page.goto("/settings");
    await expect(page.locator(".tf-profile__name")).toHaveText("Alan Turing");

    await context.close();
  });

  test("AS-01: a non-admitted account is rejected with a recoverable message and NO account", async ({
    browser,
  }) => {
    const sub = "google-as01-nonadmitted";
    await setNextIdentity({
      sub,
      email: "intruder@not-allowed.test",
      emailVerified: true,
      name: "Mallory",
    });

    const context = await browser.newContext();
    const page = await context.newPage();
    await page.goto("/signin");
    await page.getByRole("link", { name: /sign in with google/i }).click();

    await page.waitForURL(/\/signin\?error=not_admitted/);
    await expect(page.getByRole("alert")).toContainText(/not authorized/i);

    expect(await userExistsByGoogleSub(sub)).toBe(false);

    await context.close();
  });

  test("AS-01: an unverified email is rejected even when the address is on the allowlist (no account)", async ({
    browser,
  }) => {
    const sub = "google-as01-unverified";
    // The email IS the admitted address, but email_verified=false must still reject (FR-087).
    await setNextIdentity({
      sub,
      email: ADMITTED_EMAIL,
      emailVerified: false,
      name: "Spoofed",
    });

    const context = await browser.newContext();
    const page = await context.newPage();
    await page.goto("/signin");
    await page.getByRole("link", { name: /sign in with google/i }).click();

    await page.waitForURL(/\/signin\?error=not_admitted/);
    await expect(page.getByRole("alert")).toContainText(/not authorized/i);

    // Admission runs BEFORE ensure, so no account is bootstrapped for this subject.
    expect(await userExistsByGoogleSub(sub)).toBe(false);

    await context.close();
  });
});

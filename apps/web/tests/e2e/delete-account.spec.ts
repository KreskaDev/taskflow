import { expect, test } from "@playwright/test";
import {
  getUserIdByGoogleSub,
  setNextIdentity,
  userExistsByGoogleSub,
} from "./helpers/seed";

/**
 * Account-deletion roundtrip E2E (T048, FR-049, SC-017). Drives the full OAuth sign-in, then the
 * real delete flow (DeleteAccountDialog → native form POST → BFF /api/auth/delete → API hard-delete),
 * and proves: (1) the session ends and the cookie is cleared, (2) the User row is hard-deleted
 * server-side (the cascade purges the session), and (3) re-signing in with the SAME Google identity
 * yields a BRAND-NEW empty account (a fresh id, not a restored row).
 */

test.describe("Account deletion roundtrip (FR-049 / SC-017)", () => {
  test("delete + confirm ends the session and hard-deletes the row; re-sign-in is a fresh account", async ({
    browser,
  }) => {
    const sub = "google-delete-roundtrip";
    const identity = {
      sub,
      email: "delete-roundtrip@taskflow.test",
      emailVerified: true,
      name: "Dee Leet",
      picture: "https://avatars.test/dee.png",
    };

    const context = await browser.newContext();
    const page = await context.newPage();

    // Sign in (OAuth) and land in the workspace.
    await setNextIdentity(identity);
    await page.goto("/signin");
    await page.getByRole("link", { name: /sign in with google/i }).click();
    await page.waitForURL("http://localhost:3000/");

    const id1 = await getUserIdByGoogleSub(sub);
    expect(id1).toBeTruthy();

    // Open the delete dialog and confirm.
    await page.goto("/settings");
    await page.getByRole("button", { name: "Delete account" }).click();
    await page.getByRole("button", { name: "Permanently delete account" }).click();

    // Session ends → redirected to sign-in.
    await page.waitForURL("**/signin");

    // Cookie cleared → the protected route now redirects back to sign-in.
    await page.goto("/settings");
    await page.waitForURL("**/signin");

    // SC-017: the User row was hard-deleted server-side (no residual row).
    expect(await userExistsByGoogleSub(sub)).toBe(false);

    // Re-sign-in with the SAME Google identity yields a BRAND-NEW empty account.
    await setNextIdentity(identity);
    await page.goto("/signin");
    await page.getByRole("link", { name: /sign in with google/i }).click();
    await page.waitForURL("http://localhost:3000/");

    const id2 = await getUserIdByGoogleSub(sub);
    expect(id2).toBeTruthy();
    expect(id2).not.toBe(id1);

    await context.close();
  });
});

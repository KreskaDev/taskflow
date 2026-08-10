import { expect, test } from "@playwright/test";
import { ensureUser, insertSession, isSessionInvalidated } from "./helpers/seed";

/**
 * US1 seeded-session E2E (US-11.AS-02/03/04). These drive the browser through the REAL
 * BFF→proxy→API path against a real migrated Postgres + .NET API (booted by global-setup) — no API
 * mocking — so the production token.ts HS256 carrier ↔ API validation contract is exercised on every
 * request (the orientation's #1 web risk). The OAuth/admission sign-in path (AS-01) is covered
 * separately by the fake-IdP milestone.
 */

test.describe("US1 seeded-session (AS-02/03/04)", () => {
  test("AS-04: settings shows the Google display name + avatar [INV-133]", async ({ browser }) => {
    // A distinct email/sub keeps this seeded user independent of the OAuth specs (the API enforces
    // UNIQUE(email)); a seeded session is honoured regardless of the admission allowlist.
    const profileEmail = "as04@taskflow.test";
    const profile = await ensureUser({
      sub: "google-sub-as04",
      email: profileEmail,
      name: "Ada Lovelace",
      picture: "https://avatars.test/ada.png",
    });
    const sessionId = await insertSession(profile.id);

    const context = await browser.newContext();
    await context.addCookies([
      { name: "taskflow_session", value: sessionId, url: "http://localhost:3000" },
    ]);
    const page = await context.newPage();
    await page.goto("/settings");

    await expect(page.getByRole("heading", { name: "Ustawienia" })).toBeVisible();
    // The identity fields render as a definition list inside the main content (T060).
    await expect(page.getByRole("main")).toContainText("Ada Lovelace");
    await expect(page.getByRole("main")).toContainText(profileEmail);
    // The avatar identity element: the Google photo when it loads, else the FR-105
    // deterministic initials fallback (slice 019) — both expose the display name as the
    // accessible img name. The seeded avatars.test URL never resolves, so the fallback
    // is the expected steady state here.
    await expect(
      page.getByRole("main").getByRole("img", { name: "Ada Lovelace" }).last(),
    ).toBeVisible();

    await context.close();
  });

  test("AS-03: unauthenticated access to a protected route redirects to sign-in [INV-001]", async ({
    browser,
  }) => {
    const context = await browser.newContext();
    const page = await context.newPage();
    await page.goto("/settings");

    await page.waitForURL("**/signin");
    await expect(page.getByRole("heading", { name: "TaskFlow" })).toBeVisible();
    await expect(page.getByRole("link", { name: /zaloguj się przez google/i })).toBeVisible();

    await context.close();
  });

  test("AS-03: the proxy denies an unauthenticated API call (401) [INV-006]", async ({ request }) => {
    // No session cookie → deny-by-default through the real proxy.
    const res = await request.get("http://localhost:3000/api/proxy/api/users/me");
    expect(res.status()).toBe(401);
    const body = (await res.json()) as { errorCode?: string };
    expect(body.errorCode).toBe("unauthenticated");
  });

  test("AS-02: sign-out ends the session and protected views become inaccessible; the chrome carries brand + nav + sign-out [INV-005] [INV-010]", async ({
    browser,
  }) => {
    const profile = await ensureUser({
      sub: "google-sub-as02",
      email: "as02@taskflow.test",
      name: "Grace Hopper",
      picture: "https://avatars.test/grace.png",
    });
    // This account is not on the admission allowlist, but admission only gates the OAuth sign-in
    // path; a seeded session for an existing user is honoured. Use a distinct email to satisfy the
    // API's UNIQUE(email) constraint.
    const sessionId = await insertSession(profile.id);

    const context = await browser.newContext();
    await context.addCookies([
      { name: "taskflow_session", value: sessionId, url: "http://localhost:3000" },
    ]);
    const page = await context.newPage();

    await page.goto("/");
    await expect(page.getByRole("heading", { name: "Inbox" })).toBeVisible();

    // INV-010 — the signed-in chrome: the TaskFlow brand links home, the sidebar nav is a
    // labelled landmark, and the topbar identity links to Settings (where sign-out lives).
    await expect(page.getByRole("link", { name: "TaskFlow" })).toHaveAttribute("href", "/");
    await expect(page.getByRole("navigation", { name: "Projekty" })).toBeVisible();
    const identity = page.getByRole("link", { name: /Konto: Grace Hopper/ });
    await expect(identity).toHaveAttribute("href", "/settings");

    // The sign-out affordance lives on the Settings screen since the slice-019 shell
    // rebuild (reached via the topbar identity → Settings); still a plain form POST.
    await identity.click();
    await page.waitForURL("**/settings");
    await page.getByRole("button", { name: "Wyloguj" }).click();
    await page.waitForURL("**/signin");

    // Server-side invalidation (FR-054).
    expect(await isSessionInvalidated(sessionId)).toBe(true);

    // Cookie cleared → the protected route now redirects back to sign-in.
    await page.goto("/settings");
    await page.waitForURL("**/signin");

    await context.close();
  });
});

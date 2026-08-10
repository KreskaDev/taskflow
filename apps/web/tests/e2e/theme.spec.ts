import { expect, test, type Page } from "@playwright/test";
import { ensureUser, insertSession } from "./helpers/seed";

/**
 * Palette/theme E2E (T027, slice 019 — UIT-001/002/003/005, S1.2/S1.3).
 *
 * The four palettes are forced EXCLUSIVELY by swapping the composite class on <html>
 * (contracts/ui-theme.md — no other hook exists or may be introduced).
 */

const PALETTES = ["dark-cool", "dark-warm", "light-cool", "light-warm"] as const;

/** bg-deep per palette (tokens.css) — the body background each palette must resolve. */
const EXPECTED_BODY_BG: Record<(typeof PALETTES)[number], string> = {
  "dark-cool": "rgb(20, 20, 20)", // #141414
  "dark-warm": "rgb(22, 20, 18)", // #161412
  "light-cool": "rgb(242, 242, 242)", // #F2F2F2
  "light-warm": "rgb(242, 239, 232)", // #F2EFE8
};

async function signedInPage(
  browser: import("@playwright/test").Browser,
  key: string,
): Promise<{ page: Page; context: import("@playwright/test").BrowserContext }> {
  const profile = await ensureUser({
    sub: `google-sub-${key}`,
    email: `${key}@taskflow.test`,
    name: "Theme Tester",
  });
  const sessionId = await insertSession(profile.id);
  const context = await browser.newContext();
  await context.addCookies([
    { name: "taskflow_session", value: sessionId, url: "http://localhost:3000" },
  ]);
  const page = await context.newPage();
  return { page, context };
}

async function setPalette(page: Page, palette: string): Promise<void> {
  await page.evaluate((cls) => {
    const el = document.documentElement;
    el.className = el.className
      .split(/\s+/)
      .filter((c) => !["dark-cool", "dark-warm", "light-cool", "light-warm"].includes(c))
      .concat(cls)
      .join(" ");
  }, palette);
}

test.describe("Token foundation & four palettes (S1.2/S1.3)", () => {
  test("UIT-001: SSR payload carries dark-cool — the default renders before hydration (no FOUC)", async ({
    browser,
  }) => {
    const { page, context } = await signedInPage(browser, "theme-ssr");
    // The class must be IN the server HTML (not applied by client JS after paint).
    const response = await page.goto("/");
    const rawHtml = (await response?.text()) ?? "";
    expect(rawHtml).toMatch(/<html[^>]*class="[^"]*dark-cool/);

    await expect(page.locator("html")).toHaveClass(/dark-cool/);
    const bodyBg = await page.evaluate(() => getComputedStyle(document.body).backgroundColor);
    expect(bodyBg).toBe(EXPECTED_BODY_BG["dark-cool"]);
    await context.close();
  });

  test("UIT-002/003: swapping the composite class re-themes every palette without layout shift", async ({
    browser,
  }) => {
    const { page, context } = await signedInPage(browser, "theme-swap");
    await page.goto("/");
    await expect(page.getByRole("heading", { level: 1 })).toBeVisible();

    const baseline = await page.getByRole("heading", { level: 1 }).boundingBox();

    for (const palette of PALETTES) {
      await setPalette(page, palette);
      const bodyBg = await page.evaluate(() => getComputedStyle(document.body).backgroundColor);
      expect(bodyBg, `body bg in ${palette}`).toBe(EXPECTED_BODY_BG[palette]);

      // Accent resolves per palette (spot-check a second token tier).
      const accent = await page.evaluate(() =>
        getComputedStyle(document.documentElement).getPropertyValue("--color-accent").trim(),
      );
      expect(accent.length, `--color-accent resolves in ${palette}`).toBeGreaterThan(0);

      // Re-theming must not move layout (S1.3: no layout change, colors only).
      const box = await page.getByRole("heading", { level: 1 }).boundingBox();
      expect(box?.x).toBe(baseline?.x);
      expect(box?.y).toBe(baseline?.y);
    }
    await context.close();
  });

  test("UIT-005: color-scheme follows the MODE half of the class (native controls/scrollbars)", async ({
    browser,
  }) => {
    const { page, context } = await signedInPage(browser, "theme-scheme");
    await page.goto("/");

    for (const palette of PALETTES) {
      await setPalette(page, palette);
      const scheme = await page.evaluate(
        () => getComputedStyle(document.documentElement).colorScheme,
      );
      const expected = palette.startsWith("dark") ? "dark" : "light";
      expect(scheme, `color-scheme in ${palette}`).toBe(expected);
    }
    await context.close();
  });
});

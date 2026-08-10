import { expect, test, type Page } from "@playwright/test";
import { ensureUser, insertSession } from "./helpers/seed";

/**
 * Visual regression [V] suite (slice 019, T066 — D13, UIT-100): `toHaveScreenshot`
 * per in-scope screen × 4 palettes × 3 widths (1440/1024/768). Deterministic by
 * construction: `reducedMotion: 'reduce'` + `animations: 'disabled'`, a seeded user
 * with NO data (stable empty states), and palette forced by the documented class swap
 * (contracts/ui-theme.md).
 *
 * Baselines are platform-suffixed — generate/update them INSIDE the CI-matching Linux
 * environment (`--update-snapshots` in the Playwright docker image the `web-e2e` job
 * uses); Windows/macOS-generated baselines will never match ubuntu CI. Each palette's
 * baselines are committed for HUMAN approval (T070).
 */

const PALETTES = ["dark-cool", "dark-warm", "light-cool", "light-warm"] as const;
const WIDTHS = [1440, 1024, 768] as const;
const SCREENS = [
  { path: "/", slug: "inbox" },
  { path: "/today", slug: "today" },
  { path: "/upcoming", slug: "upcoming" },
  { path: "/assigned", slug: "assigned" },
  { path: "/settings", slug: "settings" },
] as const;

test.use({ reducedMotion: "reduce" });

// Baselines are platform-suffixed and generated/verified ONLY in the linux (CI-matching)
// environment — a Windows/macOS dev run skips instead of minting unusable snapshots.
test.skip(process.platform !== "linux", "[V] baselines are linux-only (CI-matching image)");

async function forcePalette(page: Page, palette: string): Promise<void> {
  await page.evaluate((cls) => {
    document.documentElement.className = cls;
  }, palette);
}

for (const screen of SCREENS) {
  test.describe(`[V] ${screen.slug}`, () => {
    for (const palette of PALETTES) {
      for (const width of WIDTHS) {
        test(`${screen.slug} × ${palette} × ${width}px`, async ({ browser }) => {
          const profile = await ensureUser({
            sub: "google-sub-visual",
            email: "visual@taskflow.test",
            name: "Vis Ualnie",
          });
          const sessionId = await insertSession(profile.id);
          const context = await browser.newContext({
            viewport: { width, height: 900 },
            reducedMotion: "reduce",
          });
          await context.addCookies([
            { name: "taskflow_session", value: sessionId, url: "http://localhost:3000" },
          ]);
          const page = await context.newPage();
          await page.goto(screen.path);
          await page.waitForLoadState("networkidle");
          await forcePalette(page, palette);

          await expect(page).toHaveScreenshot(`${screen.slug}-${palette}-${width}.png`, {
            fullPage: true,
            animations: "disabled",
            caret: "hide",
          });

          await context.close();
        });
      }
    }
  });
}

test.describe("[V] signin (anonymous)", () => {
  for (const palette of PALETTES) {
    for (const width of WIDTHS) {
      test(`signin × ${palette} × ${width}px`, async ({ browser }) => {
        const context = await browser.newContext({
          viewport: { width, height: 900 },
          reducedMotion: "reduce",
        });
        const page = await context.newPage();
        await page.goto("/signin");
        await page.waitForLoadState("networkidle");
        await forcePalette(page, palette);

        await expect(page).toHaveScreenshot(`signin-${palette}-${width}.png`, {
          fullPage: true,
          animations: "disabled",
          caret: "hide",
        });

        await context.close();
      });
    }
  }
});

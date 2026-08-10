import { expect, test, type Page } from "@playwright/test";
import { apiAs, ensureUser, insertSession } from "./helpers/seed";

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

// Playwright ≥1.53 moved `reducedMotion` under contextOptions (BrowserContextOptions).
test.use({ contextOptions: { reducedMotion: "reduce" } });

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

/**
 * Slice-010 screens (T022): the project Board and the status-grouped List. They need a
 * seeded project (+ deterministic tasks incl. one cancelled) and the per-project
 * localStorage mode/group-by, so they extend the matrix as a sibling block of `SCREENS`
 * rather than bare paths. Baselines generate ONLY with 019's pending T066 regeneration —
 * one linux run covers both slices (do not run the update script here).
 */
test.describe("[V] project board & grouped list (slice 010)", () => {
  for (const palette of PALETTES) {
    for (const width of WIDTHS) {
      for (const projection of ["board", "grouped-list"] as const) {
        test(`${projection} × ${palette} × ${width}px`, async ({ browser }) => {
          const profile = await ensureUser({
            sub: "google-sub-visual-board",
            email: "visual-board@taskflow.test",
            name: "Vis Ualnie",
          });
          const api = apiAs(profile.id);
          const project = await api.createProject({ name: "Wizualny", color: "blue", icon: "folder" });
          const seedTask = async (title: string, position: string, status?: string) => {
            const task = await api.createTask({ title, position });
            await api.moveTask(task.id, project.id, task.version);
            if (status) {
              await api.request("PATCH", `/api/tasks/${task.id}/status`, { status, version: task.version + 1 });
            }
          };
          await seedTask("Zadanie w backlogu", "a0");
          await seedTask("Zadanie do zrobienia", "a1", "todo");
          await seedTask("Zadanie w toku", "a2", "in_progress");
          await seedTask("Zadanie zrobione", "a3", "done");
          await seedTask("Zadanie anulowane", "a4", "cancelled");

          const sessionId = await insertSession(profile.id);
          const context = await browser.newContext({
            viewport: { width, height: 900 },
            reducedMotion: "reduce",
          });
          await context.addCookies([
            { name: "taskflow_session", value: sessionId, url: "http://localhost:3000" },
          ]);
          await context.addInitScript(
            ({ id, mode }) => {
              window.localStorage.setItem(`taskflow.project-view.${id}`, mode);
              window.localStorage.setItem(
                `taskflow.project-groupby.${id}`,
                mode === "list" ? "status" : "none",
              );
            },
            { id: project.id, mode: projection === "board" ? "board" : "list" },
          );
          const page = await context.newPage();
          await page.goto(`/projects/${project.id}`);
          await page.waitForLoadState("networkidle");
          await forcePalette(page, palette);

          await expect(page).toHaveScreenshot(`${projection}-${palette}-${width}.png`, {
            fullPage: true,
            animations: "disabled",
            caret: "hide",
          });

          await context.close();
        });
      }
    }
  }
});

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

import AxeBuilder from "@axe-core/playwright";
import { expect, test, type Page } from "@playwright/test";
import { apiAs, ensureUser, insertSession } from "./helpers/seed";

/**
 * Per-palette accessibility suite (T028, slice 019 — UIT-010, SC-008, D12).
 *
 * Walks each in-scope screen × 4 palettes (class swap on <html> ONLY, per
 * contracts/ui-theme.md) asserting zero WCAG 2.1 AA violations. Runs against the
 * REAL app (self-booted stack) with seeded data so rows/labels/chips render.
 *
 * Branch note: this suite reaches green as US3–US5 migrate the screens — it is part
 * of the red-list driving the sweep until T067 (plan.md Branch-CI note).
 */

const PALETTES = ["dark-cool", "dark-warm", "light-cool", "light-warm"] as const;

/** In-scope screens (S5.1). The project route is resolved at runtime from seeding. */
const STATIC_SCREENS = ["/", "/today", "/upcoming", "/assigned", "/settings"] as const;

async function signedInPage(
  browser: import("@playwright/test").Browser,
  key: string,
): Promise<{ page: Page; context: import("@playwright/test").BrowserContext }> {
  const profile = await ensureUser({
    sub: `google-sub-${key}`,
    email: `${key}@taskflow.test`,
    name: "Axe Walker",
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

async function auditCurrentPage(page: Page, label: string): Promise<void> {
  const results = await new AxeBuilder({ page })
    .withTags(["wcag2a", "wcag2aa", "wcag21a", "wcag21aa"])
    .analyze();
  expect(
    results.violations,
    `${label}: ${results.violations.map((v) => `${v.id} (${v.nodes.length} nodes)`).join(", ")}`,
  ).toEqual([]);
}

for (const screen of STATIC_SCREENS) {
  test.describe(`axe AA — ${screen}`, () => {
    for (const palette of PALETTES) {
      test(`${screen} in ${palette}: zero WCAG 2.1 AA violations`, async ({ browser }) => {
        const { page, context } = await signedInPage(
          browser,
          `axe-${screen.replace(/\W/g, "") || "inbox"}-${palette}`,
        );
        await page.goto(screen);
        await page.waitForLoadState("networkidle");
        await setPalette(page, palette);
        await auditCurrentPage(page, `${screen} × ${palette}`);
        await context.close();
      });
    }
  });
}

/**
 * Slice 010 (T021): the project BOARD view joins the per-palette walk — seeded columns
 * with cards (chips + an empty column) audited in all four palettes [INV-142].
 */
test.describe("axe AA — project board (slice 010)", () => {
  for (const palette of PALETTES) {
    test(`board in ${palette}: zero WCAG 2.1 AA violations [INV-142]`, async ({ browser }) => {
      const profile = await ensureUser({
        sub: `google-sub-axe-board-${palette}`,
        email: `axe-board-${palette}@taskflow.test`,
        name: "Axe Walker",
      });
      const api = apiAs(profile.id);
      const project = await api.createProject({ name: "Tablica AA", color: "blue", icon: "folder" });
      const seeded = await api.createTask({ title: "Karta na tablicy", position: "a0" });
      await api.moveTask(seeded.id, project.id, seeded.version);
      const moved = await api.request("PATCH", `/api/tasks/${seeded.id}/status`, {
        status: "in_progress",
        version: seeded.version + 1,
      });
      if (!moved.ok) throw new Error(`status seed failed (${String(moved.status)})`);

      const sessionId = await insertSession(profile.id);
      const context = await browser.newContext();
      await context.addCookies([
        { name: "taskflow_session", value: sessionId, url: "http://localhost:3000" },
      ]);
      // Force the board projection before load (per-project localStorage — D8).
      await context.addInitScript((id) => {
        window.localStorage.setItem(`taskflow.project-view.${id}`, "board");
      }, project.id);
      const page = await context.newPage();
      await page.goto(`/projects/${project.id}`);
      await page.waitForLoadState("networkidle");
      await setPalette(page, palette);
      await auditCurrentPage(page, `board × ${palette}`);
      await context.close();
    });
  }
});

test.describe("axe AA — sign-in (anonymous)", () => {
  for (const palette of PALETTES) {
    test(`/signin in ${palette}: zero WCAG 2.1 AA violations`, async ({ browser }) => {
      const context = await browser.newContext();
      const page = await context.newPage();
      await page.goto("/signin");
      await setPalette(page, palette);
      await auditCurrentPage(page, `/signin × ${palette}`);
      await context.close();
    });
  }
});

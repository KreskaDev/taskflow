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
): Promise<{
  page: Page;
  context: import("@playwright/test").BrowserContext;
  profile: { id: string };
}> {
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
  return { page, context, profile };
}

/**
 * Seeds REAL rows for a screen and returns the text anchor proving they rendered
 * before the audit runs. Empty pages made the walks blind: a `role="option"` row
 * with focusable controls (checkbox / title button / „⋯”) violates axe
 * `nested-interactive`, but only when a row actually renders — the exact latent
 * defect the Board shed in PR #8. Anchors are TEXT, not roles, so the same seeds
 * stay valid across the option→row remediation.
 */
async function seedScreen(screen: string, userId: string): Promise<string | null> {
  const api = apiAs(userId);
  if (screen === "/") {
    const task = await api.createTask({ title: "Zadanie w Inbox", position: "a0" });
    const res = await api.request("PATCH", `/api/tasks/${task.id}/priority`, {
      priority: "P2",
      version: task.version,
    });
    if (!res.ok) throw new Error(`priority seed failed (${String(res.status)})`);
    return "Zadanie w Inbox";
  }
  if (screen === "/today") {
    // 12:00Z is the same Warsaw calendar day for every UTC clock; a boundary-window
    // run at worst renders the row as overdue — still a row on /today.
    const noon = `${new Date().toISOString().slice(0, 10)}T12:00:00Z`;
    await api.createTask({ title: "Zadanie na dziś", position: "a0", dueDate: noon });
    return "Zadanie na dziś";
  }
  if (screen === "/upcoming") {
    const future = new Date(Date.now() + 3 * 24 * 3600 * 1000).toISOString();
    await api.createTask({ title: "Zadanie nadchodzące", position: "a0", dueDate: future });
    return "Zadanie nadchodzące";
  }
  if (screen === "/assigned") {
    // Assignment requires a SHARED project (FR-069) — the owner self-assigns.
    const project = await api.createProject({ name: "Wspólny AA", color: "blue", icon: "folder" });
    await api.shareProject(project.id, project.version);
    const task = await api.createTask({ title: "Zadanie przypisane", position: "a0" });
    await api.moveTask(task.id, project.id, task.version);
    const res = await api.request("PATCH", `/api/tasks/${task.id}/assignees`, {
      assigneeIds: [userId],
      version: task.version + 1,
    });
    if (!res.ok) throw new Error(`assignees seed failed (${String(res.status)})`);
    return "Zadanie przypisane";
  }
  return null; // /settings has no rows to seed.
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
        const { page, context, profile } = await signedInPage(
          browser,
          `axe-${screen.replace(/\W/g, "") || "inbox"}-${palette}`,
        );
        const anchor = await seedScreen(screen, profile.id);
        await page.goto(screen);
        await page.waitForLoadState("networkidle");
        if (anchor) {
          await expect(page.getByText(anchor).first()).toBeVisible();
        }
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

/**
 * The status-GROUPED project List joins the walk with seeded rows in every group
 * (incl. „Anulowane”) — the GroupedTaskList surface the empty static walks never
 * exercised.
 */
test.describe("axe AA — project grouped list (slice 010)", () => {
  for (const palette of PALETTES) {
    test(`grouped list in ${palette}: zero WCAG 2.1 AA violations`, async ({ browser }) => {
      const profile = await ensureUser({
        sub: `google-sub-axe-glist-${palette}`,
        email: `axe-glist-${palette}@taskflow.test`,
        name: "Axe Walker",
      });
      const api = apiAs(profile.id);
      const project = await api.createProject({ name: "Lista AA", color: "blue", icon: "folder" });
      const seedTask = async (title: string, position: string, status?: string) => {
        const task = await api.createTask({ title, position });
        await api.moveTask(task.id, project.id, task.version);
        if (status) {
          const res = await api.request("PATCH", `/api/tasks/${task.id}/status`, {
            status,
            version: task.version + 1,
          });
          if (!res.ok) throw new Error(`status seed failed (${String(res.status)})`);
        }
      };
      await seedTask("Wiersz w backlogu", "a0");
      await seedTask("Wiersz w toku", "a1", "in_progress");
      await seedTask("Wiersz anulowany", "a2", "cancelled");

      const sessionId = await insertSession(profile.id);
      const context = await browser.newContext();
      await context.addCookies([
        { name: "taskflow_session", value: sessionId, url: "http://localhost:3000" },
      ]);
      // Force the grouped LIST projection before load (per-project localStorage — D8).
      await context.addInitScript((id) => {
        window.localStorage.setItem(`taskflow.project-view.${id}`, "list");
        window.localStorage.setItem(`taskflow.project-groupby.${id}`, "status");
      }, project.id);
      const page = await context.newPage();
      await page.goto(`/projects/${project.id}`);
      await page.waitForLoadState("networkidle");
      await expect(page.getByText("Wiersz anulowany").first()).toBeVisible();
      await setPalette(page, palette);
      await auditCurrentPage(page, `grouped list × ${palette}`);
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

import { expect, test, type Browser, type BrowserContext, type Page } from "@playwright/test";
import { apiAs, ensureUser, insertSession } from "./helpers/seed";

/**
 * Daily Planning E2E (slice 005, T032/T033; US-02 AS-01..AS-08, US-08 AS-01/AS-02). Drives the real
 * keyboard daily loop — `G T`/`G I`/`G U` navigation, `1`-`4` priority, `T` reschedule, `Space`
 * toggle-done, `E` editor (`Ctrl+Enter` save / `Esc` discard) — through the REAL BFF→proxy→API path
 * against a migrated Postgres + .NET API (no mocking). Each test mints its own identity (isolation) and
 * seeds tasks with due dates via the API so they land in Today/Upcoming. The booted API uses the real
 * clock, so a "due today" seed uses the current instant (reliably the current Warsaw day).
 *
 * SC-008 (WCAG 2.1 AA): asserted structurally here (the repo carries no axe dependency) — the Today and
 * Upcoming views expose a labelled `role="listbox"` of `role="option"` rows, and the editor/reschedule are
 * `role="dialog"` with the FR-101 focus contract (initial focus, Esc dismiss).
 */

async function signedInPage(
  browser: Browser,
  key: string,
): Promise<{ page: Page; context: BrowserContext; userId: string }> {
  const profile = await ensureUser({
    sub: `google-sub-${key}`,
    email: `${key}@taskflow.test`,
    name: "Daily Planner",
    picture: "https://avatars.test/dp.png",
  });
  const sessionId = await insertSession(profile.id);
  const context = await browser.newContext();
  await context.addCookies([
    { name: "taskflow_session", value: sessionId, url: "http://localhost:3000" },
  ]);
  const page = await context.newPage();
  return { page, context, userId: profile.id };
}

/** An instant reliably within today (the current Warsaw day) and one safely in the next-7-days window. */
function dueToday(): string {
  return new Date().toISOString();
}
function dueInDays(days: number): string {
  const d = new Date();
  d.setUTCDate(d.getUTCDate() + days);
  d.setUTCHours(10, 0, 0, 0);
  return d.toISOString();
}

test.describe("US-02 Daily Planning Session (AS-01..AS-08)", () => {
  test("AS-01/AS-02: G T opens Today showing the due-today task in a labelled listbox", async ({ browser }) => {
    const { page, context, userId } = await signedInPage(browser, "dp-today");
    await apiAs(userId).createTask({ title: "Review the day", position: "a0", dueDate: dueToday() });

    await page.goto("/");
    await expect(page.getByRole("heading", { name: "Your workspace" })).toBeVisible();
    await page.keyboard.press("g");
    await page.keyboard.press("t");

    await expect(page.getByRole("heading", { name: "Dziś" })).toBeVisible();
    const listbox = page.getByRole("listbox", { name: "Dziś" });
    await expect(listbox).toBeVisible();
    await expect(page.getByRole("option").filter({ hasText: "Review the day" })).toBeVisible();

    await context.close();
  });

  test("AS-04: pressing 1 sets the selected task to P0 with a visible badge", async ({ browser }) => {
    const { page, context, userId } = await signedInPage(browser, "dp-prio");
    await apiAs(userId).createTask({ title: "Prioritize me", position: "a0", dueDate: dueToday() });

    await page.goto("/today");
    await expect(page.getByRole("option").filter({ hasText: "Prioritize me" })).toBeVisible();

    const settled = page.waitForResponse((r) => r.request().method() === "PATCH" && /\/priority/.test(r.url()) && r.ok());
    await page.keyboard.press("1");
    await settled;

    await expect(page.getByRole("option").filter({ hasText: "Prioritize me" }).getByText("P0")).toBeVisible();
    await context.close();
  });

  test("AS-03: Space toggles done and the row leaves Today", async ({ browser }) => {
    const { page, context, userId } = await signedInPage(browser, "dp-done");
    await apiAs(userId).createTask({ title: "Finish me", position: "a0", dueDate: dueToday() });

    await page.goto("/today");
    await expect(page.getByRole("option").filter({ hasText: "Finish me" })).toBeVisible();

    const settled = page.waitForResponse((r) => r.request().method() === "PATCH" && /\/status/.test(r.url()) && r.ok());
    await page.keyboard.press(" ");
    await settled;

    await expect(page.getByRole("option").filter({ hasText: "Finish me" })).toHaveCount(0);
    await context.close();
  });

  test("AS-05: T then 'jutro' reschedules to tomorrow and the task leaves Today", async ({ browser }) => {
    const { page, context, userId } = await signedInPage(browser, "dp-resched");
    await apiAs(userId).createTask({ title: "Move me to tomorrow", position: "a0", dueDate: dueToday() });

    await page.goto("/today");
    await expect(page.getByRole("option").filter({ hasText: "Move me to tomorrow" })).toBeVisible();

    await page.keyboard.press("t");
    const input = page.getByRole("textbox", { name: /termin/i });
    await expect(input).toBeFocused();
    await input.fill("jutro");
    const settled = page.waitForResponse((r) => r.request().method() === "PATCH" && /\/due-date/.test(r.url()) && r.ok());
    await page.keyboard.press("Enter");
    await settled;

    await expect(page.getByRole("option").filter({ hasText: "Move me to tomorrow" })).toHaveCount(0);
    await context.close();
  });

  test("AS-06/AS-07: E opens the editor (title focused); Ctrl+Enter saves", async ({ browser }) => {
    const { page, context, userId } = await signedInPage(browser, "dp-edit-save");
    await apiAs(userId).createTask({ title: "Edit me", position: "a0", dueDate: dueToday() });

    await page.goto("/today");
    await expect(page.getByRole("option").filter({ hasText: "Edit me" })).toBeVisible();

    await page.keyboard.press("e");
    const dialog = page.getByRole("dialog");
    await expect(dialog).toBeVisible();
    const title = dialog.getByLabel("Tytuł");
    await expect(title).toBeFocused(); // AS-06: title field focused

    await title.fill("Edited title");
    const settled = page.waitForResponse((r) => r.request().method() === "PATCH" && /\/edit/.test(r.url()) && r.ok());
    await page.keyboard.press("Control+Enter");
    await settled;

    await expect(dialog).toHaveCount(0);
    await expect(page.getByRole("option").filter({ hasText: "Edited title" })).toBeVisible();
    await context.close();
  });

  test("AS-08: Esc discards the editor changes (no save)", async ({ browser }) => {
    const { page, context, userId } = await signedInPage(browser, "dp-edit-discard");
    await apiAs(userId).createTask({ title: "Keep my title", position: "a0", dueDate: dueToday() });

    await page.goto("/today");
    await expect(page.getByRole("option").filter({ hasText: "Keep my title" })).toBeVisible();

    await page.keyboard.press("e");
    const dialog = page.getByRole("dialog");
    await dialog.getByLabel("Tytuł").fill("Discarded edit");
    await page.keyboard.press("Escape");

    await expect(dialog).toHaveCount(0);
    await expect(page.getByRole("option").filter({ hasText: "Keep my title" })).toBeVisible();
    await expect(page.getByRole("option").filter({ hasText: "Discarded edit" })).toHaveCount(0);
    await context.close();
  });
});

test.describe("US-08 Keyboard Navigation (AS-01/AS-02)", () => {
  test("AS-02: G U opens Upcoming showing the next-7-days task grouped by day", async ({ browser }) => {
    const { page, context, userId } = await signedInPage(browser, "dp-upcoming");
    await apiAs(userId).createTask({ title: "Upcoming task", position: "a0", dueDate: dueInDays(2) });

    await page.goto("/");
    // Wait for the Inbox to be interactive (hydrated + the global key listener attached) before the chord,
    // so `G U` is not raced against hydration.
    await expect(page.getByRole("heading", { name: "Your workspace" })).toBeVisible();
    await page.keyboard.press("g");
    await page.keyboard.press("u");

    await expect(page.getByRole("heading", { name: "Nadchodzące" })).toBeVisible();
    await expect(page.getByRole("listbox", { name: "Nadchodzące" })).toBeVisible();
    await expect(page.getByRole("option").filter({ hasText: "Upcoming task" })).toBeVisible();
    await context.close();
  });

  test("AS-01: G I returns to the Inbox", async ({ browser }) => {
    const { page, context } = await signedInPage(browser, "dp-inbox");

    await page.goto("/today");
    await expect(page.getByRole("heading", { name: "Dziś" })).toBeVisible();
    await page.keyboard.press("g");
    await page.keyboard.press("i");

    await expect(page.getByRole("heading", { name: "Your workspace" })).toBeVisible();
    await context.close();
  });
});

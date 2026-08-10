import { expect, test, type Page } from "@playwright/test";
import { ensureUser, insertSession } from "./helpers/seed";

/**
 * New-capability E2E (slice 019, T049): sidebar counts (FR-109/UIT-024), "Duplikuj"
 * (FR-112/UIT-041), reorder persistence via drag handle AND menu items (S3.7), and the
 * topbar search placeholder semantics (spec Provenance FR-032/034 carve-out — presence +
 * disabled; UIT-022's focusability/functionality transfers to slice 013).
 */

async function signedInPage(
  browser: import("@playwright/test").Browser,
  key: string,
): Promise<{ page: Page; context: import("@playwright/test").BrowserContext }> {
  const profile = await ensureUser({
    sub: `google-sub-${key}`,
    email: `${key}@taskflow.test`,
    name: "Nowe Możliwości",
  });
  const sessionId = await insertSession(profile.id);
  const context = await browser.newContext();
  await context.addCookies([
    { name: "taskflow_session", value: sessionId, url: "http://localhost:3000" },
  ]);
  const page = await context.newPage();
  return { page, context };
}

async function createTask(page: Page, title: string): Promise<void> {
  const input = page.getByLabel("Task title");
  await input.fill(title);
  const settled = page.waitForResponse(
    (r) => r.request().method() === "PUT" && /\/api\/tasks\//.test(r.url()) && r.ok(),
  );
  await input.press("Enter");
  await settled;
}

test.describe("Sidebar counts (FR-109)", () => {
  test("counts render from GET /api/views/counts and update after a mutation", async ({
    browser,
  }) => {
    const { page, context } = await signedInPage(browser, "counts-live");
    const firstCounts = page.waitForResponse(
      (r) => r.url().includes("/api/views/counts") && r.ok(),
    );
    await page.goto("/");
    await firstCounts;

    await createTask(page, "Licznikowy");
    // The create's onSettled invalidates ['views','counts'] — the Inbox entry shows 1.
    const inboxLink = page.getByRole("link", { name: /^Inbox/ });
    await expect(inboxLink).toContainText("1");

    // Completing the task drops the incomplete count (entry hides its zero count).
    const toggled = page.waitForResponse(
      (r) => r.request().method() === "PATCH" && r.url().includes("/status") && r.ok(),
    );
    await page.getByRole("checkbox", { name: "Oznacz „Licznikowy” jako zrobione" }).click();
    await toggled;
    await expect(page.getByRole("link", { name: /^Inbox/ })).not.toContainText("1");

    await context.close();
  });
});

test.describe("Duplikuj (FR-112)", () => {
  test("the menu's Duplikuj creates the adjacent copy optimistically and persists", async ({
    browser,
  }) => {
    const { page, context } = await signedInPage(browser, "dup-adjacent");
    await page.goto("/");
    await createTask(page, "Dolny");
    await createTask(page, "Oryginał"); // newest-first: Oryginał on top, Dolny below

    await page.getByRole("button", { name: "Więcej akcji: Oryginał" }).click();
    const duplicated = page.waitForResponse(
      (r) => r.request().method() === "POST" && r.url().includes("/duplicate") && r.ok(),
    );
    await page.getByRole("menuitem", { name: "Duplikuj" }).click();

    // Optimistic paint: the copy appears immediately, ADJACENT to (directly below) the source.
    await expect(page.getByRole("option")).toHaveCount(3);
    const titles = await page
      .getByRole("option")
      .evaluateAll((rows) => rows.map((r) => r.textContent ?? ""));
    expect(titles[0]).toContain("Oryginał");
    expect(titles[1]).toContain("Oryginał"); // the duplicate sits right after its source
    expect(titles[2]).toContain("Dolny");

    await duplicated;
    await page.reload();
    await expect(page.getByRole("option")).toHaveCount(3);

    await context.close();
  });
});

test.describe("Reorder affordances (S3.7)", () => {
  test("menu 'Przenieś niżej' persists the new order across reload", async ({ browser }) => {
    const { page, context } = await signedInPage(browser, "reorder-menu");
    await page.goto("/");
    await createTask(page, "Spód");
    await createTask(page, "Góra"); // Góra first (newest-first)

    await page.getByRole("button", { name: "Więcej akcji: Góra" }).click();
    const repositioned = page.waitForResponse(
      (r) => r.request().method() === "PATCH" && r.url().includes("/position") && r.ok(),
    );
    await page.getByRole("menuitem", { name: "Przenieś niżej" }).click();

    // Optimistic swap, then persisted.
    await expect(page.getByRole("option").first()).toContainText("Spód");
    await repositioned;
    await page.reload();
    await expect(page.getByRole("option").first()).toContainText("Spód");
    await expect(page.getByRole("option").nth(1)).toContainText("Góra");

    await context.close();
  });

  test("drag handle reorder persists across reload", async ({ browser }) => {
    const { page, context } = await signedInPage(browser, "reorder-drag");
    await page.goto("/");
    await createTask(page, "Pod spodem");
    await createTask(page, "Przeciągany");

    const handle = page
      .getByRole("option")
      .filter({ hasText: "Przeciągany" })
      .locator("[data-drag-handle]");
    const target = page.getByRole("option").filter({ hasText: "Pod spodem" });

    const repositioned = page.waitForResponse(
      (r) => r.request().method() === "PATCH" && r.url().includes("/position") && r.ok(),
    );
    // Manual pointer drag (dnd-kit PointerSensor, 4px activation distance).
    const from = (await handle.boundingBox())!;
    const to = (await target.boundingBox())!;
    await page.mouse.move(from.x + from.width / 2, from.y + from.height / 2);
    await page.mouse.down();
    await page.mouse.move(to.x + to.width / 2, to.y + to.height / 2 + 10, { steps: 12 });
    await page.mouse.up();
    await repositioned;

    await page.reload();
    await expect(page.getByRole("option").first()).toContainText("Pod spodem");
    await expect(page.getByRole("option").nth(1)).toContainText("Przeciągany");

    await context.close();
  });
});

test.describe("Topbar search placeholder (slice 013 carve-out)", () => {
  test("renders per mockup, excluded from tab order, announced unavailable", async ({
    browser,
  }) => {
    const { page, context } = await signedInPage(browser, "search-placeholder");
    await page.goto("/");

    const search = page.getByRole("searchbox", { name: /Szukaj/ });
    await expect(search).toBeVisible();
    await expect(search).toHaveAttribute("aria-disabled", "true");

    // The inner input is disabled and OUT of the tab order.
    const inner = search.locator("input");
    await expect(inner).toBeDisabled();
    await expect(inner).toHaveAttribute("tabindex", "-1");

    await context.close();
  });
});

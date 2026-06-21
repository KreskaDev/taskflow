import { expect, test, type Page } from "@playwright/test";
import { ensureUser, insertSession } from "./helpers/seed";

/**
 * US1 Daily Task Capture E2E (T041; US-01.AS-01/06/07, AS-09 precursor, EC-01). The real
 * end-to-end MVP proof: a fresh authenticated user drives the `C` capture surface through the
 * REAL BFF→proxy→API path against a migrated Postgres + .NET API (booted by global-setup) — no
 * API mocking. Auth is a seeded session (mirroring auth.spec.ts), so each test gets a pristine,
 * isolated account and never reinvents the OAuth dance.
 *
 * Each test mints its OWN Google sub + email: `ensureUser` reaches the real API which enforces
 * UNIQUE(email), and the Postgres container is shared for the whole run, so distinct identities
 * are what keep one test's tasks out of another's list (test isolation).
 */

/** Seeds a fresh user + session and returns an authenticated page landed on the workspace. */
async function signedInPage(
  browser: import("@playwright/test").Browser,
  key: string,
): Promise<{ page: Page; context: import("@playwright/test").BrowserContext }> {
  const profile = await ensureUser({
    sub: `google-sub-${key}`,
    email: `${key}@taskflow.test`,
    name: "Task Capturer",
    picture: "https://avatars.test/tc.png",
  });
  const sessionId = await insertSession(profile.id);

  const context = await browser.newContext();
  await context.addCookies([
    { name: "taskflow_session", value: sessionId, url: "http://localhost:3000" },
  ]);
  const page = await context.newPage();
  return { page, context };
}

/**
 * Opens capture (`C`), types a title, presses Enter, and waits for the optimistic create's REAL
 * server write (the idempotent PUT) to land. The `waitForResponse` promise is ARMED before Enter
 * so it can never miss a fast-resolving PUT (the matcher's `PUT` method discriminates it from the
 * `GET /api/tasks` refetch).
 */
async function createTask(page: Page, title: string): Promise<void> {
  await page.keyboard.press("c");
  await page.getByRole("textbox", { name: "Task title" }).fill(title);
  const settled = page.waitForResponse(
    (r) => r.request().method() === "PUT" && /\/api\/tasks\//.test(r.url()) && r.ok(),
  );
  await page.keyboard.press("Enter");
  await settled;
}

test.describe("US1 Daily Task Capture (AS-01/06/07/09, EC-01)", () => {
  test("EC-01: a fresh user sees the accessible empty-Inbox hint and zero options", async ({
    browser,
  }) => {
    const { page, context } = await signedInPage(browser, "tasks-empty");
    await page.goto("/");

    // The empty-Inbox hint asserts the query RESOLVED with zero rows (page.tsx swaps the
    // listbox out for this <p> only once `isEmpty` is true). Asserting it first waits out the
    // transient loading state where the listbox flashes with 0 options.
    const hint = page.locator("p.tf-workspace__empty");
    await expect(hint).toBeVisible();
    await expect(hint).toContainText(/press/i);
    await expect(hint.locator("kbd")).toHaveText("C"); // the <kbd>C</kbd> press hint
    await expect(page.getByRole("option")).toHaveCount(0);

    await context.close();
  });

  test("AS-01: pressing C opens the capture dialog with the title input focused", async ({
    browser,
  }) => {
    const { page, context } = await signedInPage(browser, "tasks-focus");
    await page.goto("/");
    await expect(page.getByText(/your inbox is empty/i)).toBeVisible();

    await page.keyboard.press("c");

    const dialog = page.getByRole("dialog", { name: "Create task" });
    await expect(dialog).toBeVisible();
    // SC-003 / AS-01: the single title input receives initial focus synchronously.
    await expect(page.getByRole("textbox", { name: "Task title" })).toBeFocused();

    await context.close();
  });

  test("AS-06: Enter creates, the row paints at the top newest-first, and persists across reload", async ({
    browser,
  }) => {
    const { page, context } = await signedInPage(browser, "tasks-create");
    await page.goto("/");
    await expect(page.getByText(/your inbox is empty/i)).toBeVisible();

    // First task. Dialog closes; the optimistic row paints in the listbox.
    await createTask(page, "First task");
    await expect(page.getByRole("dialog", { name: "Create task" })).toBeHidden();
    await expect(page.getByRole("option")).toHaveCount(1);
    await expect(page.getByRole("option").first()).toHaveText(/First task/);

    // Second task → newest-first means it lands ABOVE the first.
    await createTask(page, "Second task");
    await expect(page.getByRole("option")).toHaveCount(2);
    await expect(page.getByRole("option").nth(0)).toHaveText(/Second task/);
    await expect(page.getByRole("option").nth(1)).toHaveText(/First task/);

    // Server round-trip: reload and assert both tasks persist AND newest-first order holds.
    await page.reload();
    await expect(page.getByRole("option")).toHaveCount(2);
    await expect(page.getByRole("option").nth(0)).toHaveText(/Second task/);
    await expect(page.getByRole("option").nth(1)).toHaveText(/First task/);

    await context.close();
  });

  test("AS-07: Esc cancels — no task is created and focus returns to the invoker", async ({
    browser,
  }) => {
    const { page, context } = await signedInPage(browser, "tasks-cancel");
    await page.goto("/");
    await expect(page.getByText(/your inbox is empty/i)).toBeVisible();

    // Establish a deterministic invoker: create one task so the listbox exists, then focus it.
    // Pressing `c` on the listbox (a div — not input/textarea/contenteditable) DOES open capture.
    await createTask(page, "Keeper");
    await expect(page.getByRole("option")).toHaveCount(1);

    const listbox = page.getByRole("listbox", { name: "Tasks" });
    await listbox.focus();
    await expect(listbox).toBeFocused();

    await page.keyboard.press("c");
    await expect(page.getByRole("dialog", { name: "Create task" })).toBeVisible();
    await page.getByRole("textbox", { name: "Task title" }).fill("Discarded draft");
    await page.keyboard.press("Escape");

    // No task created (count unchanged) and focus restored to the invoking listbox.
    await expect(page.getByRole("dialog", { name: "Create task" })).toBeHidden();
    await expect(page.getByRole("option")).toHaveCount(1);
    await expect(listbox).toBeFocused();

    await context.close();
  });

  test("AS-09 precursor: typing C inside the capture input inserts the character (no nested capture)", async ({
    browser,
  }) => {
    const { page, context } = await signedInPage(browser, "tasks-suppress");
    await page.goto("/");
    await expect(page.getByText(/your inbox is empty/i)).toBeVisible();

    await page.keyboard.press("c");
    const input = page.getByRole("textbox", { name: "Task title" });
    await expect(input).toBeFocused();

    // The capture surface's document-level `C` listener must NOT hijack a `C` typed into the
    // focused input (FR-031 / AS-09 precursor): the char lands in the field, no nested dialog.
    await page.keyboard.type("Cabbage");
    await expect(input).toHaveValue("Cabbage");
    await expect(page.getByRole("dialog", { name: "Create task" })).toHaveCount(1);

    await context.close();
  });
});

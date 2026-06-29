import { expect, test, type Browser, type BrowserContext, type Page } from "@playwright/test";
import { apiAs, ensureUser, insertSession } from "./helpers/seed";

/**
 * Labels E2E (slice 006, US-08.AS-04). Drives the real keyboard label flow through the real BFF→API path
 * (no mocking): `L` opens the label selector on the selected task; type-to-create a new label + Ctrl+Enter
 * applies it; the row shows the label NAME chip; re-opening with `L` shows it checked; unchecking + saving
 * removes it.
 *
 * SC-008 (a11y): asserted structurally — the selector is a labelled role="dialog" with role="checkbox" rows
 * (FR-101 focus contract) and the chip carries the label NAME (FR-044, never color alone).
 */

async function signedInPage(browser: Browser, key: string): Promise<{ page: Page; context: BrowserContext; userId: string }> {
  const email = `${key}@taskflow.test`;
  const profile = await ensureUser({ sub: `google-sub-${key}`, email, name: `User ${key}`, picture: "https://avatars.test/u.png" });
  const sessionId = await insertSession(profile.id);
  const context = await browser.newContext();
  await context.addCookies([{ name: "taskflow_session", value: sessionId, url: "http://localhost:3000" }]);
  const page = await context.newPage();
  return { page, context, userId: profile.id };
}

test.describe("US-08.AS-04 Labels (the L selector)", () => {
  test("press L → create + apply a label by keyboard → the chip appears → remove it", async ({ browser }) => {
    const { page, context, userId } = await signedInPage(browser, "lbl-owner");
    await apiAs(userId).createTask({ title: "Buy milk", position: "a0" });

    await page.goto("/");
    const row = page.getByRole("option").filter({ hasText: "Buy milk" });
    await expect(row).toBeVisible();

    // AS-04: `L` opens the label selector dialog (FR-101).
    await page.keyboard.press("l");
    const dialog = page.getByRole("dialog");
    await expect(dialog).toBeVisible();

    // Type-to-create a new label, then commit the set with Ctrl+Enter (setTaskLabels).
    const createInput = dialog.getByPlaceholder(/Nowa etykieta/);
    await createInput.fill("pilne");
    const created = page.waitForResponse((r) => r.request().method() === "PUT" && /\/api\/labels\//.test(r.url()) && r.ok());
    await createInput.press("Enter");
    await created;
    const applied = page.waitForResponse((r) => r.request().method() === "PATCH" && /\/labels$/.test(r.url()) && r.ok());
    await page.keyboard.press("Control+Enter");
    await applied;
    await expect(dialog).toHaveCount(0);

    // The row shows the label NAME chip (FR-044 — name, not color alone).
    await expect(row.getByText("pilne")).toBeVisible();

    // Re-open with `L`: the label is checked (the application persisted).
    await page.keyboard.press("l");
    const reopened = page.getByRole("dialog");
    const checkbox = reopened.getByRole("checkbox", { name: /pilne/ });
    await expect(checkbox).toBeChecked();

    // Uncheck + save → the chip is removed.
    await checkbox.uncheck();
    const removed = page.waitForResponse((r) => r.request().method() === "PATCH" && /\/labels$/.test(r.url()) && r.ok());
    await page.keyboard.press("Control+Enter");
    await removed;
    await expect(reopened).toHaveCount(0);
    await expect(row.getByText("pilne")).toHaveCount(0);

    await context.close();
  });
});

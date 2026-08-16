import { expect, test, type Browser, type BrowserContext, type Page } from "@playwright/test";
import { apiAs, ensureUser, insertSession } from "./helpers/seed";

/**
 * Labels E2E (slice 006, US-08.AS-04 — re-driven through the slice-019 UI after FR-111 removed the
 * single-key shortcut system). The label flow now runs on visible affordances: the row's "⋯" menu
 * ("Akcje taska") carries "Etykiety…" which opens the label selector; type-to-create a new label +
 * Zapisz applies it; the row shows the label NAME chip; re-opening shows it checked; unchecking +
 * saving removes it — through the real BFF→API path (no mocking).
 *
 * SC-008 (a11y): asserted structurally — the selector is a labelled role="dialog" with role="checkbox"
 * rows (FR-101 focus contract) and the chip carries the label NAME (FR-044, never color alone).
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

test.describe("US-08.AS-04 Labels (the label selector)", () => {
  test("menu 'Etykiety…' → create + apply a label → the chip appears → remove it [INV-100] [INV-101] [INV-102] [INV-103]", async ({ browser }) => {
    const { page, context, userId } = await signedInPage(browser, "lbl-owner");
    await apiAs(userId).createTask({ title: "Buy milk", position: "a0" });

    await page.goto("/");
    const row = page.getByRole("row").filter({ hasText: "Buy milk" });
    await expect(row).toBeVisible();

    // AS-04: the row's "⋯" menu carries "Etykiety…" which opens the selector dialog (FR-101).
    await page.getByRole("button", { name: "Więcej akcji: Buy milk" }).click();
    await page.getByRole("menu", { name: "Akcje taska" }).getByRole("menuitem", { name: "Etykiety…" }).click();
    const dialog = page.getByRole("dialog", { name: "Etykiety" });
    await expect(dialog).toBeVisible();

    // Type-to-create a new label (plain Enter creates via the idempotent PUT), then commit the set.
    const createInput = dialog.getByPlaceholder(/Nowa etykieta/);
    await createInput.fill("pilne");
    const created = page.waitForResponse((r) => r.request().method() === "PUT" && /\/api\/labels\//.test(r.url()) && r.ok());
    await createInput.press("Enter");
    await created;
    const applied = page.waitForResponse((r) => r.request().method() === "PATCH" && /\/labels$/.test(r.url()) && r.ok());
    await dialog.getByRole("button", { name: "Zapisz (Ctrl+Enter)" }).click();
    await applied;
    await expect(dialog).toHaveCount(0);

    // The row shows the label NAME chip (FR-044 — name, not color alone).
    await expect(row.getByText("pilne")).toBeVisible();

    // Re-open via the menu: the label is checked (the application persisted).
    await page.getByRole("button", { name: "Więcej akcji: Buy milk" }).click();
    await page.getByRole("menu", { name: "Akcje taska" }).getByRole("menuitem", { name: "Etykiety…" }).click();
    const reopened = page.getByRole("dialog", { name: "Etykiety" });
    const checkbox = reopened.getByRole("checkbox", { name: /pilne/ });
    await expect(checkbox).toBeChecked();

    // Uncheck + save → the chip is removed.
    await checkbox.uncheck();
    const removed = page.waitForResponse((r) => r.request().method() === "PATCH" && /\/labels$/.test(r.url()) && r.ok());
    await reopened.getByRole("button", { name: "Zapisz (Ctrl+Enter)" }).click();
    await removed;
    await expect(reopened).toHaveCount(0);
    await expect(row.getByText("pilne")).toHaveCount(0);

    await context.close();
  });
});

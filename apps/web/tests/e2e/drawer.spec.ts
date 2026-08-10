import { expect, test, type Page } from "@playwright/test";
import { ensureUser, insertSession } from "./helpers/seed";

/**
 * Task drawer E2E (slice 019, T055 — FR-106, D8): non-modal interactivity +
 * Esc-with-field-exception (S4.2), deep link / back-forward / list-state (S4.3, UIT-054),
 * and the FR-049 error state for an invalid id. The comment-thread regression (S4.4)
 * lives in comments.spec.ts, rewritten against the drawer.
 */

async function signedInPage(
  browser: import("@playwright/test").Browser,
  key: string,
): Promise<{ page: Page; context: import("@playwright/test").BrowserContext }> {
  const profile = await ensureUser({
    sub: `google-sub-${key}`,
    email: `${key}@taskflow.test`,
    name: "Otwieracz Paneli",
  });
  const sessionId = await insertSession(profile.id);
  const context = await browser.newContext();
  await context.addCookies([
    { name: "taskflow_session", value: sessionId, url: "http://localhost:3000" },
  ]);
  const page = await context.newPage();
  return { page, context };
}

async function createTask(page: Page, title: string): Promise<string> {
  const input = page.getByLabel("Task title");
  await input.fill(title);
  const settled = page.waitForResponse(
    (r) => r.request().method() === "PUT" && /\/api\/tasks\//.test(r.url()) && r.ok(),
  );
  await input.press("Enter");
  const response = await settled;
  const body = (await response.json()) as { id: string };
  return body.id;
}

test.describe("Task drawer (FR-106, S4.2/S4.3)", () => {
  test("title click opens the drawer; the list behind STAYS interactive (non-modal) [INV-036-adjacent]", async ({
    browser,
  }) => {
    const { page, context } = await signedInPage(browser, "drawer-nonmodal");
    await page.goto("/");
    await createTask(page, "Drugi w tle");
    await createTask(page, "Otwierany");

    await page.getByRole("button", { name: "Otwierany", exact: true }).click();
    const drawer = page.getByRole("complementary");
    await expect(drawer).toBeVisible();
    expect(new URL(page.url()).searchParams.get("task")).not.toBeNull();

    // NON-modal: the row behind is still clickable — toggle the OTHER task's checkbox.
    const toggled = page.waitForResponse(
      (r) => r.request().method() === "PATCH" && r.url().includes("/status") && r.ok(),
    );
    await page.getByRole("checkbox", { name: "Oznacz „Drugi w tle” jako zrobione" }).click();
    await toggled;
    await expect(drawer).toBeVisible();

    await context.close();
  });

  test("every field is inline-editable from the drawer (US-18.AS-03)", async ({ browser }) => {
    const { page, context } = await signedInPage(browser, "drawer-edit");
    await page.goto("/");
    await createTask(page, "Do edycji w panelu");

    await page.getByRole("button", { name: "Do edycji w panelu", exact: true }).click();
    const drawer = page.getByRole("complementary");

    // Title inline edit (Enter commits → PATCH /title).
    const renamed = page.waitForResponse(
      (r) => r.request().method() === "PATCH" && r.url().includes("/title") && r.ok(),
    );
    const title = drawer.getByRole("textbox", { name: "Tytuł zadania" });
    await title.fill("Po edycji w panelu");
    await title.press("Enter");
    await renamed;

    // Priority via the drawer's picker.
    const prioritized = page.waitForResponse(
      (r) => r.request().method() === "PATCH" && r.url().includes("/priority") && r.ok(),
    );
    await drawer.getByRole("button", { name: "— brak —" }).click();
    await page.getByRole("dialog", { name: "Priorytet" }).getByRole("button", { name: "P2" }).click();
    await prioritized;

    // Due date via the catalog DateInput (Polish NL parse unchanged).
    const rescheduled = page.waitForResponse(
      (r) => r.request().method() === "PATCH" && r.url().includes("/due-date") && r.ok(),
    );
    const due = drawer.getByRole("textbox", { name: /Nowy termin/ });
    await due.fill("jutro");
    await due.press("Enter");
    await rescheduled;

    // Description via the textarea (Ctrl+Enter commits → PATCH /edit).
    const described = page.waitForResponse(
      (r) => r.request().method() === "PATCH" && r.url().includes("/edit") && r.ok(),
    );
    await drawer.getByRole("textbox", { name: "Opis" }).fill("Opis z panelu");
    await drawer.getByRole("textbox", { name: "Opis" }).press("Control+Enter");
    await described;

    // The list behind repainted optimistically with the new title.
    await expect(page.getByRole("option").filter({ hasText: "Po edycji w panelu" })).toHaveCount(1);

    await context.close();
  });

  test("Esc closes the drawer EXCEPT while a text field inside is focused (S4.2, FR-030)", async ({
    browser,
  }) => {
    const { page, context } = await signedInPage(browser, "drawer-esc");
    await page.goto("/");
    await createTask(page, "Escapologia");

    await page.getByRole("button", { name: "Escapologia", exact: true }).click();
    const drawer = page.getByRole("complementary");

    // Esc INSIDE a text field: cancels the FIELD edit; the drawer stays open.
    const title = drawer.getByRole("textbox", { name: "Tytuł zadania" });
    await title.fill("Zmieniony tekst roboczy");
    await title.press("Escape");
    await expect(drawer).toBeVisible();
    await expect(title).toHaveValue("Escapologia"); // the draft reverted (FR-030)

    // Esc on a NON-field element inside: closes the drawer.
    await drawer.getByRole("button", { name: "Zamknij panel" }).focus();
    await page.keyboard.press("Escape");
    await expect(page.getByRole("complementary")).toHaveCount(0);
    expect(new URL(page.url()).searchParams.get("task")).toBeNull();

    await context.close();
  });

  test("deep link opens the right task; back/forward preserve list state (S4.3, UIT-054)", async ({
    browser,
  }) => {
    const { page, context } = await signedInPage(browser, "drawer-deeplink");
    await page.goto("/");
    const id = await createTask(page, "Głęboki link");

    // Direct navigation to the ?task= URL — the drawer opens on the right task.
    await page.goto(`/?task=${id}`);
    const drawer = page.getByRole("complementary");
    await expect(drawer.getByRole("textbox", { name: "Tytuł zadania" })).toHaveValue("Głęboki link");

    // Browser BACK closes it (URL state), FORWARD restores it; the list stays put.
    await page.goBack();
    await expect(page.getByRole("complementary")).toHaveCount(0);
    await expect(page.getByRole("option")).toHaveCount(1);
    await page.goForward();
    await expect(page.getByRole("complementary")).toBeVisible();
    await expect(page.getByRole("option")).toHaveCount(1);

    await context.close();
  });

  test("an invalid/inaccessible id renders the drawer's error state with recovery (FR-049)", async ({
    browser,
  }) => {
    const { page, context } = await signedInPage(browser, "drawer-error");
    await page.goto("/");
    await createTask(page, "Istniejący");

    await page.goto("/?task=00000000-0000-7000-8000-000000000000");
    const drawer = page.getByRole("complementary");
    await expect(drawer.getByRole("alert")).toContainText("Nie znaleziono zadania");
    await drawer.getByRole("button", { name: "Wróć do listy" }).click();
    await expect(page.getByRole("complementary")).toHaveCount(0);

    await context.close();
  });
});

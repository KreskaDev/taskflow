import { expect, test, type Page } from "@playwright/test";
import { ensureUser, insertSession } from "./helpers/seed";

/**
 * Binding-audit E2E (slice 019, T047 — S3.5, FR-111, D5): with a focused list, every
 * FORMER single-key shortcut does nothing — no action fires, no dead-binding errors, no
 * overlay — while FR-030 editing keys and composite-widget operability stay intact.
 * A and Delete are included per D5 (the shipped hook bound both even though S3.5's list
 * omits them; Delete's keyboard-reachable replacement is the "⋯" menu's Usuń item).
 */

async function signedInPage(
  browser: import("@playwright/test").Browser,
  key: string,
): Promise<{ page: Page; context: import("@playwright/test").BrowserContext }> {
  const profile = await ensureUser({
    sub: `google-sub-${key}`,
    email: `${key}@taskflow.test`,
    name: "Bez Skrótów",
  });
  const sessionId = await insertSession(profile.id);
  const context = await browser.newContext();
  await context.addCookies([
    { name: "taskflow_session", value: sessionId, url: "http://localhost:3000" },
  ]);
  const page = await context.newPage();
  return { page, context };
}

async function createViaInlineCapture(page: Page, title: string): Promise<void> {
  const input = page.getByRole("textbox", { name: "Nowy task" });
  await input.fill(title);
  const settled = page.waitForResponse(
    (r) => r.request().method() === "PUT" && /\/api\/tasks\//.test(r.url()) && r.ok(),
  );
  await input.press("Enter");
  await settled;
}

test.describe("Shortcut-system removal (FR-111, S3.5)", () => {
  test("former bare keys do nothing on a focused list; Delete no longer deletes [INV-120] [INV-121] [INV-122] [INV-123]", async ({
    browser,
  }) => {
    const { page, context } = await signedInPage(browser, "noshort-keys");
    await page.goto("/");
    await createViaInlineCapture(page, "Nietykalny task");

    const listbox = page.getByRole("grid", { name: "Zadania" });
    await expect(page.getByRole("row")).toHaveCount(1);
    await listbox.focus();

    const pageErrors: string[] = [];
    page.on("pageerror", (e) => pageErrors.push(String(e)));

    // Every former binding, incl. the G-chords and the Alt reorder chord.
    for (const key of ["c", "e", "m", "l", "t", "a", "1", "2", "3", "4", "?"]) {
      await page.keyboard.press(key);
    }
    await page.keyboard.press("g");
    await page.keyboard.press("t");
    await page.keyboard.press("g");
    await page.keyboard.press("u");
    await page.keyboard.press("g");
    await page.keyboard.press("i");
    await page.keyboard.press("g");
    await page.keyboard.press("a");
    await page.keyboard.press("Alt+ArrowDown");
    await page.keyboard.press("Alt+ArrowUp");
    await page.keyboard.press("Delete");
    await page.waitForTimeout(300);

    // Nothing fired: no dialog of any kind, no navigation, the task still exists,
    // the shortcuts overlay does not exist anywhere, and no dead-binding errors.
    expect(new URL(page.url()).pathname).toBe("/");
    await expect(page.getByRole("dialog")).toHaveCount(0);
    await expect(page.getByRole("dialog", { name: "Keyboard shortcuts" })).toHaveCount(0);
    await expect(page.getByRole("row")).toHaveCount(1, {
      timeout: 2000,
    });
    expect(pageErrors).toEqual([]);

    await context.close();
  });

  test("former shortcut characters type normally in inputs; Ctrl+Enter/Esc editing keys still work [INV-124] [INV-125]", async ({
    browser,
  }) => {
    const { page, context } = await signedInPage(browser, "noshort-typing");
    await page.goto("/");
    await createViaInlineCapture(page, "Edytowalny");

    // Former shortcut characters land as literal text in the capture input.
    const capture = page.getByRole("textbox", { name: "Nowy task" });
    await capture.click();
    await capture.pressSequentially("celmta1234?");
    await expect(capture).toHaveValue("celmta1234?");
    await expect(page.getByRole("dialog")).toHaveCount(0);
    await capture.press("Escape"); // FR-030: clears the field, creates nothing
    await expect(capture).toHaveValue("");

    // Composite-widget keys still operate INSIDE the listbox (D5): arrows + Space.
    const listbox = page.getByRole("grid", { name: "Zadania" });
    await listbox.focus();
    const option = page.getByRole("row").first();
    await expect(option).toHaveAttribute("aria-selected", "true");
    const toggled = page.waitForResponse(
      (r) => r.request().method() === "PATCH" && r.url().includes("/status") && r.ok(),
    );
    await page.keyboard.press(" ");
    await expect(option).toHaveAttribute("data-status", "done");
    await toggled;

    // Inline rename: Enter commits, Escape cancels (FR-030 editing keys intact).
    await page.getByRole("button", { name: "Edytuj „Edytowalny”" }).click();
    const rename = page.getByRole("textbox", { name: "Zmień nazwę zadania" });
    await expect(rename).toBeFocused();
    await rename.press("Escape");
    await expect(page.getByRole("textbox", { name: "Zmień nazwę zadania" })).toHaveCount(0);

    await context.close();
  });
});

import { expect, test } from "@playwright/test";
import { ensureUser, insertSession } from "./helpers/seed";

/**
 * The SC-018 journey (slice 019, T048 — S3.6): a FIRST-TIME user, given no instruction,
 * completes the full daily workflow through visible controls alone — create (global +
 * inline), edit, set priority/date/labels, move to a project, complete, comment on a
 * shared task — using only pointer + standard Tab/Enter/Esc. No single-key shortcuts
 * exist (FR-111).
 */

test("SC-018: full daily workflow via visible UI only [INV-020] [INV-021]", async ({ browser }) => {
  const owner = await ensureUser({
    sub: "google-sub-sc018-owner",
    email: "sc018-owner@taskflow.test",
    name: "Pierwszy Raz",
  });
  const member = await ensureUser({
    sub: "google-sub-sc018-member",
    email: "sc018-member@taskflow.test",
    name: "Wspólnik Projektu",
  });
  const sessionId = await insertSession(owner.id);
  const context = await browser.newContext();
  await context.addCookies([
    { name: "taskflow_session", value: sessionId, url: "http://localhost:3000" },
  ]);
  const page = await context.newPage();

  // ── 1. Create via the GLOBAL "+ Nowy task" (topbar) ─────────────────────────────
  await page.goto("/");
  await expect(page.getByRole("heading", { name: "Inbox" })).toBeVisible();
  await page.getByRole("button", { name: "Nowy task" }).click();
  const globalInput = page.getByRole("dialog").getByRole("textbox", { name: "Nowy task" });
  await expect(globalInput).toBeFocused();
  const created1 = page.waitForResponse(
    (r) => r.request().method() === "PUT" && /\/api\/tasks\//.test(r.url()) && r.ok(),
  );
  await globalInput.fill("Zaplanować tydzień");
  await globalInput.press("Enter");
  await created1;

  // ── 2. Create via the INLINE quick-add ──────────────────────────────────────────
  const inline = page.getByRole("textbox", { name: "Nowy task" });
  const created2 = page.waitForResponse(
    (r) => r.request().method() === "PUT" && /\/api\/tasks\//.test(r.url()) && r.ok(),
  );
  await inline.fill("Kupić kawę");
  await inline.press("Enter");
  await created2;
  await expect(page.getByRole("row")).toHaveCount(2);

  // ── 3. Edit (inline rename via the visible row action) ──────────────────────────
  await page.getByRole("button", { name: "Edytuj „Kupić kawę”" }).click();
  const rename = page.getByRole("textbox", { name: "Zmień nazwę zadania" });
  const renamed = page.waitForResponse(
    (r) => r.request().method() === "PATCH" && r.url().includes("/title") && r.ok(),
  );
  await rename.fill("Kupić kawę ziarnistą");
  await rename.press("Enter");
  await renamed;

  // ── 4. Priority via the "⋯" menu ────────────────────────────────────────────────
  await page.getByRole("button", { name: "Więcej akcji: Kupić kawę ziarnistą" }).click();
  await page.getByRole("menuitem", { name: "Priorytet…" }).click();
  const prioritized = page.waitForResponse(
    (r) => r.request().method() === "PATCH" && r.url().includes("/priority") && r.ok(),
  );
  await page.getByRole("dialog", { name: "Priorytet" }).getByRole("button", { name: "P1", exact: true }).click();
  await prioritized;
  await expect(
    page.getByRole("row").filter({ hasText: "Kupić kawę ziarnistą" }).getByText("P1"),
  ).toBeVisible();

  // ── 5. Due date via the menu ("Termin…" → Polish phrase) ────────────────────────
  await page.getByRole("button", { name: "Więcej akcji: Kupić kawę ziarnistą" }).click();
  await page.getByRole("menuitem", { name: "Termin…" }).click();
  const rescheduled = page.waitForResponse(
    (r) => r.request().method() === "PATCH" && r.url().includes("/due-date") && r.ok(),
  );
  await page.getByRole("textbox", { name: /termin/i }).fill("jutro");
  await page.getByRole("textbox", { name: /termin/i }).press("Enter");
  await rescheduled;

  // ── 6. Labels via the menu ──────────────────────────────────────────────────────
  await page.getByRole("button", { name: "Więcej akcji: Kupić kawę ziarnistą" }).click();
  await page.getByRole("menuitem", { name: "Etykiety…" }).click();
  const labelDialog = page.getByRole("dialog", { name: "Etykiety" });
  const labelCreated = page.waitForResponse(
    (r) => r.request().method() === "PUT" && /\/api\/labels\//.test(r.url()) && r.ok(),
  );
  await labelDialog.getByPlaceholder(/Nowa etykieta/).fill("zakupy");
  await labelDialog.getByPlaceholder(/Nowa etykieta/).press("Enter");
  await labelCreated;
  await labelDialog.getByRole("checkbox", { name: /zakupy/ }).check();
  const labelsSet = page.waitForResponse(
    (r) => r.request().method() === "PATCH" && r.url().includes("/labels") && r.ok(),
  );
  await labelDialog.getByRole("button", { name: /Zapisz/ }).click();
  await labelsSet;
  await expect(page.getByRole("row").filter({ hasText: "Kupić kawę ziarnistą" })).toContainText(
    "zakupy",
  );

  // ── 7. Create a SHARED project (sidebar) and move the task into it ──────────────
  await page.getByRole("button", { name: "Nowy projekt" }).click();
  const projectDialog = page.getByRole("dialog", { name: "Nowy projekt" });
  await projectDialog.getByRole("textbox", { name: "Nazwa projektu" }).fill("Wspólny plan");
  const projectCreated = page.waitForResponse(
    (r) => r.request().method() === "PUT" && /\/api\/projects\//.test(r.url()) && r.ok(),
  );
  await projectDialog.getByRole("button", { name: /Utwórz|Zapisz/ }).click();
  await projectCreated;

  await page.getByRole("button", { name: "Więcej akcji: Kupić kawę ziarnistą" }).click();
  await page.getByRole("menuitem", { name: "Przenieś do projektu…" }).click();
  const moved = page.waitForResponse(
    (r) => r.request().method() === "PATCH" && r.url().includes("/project") && r.ok(),
  );
  await page.getByRole("dialog", { name: /Przenieś/ }).getByRole("button", { name: "Wspólny plan" }).click();
  await moved;
  await expect(page.getByRole("row").filter({ hasText: "Kupić kawę ziarnistą" })).toHaveCount(0);

  // Share the project + invite the member (visible sidebar/dialog controls only — the
  // project row's "⋯" menu carries Share/Members).
  await page.getByRole("button", { name: "Akcje projektu Wspólny plan" }).click();
  await page
    .getByRole("menu", { name: "Akcje projektu Wspólny plan" })
    .getByRole("menuitem", { name: "Udostępnij", exact: true })
    .click();
  const shared = page.waitForResponse(
    (r) => r.request().method() === "PATCH" && r.url().includes("/share") && r.ok(),
  );
  await page.getByRole("dialog").getByRole("button", { name: "Udostępnij projekt" }).click();
  await shared;
  await page.getByRole("button", { name: "Akcje projektu Wspólny plan" }).click();
  await page
    .getByRole("menu", { name: "Akcje projektu Wspólny plan" })
    .getByRole("menuitem", { name: "Członkowie", exact: true })
    .click();
  const invited = page.waitForResponse(
    (r) => r.request().method() === "POST" && r.url().includes("/members") && r.ok(),
  );
  await page.locator('input[name="invite-email"]').fill("sc018-member@taskflow.test");
  await page.getByRole("button", { name: "Zaproś" }).click();
  await invited;
  await page.getByRole("button", { name: "Zamknij" }).click();

  // ── 8. Complete the OTHER task via the row checkbox ─────────────────────────────
  const done = page.waitForResponse(
    (r) => r.request().method() === "PATCH" && r.url().includes("/status") && r.ok(),
  );
  await page.getByRole("checkbox", { name: "Oznacz „Zaplanować tydzień” jako zrobione" }).click();
  await done;

  // ── 9. Comment on the shared task from the project view (drawer) ────────────────
  await page.getByRole("link", { name: /Wspólny plan/ }).click();
  await page.getByRole("button", { name: "Kupić kawę ziarnistą", exact: true }).click();
  const drawer = page.getByRole("complementary");
  await expect(drawer).toBeVisible();
  const commentsRegion = drawer.getByRole("region", { name: "Komentarze" });
  const posted = page.waitForResponse(
    (r) => r.request().method() === "POST" && r.url().includes("/comments") && r.ok(),
  );
  await commentsRegion.getByRole("textbox", { name: "Treść komentarza" }).fill("Kupuję jutro rano");
  await commentsRegion.getByRole("button", { name: "Dodaj komentarz" }).click();
  await posted;
  await expect(commentsRegion.getByRole("article")).toContainText("Kupuję jutro rano");

  // Esc (outside a text field) closes the drawer — standard key, not a shortcut.
  await drawer.getByRole("button", { name: "Zamknij panel" }).focus();
  await page.keyboard.press("Escape");
  await expect(page.getByRole("complementary")).toHaveCount(0);
  expect(new URL(page.url()).searchParams.get("task")).toBeNull();

  await context.close();
  expect(member.id).toBeTruthy();
});

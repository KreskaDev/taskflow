import { randomUUID } from "node:crypto";

import { expect, test, type Browser, type BrowserContext, type Page } from "@playwright/test";
import { apiAs, ensureUser, insertSession } from "./helpers/seed";

/**
 * Command palette & search E2E (slice 013, T013/T019 — US-04, US-08.AS-08, quickstart.md steps 1–8).
 * UI-driven through the real BFF→proxy→API path; API seeding only establishes preconditions. One unique
 * user per test × attempt (the PR-#11 isolation rule). No keyboard binding is exercised — the opener is the
 * topbar button (FR-027 deferred, OOS-20).
 */

const PALETTE = "Szukaj i wykonaj";
const INPUT = "Szukaj zadań, projektów, etykiet i akcji";

async function signedInPage(
  browser: Browser,
  key: string,
): Promise<{ page: Page; context: BrowserContext; userId: string }> {
  const profile = await ensureUser({ sub: `google-sub-${key}`, email: `${key}@taskflow.test`, name: `User ${key}` });
  const sessionId = await insertSession(profile.id);
  const context = await browser.newContext();
  await context.addCookies([{ name: "taskflow_session", value: sessionId, url: "http://localhost:3000" }]);
  const page = await context.newPage();
  return { page, context, userId: profile.id };
}

async function seedLabel(userId: string, name: string): Promise<void> {
  const res = await apiAs(userId).request("PUT", `/api/labels/${randomUUID()}`, { name, color: "blue" });
  if (!res.ok) throw new Error(`label seed failed (${String(res.status)})`);
}

async function seedTaskWithDescription(userId: string, title: string, description: string, position: string): Promise<void> {
  const api = apiAs(userId);
  const task = await api.createTask({ title, position });
  const res = await api.request("PATCH", `/api/tasks/${task.id}/edit`, {
    title,
    description,
    priority: null,
    dueDate: null,
    dueHasTime: null,
    projectId: null,
    recurrenceRule: null,
    version: task.version,
  });
  if (!res.ok) throw new Error(`description seed failed (${String(res.status)})`);
}

async function openPalette(page: Page) {
  await page.getByRole("button", { name: "Szukaj", exact: true }).click();
  const dialog = page.getByRole("dialog", { name: PALETTE });
  await expect(dialog).toBeVisible();
  const input = dialog.getByRole("combobox", { name: INPUT });
  await expect(input).toBeFocused();
  return { dialog, input };
}

test.describe("US-04 Command palette & search", () => {
  test("opener → actions → task by title/description → navigate to the drawer → Esc returns focus [INV-188] [INV-189] [INV-190] [INV-191] [INV-192]", async ({ browser }, testInfo) => {
    const { page, context, userId } = await signedInPage(browser, `pal-core-r${testInfo.retry}`);
    const api = apiAs(userId);
    await api.createTask({ title: "Kup mleko", position: "a0" });
    await seedTaskWithDescription(userId, "Zakupy", "pamiętaj o mleko bez laktozy", "a1");
    await api.createTask({ title: "Umyj okna", position: "a2" });

    await page.goto("/");
    const opener = page.getByRole("button", { name: "Szukaj", exact: true });
    await expect(opener).toBeVisible();
    await expect(opener).toHaveAttribute("aria-haspopup", "dialog");

    // AS-01: the palette opens with the input focused and the action catalogue (no shortcut column).
    const { dialog, input } = await openPalette(page);
    await expect(dialog.getByRole("group", { name: "Akcje" })).toBeVisible();
    await expect(dialog.getByRole("option", { name: "Nowy projekt" })).toBeVisible();
    await expect(dialog.getByText(/Ctrl/)).toHaveCount(0);

    // AS-02: title AND description hits.
    await input.fill("mleko");
    await expect(dialog.getByRole("option", { name: /Kup mleko/ })).toBeVisible();
    await expect(dialog.getByRole("option", { name: /Zakupy/ })).toBeVisible();
    await expect(dialog.getByRole("option", { name: /Umyj okna/ })).toHaveCount(0);

    // AS-03: ↓ to the first task result, Enter → Inbox with the drawer open on that task.
    await input.press("ArrowDown");
    await expect(input).toHaveAttribute("aria-activedescendant", /task:/);
    const activeId = await input.getAttribute("aria-activedescendant");
    const activeTitle = await dialog.locator(`#${activeId!}`).innerText();
    await input.press("Enter");
    await expect(page.getByRole("dialog", { name: PALETTE })).toHaveCount(0);
    await expect(page).toHaveURL(/\?task=/);
    await expect(page.getByRole("complementary").getByRole("textbox", { name: "Tytuł zadania" })).toHaveValue(
      activeTitle.startsWith("Kup mleko") ? "Kup mleko" : "Zakupy",
    );

    // AS-06: Esc closes and focus returns to the opener.
    await page.keyboard.press("Escape"); // closes the drawer first
    await openPalette(page);
    await page.keyboard.press("Escape");
    await expect(page.getByRole("dialog", { name: PALETTE })).toHaveCount(0);
    await expect(opener).toBeFocused();

    await context.close();
  });

  test("„create project” executes Nowy projekt; a project result navigates; an archived one shows „Przywróć” (AS-04/AS-05, US-10.AS-06) [INV-193] [INV-194]", async ({ browser }, testInfo) => {
    const { page, context, userId } = await signedInPage(browser, `pal-proj-r${testInfo.retry}`);
    const api = apiAs(userId);
    const live = await api.createProject({ name: "Remont łazienki", color: "blue", icon: "folder" });
    const old = await api.createProject({ name: "Stary remont", color: "blue", icon: "folder" });
    await api.archiveProject(old.id, old.version);

    await page.goto("/");
    let { dialog, input } = await openPalette(page);

    // AS-04: the English alias from product-vision resolves to the action; Enter opens the project form.
    await input.fill("create project");
    await expect(dialog.getByRole("option", { name: "Nowy projekt" })).toBeVisible();
    await input.press("Enter");
    await expect(page.getByRole("dialog", { name: /Nowy projekt/ })).toBeVisible();
    await page.keyboard.press("Escape");

    // AS-05: a project result navigates to its view.
    ({ dialog, input } = await openPalette(page));
    await input.fill("remont");
    await expect(dialog.getByRole("option", { name: /Remont łazienki/ })).toBeVisible();
    const archivedRow = dialog.getByRole("option", { name: /Stary remont/ });
    await expect(archivedRow).toContainText("(zarchiwizowany)");
    await dialog.getByRole("option", { name: /Remont łazienki/ }).click();
    await expect(page).toHaveURL(new RegExp(`/projects/${live.id}`));
    await expect(page.getByRole("heading", { name: "Remont łazienki" })).toBeVisible();

    // US-10.AS-06: „Przywróć" restores the archived project in place.
    ({ dialog, input } = await openPalette(page));
    await input.fill("stary");
    const restored = page.waitForResponse((r) => r.request().method() === "PATCH" && r.url().includes("/unarchive") && r.ok());
    await dialog.getByRole("button", { name: "Przywróć" }).click();
    await restored;
    await page.keyboard.press("Escape");
    await expect(page.getByRole("link", { name: /Stary remont/ })).toBeVisible();

    await context.close();
  });

  test("a label result replaces the query; no results → empty state [INV-195] [INV-196]", async ({ browser }, testInfo) => {
    const { page, context, userId } = await signedInPage(browser, `pal-label-r${testInfo.retry}`);
    await seedLabel(userId, "dom");
    await apiAs(userId).createTask({ title: "Posprzątaj dom", position: "a0" });

    await page.goto("/");
    const { dialog, input } = await openPalette(page);
    await input.fill("do");
    await dialog.getByRole("option", { name: /^dom/ }).click();
    await expect(input).toHaveValue("dom");
    await expect(dialog.getByRole("option", { name: /Posprzątaj dom/ })).toBeVisible();

    await input.fill("zzzzzz");
    await expect(dialog.getByText("Brak wyników dla „zzzzzz”")).toBeVisible();

    await context.close();
  });

  test("access scoping: a non-member never sees another user's shared task or project [INV-197]", async ({ browser }, testInfo) => {
    const owner = await ensureUser({ sub: `google-sub-pal-owner-r${testInfo.retry}`, email: `pal-owner-r${testInfo.retry}@taskflow.test`, name: "Owner" });
    const ownerApi = apiAs(owner.id);
    const project = await ownerApi.createProject({ name: "Tajny projekt", color: "blue", icon: "folder" });
    await ownerApi.shareProject(project.id, project.version);
    const task = await ownerApi.createTask({ title: "Tajne mleko", position: "a0" });
    await ownerApi.moveTask(task.id, project.id, task.version);

    const { page, context } = await signedInPage(browser, `pal-outsider-r${testInfo.retry}`);
    await page.goto("/");
    const { dialog, input } = await openPalette(page);
    await input.fill("tajn");
    await expect(dialog.getByText("Brak wyników dla „tajn”")).toBeVisible();
    await expect(dialog.getByRole("option")).toHaveCount(0);

    await context.close();
  });
});

test.describe("US-08.AS-08 View filter", () => {
  test("Inbox filter narrows rows diacritic-insensitively, shows the empty hint, and clears [INV-198] [INV-199]", async ({ browser }, testInfo) => {
    const { page, context, userId } = await signedInPage(browser, `flt-inbox-r${testInfo.retry}`);
    const api = apiAs(userId);
    await api.createTask({ title: "Środa — spotkanie", position: "a0" });
    await api.createTask({ title: "Kup mleko", position: "a1" });

    await page.goto("/");
    await expect(page.getByRole("row")).toHaveCount(2);
    const filter = page.getByRole("textbox", { name: "Filtruj zadania w tym widoku" });
    await filter.fill("sro");
    await expect(page.getByRole("row")).toHaveCount(1);
    await expect(page.getByRole("row").filter({ hasText: "Środa" })).toBeVisible();

    await filter.fill("zzz");
    await expect(page.getByText("Brak zadań pasujących do filtru")).toBeVisible();
    await page.getByRole("button", { name: "Wyczyść filtr" }).first().click();
    await expect(page.getByRole("row")).toHaveCount(2);

    await context.close();
  });

  test("project Board columns collapse under the filter; Dziś filters its groups [INV-198]", async ({ browser }, testInfo) => {
    const { page, context, userId } = await signedInPage(browser, `flt-board-r${testInfo.retry}`);
    const api = apiAs(userId);
    const project = await api.createProject({ name: "Tablica", color: "blue", icon: "folder" });
    const a = await api.createTask({ title: "Alfa zadanie", position: "a0" });
    const b = await api.createTask({ title: "Beta zadanie", position: "a1" });
    await api.moveTask(a.id, project.id, a.version);
    await api.moveTask(b.id, project.id, b.version);
    await api.request("PATCH", `/api/tasks/${b.id}/status`, { status: "in_progress", version: b.version + 1 });
    const today = `${new Date().toISOString().slice(0, 10)}T12:00:00Z`;
    await api.createTask({ title: "Dzisiejsze mleko", position: "a2", dueDate: today, dueHasTime: false });
    await api.createTask({ title: "Dzisiejszy chleb", position: "a3", dueDate: today, dueHasTime: false });

    await page.goto(`/projects/${project.id}`);
    await page.getByRole("button", { name: "Tablica" }).click();
    await expect(page.getByText("Alfa zadanie")).toBeVisible();
    const filter = page.getByRole("textbox", { name: "Filtruj zadania w tym widoku" });
    await filter.fill("beta");
    await expect(page.getByText("Beta zadanie")).toBeVisible();
    await expect(page.getByText("Alfa zadanie")).toHaveCount(0);

    await page.goto("/today");
    await expect(page.getByRole("row")).toHaveCount(2);
    await page.getByRole("textbox", { name: "Filtruj zadania w tym widoku" }).fill("chleb");
    await expect(page.getByRole("row")).toHaveCount(1);

    await context.close();
  });
});

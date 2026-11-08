import { expect, test, type Browser, type BrowserContext, type Page } from "@playwright/test";
import { apiAs, ensureUser, insertSession } from "./helpers/seed";

/**
 * Recurring tasks E2E (slice 012, T020 — US-06, quickstart.md steps 1–8). UI-driven through the real
 * BFF→proxy→API path; API seeding only establishes preconditions (due dates relative to today in
 * Europe/Warsaw). One unique user per test × attempt (the PR-#11 isolation rule).
 */

const WARSAW = "Europe/Warsaw";
const SPAWNED_TOAST = "Utworzono kolejne wystąpienie";
const DUE_GUARD = "Zadanie cykliczne musi mieć termin — wyłącz powtarzanie albo ustaw termin";

async function signedInPage(
  browser: Browser,
  key: string,
): Promise<{ page: Page; context: BrowserContext; userId: string }> {
  const profile = await ensureUser({
    sub: `google-sub-${key}`,
    email: `${key}@taskflow.test`,
    name: `User ${key}`,
  });
  const sessionId = await insertSession(profile.id);
  const context = await browser.newContext();
  await context.addCookies([{ name: "taskflow_session", value: sessionId, url: "http://localhost:3000" }]);
  const page = await context.newPage();
  return { page, context, userId: profile.id };
}

/** Today's Warsaw calendar date as `[y, m, d]`. */
function warsawToday(): [number, number, number] {
  const parts = new Intl.DateTimeFormat("en-CA", { timeZone: WARSAW, year: "numeric", month: "2-digit", day: "2-digit" })
    .format(new Date())
    .split("-")
    .map(Number);
  return [parts[0]!, parts[1]!, parts[2]!];
}

/** A Warsaw calendar date shifted by `days` (pure calendar arithmetic, DST-free). */
function shiftDate([y, m, d]: [number, number, number], days: number): [number, number, number] {
  const utc = new Date(Date.UTC(y, m - 1, d + days));
  return [utc.getUTCFullYear(), utc.getUTCMonth() + 1, utc.getUTCDate()];
}

/** The UTC instant of Warsaw midnight on a calendar date (the date-only due convention). */
function warsawMidnightIso([y, m, d]: [number, number, number]): string {
  // Find the UTC instant whose Warsaw wall-clock is 00:00 on that date by probing the offset at noon UTC.
  const probe = new Date(Date.UTC(y, m - 1, d, 12));
  const wall = new Intl.DateTimeFormat("en-GB", { timeZone: WARSAW, hour: "2-digit", hour12: false }).format(probe);
  const offsetHours = Number(wall) - 12; // +1 (CET) or +2 (CEST)
  return new Date(Date.UTC(y, m - 1, d, -offsetHours)).toISOString();
}

function ddmmyyyy([y, m, d]: [number, number, number]): string {
  return `${String(d).padStart(2, "0")}.${String(m).padStart(2, "0")}.${String(y)}`;
}

/** ISO weekday (Mon=1..Sun=7) of a calendar date. */
function isoWeekday([y, m, d]: [number, number, number]): number {
  const dow = new Date(Date.UTC(y, m - 1, d)).getUTCDay();
  return dow === 0 ? 7 : dow;
}

async function seedTask(userId: string, title: string, due: [number, number, number] | null, position = "a0"): Promise<{ id: string; version: number }> {
  const api = apiAs(userId);
  return due
    ? api.createTask({ title, position, dueDate: warsawMidnightIso(due), dueHasTime: false })
    : api.createTask({ title, position });
}

async function openDrawer(page: Page, title: string): Promise<void> {
  await page.getByRole("button", { name: title, exact: true }).click();
  await expect(page.getByRole("complementary")).toBeVisible();
}

/** Sets „Powtarzanie" in the drawer and waits for the whole-object edit to settle. */
async function setRecurrence(page: Page, type: "daily" | "every_n_days" | "weekdays" | "monthly" | "none"): Promise<void> {
  const settled = page.waitForResponse((r) => r.request().method() === "PATCH" && r.url().includes("/edit") && r.ok());
  await page.getByRole("complementary").getByRole("combobox", { name: "Powtarzanie" }).selectOption(type);
  if (type !== "weekdays") await settled;
}

function row(page: Page, title: string) {
  return page.getByRole("row").filter({ hasText: title });
}

test.describe("US-06 Recurring tasks", () => {
  test("drawer field → chip → complete today-due spawns tomorrow's instance → reopen removes it → re-complete spawns anew [INV-176] [INV-180] [INV-182] [INV-183] [INV-185]", async ({ browser }, testInfo) => {
    const { page, context, userId } = await signedInPage(browser, `rec-spawn-r${testInfo.retry}`);
    const today = warsawToday();
    await seedTask(userId, "Podlej kwiaty", today);

    await page.goto("/");
    await expect(row(page, "Podlej kwiaty")).toHaveCount(1);

    // AS-01: set „Codziennie" in the drawer — preview shows tomorrow, the row gets the chip.
    await openDrawer(page, "Podlej kwiaty");
    await setRecurrence(page, "daily");
    await expect(page.getByRole("complementary").getByText(`Następne wystąpienie: ${ddmmyyyy(shiftDate(today, 1))}`)).toBeVisible();
    await expect(row(page, "Podlej kwiaty").getByText("codziennie")).toBeVisible();
    await page.keyboard.press("Escape");

    // AS-02: complete → the successor (due tomorrow, same chip) appears + the toast.
    const completed = page.waitForResponse((r) => r.request().method() === "PATCH" && r.url().includes("/status") && r.ok());
    await page.getByRole("checkbox", { name: "Oznacz „Podlej kwiaty” jako zrobione" }).first().click();
    await completed;
    await expect(page.getByText(SPAWNED_TOAST).first()).toBeVisible();
    await expect(row(page, "Podlej kwiaty")).toHaveCount(2);
    const successor = row(page, "Podlej kwiaty").filter({ hasText: ddmmyyyy(shiftDate(today, 1)) });
    await expect(successor).toHaveCount(1);
    await expect(successor.getByText("codziennie")).toBeVisible();

    // Undo (Clarifications Q1): reopening the completed instance removes the successor.
    const reopened = page.waitForResponse((r) => r.request().method() === "PATCH" && r.url().includes("/status") && r.ok());
    // The catalog checkbox's visible box overlays the input, so a pointer click (even forced) is unreliable in CI —
    // toggle it the way a keyboard user does: focus + Space (the Space activation is WCAG operability, not a shortcut).
    const doneBox = page.getByRole("checkbox", { name: "Oznacz „Podlej kwiaty” jako niezrobione" });
    await doneBox.focus();
    await page.keyboard.press("Space");
    await reopened;
    await expect(row(page, "Podlej kwiaty")).toHaveCount(1);
    await expect(successor).toHaveCount(0);

    // Re-complete → a fresh successor.
    const recompleted = page.waitForResponse((r) => r.request().method() === "PATCH" && r.url().includes("/status") && r.ok());
    await page.getByRole("checkbox", { name: "Oznacz „Podlej kwiaty” jako zrobione" }).first().focus();
    await page.keyboard.press("Space");
    await recompleted;
    await expect(row(page, "Podlej kwiaty").filter({ hasText: ddmmyyyy(shiftDate(today, 1)) })).toHaveCount(1);

    await context.close();
  });

  test("weekday rule on a past-Monday due completes late into the following Wednesday (AS-03/AS-05) [INV-178] [INV-183]", async ({ browser }, testInfo) => {
    const { page, context, userId } = await signedInPage(browser, `rec-weekdays-r${testInfo.retry}`);
    const today = warsawToday();
    // The most recent Monday strictly before today (1..7 days back).
    const back = ((isoWeekday(today) - 1 + 6) % 7) + 1;
    const monday = shiftDate(today, -back);
    const wednesday = shiftDate(monday, 2);
    await seedTask(userId, "Pn i Śr", monday);

    await page.goto("/");
    await openDrawer(page, "Pn i Śr");
    await page.getByRole("complementary").getByRole("combobox", { name: "Powtarzanie" }).selectOption("weekdays");
    await expect(page.getByRole("complementary").getByText("Wybierz co najmniej jeden dzień")).toBeVisible();
    const firstSave = page.waitForResponse((r) => r.request().method() === "PATCH" && r.url().includes("/edit") && r.ok());
    await page.getByRole("checkbox", { name: "poniedziałek" }).check();
    await firstSave;
    const secondSave = page.waitForResponse((r) => r.request().method() === "PATCH" && r.url().includes("/edit") && r.ok());
    await page.getByRole("checkbox", { name: "środa" }).check();
    await secondSave;
    await expect(row(page, "Pn i Śr").getByText("pn, śr")).toBeVisible();
    await page.keyboard.press("Escape");

    const completed = page.waitForResponse((r) => r.request().method() === "PATCH" && r.url().includes("/status") && r.ok());
    await page.getByRole("checkbox", { name: "Oznacz „Pn i Śr” jako zrobione" }).first().click();
    await completed;
    await expect(page.getByText(SPAWNED_TOAST).first()).toBeVisible();
    // Computed from the ORIGINAL due (the Monday), not from today: the Wednesday right after it.
    await expect(row(page, "Pn i Śr").filter({ hasText: ddmmyyyy(wednesday) })).toHaveCount(1);

    await context.close();
  });

  test("completing BEFORE the due day spawns nothing and shows no toast (AS-04) [INV-184]", async ({ browser }, testInfo) => {
    const { page, context, userId } = await signedInPage(browser, `rec-early-r${testInfo.retry}`);
    const tomorrow = shiftDate(warsawToday(), 1);
    await seedTask(userId, "Jutro", tomorrow);

    await page.goto("/");
    await openDrawer(page, "Jutro");
    await setRecurrence(page, "daily");
    await page.keyboard.press("Escape");

    const completed = page.waitForResponse((r) => r.request().method() === "PATCH" && r.url().includes("/status") && r.ok());
    await page.getByRole("checkbox", { name: "Oznacz „Jutro” jako zrobione" }).click();
    await completed;
    await page.waitForLoadState("networkidle");
    await expect(page.getByText(SPAWNED_TOAST)).toHaveCount(0);
    await expect(row(page, "Jutro")).toHaveCount(1);

    await context.close();
  });

  test("cancelled recurring task never spawns (AS-06) [INV-186]", async ({ browser }, testInfo) => {
    const { page, context, userId } = await signedInPage(browser, `rec-cancel-r${testInfo.retry}`);
    const today = warsawToday();
    const task = await seedTask(userId, "Anulowane", today);
    const api = apiAs(userId);
    const edited = await api.request("PATCH", `/api/tasks/${task.id}/edit`, {
      title: "Anulowane",
      description: null,
      priority: null,
      dueDate: warsawMidnightIso(today),
      dueHasTime: false,
      projectId: null,
      recurrenceRule: { type: "daily" },
      version: task.version,
    });
    if (!edited.ok) throw new Error(`edit seed failed (${String(edited.status)})`);
    const { version } = (await edited.json()) as { version: number };
    // The UI offers no cancel affordance (slice-010 scope) — the status write is the FR-003 API path.
    const cancelled = await api.request("PATCH", `/api/tasks/${task.id}/status`, { status: "cancelled", version });
    if (!cancelled.ok) throw new Error(`cancel failed (${String(cancelled.status)})`);

    await page.goto("/");
    await expect(row(page, "Anulowane")).toHaveCount(1);
    await expect(row(page, "Anulowane").filter({ hasText: ddmmyyyy(shiftDate(today, 1)) })).toHaveCount(0);

    await context.close();
  });

  test("editor: „Usuń termin” on a recurring task is blocked; „Nie powtarza się” lifts the chip (Q4) [INV-181] [INV-187]", async ({ browser }, testInfo) => {
    const { page, context, userId } = await signedInPage(browser, `rec-guard-r${testInfo.retry}`);
    const today = warsawToday();
    const task = await seedTask(userId, "Strzeżone", today);
    const edited = await apiAs(userId).request("PATCH", `/api/tasks/${task.id}/edit`, {
      title: "Strzeżone",
      description: null,
      priority: null,
      dueDate: warsawMidnightIso(today),
      dueHasTime: false,
      projectId: null,
      recurrenceRule: { type: "every_n_days", interval: 3 },
      version: task.version,
    });
    if (!edited.ok) throw new Error(`edit seed failed (${String(edited.status)})`);

    await page.goto("/today");
    await expect(row(page, "Strzeżone").getByText("co 3 dni")).toBeVisible();
    await page.getByRole("button", { name: "Edytuj „Strzeżone”" }).click();
    const dialog = page.getByRole("dialog", { name: "Edytuj zadanie" });
    await expect(dialog).toBeVisible();

    await dialog.getByRole("checkbox", { name: "Usuń termin" }).check();
    await expect(dialog.getByRole("alert")).toHaveText(DUE_GUARD);
    await expect(dialog.getByRole("button", { name: "Zapisz (Ctrl+Enter)" })).toBeDisabled();

    // Disabling recurrence lifts the guard; saving clears due + rule — the task leaves Today.
    await dialog.getByRole("combobox", { name: "Powtarzanie" }).selectOption("none");
    await expect(dialog.getByRole("alert")).toHaveCount(0);
    const saved = page.waitForResponse((r) => r.request().method() === "PATCH" && r.url().includes("/edit") && r.ok());
    await dialog.getByRole("button", { name: "Zapisz (Ctrl+Enter)" }).click();
    await saved;
    await expect(row(page, "Strzeżone")).toHaveCount(0);

    await page.goto("/");
    await expect(row(page, "Strzeżone")).toHaveCount(1);
    await expect(row(page, "Strzeżone").getByText("co 3 dni")).toHaveCount(0);

    await context.close();
  });

  test("drawer: clearing „Termin” of a recurring task is blocked; a dateless task's field is disabled with the hint [INV-176] [INV-181]", async ({ browser }, testInfo) => {
    const { page, context, userId } = await signedInPage(browser, `rec-drawer-r${testInfo.retry}`);
    const today = warsawToday();
    await seedTask(userId, "Z terminem", today, "a0");
    await seedTask(userId, "Bez terminu", null, "a1");

    await page.goto("/");
    await openDrawer(page, "Z terminem");
    await setRecurrence(page, "daily");
    const drawer = page.getByRole("complementary");
    const dueInput = drawer.getByLabel("Nowy termin (np. jutro, piątek, 30.06)");
    await dueInput.fill("");
    await dueInput.press("Enter");
    await expect(drawer.getByRole("alert")).toHaveText(DUE_GUARD);
    await expect(row(page, "Z terminem").getByText("codziennie")).toBeVisible();
    await page.keyboard.press("Escape");

    await openDrawer(page, "Bez terminu");
    await expect(page.getByRole("complementary").getByRole("combobox", { name: "Powtarzanie" })).toBeDisabled();
    await expect(page.getByRole("complementary").getByText("Ustaw najpierw termin")).toBeVisible();

    await context.close();
  });
});

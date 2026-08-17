import { randomUUID } from "node:crypto";

import { expect, test, type Browser, type BrowserContext, type Page } from "@playwright/test";
import { apiAs, ensureUser, insertSession, resetCycles } from "./helpers/seed";

/**
 * Cycles E2E (slice 011, T022 — US-05, quickstart.md steps 1–8). UI-driven through the real
 * BFF→proxy→API path; API seeding only establishes preconditions (the axe/[V] posture). One
 * unique user per test × attempt (the PR-#11 isolation rule) so retries never inherit state.
 */

async function signedInPage(
  browser: Browser,
  key: string,
): Promise<{ page: Page; context: BrowserContext; userId: string }> {
  // Cycles are TEAM-WIDE (single-active, no per-user scoping) — every test starts from a
  // clean cycle slate or the leftovers of ANY earlier test poison activate/pre-fill facts.
  await resetCycles();
  const profile = await ensureUser({
    sub: `google-sub-${key}`,
    email: `${key}@taskflow.test`,
    name: `User ${key}`,
  });
  const sessionId = await insertSession(profile.id);
  const context = await browser.newContext();
  await context.addCookies([
    { name: "taskflow_session", value: sessionId, url: "http://localhost:3000" },
  ]);
  const page = await context.newPage();
  return { page, context, userId: profile.id };
}

/** Seeds a cycle through the real API; returns id + version. */
async function seedCycle(
  userId: string,
  input: { name: string; startDate: string; endDate: string; activate?: boolean },
): Promise<{ id: string; version: number }> {
  const api = apiAs(userId);
  const id = randomUUID();
  const created = await api.request("PUT", `/api/cycles/${id}`, {
    name: input.name,
    startDate: input.startDate,
    endDate: input.endDate,
  });
  if (!created.ok) throw new Error(`seedCycle failed (${String(created.status)}): ${await created.text()}`);
  let { version } = (await created.json()) as { version: number };
  if (input.activate) {
    const activated = await api.request("PATCH", `/api/cycles/${id}/activate`, { version });
    if (!activated.ok) throw new Error(`seedCycle activate failed (${String(activated.status)})`);
    version = ((await activated.json()) as { version: number }).version;
  }
  return { id, version };
}

/** UTC instant of Warsaw midnight `days` from today — mirrors the app's D18 convention closely
 * enough for seeding (12:00Z is inside the same Warsaw calendar day at every UTC offset). */
function warsawDayIso(days: number): string {
  const d = new Date(Date.now() + days * 24 * 3600 * 1000);
  return `${d.toISOString().slice(0, 10)}T12:00:00Z`;
}

/** `yyyy-MM-dd` + N days (pure date arithmetic — DST-free by construction). */
function addDaysToDateString(date: string, days: number): string {
  const d = new Date(`${date}T00:00:00Z`);
  d.setUTCDate(d.getUTCDate() + days);
  return d.toISOString().slice(0, 10);
}

/** The switcher's currently selected option label. */
async function selectedCycleLabel(page: Page): Promise<string> {
  return page.getByLabel("Wybrany cykl").locator("option:checked").innerText();
}

test.describe("US-05 Cycles — lifecycle", () => {
  test("empty state → create ×2 (D18 pre-fills) → activate → single-active refusal → sidebar name [INV-160] [INV-161] [INV-162] [INV-165] [INV-167]", async ({ browser }, testInfo) => {
    const { page, context } = await signedInPage(browser, `cyc-life-r${testInfo.retry}`);

    // FR-017: the sidebar entry is present with the bare label and navigates to /cycle.
    await page.goto("/");
    await page.getByRole("link", { name: "Cykl", exact: true }).click();
    // Generous timeout: the FIRST /cycle hit compiles the route on demand in `next dev`.
    await expect(page).toHaveURL(/\/cycle$/, { timeout: 30_000 });

    // FR-110 empty state with the „Nowy cykl" action.
    await expect(page.getByText("Nie masz jeszcze żadnego cyklu", { exact: false })).toBeVisible();
    await page.getByRole("button", { name: "Nowy cykl" }).click();

    // D18 pre-fills: name „Cykl 1", start = today (Warsaw), end = start + 14 (default preference).
    const dialog = page.getByRole("dialog", { name: "Nowy cykl" });
    await expect(dialog.getByLabel("Nazwa")).toHaveValue("Cykl 1");
    const start = await dialog.getByLabel("Początek").inputValue();
    await expect(dialog.getByLabel("Koniec")).toHaveValue(addDaysToDateString(start, 14));

    const created = page.waitForResponse((r) => r.request().method() === "PUT" && /\/api\/cycles\//.test(r.url()) && r.ok());
    await dialog.getByRole("button", { name: "Utwórz" }).click();
    await created;
    await expect(dialog).toHaveCount(0);
    expect(await selectedCycleLabel(page)).toBe("Cykl 1 (planowany)");

    // Second cycle: the pre-fill counts up („Cykl 2").
    await page.getByRole("button", { name: "Nowy cykl" }).click();
    const dialog2 = page.getByRole("dialog", { name: "Nowy cykl" });
    await expect(dialog2.getByLabel("Nazwa")).toHaveValue("Cykl 2");
    const created2 = page.waitForResponse((r) => r.request().method() === "PUT" && /\/api\/cycles\//.test(r.url()) && r.ok());
    await dialog2.getByRole("button", { name: "Utwórz" }).click();
    await created2;
    // Wait for the dialog to close — submitForm's post-create selection lands with it; selecting
    // „Cykl 1" any earlier would be overridden and „Aktywuj" would target the wrong cycle.
    await expect(dialog2).toHaveCount(0);
    expect(await selectedCycleLabel(page)).toBe("Cykl 2 (planowany)");

    // Activate „Cykl 1" — the sidebar entry takes the active cycle's NAME.
    await page.getByLabel("Wybrany cykl").selectOption({ label: "Cykl 1 (planowany)" });
    const activated = page.waitForResponse((r) => r.request().method() === "PATCH" && /\/activate$/.test(r.url()) && r.ok());
    await page.getByRole("button", { name: "Aktywuj" }).click();
    await activated;
    await expect(page.getByRole("link", { name: "Cykl 1" })).toBeVisible();

    // Activating the second is refused with the single-active copy (server-guarded, D3).
    await page.getByLabel("Wybrany cykl").selectOption({ label: "Cykl 2 (planowany)" });
    await page.getByRole("button", { name: "Aktywuj" }).click();
    await expect(page.getByText("Inny cykl jest już aktywny — najpierw go zamknij.").first()).toBeVisible();
    expect(await selectedCycleLabel(page)).toBe("Cykl 2 (planowany)");

    await context.close();
  });
});

test.describe("US-05 Cycles — assignment, metrics, grouping", () => {
  test("menu „Cykl…” assigns with ONE PATCH; /cycle metrics move as the task completes [INV-163] [INV-172] [INV-173]", async ({ browser }, testInfo) => {
    const { page, context, userId } = await signedInPage(browser, `cyc-assign-r${testInfo.retry}`);
    await seedCycle(userId, { name: "Sprint A", startDate: warsawDayIso(-2), endDate: warsawDayIso(12), activate: true });
    await apiAs(userId).createTask({ title: "Zadanie cykliczne", position: "a0" });

    await page.goto("/");
    const row = page.getByRole("row").filter({ hasText: "Zadanie cykliczne" });
    await expect(row).toBeVisible();

    // AS-01/02: „⋯" → „Cykl…" → the picker lists the cycle with its status suffix + „Bez cyklu";
    // selecting commits ONE PATCH and closes.
    const patches: string[] = [];
    page.on("request", (r) => {
      if (r.method() === "PATCH" && /\/api\/tasks\/[^/]+\/cycle$/.test(r.url())) patches.push(r.url());
    });
    await page.getByRole("button", { name: "Więcej akcji: Zadanie cykliczne" }).click();
    await page.getByRole("menu", { name: "Akcje taska" }).getByRole("menuitem", { name: "Cykl…" }).click();
    const picker = page.getByRole("dialog", { name: "Cykl" });
    await expect(picker.getByRole("button", { name: "Bez cyklu" })).toBeVisible();
    const assigned = page.waitForResponse((r) => r.request().method() === "PATCH" && /\/cycle$/.test(r.url()) && r.ok());
    await picker.getByRole("button", { name: "Sprint A (aktywny)" }).click();
    await assigned;
    await expect(picker).toHaveCount(0);
    expect(patches).toHaveLength(1);

    // AS-03: the metrics strip counts the assigned task; completing it moves the numbers.
    await page.goto("/cycle");
    await expect(page.getByText("0% ukończone")).toBeVisible();
    await expect(page.getByRole("row").filter({ hasText: "Zadanie cykliczne" })).toBeVisible();

    await page.goto("/");
    const done = page.waitForResponse((r) => r.request().method() === "PATCH" && /\/status$/.test(r.url()) && r.ok());
    await page.getByRole("button", { name: "Więcej akcji: Zadanie cykliczne" }).click();
    await page.getByRole("menu", { name: "Akcje taska" }).getByRole("menuitem", { name: "Oznacz jako zrobione" }).click();
    await done;

    await page.goto("/cycle");
    await expect(page.getByText("100% ukończone")).toBeVisible();

    await context.close();
  });

  test("project List „Grupuj: Cykl” renders the cycle group with „Bez cyklu” LAST [INV-174]", async ({ browser }, testInfo) => {
    const { page, context, userId } = await signedInPage(browser, `cyc-group-r${testInfo.retry}`);
    const api = apiAs(userId);
    const cycle = await seedCycle(userId, { name: "Sprint G", startDate: warsawDayIso(-2), endDate: warsawDayIso(12), activate: true });
    const project = await api.createProject({ name: "Projekt G", color: "blue", icon: "folder" });
    const inCycle = await api.createTask({ title: "Zadanie w cyklu", position: "a0" });
    await api.moveTask(inCycle.id, project.id, inCycle.version);
    const setCycle = await api.request("PATCH", `/api/tasks/${inCycle.id}/cycle`, {
      cycleId: cycle.id,
      version: inCycle.version + 1,
    });
    if (!setCycle.ok) throw new Error(`cycle seed failed (${String(setCycle.status)})`);
    const noCycle = await api.createTask({ title: "Zadanie luzem", position: "a1" });
    await api.moveTask(noCycle.id, project.id, noCycle.version);

    await page.goto(`/projects/${project.id}`);
    await expect(page.getByText("Zadanie luzem")).toBeVisible();
    await page.getByRole("button", { name: "Cykl", exact: true }).click();

    // EC-10: groups labelled by cycle NAME, „Bez cyklu" (never „Backlog") LAST.
    await expect(page.getByRole("rowgroup", { name: "Sprint G" })).toBeVisible();
    await expect(page.getByRole("rowgroup", { name: "Bez cyklu" })).toBeVisible();
    const groupLabels = await page.locator('[role="rowgroup"]').evaluateAll((els) =>
      els.map((el) => el.getAttribute("aria-label")),
    );
    expect(groupLabels.at(-1)).toBe("Bez cyklu");
    expect(groupLabels).not.toContain("Backlog");

    await context.close();
  });
});

test.describe("US-05 Cycles — close review", () => {
  test("close with „move all to next” carries counts; then closing with NO planned cycle prompts AS-06 [INV-168] [INV-169]", async ({ browser }, testInfo) => {
    const { page, context, userId } = await signedInPage(browser, `cyc-close-r${testInfo.retry}`);
    const api = apiAs(userId);
    // End at +1: an already-overdue active cycle would ALSO render the banner's second
    // „Zamknij cykl" button and break the toolbar click's strict-mode locator.
    const c1 = await seedCycle(userId, { name: "Sprint 1", startDate: warsawDayIso(-14), endDate: warsawDayIso(1), activate: true });
    await seedCycle(userId, { name: "Sprint 2", startDate: warsawDayIso(1), endDate: warsawDayIso(15) });
    for (const [title, position] of [["Niedokończone A", "a0"], ["Niedokończone B", "a1"]] as const) {
      const task = await api.createTask({ title, position });
      const res = await api.request("PATCH", `/api/tasks/${task.id}/cycle`, { cycleId: c1.id, version: task.version });
      if (!res.ok) throw new Error(`cycle seed failed (${String(res.status)})`);
    }
    const doneTask = await api.createTask({ title: "Skończone", position: "a2" });
    const cycled = await api.request("PATCH", `/api/tasks/${doneTask.id}/cycle`, { cycleId: c1.id, version: doneTask.version });
    if (!cycled.ok) throw new Error(`cycle seed failed (${String(cycled.status)})`);
    const statusRes = await api.request("PATCH", `/api/tasks/${doneTask.id}/status`, { status: "done", version: doneTask.version + 1 });
    if (!statusRes.ok) throw new Error(`status seed failed (${String(statusRes.status)})`);

    // AS-04/05: the review lists ONLY the incomplete tasks; „move all to next" is the default.
    await page.goto("/cycle");
    expect(await selectedCycleLabel(page)).toBe("Sprint 1 (aktywny)");
    await page.getByRole("button", { name: "Zamknij cykl" }).click();
    const review = page.getByRole("dialog", { name: /Zamknij cykl „Sprint 1”/ });
    await expect(review.getByText("Niedokończone A")).toBeVisible();
    await expect(review.getByText("Niedokończone B")).toBeVisible();
    await expect(review.getByText("Skończone")).toHaveCount(0);
    await expect(review.getByText("dotyczy także zadań innych użytkowników", { exact: false })).toBeVisible();

    const closed = page.waitForResponse((r) => r.request().method() === "PATCH" && /\/close$/.test(r.url()) && r.ok());
    await review.getByRole("button", { name: "Zamknij cykl" }).click();
    await closed;
    await expect(page.getByText("Cykl zamknięty — przeniesione do następnego cyklu: 2.").first()).toBeVisible();

    // The incomplete tasks landed in „Sprint 2".
    await page.getByLabel("Wybrany cykl").selectOption({ label: "Sprint 2 (planowany)" });
    await expect(page.getByRole("row").filter({ hasText: "Niedokończone A" })).toBeVisible();
    await expect(page.getByRole("row").filter({ hasText: "Niedokończone B" })).toBeVisible();

    // AS-06: activate „Sprint 2", close with rollover „next" and NO planned cycle → the prompt.
    const activated = page.waitForResponse((r) => r.request().method() === "PATCH" && /\/activate$/.test(r.url()) && r.ok());
    await page.getByRole("button", { name: "Aktywuj" }).click();
    await activated;
    await page.getByRole("button", { name: "Zamknij cykl" }).click();
    const review2 = page.getByRole("dialog", { name: /Zamknij cykl „Sprint 2”/ });
    await review2.getByRole("button", { name: "Zamknij cykl" }).click();
    await expect(review2.getByText("Najpierw utwórz nowy cykl", { exact: false })).toBeVisible();

    // The prompt's action opens the create dialog; NOTHING was closed.
    await review2.getByRole("button", { name: "Nowy cykl" }).click();
    const createDialog = page.getByRole("dialog", { name: "Nowy cykl" });
    await expect(createDialog).toBeVisible();
    await createDialog.getByRole("button", { name: "Anuluj" }).click();
    expect(await selectedCycleLabel(page)).toBe("Sprint 2 (aktywny)");

    await context.close();
  });
});

test.describe("US-05 Cycles — delete guards & keep-rollover", () => {
  test("„Usuń” refused on active (AS-07 verbatim) and on a non-empty closed; empty planned deletes; „keep” flags „przeniesione” [INV-164] [INV-170]", async ({ browser }, testInfo) => {
    const { page, context, userId } = await signedInPage(browser, `cyc-del-r${testInfo.retry}`);
    const api = apiAs(userId);
    const c1 = await seedCycle(userId, { name: "Sprint D", startDate: warsawDayIso(-14), endDate: warsawDayIso(1), activate: true });
    await seedCycle(userId, { name: "Pusty", startDate: warsawDayIso(30), endDate: warsawDayIso(44) });
    const task = await api.createTask({ title: "Zadanie zostające", position: "a0" });
    const res = await api.request("PATCH", `/api/tasks/${task.id}/cycle`, { cycleId: c1.id, version: task.version });
    if (!res.ok) throw new Error(`cycle seed failed (${String(res.status)})`);

    // AS-07: „Usuń" is VISIBLE on the active cycle; invoking it is prevented WITH the message.
    await page.goto("/cycle");
    expect(await selectedCycleLabel(page)).toBe("Sprint D (aktywny)");
    await page.getByRole("button", { name: "Usuń" }).click();
    await expect(page.getByText("Cyklu nie można usunąć — najpierw go zamknij.").first()).toBeVisible();
    await expect(page.getByLabel("Wybrany cykl").locator("option", { hasText: "Sprint D" })).toHaveCount(1);

    // Close with „keep": the task STAYS in the closed cycle flagged „przeniesione" (D7).
    await page.getByRole("button", { name: "Zamknij cykl" }).click();
    const review = page.getByRole("dialog", { name: /Zamknij cykl „Sprint D”/ });
    await review.getByRole("radio", { name: /Zostaw w zamkniętym cyklu/ }).check();
    const closed = page.waitForResponse((r) => r.request().method() === "PATCH" && /\/close$/.test(r.url()) && r.ok());
    await review.getByRole("button", { name: "Zamknij cykl" }).click();
    await closed;
    await expect(page.getByText(/pozostawione jako „przeniesione”: 1/).first()).toBeVisible();

    await page.getByLabel("Wybrany cykl").selectOption({ label: "Sprint D (zamknięty)" });
    const keptRow = page.getByRole("row").filter({ hasText: "Zadanie zostające" });
    await expect(keptRow).toBeVisible();
    await expect(keptRow.getByText("przeniesione")).toBeVisible();

    // EC-04: a non-empty CLOSED cycle refuses deletion with the FR-049 copy.
    await page.getByRole("button", { name: "Usuń" }).click();
    await expect(page.getByText("Cykl zawiera zadania — najpierw przenieś je do innego cyklu lub backlogu.").first()).toBeVisible();

    // An EMPTY planned cycle deletes.
    await page.getByLabel("Wybrany cykl").selectOption({ label: "Pusty (planowany)" });
    const deleted = page.waitForResponse((r) => r.request().method() === "DELETE" && /\/api\/cycles\//.test(r.url()) && r.ok());
    await page.getByRole("button", { name: "Usuń" }).click();
    await deleted;
    await expect(page.getByLabel("Wybrany cykl").locator("option", { hasText: "Pusty" })).toHaveCount(0);

    await context.close();
  });
});

test.describe("US-05 Cycles — overdue banner & EC-12", () => {
  test("an overdue active cycle banners „zamknij go” + sidebar „po terminie”; archived-project rows STAY visible [INV-161] [INV-164] [INV-171]", async ({ browser }, testInfo) => {
    const { page, context, userId } = await signedInPage(browser, `cyc-over-r${testInfo.retry}`);
    const api = apiAs(userId);
    const cycle = await seedCycle(userId, { name: "Zaległy", startDate: warsawDayIso(-14), endDate: warsawDayIso(-1), activate: true });
    const project = await api.createProject({ name: "Do archiwum", color: "blue", icon: "folder" });
    const task = await api.createTask({ title: "Zadanie z archiwum", position: "a0" });
    await api.moveTask(task.id, project.id, task.version);
    const res = await api.request("PATCH", `/api/tasks/${task.id}/cycle`, { cycleId: cycle.id, version: task.version + 1 });
    if (!res.ok) throw new Error(`cycle seed failed (${String(res.status)})`);
    await api.archiveProject(project.id, project.version);

    await page.goto("/cycle");
    // FR-044: the overdue facts are TEXT — the banner, the metrics label and the sidebar badge.
    await expect(page.getByText("Cykl dobiegł końca — zamknij go.")).toBeVisible();
    await expect(page.getByText("0 dni (po terminie)")).toBeVisible();
    await expect(page.getByRole("link", { name: /Zaległy/ }).getByText("po terminie")).toBeVisible();

    // EC-12: the archived project's task remains visible on /cycle.
    await expect(page.getByRole("row").filter({ hasText: "Zadanie z archiwum" })).toBeVisible();

    await context.close();
  });
});

test.describe("US-05 Cycles — settings preference", () => {
  test("the /settings duration (1..90) roundtrips and drives the create pre-fill [INV-175]", async ({ browser }, testInfo) => {
    const { page, context } = await signedInPage(browser, `cyc-pref-r${testInfo.retry}`);

    await page.goto("/settings");
    const field = page.getByLabel("Domyślna długość cyklu (dni)");
    await expect(field).toHaveValue("14");
    await field.fill("21");
    const saved = page.waitForResponse((r) => r.request().method() === "PATCH" && /\/preferences$/.test(r.url()) && r.ok());
    await page.getByRole("button", { name: "Zapisz" }).click();
    await saved;

    // Roundtrip across reload (server-side, not device-local).
    await page.reload();
    await expect(page.getByLabel("Domyślna długość cyklu (dni)")).toHaveValue("21");

    // The create dialog's end pre-fill follows the preference (D18).
    await page.goto("/cycle");
    await page.getByRole("button", { name: "Nowy cykl" }).click();
    const dialog = page.getByRole("dialog", { name: "Nowy cykl" });
    const start = await dialog.getByLabel("Początek").inputValue();
    await expect(dialog.getByLabel("Koniec")).toHaveValue(addDaysToDateString(start, 21));

    await context.close();
  });
});

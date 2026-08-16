import { expect, test, type Browser, type BrowserContext, type Page } from "@playwright/test";
import { apiAs, ensureUser, insertSession } from "./helpers/seed";

/**
 * Board journey E2E (slice 010, T020 — US-03, quickstart Scenarios 1–3; UI-driven per the
 * 019 posture). Covers: sidebar entry into the project (AS-01), the four-column Board with
 * counts (AS-03), drag + menu column moves issuing ONE status PATCH (AS-04..06, D6),
 * per-project last-used mode (AS-02), the groupable List with the cancelled group vs the
 * Board hiding cancelled entirely (AS-07, EC-11), and the viewer's read-only board.
 */

interface Seeded {
  page: Page;
  context: BrowserContext;
  userId: string;
}

async function signedInPage(browser: Browser, key: string, name = "Ada Tablicowa"): Promise<Seeded> {
  const profile = await ensureUser({
    sub: `google-sub-${key}`,
    email: `${key}@taskflow.test`,
    name,
  });
  const sessionId = await insertSession(profile.id);
  const context = await browser.newContext();
  await context.addCookies([
    { name: "taskflow_session", value: sessionId, url: "http://localhost:3000" },
  ]);
  const page = await context.newPage();
  return { page, context, userId: profile.id };
}

/** Seeds a projected task in an explicit status through the REAL API (D3 — no DB pokes). */
async function seedStatusTask(
  api: ReturnType<typeof apiAs>,
  projectId: string,
  title: string,
  position: string,
  status?: string,
  priority?: string,
): Promise<string> {
  const task = await api.createTask({ title, position });
  await api.moveTask(task.id, projectId, task.version);
  let version = task.version + 1;
  if (status) {
    const res = await api.request("PATCH", `/api/tasks/${task.id}/status`, { status, version });
    if (!res.ok) throw new Error(`status seed failed (${String(res.status)})`);
    version += 1;
  }
  if (priority) {
    const res = await api.request("PATCH", `/api/tasks/${task.id}/priority`, { priority, version });
    if (!res.ok) throw new Error(`priority seed failed (${String(res.status)})`);
  }
  return task.id;
}

/** Enters the project through its VISIBLE sidebar entry (US-03.AS-01 — never a direct URL). */
async function enterProjectFromSidebar(page: Page, projectName: string, projectId: string): Promise<void> {
  await page.goto("/");
  const nav = page.getByRole("navigation", { name: "Projekty" });
  await nav.getByRole("link", { name: new RegExp(projectName) }).click();
  await page.waitForURL(`**/projects/${projectId}`);
}

test.describe("Board rendering & column moves (US-03.AS-01/03..06)", () => {
  test("sidebar entry opens the project; Tablica renders four counted columns; drag and menu moves issue ONE PATCH each [INV-140] [INV-142] [INV-144] [INV-145]", async ({
    browser,
  }) => {
    const { page, context, userId } = await signedInPage(browser, "board-core");
    const api = apiAs(userId);
    const project = await api.createProject({ name: "Tablicowy", color: "blue", icon: "folder" });
    await seedStatusTask(api, project.id, "W backlogu", "a0");
    await seedStatusTask(api, project.id, "Do wzięcia", "a1", "todo");
    await seedStatusTask(api, project.id, "Skończone", "a2", "done");

    // AS-01: the project is reachable from the visible sidebar entry.
    await enterProjectFromSidebar(page, "Tablicowy", project.id);

    // AS-02 entry point: the visible mode switch; AS-03: four columns in order, with counts.
    await page.getByRole("button", { name: "Tablica", exact: true }).click();
    await expect(page.getByRole("list", { name: "Backlog, 1 zadanie" })).toBeVisible();
    await expect(page.getByRole("list", { name: "Do zrobienia, 1 zadanie" })).toBeVisible();
    // An EMPTY column's list element is zero-height (its EmptyState sibling carries the
    // visuals) — attached to the a11y tree, not "visible" in Playwright's bounding-box sense.
    await expect(page.getByRole("list", { name: "W toku, 0 zadań" })).toBeAttached();
    await expect(page.getByRole("list", { name: "Zrobione, 1 zadanie" })).toBeVisible();

    // AS-04: drag „Do wzięcia” from Do zrobienia to W toku — optimistic paint + exactly ONE
    // PATCH /status carrying in_progress (D6: position untouched — no /position request).
    const statusPatches: string[] = [];
    page.on("request", (r) => {
      if (r.method() === "PATCH" && r.url().includes("/status")) statusPatches.push(r.url());
    });
    const positionPatches: string[] = [];
    page.on("request", (r) => {
      if (r.method() === "PATCH" && r.url().includes("/position")) positionPatches.push(r.url());
    });

    const card = page.getByRole("listitem", { name: /Do wzięcia/ });
    const targetColumn = page.getByRole("list", { name: /W toku/ });
    const moved = page.waitForResponse(
      (r) => r.request().method() === "PATCH" && r.url().includes("/status") && r.ok(),
    );
    const from = (await card.boundingBox())!;
    const to = (await targetColumn.boundingBox())!;
    await page.mouse.move(from.x + from.width / 2, from.y + from.height / 2);
    await page.mouse.down();
    await page.mouse.move(to.x + to.width / 2, to.y + to.height / 2, { steps: 12 });
    await page.mouse.up();

    // Optimistic: the card is in W toku before the server answer settles the UI.
    await expect(page.getByRole("list", { name: /W toku/ }).getByRole("listitem", { name: /Do wzięcia/ })).toBeVisible();
    await moved;
    expect(statusPatches).toHaveLength(1);
    expect(positionPatches).toHaveLength(0);

    // AS-05/AS-06: menu moves — „Przenieś w lewo” back to Do zrobienia; boundary items absent.
    await page.getByRole("button", { name: "Więcej akcji: Do wzięcia" }).click();
    const menuMoved = page.waitForResponse(
      (r) => r.request().method() === "PATCH" && r.url().includes("/status") && r.ok(),
    );
    await page.getByRole("menuitem", { name: /Przenieś w lewo/ }).click();
    await menuMoved;
    await expect(
      page.getByRole("list", { name: /Do zrobienia/ }).getByRole("listitem", { name: /Do wzięcia/ }),
    ).toBeVisible();

    // Zrobione offers no right move; Backlog no left (items OMITTED, not disabled).
    await page.getByRole("button", { name: "Więcej akcji: Skończone" }).click();
    await expect(page.getByRole("menuitem", { name: /Przenieś w lewo/ })).toBeVisible();
    await expect(page.getByRole("menuitem", { name: /Przenieś w prawo/ })).toHaveCount(0);
    await page.keyboard.press("Escape");
    await page.getByRole("button", { name: "Więcej akcji: W backlogu" }).click();
    await expect(page.getByRole("menuitem", { name: /Przenieś w prawo/ })).toBeVisible();
    await expect(page.getByRole("menuitem", { name: /Przenieś w lewo/ })).toHaveCount(0);
    await page.keyboard.press("Escape");

    // Done-boundary coherence: a menu move into Zrobione stamps the check state the List shows.
    await page.getByRole("button", { name: "Więcej akcji: W backlogu" }).click();
    const toTodo = page.waitForResponse(
      (r) => r.request().method() === "PATCH" && r.url().includes("/status") && r.ok(),
    );
    await page.getByRole("menuitem", { name: /Przenieś w prawo/ }).click();
    await toTodo;
    await expect(
      page.getByRole("list", { name: /Do zrobienia/ }).getByRole("listitem", { name: /W backlogu/ }),
    ).toBeVisible();
    await page.getByRole("button", { name: "Lista", exact: true }).click();
    await expect(page.getByRole("checkbox", { name: "Oznacz „Skończone” jako niezrobione" })).toBeChecked();

    await context.close();
  });
});

test.describe("Last-used mode per project (US-03.AS-02)", () => {
  test("Tablica persists across reload for THIS project; a different project defaults to Lista [INV-141]", async ({
    browser,
  }) => {
    const { page, context, userId } = await signedInPage(browser, "board-mode");
    const api = apiAs(userId);
    const boardProject = await api.createProject({ name: "Zapamiętany", color: "blue", icon: "folder" });
    const otherProject = await api.createProject({ name: "Świeży", color: "red", icon: "star" });
    await seedStatusTask(api, boardProject.id, "Karta", "a0", "todo");

    await enterProjectFromSidebar(page, "Zapamiętany", boardProject.id);
    await page.getByRole("button", { name: "Tablica", exact: true }).click();
    await expect(page.getByRole("list", { name: /Do zrobienia/ })).toBeVisible();

    // Reload → the Board renders again (per-project localStorage).
    await page.reload();
    await expect(page.getByRole("list", { name: /Do zrobienia/ })).toBeVisible();
    await expect(page.getByRole("button", { name: "Tablica", exact: true })).toHaveAttribute("aria-pressed", "true");

    // A DIFFERENT project still defaults to Lista.
    await enterProjectFromSidebar(page, "Świeży", otherProject.id);
    await expect(page.getByRole("button", { name: "Lista", exact: true })).toHaveAttribute("aria-pressed", "true");
    await expect(page.getByRole("list", { name: /Do zrobienia/ })).toHaveCount(0);

    // Returning → still Board.
    await enterProjectFromSidebar(page, "Zapamiętany", boardProject.id);
    await expect(page.getByRole("list", { name: /Do zrobienia/ })).toBeVisible();

    await context.close();
  });
});

test.describe("Groupable List & cancelled (US-03.AS-07, EC-11)", () => {
  test("status grouping shows „Anulowane” last; the Board shows the cancelled task NOWHERE; priority grouping P0→P3 + „Bez priorytetu”; grouping persists [INV-143] [INV-146] [INV-147] [INV-148]", async ({
    browser,
  }) => {
    const { page, context, userId } = await signedInPage(browser, "board-groups");
    const api = apiAs(userId);
    const project = await api.createProject({ name: "Grupowany", color: "blue", icon: "folder" });
    await seedStatusTask(api, project.id, "Zwykłe", "a0", "todo", "P0");
    await seedStatusTask(api, project.id, "Bez priorytetu zadanie", "a1", "in_progress");
    // EC-11 seeding basis (D3): cancelled through the REAL status API.
    await seedStatusTask(api, project.id, "Porzucone", "a2", "cancelled", "P3");

    await enterProjectFromSidebar(page, "Grupowany", project.id);

    // Status grouping: groups render in column order with „Anulowane” LAST and the cancelled
    // task visible IN THE LIST (EC-11 counterpart).
    await page.getByRole("button", { name: "Status", exact: true }).click();
    const groups = page.getByRole("group");
    await expect(groups).toHaveCount(3); // Do zrobienia, W toku, Anulowane (empty omitted)
    await expect(groups.last()).toHaveAccessibleName("Anulowane");
    await expect(page.getByRole("group", { name: "Anulowane" }).getByRole("option", { name: /Porzucone/ })).toBeVisible();

    // The Board shows the cancelled task NOWHERE (EC-11) — and no „Anulowane” column exists.
    await page.getByRole("button", { name: "Tablica", exact: true }).click();
    await expect(page.getByRole("list", { name: /Backlog/ })).toBeAttached();
    await expect(page.getByRole("listitem", { name: /Porzucone/ })).toHaveCount(0);
    await expect(page.getByRole("list", { name: /Anulowane/ })).toHaveCount(0);

    // Priority grouping: P0 → „Bez priorytetu” (P3 belongs to the cancelled task's group).
    await page.getByRole("button", { name: "Lista", exact: true }).click();
    await page.getByRole("button", { name: "Priorytet", exact: true }).click();
    const priorityGroups = page.getByRole("group");
    await expect(priorityGroups.first()).toHaveAccessibleName("P0");
    await expect(priorityGroups.last()).toHaveAccessibleName("Bez priorytetu");
    await expect(page.getByRole("group", { name: "P3" }).getByRole("option", { name: /Porzucone/ })).toBeVisible();

    // The grouping choice survives reload (per-project localStorage); „Brak” restores flat.
    await page.reload();
    await expect(page.getByRole("button", { name: "Priorytet", exact: true })).toHaveAttribute("aria-pressed", "true");
    await expect(page.getByRole("group", { name: "P0" })).toBeVisible();
    await page.getByRole("button", { name: "Brak", exact: true }).click();
    await expect(page.getByRole("group")).toHaveCount(0);
    await expect(page.getByRole("option", { name: /Porzucone/ })).toBeVisible();

    await context.close();
  });
});

test.describe("Viewer sees a read-only board (FR-065/068)", () => {
  test("no drag affordances and no move items for a viewer; the board still renders [INV-149]", async ({
    browser,
  }) => {
    const owner = await signedInPage(browser, "board-owner", "Ada Właścicielka");
    const ownerApi = apiAs(owner.userId);
    const project = await ownerApi.createProject({ name: "Współdzielony", color: "blue", icon: "folder" });
    const shared = await ownerApi.shareProject(project.id, 0);
    await seedStatusTask(ownerApi, project.id, "Cudze zadanie", "a0", "todo");

    // The viewer must EXIST before the invite resolves their email.
    const viewerProfile = await ensureUser({
      sub: "google-sub-board-viewer",
      email: "board-viewer@taskflow.test",
      name: "Wanda Widz",
    });
    await ownerApi.inviteMember(project.id, "board-viewer@taskflow.test", "viewer", shared.version);

    const viewerSession = await insertSession(viewerProfile.id);
    const viewerContext = await browser.newContext();
    await viewerContext.addCookies([
      { name: "taskflow_session", value: viewerSession, url: "http://localhost:3000" },
    ]);
    const viewerPage = await viewerContext.newPage();

    await enterProjectFromSidebar(viewerPage, "Współdzielony", project.id);
    await viewerPage.getByRole("button", { name: "Tablica", exact: true }).click();

    // The board renders (read access) — but with NO action affordances at all.
    await expect(
      viewerPage.getByRole("list", { name: /Do zrobienia/ }).getByRole("listitem", { name: /Cudze zadanie/ }),
    ).toBeVisible();
    await expect(viewerPage.getByRole("button", { name: /Więcej akcji/ })).toHaveCount(0);
    await expect(viewerPage.getByRole("checkbox")).toHaveCount(0);

    // Belt and braces: a forged PATCH is denied server-side (403 forbidden).
    const viewerApi = apiAs(viewerProfile.id);
    const listed = await viewerApi.request("GET", `/api/projects/${project.id}/tasks`);
    const tasks = (await listed.json()) as { id: string; version: number }[];
    const forged = await viewerApi.request("PATCH", `/api/tasks/${tasks[0]!.id}/status`, {
      status: "in_progress",
      version: tasks[0]!.version,
    });
    expect(forged.status).toBe(403);

    await owner.context.close();
    await viewerContext.close();
  });
});

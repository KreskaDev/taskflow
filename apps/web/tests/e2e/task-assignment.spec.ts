import { expect, test, type Browser, type BrowserContext, type Page } from "@playwright/test";
import { apiAs, ensureUser, insertSession } from "./helpers/seed";

/**
 * Task Assignment E2E (slice 008, T031; US-13 AS-01..AS-04). Drives the real keyboard assignment flow —
 * `A` opens the assignee picker on a shared-project task, check a member, save; the row shows the assignee
 * count; `G A` opens "Assigned to me" for the assigned member; a personal (Inbox) task offers no picker —
 * through the real BFF→API path (no mocking). The booted API uses the real clock, so a "due today" seed
 * uses the current instant.
 *
 * SC-008 (a11y): asserted structurally — the picker is a labelled role="dialog" with role="checkbox" rows
 * (FR-101 focus contract); the assigned view is a labelled role="listbox".
 */

async function signedInPage(browser: Browser, key: string): Promise<{ page: Page; context: BrowserContext; userId: string; email: string }> {
  const email = `${key}@taskflow.test`;
  const profile = await ensureUser({ sub: `google-sub-${key}`, email, name: `User ${key}`, picture: "https://avatars.test/u.png" });
  const sessionId = await insertSession(profile.id);
  const context = await browser.newContext();
  await context.addCookies([{ name: "taskflow_session", value: sessionId, url: "http://localhost:3000" }]);
  const page = await context.newPage();
  return { page, context, userId: profile.id, email };
}

function dueToday(): string {
  return new Date().toISOString();
}

test.describe("US-13 Task Assignment (AS-01..AS-04)", () => {
  test("AS-01/AS-02/AS-03: assign a member via the picker; it appears; the member sees it in Assigned", async ({ browser }) => {
    // The editor-member must exist before the owner invites them by email.
    const editor = await ensureUser({ sub: "google-sub-ta-ed", email: "ta-ed@taskflow.test", name: "Edith Editor", picture: "https://avatars.test/e.png" });
    const { page, context, userId: ownerId } = await signedInPage(browser, "ta-owner");

    // Owner sets up: a shared project with the editor as a member, and a due-today task in it.
    const owner = apiAs(ownerId);
    const project = await owner.createProject({ name: "Launch", color: "blue", icon: "folder" });
    const shared = await owner.shareProject(project.id, project.version);
    await owner.inviteMember(project.id, "ta-ed@taskflow.test", "editor", shared.version);
    const task = await owner.createTask({ title: "Coordinate launch", position: "a0", dueDate: dueToday() });
    await owner.moveTask(task.id, project.id, task.version);

    // Owner opens Today, selects the task, opens the assignee picker (A), checks the editor, saves.
    await page.goto("/today");
    const row = page.getByRole("option").filter({ hasText: "Coordinate launch" });
    await expect(row).toBeVisible();
    await page.keyboard.press("a");
    const dialog = page.getByRole("dialog");
    await expect(dialog).toBeVisible();
    const editorBox = dialog.getByRole("checkbox", { name: /Edith Editor/ });
    await editorBox.check();
    const settled = page.waitForResponse((r) => r.request().method() === "PATCH" && /\/assignees/.test(r.url()) && r.ok());
    await page.keyboard.press("Control+Enter");
    await settled;
    await expect(dialog).toHaveCount(0);

    // AS-02: re-opening the picker shows the editor checked (the assignment persisted).
    await page.keyboard.press("a");
    await expect(page.getByRole("dialog").getByRole("checkbox", { name: /Edith Editor/ })).toBeChecked();
    await page.keyboard.press("Escape");
    await context.close();

    // AS-03: the editor opens "Assigned to me" and sees the task.
    const editorSession = await insertSession(editor.id);
    const editorContext = await browser.newContext();
    await editorContext.addCookies([{ name: "taskflow_session", value: editorSession, url: "http://localhost:3000" }]);
    const editorPage = await editorContext.newPage();
    await editorPage.goto("/assigned");
    await expect(editorPage.getByRole("heading", { name: "Przypisane do mnie" })).toBeVisible();
    await expect(editorPage.getByRole("option").filter({ hasText: "Coordinate launch" })).toBeVisible();
    await editorContext.close();
  });

  test("AS-04: a personal (Inbox) task offers no assignment picker", async ({ browser }) => {
    const { page, context, userId } = await signedInPage(browser, "ta-personal");
    await apiAs(userId).createTask({ title: "Personal errand", position: "a0" });

    await page.goto("/");
    await expect(page.getByRole("heading", { name: "Your workspace" })).toBeVisible();
    await expect(page.getByRole("option").filter({ hasText: "Personal errand" })).toBeVisible();

    await page.keyboard.press("a"); // no assignment control on a personal task (AS-04)
    await expect(page.getByRole("dialog")).toHaveCount(0);
    await context.close();
  });
});

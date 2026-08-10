import { expect, test, type Browser, type BrowserContext, type Locator, type Page } from "@playwright/test";
import { apiAs, deleteMembershipRow, ensureUser, insertSession } from "./helpers/seed";

/**
 * Comments & @Mentions E2E (slice 009, T039; US-14 AS-01..AS-04 + the edge cases — re-driven through
 * the slice-019 task DRAWER after T053/T054 replaced the comments modal: clicking the task title on
 * the project view opens the non-modal drawer hosting the thread + composer). Runs through the real
 * BFF→API path (no mocking). The deny cells that a correct UI never exposes (direct post as viewer,
 * non-author edit, former-member access) are asserted as REAL API status codes via the seeding client,
 * proving the server gate is authoritative regardless of the UI (FR-068). The sanitization case posts
 * a hostile payload through the REAL composer and asserts it renders INERT (the stored-XSS regression
 * at the journey level, Constitution XII).
 */

async function signedInPage(
  browser: Browser,
  key: string,
  name: string,
): Promise<{ page: Page; context: BrowserContext; userId: string; email: string }> {
  const email = `${key}@taskflow.test`;
  const profile = await ensureUser({ sub: `google-sub-${key}`, email, name, picture: "https://avatars.test/u.png" });
  const sessionId = await insertSession(profile.id);
  const context = await browser.newContext();
  await context.addCookies([{ name: "taskflow_session", value: sessionId, url: "http://localhost:3000" }]);
  const page = await context.newPage();
  return { page, context, userId: profile.id, email };
}

/** Owner seeds: a SHARED project, one member, and one task in it. Returns the ids the spec drives. */
async function seedSharedTask(
  ownerId: string,
  memberEmail: string,
  role: "editor" | "viewer",
  slug: string,
): Promise<{ projectId: string; taskId: string; taskTitle: string }> {
  const owner = apiAs(ownerId);
  const project = await owner.createProject({ name: `Projekt ${slug}`, color: "blue", icon: "folder" });
  const shared = await owner.shareProject(project.id, project.version);
  await owner.inviteMember(project.id, memberEmail, role, shared.version);
  const taskTitle = `Zadanie ${slug}`;
  const task = await owner.createTask({ title: taskTitle, position: "a0" });
  await owner.moveTask(task.id, project.id, task.version);
  return { projectId: project.id, taskId: task.id, taskTitle };
}

/**
 * Opens the task drawer (hosting the comment thread — T053) from the project view: the row's
 * title button pushes `?task=<id>` and the non-modal drawer mounts (FR-106). Returns the drawer.
 */
async function openThread(page: Page, projectId: string, taskTitle: string): Promise<Locator> {
  await page.goto(`/projects/${projectId}`);
  await page.getByRole("button", { name: taskTitle, exact: true }).click();
  const drawer = page.getByRole("complementary");
  await expect(drawer).toBeVisible();
  await expect(drawer.getByRole("heading", { name: "Komentarze" })).toBeVisible();
  return drawer;
}

/** The comment composer's textarea (sr-labelled "Treść komentarza") — scoped to the drawer. */
function composerBox(drawer: Locator): Locator {
  return drawer.getByRole("textbox", { name: "Treść komentarza" });
}

test.describe("US-14 Comments & @Mentions (AS-01..AS-04)", () => {
  test("AS-01/AS-02/AS-04: an editor posts + @mentions; the author edits and deletes their own comment [INV-110] [INV-111] [INV-112] [INV-113] [INV-114]", async ({ browser }) => {
    const owner = await ensureUser({ sub: "google-sub-cm-owner1", email: "cm-owner1@taskflow.test", name: "Olga Owner", picture: "https://avatars.test/o.png" });
    const { page, context } = await signedInPage(browser, "cm-editor1", "Edith Editor");
    const { projectId, taskTitle } = await seedSharedTask(owner.id, "cm-editor1@taskflow.test", "editor", "A");

    const drawer = await openThread(page, projectId, taskTitle);

    // AS-01: compose + post — the comment appears with author + timestamp.
    await composerBox(drawer).fill("Pierwszy **komentarz** w wątku");
    // AS-02: @mention the owner via the TYPED picker (never prose parsing).
    await drawer.getByRole("button", { name: "@ Wspomnij" }).click();
    await page.getByRole("button", { name: /@Olga Owner/ }).click();
    const posted = page.waitForResponse((r) => r.request().method() === "POST" && /\/comments/.test(r.url()) && r.ok());
    await drawer.getByRole("button", { name: "Dodaj komentarz" }).click();
    await posted;

    const article = drawer.getByRole("article");
    await expect(article).toBeVisible();
    await expect(article).toContainText("Edith Editor");
    await expect(article.locator("strong")).toHaveText("komentarz");
    await expect(article).toContainText("@Olga Owner");
    await expect(article.locator("time")).toBeVisible();

    // AS-04 (edit): the author revises the body; the thread shows the update + the edited affordance.
    await article.getByRole("button", { name: "Edytuj" }).click();
    // Two composers exist while editing (the in-place edit composer inside the list item + the
    // fresh-post composer below the thread) — scope to the list item's one.
    const editBox = drawer.getByRole("listitem").getByRole("textbox", { name: "Treść komentarza" });
    await editBox.fill("Poprawiona treść komentarza");
    const edited = page.waitForResponse((r) => r.request().method() === "PATCH" && /\/api\/comments\//.test(r.url()) && r.ok());
    await drawer.getByRole("button", { name: "Zapisz zmiany" }).click();
    await edited;
    await expect(drawer.getByRole("article")).toContainText("Poprawiona treść komentarza");
    await expect(drawer.getByRole("article")).toContainText("(edytowano)");

    // AS-04 (delete): confirm via the FR-101 dialog; the comment leaves the thread. The "Usuń"
    // affordance is scoped to the ARTICLE — the drawer carries its own task-level "Usuń" button.
    await drawer.getByRole("article").getByRole("button", { name: "Usuń", exact: true }).click();
    const confirm = page.getByRole("dialog", { name: "Usunąć komentarz?" });
    await expect(confirm).toBeVisible();
    const deleted = page.waitForResponse((r) => r.request().method() === "DELETE" && /\/api\/comments\//.test(r.url()) && r.ok());
    await confirm.getByRole("button", { name: "Usuń", exact: true }).click();
    await deleted;
    await expect(drawer.getByRole("article")).toHaveCount(0);
    await expect(drawer.getByText("Brak komentarzy.")).toBeVisible();

    await context.close();
  });

  test("AS-03: a viewer reads the full thread but gets NO composer; a direct post is 403 [INV-116]", async ({ browser }) => {
    const owner = await ensureUser({ sub: "google-sub-cm-owner2", email: "cm-owner2@taskflow.test", name: "Olga Owner", picture: "https://avatars.test/o.png" });
    const { page, context, userId: viewerId } = await signedInPage(browser, "cm-viewer2", "Vera Viewer");
    const { projectId, taskId, taskTitle } = await seedSharedTask(owner.id, "cm-viewer2@taskflow.test", "viewer", "B");

    // The owner posts a comment the viewer will read.
    const post = await apiAs(owner.id).request("POST", `/api/tasks/${taskId}/comments`, {
      body: "Komentarz właściciela",
      mentionedUserIds: [],
    });
    expect(post.status).toBe(200);

    const drawer = await openThread(page, projectId, taskTitle);

    // The full thread is readable...
    await expect(drawer.getByRole("article")).toContainText("Komentarz właściciela");
    // ...but there is NO composer at all (AS-03): no comment textbox, no post affordance. (The
    // drawer's task FIELDS remain rendered — the viewer-role affordance gap applies to the thread.)
    await expect(composerBox(drawer)).toHaveCount(0);
    await expect(drawer.getByRole("button", { name: "Dodaj komentarz" })).toHaveCount(0);

    // The UI gate is convenience only — the SERVER denies a direct viewer post with 403 (FR-068).
    const denied = await apiAs(viewerId).request("POST", `/api/tasks/${taskId}/comments`, {
      body: "Viewer próbuje pisać",
      mentionedUserIds: [],
    });
    expect(denied.status).toBe(403);

    await context.close();
  });

  test("a non-author (incl. the OWNER) sees no edit/delete affordance and is denied 403; a former member loses ALL access (404) [INV-115]", async ({ browser }) => {
    const { page, context, userId: ownerId } = await signedInPage(browser, "cm-owner3", "Olga Owner");
    const editor = await ensureUser({ sub: "google-sub-cm-editor3", email: "cm-editor3@taskflow.test", name: "Edith Editor", picture: "https://avatars.test/e.png" });
    const { projectId, taskId, taskTitle } = await seedSharedTask(ownerId, "cm-editor3@taskflow.test", "editor", "C");

    // The editor authors a comment through the real API.
    const post = await apiAs(editor.id).request("POST", `/api/tasks/${taskId}/comments`, {
      body: "Komentarz edytora",
      mentionedUserIds: [],
    });
    expect(post.status).toBe(200);
    const comment = (await post.json()) as { id: string };

    // The OWNER opens the thread: the comment is readable but carries NO edit/delete affordance
    // (canEdit=false — FR-075: role does not override authorship).
    const drawer = await openThread(page, projectId, taskTitle);
    const article = drawer.getByRole("article");
    await expect(article).toContainText("Komentarz edytora");
    await expect(article.getByRole("button", { name: "Edytuj" })).toHaveCount(0);
    await expect(article.getByRole("button", { name: "Usuń", exact: true })).toHaveCount(0);

    // The server backs the affordance gate: a direct owner edit/delete is 403.
    const ownerEdit = await apiAs(ownerId).request("PATCH", `/api/comments/${comment.id}`, {
      body: "Właściciel nadpisuje",
      mentionedUserIds: [],
    });
    expect(ownerEdit.status).toBe(403);
    const ownerDelete = await apiAs(ownerId).request("DELETE", `/api/comments/${comment.id}`);
    expect(ownerDelete.status).toBe(403);

    // FR-066 > FR-075: after losing membership the AUTHOR loses everything — read, edit, delete → 404.
    await deleteMembershipRow(projectId, editor.id);
    const formerRead = await apiAs(editor.id).request("GET", `/api/tasks/${taskId}/comments`);
    expect(formerRead.status).toBe(404);
    const formerEdit = await apiAs(editor.id).request("PATCH", `/api/comments/${comment.id}`, {
      body: "Nadal moje?",
      mentionedUserIds: [],
    });
    expect(formerEdit.status).toBe(404);
    const formerDelete = await apiAs(editor.id).request("DELETE", `/api/comments/${comment.id}`);
    expect(formerDelete.status).toBe(404);

    await context.close();
  });

  test("content safety: empty/over-length rejected (422 + FR-049 message); a hostile payload renders INERT [INV-117]", async ({ browser }) => {
    const owner = await ensureUser({ sub: "google-sub-cm-owner4", email: "cm-owner4@taskflow.test", name: "Olga Owner", picture: "https://avatars.test/o.png" });
    const { page, context, userId: editorId } = await signedInPage(browser, "cm-editor4", "Edith Editor");
    const { projectId, taskId, taskTitle } = await seedSharedTask(owner.id, "cm-editor4@taskflow.test", "editor", "D");

    const drawer = await openThread(page, projectId, taskTitle);

    // An empty/whitespace-only body is rejected at the trust boundary with an actionable message (FR-049)
    // and creates NO thread entry.
    await composerBox(drawer).fill("   ");
    await drawer.getByRole("button", { name: "Dodaj komentarz" }).click();
    // Next's route announcer is also role=alert — assert on the composer's specific FR-049 message.
    await expect(page.getByText("Komentarz nie może być pusty.")).toBeVisible();
    await expect(drawer.getByRole("article")).toHaveCount(0);

    // The server is authoritative for over-length: a direct 4001-char post is 422 (FR-098).
    const overLength = await apiAs(editorId).request("POST", `/api/tasks/${taskId}/comments`, {
      body: "x".repeat(4001),
      mentionedUserIds: [],
    });
    expect(overLength.status).toBe(422);

    // The stored-XSS journey: post a hostile payload through the REAL composer — it renders INERT.
    // Separate paragraphs: a line starting with <script> opens a CommonMark HTML BLOCK that would swallow
    // trailing text on the same line (it is dropped whole — inert either way); splitting lets the spec
    // assert BOTH properties — the hostile blocks vanish AND the safe subset still renders.
    const hostile = '<script>window.__xss=1</script>\n\n<img src="x" onerror="window.__xss=2">\n\n**pogrubione**';
    await composerBox(drawer).fill(hostile);
    const posted = page.waitForResponse((r) => r.request().method() === "POST" && /\/comments/.test(r.url()) && r.ok());
    await drawer.getByRole("button", { name: "Dodaj komentarz" }).click();
    await posted;

    const article = drawer.getByRole("article");
    await expect(article).toBeVisible();
    await expect(article.locator("strong")).toHaveText("pogrubione", { useInnerText: true });
    await expect(article.locator("script")).toHaveCount(0);
    await expect(article.locator("img")).toHaveCount(0);
    const xssProbe = await page.evaluate(() => (window as unknown as { __xss?: number }).__xss);
    expect(xssProbe).toBeUndefined();

    await context.close();
  });
});

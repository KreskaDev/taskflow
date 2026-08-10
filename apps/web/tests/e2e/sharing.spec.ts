import { expect, test, type Browser, type BrowserContext, type Page } from "@playwright/test";

import { apiAs, ensureUser, insertSession } from "./helpers/seed";

/**
 * Slice-007 Project Sharing, Membership & Roles E2E (T047; US-12.AS-01..AS-06 + edge cases). Drives the
 * REAL BFF→proxy→API path on seeded sessions (no API mocking) against the migrated Postgres + .NET API
 * booted by global-setup. Covers: share (personal→shared) + invite-by-email; a member sees the shared
 * project with role-aware gating (a non-owner sees a read-only roster + Leave, never the manage controls);
 * remove revokes ALL access (the removed member no longer sees the project); unshare round-trips back to
 * personal. The owner never sees Leave (the last-owner safeguard at the UI). Server-side viewer task-WRITE
 * denial is slice 008 — here the UI gating via `ProjectResponse.role` is what is asserted (data-model §3).
 */

const COLOR = "blue";
const ICON = "folder";

interface Signed {
  page: Page;
  context: BrowserContext;
  api: ReturnType<typeof apiAs>;
  id: string;
  email: string;
  name: string;
}

async function signedInPage(browser: Browser, key: string, name: string): Promise<Signed> {
  const email = `share-${key}@taskflow.test`;
  const profile = await ensureUser({ sub: `google-sub-share-${key}`, email, name });
  const sessionId = await insertSession(profile.id);
  const context = await browser.newContext();
  await context.addCookies([{ name: "taskflow_session", value: sessionId, url: "http://localhost:3000" }]);
  const page = await context.newPage();
  return { page, context, api: apiAs(profile.id), id: profile.id, email, name };
}

function sidebarTree(page: Page) {
  return page.locator(".tf-sidebar__tree");
}

/** Opens the sidebar project row's "⋯" menu and activates the given management item (slice 019). */
async function projectMenuAction(page: Page, projectName: string, item: string): Promise<void> {
  await page.getByRole("button", { name: `Akcje projektu ${projectName}` }).click();
  await page
    .getByRole("menu", { name: `Akcje projektu ${projectName}` })
    .getByRole("menuitem", { name: item, exact: true })
    .click();
}

test.describe("US-12 Project Sharing — wired UI", () => {
  test("AS-01/AS-02: owner shares + invites by email; the member sees a role-gated read-only roster [INV-085] [INV-086] [INV-087] [INV-088] [INV-016]", async ({
    browser,
  }) => {
    const owner = await signedInPage(browser, "a1-owner", "Olivia Owner");
    const member = await signedInPage(browser, "a1-member", "Eddie Editor");

    // Seed the owner's personal project, then drive the SHARE through the wired sidebar.
    await owner.api.createProject({ name: "Team Space", color: COLOR, icon: ICON });
    await owner.page.goto("/");
    await expect(sidebarTree(owner.page).getByText("Team Space", { exact: true })).toBeVisible();

    const shared = owner.page.waitForResponse((r) => r.request().method() === "PATCH" && /\/share$/.test(r.url()) && r.ok());
    await projectMenuAction(owner.page, "Team Space", "Udostępnij");
    await owner.page.getByRole("dialog", { name: "Udostępnij projekt" }).getByRole("button", { name: "Udostępnij projekt" }).click();
    await shared;

    // The shared indicator now renders; the menu entry point becomes "Members".
    await expect(owner.page.getByTestId("shared-indicator").first()).toBeVisible();
    await projectMenuAction(owner.page, "Team Space", "Członkowie");

    const dialog = owner.page.getByRole("dialog", { name: "Członkowie: Team Space" });
    await expect(dialog).toBeVisible();
    // The owner sees the manage surface; the owner NEVER sees Leave (last-owner safeguard, R7).
    await expect(dialog.getByRole("button", { name: "Cofnij udostępnianie" })).toBeVisible();
    await expect(dialog.getByRole("button", { name: "Opuść projekt" })).toHaveCount(0);

    // Invite the member by email at the editor role (the wired form).
    const invited = owner.page.waitForResponse((r) => r.request().method() === "POST" && /\/members$/.test(r.url()) && r.ok());
    await dialog.locator('input[name="invite-email"]').fill(member.email);
    await dialog.getByRole("button", { name: "Zaproś" }).click();
    await invited;
    await expect(dialog.getByText("Eddie Editor", { exact: true })).toBeVisible();

    // The member sees the shared project with role-aware gating: a read-only roster + Leave, no manage.
    await member.page.goto("/");
    await expect(sidebarTree(member.page).getByText("Team Space", { exact: true })).toBeVisible();
    await expect(member.page.getByTestId("shared-indicator").first()).toBeVisible();
    await projectMenuAction(member.page, "Team Space", "Członkowie");
    const memberDialog = member.page.getByRole("dialog", { name: "Członkowie: Team Space" });
    await expect(memberDialog).toBeVisible();
    await expect(memberDialog.getByRole("button", { name: "Opuść projekt" })).toBeVisible();
    await expect(memberDialog.locator('input[name="invite-email"]')).toHaveCount(0);
    await expect(memberDialog.getByRole("button", { name: "Cofnij udostępnianie" })).toHaveCount(0);

    await owner.context.close();
    await member.context.close();
  });

  test("AS-04/AS-06: removing a member revokes all access; unshare round-trips to personal [INV-089] [INV-090]", async ({
    browser,
  }) => {
    const owner = await signedInPage(browser, "a2-owner", "Olivia Owner");
    const member = await signedInPage(browser, "a2-member", "Mia Member");

    // Seed share + invite through the real API (the membership UI is asserted live in the first test).
    const project = await owner.api.createProject({ name: "Shared Plan", color: COLOR, icon: ICON });
    const shared = await owner.api.shareProject(project.id, project.version);
    await owner.api.inviteMember(project.id, member.email, "editor", shared.version);

    // The member sees it before removal.
    await member.page.goto("/");
    await expect(sidebarTree(member.page).getByText("Shared Plan", { exact: true })).toBeVisible();

    // The owner removes the member through the wired roster.
    await owner.page.goto("/");
    await projectMenuAction(owner.page, "Shared Plan", "Członkowie");
    const dialog = owner.page.getByRole("dialog", { name: "Członkowie: Shared Plan" });
    const removed = owner.page.waitForResponse((r) => r.request().method() === "DELETE" && /\/members\//.test(r.url()) && r.ok());
    await dialog.getByRole("button", { name: "Usuń Mia Member" }).click();
    await owner.page.getByRole("dialog", { name: "Usuń członka" }).getByRole("button", { name: "Usuń członka" }).click();
    await removed;

    // The removed member loses ALL access — the project is gone from their sidebar (R10).
    await member.page.reload();
    await expect(sidebarTree(member.page).getByText("Shared Plan", { exact: true })).toHaveCount(0);

    // The owner unshares from the still-open members dialog — the project round-trips back to personal.
    const unshared = owner.page.waitForResponse((r) => r.request().method() === "PATCH" && /\/unshare$/.test(r.url()) && r.ok());
    await dialog.getByRole("button", { name: "Cofnij udostępnianie" }).click();
    await owner.page.getByRole("dialog", { name: "Cofnij udostępnianie" }).getByRole("button", { name: "Cofnij udostępnianie" }).click();
    await unshared;
    // Close the (now-stale) members dialog and confirm the row menu offers "Share" again
    // (the project round-tripped to personal).
    await owner.page.keyboard.press("Escape");
    await owner.page.getByRole("button", { name: "Akcje projektu Shared Plan" }).click();
    await expect(
      owner.page
        .getByRole("menu", { name: "Akcje projektu Shared Plan" })
        .getByRole("menuitem", { name: "Udostępnij", exact: true }),
    ).toBeVisible();

    await owner.context.close();
    await member.context.close();
  });

  test("a non-owner MEMBER's project menu carries the same management affordances (server denial stays authoritative) [INV-017]", async ({
    browser,
  }) => {
    const owner = await signedInPage(browser, "a3-owner", "Olivia Owner");
    const member = await signedInPage(browser, "a3-member", "Vera Viewer");

    const project = await owner.api.createProject({ name: "Wspólny Kąt", color: COLOR, icon: ICON });
    const shared = await owner.api.shareProject(project.id, project.version);
    await owner.api.inviteMember(project.id, member.email, "viewer", shared.version);

    // Recorded as-is from the pre-redesign app (INV-017): the sidebar menu offers the SAME
    // management items to every member — role gating is server-side, not affordance-side.
    await member.page.goto("/");
    await member.page.getByRole("button", { name: "Akcje projektu Wspólny Kąt" }).click();
    const menu = member.page.getByRole("menu", { name: "Akcje projektu Wspólny Kąt" });
    for (const item of ["Członkowie", "Edytuj", "Archiwizuj", "Usuń"]) {
      await expect(menu.getByRole("menuitem", { name: item, exact: true })).toBeVisible();
    }

    await owner.context.close();
    await member.context.close();
  });

  test("role semantics server-side: a VIEWER's task writes are denied; assignment cleanup follows membership loss [INV-091] [INV-094]", async ({
    browser,
  }) => {
    const owner = await signedInPage(browser, "a4-owner", "Olivia Owner");
    const viewer = await signedInPage(browser, "a4-viewer", "Vera Viewer");

    const project = await owner.api.createProject({ name: "Strefa Ról", color: COLOR, icon: ICON });
    const shared = await owner.api.shareProject(project.id, project.version);
    const invited = await owner.api.inviteMember(project.id, viewer.email, "viewer", shared.version);
    const task = await owner.api.createTask({ title: "Chronione zadanie", position: "a0" });
    await owner.api.moveTask(task.id, project.id, task.version);

    // The move bumped the task's version — read the FRESH row before any versioned write.
    const fresh = await owner.api.request("GET", `/api/projects/${project.id}/tasks`);
    const freshTask = ((await fresh.json()) as { id: string; version: number }[]).find((t) => t.id === task.id)!;

    // INV-091: the viewer READS the shared task…
    const read = await viewer.api.request("GET", `/api/projects/${project.id}/tasks`);
    expect(read.status).toBe(200);
    // …but a WRITE (correct version — the denial is authorization, not concurrency) is
    // denied server-side regardless of any affordance (deny-by-default).
    const denied = await viewer.api.request("PATCH", `/api/tasks/${task.id}/title`, {
      title: "Viewer nadpisuje",
      version: freshTask.version,
    });
    expect([403, 404]).toContain(denied.status);

    // INV-094: assign the (editor-promoted) member, then remove them — the assignment is
    // cleaned up server-side.
    const roster = await owner.api.request("GET", `/api/projects/${project.id}/members`);
    const rosterBody = (await roster.json()) as { version: number };
    const promoted = await owner.api.request("PATCH", `/api/projects/${project.id}/members/${invited.userId}`, {
      role: "editor",
      version: rosterBody.version,
    });
    expect(promoted.status).toBe(200);
    const assigned = await owner.api.request("PATCH", `/api/tasks/${task.id}/assignees`, {
      assigneeIds: [invited.userId],
      version: freshTask.version,
    });
    expect(assigned.status).toBe(200);

    const roster2 = await owner.api.request("GET", `/api/projects/${project.id}/members`);
    const roster2Body = (await roster2.json()) as { version: number };
    const removed = await owner.api.request(
      "DELETE",
      `/api/projects/${project.id}/members/${invited.userId}?version=${roster2Body.version}`,
    );
    expect([200, 204]).toContain(removed.status);

    // The cleanup rides the MemberRemoved domain event (async side effect) — poll until
    // the read model reflects it rather than racing the event handler.
    await expect
      .poll(
        async () => {
          const after = await owner.api.request("GET", `/api/projects/${project.id}/tasks`);
          const rows = (await after.json()) as { id: string; assignees: string[] }[];
          return rows.find((t) => t.id === task.id)?.assignees ?? null;
        },
        { timeout: 10_000 },
      )
      .toEqual([]);

    await owner.context.close();
    await viewer.context.close();
  });
});

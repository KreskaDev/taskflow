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

test.describe("US-12 Project Sharing — wired UI", () => {
  test("AS-01/AS-02: owner shares + invites by email; the member sees a role-gated read-only roster", async ({
    browser,
  }) => {
    const owner = await signedInPage(browser, "a1-owner", "Olivia Owner");
    const member = await signedInPage(browser, "a1-member", "Eddie Editor");

    // Seed the owner's personal project, then drive the SHARE through the wired sidebar.
    await owner.api.createProject({ name: "Team Space", color: COLOR, icon: ICON });
    await owner.page.goto("/");
    await expect(sidebarTree(owner.page).getByText("Team Space", { exact: true })).toBeVisible();

    const shared = owner.page.waitForResponse((r) => r.request().method() === "PATCH" && /\/share$/.test(r.url()) && r.ok());
    await owner.page.getByRole("button", { name: "Share Team Space" }).click();
    await owner.page.getByRole("dialog", { name: "Share project" }).getByRole("button", { name: "Share project" }).click();
    await shared;

    // The shared indicator now renders; the entry point becomes "Manage members".
    await expect(owner.page.getByTestId("shared-indicator").first()).toBeVisible();
    const manage = owner.page.getByRole("button", { name: "Manage members of Team Space" });
    await manage.click();

    const dialog = owner.page.getByRole("dialog", { name: "Members of Team Space" });
    await expect(dialog).toBeVisible();
    // The owner sees the manage surface; the owner NEVER sees Leave (last-owner safeguard, R7).
    await expect(dialog.getByRole("button", { name: "Unshare project" })).toBeVisible();
    await expect(dialog.getByRole("button", { name: "Leave project" })).toHaveCount(0);

    // Invite the member by email at the editor role (the wired form).
    const invited = owner.page.waitForResponse((r) => r.request().method() === "POST" && /\/members$/.test(r.url()) && r.ok());
    await dialog.locator('input[name="invite-email"]').fill(member.email);
    await dialog.getByRole("button", { name: "Invite" }).click();
    await invited;
    await expect(dialog.getByText("Eddie Editor", { exact: true })).toBeVisible();

    // The member sees the shared project with role-aware gating: a read-only roster + Leave, no manage.
    await member.page.goto("/");
    await expect(sidebarTree(member.page).getByText("Team Space", { exact: true })).toBeVisible();
    await expect(member.page.getByTestId("shared-indicator").first()).toBeVisible();
    await member.page.getByRole("button", { name: "Manage members of Team Space" }).click();
    const memberDialog = member.page.getByRole("dialog", { name: "Members of Team Space" });
    await expect(memberDialog).toBeVisible();
    await expect(memberDialog.getByRole("button", { name: "Leave project" })).toBeVisible();
    await expect(memberDialog.locator('input[name="invite-email"]')).toHaveCount(0);
    await expect(memberDialog.getByRole("button", { name: "Unshare project" })).toHaveCount(0);

    await owner.context.close();
    await member.context.close();
  });

  test("AS-04/AS-06: removing a member revokes all access; unshare round-trips to personal", async ({
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
    await owner.page.getByRole("button", { name: "Manage members of Shared Plan" }).click();
    const dialog = owner.page.getByRole("dialog", { name: "Members of Shared Plan" });
    const removed = owner.page.waitForResponse((r) => r.request().method() === "DELETE" && /\/members\//.test(r.url()) && r.ok());
    await dialog.getByRole("button", { name: "Remove Mia Member" }).click();
    await owner.page.getByRole("dialog", { name: "Remove member" }).getByRole("button", { name: "Remove member" }).click();
    await removed;

    // The removed member loses ALL access — the project is gone from their sidebar (R10).
    await member.page.reload();
    await expect(sidebarTree(member.page).getByText("Shared Plan", { exact: true })).toHaveCount(0);

    // The owner unshares from the still-open members dialog — the project round-trips back to personal.
    const unshared = owner.page.waitForResponse((r) => r.request().method() === "PATCH" && /\/unshare$/.test(r.url()) && r.ok());
    await dialog.getByRole("button", { name: "Unshare project" }).click();
    await owner.page.getByRole("dialog", { name: "Unshare project" }).getByRole("button", { name: "Unshare project" }).click();
    await unshared;
    // Close the (now-stale) members dialog and confirm the sidebar offers "Share" again (personal again).
    await owner.page.keyboard.press("Escape");
    await expect(owner.page.getByRole("button", { name: "Share Shared Plan" })).toBeVisible();

    await owner.context.close();
    await member.context.close();
  });
});

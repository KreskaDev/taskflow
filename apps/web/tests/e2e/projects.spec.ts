import { expect, test, type Page } from "@playwright/test";
import { apiAs, ensureUser, insertSession } from "./helpers/seed";

/**
 * Slice-004 Project Management E2E (T042; US-10.AS-01..AS-05/AS-07..AS-11, US-08.AS-05, EC-03, the
 * `M` move incl. move-to-Inbox, and the Inbox narrowing). Drives the REAL BFF→proxy→API path on a
 * seeded session (mirroring tasks.spec.ts) against the migrated Postgres + .NET API booted by
 * global-setup — no API mocking. Each test mints its OWN Google sub + email so the per-user
 * owner-scoping keeps one test's projects/tasks out of another's lists.
 *
 * ─────────────────────────── STATUS: fully wired (T041 + T049–T052) ───────────────────────────
 * Every scenario is a live test. The app-shell orchestration is complete:
 *   - `app/(app)/page.tsx` binds the `M` shortcut (`onMove`) and mounts `ProjectSelector` (T041).
 *   - `Sidebar.tsx` exposes per-project Edit / Archive / Delete affordances that open the edit
 *     `ProjectForm` and `DeleteProjectDialog` (T049/T050); each project row links to its task view.
 *   - `app/(app)/projects/[id]/page.tsx` is the project-tasks view with the move-to-another-project
 *     affordance, enabling the move-to-Inbox round-trip (T051).
 * Covered here: AS-01 (create form), AS-02 (nesting), AS-03 (grandchild prevention by omission),
 * AS-04/EC-03 + AS-10 (delete dispositions), AS-05 (archived hidden), AS-07/08/09 (edit/re-parent),
 * AS-11 (unarchive), US-08.AS-05 (the `M` move + move-to-Inbox), and the Inbox narrowing (FR-021).
 *
 * AS-06 (command-palette SEARCH for an archived project) is slice 013 — out of scope here; the
 * Archived-disclosure bridge (AS-11) covers reaching + unarchiving an archived project (research R8).
 */

const COLOR = "blue";
const ICON = "folder";

/** Seeds a fresh user + session and returns an authenticated page + an API seeder bound to it. */
async function signedInPage(
  browser: import("@playwright/test").Browser,
  key: string,
): Promise<{
  page: Page;
  context: import("@playwright/test").BrowserContext;
  api: ReturnType<typeof apiAs>;
}> {
  const identity = {
    sub: `google-sub-proj-${key}`,
    email: `proj-${key}@taskflow.test`,
    name: "Project Owner",
  };
  const profile = await ensureUser({ ...identity, picture: "https://avatars.test/po.png" });
  const sessionId = await insertSession(profile.id);

  const context = await browser.newContext();
  await context.addCookies([
    { name: "taskflow_session", value: sessionId, url: "http://localhost:3000" },
  ]);
  const page = await context.newPage();
  // `apiAs` mints its carrier with `sub` = the TaskFlow user id (a GUID), matching what the real BFF
  // proxy presents to ownership-scoped endpoints (NOT the Google subject id).
  return { page, context, api: apiAs(profile.id) };
}

/** The sidebar's active project tree (excludes the Archived disclosure list). */
function sidebarTree(page: Page) {
  return page.locator(".tf-sidebar__tree");
}

/* ───────────────────────────────── GREEN: wired UI ───────────────────────────────── */

test.describe("US-10 Project Management — wired UI (GREEN)", () => {
  test("AS-01: the New project control opens a create form with name, color, icon, and parent fields", async ({
    browser,
  }) => {
    const { page, context } = await signedInPage(browser, "as01-form");
    await page.goto("/");

    // The sidebar's dedicated "New project" action opens the creation form (US-10.AS-01). The
    // command-palette path is slice 013; the dedicated action is the slice-004 surface.
    await page.getByRole("button", { name: "New project" }).click();

    const dialog = page.getByRole("dialog", { name: "New project" });
    await expect(dialog).toBeVisible();
    // The four documented fields: name, a preset color picker, a preset icon picker, optional parent.
    await expect(page.getByRole("textbox", { name: "Project name" })).toBeVisible();
    await expect(dialog.getByRole("radio", { name: COLOR })).toBeVisible();
    await expect(dialog.getByRole("radio", { name: ICON })).toBeVisible();
    await expect(dialog.getByRole("combobox")).toBeVisible(); // the parent <select>
    // The parent selector defaults to top-level.
    await expect(dialog.getByRole("combobox")).toHaveValue("");

    await context.close();
  });

  test("AS-02: creating a child under a parent nests it one level under the parent in the sidebar", async ({
    browser,
  }) => {
    const seeded = await signedInPage(browser, "as02-nest");
    await seeded.page.goto("/");

    // Seed a parent project through the real API so it's available as a parent CHOICE in the form.
    await seeded.api.createProject({ name: "Work", color: COLOR, icon: ICON });
    await seeded.page.reload();
    await expect(sidebarTree(seeded.page).getByText("Work", { exact: true })).toBeVisible();

    // Create a CHILD via the wired form, choosing "Work" as parent (AS-02).
    await seeded.page.getByRole("button", { name: "New project" }).click();
    const dialog = seeded.page.getByRole("dialog", { name: "New project" });
    await seeded.page.getByRole("textbox", { name: "Project name" }).fill("Backend");
    await dialog.getByRole("combobox").selectOption({ label: "Work" });
    const created = seeded.page.waitForResponse(
      (r) => r.request().method() === "PUT" && /\/api\/projects\//.test(r.url()) && r.ok(),
    );
    await dialog.getByRole("button", { name: "Create" }).click();
    await created;

    // The child renders NESTED under the parent (one level): "Backend" lives inside the parent's
    // children sublist (`.tf-sidebar__children`), never as a top-level root.
    const childList = sidebarTree(seeded.page).locator(".tf-sidebar__children");
    await expect(childList.getByText("Backend", { exact: true })).toBeVisible();

    await seeded.context.close();
  });

  test("AS-03: the create form prevents grandchildren — its parent picker offers only top-level projects, never a child", async ({
    browser,
  }) => {
    const seeded = await signedInPage(browser, "as03-no-grandchild");
    await seeded.page.goto("/");

    // Seed a parent → child chain via the API. The one-level rule (FR-012) means a project that is
    // ALREADY a child must never be selectable as a parent — choosing it would create a grandchild.
    const parent = await seeded.api.createProject({ name: "Parent", color: COLOR, icon: ICON });
    await seeded.api.createProject({ name: "Child", color: COLOR, icon: ICON, parentId: parent.id });
    await seeded.page.reload();
    await expect(sidebarTree(seeded.page).getByText("Parent", { exact: true })).toBeVisible();

    // Open the create form and inspect the parent <select>. AS-03's grandchild prevention is realized
    // on the create path by OMISSION: the picker lists "Parent" (top-level, a legal parent) but NOT
    // "Child" (already nested) — so there is no UI path to even attempt a grandchild (FR-012).
    await seeded.page.getByRole("button", { name: "New project" }).click();
    const select = seeded.page.getByRole("dialog", { name: "New project" }).getByRole("combobox");
    // `exact` because the default "No parent (top-level)" option's name contains the substring
    // "parent" — a non-exact match would conflate the two.
    await expect(select.getByRole("option", { name: "Parent", exact: true })).toHaveCount(1);
    await expect(select.getByRole("option", { name: "Child", exact: true })).toHaveCount(0);

    await seeded.context.close();
  });

  test("AS-05: an archived project is NOT visible in the sidebar's default tree", async ({
    browser,
  }) => {
    const { page, context, api } = await signedInPage(browser, "as05-hidden");

    // Two projects; archive one through the real API (no UI archive trigger exists yet — archiving
    // is a precondition here, and the assertion is purely about the sidebar's default visibility).
    await api.createProject({ name: "Visible", color: COLOR, icon: ICON });
    const doomed = await api.createProject({ name: "Hidden", color: COLOR, icon: ICON });
    await api.archiveProject(doomed.id, doomed.version);

    await page.goto("/");

    // The active tree shows the non-archived project but NOT the archived one (AS-05).
    await expect(sidebarTree(page).getByText("Visible", { exact: true })).toBeVisible();
    await expect(sidebarTree(page).getByText("Hidden", { exact: true })).toHaveCount(0);

    await context.close();
  });

  test("AS-11: an archived project is reachable via the Archived disclosure and can be unarchived back to the tree", async ({
    browser,
  }) => {
    const { page, context, api } = await signedInPage(browser, "as11-unarchive");

    const project = await api.createProject({ name: "Dormant", color: COLOR, icon: ICON });
    await api.archiveProject(project.id, project.version);

    await page.goto("/");
    // It is hidden from the default tree (AS-05) …
    await expect(sidebarTree(page).getByText("Dormant", { exact: true })).toHaveCount(0);

    // … but reachable via the keyboard-operable "Archived" disclosure (R8 bridge for AS-11).
    await page.getByRole("button", { name: /Archived/ }).click();
    const archivedRow = page.locator(".tf-sidebar__archived-row").filter({ hasText: "Dormant" });
    await expect(archivedRow).toBeVisible();

    // Unarchive (AS-11): the wired "Unarchive" control restores it to the default tree.
    const unarchived = page.waitForResponse(
      (r) => r.request().method() === "PATCH" && /\/unarchive$/.test(r.url()) && r.ok(),
    );
    await archivedRow.getByRole("button", { name: "Unarchive" }).click();
    await unarchived;

    await expect(sidebarTree(page).getByText("Dormant", { exact: true })).toBeVisible();

    await context.close();
  });

  test("Inbox narrowing (FR-021): a task moved into a project leaves the Inbox list", async ({
    browser,
  }) => {
    const { page, context, api } = await signedInPage(browser, "inbox-narrow");

    // Two Inbox tasks + a project; move ONE task into the project via the real API (the `M` UI is
    // not wired — this asserts the server-side narrowing FR-021/R6 surfaces in the wired Inbox view).
    const stay = await api.createTask({ title: "Stays in Inbox", position: "a0" });
    void stay;
    const move = await api.createTask({ title: "Goes to project", position: "a1" });
    const project = await api.createProject({ name: "Destination", color: COLOR, icon: ICON });

    await page.goto("/");
    // Both tasks start in the Inbox (the narrowed GET /api/tasks).
    await expect(page.getByRole("option")).toHaveCount(2);

    await api.moveTask(move.id, project.id, move.version);
    await page.reload();

    // After the move, only the unprojected task remains in the Inbox list (FR-021).
    await expect(page.getByRole("option")).toHaveCount(1);
    await expect(page.getByRole("option").first()).toHaveText(/Stays in Inbox/);
    await expect(page.getByText(/Goes to project/)).toHaveCount(0);

    await context.close();
  });
});

/* ───── RED + FIXME: behaviours blocked on the T041/T028 wiring gap (and one beyond it) ───── */

test.describe("US-10/US-08 Project Management — edit / delete / move-to-project (wired, T041/T049–T052)", () => {
  // The `M` move (real trigger) plus the edit/archive/delete affordances and the project-tasks view
  // are now wired into the app shell (T049–T052), so every scenario below is a live test.

  test("US-08.AS-05: pressing M on the selected task opens the move-to-project selector", async ({
    browser,
  }) => {
    const seeded = await signedInPage(browser, "as05-move");
    await seeded.api.createTask({ title: "Movable", position: "a0" });
    await seeded.api.createProject({ name: "Target", color: COLOR, icon: ICON });

    await seeded.page.goto("/");
    await expect(seeded.page.getByRole("option")).toHaveCount(1);

    // Select the row (it defaults to index 0) and press `M`. The selector dialog should open with
    // the Inbox option + the owned project as keyboard-reachable choices (US-08.AS-05, R7).
    const listbox = seeded.page.getByRole("listbox", { name: "Tasks" });
    await listbox.focus();
    await seeded.page.keyboard.press("m");

    const selector = seeded.page.getByRole("dialog", { name: /Move/ });
    await expect(selector).toBeVisible({ timeout: 5_000 });
    await expect(selector.getByRole("button", { name: "Inbox" })).toBeVisible();
    await expect(selector.getByRole("button", { name: /Target/ })).toBeVisible();

    await seeded.context.close();
  });

  // The move-to-Inbox ROUND-TRIP exercises the `/projects/[id]` view (T051): a projected task is
  // absent from the Inbox (FR-021), so its row + move affordance live on the project view, where
  // choosing "Inbox" in the selector returns it to the Inbox.
  test(
    "US-08.AS-05 (move-to-Inbox round-trip): a projected task is moved back to the Inbox via the selector",
    async ({ browser }) => {
      const { page, context, api } = await signedInPage(browser, "as05-to-inbox");

      const task = await api.createTask({ title: "In a project", position: "a0" });
      const project = await api.createProject({ name: "Holder", color: COLOR, icon: ICON });
      await api.moveTask(task.id, project.id, task.version);

      // On the project view, the projected task row carries a "Move … to another project" trigger
      // → opening the selector → choosing "Inbox" (projectId = null, R6/R7) returns it to the Inbox.
      await page.goto(`/projects/${project.id}`);
      await page.getByRole("button", { name: /Move .* to another project/ }).click();
      // Await the move PATCH before navigating, so the round-trip never races the server round-trip.
      const moved = page.waitForResponse(
        (r) => r.request().method() === "PATCH" && /\/api\/tasks\/.*\/project$/.test(r.url()) && r.ok(),
      );
      await page.getByRole("dialog", { name: /Move/ }).getByRole("button", { name: "Inbox" }).click();
      await moved;

      await page.goto("/");
      await expect(page.getByRole("option").filter({ hasText: "In a project" })).toBeVisible();

      await context.close();
    },
  );

  // EDIT / RE-PARENT (AS-07/08/09): the sidebar's per-project Edit affordance opens the edit
  // ProjectForm (T049); these drive it through rename + the allowed/rejected re-parent paths.
  test("AS-07: editing a project's name/color/icon/parent persists and reflects in the sidebar", async ({
    browser,
  }) => {
    const { page, context, api } = await signedInPage(browser, "as07-edit");
    const project = await api.createProject({ name: "Old name", color: COLOR, icon: ICON });
    void project;

    await page.goto("/");
    await expect(sidebarTree(page).getByText("Old name", { exact: true })).toBeVisible();

    // The sidebar Edit affordance opens the editor seeded with the project; renaming + Save persists
    // and the sidebar reflects the new name.
    await page.getByRole("button", { name: "Edit Old name" }).click();
    await page.getByRole("dialog", { name: "Edit project" }).waitFor();
    await page.getByRole("textbox", { name: "Project name" }).fill("New name");
    await page.getByRole("button", { name: "Save" }).click();
    await expect(sidebarTree(page).getByText("New name", { exact: true })).toBeVisible();

    await context.close();
  });

  test("AS-08: re-parenting a top-level project under another top-level project is allowed", async ({
    browser,
  }) => {
    const { page, context, api } = await signedInPage(browser, "as08-reparent-ok");
    await api.createProject({ name: "Mover", color: COLOR, icon: ICON });
    await api.createProject({ name: "NewParent", color: COLOR, icon: ICON });

    await page.goto("/");
    await expect(sidebarTree(page).getByText("Mover", { exact: true })).toBeVisible();

    // Opening the editor on "Mover", choosing "NewParent" as parent, and saving nests it one
    // level under "NewParent" (AS-08, within FR-012).
    await page.getByRole("button", { name: "Edit Mover" }).click();
    const dialog = page.getByRole("dialog", { name: "Edit project" });
    await dialog.waitFor();
    await dialog.getByRole("combobox").selectOption({ label: "NewParent" });
    await page.getByRole("button", { name: "Save" }).click();
    await expect(sidebarTree(page).locator(".tf-sidebar__children").getByText("Mover", { exact: true })).toBeVisible();

    await context.close();
  });

  test("AS-09: re-parenting that would create a grandchild is rejected with the one-level message (FR-049)", async ({
    browser,
  }) => {
    const { page, context, api } = await signedInPage(browser, "as09-reparent-reject");
    const root = await api.createProject({ name: "Root", color: COLOR, icon: ICON });
    // "Mover" already has its OWN child, so re-parenting it under anything would create a grandchild.
    const mover = await api.createProject({ name: "Mover", color: COLOR, icon: ICON });
    await api.createProject({ name: "Leaf", color: COLOR, icon: ICON, parentId: mover.id });
    void root;

    await page.goto("/");
    await expect(sidebarTree(page).getByText("Mover", { exact: true })).toBeVisible();

    // Opening the editor on "Mover" (which has children) and attempting to set "Root" as its parent
    // surfaces the inline one-level-nesting message (R15/FR-049) and disables Save.
    await page.getByRole("button", { name: "Edit Mover" }).click();
    const dialog = page.getByRole("dialog", { name: "Edit project" });
    await dialog.waitFor();
    await dialog.getByRole("combobox").selectOption({ label: "Root" });
    await expect(dialog.getByRole("status")).toHaveText(/one level|nest/i);
    await expect(page.getByRole("button", { name: "Save" })).toBeDisabled();

    await context.close();
  });

  // DELETE (AS-04/EC-03, AS-10): the sidebar's per-project Delete affordance opens the
  // DeleteProjectDialog (T050) with the task/child disposition prompts.
  test("AS-04 / EC-03: deleting a project with tasks prompts the three-way task disposition", async ({
    browser,
  }) => {
    const { page, context, api } = await signedInPage(browser, "ec03-delete-tasks");
    const project = await api.createProject({ name: "Busy", color: COLOR, icon: ICON });
    const task = await api.createTask({ title: "Owned task", position: "a0" });
    await api.moveTask(task.id, project.id, task.version);

    await page.goto("/");
    await expect(sidebarTree(page).getByText("Busy", { exact: true })).toBeVisible();

    // The sidebar Delete affordance opens the DeleteProjectDialog with the THREE task dispositions
    // (move-to-Inbox / archive-with-tasks / cascade), defaulting to the least-destructive choice.
    await page.getByRole("button", { name: "Delete Busy" }).click();
    const dialog = page.getByRole("dialog", { name: "Delete project" });
    await dialog.waitFor();
    await expect(dialog.getByRole("radio", { name: /Move them to the Inbox/ })).toBeVisible();
    await expect(dialog.getByRole("radio", { name: /Archive the project instead/ })).toBeVisible();
    await expect(dialog.getByRole("radio", { name: /Delete them too/ })).toBeVisible();

    await context.close();
  });

  test("AS-10: deleting a parent with children prompts the child disposition (cascade vs orphan-to-top)", async ({
    browser,
  }) => {
    const { page, context, api } = await signedInPage(browser, "as10-delete-children");
    const parent = await api.createProject({ name: "Umbrella", color: COLOR, icon: ICON });
    await api.createProject({ name: "Sub", color: COLOR, icon: ICON, parentId: parent.id });

    await page.goto("/");
    await expect(sidebarTree(page).getByText("Umbrella", { exact: true })).toBeVisible();

    // The sidebar Delete affordance surfaces the TWO-way child disposition with its blast radius
    // (Principle VII): orphan-to-top (default) vs cascade.
    await page.getByRole("button", { name: "Delete Umbrella" }).click();
    const dialog = page.getByRole("dialog", { name: "Delete project" });
    await dialog.waitFor();
    await expect(dialog.getByRole("radio", { name: /Promote them to top-level/ })).toBeVisible();
    await expect(dialog.getByRole("radio", { name: /Delete them too/ })).toBeVisible();

    await context.close();
  });

  test("AS-10 (archive): archiving a parent with children prompts the child disposition", async ({
    browser,
  }) => {
    const { page, context, api } = await signedInPage(browser, "as10-archive-children");
    const parent = await api.createProject({ name: "Canopy", color: COLOR, icon: ICON });
    await api.createProject({ name: "Twig", color: COLOR, icon: ICON, parentId: parent.id });

    await page.goto("/");
    await expect(sidebarTree(page).getByText("Canopy", { exact: true })).toBeVisible();

    // AS-10 covers archive AS WELL AS delete: archiving a parent-with-children prompts the child
    // disposition (cascade-archive the subtree vs orphan-to-top) and states its blast radius — it does
    // NOT silently default. Archive keeps the project's tasks, so there is no task disposition.
    await page.getByRole("button", { name: "Archive Canopy" }).click();
    const dialog = page.getByRole("dialog", { name: "Archive project" });
    await dialog.waitFor();
    await expect(dialog.getByRole("radio", { name: /Promote them to top-level/ })).toBeVisible();
    await expect(dialog.getByRole("radio", { name: /Archive them too/ })).toBeVisible();

    // Confirming with the default (orphan-to-top) archives the parent → it leaves the default tree.
    const archived = page.waitForResponse(
      (r) => r.request().method() === "PATCH" && /\/archive$/.test(r.url()) && r.ok(),
    );
    await dialog.getByRole("button", { name: "Archive project" }).click();
    await archived;
    await expect(sidebarTree(page).getByText("Canopy", { exact: true })).toHaveCount(0);
    // The promoted child remains in the active tree (orphan-to-top), now top-level.
    await expect(sidebarTree(page).getByText("Twig", { exact: true })).toBeVisible();

    await context.close();
  });
});

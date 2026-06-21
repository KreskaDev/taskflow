import { expect, test, type Page } from "@playwright/test";
import { ensureUser, insertSession } from "./helpers/seed";

/**
 * US1 Daily Task Capture E2E (T041; US-01.AS-01/06/07, AS-09 precursor, EC-01). The real
 * end-to-end MVP proof: a fresh authenticated user drives the `C` capture surface through the
 * REAL BFF→proxy→API path against a migrated Postgres + .NET API (booted by global-setup) — no
 * API mocking. Auth is a seeded session (mirroring auth.spec.ts), so each test gets a pristine,
 * isolated account and never reinvents the OAuth dance.
 *
 * Each test mints its OWN Google sub + email: `ensureUser` reaches the real API which enforces
 * UNIQUE(email), and the Postgres container is shared for the whole run, so distinct identities
 * are what keep one test's tasks out of another's list (test isolation).
 */

/** Seeds a fresh user + session and returns an authenticated page landed on the workspace. */
async function signedInPage(
  browser: import("@playwright/test").Browser,
  key: string,
): Promise<{ page: Page; context: import("@playwright/test").BrowserContext }> {
  const profile = await ensureUser({
    sub: `google-sub-${key}`,
    email: `${key}@taskflow.test`,
    name: "Task Capturer",
    picture: "https://avatars.test/tc.png",
  });
  const sessionId = await insertSession(profile.id);

  const context = await browser.newContext();
  await context.addCookies([
    { name: "taskflow_session", value: sessionId, url: "http://localhost:3000" },
  ]);
  const page = await context.newPage();
  return { page, context };
}

/**
 * Opens capture (`C`), types a title, presses Enter, and waits for the optimistic create's REAL
 * server write (the idempotent PUT) to land. The `waitForResponse` promise is ARMED before Enter
 * so it can never miss a fast-resolving PUT (the matcher's `PUT` method discriminates it from the
 * `GET /api/tasks` refetch).
 */
async function createTask(page: Page, title: string): Promise<void> {
  await page.keyboard.press("c");
  await page.getByRole("textbox", { name: "Task title" }).fill(title);
  const settled = page.waitForResponse(
    (r) => r.request().method() === "PUT" && /\/api\/tasks\//.test(r.url()) && r.ok(),
  );
  await page.keyboard.press("Enter");
  await settled;
}

test.describe("US1 Daily Task Capture (AS-01/06/07/09, EC-01)", () => {
  test("EC-01: a fresh user sees the accessible empty-Inbox hint and zero options", async ({
    browser,
  }) => {
    const { page, context } = await signedInPage(browser, "tasks-empty");
    await page.goto("/");

    // The empty-Inbox hint asserts the query RESOLVED with zero rows (page.tsx swaps the
    // listbox out for this <p> only once `isEmpty` is true). Asserting it first waits out the
    // transient loading state where the listbox flashes with 0 options.
    const hint = page.locator("p.tf-workspace__empty");
    await expect(hint).toBeVisible();
    await expect(hint).toContainText(/press/i);
    await expect(hint.locator("kbd")).toHaveText("C"); // the <kbd>C</kbd> press hint
    await expect(page.getByRole("option")).toHaveCount(0);

    await context.close();
  });

  test("AS-01: pressing C opens the capture dialog with the title input focused", async ({
    browser,
  }) => {
    const { page, context } = await signedInPage(browser, "tasks-focus");
    await page.goto("/");
    await expect(page.getByText(/your inbox is empty/i)).toBeVisible();

    await page.keyboard.press("c");

    const dialog = page.getByRole("dialog", { name: "Create task" });
    await expect(dialog).toBeVisible();
    // SC-003 / AS-01: the single title input receives initial focus synchronously.
    await expect(page.getByRole("textbox", { name: "Task title" })).toBeFocused();

    await context.close();
  });

  test("AS-06: Enter creates, the row paints at the top newest-first, and persists across reload", async ({
    browser,
  }) => {
    const { page, context } = await signedInPage(browser, "tasks-create");
    await page.goto("/");
    await expect(page.getByText(/your inbox is empty/i)).toBeVisible();

    // First task. Dialog closes; the optimistic row paints in the listbox.
    await createTask(page, "First task");
    await expect(page.getByRole("dialog", { name: "Create task" })).toBeHidden();
    await expect(page.getByRole("option")).toHaveCount(1);
    await expect(page.getByRole("option").first()).toHaveText(/First task/);

    // Second task → newest-first means it lands ABOVE the first.
    await createTask(page, "Second task");
    await expect(page.getByRole("option")).toHaveCount(2);
    await expect(page.getByRole("option").nth(0)).toHaveText(/Second task/);
    await expect(page.getByRole("option").nth(1)).toHaveText(/First task/);

    // Server round-trip: reload and assert both tasks persist AND newest-first order holds.
    await page.reload();
    await expect(page.getByRole("option")).toHaveCount(2);
    await expect(page.getByRole("option").nth(0)).toHaveText(/Second task/);
    await expect(page.getByRole("option").nth(1)).toHaveText(/First task/);

    await context.close();
  });

  test("AS-07: Esc cancels — no task is created and focus returns to the invoker", async ({
    browser,
  }) => {
    const { page, context } = await signedInPage(browser, "tasks-cancel");
    await page.goto("/");
    await expect(page.getByText(/your inbox is empty/i)).toBeVisible();

    // Establish a deterministic invoker: create one task so the listbox exists, then focus it.
    // Pressing `c` on the listbox (a div — not input/textarea/contenteditable) DOES open capture.
    await createTask(page, "Keeper");
    await expect(page.getByRole("option")).toHaveCount(1);

    const listbox = page.getByRole("listbox", { name: "Tasks" });
    await listbox.focus();
    await expect(listbox).toBeFocused();

    await page.keyboard.press("c");
    await expect(page.getByRole("dialog", { name: "Create task" })).toBeVisible();
    await page.getByRole("textbox", { name: "Task title" }).fill("Discarded draft");
    await page.keyboard.press("Escape");

    // No task created (count unchanged) and focus restored to the invoking listbox.
    await expect(page.getByRole("dialog", { name: "Create task" })).toBeHidden();
    await expect(page.getByRole("option")).toHaveCount(1);
    await expect(listbox).toBeFocused();

    await context.close();
  });

  test("AS-09 precursor: typing C inside the capture input inserts the character (no nested capture)", async ({
    browser,
  }) => {
    const { page, context } = await signedInPage(browser, "tasks-suppress");
    await page.goto("/");
    await expect(page.getByText(/your inbox is empty/i)).toBeVisible();

    await page.keyboard.press("c");
    const input = page.getByRole("textbox", { name: "Task title" });
    await expect(input).toBeFocused();

    // The capture surface's document-level `C` listener must NOT hijack a `C` typed into the
    // focused input (FR-031 / AS-09 precursor): the char lands in the field, no nested dialog.
    await page.keyboard.type("Cabbage");
    await expect(input).toHaveValue("Cabbage");
    await expect(page.getByRole("dialog", { name: "Create task" })).toHaveCount(1);

    await context.close();
  });
});

/**
 * US1 Natural-Language Dates capture E2E (slice 003, T019; US-01.AS-02..AS-05 + EC-02 + the
 * "version number is not a date" guard). Drives the SAME `C` capture surface as the slice-002
 * specs through the REAL BFF→proxy→API write path on a seeded session — the slice-003 delta is
 * purely client-side parsing (`lib/dates.ts`) feeding the create payload, so these prove the
 * end-to-end behaviour: a trailing Polish date phrase is stripped from the title and paints a
 * due-date label on the row; an impossible date attempt ("30.02") creates nothing and announces
 * "nie rozpoznano"; a non-date trailing token ("2.0") is left in the title with no error.
 *
 * CLOCK NOTE: the parser runs against the REAL system clock (no `now` injection on the live
 * `C`→Enter path — only the Vitest unit suite injects `now`). So these assertions are robust to
 * wall-clock time: they assert the TITLE is correctly stripped and that a due-date label is
 * VISIBLE for the resolved cases, but never assert an exact instant/time-of-day (the unit tests
 * own exact instants). The title node is asserted SPECIFICALLY (`.tf-task-row__title`) rather
 * than the whole row, because the row also contains the date label — a whole-row substring match
 * would wrongly pass even if "po 17" leaked into the title.
 */
test.describe("US1 Natural-Language Dates (AS-02..05 capture-with-date, EC-02, version guard)", () => {
  /** The visible title text of the top (newest-first) row — the strip-correctness probe. */
  const topTitle = (page: Page) =>
    page.getByRole("option").first().locator(".tf-task-row__title");
  /** The due-date label on the top row (FR-046 visible, non-hover affordance). */
  const topDue = (page: Page) => page.getByRole("option").first().locator(".tf-task-row__due");

  // AS-02..AS-05 + the date-only/explicit-time rows: typing the full raw input and Enter creates
  // a task whose title is the STRIPPED prefix and whose row shows a due-date label. `createTask`
  // arms the optimistic PUT's waitForResponse before Enter, so the server write is awaited.
  const captureCases: ReadonlyArray<{ scenario: string; input: string; title: string }> = [
    { scenario: "AS-02 time-today (po)", input: "Kupic mleko po 17", title: "Kupic mleko" },
    { scenario: "AS-03 tomorrow", input: "Raport jutro", title: "Raport" },
    { scenario: "AS-04 weekday", input: "Meeting piatek", title: "Meeting" },
    { scenario: "AS-05 relative days", input: "Zakupy za 3 dni", title: "Zakupy" },
    { scenario: "explicit time (o HH:MM)", input: "Call o 9:30", title: "Call" },
    { scenario: "day.month (DD.MM)", input: "Urodziny 30.06", title: "Urodziny" },
  ];

  for (const { scenario, input, title } of captureCases) {
    test(`${scenario}: "${input}" → title "${title}" stripped + due-date label visible`, async ({
      browser,
    }) => {
      const { page, context } = await signedInPage(
        browser,
        `dates-${title.toLowerCase().replace(/\s+/g, "-")}`,
      );
      await page.goto("/");
      await expect(page.getByText(/your inbox is empty/i)).toBeVisible();

      await createTask(page, input);

      // Exactly one row, and its TITLE is the stripped prefix (string match = exact equality, so
      // a leaked date phrase like "Kupic mleko po 17" would fail). This is the strip proof.
      await expect(page.getByRole("option")).toHaveCount(1);
      await expect(topTitle(page)).toHaveText(title);

      // The resolved due date paints a visible label on the row (the end-to-end point — we do NOT
      // assert the exact instant; the unit suite owns that against an injected clock).
      await expect(topDue(page)).toBeVisible();

      await context.close();
    });
  }

  test('EC-02: "Spotkanie 30.02" creates NO task and announces "nie rozpoznano"; field retains value', async ({
    browser,
  }) => {
    const { page, context } = await signedInPage(browser, "dates-ec02-impossible");
    await page.goto("/");
    await expect(page.getByText(/your inbox is empty/i)).toBeVisible();

    await page.keyboard.press("c");
    const input = page.getByRole("textbox", { name: "Task title" });
    await expect(input).toBeFocused();
    await input.fill("Spotkanie 30.02");

    // An impossible in-range date ("30.02") is a genuine trailing date ATTEMPT that fails to
    // resolve → NO mutation fires (so there is no PUT to wait on — waiting would hang). Enter
    // surfaces the recoverable failure synchronously via the polite status node; that visibility
    // is the synchronization point. Target the node by its stable id to dodge the other role=status
    // nodes layout.tsx / the slice-002 LiveRegion mount (strict-mode ambiguity).
    await page.keyboard.press("Enter");

    const errorNode = page.locator("#task-capture-error");
    await expect(errorNode).toBeVisible();
    await expect(errorNode).toHaveText(/nie rozpoznano/i);

    // No task was created (the inbox stays empty), and the dialog stays open with the field's value
    // retained so the user can fix the phrase (EC-02 / FR-006).
    await expect(page.getByRole("option")).toHaveCount(0);
    await expect(page.getByText(/your inbox is empty/i)).toBeVisible();
    await expect(page.getByRole("dialog", { name: "Create task" })).toBeVisible();
    await expect(input).toHaveValue("Spotkanie 30.02");

    await context.close();
  });

  test('guard: "Wersja 2.0" is created as-is with NO due-date label and NO error', async ({
    browser,
  }) => {
    const { page, context } = await signedInPage(browser, "dates-guard-version");
    await page.goto("/");
    await expect(page.getByText(/your inbox is empty/i)).toBeVisible();

    // "2.0" is NOT a date-shaped trailing token (out of clock/calendar range, R4) → the whole
    // string is the title, no due date, no error. `createTask` proves a real server write landed.
    await createTask(page, "Wersja 2.0");

    await expect(page.getByRole("option")).toHaveCount(1);
    await expect(topTitle(page)).toHaveText("Wersja 2.0");
    // No due-date label rendered, and the capture surface raised no recoverable-failure message.
    await expect(topDue(page)).toHaveCount(0);
    await expect(page.locator("#task-capture-error")).toHaveCount(0);

    await context.close();
  });
});

/**
 * US8 Keyboard Navigation & Operate E2E (T059; US-08.AS-03/07/09 + Space/E/Del/Alt+↑↓ operate +
 * virtualization-focus). Drives the REAL listbox keyboard surface (the page-owned global gate +
 * the controlled TaskList/TaskRow) through the same seeded-session auth and the REAL BFF→proxy→API
 * write path as the US1 specs. Reuses {@link signedInPage} (fresh isolated account per test, keyed
 * by a unique email/sub) and {@link createTask} (arms the optimistic PUT's waitForResponse before
 * Enter). Every mutating operate key arms its OWN waitForResponse (discriminated by HTTP method so
 * the onSettled GET refetch is never mistaken for the write) before any `reload()` so the
 * persistence assertions never race the server.
 */
test.describe("US8 Keyboard Nav & Operate (AS-03/07/09, Space/E/Del/Alt+↑↓, virtualization)", () => {
  /** The browser talks to the BFF proxy, so write URLs are `/api/proxy/api/tasks/<id>/...`. */
  const statusWrite = (r: import("@playwright/test").Response) =>
    r.request().method() === "PATCH" && /\/api\/tasks\/.+\/status/.test(r.url()) && r.ok();
  const titleWrite = (r: import("@playwright/test").Response) =>
    r.request().method() === "PATCH" && /\/api\/tasks\/.+\/title/.test(r.url()) && r.ok();
  const positionWrite = (r: import("@playwright/test").Response) =>
    r.request().method() === "PATCH" && /\/api\/tasks\/.+\/position/.test(r.url()) && r.ok();
  const deleteWrite = (r: import("@playwright/test").Response) =>
    r.request().method() === "DELETE" && /\/api\/tasks\//.test(r.url()) && r.ok();

  /** Seeds N tasks newest-first via the UI capture path and returns titles in render order (top-first). */
  async function seedTasks(page: Page, titles: string[]): Promise<void> {
    for (const title of titles) {
      await createTask(page, title);
    }
    // Newest-first: the LAST created lands at the TOP. Assert the top row to settle the seed in a
    // way compatible with virtualization — @tanstack/react-virtual only mounts the visible window
    // (~21 rows here), so asserting the full `titles.length` option count would (wrongly) fail for
    // large seeds even though every task was created. Each caller asserts its own order afterward.
    await expect(page.getByRole("option").first()).toHaveText(
      new RegExp(titles[titles.length - 1]!),
    );
  }

  test("AS-03: ↑/↓ move the selection (aria-selected + listbox aria-activedescendant track it)", async ({
    browser,
  }) => {
    const { page, context } = await signedInPage(browser, "us8-nav");
    await page.goto("/");
    await expect(page.getByText(/your inbox is empty/i)).toBeVisible();

    // Newest-first ⇒ render order top→bottom is Gamma, Beta, Alpha.
    await seedTasks(page, ["Alpha", "Beta", "Gamma"]);

    const listbox = page.getByRole("listbox", { name: "Tasks" });
    const options = page.getByRole("option");

    // selectedIndex defaults to 0, so the TOP row is already the active option on load.
    const top = options.nth(0);
    const second = options.nth(1);
    await expect(top).toHaveAttribute("aria-selected", "true");
    const topId = await top.getAttribute("id");
    expect(topId).toBeTruthy();
    await expect(listbox).toHaveAttribute("aria-activedescendant", topId!);

    // ArrowDown → selection moves to the second row; both signals follow it.
    await listbox.focus();
    await page.keyboard.press("ArrowDown");
    await expect(second).toHaveAttribute("aria-selected", "true");
    await expect(top).toHaveAttribute("aria-selected", "false");
    const secondId = await second.getAttribute("id");
    await expect(listbox).toHaveAttribute("aria-activedescendant", secondId!);

    // ArrowUp → back to the top row.
    await page.keyboard.press("ArrowUp");
    await expect(top).toHaveAttribute("aria-selected", "true");
    await expect(listbox).toHaveAttribute("aria-activedescendant", topId!);

    await context.close();
  });

  test("AS-07: '?' opens the shortcuts help; Esc closes it and returns focus to the listbox", async ({
    browser,
  }) => {
    const { page, context } = await signedInPage(browser, "us8-help");
    await page.goto("/");
    await expect(page.getByText(/your inbox is empty/i)).toBeVisible();

    // A row makes the listbox exist; focus it so it's the help dialog's invoker (focus-return target).
    await createTask(page, "Anchor");
    const listbox = page.getByRole("listbox", { name: "Tasks" });
    await listbox.focus();
    await expect(listbox).toBeFocused();

    await page.keyboard.press("?");
    const help = page.getByRole("dialog", { name: "Keyboard shortcuts" });
    await expect(help).toBeVisible();

    // Esc dismisses and the Dialog focus contract restores focus to the invoking listbox.
    await page.keyboard.press("Escape");
    await expect(help).toBeHidden();
    await expect(listbox).toBeFocused();

    await context.close();
  });

  test("AS-09: single-key shortcuts are suppressed while a text input is focused", async ({
    browser,
  }) => {
    const { page, context } = await signedInPage(browser, "us8-suppress");
    await page.goto("/");
    await expect(page.getByText(/your inbox is empty/i)).toBeVisible();

    // Open capture; the title input is focused. The global gate must let C/E/Space/`?` land as
    // literal characters (FR-031/AS-09) — none may be interpreted as a command.
    await page.keyboard.press("c");
    const input = page.getByRole("textbox", { name: "Task title" });
    await expect(input).toBeFocused();

    await page.keyboard.type("Ceb");
    await expect(input).toHaveValue("Ceb");
    // No nested capture dialog spawned (still exactly the one), and no help overlay opened.
    await expect(page.getByRole("dialog", { name: "Create task" })).toHaveCount(1);
    await expect(page.getByRole("dialog", { name: "Keyboard shortcuts" })).toHaveCount(0);

    await context.close();
  });

  test("Space toggles the selected task done↔backlog and the done state persists across reload", async ({
    browser,
  }) => {
    const { page, context } = await signedInPage(browser, "us8-toggle");
    await page.goto("/");
    await expect(page.getByText(/your inbox is empty/i)).toBeVisible();

    await createTask(page, "Toggle me");
    const row = page.getByRole("option").first();
    await expect(row).toHaveAttribute("data-status", "backlog");

    const listbox = page.getByRole("listbox", { name: "Tasks" });
    await listbox.focus();

    // Space → done (the visible ✓ glyph is aria-hidden, so assert the data-status hook the CSS uses).
    const doneWrite = page.waitForResponse(statusWrite);
    await page.keyboard.press(" ");
    await expect(row).toHaveAttribute("data-status", "done");
    await expect(row).toHaveClass(/tf-task-row--done/);
    await doneWrite;

    // Space again → backlog.
    const backWrite = page.waitForResponse(statusWrite);
    await page.keyboard.press(" ");
    await expect(row).toHaveAttribute("data-status", "backlog");
    await backWrite;

    // Toggle to done once more and prove the server persisted it across a reload.
    const persistWrite = page.waitForResponse(statusWrite);
    await page.keyboard.press(" ");
    await expect(row).toHaveAttribute("data-status", "done");
    await persistWrite;

    await page.reload();
    await expect(page.getByRole("option").first()).toHaveAttribute("data-status", "done");

    await context.close();
  });

  test("E renames the selected task inline (Enter commits + persists); Esc keeps the original", async ({
    browser,
  }) => {
    const { page, context } = await signedInPage(browser, "us8-rename");
    await page.goto("/");
    await expect(page.getByText(/your inbox is empty/i)).toBeVisible();

    await createTask(page, "Original title");
    const row = page.getByRole("option").first();
    const listbox = page.getByRole("listbox", { name: "Tasks" });
    await listbox.focus();

    // E → inline rename input, autofocused and seeded with the current title.
    await page.keyboard.press("e");
    const renameInput = page.getByRole("textbox", { name: "Rename task" });
    await expect(renameInput).toBeFocused();
    await expect(renameInput).toHaveValue("Original title");

    // Type a new title + Enter commits; the row shows the new title (never click away — blur cancels).
    const renameSettled = page.waitForResponse(titleWrite);
    await renameInput.fill("Renamed title");
    await renameInput.press("Enter");
    await expect(row).toHaveText(/Renamed title/);
    await renameSettled;

    await page.reload();
    await expect(page.getByRole("option").first()).toHaveText(/Renamed title/);

    // Esc path: open rename again, Esc, and the committed title stays intact (no write).
    await page.getByRole("listbox", { name: "Tasks" }).focus();
    await page.keyboard.press("e");
    const reopened = page.getByRole("textbox", { name: "Rename task" });
    await expect(reopened).toBeFocused();
    await reopened.fill("Discarded edit");
    await page.keyboard.press("Escape");
    await expect(page.getByRole("textbox", { name: "Rename task" })).toHaveCount(0);
    await expect(page.getByRole("option").first()).toHaveText(/Renamed title/);

    await context.close();
  });

  test("Del soft-deletes the selected task and it stays gone across reload", async ({
    browser,
  }) => {
    const { page, context } = await signedInPage(browser, "us8-delete");
    await page.goto("/");
    await expect(page.getByText(/your inbox is empty/i)).toBeVisible();

    // Two rows so the listbox survives the delete; delete the top (selectedIndex 0).
    await seedTasks(page, ["Keeper", "Doomed"]); // render order top→bottom: Doomed, Keeper
    const top = page.getByRole("option").first();
    await expect(top).toHaveText(/Doomed/);

    const listbox = page.getByRole("listbox", { name: "Tasks" });
    await listbox.focus();

    const deleteSettled = page.waitForResponse(deleteWrite);
    await page.keyboard.press("Delete");
    await expect(page.getByRole("option")).toHaveCount(1);
    await expect(page.getByText(/Doomed/)).toHaveCount(0);
    await deleteSettled;

    await page.reload();
    await expect(page.getByRole("option")).toHaveCount(1);
    await expect(page.getByText(/Doomed/)).toHaveCount(0);
    await expect(page.getByRole("option").first()).toHaveText(/Keeper/);

    await context.close();
  });

  test("Del rollback-in-place: a server 500 reappears the row in position + announces the failure (FR-049)", async ({
    browser,
  }) => {
    const { page, context } = await signedInPage(browser, "us8-delete-rollback");
    await page.goto("/");
    await expect(page.getByText(/your inbox is empty/i)).toBeVisible();

    // Three rows; select the MIDDLE so "reappears in original position" tests position, not presence.
    await seedTasks(page, ["Bottom", "Middle", "Top"]); // render order top→bottom: Top, Middle, Bottom
    const options = page.getByRole("option");
    await expect(options.nth(0)).toHaveText(/Top/);
    await expect(options.nth(1)).toHaveText(/Middle/);
    await expect(options.nth(2)).toHaveText(/Bottom/);

    const listbox = page.getByRole("listbox", { name: "Tasks" });
    await listbox.focus();
    await page.keyboard.press("ArrowDown"); // select index 1 (Middle)
    await expect(options.nth(1)).toHaveAttribute("aria-selected", "true");

    // Intercept ONLY the DELETE on the proxy path and fulfil a 500 with a problem+json body. A
    // non-empty parseable body is REQUIRED: openapi-fetch only populates `error` when it can parse
    // a body, so an empty 500 would be read as success and skip the rollback/announcement entirely.
    // Every non-DELETE request must fall through or the GET list refetch would hang.
    await page.route("**/api/proxy/**", async (route) => {
      if (route.request().method() === "DELETE") {
        await route.fulfill({
          status: 500,
          contentType: "application/problem+json",
          body: JSON.stringify({
            type: "https://taskflow.example/errors/internal_error",
            title: "Internal Server Error",
            status: 500,
            errorCode: "internal_error",
          }),
        });
        return;
      }
      await route.fallback();
    });

    await page.keyboard.press("Delete");

    // Optimistic remove then rollback: the row reappears AT ITS ORIGINAL INDEX (still 3 rows, Middle in the middle).
    await expect(options).toHaveCount(3);
    await expect(options.nth(0)).toHaveText(/Top/);
    await expect(options.nth(1)).toHaveText(/Middle/);
    await expect(options.nth(2)).toHaveText(/Bottom/);

    // FR-049: the failure is announced through the shared polite LiveRegion (role=status). The
    // visual toast carries the same text but is aria-hidden, and layout.tsx mounts a second
    // (unfed) role=status — so filter the status nodes by the fallback message to avoid strict-mode
    // ambiguity. internal_error with no recognised code → the "Something went wrong" fallback.
    await expect(
      page.getByRole("status").filter({ hasText: /something went wrong/i }),
    ).toBeVisible();

    await page.unroute("**/api/proxy/**");
    await context.close();
  });

  test("Alt+↓ reorders the selected task down; the new order persists and the URL is unchanged", async ({
    browser,
  }) => {
    const { page, context } = await signedInPage(browser, "us8-reorder");
    await page.goto("/");
    await expect(page.getByText(/your inbox is empty/i)).toBeVisible();

    await seedTasks(page, ["Third", "Second", "First"]); // render order top→bottom: First, Second, Third
    const options = page.getByRole("option");
    await expect(options.nth(0)).toHaveText(/First/);

    const urlBefore = page.url();
    const listbox = page.getByRole("listbox", { name: "Tasks" });
    await listbox.focus();

    // Top row is selected (index 0). Alt+↓ moves it down one rank: First should drop below Second.
    const reorderSettled = page.waitForResponse(positionWrite);
    await page.keyboard.press("Alt+ArrowDown");
    await expect(options.nth(0)).toHaveText(/Second/);
    await expect(options.nth(1)).toHaveText(/First/);
    await reorderSettled;

    // Alt+Arrow is preventDefault'd, so it must not navigate/scroll the page URL.
    expect(page.url()).toBe(urlBefore);

    await page.reload();
    await expect(page.getByRole("option").nth(0)).toHaveText(/Second/);
    await expect(page.getByRole("option").nth(1)).toHaveText(/First/);

    await context.close();
  });

  test("virtualization-focus: the selected row stays mounted + addressable after a wheel scroll, and ↑/↓ still move", async ({
    browser,
  }) => {
    const { page, context } = await signedInPage(browser, "us8-virtualize");
    await page.goto("/");
    await expect(page.getByText(/your inbox is empty/i)).toBeVisible();

    // NOTE: there is NO DB task-seed helper in the e2e harness — the only seeding path is the UI
    // `createTask` capture flow (one optimistic PUT each). Seeding ~60 rows this way is SLOW but is
    // the only mechanism available without inventing an inserter against an unseen `tasks` schema.
    // The force-include-selected window guarantee (TaskList.rangeExtractor, research R10) also has
    // dedicated unit coverage; this e2e is the best-effort end-to-end proof.
    const titles = Array.from({ length: 60 }, (_, i) => `Bulk task ${String(i + 1).padStart(2, "0")}`);
    await seedTasks(page, titles);

    const listbox = page.getByRole("listbox", { name: "Tasks" });
    await listbox.focus();

    // The top row (index 0) is selected. Capture its option id from aria-activedescendant.
    const activeId = await listbox.getAttribute("aria-activedescendant");
    expect(activeId).toBeTruthy();

    // Scroll the listbox far enough that the selection leaves the normal rendered window. With
    // virtualization the selected option must STILL be in the DOM (force-include keeps it mounted
    // so aria-activedescendant never dangles). Use an attribute selector (not `#id`) to dodge any
    // CSS-escaping of the id.
    await listbox.hover();
    await page.mouse.wheel(0, 4000);
    const activeOption = page.locator(`[id="${activeId}"]`);
    await expect(activeOption).toBeAttached();
    // It remains the active descendant (the reference resolves to a present element).
    await expect(listbox).toHaveAttribute("aria-activedescendant", activeId!);

    // ↑/↓ still move selection after the wheel scroll.
    await page.keyboard.press("ArrowDown");
    const afterDown = await listbox.getAttribute("aria-activedescendant");
    expect(afterDown).not.toBe(activeId);
    await page.keyboard.press("ArrowUp");
    await expect(listbox).toHaveAttribute("aria-activedescendant", activeId!);

    await context.close();
  });
});

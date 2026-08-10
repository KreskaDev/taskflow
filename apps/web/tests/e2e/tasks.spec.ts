import { expect, test, type Page } from "@playwright/test";
import { ensureUser, insertSession } from "./helpers/seed";

/**
 * US1 Daily Task Capture E2E (T041; US-01.AS-01/06/07, AS-09 precursor, EC-01). The real
 * end-to-end MVP proof: a fresh authenticated user drives the INLINE quick-add capture
 * (slice 019 removed the single-key shortcut system — FR-111; the old `C` dialog is gone,
 * FR-107 replaced it with an always-present input at the top of the list) through the REAL
 * BFF→proxy→API path against a migrated Postgres + .NET API (booted by global-setup) — no
 * API mocking. Auth is a seeded session (mirroring auth.spec.ts), so each test gets a
 * pristine, isolated account and never reinvents the OAuth dance.
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

/** The confirmed-empty Inbox hint (EmptyState) — the "query resolved with zero rows" sync point. */
function emptyHint(page: Page) {
  return page.getByText("Twój Inbox jest pusty.");
}

/**
 * Fills the INLINE quick-add capture ("Task title", FR-107), presses Enter, and waits for the
 * optimistic create's REAL server write (the idempotent PUT) to land. The `waitForResponse`
 * promise is ARMED before Enter so it can never miss a fast-resolving PUT (the matcher's `PUT`
 * method discriminates it from the `GET /api/tasks` refetch).
 */
async function createTask(page: Page, title: string): Promise<void> {
  const input = page.getByLabel("Task title");
  await input.fill(title);
  const settled = page.waitForResponse(
    (r) => r.request().method() === "PUT" && /\/api\/tasks\//.test(r.url()) && r.ok(),
  );
  await input.press("Enter");
  await settled;
}

test.describe("US1 Daily Task Capture (AS-01/06/07/09, EC-01)", () => {
  test("EC-01: a fresh user sees the accessible empty-Inbox hint and zero options [INV-030]", async ({
    browser,
  }) => {
    const { page, context } = await signedInPage(browser, "tasks-empty");
    await page.goto("/");

    // The empty-Inbox hint asserts the query RESOLVED with zero rows (page.tsx swaps the
    // listbox out for the EmptyState only once `isEmpty` is true). Asserting it first waits
    // out the transient loading state where the listbox flashes with 0 options. The empty
    // state carries a hint + a real action (FR-110) — no shortcut copy.
    await expect(emptyHint(page)).toBeVisible();
    await expect(page.getByRole("button", { name: "Dodaj pierwszy task" })).toBeVisible();
    await expect(page.getByRole("option")).toHaveCount(0);

    await context.close();
  });

  test("AS-01: the inline capture is ready on the Inbox and 'Nowy task' opens the global capture with the title input focused [INV-020]", async ({
    browser,
  }) => {
    const { page, context } = await signedInPage(browser, "tasks-focus");
    await page.goto("/");
    await expect(emptyHint(page)).toBeVisible();

    // The INLINE quick-add (FR-107) is the always-present Inbox capture surface — visible
    // and focusable without any shortcut.
    const inline = page.getByLabel("Task title");
    await expect(inline).toBeVisible();
    await inline.focus();
    await expect(inline).toBeFocused();

    // The topbar's global "Nowy task" opens the modal capture host with its own title input.
    // SC-003 / AS-01: the single title input receives initial focus synchronously.
    await page.getByRole("button", { name: "Nowy task" }).click();
    const dialog = page.getByRole("dialog", { name: "Nowy task" });
    await expect(dialog).toBeVisible();
    await expect(dialog.getByRole("textbox", { name: "Task title" })).toBeFocused();

    await context.close();
  });

  test("AS-06: Enter creates, the row paints at the top newest-first, and persists across reload [INV-021]", async ({
    browser,
  }) => {
    const { page, context } = await signedInPage(browser, "tasks-create");
    await page.goto("/");
    await expect(emptyHint(page)).toBeVisible();

    // First task. The inline capture clears for the next entry; the optimistic row paints.
    await createTask(page, "First task");
    await expect(page.getByLabel("Task title")).toHaveValue("");
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

  test("AS-07: Esc cancels the global capture — no task is created and focus returns to the invoker [INV-022]", async ({
    browser,
  }) => {
    const { page, context } = await signedInPage(browser, "tasks-cancel");
    await page.goto("/");
    await expect(emptyHint(page)).toBeVisible();

    // One committed task so the count-unchanged assertion is meaningful.
    await createTask(page, "Keeper");
    await expect(page.getByRole("option")).toHaveCount(1);

    // The topbar "Nowy task" button is the deterministic invoker (the Dialog focus contract's
    // return target). Esc inside the capture input cancels without creating (FR-030).
    const invoker = page.getByRole("button", { name: "Nowy task" });
    await invoker.click();
    const dialog = page.getByRole("dialog", { name: "Nowy task" });
    await expect(dialog).toBeVisible();
    await dialog.getByRole("textbox", { name: "Task title" }).fill("Discarded draft");
    await page.keyboard.press("Escape");

    // No task created (count unchanged) and focus restored to the invoking button.
    await expect(page.getByRole("dialog", { name: "Nowy task" })).toBeHidden();
    await expect(page.getByRole("option")).toHaveCount(1);
    await expect(invoker).toBeFocused();

    await context.close();
  });

  test("AS-09 precursor: typing C inside the capture input inserts the character (no capture hijack) [INV-023]", async ({
    browser,
  }) => {
    const { page, context } = await signedInPage(browser, "tasks-suppress");
    await page.goto("/");
    await expect(emptyHint(page)).toBeVisible();

    const input = page.getByLabel("Task title");
    await input.click();
    await expect(input).toBeFocused();

    // With the shortcut system removed (FR-111), a typed `C` is only ever a character: it
    // lands in the field and no dialog of any kind spawns.
    await page.keyboard.type("Cabbage");
    await expect(input).toHaveValue("Cabbage");
    await expect(page.getByRole("dialog")).toHaveCount(0);

    await context.close();
  });
});

/**
 * US1 Natural-Language Dates capture E2E (slice 003, T019; US-01.AS-02..AS-05 + EC-02 + the
 * "version number is not a date" guard). Drives the SAME inline quick-add capture as the specs
 * above through the REAL BFF→proxy→API write path on a seeded session — the slice-003 delta is
 * purely client-side parsing (`lib/dates.ts`) feeding the create payload, so these prove the
 * end-to-end behaviour: a trailing Polish date phrase is stripped from the title and paints a
 * due-date label on the row; an impossible date attempt ("30.02") creates nothing and announces
 * "nie rozpoznano"; a non-date trailing token ("2.0") is left in the title with no error.
 *
 * CLOCK NOTE: the parser runs against the REAL system clock (no `now` injection on the live
 * Enter path — only the Vitest unit suite injects `now`). So these assertions are robust to
 * wall-clock time: they assert the TITLE is correctly stripped and that a due-date label is
 * VISIBLE for the resolved cases, but never assert an exact instant/time-of-day (the unit tests
 * own exact instants). The title is asserted via the row's title BUTTON matched by its EXACT
 * accessible name rather than the whole row, because the row also contains the date label — a
 * whole-row substring match would wrongly pass even if "po 17" leaked into the title.
 */
test.describe("US1 Natural-Language Dates (AS-02..05 capture-with-date, EC-02, version guard)", () => {
  /** The top (newest-first) row. */
  const topRow = (page: Page) => page.getByRole("option").first();
  /**
   * The top row's title BUTTON (the drawer trigger) matched by its EXACT accessible name —
   * the strip-correctness probe: a leaked date phrase like "Kupic mleko po 17" fails the
   * exact match, so this only resolves when the title equals the stripped prefix.
   */
  const topTitle = (page: Page, title: string) =>
    topRow(page).getByRole("button", { name: title, exact: true });
  /** The visible due-date label text on the top row (FR-046 visible, non-hover affordance). */
  const topDue = (page: Page) => topRow(page).getByText(/\d{2}\.\d{2}\.\d{4}/);

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
    test(`${scenario}: "${input}" → title "${title}" stripped + due-date label visible [INV-024]`, async ({
      browser,
    }) => {
      const { page, context } = await signedInPage(
        browser,
        `dates-${title.toLowerCase().replace(/\s+/g, "-")}`,
      );
      await page.goto("/");
      await expect(emptyHint(page)).toBeVisible();

      await createTask(page, input);

      // Exactly one row, and its TITLE is the stripped prefix (exact accessible-name match, so
      // a leaked date phrase like "Kupic mleko po 17" would fail). This is the strip proof.
      await expect(page.getByRole("option")).toHaveCount(1);
      await expect(topTitle(page, title)).toBeVisible();

      // The resolved due date paints a visible label on the row (the end-to-end point — we do NOT
      // assert the exact instant; the unit suite owns that against an injected clock).
      await expect(topDue(page)).toBeVisible();

      await context.close();
    });
  }

  test('EC-02: "Spotkanie 30.02" creates NO task and announces "nie rozpoznano"; field retains value [INV-025]', async ({
    browser,
  }) => {
    const { page, context } = await signedInPage(browser, "dates-ec02-impossible");
    await page.goto("/");
    await expect(emptyHint(page)).toBeVisible();

    const input = page.getByLabel("Task title");
    await input.click();
    await expect(input).toBeFocused();
    await input.fill("Spotkanie 30.02");

    // An impossible in-range date ("30.02") is a genuine trailing date ATTEMPT that fails to
    // resolve → NO mutation fires (so there is no PUT to wait on — waiting would hang). Enter
    // surfaces the recoverable failure synchronously via the capture's persistent polite status
    // node; that visibility is the synchronization point. Target the node by its stable id to
    // dodge the other role=status nodes (strict-mode ambiguity).
    await page.keyboard.press("Enter");

    const errorNode = page.locator("#inbox-capture-error");
    await expect(errorNode).toBeVisible();
    await expect(errorNode).toHaveText(/nie rozpoznano/i);

    // No task was created (the inbox stays empty), and the inline capture retains the field's
    // value so the user can fix the phrase (EC-02 / FR-006).
    await expect(page.getByRole("option")).toHaveCount(0);
    await expect(emptyHint(page)).toBeVisible();
    await expect(input).toHaveValue("Spotkanie 30.02");

    await context.close();
  });

  test('guard: "Wersja 2.0" is created as-is with NO due-date label and NO error [INV-026]', async ({
    browser,
  }) => {
    const { page, context } = await signedInPage(browser, "dates-guard-version");
    await page.goto("/");
    await expect(emptyHint(page)).toBeVisible();

    // "2.0" is NOT a date-shaped trailing token (out of clock/calendar range, R4) → the whole
    // string is the title, no due date, no error. `createTask` proves a real server write landed.
    await createTask(page, "Wersja 2.0");

    await expect(page.getByRole("option")).toHaveCount(1);
    await expect(topTitle(page, "Wersja 2.0")).toBeVisible();
    // No due-date label rendered (no "termin:" qualifier on the row), and the capture raised no
    // recoverable-failure message — its persistent status node stays EMPTY (it is always
    // mounted, fed "" when errorless).
    await expect(topRow(page).getByText(/termin:/)).toHaveCount(0);
    await expect(page.locator("#inbox-capture-error")).toHaveText("");

    await context.close();
  });
});

/**
 * US8 Keyboard Navigation & Row Operations E2E (T059; US-08.AS-03/09 + the Space toggle +
 * rename/delete/reorder via the row affordances + virtualization-focus). Slice 019 removed the
 * document-level shortcut system (FR-111): keyboard operability now lives INSIDE the listbox
 * composite widget (↑/↓/Home/End/Space/Enter on the FOCUSED listbox — D5), and every other
 * operation is a visible row affordance (the "Edytuj" quick action + the complete "⋯" menu,
 * FR-108). Drives the REAL listbox surface through the same seeded-session auth and the REAL
 * BFF→proxy→API write path as the US1 specs. Reuses {@link signedInPage} (fresh isolated account
 * per test, keyed by a unique email/sub) and {@link createTask} (arms the optimistic PUT's
 * waitForResponse before Enter). Every mutating operation arms its OWN waitForResponse
 * (discriminated by HTTP method so the onSettled GET refetch is never mistaken for the write)
 * before any `reload()` so the persistence assertions never race the server.
 */
test.describe("US8 Keyboard Nav & Row Operations (AS-03/09, Space toggle, rename/delete/reorder, virtualization)", () => {
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

  test("AS-03: ↑/↓ move the selection (aria-selected + listbox aria-activedescendant track it) [INV-032]", async ({
    browser,
  }) => {
    const { page, context } = await signedInPage(browser, "us8-nav");
    await page.goto("/");
    await expect(emptyHint(page)).toBeVisible();

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

    // ArrowDown → selection moves to the second row; both signals follow it. The arrow keys are
    // container-level handlers on the composite widget, so the listbox must be FOCUSED first.
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

  test("AS-09: single-key shortcuts are suppressed while a text input is focused [INV-124]", async ({
    browser,
  }) => {
    const { page, context } = await signedInPage(browser, "us8-suppress");
    await page.goto("/");
    await expect(emptyHint(page)).toBeVisible();

    // Focus the inline capture input. With the shortcut system removed (FR-111) there are no
    // document-level single-key listeners left: C/E/Space typed into a focused text input land
    // as literal characters — none may be interpreted as a command.
    const input = page.getByLabel("Task title");
    await input.click();
    await expect(input).toBeFocused();

    await page.keyboard.type("Ceb");
    await expect(input).toHaveValue("Ceb");
    // No capture dialog spawned, and no shortcuts-help overlay exists to open.
    await expect(page.getByRole("dialog")).toHaveCount(0);
    await expect(page.getByRole("dialog", { name: "Keyboard shortcuts" })).toHaveCount(0);

    await context.close();
  });

  test("Space toggles the selected task done↔backlog and the done state persists across reload [INV-033]", async ({
    browser,
  }) => {
    const { page, context } = await signedInPage(browser, "us8-toggle");
    await page.goto("/");
    await expect(emptyHint(page)).toBeVisible();

    await createTask(page, "Toggle me");
    const row = page.getByRole("option").first();
    await expect(row).toHaveAttribute("data-status", "backlog");

    // Space is a composite-widget key: it toggles the SELECTED row and must be sent to the
    // FOCUSED listbox (container-level handler, not document-level).
    const listbox = page.getByRole("listbox", { name: "Tasks" });
    await listbox.focus();

    // Space → done (assert the data-status hook the styling reads).
    const doneWrite = page.waitForResponse(statusWrite);
    await page.keyboard.press(" ");
    await expect(row).toHaveAttribute("data-status", "done");
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

  test("the row's Edytuj action renames inline (Enter commits + persists); Esc keeps the original [INV-034]", async ({
    browser,
  }) => {
    const { page, context } = await signedInPage(browser, "us8-rename");
    await page.goto("/");
    await expect(emptyHint(page)).toBeVisible();

    await createTask(page, "Original title");
    const row = page.getByRole("option").first();

    // The row's "Edytuj" quick action → inline rename input, autofocused and seeded with the
    // current title (the old `E` shortcut's affordance replacement, FR-108).
    await row.getByRole("button", { name: "Edytuj „Original title”" }).click();
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

    // Esc path: re-open via the (renamed) Edytuj button, Esc, and the committed title stays
    // intact (no write).
    await page
      .getByRole("option")
      .first()
      .getByRole("button", { name: "Edytuj „Renamed title”" })
      .click();
    const reopened = page.getByRole("textbox", { name: "Rename task" });
    await expect(reopened).toBeFocused();
    await reopened.fill("Discarded edit");
    await page.keyboard.press("Escape");
    await expect(page.getByRole("textbox", { name: "Rename task" })).toHaveCount(0);
    await expect(page.getByRole("option").first()).toHaveText(/Renamed title/);

    await context.close();
  });

  test("the row menu's Usuń soft-deletes the task and it stays gone across reload [INV-036]", async ({
    browser,
  }) => {
    const { page, context } = await signedInPage(browser, "us8-delete");
    await page.goto("/");
    await expect(emptyHint(page)).toBeVisible();

    // Two rows so the listbox survives the delete; delete the top one.
    await seedTasks(page, ["Keeper", "Doomed"]); // render order top→bottom: Doomed, Keeper
    const top = page.getByRole("option").first();
    await expect(top).toHaveText(/Doomed/);

    // "Usuń" in the row's "⋯" menu is the delete affordance — IMMEDIATE, no confirm (the same
    // contract as the old Delete key).
    await top.getByRole("button", { name: "Więcej akcji: Doomed" }).click();
    const deleteSettled = page.waitForResponse(deleteWrite);
    await page.getByRole("menuitem", { name: "Usuń" }).click();
    await expect(page.getByRole("option")).toHaveCount(1);
    await expect(page.getByText(/Doomed/)).toHaveCount(0);
    await deleteSettled;

    await page.reload();
    await expect(page.getByRole("option")).toHaveCount(1);
    await expect(page.getByText(/Doomed/)).toHaveCount(0);
    await expect(page.getByRole("option").first()).toHaveText(/Keeper/);

    await context.close();
  });

  test("Usuń rollback-in-place: a server 500 reappears the row in position + announces the failure (FR-049) [INV-037]", async ({
    browser,
  }) => {
    const { page, context } = await signedInPage(browser, "us8-delete-rollback");
    await page.goto("/");
    await expect(emptyHint(page)).toBeVisible();

    // Three rows; delete the MIDDLE so "reappears in original position" tests position, not presence.
    await seedTasks(page, ["Bottom", "Middle", "Top"]); // render order top→bottom: Top, Middle, Bottom
    const options = page.getByRole("option");
    await expect(options.nth(0)).toHaveText(/Top/);
    await expect(options.nth(1)).toHaveText(/Middle/);
    await expect(options.nth(2)).toHaveText(/Bottom/);

    // Selection still tracks arrow-nav on the focused listbox (kept from the pre-019 spec —
    // the delete itself is row-scoped via the menu, independent of selection).
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

    await options.nth(1).getByRole("button", { name: "Więcej akcji: Middle" }).click();
    await page.getByRole("menuitem", { name: "Usuń" }).click();

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

  test("the row menu's Przenieś niżej reorders the task down; the new order persists and the URL is unchanged [INV-038]", async ({
    browser,
  }) => {
    const { page, context } = await signedInPage(browser, "us8-reorder");
    await page.goto("/");
    await expect(emptyHint(page)).toBeVisible();

    await seedTasks(page, ["Third", "Second", "First"]); // render order top→bottom: First, Second, Third
    const options = page.getByRole("option");
    await expect(options.nth(0)).toHaveText(/First/);

    const urlBefore = page.url();

    // "Przenieś niżej" in the top row's "⋯" menu is the keyboard-reachable reorder (S3.7) —
    // the same optimistic PATCH /position write the old Alt+↓ performed. The order assertions
    // run against the OPTIMISTIC swap (armed before, awaited after).
    await options.nth(0).getByRole("button", { name: "Więcej akcji: First" }).click();
    const reorderSettled = page.waitForResponse(positionWrite);
    await page.getByRole("menuitem", { name: "Przenieś niżej" }).click();
    await expect(options.nth(0)).toHaveText(/Second/);
    await expect(options.nth(1)).toHaveText(/First/);
    await reorderSettled;

    // A menu action must not navigate/scroll the page URL.
    expect(page.url()).toBe(urlBefore);

    await page.reload();
    await expect(page.getByRole("option").nth(0)).toHaveText(/Second/);
    await expect(page.getByRole("option").nth(1)).toHaveText(/First/);

    await context.close();
  });

  test("virtualization-focus: the selected row stays mounted + addressable after a wheel scroll, and ↑/↓ still move [INV-041] [INV-136]", async ({
    browser,
  }) => {
    const { page, context } = await signedInPage(browser, "us8-virtualize");
    await page.goto("/");
    await expect(emptyHint(page)).toBeVisible();

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

    // ↑/↓ still move selection after the wheel scroll (keys on the focused listbox).
    await page.keyboard.press("ArrowDown");
    const afterDown = await listbox.getAttribute("aria-activedescendant");
    expect(afterDown).not.toBe(activeId);
    await page.keyboard.press("ArrowUp");
    await expect(listbox).toHaveAttribute("aria-activedescendant", activeId!);

    await context.close();
  });
});

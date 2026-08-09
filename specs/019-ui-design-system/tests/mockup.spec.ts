import { test, expect, type Page } from "@playwright/test";
import { pathToFileURL } from "node:url";
import path from "node:path";

const MOCKUP_URL = pathToFileURL(
  path.resolve(__dirname, "..", "mockup-inbox.html"),
).href;

// Tokeny z bloga (ADR-039/041) — oczekiwane wartości per paleta.
const ACCENT = {
  "dark-cool": "rgb(82, 144, 189)", // #5290BD
  "dark-warm": "rgb(201, 126, 135)", // #C97E87
  "light-cool": "rgb(58, 113, 148)", // #3A7194
  "light-warm": "rgb(157, 71, 84)", // #9D4754
} as const;

const BG_BASE = {
  "dark-cool": "rgb(24, 24, 24)", // #181818
  "dark-warm": "rgb(26, 24, 22)", // #1A1816
  "light-cool": "rgb(250, 250, 250)", // #FAFAFA
  "light-warm": "rgb(250, 247, 242)", // #FAF7F2
} as const;

test.beforeEach(async ({ page }) => {
  await page.goto(MOCKUP_URL);
});

test.describe("chrome aplikacji (F1/F3)", () => {
  test("globalny przycisk „+ Nowy task” istnieje i jest widoczny", async ({ page }) => {
    await expect(page.getByRole("button", { name: "Nowy task" })).toBeVisible();
  });

  test("inline quick-add istnieje w liście (F1: oba sposoby dodawania)", async ({ page }) => {
    await expect(page.getByRole("textbox", { name: "Dodaj task" })).toBeVisible();
  });

  test("wyszukiwarka jest dostępna z topbaru", async ({ page }) => {
    await expect(page.getByRole("textbox", { name: "Szukaj" })).toBeVisible();
  });

  test("sidebar: wszystkie widoki z licznikami + projekty (F3)", async ({ page }) => {
    const nav = page.getByRole("navigation", { name: "Nawigacja główna" });
    for (const view of ["Inbox", "Today", "Upcoming", "Assigned"]) {
      await expect(nav.getByRole("button", { name: new RegExp(`^${view}`) })).toBeVisible();
    }
    // Liczniki (np. "Inbox 12") — treść przycisku zawiera liczbę.
    await expect(nav.getByRole("button", { name: /Inbox\s+12/ })).toBeVisible();
    for (const proj of ["Website Redesign", "Launch Q3", "Backlog produktu"]) {
      await expect(nav.getByRole("button", { name: new RegExp(proj) })).toBeVisible();
    }
  });

  test("collapse sidebaru: aria-expanded odzwierciedla stan, label się zmienia", async ({ page }) => {
    const btn = page.getByRole("button", { name: "Zwiń sidebar" });
    await expect(btn).toHaveAttribute("aria-expanded", "true");
    await btn.click();
    await expect(page.getByRole("button", { name: "Rozwiń sidebar" })).toHaveAttribute(
      "aria-expanded",
      "false",
    );
    await expect(page.getByRole("navigation", { name: "Nawigacja główna" })).toBeHidden();
    await page.getByRole("button", { name: "Rozwiń sidebar" }).click();
    await expect(page.getByRole("navigation", { name: "Nawigacja główna" })).toBeVisible();
  });
});

test.describe("system 4 palet (B1/B2)", () => {
  test("default to dark-cool", async ({ page }) => {
    await expect(page.locator("html")).toHaveClass("dark-cool");
  });

  for (const [mode, palette, cls] of [
    ["light", "cool", "light-cool"],
    ["light", "warm", "light-warm"],
    ["dark", "warm", "dark-warm"],
  ] as const) {
    test(`przełączenie na ${cls}: klasa, aria-pressed i tokeny`, async ({ page }) => {
      if (mode === "light") await page.getByRole("button", { name: "Tryb jasny" }).click();
      if (palette === "warm")
        await page.getByRole("button", { name: "Paleta bordowa (warm)" }).click();
      await expect(page.locator("html")).toHaveClass(cls);

      const modeBtn = page.getByRole("button", {
        name: mode === "light" ? "Tryb jasny" : "Tryb ciemny",
      });
      await expect(modeBtn).toHaveAttribute("aria-pressed", "true");

      // Tokeny naprawdę się przełączają: tło listy i kolor akcentu (@mention).
      const listBg = await page
        .locator(".main")
        .evaluate((el) => getComputedStyle(el).backgroundColor);
      expect(listBg).toBe(BG_BASE[cls]);
      const mentionColor = await page
        .locator(".mention")
        .first()
        .evaluate((el) => getComputedStyle(el).color);
      expect(mentionColor).toBe(ACCENT[cls]);
    });
  }
});

test.describe("wiersze tasków i akcje (F2)", () => {
  test("każdy wiersz ma checkbox, tytuł-przycisk i pasek akcji z „⋯”", async ({ page }) => {
    const rows = page.locator(".row");
    await expect(rows).toHaveCount(7);
    for (let i = 0; i < 7; i++) {
      const row = rows.nth(i);
      await expect(row.locator(".chk")).toHaveCount(1);
      await expect(row.locator("button.row-title")).toHaveCount(1);
      await expect(
        row.getByRole("button", { name: "Więcej akcji" }),
      ).toHaveCount(1);
    }
  });

  test("pasek akcji ujawnia się przy fokusie klawiaturowym (nie tylko hover)", async ({ page }) => {
    const firstRow = page.locator(".row").first();
    const actions = firstRow.locator(".row-actions");
    expect(await actions.evaluate((el) => getComputedStyle(el).opacity)).toBe("0");
    await firstRow.locator(".chk").focus();
    await expect
      .poll(() => actions.evaluate((el) => getComputedStyle(el).opacity))
      .toBe("1");
  });

  test("etykiety (chips) renderują się z kropką w kolorze semantycznym", async ({ page }) => {
    const expected: Record<string, string> = {
      bug: "rgb(201, 126, 135)", // --danger  #C97E87
      design: "rgb(82, 144, 189)", // --accent  #5290BD
      docs: "rgb(201, 184, 80)", // --warning #C9B850
      perf: "rgb(123, 168, 135)", // --success #7BA887
    };
    for (const [label, color] of Object.entries(expected)) {
      const chip = page.locator(".chip", { hasText: label });
      await expect(chip).toBeVisible();
      const dotColor = await chip
        .locator(".dot")
        .evaluate((el) => getComputedStyle(el).backgroundColor);
      expect(dotColor, `kropka etykiety "${label}"`).toBe(color);
    }
  });

  test("task zrobiony ma przekreślony tytuł i zielony znacznik", async ({ page }) => {
    const done = page.locator(".row", { hasText: "Wysłać zaproszenia" });
    await expect(done.locator(".done-text")).toHaveCSS("text-decoration-line", "line-through");
    const chkBg = await done
      .locator(".chk.done")
      .evaluate((el) => getComputedStyle(el, "::after").backgroundColor);
    expect(chkBg).toBe("rgb(123, 168, 135)"); // --success #7BA887
  });

  test("zaległy termin jest oznaczony kolorem danger", async ({ page }) => {
    await expect(page.locator(".due.overdue").first()).toHaveCSS(
      "color",
      "rgb(201, 126, 135)",
    );
  });
});

test.describe("drawer szczegółów (E2)", () => {
  test("komentarze renderują się poprawnie: 2 sztuki, autor, treść, @mention w akcencie", async ({ page }) => {
    const comments = page.locator(".comment");
    await expect(comments).toHaveCount(2);
    await expect(comments.nth(0)).toContainText("Ada Sowińska");
    await expect(comments.nth(0)).toContainText("pamiętaj o walidacji kontrastu");
    await expect(comments.nth(1)).toContainText("Kreska");
    const mention = comments.nth(0).locator(".mention");
    await expect(mention).toHaveText("@Kreska");
    await expect(mention).toHaveCSS("color", ACCENT["dark-cool"]);
    await expect(page.locator(".composer")).toContainText("Napisz komentarz");
  });

  test("pola edycji: Status / Priorytet / Termin / Etykiety / Przypisani", async ({ page }) => {
    const fields = page.locator(".fields");
    for (const label of ["Status", "Priorytet", "Termin", "Etykiety", "Przypisani"]) {
      await expect(fields.getByText(label, { exact: true })).toBeVisible();
    }
    await expect(fields.getByRole("button", { name: "+ dodaj" })).toBeVisible();
    await expect(fields.getByRole("button", { name: "+ przypisz" })).toBeVisible();
  });

  test("identyfikator taska jest monospace", async ({ page }) => {
    const ff = await page
      .locator(".task-id")
      .evaluate((el) => getComputedStyle(el).fontFamily);
    expect(ff).toContain("JetBrains Mono");
  });

  test("zamknięcie X → otwarcie kliknięciem tytułu; Esc zamyka i oddaje fokus", async ({ page }) => {
    const drawer = page.getByRole("complementary", { name: "Szczegóły taska" });
    await expect(drawer).toBeVisible();
    await page.getByRole("button", { name: "Zamknij panel" }).click();
    await expect(drawer).toBeHidden();

    const title = page.locator("button.row-title").first();
    await title.click();
    await expect(drawer).toBeVisible();

    await page.keyboard.press("Escape");
    await expect(drawer).toBeHidden();
    await expect(title).toBeFocused(); // powrót fokusa do wywołującego
  });
});

async function openFirstRowMenu(page: Page) {
  const trigger = page
    .locator(".row")
    .first()
    .getByRole("button", { name: "Więcej akcji" });
  await trigger.click();
  return trigger;
}

test.describe("menu kontekstowe „⋯”", () => {
  test("otwiera się z kompletem akcji, aria-expanded, fokus na pierwszej pozycji", async ({ page }) => {
    const trigger = await openFirstRowMenu(page);
    const menu = page.getByRole("menu", { name: "Akcje taska" });
    await expect(menu).toBeVisible();
    await expect(trigger).toHaveAttribute("aria-expanded", "true");
    await expect(menu.getByRole("menuitem")).toHaveCount(9);
    for (const item of ["Oznacz jako zrobione", "Edytuj", "Przenieś do projektu…", "Duplikuj", "Usuń"]) {
      await expect(menu.getByRole("menuitem", { name: item })).toBeVisible();
    }
    await expect(menu.getByRole("menuitem").first()).toBeFocused();
  });

  test("nawigacja strzałkami działa, Esc zamyka i oddaje fokus triggerowi", async ({ page }) => {
    const trigger = await openFirstRowMenu(page);
    const menu = page.getByRole("menu", { name: "Akcje taska" });
    await page.keyboard.press("ArrowDown");
    await expect(menu.getByRole("menuitem", { name: "Edytuj" })).toBeFocused();
    await page.keyboard.press("ArrowUp");
    await expect(menu.getByRole("menuitem", { name: "Oznacz jako zrobione" })).toBeFocused();
    await page.keyboard.press("Escape");
    await expect(menu).toBeHidden();
    await expect(trigger).toBeFocused();
    await expect(trigger).toHaveAttribute("aria-expanded", "false");
  });

  test("wybranie akcji zamyka menu", async ({ page }) => {
    await openFirstRowMenu(page);
    const menu = page.getByRole("menu", { name: "Akcje taska" });
    await menu.getByRole("menuitem", { name: "Edytuj" }).click();
    await expect(menu).toBeHidden();
  });
});

test.describe("modal usuwania + toast z undo (Konstytucja VII)", () => {
  test("pełny przepływ: menu → Usuń → modal → potwierdzenie → toast → Cofnij", async ({ page }) => {
    await openFirstRowMenu(page);
    await page.getByRole("menuitem", { name: "Usuń" }).click();

    const dialog = page.locator("dialog#confirm-dialog");
    await expect(dialog).toBeVisible();
    await expect(dialog).toContainText("Możesz cofnąć tę operację przez 30 sekund");

    // Anuluj zamyka bez skutków.
    await dialog.getByRole("button", { name: "Anuluj" }).click();
    await expect(dialog).toBeHidden();
    await expect(page.locator("#toast")).toBeHidden();

    // Potwierdzenie pokazuje toast (role=status) z przyciskiem Cofnij.
    await openFirstRowMenu(page);
    await page.getByRole("menuitem", { name: "Usuń" }).click();
    await dialog.getByRole("button", { name: "Usuń" }).click();
    await expect(dialog).toBeHidden();

    const toast = page.locator("#toast");
    await expect(toast).toBeVisible();
    await expect(toast).toHaveAttribute("role", "status");
    await expect(toast).toContainText("Task usunięty.");
    await toast.getByRole("button", { name: "Cofnij" }).click();
    await expect(toast).toBeHidden();
  });
});

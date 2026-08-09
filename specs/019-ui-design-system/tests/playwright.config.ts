import { defineConfig } from "@playwright/test";

// Samodzielny suite dla statycznego mockupu (file://) — celowo poza konfigiem
// E2E aplikacji (apps/web/playwright.config.ts), który bootuje PG+API+BFF.
// Uruchamianie: cd apps/web && npx playwright test -c ../../specs/019-ui-design-system/tests
export default defineConfig({
  testDir: ".",
  timeout: 15_000,
  use: { browserName: "chromium" },
  reporter: [["list"]],
});

import { defineConfig, devices } from "@playwright/test";

/**
 * E2E test environment (throwaway, test-only values — NOT secrets). Set here so the values are
 * present in the Playwright runner, in globalSetup (which boots Postgres + the .NET API), AND in
 * every worker process that runs a spec (the config is evaluated in each worker). `??=` never
 * clobbers a real local `.env` the developer may already have exported.
 *
 * Ports are deliberately non-default (Postgres on 55432) so the harness never collides with a
 * developer's local Postgres / API instance.
 */
const E2E_ENV: Record<string, string> = {
  // Postgres (shared by the .NET API and the BFF session store).
  ConnectionStrings__postgres:
    "Host=localhost;Port=55432;Database=taskflow;Username=taskflow;Password=taskflow_e2e",
  DATABASE_URL: "postgres://taskflow:taskflow_e2e@localhost:55432/taskflow",
  // Shared HMAC key: BFF mints the carrier (JWT_SIGNING_KEY), API validates it (config key
  // `Jwt:SigningKey` ← env `Jwt__SigningKey`). The two env names MUST carry the same value.
  JWT_SIGNING_KEY: "e2e-test-jwt-signing-key-0123456789abcdef0123456789abcdef",
  Jwt__SigningKey: "e2e-test-jwt-signing-key-0123456789abcdef0123456789abcdef",
  SESSION_SECRET: "e2e-test-session-secret-0123456789abcdef0123456789abcdef",
  API_INTERNAL_URL: "http://localhost:4311",
  APP_URL: "http://localhost:3000",
  // Admission must be configured or the BFF fails fast at startup (FR-087). The seeded-session
  // milestone admits this address; the fake-IdP milestone reuses it for the admitted-sign-in case.
  ADMISSION_EMAILS: "admitted@taskflow.test,delete-roundtrip@taskflow.test",
  ASPNETCORE_URLS: "http://localhost:4311",
  ASPNETCORE_ENVIRONMENT: "Development",
  // Fake-IdP wiring for the AS-01 sign-in milestone (oauth.ts honours these overrides). The IdP
  // process is started by global-setup on port 4321 with a runtime-generated RS256 key.
  GOOGLE_CLIENT_ID: "e2e-fake-client-id",
  GOOGLE_CLIENT_SECRET: "e2e-fake-client-secret",
  GOOGLE_ISSUER: "http://localhost:4321",
  GOOGLE_AUTH_URI: "http://localhost:4321/auth",
  GOOGLE_TOKEN_URI: "http://localhost:4321/token",
  GOOGLE_JWKS_URI: "http://localhost:4321/jwks",
};
for (const [key, value] of Object.entries(E2E_ENV)) {
  process.env[key] ??= value;
}

export default defineConfig({
  testDir: "./tests/e2e",
  // Next.js dev-mode compiles routes on first hit; allow generous per-test time.
  timeout: 60_000,
  // The harness boots a single shared Postgres + API + BFF; run specs serially against it.
  fullyParallel: false,
  workers: 1,
  forbidOnly: !!process.env.CI,
  retries: process.env.CI ? 1 : 0,
  globalSetup: "./tests/e2e/global-setup.ts",
  globalTeardown: "./tests/e2e/global-teardown.ts",
  use: {
    baseURL: process.env.APP_URL ?? "http://localhost:3000",
    trace: "on-first-retry",
  },
  projects: [{ name: "chromium", use: { ...devices["Desktop Chrome"] } }],
});

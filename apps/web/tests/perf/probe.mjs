// T069 (slice 019) — performance probe against a PRODUCTION build (SC-002/003, UIT-110/111).
// Boots the same stack shape as tests/e2e/global-setup.ts (disposable PG :55432, built Debug
// API :4311, Next BFF :3000) but with `next start` over a prior `next build`, seeds a session
// like tests/e2e/helpers/seed.ts, measures, and tears everything down.
//
// Prereqs:  dotnet build apps/api/src/TaskFlow.Api -c Debug
//           pnpm --dir apps/web build          (with the same env this script exports)
// Run:      node apps/web/tests/perf/probe.mjs
//
// Metrics (median of 5 fresh browser contexts):
//   FCP  — first-contentful-paint entry (budget <1000 ms, SC-002)
//   TTI  — proxy: max(FCP, DOMContentLoaded end, end of last long task) read after
//          network-idle + settle (budget <2500 ms, SC-002)
//   Optimistic paint — Enter in the Inbox quick-add; MutationObserver + first-rAF check
//          that the new row is in the DOM within one frame of the keystroke (<16 ms, SC-003)
import { randomUUID } from "node:crypto";
import { execFileSync, spawn } from "node:child_process";
import { existsSync, openSync } from "node:fs";
import { dirname, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import { chromium } from "@playwright/test";
import { SignJWT } from "jose";
import { Client } from "pg";

const HERE = dirname(fileURLToPath(import.meta.url)); // apps/web/tests/perf
const WEB_DIR = resolve(HERE, "../..");
const REPO_ROOT = resolve(WEB_DIR, "../..");
const API_DLL = resolve(REPO_ROOT, "apps/api/src/TaskFlow.Api/bin/Debug/net9.0/TaskFlow.Api.dll");
const NEXT_BIN = resolve(WEB_DIR, "node_modules/next/dist/bin/next");
const PG_CONTAINER = "taskflow-e2e-pg";

const ENV = {
  ConnectionStrings__postgres:
    "Host=localhost;Port=55432;Database=taskflow;Username=taskflow;Password=taskflow_e2e",
  DATABASE_URL: "postgres://taskflow:taskflow_e2e@localhost:55432/taskflow",
  JWT_SIGNING_KEY: "e2e-test-jwt-signing-key-0123456789abcdef0123456789abcdef",
  Jwt__SigningKey: "e2e-test-jwt-signing-key-0123456789abcdef0123456789abcdef",
  SESSION_SECRET: "e2e-test-session-secret-0123456789abcdef0123456789abcdef",
  API_INTERNAL_URL: "http://localhost:4311",
  APP_URL: "http://localhost:3000",
  ADMISSION_EMAILS: "admitted@taskflow.test",
  ASPNETCORE_URLS: "http://localhost:4311",
  ASPNETCORE_ENVIRONMENT: "Development",
  GOOGLE_CLIENT_ID: "e2e-fake-client-id",
  GOOGLE_CLIENT_SECRET: "e2e-fake-client-secret",
  GOOGLE_ISSUER: "http://localhost:4321",
  GOOGLE_AUTH_URI: "http://localhost:4321/auth",
  GOOGLE_TOKEN_URI: "http://localhost:4321/token",
  GOOGLE_JWKS_URI: "http://localhost:4321/jwks",
};

const SESSION_DDL = `
CREATE TABLE IF NOT EXISTS sessions (
  id uuid PRIMARY KEY,
  user_id uuid NOT NULL REFERENCES users(id) ON DELETE CASCADE,
  created_at timestamptz NOT NULL DEFAULT now(),
  last_accessed_at timestamptz NOT NULL DEFAULT now(),
  expires_at timestamptz NOT NULL,
  is_invalidated boolean NOT NULL DEFAULT false
);
CREATE INDEX IF NOT EXISTS ix_sessions_user_id ON sessions (user_id);
`;

const LOG = (m) => console.log(`[perf] ${m}`);
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
const median = (xs) => {
  const s = [...xs].sort((a, b) => a - b);
  const mid = Math.floor(s.length / 2);
  return s.length % 2 ? s[mid] : (s[mid - 1] + s[mid]) / 2;
};

function killTree(pid) {
  if (!pid) return;
  try {
    if (process.platform === "win32")
      execFileSync("taskkill", ["/F", "/T", "/PID", String(pid)], { stdio: "ignore" });
    else process.kill(pid, "SIGKILL");
  } catch {}
}

async function waitForPostgres(cs, timeoutMs = 60000) {
  const deadline = Date.now() + timeoutMs;
  for (;;) {
    const c = new Client({ connectionString: cs });
    c.on("error", () => {});
    try {
      await c.connect();
      await c.query("SELECT 1");
      await c.end();
      return;
    } catch (e) {
      await c.end().catch(() => {});
      if (Date.now() > deadline) throw e;
      await sleep(1000);
    }
  }
}

async function waitForHttp(url, label, timeoutMs = 120000) {
  const deadline = Date.now() + timeoutMs;
  for (;;) {
    try {
      const r = await fetch(url, { signal: AbortSignal.timeout(5000) });
      if (r.status < 500) return;
    } catch {}
    if (Date.now() > deadline) throw new Error(`${label} not ready within ${timeoutMs}ms (${url})`);
    await sleep(1000);
  }
}

// --- seeding (mirrors tests/e2e/helpers/seed.ts) --------------------------------------------
async function mintCarrier(claims) {
  const key = new TextEncoder().encode(ENV.JWT_SIGNING_KEY);
  const payload = {};
  if (claims.email !== undefined) payload.email = claims.email;
  if (claims.name !== undefined) payload.name = claims.name;
  return new SignJWT(payload)
    .setProtectedHeader({ alg: "HS256" })
    .setSubject(claims.sub)
    .setIssuer("taskflow-bff")
    .setAudience("taskflow-api")
    .setIssuedAt()
    .setExpirationTime("60s")
    .sign(key);
}

async function ensureUser(identity) {
  const token = await mintCarrier({ sub: identity.sub, email: identity.email, name: identity.name });
  const res = await fetch(new URL("/api/users/ensure", ENV.API_INTERNAL_URL), {
    method: "POST",
    headers: { "content-type": "application/json", authorization: `Bearer ${token}` },
    body: JSON.stringify({
      googleSubjectId: identity.sub,
      email: identity.email,
      displayName: identity.name,
    }),
  });
  if (!res.ok) throw new Error(`ensureUser failed (${res.status}): ${await res.text()}`);
  return res.json();
}

async function insertSession(userId) {
  const id = randomUUID();
  const client = new Client({ connectionString: ENV.DATABASE_URL });
  await client.connect();
  try {
    await client.query(SESSION_DDL);
    await client.query(
      `INSERT INTO sessions (id, user_id, expires_at) VALUES ($1, $2, now() + interval '1 hour')`,
      [id, userId],
    );
  } finally {
    await client.end();
  }
  return id;
}

async function createTask(taskFlowUserId, title, position) {
  const token = await mintCarrier({ sub: taskFlowUserId });
  const res = await fetch(new URL(`/api/tasks/${randomUUID()}`, ENV.API_INTERNAL_URL), {
    method: "PUT",
    headers: { "content-type": "application/json", authorization: `Bearer ${token}` },
    body: JSON.stringify({ title, position }),
  });
  if (!res.ok) throw new Error(`createTask failed (${res.status}): ${await res.text()}`);
}

// --- stack lifecycle -------------------------------------------------------------------------
const pids = {};

function teardown() {
  killTree(pids.web);
  killTree(pids.api);
  try {
    execFileSync("docker", ["rm", "-f", PG_CONTAINER], { stdio: "ignore" });
  } catch {}
}

async function bootStack() {
  if (!existsSync(API_DLL))
    throw new Error(`API not built: ${API_DLL} — run: dotnet build apps/api/src/TaskFlow.Api -c Debug`);
  if (!existsSync(resolve(WEB_DIR, ".next/BUILD_ID")))
    throw new Error(`No production build — run: pnpm --dir apps/web build`);

  try {
    execFileSync("docker", ["rm", "-f", PG_CONTAINER], { stdio: "ignore" });
  } catch {}
  LOG("starting Postgres container…");
  execFileSync(
    "docker",
    ["run", "-d", "--name", PG_CONTAINER, "-e", "POSTGRES_USER=taskflow", "-e",
      "POSTGRES_PASSWORD=taskflow_e2e", "-e", "POSTGRES_DB=taskflow", "-p", "55432:5432", "postgres:17"],
    { stdio: "ignore" },
  );
  await waitForPostgres(ENV.DATABASE_URL);
  LOG("Postgres ready");

  LOG("starting .NET API…");
  const apiLogFd = openSync(resolve(HERE, ".api.log"), "w");
  const api = spawn("dotnet", [API_DLL], {
    cwd: dirname(API_DLL),
    env: process.env,
    stdio: ["ignore", apiLogFd, apiLogFd],
    windowsHide: true,
  });
  api.unref();
  pids.api = api.pid;
  await waitForHttp(`${ENV.API_INTERNAL_URL}/openapi/v1.json`, ".NET API");
  LOG("API ready");

  LOG("starting Next BFF (production, next start)…");
  const webLogFd = openSync(resolve(HERE, ".web.log"), "w");
  const web = spawn(process.execPath, [NEXT_BIN, "start"], {
    cwd: WEB_DIR,
    env: process.env,
    stdio: ["ignore", webLogFd, webLogFd],
    windowsHide: true,
  });
  web.unref();
  pids.web = web.pid;
  await waitForHttp(`${ENV.APP_URL}/signin`, "Next BFF");
  LOG("BFF ready (production)");
}

// --- measurements ----------------------------------------------------------------------------
const LONGTASK_INIT = `
window.__lt = [];
try {
  new PerformanceObserver((list) => {
    for (const e of list.getEntries()) window.__lt.push({ start: e.startTime, end: e.startTime + e.duration });
  }).observe({ type: "longtask", buffered: true });
} catch {}
`;

async function measureLoad(browser, sessionId) {
  const context = await browser.newContext({ viewport: { width: 1440, height: 900 } });
  await context.addCookies([
    { name: "taskflow_session", value: sessionId, url: "http://localhost:3000" },
  ]);
  await context.addInitScript(LONGTASK_INIT);
  const page = await context.newPage();
  await page.goto(`${ENV.APP_URL}/`, { waitUntil: "networkidle" });
  await sleep(500); // settle: let trailing long tasks be observed
  const metrics = await page.evaluate(() => {
    const nav = performance.getEntriesByType("navigation")[0];
    const fcp = performance
      .getEntriesByType("paint")
      .find((p) => p.name === "first-contentful-paint")?.startTime;
    const lastLongTaskEnd = Math.max(0, ...window.__lt.map((t) => t.end));
    const dcl = nav ? nav.domContentLoadedEventEnd : 0;
    return {
      fcpMs: fcp ?? null,
      ttiMs: Math.max(fcp ?? 0, dcl, lastLongTaskEnd),
    };
  });
  await context.close();
  return metrics;
}

async function measureOptimisticPaint(browser, sessionId, runIndex) {
  const context = await browser.newContext({ viewport: { width: 1440, height: 900 } });
  await context.addCookies([
    { name: "taskflow_session", value: sessionId, url: "http://localhost:3000" },
  ]);
  const page = await context.newPage();
  await page.goto(`${ENV.APP_URL}/`, { waitUntil: "networkidle" });
  // Letters-only marker: a trailing number would be consumed by the slice-003 trailing
  // date-phrase parser and the created title would no longer contain the marker.
  const title = `Sonda optymistyczna ${"ABCDE"[runIndex - 1] ?? "X"}`;
  const result = await page.evaluate(
    (TITLE) =>
      new Promise((resolvePromise) => {
        const input = document.querySelector('input[aria-label="Nowy task"]');
        if (!input) {
          resolvePromise({ error: "quick-add input not found" });
          return;
        }
        const setter = Object.getOwnPropertyDescriptor(
          window.HTMLInputElement.prototype,
          "value",
        ).set;
        setter.call(input, TITLE);
        input.dispatchEvent(new Event("input", { bubbles: true }));
        // Let React commit the controlled-value update, then fire Enter in a fresh task.
        setTimeout(() => {
          const target = document.querySelector("main") ?? document.body;
          let observedAt = null;
          const mo = new MutationObserver(() => {
            if (observedAt === null && target.textContent.includes(TITLE))
              observedAt = performance.now();
          });
          mo.observe(target, { childList: true, subtree: true, characterData: true });
          const t0 = performance.now();
          input.dispatchEvent(
            new KeyboardEvent("keydown", { key: "Enter", bubbles: true, cancelable: true }),
          );
          const tSync = performance.now();
          const syncReflected = target.textContent.includes(TITLE);
          requestAnimationFrame(() => {
            const tFrame = performance.now();
            mo.disconnect();
            resolvePromise({
              syncReflected,
              syncDeltaMs: tSync - t0,
              frameReflected: target.textContent.includes(TITLE),
              frameDeltaMs: tFrame - t0,
              observerDeltaMs: observedAt === null ? null : observedAt - t0,
              inputCleared: input.value === "", // handler ran ⇒ setTitle("") cleared the field
            });
          });
        }, 50);
      }),
    title,
  );
  await context.close();
  return result;
}

// --- main ------------------------------------------------------------------------------------
async function main() {
  Object.assign(process.env, ENV);
  try {
    await bootStack();

    const profile = await ensureUser({
      sub: "google-sub-perf",
      email: "perf@taskflow.test",
      name: "Perf Proba",
    });
    const sessionId = await insertSession(profile.id);
    // A realistic (non-empty) Inbox: a handful of rows so the list actually renders content.
    for (let i = 0; i < 5; i++) await createTask(profile.id, `Zadanie bazowe ${i + 1}`, `a${i}`);
    LOG("seeded user + 5 tasks");

    const browser = await chromium.launch();
    const loads = [];
    for (let i = 0; i < 5; i++) {
      const m = await measureLoad(browser, sessionId);
      LOG(`load run ${i + 1}: FCP=${m.fcpMs?.toFixed(0)}ms TTI≈${m.ttiMs.toFixed(0)}ms`);
      loads.push(m);
    }
    const optimistic = [];
    for (let i = 0; i < 5; i++) {
      const o = await measureOptimisticPaint(browser, sessionId, i + 1);
      if (o.error) throw new Error(o.error);
      LOG(
        `optimistic run ${i + 1}: sync=${o.syncReflected} syncΔ=${o.syncDeltaMs.toFixed(2)}ms ` +
          `frame=${o.frameReflected} frameΔ=${o.frameDeltaMs.toFixed(2)}ms ` +
          `observerΔ=${o.observerDeltaMs === null ? "n/a" : o.observerDeltaMs.toFixed(2) + "ms"}`,
      );
      optimistic.push(o);
    }
    await browser.close();

    const summary = {
      build: "production (next build + next start)",
      fcpMedianMs: median(loads.map((l) => l.fcpMs)),
      ttiMedianMs: median(loads.map((l) => l.ttiMs)),
      optimisticSyncAll: optimistic.every((o) => o.syncReflected),
      optimisticFrameAll: optimistic.every((o) => o.frameReflected),
      optimisticDomDeltaMedianMs: median(
        optimistic.map((o) => (o.observerDeltaMs ?? o.syncDeltaMs)),
      ),
      budgets: { fcpMs: 1000, ttiMs: 2500, optimisticMs: 16 },
    };
    summary.pass =
      summary.fcpMedianMs < summary.budgets.fcpMs &&
      summary.ttiMedianMs < summary.budgets.ttiMs &&
      summary.optimisticFrameAll &&
      summary.optimisticDomDeltaMedianMs < summary.budgets.optimisticMs;
    console.log(`[perf] RESULT ${JSON.stringify(summary, null, 2)}`);
    if (!summary.pass) process.exitCode = 1;
  } finally {
    teardown();
    LOG("stack torn down");
  }
}

main().catch((e) => {
  console.error(`[perf] FAILED: ${e?.stack || e}`);
  teardown();
  process.exit(1);
});

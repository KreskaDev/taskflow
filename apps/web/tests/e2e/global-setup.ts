import { type ChildProcess, execFileSync, spawn } from "node:child_process";
import { createWriteStream, existsSync, openSync, readFileSync, rmSync, writeFileSync } from "node:fs";
import { dirname } from "node:path";
import { Client } from "pg";
import { startFakeIdp } from "./helpers/fake-idp";
import {
  API_DLL,
  NEXT_BIN,
  PG_CONTAINER,
  PG_PORT,
  SESSION_DDL,
  STATE_FILE,
  WEB_DIR,
} from "./helpers/paths";

/**
 * Boots the full US1 runtime — Postgres, the real .NET API (migrated), a fake RS256 IdP, and the
 * Next BFF — in strict order, so the seeded-session E2E (AS-02/03/04) drives the browser through the
 * REAL proxy→API path (the #1 web risk: the production token.ts HS256 carrier ↔ API validation, never
 * covered by the C# tests) and the AS-01 sign-in E2E drives the full OAuth dance through oauth.ts.
 * All processes are managed here (not via Playwright's `webServer`) because the FK from
 * `sessions` → `users` requires the API migration to run before the BFF touches the DB; orchestrating
 * here guarantees that ordering deterministically.
 *
 * Robustness: Playwright does NOT run `globalTeardown` if `globalSetup` throws, so a partial-boot
 * failure could orphan the .NET API on port 4311 and poison subsequent runs. Three guards prevent
 * that: (1) on entry we kill any PIDs recorded by a prior crashed run; (2) PIDs are written to the
 * state file incrementally, immediately after each spawn; (3) the whole boot is wrapped in a
 * try/catch that tears everything down before rethrowing.
 */

interface HarnessState {
  apiPid?: number;
  webPid?: number;
}

function log(message: string): void {
  // eslint-disable-next-line no-console -- harness diagnostics
  console.log(`[e2e setup] ${message}`);
}

async function sleep(ms: number): Promise<void> {
  await new Promise((r) => setTimeout(r, ms));
}

function killTree(pid: number | undefined): void {
  if (!pid) {
    return;
  }
  try {
    if (process.platform === "win32") {
      execFileSync("taskkill", ["/F", "/T", "/PID", String(pid)], { stdio: "ignore" });
    } else {
      process.kill(pid, "SIGKILL");
    }
  } catch {
    // already gone
  }
}

function writeState(state: HarnessState): void {
  writeFileSync(STATE_FILE, JSON.stringify(state), "utf8");
}

async function waitForPostgres(connectionString: string, timeoutMs = 60_000): Promise<void> {
  const deadline = Date.now() + timeoutMs;
  for (;;) {
    const client = new Client({ connectionString });
    try {
      await client.connect();
      await client.query("SELECT 1");
      await client.end();
      return;
    } catch (err) {
      await client.end().catch(() => undefined);
      if (Date.now() > deadline) {
        throw new Error(`Postgres not ready within ${String(timeoutMs)}ms: ${String(err)}`);
      }
      await sleep(1000);
    }
  }
}

async function waitForHttp(url: string, label: string, timeoutMs = 120_000): Promise<void> {
  const deadline = Date.now() + timeoutMs;
  for (;;) {
    try {
      const res = await fetch(url, { signal: AbortSignal.timeout(5000) });
      // Any HTTP response below 500 means the server is up and routing.
      if (res.status < 500) {
        return;
      }
    } catch {
      // not up yet
    }
    if (Date.now() > deadline) {
      throw new Error(`${label} not ready within ${String(timeoutMs)}ms (${url})`);
    }
    await sleep(1000);
  }
}

function docker(args: string[]): void {
  execFileSync("docker", args, { stdio: "ignore" });
}

/** Kills any processes/container left behind by a prior run that crashed before teardown. */
function reapStaleRun(): void {
  if (existsSync(STATE_FILE)) {
    try {
      const prior = JSON.parse(readFileSync(STATE_FILE, "utf8")) as HarnessState;
      killTree(prior.webPid);
      killTree(prior.apiPid);
    } catch {
      // unreadable state — nothing to reap
    }
    rmSync(STATE_FILE, { force: true });
  }
  try {
    docker(["rm", "-f", PG_CONTAINER]);
  } catch {
    // no prior container — fine
  }
}

export default async function globalSetup(): Promise<void> {
  const connectionString = process.env.DATABASE_URL as string;

  reapStaleRun();

  const state: HarnessState = {};
  let api: ChildProcess | undefined;
  let web: ChildProcess | undefined;
  let idp: import("node:http").Server | undefined;

  try {
    // --- 1. Disposable Postgres -------------------------------------------------------------
    log("starting Postgres container…");
    docker([
      "run",
      "-d",
      "--name",
      PG_CONTAINER,
      "-e",
      "POSTGRES_USER=taskflow",
      "-e",
      "POSTGRES_PASSWORD=taskflow_e2e",
      "-e",
      "POSTGRES_DB=taskflow",
      "-p",
      `${String(PG_PORT)}:5432`,
      "postgres:17",
    ]);
    await waitForPostgres(connectionString);
    log("Postgres ready");

    // --- 2. .NET API (migrates `users` + tombstone on startup) ------------------------------
    log("starting .NET API…");
    const apiLogFd = openSync(`${WEB_DIR}/tests/e2e/.api.log`, "w");
    api = spawn("dotnet", [API_DLL], {
      cwd: dirname(API_DLL),
      env: process.env,
      stdio: ["ignore", apiLogFd, apiLogFd],
      windowsHide: true,
    });
    // spawn failures (e.g. ENOENT) surface asynchronously; absorb them so they don't become an
    // uncaughtException that bypasses the catch below. The wait-for-http then fails cleanly, and the
    // catch (or the next run's reaper) tears everything down.
    api.on("error", (e) => log(`API process error: ${String(e)}`));
    api.unref();
    // Record the PID BEFORE the wait, so a boot-timeout still leaves a reapable trace.
    state.apiPid = api.pid;
    writeState(state);
    await waitForHttp(`${process.env.API_INTERNAL_URL}/openapi/v1.json`, ".NET API");
    log("API ready (migration applied)");

    // --- 3. Session table (eager, so seeding never races the BFF startup hook) ---------------
    const seedClient = new Client({ connectionString });
    await seedClient.connect();
    try {
      await seedClient.query(SESSION_DDL);
    } finally {
      await seedClient.end();
    }
    log("sessions table ensured");

    // --- 4. Fake IdP (AS-01) — up before the BFF makes any token/JWKS call -------------------
    const idpUrl = new URL(process.env.GOOGLE_ISSUER as string);
    idp = await startFakeIdp(
      Number(idpUrl.port),
      process.env.GOOGLE_ISSUER as string,
      process.env.GOOGLE_CLIENT_ID as string,
    );
    (globalThis as { __fakeIdp?: import("node:http").Server }).__fakeIdp = idp;
    log("fake IdP ready");

    // --- 5. Next BFF (dev mode → non-Secure cookies over http://localhost) -------------------
    log("starting Next BFF…");
    const webLogStream = createWriteStream(`${WEB_DIR}/tests/e2e/.web.log`, { flags: "w" });
    web = spawn(process.execPath, [NEXT_BIN, "dev"], {
      cwd: WEB_DIR,
      env: process.env,
      stdio: ["ignore", "pipe", "pipe"],
      windowsHide: true,
    });
    web.on("error", (e) => log(`BFF process error: ${String(e)}`));
    web.stdout?.pipe(webLogStream);
    web.stderr?.pipe(webLogStream);
    web.unref();
    state.webPid = web.pid;
    writeState(state);
    await waitForHttp(`${process.env.APP_URL}/signin`, "Next BFF");
    // Warm the dynamic routes the specs hit so the first real navigation is not lost to compilation.
    await fetch(`${process.env.APP_URL}/api/auth/session`).catch(() => undefined);
    await fetch(`${process.env.APP_URL}/settings`).catch(() => undefined);
    log("BFF ready");

    log("setup complete");
  } catch (err) {
    // globalTeardown will NOT run on a setup throw — tear down whatever started, here and now.
    log(`setup failed, tearing down: ${String(err)}`);
    killTree(web?.pid);
    killTree(api?.pid);
    idp?.close();
    try {
      docker(["rm", "-f", PG_CONTAINER]);
    } catch {
      // already gone
    }
    if (existsSync(STATE_FILE)) {
      rmSync(STATE_FILE, { force: true });
    }
    throw err;
  }
}

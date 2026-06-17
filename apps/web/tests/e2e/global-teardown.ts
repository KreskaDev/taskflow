import { execFileSync } from "node:child_process";
import { existsSync, readFileSync, rmSync } from "node:fs";
import { PG_CONTAINER, STATE_FILE } from "./helpers/paths";

/** Tears down everything global-setup started: the BFF + API process trees and the Postgres container. */

function killTree(pid: number | undefined): void {
  if (!pid) {
    return;
  }
  try {
    if (process.platform === "win32") {
      // /T kills the whole tree (next dev spawns a worker; dotnet may spawn children).
      execFileSync("taskkill", ["/F", "/T", "/PID", String(pid)], { stdio: "ignore" });
    } else {
      process.kill(-pid, "SIGKILL");
    }
  } catch {
    // already gone
  }
}

export default function globalTeardown(): void {
  let state: { apiPid?: number; webPid?: number } = {};
  if (existsSync(STATE_FILE)) {
    try {
      state = JSON.parse(readFileSync(STATE_FILE, "utf8")) as typeof state;
    } catch {
      state = {};
    }
  }

  killTree(state.webPid);
  killTree(state.apiPid);

  const idp = (globalThis as { __fakeIdp?: import("node:http").Server }).__fakeIdp;
  if (idp) {
    idp.close();
  }

  try {
    execFileSync("docker", ["rm", "-f", PG_CONTAINER], { stdio: "ignore" });
  } catch {
    // container already removed
  }

  if (existsSync(STATE_FILE)) {
    rmSync(STATE_FILE, { force: true });
  }
}

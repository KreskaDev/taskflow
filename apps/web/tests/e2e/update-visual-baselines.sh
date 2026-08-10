#!/usr/bin/env bash
# T066 (slice 019) — container-side baseline generation for the [V] visual suite.
# Runs INSIDE mcr.microsoft.com/playwright:v1.61.0-noble (see update-visual-baselines.ps1).
# `toHaveScreenshot` baselines are platform-suffixed: they MUST be produced in the
# CI-matching linux environment (ubuntu-latest ≙ noble + Playwright 1.61.0 browsers).
#
# The E2E harness (tests/e2e/global-setup.ts) self-boots its stack; in-container that means:
#   - `docker run` goes to the HOST daemon via the mounted /var/run/docker.sock, so the
#     disposable Postgres publishes :55432 on the HOST → reach it via host.docker.internal
#   - the built Debug TaskFlow.Api.dll (built on the host, portable) needs an ASP.NET Core 9
#     runtime inside the container
#   - Windows node_modules are unusable on linux → work on a copy, fresh `pnpm install`
#
# Generate baselines ONLY when the UI is in the state you want to freeze — every pixel
# committed here becomes the CI truth until regenerated (human approval per palette, T070).
set -euo pipefail

REPO_MOUNT=/mnt/repo
WORK=/work
DOCKER_CLI_VERSION=27.3.1
PNPM_VERSION=11.5.1   # keep in lockstep with pnpm/action-setup in .github/workflows/ci.yml

echo "[baselines] docker CLI…"
if ! command -v docker >/dev/null 2>&1; then
  curl -fsSL "https://download.docker.com/linux/static/stable/x86_64/docker-${DOCKER_CLI_VERSION}.tgz" \
    | tar -xz -C /tmp
  install /tmp/docker/docker /usr/local/bin/docker
fi
docker version --format 'server {{.Server.Version}}' >/dev/null # fails fast if the socket is missing

echo "[baselines] ASP.NET Core 9 runtime…"
if ! command -v dotnet >/dev/null 2>&1; then
  curl -fsSL https://dot.net/v1/dotnet-install.sh -o /tmp/dotnet-install.sh
  bash /tmp/dotnet-install.sh --channel 9.0 --runtime aspnetcore --install-dir /usr/share/dotnet
  ln -sf /usr/share/dotnet/dotnet /usr/local/bin/dotnet
fi

echo "[baselines] linux working copy (mounted node_modules are Windows-flavoured)…"
mkdir -p "$WORK"
tar -C "$REPO_MOUNT" \
  --exclude=node_modules --exclude=.next --exclude=.git \
  --exclude=test-results --exclude=playwright-report \
  -cf - . | tar -C "$WORK" -xf -
cd "$WORK"

echo "[baselines] pnpm ${PNPM_VERSION} + install…"
npm install -g "pnpm@${PNPM_VERSION}" >/dev/null
pnpm install --frozen-lockfile

if [ ! -f "$WORK/apps/api/src/TaskFlow.Api/bin/Debug/net9.0/TaskFlow.Api.dll" ]; then
  echo "[baselines] FATAL: Debug API DLL missing — run on the host first:" >&2
  echo "           dotnet build apps/api/src/TaskFlow.Api -c Debug" >&2
  exit 1
fi

# The harness PG runs on the HOST daemon and publishes :55432 there; everything else
# (API :4311, fake IdP :4321, BFF :3000) runs inside THIS container on localhost.
# playwright.config.ts uses `??=`, so these pre-set values win over its defaults.
export DATABASE_URL="postgres://taskflow:taskflow_e2e@host.docker.internal:55432/taskflow"
export ConnectionStrings__postgres="Host=host.docker.internal;Port=55432;Database=taskflow;Username=taskflow;Password=taskflow_e2e"

echo "[baselines] generating [V] baselines (--update-snapshots)…"
cd "$WORK/apps/web"
npx playwright test tests/e2e/visual.spec.ts --update-snapshots

echo "[baselines] copying linux-suffixed baselines back into the repo…"
mkdir -p "$REPO_MOUNT/apps/web/tests/e2e/visual.spec.ts-snapshots"
cp -r "$WORK/apps/web/tests/e2e/visual.spec.ts-snapshots/." \
      "$REPO_MOUNT/apps/web/tests/e2e/visual.spec.ts-snapshots/"

echo "[baselines] done — review + commit the snapshots, then human-approve per palette (T070)"

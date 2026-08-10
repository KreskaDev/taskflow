# T066 (slice 019) — host-side wrapper: generate the [V] visual baselines inside the
# CI-matching Playwright linux image (see update-visual-baselines.sh for the details).
#
# Prereqs (host):
#   - Docker Desktop running
#   - dotnet build apps/api/src/TaskFlow.Api -c Debug     (the harness runs the Debug DLL)
#   - port 55432 free on the host (stop dev-run / e2e stacks first)
#
# Run only when the UI is final for this slice — the generated PNGs become the CI truth
# and require human approval per palette (T070) before merge.
param(
  # Must match the Playwright version in apps/web (pnpm-lock: @playwright/test 1.61.0)
  # AND the ubuntu flavour of the CI runner (ubuntu-latest = noble).
  [string]$Image = "mcr.microsoft.com/playwright:v1.61.0-noble"
)
$ErrorActionPreference = "Stop"

$repo = (Resolve-Path (Join-Path $PSScriptRoot "..\..\..\..")).Path
Write-Host "[baselines] repo:  $repo"
Write-Host "[baselines] image: $Image"

docker run --rm `
  -v /var/run/docker.sock:/var/run/docker.sock `
  -v "${repo}:/mnt/repo" `
  --add-host "host.docker.internal:host-gateway" `
  $Image `
  bash /mnt/repo/apps/web/tests/e2e/update-visual-baselines.sh
if ($LASTEXITCODE -ne 0) { throw "baseline generation failed (exit $LASTEXITCODE)" }

Write-Host "[baselines] snapshots written to apps/web/tests/e2e/visual.spec.ts-snapshots/"

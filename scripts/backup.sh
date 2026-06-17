#!/usr/bin/env bash
# pg_dump wrapper — pre-migration and scheduled backups (Constitution VII / FR-051).
# Writes a custom-format dump and echoes its path on stdout.
set -euo pipefail

BACKUP_DIR="${BACKUP_DIR:-/var/backups/taskflow}"
mkdir -p "${BACKUP_DIR}"

STAMP="$(date -u +%Y%m%dT%H%M%SZ)"
OUT="${BACKUP_DIR}/taskflow-${STAMP}.dump"

CONN="${DATABASE_URL:-postgres://${POSTGRES_USER:?}:${POSTGRES_PASSWORD:?}@${POSTGRES_HOST:-localhost}:${POSTGRES_PORT:-5432}/${POSTGRES_DB:?}}"

echo "Backing up to ${OUT}" >&2
pg_dump --format=custom --no-owner --no-privileges --dbname="${CONN}" --file="${OUT}"
echo "${OUT}"

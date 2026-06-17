#!/usr/bin/env bash
# Restore a backup into a throwaway database and assert integrity (Constitution VII).
# "Restorable" is not assertable until a restore is verified.
set -euo pipefail

DUMP="${1:?usage: restore-test.sh <dump-file>}"
[ -f "${DUMP}" ] || { echo "dump not found: ${DUMP}" >&2; exit 1; }

PGHOST="${POSTGRES_HOST:-localhost}"
PGPORT="${POSTGRES_PORT:-5432}"
PGUSER="${POSTGRES_USER:?}"
export PGPASSWORD="${POSTGRES_PASSWORD:?}"
TMP_DB="taskflow_restoretest_$$"

cleanup() { dropdb -h "${PGHOST}" -p "${PGPORT}" -U "${PGUSER}" --if-exists "${TMP_DB}" >/dev/null 2>&1 || true; }
trap cleanup EXIT

echo "Restoring ${DUMP} into throwaway DB ${TMP_DB}" >&2
createdb -h "${PGHOST}" -p "${PGPORT}" -U "${PGUSER}" "${TMP_DB}"
pg_restore --no-owner --no-privileges --dbname="${TMP_DB}" \
    -h "${PGHOST}" -p "${PGPORT}" -U "${PGUSER}" "${DUMP}"

# Integrity assertion: the restored schema must contain the users table.
COUNT="$(psql -h "${PGHOST}" -p "${PGPORT}" -U "${PGUSER}" -d "${TMP_DB}" -tAc \
    "SELECT count(*) FROM information_schema.tables WHERE table_name = 'users';")"
if [ "${COUNT}" -lt 1 ]; then
    echo "restore-test FAILED: 'users' table not present in restored DB" >&2
    exit 1
fi
echo "restore-test OK: schema restored and verified" >&2

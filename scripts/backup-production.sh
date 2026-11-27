#!/usr/bin/env bash
# Full backup of a PSMPE Portal environment: the Postgres database AND the uploaded files
# (member photos, PRC IDs, payment proofs). Always take both - a database restore alone leaves
# every member without their images. See docs/backup-sop.md.
#
# Usage (Git Bash / Linux / macOS):
#   BACKUP_SSH_HOST=<ssh host or alias> BACKUP_LOCAL_DIR=<folder for the local copy> \
#     scripts/backup-production.sh [compose-project]        # default project: psmpe-production
#
# Read-only on the server apart from writing the backup files. Nothing is stopped or changed.
set -euo pipefail

: "${BACKUP_SSH_HOST:?set BACKUP_SSH_HOST to the SSH host/alias of the server}"
: "${BACKUP_LOCAL_DIR:?set BACKUP_LOCAL_DIR to the folder for the local copy}"

PROJECT="${1:-psmpe-production}"
DB_USER="${DB_USER:-psmpe_user}"
DB_NAME="${DB_NAME:-psmpe_portal}"
REMOTE_ROOT="${REMOTE_ROOT:-/root/backups/$PROJECT}"
STAMP="$(date +%Y%m%d-%H%M%S)"
SET="$REMOTE_ROOT/$STAMP"
PG_CONTAINER="${PROJECT}-postgres-1"
UPLOADS_VOLUME="${PROJECT}_uploads_data"
SSH=(ssh -o BatchMode=yes "$BACKUP_SSH_HOST")

echo "==> Backing up '$PROJECT' as set $STAMP"

# 1. Dump the database and archive the uploads volume on the server. Both files are written
#    owner-only (umask 077) because they contain member personal data.
"${SSH[@]}" bash -s -- "$SET" "$PG_CONTAINER" "$DB_USER" "$DB_NAME" "$UPLOADS_VOLUME" <<'REMOTE'
set -euo pipefail
umask 077
set_dir="$1"; pg="$2"; db_user="$3"; db_name="$4"; volume="$5"
mkdir -p "$set_dir"

docker exec "$pg" pg_dump -U "$db_user" -d "$db_name" -Fc --no-owner > "$set_dir/database.dump"

mount="$(docker volume inspect "$volume" --format '{{.Mountpoint}}')"
tar -czf "$set_dir/uploads.tar.gz" -C "$mount" .

# 2. Verify on the server: the dump must list its table data, and the archive must read back.
tables="$(docker exec -i "$pg" pg_restore --list < "$set_dir/database.dump" | grep -c ' TABLE DATA ')"
files_on_disk="$(find "$mount" -type f | wc -l)"
files_in_tar="$(tar -tzf "$set_dir/uploads.tar.gz" | grep -vc '/$' || true)"
echo "database.dump : $tables tables with data"
echo "uploads       : $files_in_tar files archived, $files_on_disk on disk"
[ "$tables" -gt 0 ] || { echo "ERROR: dump has no table data" >&2; exit 1; }
[ "$files_in_tar" -eq "$files_on_disk" ] || { echo "ERROR: uploads archive is missing files" >&2; exit 1; }

( cd "$set_dir" && sha256sum database.dump uploads.tar.gz > SHA256SUMS )
ls -l "$set_dir"
REMOTE

# 3. Copy the set to this machine and check every checksum again after the transfer.
mkdir -p "$BACKUP_LOCAL_DIR/$STAMP"
scp -q -o BatchMode=yes "$BACKUP_SSH_HOST:$SET/database.dump" "$BACKUP_SSH_HOST:$SET/uploads.tar.gz" \
    "$BACKUP_SSH_HOST:$SET/SHA256SUMS" "$BACKUP_LOCAL_DIR/$STAMP/"
( cd "$BACKUP_LOCAL_DIR/$STAMP" && sha256sum -c SHA256SUMS )

echo "==> OK. Server copy: $BACKUP_SSH_HOST:$SET"
echo "==> OK. Local copy : $BACKUP_LOCAL_DIR/$STAMP"

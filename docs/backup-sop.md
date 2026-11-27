# Backup SOP (standard operating procedure)

**Every backup is a full backup: the database AND the uploaded images.** A database restore alone
leaves members without their photos, PRC IDs and payment proofs. Never take, or accept, a
database-only backup.

## What is in a backup set

| File | Contents |
|---|---|
| `database.dump` | `pg_dump -Fc` of the `psmpe_portal` database (members, payments, users, ...) |
| `uploads.tar.gz` | The `<project>_uploads_data` Docker volume: member photos, PRC IDs, payment proofs |
| `SHA256SUMS` | Checksums of the two files, re-checked after copying to the local machine |

Both files contain member personal data. They are written owner-only on the server; keep the local
copy somewhere private.

## When to back up

- **Before every production release** (anything that merges into `main`).
- Before any manual change to production data (SQL, deleting rows, bulk edits).
- Before restoring, migrating or rebuilding the server.

## How

From Git Bash on the machine with SSH access to the server:

```bash
BACKUP_SSH_HOST=<ssh host or alias> \
BACKUP_LOCAL_DIR="<folder for the local copy>" \
  scripts/backup-production.sh            # production; pass a compose project name for another env
```

The script dumps the database, archives the uploads volume, verifies both (the dump must list its
table data; the archive must hold as many files as the volume), checksums them, copies the set to
`BACKUP_LOCAL_DIR/<timestamp>/` and re-checks the checksums there. It stops on the first failure and
prints `OK` for both copies only when everything passed. It only reads from the running site, so it
is safe to run at any time.

A copy also stays on the server under `/root/backups/<project>/<timestamp>/`.

## Restore (outline)

1. Database: `pg_restore -d psmpe_portal --clean --if-exists database.dump` inside the Postgres
   container.
2. Uploads: extract `uploads.tar.gz` into the volume's mount point
   (`docker volume inspect <project>_uploads_data`), then `chown -R` it to the app user's uid so the
   backend can write to it.

Test a restore into a scratch environment before relying on a backup for the first time.

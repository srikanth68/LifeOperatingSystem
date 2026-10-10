# Maaya backup

Every byte of Maaya is a handful of SQLite files under `deploy/data`. Most of it cannot be
reconstructed: NorthStar's accumulated memory, Aasthi's property history, years of habit
streaks and health data. Statements and Oura pulls could be re-imported; that lot could not.

## How it works: weekly, while Maaya is stopped

Every Sunday at 03:30 the job:

1. **Stops** the stack (`docker compose stop`), remembering which services were running.
2. **Copies** every database and any side file a stop can leave (`-wal`, `-journal`), plus
   module `storage/` folders. Nothing is writing, so a plain copy is exactly consistent.
3. **Starts** again exactly what was running. This happens however the script ends: on
   success, on a failure, or on Ctrl-C.
4. **Checks** every copy with `PRAGMA integrity_check`, after the stack is back up.
5. Writes `deploy/data/backup-status.json`, which San's health check reads.

Downtime is about a minute.

### Why it stops Maaya instead of copying live

On Everest the containers run inside Colima's virtual machine (`vmType: vz`) while the
databases live on the Mac, shared in through **virtiofs**. SQLite coordinates several
programs on one database through file locks and a shared-memory file, and those do not
reliably cross that boundary. In October 2026 two databases were damaged after a program on
the Mac opened them while containers had them open:

| Date | Database | Opened from the Mac by |
|---|---|---|
| 2026-10-07 | `vitara.db` (first page destroyed) | this script's previous version (`sqlite3 … VACUUM INTO`) |
| 2026-10-08 | `san.db` (six pages destroyed) | DB Browser for SQLite |

**Rule: never open a live Maaya database from the Mac**, with any tool. To look inside one,
copy the files first (`x.db`, `x.db-wal`, `x.db-shm`) and open the copy, read-only.

### What it guarantees

- **One damaged database never costs the others.** Every good copy is kept; the damaged one
  is kept too (it is the starting point for a repair) and listed by name in the snapshot's
  `RESULT` file and the status file, with SQLite's own error message.
- **It catches damage in the live data.** The copy is taken from files nothing is writing to,
  so a copy that fails the check means the live database is damaged. San raises it as
  critical.
- **Retention never deletes the newest snapshot that passed every check**, however old: if a
  database has been quietly damaged for months, the last good copy is the one that matters.
- **It never stops a stack it cannot restart cleanly:** if `docker compose stop` fails, it
  copies nothing and starts everything again.

## Install (on Everest)

1. **Give the background job access to Documents.** The repo is in `~/Documents`, which macOS
   protects. A job run by launchd is blocked from reading it ("Operation not permitted")
   unless `/bin/bash` has Full Disk Access:
   System Settings → Privacy & Security → **Full Disk Access** → **+** → press
   ⌘⇧G, type `/bin/bash`, add it, and make sure it is switched on.

2. Install the weekly job:

```bash
cp ~/Documents/maaya/deploy/backup/com.maaya.backup.plist ~/Library/LaunchAgents/ && launchctl load ~/Library/LaunchAgents/com.maaya.backup.plist
```

3. Run it once **through launchd**, not by hand, so the permission in step 1 is tested too:

```bash
launchctl start com.maaya.backup
```

4. A minute later, read the result:

```bash
tail -30 ~/Documents/maaya/deploy/backups/backup.log && cat ~/Documents/maaya/deploy/data/backup-status.json
```

`"ok": true` and one `ok` line per database means it works.

## Restoring

Snapshots are plain SQLite files in the same layout as `deploy/data`, so a restore is a copy.
**Stop the stack (or the one service) first**: restoring underneath a running process is how
to get a second damaged database on the worst possible day. Check the snapshot's `RESULT`
file says `ok` first.

Everything:

```bash
cd ~/Documents/maaya && docker compose stop && cp -R deploy/backups/2026-10-18T073000Z/. deploy/data/ && docker compose start
```

One module (for example San):

```bash
cd ~/Documents/maaya && docker compose stop san san-worker && cp deploy/backups/2026-10-18T073000Z/san/san.db deploy/data/san/ && docker compose start san san-worker
```

## Configuration (environment variables, all optional)

| Variable | Default | Meaning |
|---|---|---|
| `MAAYA_REPO` | the repo this script is in | where `docker compose` runs |
| `MAAYA_DATA` | `deploy/data` | live data (the compose bind mount) |
| `MAAYA_BACKUP_DIR` | `deploy/backups` | tier 1 destination |
| `MAAYA_BACKUP_MIRROR` | *(unset)* | tier 2, an external drive; unmounted is not an error |
| `MAAYA_KEEP` | `12` | snapshots kept (about three months of weekly runs) |
| `MAAYA_BACKUP_EVERY_HOURS` | `168` | written to the status file so San knows when a run is overdue |

## Tiers

| Tier | Where | Protects against |
|---|---|---|
| 1 | `deploy/backups/` (same disk) | a bad deploy, a damaged database, a migration that eats a table, deleting the wrong thing |
| 2 | external drive (`MAAYA_BACKUP_MIRROR`) | disk failure, which tier 1 cannot |
| 3 | off-site, encrypted | theft, fire: **not built** |

On the drive: a USB flash drive is the worst choice for repeated backup writes. They wear out
and fail silently. Prefer a cheap external SSD.

## Monitoring

San's health check reads `backup-status.json`. It raises **critical** when the last run
reported a problem (naming each database), and **high** when no run has succeeded for a day
longer than `expectedEveryHours`, so a missed Sunday is noticed on Monday.

## Tests

```bash
python3 deploy/backup/test_maaya_backup.py
```

Runs the script against a fake Docker and real SQLite files: a normal run, a database left
with an unfolded WAL, one damaged database, a stack that will not stop, Docker down, retention,
and a second run while one is running.

## Not yet done

- **Tier 3, off-site.** The data is financial, medical and personal; if it ever leaves the
  Mac it must be encrypted *before* upload (`age` or `gpg`), never by trusting the destination.
- **A restore drill.** A backup nobody has restored is a hypothesis. Worth doing once,
  deliberately, into a scratch folder.
- **Moving the databases into the VM's own disk** (a Docker volume) would remove the file-lock
  boundary entirely. Bigger change, separate decision.

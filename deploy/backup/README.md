# Maaya backup

Every byte of Maaya is a handful of SQLite files under `deploy/data`. Most of it
cannot be reconstructed — NorthStar's accumulated memory, Aasthi's property history,
years of habit streaks and health data. Statements and Oura pulls could be
re-imported; that lot could not.

## What it does

| Tier | Where | Protects against |
|---|---|---|
| 1 | `deploy/backups/` (same disk) | bad deploy, `docker compose down -v`, a migration that eats a table, deleting the wrong thing |
| 2 | external drive (`MAAYA_BACKUP_MIRROR`) | disk failure, which tier 1 cannot |
| 3 | off-site, encrypted | theft, fire — **not built yet** |

Tier 1 covers the overwhelming majority of real incidents and costs nothing. Tier 2
is one env var away once a drive is attached.

## Two things it does NOT do the obvious way, on purpose

**It does not copy the `.db` files.** Copying a live SQLite database can capture a
half-applied transaction and produce a backup that looks perfectly fine until the day
you restore it. It uses `VACUUM INTO`, which takes a consistent snapshot of a database
that is actively being written to. **Nothing has to be stopped.**

**It verifies before it deletes.** Every snapshot gets `PRAGMA integrity_check`, and
retention pruning only runs after the new snapshot has passed. A bad backup night can
never take the last good copies with it.

## Install (on the Mac mini)

1. Edit `com.maaya.backup.plist` — replace `/Users/kanth/maaya` with the real repo
   path, and set `MAAYA_BACKUP_MIRROR` to the external drive (or delete those two
   lines for tier 1 only).

2. Install and start it:

```bash
cp deploy/backup/com.maaya.backup.plist ~/Library/LaunchAgents/ && launchctl load ~/Library/LaunchAgents/com.maaya.backup.plist
```

3. Run it once by hand to confirm it works before trusting the schedule:

```bash
bash deploy/backup/maaya-backup.sh
```

Runs nightly at 03:15 local. Log: `deploy/backups/backup.log`.

## Restoring

Snapshots are plain SQLite files in the same layout as `deploy/data`, so a restore is
a copy. **Stop the stack first** — restoring underneath a running process gives you a
second corruption to debug on the worst possible day.

```bash
docker compose down && cp -R deploy/backups/2026-08-11T031500Z/. deploy/data/ && docker compose up -d
```

Single module (e.g. just NorthStar):

```bash
docker compose stop northstar && cp deploy/backups/2026-08-11T031500Z/northstar/run/northstar.db deploy/data/northstar/run/ && docker compose start northstar
```

## Configuration

| Variable | Default | Meaning |
|---|---|---|
| `MAAYA_DATA` | `deploy/data` | live data (the compose bind mount) |
| `MAAYA_BACKUP_DIR` | `deploy/backups` | tier 1 destination |
| `MAAYA_BACKUP_MIRROR` | *(unset)* | tier 2 destination; unmounted is not an error |
| `MAAYA_KEEP_DAILY` | `14` | every snapshot kept for this many days |
| `MAAYA_KEEP_WEEKLY` | `8` | then one per week for this many weeks |

The databases total well under a megabyte, so retention is generous by default — a
year of snapshots costs less than a single photo.

## Monitoring

Each run writes `deploy/data/backup-status.json`, which every container sees at
`/data/backup-status.json`. San's self-check reads it and raises a problem if the last
successful backup is more than 48h old or the last run failed — so a backup that
silently stopped three weeks ago tells you, rather than waiting to be discovered on
the day you need it.

## On the destination drive

A USB **flash drive / pendrive** is the worst choice for repeated backup writes: they
wear out and fail *silently*, which is precisely the failure mode you cannot afford
in a backup. Prefer a cheap external SSD. If a pendrive is what is available, use it
— it still beats tier 1 alone — but rotate two and treat it as temporary.

## Not yet done

- **Tier 3, off-site.** This data is financial, medical and personal; if it ever
  leaves the Mac it must be encrypted *before* upload (`age` or `gpg`), never by
  trusting the destination.
- **A restore drill.** A backup nobody has ever restored is a hypothesis, not a
  backup. Worth doing once, deliberately, into a scratch directory.

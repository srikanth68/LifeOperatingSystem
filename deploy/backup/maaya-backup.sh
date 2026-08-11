#!/usr/bin/env bash
#
# Maaya OS — nightly backup.
#
# Runs on the HOST (the Mac mini), not in a container, deliberately: a backup that
# needs Docker to be healthy is missing the case where Docker is why you need it.
# It also needs no cooperation from the stack — nothing has to be stopped.
#
# Every byte of Maaya is a handful of SQLite files under deploy/data. Most of it
# cannot be reconstructed: NorthStar's accumulated memory, Aasthi's property history,
# years of habit streaks. Statements and Oura pulls could be re-imported; that lot
# could not.
#
# Consistency: uses `VACUUM INTO`, NOT cp/rsync of the .db files. Copying a live
# SQLite database can capture a half-applied transaction and produce a backup that
# looks perfectly fine until the day you restore it. VACUUM INTO takes a proper
# consistent snapshot of a database that is being written to, and every snapshot is
# then verified with PRAGMA integrity_check before anything old is deleted.
#
# Usage:   ./maaya-backup.sh
# Config:  environment variables, all optional — see below.
# Install: see README.md (launchd).

set -euo pipefail

HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

# Where the live data lives (the deploy/data bind mount).
DATA="${MAAYA_DATA:-$(cd "$HERE/.." && pwd)/data}"

# Tier 1 — same disk. Catches the overwhelmingly common disasters: a bad deploy,
# `docker compose down -v`, a migration that eats a table, deleting the wrong thing.
# Fast to write, instant to restore, needs no hardware.
BACKUP_DIR="${MAAYA_BACKUP_DIR:-$(cd "$HERE/.." && pwd)/backups}"

# Tier 2 — external drive. Catches disk failure, which tier 1 cannot. Optional, and
# a missing/unmounted drive is NOT an error: tier 1 still ran and still protects you.
MIRROR="${MAAYA_BACKUP_MIRROR:-}"

KEEP_DAILY="${MAAYA_KEEP_DAILY:-14}"
KEEP_WEEKLY="${MAAYA_KEEP_WEEKLY:-8}"

STAMP="$(date -u +%Y-%m-%dT%H%M%SZ)"
STARTED_AT="$(date -u +%Y-%m-%dT%H:%M:%SZ)"
START_EPOCH="$(date +%s)"
STATUS_FILE="$DATA/backup-status.json"

log() { printf '%s  %s\n' "$(date -u +%H:%M:%S)" "$*"; }
die() { log "FATAL: $*"; write_status "false" "0" "0" "$*"; exit 1; }

# Written into the data directory, which is bind-mounted into every container as
# /data — so San's health check can read it and tell you the backups have stopped.
# A backup you are not watching is a backup you will discover has been broken for
# three weeks.
write_status() {
    local ok="$1" dbs="$2" bytes="$3" err="${4:-}"
    local finished; finished="$(date -u +%Y-%m-%dT%H:%M:%SZ)"
    local secs=$(( $(date +%s) - START_EPOCH ))
    mkdir -p "$DATA" 2>/dev/null || true
    cat > "$STATUS_FILE" <<EOF
{
  "lastRunUtc": "$finished",
  "startedUtc": "$STARTED_AT",
  "ok": $ok,
  "databases": $dbs,
  "bytes": $bytes,
  "durationSeconds": $secs,
  "mirrored": $([ -n "$MIRROR" ] && [ -d "$MIRROR" ] && echo true || echo false),
  "mirrorPath": "${MIRROR//\"/}",
  "snapshot": "$STAMP",
  "error": "${err//\"/}"
}
EOF
}

command -v sqlite3 >/dev/null 2>&1 || die "sqlite3 not found on PATH (macOS ships it at /usr/bin/sqlite3)."
[ -d "$DATA" ] || die "data directory not found: $DATA"

# Build into .partial and rename only on success, so an interrupted run can never be
# mistaken for a complete snapshot by the restore instructions or the pruner.
DEST="$BACKUP_DIR/$STAMP"
PARTIAL="$DEST.partial"
rm -rf "$PARTIAL"
mkdir -p "$PARTIAL"

log "Maaya backup → $DEST"
log "  source: $DATA"

db_count=0
failed=""

# -print0/read -d handles paths with spaces — "Claude Workspace" already has one.
while IFS= read -r -d '' db; do
    rel="${db#"$DATA"/}"
    out="$PARTIAL/$rel"
    mkdir -p "$(dirname "$out")"

    if ! sqlite3 "$db" "VACUUM INTO '$out'" 2>/dev/null; then
        log "  !! snapshot FAILED: $rel"
        failed="$failed $rel"
        continue
    fi

    # A snapshot that cannot be read back is not a backup. Checked here, before any
    # retention pruning, so a bad run can never take the good copies with it.
    check="$(sqlite3 "$out" "PRAGMA integrity_check;" 2>/dev/null | head -1)"
    if [ "$check" != "ok" ]; then
        log "  !! integrity check FAILED: $rel ($check)"
        failed="$failed $rel"
        continue
    fi

    log "  ok  $rel"
    db_count=$((db_count + 1))
done < <(find "$DATA" -type f -name '*.db' -print0)

[ "$db_count" -gt 0 ] || die "no databases found under $DATA — refusing to record a successful empty backup."

# Sutra's uploads and any other module storage are ordinary files, not databases, and
# are just as unrecoverable.
while IFS= read -r -d '' dir; do
    rel="${dir#"$DATA"/}"
    if [ -n "$(ls -A "$dir" 2>/dev/null)" ]; then
        mkdir -p "$PARTIAL/$rel"
        cp -R "$dir/." "$PARTIAL/$rel/" 2>/dev/null || log "  !! partial copy: $rel"
        log "  ok  $rel/ (files)"
    fi
done < <(find "$DATA" -type d -name storage -print0)

if [ -n "$failed" ]; then
    rm -rf "$PARTIAL"
    die "one or more databases failed to back up:$failed"
fi

mv "$PARTIAL" "$DEST"
bytes="$(du -sk "$DEST" | awk '{print $1 * 1024}')"
log "Snapshot complete: $db_count database(s), $((bytes / 1024)) KiB"

# ── Tier 2: external drive ────────────────────────────────────────────────────
mirrored=0
if [ -n "$MIRROR" ]; then
    if [ -d "$MIRROR" ]; then
        mkdir -p "$MIRROR"
        if cp -R "$DEST" "$MIRROR/"; then
            log "Mirrored to $MIRROR"
            mirrored=1
        else
            log "!! mirror copy failed — tier 1 snapshot is still good"
        fi
    else
        # The realistic state of a removable drive. Not an error: it must not fail the
        # run or block pruning, but it is worth saying out loud every time.
        log "!! mirror path not mounted ($MIRROR) — tier 1 only for this run"
    fi
fi

# ── Retention ─────────────────────────────────────────────────────────────────
# Only ever runs after a verified snapshot, so a broken backup night cannot delete
# the last good copies. Keeps every snapshot for KEEP_DAILY days, then one per
# ISO week for KEEP_WEEKLY weeks.
prune() {
    local dir="$1"
    [ -d "$dir" ] || return 0
    local kept_weeks=""
    local i=0

    while IFS= read -r snap; do
        local name; name="$(basename "$snap")"
        case "$name" in *.partial) rm -rf "$snap"; continue ;; esac
        i=$((i + 1))

        if [ "$i" -le "$KEEP_DAILY" ]; then continue; fi

        # 2026-08-11T031500Z → week bucket
        local day="${name%%T*}"
        local week
        week="$(date -j -f "%Y-%m-%d" "$day" "+%Y-%V" 2>/dev/null \
                || date -d "$day" "+%Y-%V" 2>/dev/null || echo "$day")"

        case " $kept_weeks " in
            *" $week "*)
                rm -rf "$snap"
                ;;
            *)
                kept_weeks="$kept_weeks $week"
                local weeks_kept; weeks_kept="$(echo "$kept_weeks" | wc -w | tr -d ' ')"
                if [ "$weeks_kept" -gt "$KEEP_WEEKLY" ]; then rm -rf "$snap"; fi
                ;;
        esac
    done < <(find "$dir" -mindepth 1 -maxdepth 1 -type d | sort -r)
}

prune "$BACKUP_DIR"
[ "$mirrored" -eq 1 ] && prune "$MIRROR"

write_status "true" "$db_count" "$bytes"
log "Done."

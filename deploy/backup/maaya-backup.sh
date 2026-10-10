#!/usr/bin/env bash
#
# Maaya OS — weekly backup, taken while Maaya is stopped.
#
# WHY STOPPED. Every byte of Maaya is a handful of SQLite files under deploy/data, and
# on Everest the containers that write them run inside Colima's virtual machine while
# the files live on the Mac, shared in through virtiofs. File locks do not reliably
# cross that boundary. Twice in October 2026 a program on the Mac opened a database
# that containers had open -- this script's own earlier version (`sqlite3 ... VACUUM
# INTO`) and a database browser -- and both databases were damaged. So this version
# never opens a database anything else has open: it stops the stack, copies the files
# (exactly consistent, because nothing is writing), starts the stack again, and only
# then checks the copies. Downtime is the stop, a copy of a few megabytes, and the start:
# about a minute, early on a Sunday.
#
# THREE RULES THE OLD VERSION BROKE:
#   - One failing database never costs the others. Every good copy is kept; the
#     failures are listed by name with SQLite's own error, never hidden.
#   - The stack is always started again: on success, on failure, on Ctrl-C.
#   - Retention never deletes the newest snapshot that passed every check.
#
# The integrity check also guards the LIVE data: a copy taken from stopped files is the
# live file, so a damaged copy means damaged live data, and the status file says which.
# San's health check reads that file and raises it.
#
# Usage:   bash deploy/backup/maaya-backup.sh
# Install: see README.md (launchd, weekly).

set -uo pipefail

HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO="${MAAYA_REPO:-$(cd "$HERE/../.." && pwd)}"

DATA="${MAAYA_DATA:-$REPO/deploy/data}"
BACKUP_DIR="${MAAYA_BACKUP_DIR:-$REPO/deploy/backups}"
# Tier 2 -- external drive. Optional; an unmounted drive is not an error.
MIRROR="${MAAYA_BACKUP_MIRROR:-}"
# Snapshots kept (weekly runs: 12 is about three months).
KEEP="${MAAYA_KEEP:-12}"
# How often a run is expected. Written into the status file so San's health check
# knows when "no recent backup" is a problem, instead of assuming a nightly job.
EVERY_HOURS="${MAAYA_BACKUP_EVERY_HOURS:-168}"

# Overridable so the script can be tested without Docker or the real sqlite3.
DOCKER="${MAAYA_DOCKER:-docker}"
SQLITE="${MAAYA_SQLITE:-sqlite3}"

STAMP="$(date -u +%Y-%m-%dT%H%M%SZ)"
STARTED_AT="$(date -u +%Y-%m-%dT%H:%M:%SZ)"
START_EPOCH="$(date +%s)"
STATUS_FILE="$DATA/backup-status.json"
DEST="$BACKUP_DIR/$STAMP"

log() { printf '%s  %s\n' "$(date -u +%H:%M:%S)" "$*"; }

json_str() { local s="${1//\\/\\\\}"; s="${s//\"/\\\"}"; s="${s//$'\n'/ }"; printf '%s' "$s"; }

# Written into the data directory, which every container sees at /data -- so San can
# tell you when backups stop or fail. A backup nobody watches is one you discover has
# been broken for three weeks.
write_status() {
    local ok="$1" dbs="$2" bytes="$3" err="${4:-}"
    local secs=$(( $(date +%s) - START_EPOCH ))
    local failed_json="[" sep=""
    for f in "${FAILED[@]+"${FAILED[@]}"}"; do failed_json+="$sep\"$(json_str "$f")\""; sep=","; done
    failed_json+="]"
    mkdir -p "$DATA" 2>/dev/null || true
    cat > "$STATUS_FILE" <<EOF
{
  "lastRunUtc": "$(date -u +%Y-%m-%dT%H:%M:%SZ)",
  "startedUtc": "$STARTED_AT",
  "ok": $ok,
  "databases": $dbs,
  "failed": $failed_json,
  "bytes": $bytes,
  "durationSeconds": $secs,
  "expectedEveryHours": $EVERY_HOURS,
  "mirrored": $([ -n "$MIRROR" ] && [ -d "$MIRROR" ] && echo true || echo false),
  "mirrorPath": "$(json_str "$MIRROR")",
  "snapshot": "$STAMP",
  "error": "$(json_str "$err")"
}
EOF
}

FAILED=()
RUNNING=""
STOPPED=0

# Always start again what was running before, however this script ends.
restart_stack() {
    if [ "$STOPPED" -eq 1 ] && [ -n "$RUNNING" ]; then
        log "Starting Maaya again"
        # shellcheck disable=SC2086
        if ! (cd "$REPO" && "$DOCKER" compose start $RUNNING); then
            log "!! could not start every service -- run: cd \"$REPO\" && docker compose up -d"
            FAILED+=("restart: docker compose start failed")
        fi
        STOPPED=0
    fi
}
trap restart_stack EXIT
trap 'exit 130' INT TERM

fail_run() {   # nothing usable was produced: record it and stop
    log "FATAL: $*"
    restart_stack
    write_status "false" "0" "0" "$*"
    exit 1
}

command -v "$SQLITE" >/dev/null 2>&1 || fail_run "sqlite3 not found (macOS ships it at /usr/bin/sqlite3)."
[ -d "$DATA" ] || fail_run "data directory not found: $DATA"

# One run at a time. A second copy started by hand while launchd runs one would stop
# the stack under it.
LOCK="$BACKUP_DIR/.lock"
mkdir -p "$BACKUP_DIR"
if ! mkdir "$LOCK" 2>/dev/null; then
    log "Another backup is running (remove $LOCK if it is stale)."
    exit 1
fi
trap 'restart_stack; rmdir "$LOCK" 2>/dev/null' EXIT

log "Maaya backup -> $DEST"
log "  data: $DATA"

# ── 1. Stop ──────────────────────────────────────────────────────────────────
if "$DOCKER" info >/dev/null 2>&1; then
    RUNNING="$(cd "$REPO" && "$DOCKER" compose ps --services --status running 2>/dev/null | tr '\n' ' ')"
    if [ -n "${RUNNING// /}" ]; then
        log "Stopping Maaya ($(echo $RUNNING | wc -w | tr -d ' ') services)"
        STOPPED=1
        if ! (cd "$REPO" && "$DOCKER" compose stop); then
            fail_run "could not stop the stack, so no copy was taken (copying open databases is what damaged them)."
        fi
    else
        log "Nothing is running"
    fi
else
    # Docker (or Colima) is down: then nothing in the stack can have a database open.
    log "Docker is not reachable: nothing can have the databases open, copying as they are"
fi

# ── 2. Copy, while nothing is writing ────────────────────────────────────────
mkdir -p "$DEST"
COPIED=()
while IFS= read -r -d '' db; do
    rel="${db#"$DATA"/}"
    out="$DEST/$rel"
    mkdir -p "$(dirname "$out")"
    # The database and any side file a hard stop could leave behind (a WAL that was not
    # folded in yet holds committed data). The copies are opened only after the stack
    # is running again, and opening them folds the side files in.
    if cp -p "$db" "$out" \
       && { [ ! -f "$db-wal" ] || cp -p "$db-wal" "$out-wal"; } \
       && { [ ! -f "$db-journal" ] || cp -p "$db-journal" "$out-journal"; }; then
        COPIED+=("$rel")
    else
        log "  !! copy FAILED: $rel"
        FAILED+=("$rel: copy failed")
    fi
done < <(find "$DATA" -type f -name '*.db' -print0)

# Uploaded documents and other module storage: ordinary files, just as irreplaceable.
while IFS= read -r -d '' dir; do
    rel="${dir#"$DATA"/}"
    if [ -n "$(ls -A "$dir" 2>/dev/null)" ]; then
        mkdir -p "$DEST/$rel"
        cp -R "$dir/." "$DEST/$rel/" || FAILED+=("$rel/: copy failed")
    fi
done < <(find "$DATA" -type d -name storage -print0)

# ── 3. Start again, before the slow part ─────────────────────────────────────
restart_stack

[ "${#COPIED[@]}" -gt 0 ] || fail_run "no databases found under $DATA -- refusing to record an empty backup."

# ── 4. Check every copy ──────────────────────────────────────────────────────
ok_count=0
for rel in "${COPIED[@]}"; do
    out="$DEST/$rel"
    # stderr included on purpose: SQLite's own words are the diagnosis.
    check="$("$SQLITE" "$out" "PRAGMA integrity_check;" 2>&1 | tr -d '\r' | head -5 | tr '\n' ' ' | sed 's/ *$//')"
    if [ "$check" = "ok" ]; then
        log "  ok  $rel"
        ok_count=$((ok_count + 1))
    else
        log "  !! DAMAGED: $rel -- $check"
        FAILED+=("$rel: $check")
    fi
    rm -f "$out-shm"
done

bytes="$(du -sk "$DEST" | awk '{print $1 * 1024}')"
if [ "${#FAILED[@]}" -eq 0 ]; then
    echo "ok" > "$DEST/RESULT"
    log "Snapshot complete: $ok_count database(s), $((bytes / 1024)) KiB"
else
    printf '%s\n' "failed" "${FAILED[@]}" > "$DEST/RESULT"
    log "Snapshot kept with problems: $ok_count good, ${#FAILED[@]} failed (listed in $DEST/RESULT)"
fi

# ── 5. Tier 2: external drive ────────────────────────────────────────────────
mirrored=0
if [ -n "$MIRROR" ]; then
    if [ -d "$MIRROR" ]; then
        if cp -R "$DEST" "$MIRROR/"; then log "Mirrored to $MIRROR"; mirrored=1
        else log "!! mirror copy failed -- tier 1 snapshot is still good"; fi
    else
        log "!! mirror path not mounted ($MIRROR) -- tier 1 only for this run"
    fi
fi

# ── 6. Retention ─────────────────────────────────────────────────────────────
# Keep the newest KEEP snapshots, and never delete the newest one that passed every
# check, however old: if the database has been damaged for months, the last good copy
# is the one that matters.
prune() {
    local dir="$1" last_good="" i=0 snap name
    [ -d "$dir" ] || return 0
    while IFS= read -r snap; do
        if [ -z "$last_good" ] && [ "$(head -1 "$snap/RESULT" 2>/dev/null)" = "ok" ]; then last_good="$snap"; fi
    done < <(find "$dir" -mindepth 1 -maxdepth 1 -type d -name '20*' | sort -r)
    while IFS= read -r snap; do
        i=$((i + 1))
        [ "$i" -le "$KEEP" ] && continue
        [ "$snap" = "$last_good" ] && continue
        rm -rf "$snap"
    done < <(find "$dir" -mindepth 1 -maxdepth 1 -type d -name '20*' | sort -r)
}
prune "$BACKUP_DIR"
[ "$mirrored" -eq 1 ] && prune "$MIRROR"

if [ "${#FAILED[@]}" -eq 0 ]; then
    write_status "true" "$ok_count" "$bytes"
    log "Done."
else
    write_status "false" "$ok_count" "$bytes" "${#FAILED[@]} problem(s): ${FAILED[*]}"
    log "Done, with problems."
    exit 2
fi

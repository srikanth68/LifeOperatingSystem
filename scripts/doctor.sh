#!/usr/bin/env bash
# Maaya deploy doctor — "is it actually working?", answered in one command.
#
#   ./scripts/doctor.sh                  # check Everest over Meshnet
#   ./scripts/doctor.sh localhost        # check from the Mac itself
#
# Why this exists: the stack has twice failed in a way that looked like something
# else entirely. A full disk surfaced as "mount callback failed"; nginx caching dead
# container IPs surfaced as every module offline and the login PIN pad vanishing —
# while every module was healthy on its own port the whole time. Both cost hours of
# reasoning about the wrong subsystem.
#
# The trick that separates them is comparing each module TWO ways: through nginx, and
# direct to its published port. Those two numbers together name the fault:
#
#   nginx    direct   meaning
#   ------   ------   -------------------------------------------------------------
#   up       up       healthy
#   502      up       nginx holds a stale IP  -> docker compose restart frontend
#   down     down     the container is genuinely down -> docker compose logs <mod>
#   up       down     published port not bound (rare; check the ports: mapping)
#   none     up       nothing answered via nginx -> the network between you and the
#                     box, NOT a stale upstream. Stale DNS makes a live nginx say 502;
#                     silence says the request never arrived.
#
# Read-only: every request is a GET, nothing is written, nothing restarted. Safe to
# run against a live system at any time, including from Windows via Git Bash.

set -uo pipefail

HOST="${1:-100.126.41.41}"
WEB_PORT="${WEB_PORT:-3000}"
TIMEOUT="${TIMEOUT:-12}"

# module:port — the published host port from docker-compose.yml.
MODULES="vault:5000 vitara:5100 aasthi:5200 san:5300 sutra:5400 northstar:5500 karma:5600 nexus:5700"

red()   { printf '\033[31m%s\033[0m' "$1"; }
green() { printf '\033[32m%s\033[0m' "$1"; }
yellow(){ printf '\033[33m%s\033[0m' "$1"; }
dim()   { printf '\033[2m%s\033[0m' "$1"; }

problems=0
stale_dns=0

# Prints the HTTP status, or 000 when nothing answered at all. A connection refused
# and a 500 are different diagnoses, so they must not collapse into one value.
code_for() {
  local out
  out=$(curl -s -o /dev/null -w '%{http_code}' --max-time "$TIMEOUT" "$1" 2>/dev/null)
  # curl reports 000 when nothing answered. One retry: measured over a Meshnet tunnel
  # that had just reconnected, a single slow request timed out and an entire healthy
  # module got reported as a stale-DNS fault. A checker that cries wolf is worse than
  # no checker, so a no-answer has to happen twice before it counts.
  if [ -z "$out" ] || [ "$out" = "000" ]; then
    sleep 1
    out=$(curl -s -o /dev/null -w '%{http_code}' --max-time "$TIMEOUT" "$1" 2>/dev/null)
  fi
  echo "${out:-000}"
}

# Nothing answered at all: a timeout or a refused connection, NOT an error from a
# running server. Kept separate from a 5xx because they point somewhere completely
# different — one is the network between here and the box, the other is the box's proxy.
noanswer() { [ "$1" = "000" ]; }

# 401 counts as ALIVE, deliberately. Every module runs a fallback authorization policy
# that answers 401 for anything without a token — including routes that don't exist.
# So 401 proves the app is up and serving, which is exactly the question here, and
# avoids needing a token just to run a health check.
alive() { [ "$1" = "200" ] || [ "$1" = "204" ] || [ "$1" = "401" ] || [ "$1" = "404" ]; }

echo
echo "Maaya doctor — $HOST"
dim "  nginx :$WEB_PORT   direct :<module port>"; echo
echo

# ── The SPA itself ────────────────────────────────────────────────────────────
spa=$(code_for "http://$HOST:$WEB_PORT/")
if alive "$spa"; then
  printf '  %s  frontend (nginx)        %s\n' "$(green ' ok ')" "$(dim "HTTP $spa")"
else
  printf '  %s  frontend (nginx)        %s\n' "$(red 'DOWN')" "HTTP $spa"
  echo
  red "  nginx is not answering — nothing else can be trusted below."; echo
  dim "  Is the Mac awake? Is Docker running? → docker compose ps"; echo
  exit 1
fi

# ── Each module, both ways ────────────────────────────────────────────────────
for entry in $MODULES; do
  mod="${entry%%:*}"
  port="${entry##*:}"

  via=$(code_for "http://$HOST:$WEB_PORT/svc/$mod/api/health")
  dir=$(code_for "http://$HOST:$port/api/health")

  if alive "$via" && alive "$dir"; then
    printf '  %s  %-22s %s\n' "$(green ' ok ')" "$mod" "$(dim "nginx $via / direct $dir")"
  elif alive "$dir" && noanswer "$via"; then
    # Stale DNS makes a LIVE nginx answer 502; silence is not that. Reporting this
    # as stale sends someone restarting containers to fix their own network.
    printf '  %s  %-22s %s\n' "$(yellow 'NET  ')" "$mod" "direct $dir but no answer via nginx — network path, not the container"
    problems=$((problems + 1))
  elif alive "$dir" && ! alive "$via"; then
    printf '  %s  %-22s %s\n' "$(yellow 'STALE')" "$mod" "nginx $via but direct $dir — nginx has a dead IP cached"
    stale_dns=$((stale_dns + 1))
    problems=$((problems + 1))
  elif alive "$via" && ! alive "$dir"; then
    printf '  %s  %-22s %s\n' "$(yellow 'PORT ')" "$mod" "reachable via nginx but not on :$port — check the ports: mapping"
    problems=$((problems + 1))
  else
    printf '  %s  %-22s %s\n' "$(red 'DOWN')" "$mod" "nginx $via / direct $dir"
    problems=$((problems + 1))
  fi
done

# ── Host-side services (not containers; they run on the Mac itself) ───────────
echo
llama=$(code_for "http://$HOST:8080/props")
if alive "$llama"; then
  # Gemma's audio + vision are what San's voice input and image understanding rest on.
  mods=$(curl -s --max-time "$TIMEOUT" "http://$HOST:8080/props" 2>/dev/null \
         | tr -d ' \n' | grep -o '"modalities":{[^}]*}' || true)
  printf '  %s  %-22s %s\n' "$(green ' ok ')" "llama.cpp (Gemma)" "$(dim "${mods:-HTTP $llama}")"
  case "$mods" in
    *'"audio":true'*) ;;
    '') ;;
    *) printf '  %s  %-22s %s\n' "$(yellow 'WARN ')" "" "no audio modality — San's mic input will fail" ;;
  esac
else
  printf '  %s  %-22s %s\n' "$(red 'DOWN')" "llama.cpp (Gemma)" "HTTP $llama — chat, voice and images all need this"
  problems=$((problems + 1))
fi

kokoro=$(code_for "http://$HOST:8880/health")
if alive "$kokoro"; then
  printf '  %s  %-22s %s\n' "$(green ' ok ')" "kokoro (TTS)" "$(dim "HTTP $kokoro")"
else
  printf '  %s  %-22s %s\n' "$(yellow 'WARN ')" "kokoro (TTS)" "HTTP $kokoro — San can still chat, but won't speak"
fi

# ── Login, the part that silently degrades ────────────────────────────────────
# The dashboard falls back to the password form on ANY probe failure, so a broken
# probe and a deliberate "use a password" look identical on screen. Worth naming.
echo
probe=$(curl -s --max-time "$TIMEOUT" "http://$HOST:$WEB_PORT/svc/vault/api/auth/probe" 2>/dev/null)
case "$probe" in
  *'"method":"pin"'*)
    printf '  %s  %-22s %s\n' "$(green ' ok ')" "login" "$(dim 'PIN pad')" ;;
  *'"method":"credentials"'*)
    reason=$(echo "$probe" | grep -o '"reason":"[^"]*"' | cut -d'"' -f4)
    printf '  %s  %-22s %s\n' "$(yellow 'WARN ')" "login" "password form${reason:+ ($reason)} — no PIN pad"
    ;;
  *)
    printf '  %s  %-22s %s\n' "$(red 'DOWN')" "login" "probe unreachable — the dashboard will show the password form"
    problems=$((problems + 1)) ;;
esac

# ── Verdict ───────────────────────────────────────────────────────────────────
echo
if [ "$problems" -eq 0 ]; then
  green "  All good."; echo; echo
  exit 0
fi

red "  $problems problem(s)."; echo
if [ "$stale_dns" -gt 0 ]; then
  echo
  echo "  $stale_dns module(s) are healthy but unreachable through nginx — it cached their"
  echo "  addresses at startup and a rebuild moved them. This is not a backend outage:"
  echo
  echo "      docker compose restart frontend"
  echo
fi
exit 1

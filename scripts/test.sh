#!/usr/bin/env bash
# Run every test project in the repo.
#
# There is no .sln, so `dotnet test` at the root finds nothing and quietly succeeds —
# which is worse than failing. This enumerates the projects explicitly and reports a
# per-project line plus a non-zero exit if any of them fail.
#
#   ./scripts/test.sh

set -uo pipefail
cd "$(dirname "$0")/.."

# Every project the Docker image publishes. Compiled BEFORE the tests, because the
# test projects only reference Domain/Application/Infrastructure — so a break in an
# API or Worker project passes every test and then fails the image build minutes
# later on the deploy box. That happened: deleting a class while a controller still
# referenced it was invisible to tsc, vite and all 236 tests, and only surfaced as a
# failed `docker compose up --build`.
BUILD_PROJECTS="
vault/Vault.API/Vault.API.csproj
vault/Vault.Worker/Vault.Worker.csproj
vitara/Vitara.API/Vitara.API.csproj
vitara/Vitara.Worker/Vitara.Worker.csproj
aasthi/Aasthi.API/Aasthi.API.csproj
san/San.API/San.API.csproj
san/San.Worker/San.Worker.csproj
northstar/NorthStar.API/NorthStar.API.csproj
sutra/Sutra.API/Sutra.API.csproj
karma/Karma.API/Karma.API.csproj
nexus/Nexus.API/Nexus.API.csproj
mcp/Maaya.Mcp/Maaya.Mcp.csproj
"

PROJECTS="
shared/Maaya.Auth.Tests/Maaya.Auth.Tests.csproj
san/San.Tests/San.Tests.csproj
karma/Karma.Tests/Karma.Tests.csproj
northstar/NorthStar.Tests/NorthStar.Tests.csproj
vitara/Vitara.Tests/Vitara.Tests.csproj
"

failed=0

echo "  building every project the image publishes..."
for proj in $BUILD_PROJECTS; do
  name=$(basename "$proj" .csproj)
  if [ ! -f "$proj" ]; then
    printf '  [33mSKIP[0m  %-20s (not found)
' "$name"
    continue
  fi
  if out=$(dotnet build "$proj" -v q --nologo 2>&1); then
    printf '  [32m ok [0m  %-20s builds
' "$name"
  else
    printf '  [31mFAIL[0m  %-20s
' "$name"
    echo "$out" | grep -E "error" | head -5 | sed 's/^/        /'
    failed=$((failed + 1))
  fi
done
echo

for proj in $PROJECTS; do
  name=$(basename "$proj" .csproj)
  if [ ! -f "$proj" ]; then
    printf '  \033[33mSKIP\033[0m  %-20s (not found)\n' "$name"
    continue
  fi
  line=$(dotnet test "$proj" -v q --nologo 2>&1 | grep -E 'Passed!|Failed!' | tail -1)
  case "$line" in
    Passed*) printf '  \033[32m ok \033[0m  %-20s %s\n' "$name" "$(echo "$line" | sed 's/^Passed! *- *//; s/ - [^ ]*\.dll.*//')" ;;
    *)       printf '  \033[31mFAIL\033[0m  %-20s %s\n' "$name" "${line:-no result}"; failed=$((failed + 1)) ;;
  esac
done

echo
if [ "$failed" -eq 0 ]; then
  printf '\033[32m  All projects build, all suites pass.\033[0m\n\n'
  exit 0
fi
printf '\033[31m  %d project(s)/suite(s) failed.\033[0m\n\n' "$failed"
exit 1

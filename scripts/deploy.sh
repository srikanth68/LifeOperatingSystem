#!/usr/bin/env bash
# Deploy a Maaya tarball on Everest. Run this ON the Mac.
#
#   ./scripts/deploy.sh ~/Downloads/maaya-17629b2.tar.gz
#
# Extracts over the live checkout and rebuilds. deploy/env/ and deploy/data/ are not
# in the archive, so your secrets and databases are untouched by the extraction.

set -euo pipefail

TARBALL="${1:-}"
REPO="${MAAYA_REPO:-$HOME/Documents/maaya}"

if [ -z "$TARBALL" ] || [ ! -f "$TARBALL" ]; then
  echo "usage: $0 <tarball>"; exit 2
fi

# A build needs several GB for intermediate layers. Checking first turns a ten-minute
# failure that ends in "no space left on device" into a one-second answer -- which is
# exactly how a full disk cost an evening once already.
#
# The space that matters is Docker's, not the Mac's. On Everest Docker runs inside
# Colima's VM, which has its own fixed disk. This check used to read the host
# filesystem, found plenty free, passed -- and the build then died inside the VM with
# "no space left on device" while writing a NuGet package into an image layer. Ask the
# VM when there is one.
if command -v colima >/dev/null 2>&1 && colima status >/dev/null 2>&1; then
  FREE_GB=$(colima ssh -- df -BG /var/lib/containerd 2>/dev/null | awk 'NR==2 {gsub("G","",$4); print $4}')
  WHERE="Colima VM"
else
  FREE_GB=$(df -g "$REPO" 2>/dev/null | awk 'NR==2 {print $4}')
  WHERE="host"
fi
if [ -n "${FREE_GB:-}" ] && [ "$FREE_GB" -lt 8 ]; then
  echo "only ${FREE_GB}GB free on the ${WHERE} — reclaim first, then re-run:"
  echo "    docker builder prune -af && docker image prune -f && docker container prune -f"
  exit 1
fi

cd "$REPO"
echo "==> extracting $(basename "$TARBALL")"
tar -xzf "$TARBALL"

echo "==> building"
docker compose up -d --build

echo
echo "==> running:"
cat VERSION 2>/dev/null || echo "  (no VERSION — tarball predates scripts/release.sh)"
echo
docker compose ps --format 'table {{.Service}}\t{{.Status}}'

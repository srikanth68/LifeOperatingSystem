#!/usr/bin/env bash
# Build a deploy tarball that knows what it is.
#
#   ./scripts/release.sh              # tarball of HEAD into ../
#   ./scripts/release.sh /tmp/out     # somewhere else
#
# Maaya is deployed by copying a tarball to the box and rebuilding there, not by
# pulling a branch. Nothing on the running machine therefore knows which commit it
# is running, and that has cost real time twice: once when a tarball reused an
# earlier filename and a stale build was deployed and then debugged as if it were
# the new one, and once when a whole session's fixes were discussed without anyone
# being able to say which of them were live.
#
# So the archive carries a VERSION file, the Dockerfile copies it to /app/VERSION,
# and every module -- they all share the one image -- can answer the question:
#
#     docker compose exec san cat /app/VERSION
#
# The filename is stamped with the commit too, so two tarballs are never confusable
# in a downloads folder.

set -euo pipefail
cd "$(dirname "$0")/.."

OUTDIR="${1:-..}"
mkdir -p "$OUTDIR"

if ! git diff --quiet || ! git diff --cached --quiet; then
  echo "warning: working tree is dirty — the tarball is built from HEAD, not from your"
  echo "         uncommitted changes. Commit first if that is not what you want."
  echo
fi

HASH="$(git rev-parse --short HEAD)"
SUBJECT="$(git log -1 --pretty=%s)"
STAMP="$(date -u +%Y-%m-%dT%H:%M:%SZ)"

# Written into the working tree only long enough to be archived, then removed:
# a tracked VERSION would conflict on every release and a stale one is worse than none.
cat > VERSION <<EOF
commit:  $HASH
subject: $SUBJECT
built:   $STAMP
EOF
trap 'rm -f VERSION' EXIT

TARBALL="$OUTDIR/maaya-$HASH.tar.gz"

# --add-file puts the untracked VERSION into an otherwise clean archive of HEAD.
git archive --format=tar.gz --add-file=VERSION -o "$TARBALL" HEAD

echo "$TARBALL"
echo
cat VERSION

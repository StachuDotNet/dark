#!/usr/bin/env bash
# Automatic-daemon e2e: connect a peer, start the background sync daemon, author in the peer, and assert
# the local instance AUTO-converges with no manual pull (the "set up once, then automatic" UX). Then stop.
# Run: docker exec peaceful_knuth bash -lc 'cd /home/dark/app && ./scripts/sync-daemon-e2e.sh'
set -euo pipefail
cd "$(dirname "$0")/.."
RUNDIR="${DARK_CONFIG_RUNDIR:-$(pwd)/rundir}"
SRC="$RUNDIR/data.db"
A="$RUNDIR/daemon-A.db"
B="$RUNDIR/daemon-B.db"
LOC="Stachu.DaemonE2E.v"
strip() { sed 's/\x1b\[[0-9;]*m//g'; }
cli() { local db="$1"; shift; DARK_CONFIG_DB_NAME="$(basename "$db")" ./scripts/run-cli "$@"; }
cleanup() { cli "$B" apps stop sync >/dev/null 2>&1 || true; rm -f "$A" "$B"; }
trap cleanup EXIT

cp "$SRC" "$A"; cp "$SRC" "$B"
cli "$B" sync connect "$A" >/dev/null 2>&1
cli "$B" apps start sync >/dev/null 2>&1

# author in A while the daemon polls on B
cli "$A" login Stachu >/dev/null 2>&1
cli "$A" val "$LOC" = 77 >/dev/null 2>&1
echo y | cli "$A" commit -m daemon >/dev/null 2>&1

# poll for auto-convergence (daemon interval ~3s; allow up to ~16s)
ok=0
for i in $(seq 1 8); do
  if cli "$B" view "$LOC" 2>/dev/null | strip | grep -q "77"; then ok=1; break; fi
  sleep 2
done

if [ "$ok" = "1" ]; then
  echo "PASS: B auto-synced via the daemon ($LOC = 77, no manual pull)"
else
  echo "FAIL: B did not auto-sync within the window"; exit 1
fi
echo "SYNC DAEMON E2E GREEN"

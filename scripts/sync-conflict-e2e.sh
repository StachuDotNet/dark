#!/usr/bin/env bash
# Conflict convergence test: two instances concurrently bind the SAME location to different values;
# after syncing (either direction), both converge to the SAME winner (last-writer-wins by origin_ts).
# The core auto-resolution is inherited from `stable`'s origin_ts-ordered fold — this pins the behavior.
# Run: docker exec peaceful_knuth bash -lc 'cd /home/dark/app && ./scripts/sync-conflict-e2e.sh'
set -euo pipefail
cd "$(dirname "$0")/.."
RUNDIR="${DARK_CONFIG_RUNDIR:-$(pwd)/rundir}"
SRC="$RUNDIR/data.db"
A="$RUNDIR/conflict-A.db"
B="$RUNDIR/conflict-B.db"
LOC="Stachu.ConflictE2E.x"
strip() { sed 's/\x1b\[[0-9;]*m//g'; }
cli() { local db="$1"; shift; DARK_CONFIG_DB_NAME="$(basename "$db")" ./scripts/run-cli "$@"; }
valof() { cli "$1" view "$LOC" 2>/dev/null | strip | grep -oE 'x = [0-9]+' | grep -oE '[0-9]+' | tail -1; }
cleanup() { rm -f "$A" "$B"; }
trap cleanup EXIT

cp "$SRC" "$A"; cp "$SRC" "$B"

# A binds x=1 first, then B binds x=2 (later -> B has the greater origin_ts -> B should win everywhere)
cli "$A" login Stachu >/dev/null 2>&1; cli "$A" val "$LOC" = 1 >/dev/null 2>&1; echo y | cli "$A" commit -m a >/dev/null 2>&1
cli "$B" login Stachu >/dev/null 2>&1; cli "$B" val "$LOC" = 2 >/dev/null 2>&1; echo y | cli "$B" commit -m b >/dev/null 2>&1

# sync BOTH directions
cli "$B" sync pull "$A" >/dev/null 2>&1   # A's x=1 into B
cli "$A" sync pull "$B" >/dev/null 2>&1   # B's x=2 into A

va=$(valof "$A"); vb=$(valof "$B")
echo "after bidirectional sync: A.x=$va  B.x=$vb  (expect both = 2, the later writer)"
if [ "$va" = "2" ] && [ "$vb" = "2" ]; then
  echo "PASS: conflict converged — both instances agree on the LWW winner (x=2)"
else
  echo "FAIL: divergent after sync (A=$va B=$vb)"; exit 1
fi
echo "SYNC CONFLICT E2E GREEN ✓"

#!/usr/bin/env bash
# Incremental sync: pull, author more, pull again — the second pull folds only the NEW ops and keeps the old
# ones (exactly what the autosync daemon does every poll). Then a no-op pull folds nothing.
# Run in the container: docker exec peaceful_knuth bash -lc 'cd /home/dark/app && ./scripts/sync-incremental-e2e.sh'
set -euo pipefail
cd "$(dirname "$0")/.."
RUNDIR="${DARK_CONFIG_RUNDIR:-$(pwd)/rundir}"
A="$RUNDIR/inc-A.db"; B="$RUNDIR/inc-B.db"
strip() { sed 's/\x1b\[[0-9;]*m//g'; }
cli() { local db="$1"; shift; DARK_CONFIG_DB_NAME="$(basename "$db")" ./scripts/run-cli "$@"; }
cleanup() { rm -f "$A" "$B"; }
trap cleanup EXIT

cp "$RUNDIR/data.db" "$A"; cp "$RUNDIR/data.db" "$B"
cli "$A" login Stachu >/dev/null 2>&1

# round 1 — author v1, pull, converge
cli "$A" val Stachu.Inc.one = 11 >/dev/null 2>&1; echo y | cli "$A" commit -m one >/dev/null 2>&1
cli "$B" sync pull "$A" >/dev/null 2>&1
cli "$B" view Stachu.Inc.one 2>/dev/null | strip | grep -q "= 11" || { echo "FAIL: v1 not synced"; exit 1; }
echo "PASS: v1 synced"

# round 2 — author v2, pull again: gets the new op, keeps the old
cli "$A" val Stachu.Inc.two = 22 >/dev/null 2>&1; echo y | cli "$A" commit -m two >/dev/null 2>&1
out=$(cli "$B" sync pull "$A" 2>/dev/null | strip)
cli "$B" view Stachu.Inc.two 2>/dev/null | strip | grep -q "= 22" || { echo "FAIL: v2 not synced on 2nd pull"; exit 1; }
cli "$B" view Stachu.Inc.one 2>/dev/null | strip | grep -q "= 11" || { echo "FAIL: v1 lost after 2nd pull"; exit 1; }
echo "PASS: incremental pull got v2, kept v1 ($out)"

# round 3 — no new authoring: idempotent no-op
out3=$(cli "$B" sync pull "$A" 2>/dev/null | strip)
echo "$out3" | grep -q "pulled 0 change" || { echo "FAIL: no-op pull not idempotent ($out3)"; exit 1; }
echo "PASS: no-op pull idempotent"

echo "SYNC INCREMENTAL E2E GREEN ✓"

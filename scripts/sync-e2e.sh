#!/usr/bin/env bash
# End-to-end 2-instance sync test over the FILE transport (the thin-slice milestone).
# Author a value in instance A, Darklang.Sync.pullFile into instance B, assert B converges + idempotency.
# Run in the container: docker exec peaceful_knuth bash -lc 'cd /home/dark/app && ./scripts/sync-e2e.sh'
set -euo pipefail
cd "$(dirname "$0")/.."
RUNDIR="${DARK_CONFIG_RUNDIR:-$(pwd)/rundir}"
SRC="$RUNDIR/data.db"
A="$RUNDIR/sync-e2e-A.db"
B="$RUNDIR/sync-e2e-B.db"
VAL="Stachu.SyncE2E.value"
strip() { sed 's/\x1b\[[0-9;]*m//g'; }
cli() { local db="$1"; shift; DARK_CONFIG_DB_NAME="$(basename "$db")" ./scripts/run-cli "$@"; }
cleanup() { rm -f "$A" "$B"; }
trap cleanup EXIT

cp "$SRC" "$A"; cp "$SRC" "$B"

# --- author + commit a value in instance A ---
cli "$A" login Stachu >/dev/null 2>&1
cli "$A" val "$VAL" = 42 >/dev/null 2>&1
echo y | cli "$A" commit -m "sync-e2e" >/dev/null 2>&1

# --- precondition: B does NOT have it yet ---
if cli "$B" view "$VAL" 2>/dev/null | strip | grep -q "42"; then
  echo "FAIL: B already has $VAL before sync"; exit 1
fi

# --- sync A -> B (copy ops via ATTACH + fold), assert convergence ---
folded=$(cli "$B" eval "Darklang.Sync.pullFile \"$B\" \"$A\"" 2>/dev/null | strip | tail -1)
if cli "$B" view "$VAL" 2>/dev/null | strip | grep -q "42"; then
  echo "PASS: B converged — $VAL synced from A (folded $folded op(s))"
else
  echo "FAIL: B did not converge after sync"; exit 1
fi

# --- idempotency: a second pull folds nothing new ---
folded2=$(cli "$B" eval "Darklang.Sync.pullFile \"$B\" \"$A\"" 2>/dev/null | strip | tail -1)
if [ "$folded2" = "0" ]; then
  echo "PASS: idempotent re-pull folded 0"
else
  echo "FAIL: re-pull folded $folded2 (expected 0)"; exit 1
fi

echo "SYNC E2E GREEN ✓"

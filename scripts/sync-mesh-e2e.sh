#!/usr/bin/env bash
# 3-instance MESH convergence over the file transport. A, B, C each author a distinct value, then run two
# rounds where every instance pulls from the other two; assert all three converge to {a, b, c}. Proves the
# mesh (not just a 2-node line) converges, and that pulling from multiple peers composes.
# Run in the container: docker exec peaceful_knuth bash -lc 'cd /home/dark/app && ./scripts/sync-mesh-e2e.sh'
set -euo pipefail
cd "$(dirname "$0")/.."
RUNDIR="${DARK_CONFIG_RUNDIR:-$(pwd)/rundir}"
SRC="$RUNDIR/data.db"
A="$RUNDIR/mesh-A.db"; B="$RUNDIR/mesh-B.db"; C="$RUNDIR/mesh-C.db"
strip() { sed 's/\x1b\[[0-9;]*m//g'; }
cli() { local db="$1"; shift; DARK_CONFIG_DB_NAME="$(basename "$db")" ./scripts/run-cli "$@"; }
cleanup() { rm -f "$A" "$B" "$C"; }
trap cleanup EXIT

cp "$SRC" "$A"; cp "$SRC" "$B"; cp "$SRC" "$C"

# --- each instance authors its own value ---
for db in "$A" "$B" "$C"; do cli "$db" login Stachu >/dev/null 2>&1; done
cli "$A" val Stachu.Mesh.a = 1 >/dev/null 2>&1; echo y | cli "$A" commit -m a >/dev/null 2>&1
cli "$B" val Stachu.Mesh.b = 2 >/dev/null 2>&1; echo y | cli "$B" commit -m b >/dev/null 2>&1
cli "$C" val Stachu.Mesh.c = 3 >/dev/null 2>&1; echo y | cli "$C" commit -m c >/dev/null 2>&1

# --- two mesh rounds: every node pulls from the other two (order-independent convergence) ---
round() {
  cli "$A" sync pull "$B" >/dev/null 2>&1; cli "$A" sync pull "$C" >/dev/null 2>&1
  cli "$B" sync pull "$A" >/dev/null 2>&1; cli "$B" sync pull "$C" >/dev/null 2>&1
  cli "$C" sync pull "$A" >/dev/null 2>&1; cli "$C" sync pull "$B" >/dev/null 2>&1
}
round; round

# --- assert every instance now holds all three values ---
check() {
  local db="$1" label="$2" ok=1
  for pair in "Stachu.Mesh.a:1" "Stachu.Mesh.b:2" "Stachu.Mesh.c:3"; do
    if ! cli "$db" view "${pair%:*}" 2>/dev/null | strip | grep -q "= ${pair#*:}"; then
      echo "FAIL: instance $label is missing ${pair%:*}"; ok=0
    fi
  done
  [ $ok -eq 1 ] && echo "PASS: instance $label converged to {a,b,c}"
  [ $ok -eq 1 ]
}
check "$A" A && check "$B" B && check "$C" C

echo "SYNC MESH E2E GREEN ✓"

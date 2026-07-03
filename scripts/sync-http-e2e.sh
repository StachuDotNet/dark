#!/usr/bin/env bash
# Cross-computer HTTP sync e2e: author in A, serve A's db over HTTP (stands in for `tailscale serve`),
# `dark sync connect <url>` + `dark sync` on B, assert B converges over the network. No Dark HttpServer.
set -euo pipefail
cd "$(dirname "$0")/.."
RUNDIR="${DARK_CONFIG_RUNDIR:-$(pwd)/rundir}"
A="$RUNDIR/httpe2e-A.db"; B="$RUNDIR/httpe2e-B.db"; LOC="Stachu.HttpE2E.v"; PORT=9219
strip() { sed 's/\x1b\[[0-9;]*m//g'; }
cli() { local db="$1"; shift; DARK_CONFIG_DB_NAME="$(basename "$db")" ./scripts/run-cli "$@"; }
cleanup() { pkill -f "http.server $PORT" 2>/dev/null || true; rm -f "$A" "$B" /tmp/httpe2e/peer.db; }
trap cleanup EXIT
cp "$RUNDIR/data.db" "$A"; cp "$RUNDIR/data.db" "$B"
cli "$A" login Stachu >/dev/null 2>&1; cli "$A" val "$LOC" = 42 >/dev/null 2>&1; echo y | cli "$A" commit -m e >/dev/null 2>&1
mkdir -p /tmp/httpe2e && cp "$A" /tmp/httpe2e/peer.db
(cd /tmp/httpe2e && nohup python3 -m http.server $PORT >/dev/null 2>&1 &); sleep 2
cli "$B" sync connect "http://127.0.0.1:$PORT/peer.db" >/dev/null 2>&1
cli "$B" sync >/dev/null 2>&1
if cli "$B" view "$LOC" 2>/dev/null | strip | grep -q "42"; then
  echo "PASS: B converged over HTTP ($LOC synced across the network, no Dark HttpServer)"
else
  echo "FAIL: B did not converge over HTTP"; exit 1
fi
echo "SYNC HTTP E2E GREEN"

#!/bin/bash
# usage: run-round.sh <binary> <label> <task-list> [attempts]
#   binary     a darklang executable (e.g. bins/main/darklang, or a clis/ build)
#   label      names the results: results/<label>.jsonl and results/<label>/<task>-<n>/
#   task-list  a file with one task per line (lists/*.txt), or a comma list
#   attempts   per task, default 1 (0: only set up bins/<label>/dark)
# env: PAR (parallel attempts, default 3), MODEL (default claude-opus-5-5)
set -euo pipefail
B=$(cd "$(dirname "$0")" && pwd)
bin=$(realpath "$1"); label=$2; list=$3; attempts=${4:-1}
[ -x "$bin" ] || { echo "not executable: $bin"; exit 1; }
if [ -f "$list" ]; then tasks=$(grep -v '^\s*#' "$list" | grep .); elif [ -f "$B/lists/$list.txt" ]; then tasks=$(grep -v '^\s*#' "$B/lists/$list.txt" | grep .); else tasks=$(tr , '\n' <<<"$list"); fi
mkdir -p "$B/bins/$label" "$B/results/$label" /home/stachu/code/bench-loop-ws
if [ "$bin" != "$B/bins/$label/darklang" ]; then
  cp "$bin" "$B/bins/$label/darklang.new" && mv "$B/bins/$label/darklang.new" "$B/bins/$label/darklang"
  [ -f "$bin.mode" ] && cp "$bin.mode" "$B/bins/$label/darklang.mode"
fi
cat > "$B/bins/$label/dark" <<W
#!/bin/sh
# give each Dark call the attempt's private store folder as its HOME
case "\${DARK_CONFIG_RUNDIR:-}" in
  /home/stachu/code/bench-loop-ws/*/.darklang) ;;
  *) echo "bench wrapper: DARK_CONFIG_RUNDIR is '\${DARK_CONFIG_RUNDIR:-}', not a bench store; refusing" >&2; exit 97 ;;
esac
HOME="\${DARK_CONFIG_RUNDIR%/.darklang}" exec $B/bins/$label/darklang "\$@"
W
chmod +x "$B/bins/$label/dark"
[ "$attempts" = 0 ] && exit 0  # just the wrapper (run-ab.sh)
jobs=()
for t in $tasks; do for n in $(seq 1 "$attempts"); do jobs+=("$t $n"); done; done
echo "$(date +%T) $label: ${#jobs[@]} attempts, ${PAR:-3} at a time, $(sha256sum "$bin" | cut -c1-12)"
printf '%s\n' "${jobs[@]}" | xargs -P "${PAR:-3}" -L1 bash "$B/one.sh" "$label"
echo "$(date +%T) $label done"
node "$B/summary.mjs" "$B/results/$label.jsonl"

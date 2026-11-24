#!/bin/bash
# usage: run-ab.sh <binA> <labelA> <binB> <labelB> <task-list> [attempts]
# Like run-round.sh for two binaries at once, with their attempts interleaved (A t1#1, B t1#1, A t2#1, ...)
# so machine load lands on both alike. Ends with summary.mjs A B.
set -euo pipefail
B=$(cd "$(dirname "$0")" && pwd)
la=$2; lb=$4; list=$5; attempts=${6:-1}
"$B/run-round.sh" "$1" "$la" "$list" 0 > /dev/null
"$B/run-round.sh" "$3" "$lb" "$list" 0 > /dev/null
if [ -f "$list" ]; then tasks=$(grep -v '^\s*#' "$list" | grep .); elif [ -f "$B/lists/$list.txt" ]; then tasks=$(grep -v '^\s*#' "$B/lists/$list.txt" | grep .); else tasks=$(tr , '\n' <<<"$list"); fi
jobs=()
for n in $(seq 1 "$attempts"); do for t in $tasks; do jobs+=("$la $t $n" "$lb $t $n"); done; done
echo "$(date +%T) $la vs $lb: ${#jobs[@]} attempts, ${PAR:-3} at a time"
printf '%s\n' "${jobs[@]}" | xargs -P "${PAR:-3}" -L1 bash "$B/one.sh"
echo "$(date +%T) $la vs $lb done"
node "$B/summary.mjs" "$B/results/$la.jsonl" "$B/results/$lb.jsonl"

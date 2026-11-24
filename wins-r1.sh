#!/bin/bash
# round 1: per-win before/after on tuning tasks, then the final whole sets on the tip.
B=$(cd "$(dirname "$0")" && pwd); cd "$B"
need() { until [ -f "bins/$1/commit" ] && [ "$(cat bins/$1/commit)" = "$(git -C ~/code/dark/bench-loop rev-parse "$2")" ]; do sleep 30; done; }
br=bench-round-1
sha() { git -C ~/code/dark/bench-loop rev-list --reverse "$(git -C ~/code/dark/bench-loop merge-base $br upstream/main)..$br" | sed -n "${1}p"; }
win() { # win <k> <tasks> <attempts>
  local k=$1 prev=$(( $1 - 1 ))
  need c$prev "$( [ $prev = 0 ] && git -C ~/code/dark/bench-loop merge-base $br upstream/main || sha $prev )"
  need c$k "$(sha $k)"
  ./run-round.sh bins/c$prev/darklang w$k-before "$2" "$3" > results/w$k-before.out 2>&1
  ./run-round.sh bins/c$k/darklang w$k-after "$2" "$3" > results/w$k-after.out 2>&1
}
win 1 inventory-api,health-api 2
win 2 live-greeting 3
win 3 link-checker 2
win 4 inventory-api 2
win 5 health-api,webhook-receiver 2
win 6 flashcards,sum-column,grep-lite 3
./run-round.sh bins/c6/darklang final-tune tune 1 > results/final-tune.out 2>&1
./run-round.sh bins/c6/darklang final-measure measure 1 > results/final-measure.out 2>&1
echo ROUND DONE

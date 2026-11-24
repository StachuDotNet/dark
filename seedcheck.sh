#!/bin/bash
# check the seeds reproduce their fixtures' expected output on a binary
B=/home/stachu/code/dark/bench-loop.bench; DARK=$B/bins/${1:-base-tune}/dark
for seed in invoice salesreport; do
  d=$(mktemp -d /home/stachu/code/bench-loop-ws/seedcheck-XXXX); mkdir -p $d/.darklang $d/ws; cd $d/ws
  export DARK_CONFIG_RUNDIR=$d/.darklang
  cp -r $B/tasks/seeds/$seed/dark/. .; cp -r $B/tasks/seeds/$seed/fixtures .; rm -f setup.sh
  DARK=$DARK sh $B/tasks/seeds/$seed/dark/setup.sh > /dev/null 2>&1 || echo "$seed setup FAILED"
  script=$( [ $seed = invoice ] && echo receipt.dark || echo salesreport.dark )
  for inp in fixtures/*.json fixtures/*.csv; do
    [ -f "$inp" ] || continue
    exp=${inp%.*}.expected
    out=$($DARK run --allow "file read $PWD" $script "$inp" 2>&1)
    if [ "$out" = "$(cat $exp)" ]; then echo "$seed $inp ok"; else echo "$seed $inp DIFF"; diff <(echo "$out") $exp | head -5; fi
  done
  cd /; rm -rf $d
done

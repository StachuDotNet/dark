#!/bin/bash
# round 4 baseline: main on tune (1 attempt), then measure (3)
B=/home/stachu/code/dark/bench-loop.bench; cd $B
( while true; do echo "$(date +%T) $(cut -d" " -f1-3 /proc/loadavg)"; sleep 60; done ) > results/r4-load.log 2>&1 & LP=$!
./run-round.sh bins/r4main/darklang r4base-tune tune 1 > results/r4base-tune.out 2>&1
./run-round.sh bins/r4main/darklang r4base-measure measure 3 > results/r4base-measure.out 2>&1
kill $LP
echo "$(date +%T) ALL DONE" >> results/r4base-measure.out

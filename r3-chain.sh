#!/bin/bash
# round 3: main vs tip on the tasks the wins came from (2 attempts), then the measure set (3), interleaved
B=/home/stachu/code/dark/bench-loop.bench; cd $B
( while true; do echo "$(date +%T) $(cut -d" " -f1-3 /proc/loadavg)"; sleep 60; done ) > results/r3-load.log 2>&1 & LP=$!
./run-ab.sh bins/r3main/darklang r3w-before bins/r3tip/darklang r3w-after lists/r3wins.txt 2 > results/r3w.out 2>&1
./run-ab.sh bins/r3main/darklang r3m-before bins/r3tip/darklang r3m-after measure 3 > results/r3m.out 2>&1
kill $LP
echo "$(date +%T) ALL DONE" >> results/r3m.out

#!/bin/bash
# release 116 vs 115: perf session, then the AI bench measure set (3 attempts) and tune set (1), interleaved
B=/home/stachu/code/dark/bench-loop.bench; R=/tmp/rcb/r116
bash $R/go.sh > $R/go.log 2>&1
cd $B
( while true; do echo "$(date +%T) $(cut -d" " -f1-3 /proc/loadavg)"; sleep 60; done ) > results/rc116-load.log 2>&1 & LP=$!
./run-ab.sh $R/bin/v0.0.115/dark-bin rc115-measure $R/bin/v0.0.116/dark-bin rc116-measure measure 3 > results/rc116-measure.out 2>&1
./run-ab.sh $R/bin/v0.0.115/dark-bin rc115-tune $R/bin/v0.0.116/dark-bin rc116-tune tune 1 > results/rc116-tune.out 2>&1
kill $LP
echo "$(date +%T) ALL DONE" >> results/rc116-tune.out

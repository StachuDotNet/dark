#!/bin/bash
# AI bench 10x round (Nov 23): v0.0.116 vs main on the measure set (3 attempts, interleaved), the new
# held-out-2 tasks on main (3), Python on the measure set (1) for a floor, then main on the tune set (1).
# Every attempt yields to queued suites (BENCH_YIELD) and takes no slot (BENCH_NOSLOT).
B=/home/stachu/code/dark/bench-loop.bench; cd $B
export BENCH_YIELD=1 BENCH_NOSLOT=1 PAR=${PAR:-3}
( while true; do echo "$(date +%T) $(cut -d" " -f1-3 /proc/loadavg) $(grep full /proc/pressure/io)"; sleep 60; done ) > results/r10x-load.log 2>&1 & LP=$!
./run-ab.sh bins/rc116-measure/darklang x116-measure bins/m1123/darklang xmain-measure measure 3 > results/r10x-measure.out 2>&1
./run-round.sh bins/m1123/darklang xmain-new measure2 3 > results/r10x-new.out 2>&1
BENCH_LANG=Python ./run-round.sh bins/m1123/darklang xpy-measure measure 1 > results/r10x-py.out 2>&1
./run-round.sh bins/m1123/darklang xmain-tune tune 1 > results/r10x-tune.out 2>&1
kill $LP
echo "$(date +%T) ALL DONE" >> results/r10x-tune.out

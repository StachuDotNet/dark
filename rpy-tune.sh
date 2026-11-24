#!/bin/bash
# after r10x: Python on the tune set (1 attempt), to pair with the Dark sessions on main (pair.mjs)
B=/home/stachu/code/dark/bench-loop.bench; cd $B
until grep -q "ALL DONE" results/r10x-tune.out 2>/dev/null; do sleep 60; done
BENCH_YIELD=1 BENCH_NOSLOT=1 PAR=2 BENCH_LANG=Python ./run-round.sh bins/m1123/darklang xpy-tune tune 1 > results/rpy-tune.out 2>&1
echo "$(date +%T) ALL DONE" >> results/rpy-tune.out

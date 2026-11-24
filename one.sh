#!/bin/bash
# one.sh <label> <task> <n>: one attempt under a box-wide heavy-job slot
# BENCH_YIELD=1: first wait until no suite is queued and disk IO is calm (yield-gate.sh).
# BENCH_NOSLOT=1 (with BENCH_YIELD): then start without taking a slot, so the bench never holds one
# that a suite queued later would wait for.
B=$(cd "$(dirname "$0")" && pwd)
export DARK_SUITE_SLOTS=${DARK_SUITE_SLOTS:-4} SUITE_SLOT_DIR=$HOME/.cache/darklang/suite-slots
. "$B/_suite-slot"
# a label listed in PAUSE waits while that file names it (another bench run has the box)
while [ -f "$B/PAUSE" ] && grep -qx "$1" "$B/PAUSE"; do sleep 30; done
[ -z "${BENCH_YIELD:-}" ] || "$B/yield-gate.sh"
[ -n "${BENCH_YIELD:-}" ] && [ -n "${BENCH_NOSLOT:-}" ] || suite_slot_take "bench-loop $1 $2#$3" > /dev/null
node "$B/attempt.mjs" "$1" "$2" "$3" >> "$B/results/$1.log" 2>&1
tail -1 "$B/results/$1.log"

#!/bin/bash
# try-seed.sh <bin-label> <seed> [cmds-file]: set the seed up in a throwaway store, then run the commands in cmds-file with $D
B=/home/stachu/code/dark/bench-loop.bench; D=$B/bins/$1/darklang; seed=$2
d=$(mktemp -d /home/stachu/code/bench-loop-ws/seedtry-XXXX); mkdir -p $d/.darklang $d/ws; cd $d/ws
export DARK_CONFIG_RUNDIR=$d/.darklang HOME=$d
cp -r $B/tasks/seeds/$seed/dark/. .; rm -f setup.sh
echo "== setup"; DARK=$D sh -x $B/tasks/seeds/$seed/dark/setup.sh 2>&1 | tail -40
[ -n "$3" ] && { echo "== cmds"; D=$D bash -x "$3" 2>&1; }
cd /; rm -rf $d

#!/bin/bash
# re-grade every graded set with the W696 grader fixes; keep the old rows as <label>.grades.pre-w696.jsonl
cd /home/stachu/code/dark/bench-loop.bench
for l in ab3-m2-cc ab3-m2-da ab3-m2-dg ab3-m2-dt ab3-r3-cc ab3-r3-da x116-measure xmain-measure xmain-new; do
  [ -f results/$l.grades.jsonl ] || continue
  [ -f results/$l.grades.pre-w696.jsonl ] || cp results/$l.grades.jsonl results/$l.grades.pre-w696.jsonl
  rm -f results/$l.grades.jsonl
  node grade.mjs $l > results/$l.regrade.log 2>&1
done
echo "REGRADE DONE $(date +%T)" > results/regrade-w696.done

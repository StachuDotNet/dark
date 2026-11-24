#!/bin/bash
# yield-gate.sh: returns once no suite is waiting for a box-wide slot and disk IO is calm,
# so a bench step never queues ahead of a test suite (major is disk-bound).
Q=$HOME/.cache/darklang/suite-slots/queue
calm=0
while [ $calm -lt ${YIELD_CALM:-3} ]; do
  live=0
  for e in "$Q"/*; do [ -e "$e" ] || continue; flock -n "$e" true 2>/dev/null || live=$((live+1)); done
  full=$(awk "/^full/{split(\$3,a,\"=\"); print int(a[2])}" /proc/pressure/io)
  if [ $live -eq 0 ] && [ "${full:-0}" -lt ${YIELD_IO:-15} ]; then calm=$((calm+1)); else calm=0; fi
  sleep ${YIELD_SLEEP:-20}
done

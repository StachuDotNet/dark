#!/usr/bin/env bash
# Durable wave-2 loop backstop.
#
# WHY: the in-Claude cron is session-only — it dies if the Claude *process* dies
# (crash, reboot; quota usually just pauses and self-resumes). This script is a
# process-independent driver: OS cron runs it every 5 min, it launches one headless
# Claude chunk that reads notes/loop-work/wave2-todo.md and continues the loop.
# State is git + that worklist, so it resumes correctly from a cold start.
#
# ENABLE (only if you want hard-crash resilience; verify headless auth first):
#   crontab -e   # then add:
#   */5 * * * * /home/stachu/code/dark/main/notes/loop-work/loop-watchdog.sh
# DISABLE: remove that crontab line (or `touch /tmp/dark-wave2-loop.STOP`).
#
# NOTE: run this OR the in-session cron, not both at once (they'd double-edit).
# The lock below only guards this script's own overlap.

set -uo pipefail
REPO=/home/stachu/code/dark/main
LOCK=/tmp/dark-wave2-loop.lock
STOP=/tmp/dark-wave2-loop.STOP
LOG="$REPO/notes/loop-work/watchdog.log"

[ -f "$STOP" ] && exit 0
# stale-lock takeover after 30 min (a wedged run shouldn't block forever)
if [ -f "$LOCK" ] && [ "$(( $(date +%s) - $(stat -c %Y "$LOCK" 2>/dev/null || echo 0) ))" -lt 1800 ]; then
  exit 0
fi
date > "$LOCK"
cd "$REPO" || { rm -f "$LOCK"; exit 1; }

PROMPT='Resume the wave-2 loop. Read notes/loop-work/wave2-todo.md fully (How this loop works incl. Recovery, Keep going until Sunday 6pm, Bucket/dependency order) and do ONE large chunk per its rules: top priority pre-S&S then S&S; tight docs not long; include visuals (fake CLI/TUI mockups, dense code, diagrams); sketch PR shapes; enforce the dependency rule; edit only under notes/loop-work/ (outputs/ + wave2-todo.md); verify 0 broken links; commit locally (NEVER push); delete done todos and add Discovered; one-line status. Never touch real pdd-thinking/ or the Obsidian vault. pdd is a research spike — verify codebase claims against main (git show main:path). Keep going until ~6pm Sunday.'

{ echo "=== $(date) watchdog fire ==="; \
  claude -p "$PROMPT" --permission-mode acceptEdits 2>&1; \
  echo "=== exit $? ==="; } >> "$LOG" 2>&1 || true

rm -f "$LOCK"

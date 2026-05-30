#!/usr/bin/env bash
# Durable wave-2 loop backstop — DEFERENTIAL + WARM-CONTEXT.
#
# WHY this exists: the in-Claude cron is session-only. It dies if the Claude
# *process* dies (crash, reboot). Quota exhaustion usually just *pauses* the
# process (it self-resumes on reset), so this is ONLY for a hard process death.
#
# TWO fixes for "a cold headless run loses context and does dumb stuff":
#
#   1. WARM CONTEXT — it does NOT start cold. It RESUMES this exact session
#      (--resume $SESSION), so the headless run inherits the full conversation,
#      not just the worklist file. Same judgment, same nuance.
#
#   2. DEFERENTIAL — it only fires when the live session is GONE. The in-session
#      loop touches $HEARTBEAT every pass. While that heartbeat is fresh
#      (< STALE_S), this script exits immediately and stays out of the way, so it
#      can NEVER double-run against a live session. It takes over only when the
#      heartbeat goes stale (the real session actually died).
#
# So: run the in-session loop (in tmux) as primary; leave this enabled as a
# pure crash/reboot backstop. They no longer conflict.
#
# ENABLE:  crontab -e  →  */5 * * * * /home/stachu/code/dark/main/notes/loop-work/loop-watchdog.sh
# DISABLE: remove that line, or `touch /tmp/dark-wave2-loop.STOP`.

set -uo pipefail
REPO=/home/stachu/code/dark/main
SESSION=0bee6022-83da-41dc-a325-7c5e6a4b2c2c   # this conversation; resumed warm
HEARTBEAT=/tmp/dark-wave2-loop.heartbeat       # touched by the live in-session loop
STALE_S=900                                    # 15 min: live session presumed dead past this
LOCK=/tmp/dark-wave2-loop.lock
STOP=/tmp/dark-wave2-loop.STOP
LOG="$REPO/notes/loop-work/watchdog.log"

[ -f "$STOP" ] && exit 0

# Defer to a live session: fresh heartbeat ⇒ the real loop is handling it.
if [ -f "$HEARTBEAT" ] && [ "$(( $(date +%s) - $(stat -c %Y "$HEARTBEAT" 2>/dev/null || echo 0) ))" -lt "$STALE_S" ]; then
  exit 0
fi

# Guard this script's own overlap (stale-lock takeover after 30 min).
if [ -f "$LOCK" ] && [ "$(( $(date +%s) - $(stat -c %Y "$LOCK" 2>/dev/null || echo 0) ))" -lt 1800 ]; then
  exit 0
fi
date > "$LOCK"
cd "$REPO" || { rm -f "$LOCK"; exit 1; }

PROMPT='Resume the wave-2 loop (the live session appears to have died — you are the crash backstop). Read notes/loop-work/wave2-todo.md fully and do ONE chunk per its rules: pre-S&S then S&S; tight prose but keep code specs + step-by-step; visuals; enforce the dependency rule; main is the source of truth (verify vs git show main:path; pdd is a spike); edit only under notes/loop-work/; touch /tmp/dark-wave2-loop.heartbeat; verify 0 broken links; commit locally (NEVER push); delete done todos. One-line status.'

{ echo "=== $(date) watchdog fire (heartbeat stale) ==="; \
  claude --resume "$SESSION" -p "$PROMPT" --permission-mode acceptEdits 2>&1; \
  echo "=== exit $? ==="; } >> "$LOG" 2>&1 || true

rm -f "$LOCK"

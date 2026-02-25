#!/usr/bin/env bash
# Mock CLI screen for: dark ai chats

set -euo pipefail

# Colors
RESET="\033[0m"
BOLD="\033[1m"
DIM="\033[2m"
ITALIC="\033[3m"
UNDERLINE="\033[4m"

BLACK="\033[30m"
RED="\033[31m"
GREEN="\033[32m"
YELLOW="\033[33m"
BLUE="\033[34m"
MAGENTA="\033[35m"
CYAN="\033[36m"
WHITE="\033[37m"

BG_RED="\033[41m"
BG_GREEN="\033[42m"
BG_YELLOW="\033[43m"
BG_BLUE="\033[44m"
BG_MAGENTA="\033[45m"
BG_CYAN="\033[46m"
BG_WHITE="\033[47m"
BG_BRIGHT_BLACK="\033[100m"

# Get terminal width, default to 110
COLS=$(tput cols 2>/dev/null || echo 110)

hr() {
  printf "${DIM}"
  printf '%.0s─' $(seq 1 "$COLS")
  printf "${RESET}\n"
}

thin_hr() {
  printf "${DIM}"
  printf '%.0s╌' $(seq 1 "$COLS")
  printf "${RESET}\n"
}

status_badge() {
  local status="$1"
  case "$status" in
    running)    printf "${BG_GREEN}${BLACK}${BOLD} RUNNING ${RESET}" ;;
    review)     printf "${BG_YELLOW}${BLACK}${BOLD} REVIEW ${RESET}" ;;
    blocked)    printf "${BG_RED}${WHITE}${BOLD} BLOCKED ${RESET}" ;;
    done)       printf "${BG_BLUE}${WHITE}${BOLD} DONE    ${RESET}" ;;
    paused)     printf "${BG_BRIGHT_BLACK}${WHITE}${BOLD} PAUSED  ${RESET}" ;;
    error)      printf "${BG_RED}${WHITE}${BOLD} ERROR   ${RESET}" ;;
  esac
}

clear

# Header
printf "\n"
printf "  ${BOLD}dark ai chats${RESET}  ${DIM}—  6 threads  •  3 running  •  2 awaiting review${RESET}\n"
printf "\n"
hr

# Column headers
printf "  ${BOLD}${DIM}%-4s${RESET}" "ID"
printf "  ${BOLD}${DIM}%-10s${RESET}" "STATUS"
printf "  ${BOLD}${DIM}%-40s${RESET}" "TOPIC"
printf "  ${BOLD}${DIM}%-8s${RESET}" "BRANCH"
printf "  ${BOLD}${DIM}%-10s${RESET}" "COST"
printf "  ${BOLD}${DIM}%-10s${RESET}" "TOKENS"
printf "  ${BOLD}${DIM}%-8s${RESET}" "TIME"
printf "\n"
hr

# ── Thread 1: running ──
printf "  ${BOLD}${CYAN}#1${RESET}"
printf "    "
status_badge "running"
printf "  "
printf "${BOLD}%-40s${RESET}" "Add binary serialization for sync"
printf "  ${DIM}%-8s${RESET}" "ai/sync"
printf "  ${GREEN}%-10s${RESET}" "\$4.82"
printf "  ${DIM}%-10s${RESET}" "128k"
printf "  ${DIM}%-8s${RESET}" "12m"
printf "\n"
printf "  ${DIM}     ╰─ Currently editing packages/darklang/sync/serialize.dark  •  14 files changed${RESET}\n"
thin_hr

# ── Thread 2: awaiting review ──
printf "  ${BOLD}${CYAN}#2${RESET}"
printf "    "
status_badge "review"
printf "   "
printf "${BOLD}%-40s${RESET}" "Refactor HTTP handler error paths"
printf "  ${DIM}%-8s${RESET}" "ai/errs"
printf "  ${YELLOW}%-10s${RESET}" "\$2.17"
printf "  ${DIM}%-10s${RESET}" "64k"
printf "  ${DIM}%-8s${RESET}" "8m"
printf "\n"
printf "  ${YELLOW}     ╰─ ⚠ Awaiting your review: 3 questions about error propagation strategy${RESET}\n"
thin_hr

# ── Thread 3: running ──
printf "  ${BOLD}${CYAN}#3${RESET}"
printf "    "
status_badge "running"
printf "  "
printf "${BOLD}%-40s${RESET}" "Write tests for package manager"
printf "  ${DIM}%-8s${RESET}" "ai/test"
printf "  ${GREEN}%-10s${RESET}" "\$1.53"
printf "  ${DIM}%-10s${RESET}" "41k"
printf "  ${DIM}%-8s${RESET}" "5m"
printf "\n"
printf "  ${DIM}     ╰─ Running test suite (pass: 12, fail: 2, pending: 6)  •  4 files changed${RESET}\n"
thin_hr

# ── Thread 4: awaiting review ──
printf "  ${BOLD}${CYAN}#4${RESET}"
printf "    "
status_badge "review"
printf "   "
printf "${BOLD}%-40s${RESET}" "Design new DB migration system"
printf "  ${DIM}%-8s${RESET}" "ai/db"
printf "  ${YELLOW}%-10s${RESET}" "\$6.31"
printf "  ${DIM}%-10s${RESET}" "187k"
printf "  ${DIM}%-8s${RESET}" "22m"
printf "\n"
printf "  ${YELLOW}     ╰─ ⚠ Awaiting your review: proposed 2 migration approaches, needs decision${RESET}\n"
thin_hr

# ── Thread 5: running ──
printf "  ${BOLD}${CYAN}#5${RESET}"
printf "    "
status_badge "running"
printf "  "
printf "${BOLD}%-40s${RESET}" "Fix canvas rendering perf regression"
printf "  ${DIM}%-8s${RESET}" "ai/perf"
printf "  ${GREEN}%-10s${RESET}" "\$0.89"
printf "  ${DIM}%-10s${RESET}" "22k"
printf "  ${DIM}%-8s${RESET}" "3m"
printf "\n"
printf "  ${DIM}     ╰─ Profiling render loop, found bottleneck in tree diff  •  1 file changed${RESET}\n"
thin_hr

# ── Thread 6: done ──
printf "  ${BOLD}${CYAN}#6${RESET}"
printf "    "
status_badge "done"
printf "  "
printf "${BOLD}${DIM}%-40s${RESET}" "Update stdlib List docs"
printf "  ${DIM}%-8s${RESET}" "ai/docs"
printf "  ${DIM}%-10s${RESET}" "\$0.34"
printf "  ${DIM}%-10s${RESET}" "9k"
printf "  ${DIM}%-8s${RESET}" "1m"
printf "\n"
printf "  ${DIM}     ╰─ Completed. PR #5601 ready to merge  •  8 files changed${RESET}\n"

hr

# Summary footer
printf "\n"
printf "  ${BOLD}Session totals${RESET}  "
printf "${DIM}│${RESET}  Cost: ${BOLD}\$16.06${RESET}  "
printf "${DIM}│${RESET}  Tokens: ${BOLD}451k${RESET}  "
printf "${DIM}│${RESET}  Uptime: ${BOLD}51m${RESET}  "
printf "${DIM}│${RESET}  Files changed: ${BOLD}31${RESET}"
printf "\n\n"

# Keyboard hints
printf "  ${DIM}[enter]${RESET} open thread   "
printf "${DIM}[r]${RESET} review flagged   "
printf "${DIM}[n]${RESET} new thread   "
printf "${DIM}[k]${RESET} kill thread   "
printf "${DIM}[q]${RESET} quit"
printf "\n\n"

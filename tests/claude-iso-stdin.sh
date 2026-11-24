#!/bin/bash
# The login refresh in claude-iso must not eat the task prompt on stdin (Nov 24: two attempts ended
# "environment blocked" with "Input must be provided either through stdin..." right after a refresh).
# Runs claude-iso against a stub claude in a throwaway bench folder; touches no real login.
set -eu
H=$(cd "$(dirname "$0")/.." && pwd)
T=$(mktemp -d); trap 'rm -rf "$T"' EXIT
mkdir -p "$T/cred" "$T/sbin"
soon=$(( ($(date +%s) + 60) * 1000 ))   # inside the 15-minute window, so claude-iso refreshes first
echo "{\"claudeAiOauth\":{\"expiresAt\":$soon}}" > "$T/cred/latest.json"
echo '{"claudeAiOauth":{"expiresAt":0}}' > "$T/user.json"
cat > "$T/claude" <<'S'
#!/bin/bash
# like claude: with --print, a prompt argument and piped stdin are both read
if [[ " $* " == *" Reply OK "* ]]; then cat > /dev/null; exit 0; fi
printf 'got: %s' "$(cat)"
S
chmod +x "$T/claude"
out=$(printf 'build the thing' | CLAUDE_ISO_BENCH=$T CLAUDE_ISO_BIN=$T/claude CLAUDE_ISO_USER_CRED=$T/user.json "$H/claude-iso" --print)
[ "$out" = "got: build the thing" ] && echo "ok: the prompt survives a refresh" || { echo "FAIL: session got '$out'"; exit 1; }

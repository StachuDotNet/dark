# Known blockers — fix before tonight's 21:00 kickoff

Per round-2 P0 #6: HTTP and UserDB-bound projects must be in tonight's set. The bench has known-real concerns that the user should resolve **before** the run starts. This file lists them with severity + recovery.

---

## 🔴 Blocker 1 — `dark serve` non-TTY startup flakiness

**Probed iter 15 + iter 26**: starting `dark serve <router> --port <port>` from a non-interactive shell (i.e. background mode under the wrapper) succeeds on roughly 1 of 3 attempts. The other 2 attempts: process consumes, log file stays empty, port never binds.

**Severity**: blocks any HTTP project. `http-healthz` (#9 in tonights-queue.md) hits this directly.

**Mitigation today** (per plan §4.4.1): port-poll readiness check (`socket.connect_ex` every 200 ms, 30 s timeout) + retry-on-startup-failure up to 3 times. **This is implemented in the harness wrapper spec but not yet built.** If the wrapper is stood up tonight, include the retry logic; if time-constrained, drop `http-healthz` from the queue and accept the HTTP-coverage gap.

**Permanent fix** (out of scope for tonight): add a `--ready-fd <N>` flag to `dark serve` that writes a single byte to fd N when the listener is bound. The wrapper waits on the fd; deterministic, no flake. ~15 lines of F# in `serve.fs`. Prefer this over polling, eventually.

**Mitigation if not done**: drop `http-healthz` from tonights-queue.md. Document the gap in tomorrow's report.

---

## 🟡 Blocker 2 — `multi set-fork` precondition

**Verified iter 52**: `~/.config/dark-multi/queue.json` has a stale task with the literal error `failed to create branch: GitHub fork not configured. Run: multi set-fork git@github.com:USERNAME/dark.git`. Multi's branch-creation requires a configured fork.

**Severity**: blocks any Dark sweep that creates new branches per run (which is the §4.10 launch-checklist design — every Dark project run gets a fresh branch for isolation, then deleted post-analysis per round-2 P0 #5).

**Mitigation**: the user runs `multi set-fork git@github.com:<your-fork>/dark.git` once before 21:00. Phase A4b in tonight's launch checklist (§4.10) was added at iter 52 to surface this; if the user follows the checklist, this is handled.

**Confirm**: `cat ~/.config/dark-multi/github-fork` should print a non-empty `git@github.com:...` line.

---

## 🟡 Blocker 3 — `pricing.json` must exist

**Per plan §6.0 cost-attribution**: the wrapper refuses to start without `evals/bench/pricing.json`. The file maps each model to its per-million-token rates (input / output / cache_creation / cache_read).

**Severity**: hard requirement. No `pricing.json`, no sweep.

**Mitigation**: the user populates `evals/bench/pricing.json` before 21:00 with current Anthropic Pro/Max-equivalent + OpenAI rates. Seed values in plan §6.0 cost-attribution-formula. **Round-2 note**: even though the bench runs on subscription auth ($0 marginal), the report's "API-equivalent cost" needs these rates to compute. Don't skip.

---

## 🟡 Blocker 4 — `claude --print "ping"` must succeed

**Per plan §4.10 Phase A4 + iter 51 auth correction**: bench uses host-side Claude Code subscription OAuth. `claude --print "ping"` should return a response without prompting for login.

**Severity**: hard requirement. Without auth, every agent run errors immediately.

**Mitigation**: the user runs `claude` interactively once if needed; OAuth's stored credentials carry the bench. **Don't `export ANTHROPIC_API_KEY` for the bench** — that switches to metered API billing.

---

## 🟢 Soft concern — `http-healthz` spec is missing

`http-healthz` is in tonights-queue.md (#9) but doesn't have a fully-shaped spec yet — only a compact entry from iter 2.

**Severity**: not a hard blocker; a future loop iter or the implementer materializes the spec. ~20 min of writing.

**Mitigation**: write the spec before 21:00 (a loop iter could do this). If skipped, drop `http-healthz` from the queue and accept HTTP-coverage gap on tonight's run.

---

## 🟢 Soft concern — `multi bench` Cobra subcommands aren't built

**Per plan §4.8 tonight's-anchor decision**: tonight's launch wraps Multi's *existing* CLI directly (`multi new <branch>`, etc.) without new subcommands. The `multi bench enqueue/score/report/dashboard` subcommands are deferred to Phase 2.

**Severity**: not a blocker. The Python wrapper (`evals/harness/main.py`) drives `multi`'s existing CLI for tonight. No need to extend Multi for the kickoff.

**Mitigation**: none needed. Mention in PR/post-mortem so future runs know which path was taken.

---

## Pre-launch checklist (run before 21:00)

```bash
# 1. Verify Multi
multi --version
cat ~/.config/dark-multi/github-fork  # should print a non-empty fork URL

# 2. Verify Claude Code subscription auth
claude --version
claude --print "ping"  # should return a response without OAuth prompt

# 3. Verify pricing.json
cat evals/bench/pricing.json | jq .  # should have entries for claude-opus-4-7, claude-sonnet-4-6, claude-haiku-4-5

# 4. Decide http-healthz inclusion
ls evals/projects/http-healthz/spec.md  # if missing, drop from tonights-queue.md OR write the spec first

# 5. Confirm Python harness is built
ls evals/harness/main.py
python -m harness --help

# 6. Pre-flight the queue
python -m harness sweep --dry-run --queue tonights-queue.md  # should list 50 attempts (10 projects × 5 langs)
```

If all 6 pass: 21:00 kickoff is green.

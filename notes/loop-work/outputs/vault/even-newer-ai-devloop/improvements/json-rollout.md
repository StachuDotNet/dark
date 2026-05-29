---
title: Cross-cutting --json audit and roll-out
section: 3.1 Discovery
priority: P1
harness_signal: median tokens per run + first-parse-success + constraint-escape attempts
---

# Cross-cutting `--json` audit and roll-out

**Problem**: `--json` exists on 5 commands, missing on 9 of the most-used ones. Verified iter 18 (rerun any of these to confirm):

| Command | `--json`? | Verified by |
|---|---|---|
| `traces list` | ✅ clean JSON per-record | `./scripts/run-cli traces list --json 5` |
| `traces find` | ✅ | `help traces` (iter 6) |
| `traces stats` | ✅ clean JSON | `./scripts/run-cli traces stats --json` |
| `traces hotspots` | ✅ | `help traces` (iter 6) |
| `traces follow` | ✅ NDJSON | `help traces` (iter 6) |
| `traces view` | ❌ | iter 6 — silently ignores `--json` |
| `tree` | ❌ | iter 18 — emits ANSI tree |
| `search` | ❌ | iter 13 — formatted output |
| `status` | ❌ | iter 18 — ANSI |
| `log` | ❌ | iter 18 |
| `view` | ❌ | iter 18 — flag rejected, usage printed |
| `branch` | ❌ | iter 18 |
| `deps` | ❌ | iter 18 |
| `docs <topic>` | ❌ | iter 18 — silently ignores |
| `builtins` | ❌❌ | iter 18 — `--json` interpreted as a *search filter* (worse than ignored: the UX confuses an agent into thinking it filtered) |

5 with, 9 without. The 9 without are the ones agents reach for most often (orient → search → view → log → status). The `builtins` bug is the worst category: `--json` doesn't error or get ignored, it gets coerced into a filter argument.

**Proposed fix**: roll out `--json` across all 9 missing commands plus fix the `builtins` bug. Output schema for each: structured shape that mirrors the human view (e.g. `tree --json` emits `{owner, modules: [{path, fns: [...], types: [...]}, ...]}`). Behind a small shared formatter so adding `--json` to a new command becomes a one-liner. No language/runtime change.

**Meta-principle for §3 broadly**: **any command an agent may consume should ship `--json`**. New commands should default to JSON-supportable output design. Audit-as-of-2026-05-02 above is the baseline; track it per sweep to ensure we don't regress.

**Harness signal**: median tokens per Dark run drops (parseable output is denser than ANSI-formatted human view); first-parse-success attempts (§6 #12) — a downstream effect since agents that parse `view` cleanly produce better follow-up code; constraint-escape attempts (§6 #13) — agents won't shell out to `grep`/`awk` to massage `view` output if `view --json` exists.

---

**Round-2 reconsideration** *(per feedback-plan P2 "Reconsider --json as the default")*: the user asked whether `--json` is really the best format for many of these situations, or whether dense plaintext would work better. For some commands (e.g. `tree`), structured-but-text might beat JSON's bracket-overhead. The decision rule should be: default to JSON for parser-friendly cases (search, deps); default to dense plaintext for human-readable structured (tree, list). The roll-out below should be re-audited per-command before shipping. Cross-link: [tty-detection.md](tty-detection.md) covers the non-TTY-default question.

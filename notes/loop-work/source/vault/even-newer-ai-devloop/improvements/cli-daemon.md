---
title: CLI daemon vs per-call invocation — trade-off analysis
section: 3.2 Authoring (cross-cuts §3.1 Discovery)
priority: P3 (analysis only; not blocking tonight)
harness_signal: cold-start time per CLI call (already a §6 supporting metric)
---

# CLI daemon trade-offs — answering the user's "along what axes?" question

**The user's question** *(round-2 verbatim)*: *"what problems would 'having a CLI daemon instead of running EVERYTHING in the user-facing CLI' help? along what axes? idk."*

**The measured cost today**: each `dark` invocation pays a ~0.7-1.1 s cold-start (fresh .NET process per call, verified iter 23). A 10-turn agent loop pays ~7-11 s of pure CLI overhead, which dominates wall time at small budgets. Memory `feedback_no_json_or_dotnet_prewarm` deliberately defers a CLI-daemon optimisation; this file makes the trade-off explicit so the deferral is informed, not implicit.

## What a daemon would buy (the case for)

| Axis | Today (per-call CLI) | With daemon | Why daemon wins |
|---|---|---|---|
| **Cold-start amortization** | ~0.7-1.1 s per call | ~0 s after first call | Saves 7-11 s in a typical 10-turn agent loop. Compounds across 50 bench tasks/sweep. |
| **Package-tree cache** | Reloaded from `data.db` each call | Loaded once, kept warm in-memory | Eliminates the SQLite hot-path on every `view` / `tree` / `search`. |
| **Persistent watcher** | No equivalent — `File.watchLoop` blocks the foreground process | Runs as a daemon thread, fires events to short-lived clients | Unlocks `entr`-style "rebuild on save" workflows. |
| **Faster autocomplete** | Each tab-completion forks a fresh process | Tab queries hit the warm package-tree cache | Latency drops from ~700ms to <50ms — feels instant. |
| **Cross-session telemetry correlation** | `rundir/logs/telemetry.jsonl` per process; correlator must be inferred | Daemon owns the correlator — `ctx.session_id` populated correctly | Solves the §4.2 "ctx is empty" gap natively. |
| **Streaming stdin handling** | `readKey` blocks the call (per `improvements/realtime-roguelike` blocker) | Daemon handles input loop; clients get events | Adjacent to the `no-non-blocking-stdin` runtime gap, but at the CLI layer rather than language. |

## What a daemon would cost (the case against)

| Axis | Today (per-call CLI) | With daemon | Why daemon loses |
|---|---|---|---|
| **Lifecycle complexity** | Process boundary == cleanup; CTRL-C kills cleanly | Need to spec start / stop / restart / health-check / crashed-recovery | Adds ~6 commands to the surface and ~500 lines of supervision code. |
| **Signal handling** | Each invocation is its own SIGTERM target | Daemon must distinguish "stop the daemon" from "interrupt the current operation" | Compounds with the existing `no-signal-handling` Dark runtime gap (vault `where we're a bit short.md`). |
| **Multi-user contention** | Each user's CLI is independent | Per-user daemon? Per-rundir daemon? Per-machine daemon? Each model has different multi-tenant gotchas | The bench specifically wants concurrency=4 (per `queue-mechanism.md`); 4 daemons or 1 daemon + 4 workers? |
| **Dev vs bench vs prod queries** | Each call is stateless and simple | Daemon must isolate concurrent agent runs from each other (rundir isolation, package-tree branch isolation) | Risk of cross-contamination between bench tasks. Today's per-call model is naturally isolated. |
| **Observability** | `telemetry.jsonl` lines are independent and easy to grep | Daemon emits a stream where some events span multiple clients | Existing telemetry tooling assumes per-process boundaries. |
| **Debuggability** | "Reproduce: run this exact command" works | Daemon-state-dependent bugs require state reconstruction | Adds a class of "works-in-fresh-daemon, fails-in-warm-daemon" Heisenbugs. |

## Conclusion

**Probably yes for agent-heavy workloads; not blocking for tonight.** The daemon's cold-start savings compound with bench scale (50 tasks/sweep × ~10 calls/task × ~0.9 s = ~7.5 min/sweep saved). At 1-2 sweeps/week, that's ~10-15 min/week — a real gain but not a blocker. The lifecycle / multi-tenancy complexity is real and the §4.4 sandboxing model relies on per-process isolation today; a daemon redesign would re-litigate that.

**Recommendation**:
1. **Phase 4+**: spec a daemon, but as an opt-in mode (`dark --daemon` starts; `DARK_DAEMON=1 dark <cmd>` connects). Default stays per-process.
2. **Phase 1-3**: keep measuring cold-start (§6 supporting metric) so the benefit case sharpens with data.
3. **Mitigation in the meantime**: `improvements/claude-md-template.md` + `improvements/for-ai-bundle.md` reduce the *number* of orient calls; that's a cheaper-and-shippable substitute for daemon's cold-start savings.

---

**Cross-references**:
- Cold-start measurement: plan.md §4.2 supporting metrics (track median CLI invocation cold-start as part of every sweep).
- Reduce orient-call count instead of amortizing each: [`claude-md-template.md`](claude-md-template.md) + [`for-ai-bundle.md`](for-ai-bundle.md).
- Adjacent runtime gap: `no-non-blocking-stdin` (used by `realtime-roguelike` spec) — daemon would partially mitigate but not solve.

# Iter 06 — daemon performance budget

The rewrite doc gestures at perf in § 17 ("app subprocess overhead",
"projection-rebuild thundering herd"). Time to put numbers on
everything, see where the design hits real walls, and budget
explicitly.

## Idle daemon

A daemon doing nothing — just listening on a socket, holding open
connections to ops.db and a few projections.

```
Cost                                Estimate
.NET runtime (AOT-trimmed)             30 MB
LibExecution + builtins loaded         70 MB (Dval shapes, package refs)
ops.db connection (RW + 2 RO)           1 MB (WAL shared cache + per-conn)
Open projections (LRU=32 max,
   ~5 typically open)                   5 MB
Hash cache (PackageRefs)                2 MB
.NET reserved heap                     20 MB (initial gen-0 budget)
                                      ───────
Idle daemon                          ~130 MB
```

130 MB at idle. Compare today's CLI: each invocation pays ~150 MB
peak; over a workday of 50 commands, it's 7.5 GB of memory
allocated and freed. Daemon model trades the per-command spike for
a steady state. Net positive.

The 70 MB for "builtins loaded" is suspicious-large. Most of it is
the F# code paths themselves (interpreter, package types, etc.).
Per iter 02, much of this can move to Dark — which doesn't
necessarily save RAM (Dark fns live in package storage too) but
makes the F# binary smaller (~20-30 MB savings to the AOT'd
deliverable).

## Per-app RAM

The rewrite doc § 17: "Each running app being its own .NET process
means each consumes ~20MB+ baseline. If we have 50 apps running,
that's a gig."

That's per-process. In-daemon hosting (default per § 8) is
different:

```
Cost per in-daemon app                  Estimate
ExecutionState (VMState + symbol table)   3 MB
App's package fns (cached, deduped)       2 MB shared, ~500 KB unique
data.db connection                        1 MB
HTTP listener overhead                    1 MB
                                         ───────
Per app                                 ~5 MB
```

50 apps in-daemon: 250 MB above idle. Daemon total: ~380 MB. Fine.

100 apps: ~630 MB. Still fine.

500 apps: ~2.6 GB. Now we care. Mitigation: idle-evict — apps that
haven't served a request in 10 minutes get suspended (state held
on disk, removed from RAM). Resume on next request adds ~20ms.

App in subprocess (the `--isolated` opt-in or after-3-crashes
fallback): full 30 MB per process, plus overhead. Reserved for
apps that genuinely need crash isolation.

## Write amplification

The unified op model adds one indirection: every state change is
both an op-append and a projection update.

| Action | Today | New | Amplification |
|---|---|---|---|
| `dark fn Foo.bar` (one-line edit) | 1 INSERT package_ops + ~3 INSERTs projection | 1 INSERT ops + ~3 INSERTs pkg.db | 1.0× |
| Commit a branch (5 ops) | 5 INSERTs package_ops + 5 INSERTs commits | 5 INSERTs ops (1 batched) + 5 INSERTs pkg.db | 1.0× |
| Single trace (~50 fn calls) | 50 INSERTs trace_fn_calls + 1 INSERT traces | 50 INSERTs ops + 50 INSERTs traces.db + 1 INSERT each side | 2.0× (no batch); 1.05× (batched into a single op) |
| App `DB.set("k", v)` | 1 INSERT user_data | 1 INSERT ops + 1 INSERT data.db | 2.0× |
| HTTP request handler completes | 1 INSERT trace + N fn-call INSERTs | 1 op envelope batch + 1 trace projection insert | ~1.0× with batching |

The bad case is "many tiny ops." Trace recording is the textbook
example. Mitigation: **batched op shapes** in the unified model —
`RecordCalls of List<…>` instead of one op per call. Per-stream
batching keeps amplification at ~1.05× across the board.

For app data: high-throughput apps use `LocalDatastore` (per
unified-model § 7) which bypasses the op log. Loses sync/audit
in exchange for ~2× write throughput. Per-table opt-in.

## Hot path: serving an HTTP request

```
Step                            Estimate (warm)
WS / unix-socket frame parse        20 µs
Dispatch into ExecutionState       100 µs (look up route by hash)
Run the app's router fn          ~1-50 ms (depends on the fn)
Trace recorder writes              ~50 µs/event, batched at end
Format response                   100 µs
Send response                      20 µs
                                   ───────
Round-trip overhead              ~300 µs (excluding the user's fn body)
```

Daemon should comfortably handle 5-10K simple requests/sec. The
`HttpListener` we're using today bottoms out at ~3K req/sec on a
single machine due to its architecture (per the
notes/merge-readiness-report.md). At 1000 req/sec with 50 apps,
we're at 20 req/sec/app — well under budget.

If a single app needs 5K+ req/sec, isolate it (`--isolated`
subprocess) and give it a dedicated tiny Dark runtime.

## Sync pull burst

User comes back from a week off. Hub has 5000 new ops queued.

```
Phase                           Estimate
Open WS, request stream          200 ms
Receive 5000 ops (250 KB)        300 ms (network)
Verify signatures (Ed25519)      ~150 µs each → 750 ms total
INSERT INTO ops (batched txn)    100 ms (50K inserts/sec)
applyOp on each (projection)     ~50 µs each → 250 ms total
Write watermark                  10 ms
                                ─────────
Total                          ~1.6 s
```

1.6 seconds for 5000 ops is fine — feels instant. Scales linearly
to 50K ops (~16s). Beyond that, daemon should chunk and stream.

The signature-verification 750ms is half the budget. Worth checking
if Ed25519 verify is hardware-accelerated on the target arches
(x86_64 yes, ARM64 with AES-NI yes, others slower).

## Projection rebuild storm

Worst case: schema bump triggers rebuilds on next access. User has
30 branches.

Sequential rebuild: 30 × 1.5s = 45s. Bad.

Parallel rebuild (4 workers): 30 / 4 × 1.5s = ~12s. Better.

Lazy rebuild (most users only touch 2-5 branches actively): user
feels 1.5s on first command, projections build for other branches
in background or on-demand. Best.

```fsharp
module LibProjection.RebuildPool

// Rebuild N=4 projections in parallel. ops.db readers don't block
// each other (WAL).
let rebuildAll (branchIds: List<BranchId>) : Task<unit> =
  branchIds
  |> List.chunkBySize 4
  |> List.iter (fun chunk ->
      chunk |> List.map Build.buildFromScratch |> Task.WhenAll |> Task.Wait)
```

## Disk budget

Long-term storage projections.

```
Component                              Per-day        Per-year
ops.db (user-private packages)         ~50 KB         ~18 MB
ops.db (trace ops, batched)            ~5 MB          ~1.8 GB
ops.db (session ops)                   ~10 KB         ~3.6 MB
ops.db (app ops, mid-traffic apps)     ~20 MB         ~7 GB
                                       ─────          ──────
ops.db total                           ~25 MB         ~9 GB

projections/<branch>/pkg.db            ~10 MB         (rebuildable)
projections/<branch>/traces.db         ~50 MB         (rebuildable, +retention)
apps/<id>/data.db                      varies         (depends on app)
```

The 9 GB/year is dominated by trace ops. Mitigations already in
the design:
- **Trace retention** (`stream_config.retention_days = 30`): drops
  ops > 30 days old at sync time. Steady-state ~150 MB instead
  of 1.8 GB.
- **Selective sync** for traces (default off; opt-in per trace):
  most ops never leave the originating instance.
- **Compression**: ops.db blobs compress well with zstd; SQLite
  has built-in support but it's manual. Estimated 3-4× savings
  on trace payloads.

For "I'm a tiny user with one device": 100 MB of state after a
year, dominated by package ops. Easy.

For "I run 50 apps, each ingesting 1000 events/day, retention
30d": ~50 GB after the first month, then steady-state. Should
add VACUUM cron, daily.

## RAM leak surveillance

Long-running .NET processes accumulate. Specific risks for the
daemon:

- **Dval references in long-lived ExecutionStates.** Apps with
  state (counters, caches) hold dvals. These don't compact well
  in .NET gen-2. Mitigation: per-app GC sweep when an app stops
  serving for >5 min; nuke its caches.

- **Open SQLite cursors not disposed.** Easy bug to write. Code
  review discipline + integration test that asserts open
  connections are bounded after running 1000 commands.

- **Hash cache unbounded growth.** PackageRefs / branchChain
  caches grow with the package store. Should be bounded LRUs,
  not unlimited dicts. Audit during slice 5.

- **Subscribe-but-never-unsubscribe.** Hub WS subscribes to
  events; if subscribers don't unsubscribe on app stop, dangling
  callbacks hold app state alive. Common JS-style bug. Mitigation:
  weak refs for subscribers, daemon-level sweep.

Restart policy: if RSS exceeds 2GB or 90% of `ulimit -v`, daemon
restarts. Apps survive (state in their own DBs); user sees a
~3s blip during restart.

## Concurrent app load — 100-app daemon

Test harness should validate this. Synthetic 100 apps, each
serving HTTP, each emitting traces:

```
Setup:
  100 apps registered, in-daemon hosted
  Each: simple `request -> response` Dark fn
  Synthetic: 100 ops/sec total (1 req/sec/app)

Expected:
  Daemon RSS:        ~700-800 MB
  CPU:               ~20-40% one core (mostly trace recording)
  Disk write:        ~5K trace ops/sec → ~25K rows/sec total
  P50 latency:       ~5 ms
  P99 latency:       ~50 ms
  Trace persistence: <100 ms after request completes

Stress: 1000 ops/sec (10 req/sec/app)
  Daemon RSS:        ~1 GB
  CPU:               ~80% one core, 30% second core
  Disk write:        50K rows/sec — at SQLite WAL ceiling
  P99 latency:       100-200 ms (queue backup possible)
```

Both should be in budget. The 1000-ops/sec stress is at the SQLite
ceiling — if it actually hits in production, we'd batch trace ops
within a 100ms window before persisting (already in the design but
worth testing).

## What "fast" feels like

Numbers don't directly translate to user experience. The
end-to-end "feel" tests:

| Action | Today | After daemon |
|---|---|---|
| `dark help` | ~1.5s | ~30 ms |
| Cold `dark fn Foo.bar` after no-action | ~2s | ~150 ms |
| Warm `dark fn Foo.bar` (recent) | ~1.5s | ~50 ms |
| `dark eval "1 + 2"` | ~1.8s | ~80 ms |
| `dark traces tail` (live) | starts in ~1.5s | starts in ~50 ms |
| `dark sync` (no-op) | n/a | ~200 ms |
| `dark conflicts` (5 conflicts) | n/a | ~50 ms |

The 30× improvement on `dark help` matters less than the 30×
improvement on `dark fn`. Fast `dark fn` is what unlocks the "I
can iterate without waiting" feel that we want.

## Where the budget will get spent

If the design holds, the daemon's worst-case load is:

1. **Trace ingestion** (highest sustained load, batchable).
2. **Projection rebuilds** (rare but expensive, parallelize).
3. **Sync clones** (one-shot bursty, well-bounded).
4. **App request handling** (linear in incoming rate, profilable
   per-app).

Fine. None of these are existential. The risk is *forgetting to
batch*: if every trace event becomes its own op without
batching, write amplification swallows the daemon's day. Make
batching the default in the trace builder; require an
explicit `--no-batch` to disable for debugging.

## Things I haven't budgeted

- **WebSocket frame overhead** between daemon ↔ hub. Probably
  negligible (frames are small; volume is low).
- **JSON parsing in the CLI ↔ daemon RPC.** Worth profiling; if
  it's >5% of round-trip, switch to bincode.
- **Garbage collection pauses.** AOT-trimmed F# tends to have
  better GC behavior than full .NET, but pauses still happen.
  Worth a histogram on a 1-hour synthetic load.
- **Cold startup of an idle-evicted app.** When an app comes
  back from suspend, what's the actual cost? Loading from
  disk + initializing ExecutionState + hot-replaying recent
  state: estimate ~200ms. Should test.

These are slice-5+ concerns; today they're hypothetical.

## Compared to: today's CLI

Today's perf model is "every CLI invocation is a fresh process."
The startup tax (~1s) dominates everything. Even a no-op
`dark` command pays it. The new model:

- Startup tax paid once per session (`darkd` boot).
- Subsequent commands amortize against an open process.
- Net: 30-50× faster on common interactive patterns.

The cost: a long-running process to maintain, leaks to monitor,
restart hygiene. Trade we explicitly want to make.

## Open questions

1. **Should the daemon eagerly preload top-N most-used
   projections at startup?** Probably yes. User's `main` branch
   is hot 95% of the time. Preloading after daemon start adds
   ~3s before first user command — but the user is typically
   not running anything in the first 3s anyway.

2. **What's the right LRU size for projection connections?**
   32 is arbitrary. Real-user telemetry would say. Default 32,
   tunable via `daemon.toml`.

3. **Per-app SQLite connection pool size.** Apps doing lots of
   concurrent reads need bigger pools. Default 4, tunable per
   app.

4. **Memory pressure handling.** OS says "you're at OOM
   ceiling." What does the daemon do? Suspend non-essential
   apps? Refuse to bring projections back from idle-evict?
   Surface a notification? All of those, in some priority
   order.

## TL;DR

| Resource | Idle | Loaded (50 apps) | Stress (100 apps × 10 req/s) |
|---|---|---|---|
| RAM | 130 MB | 380 MB | ~1 GB |
| CPU | <1% | ~20% one core | ~120% (1.2 cores) |
| Disk write | 0 | ~5K rows/sec | ~50K rows/sec |
| Disk steady-state | n/a | ~5 GB total | ~50 GB total |
| Network (hub WS) | ~10 KB/min | ~10 KB/min idle | bursts during sync |

These fit on commodity hardware (2 GB RAM laptop, modern SSD)
without trouble. Power users with high-traffic apps need to
think about retention; defaults are safe.

The performance model is good. The risk is not in the numbers
but in the discipline: **batch ops at every stream's recorder**;
**LRU and idle-evict aggressively**; **don't leak**. With those,
we have orders-of-magnitude headroom over the felt-latency floor
of "the user notices a delay."

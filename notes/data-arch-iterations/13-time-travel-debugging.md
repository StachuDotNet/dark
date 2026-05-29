# Iter 13 — Time-travel debugging

The architecture has traces as first-class ops in their own
stream (per the rewrite doc § 6 and iter 04). With pure-fn
semantics + recorded inputs + content-addressed code, **every
past execution can be replayed exactly**. This iter sketches
what makes that into a distinctive product feature, not just
"like Chrome DevTools but for Dark."

## What the trace records

Every fn call (in user code, not Stdlib internals — see below)
emits one trace op:

```dark
type TraceOp =
  { id: TraceId
    parentId: Option<TraceId>
    fnHash: Bytes  // content hash of the fn body
    fnName: FQFnName
    args: List<Dval>
    sideEffects: List<SideEffect>  // DB reads, HTTP responses, time samples
    result: Stdlib.Result.Result<Dval, RuntimeError>
    durationNs: Int64
    startedAt: DateTime }

type SideEffect =
  | DbRead of dbName: String * key: Dval * value: Option<Dval>
  | DbWrite of dbName: String * key: Dval * value: Dval
  | HttpRequest of url: String * response: HttpResponse
  | TimeNow of DateTime
  | RandomBytes of Int64 * Bytes
  | EnvRead of String * Option<String>
```

A trace tree is the closure of all these ops with the same root
parent. A handler invocation typically produces 10-1000 trace
ops; tight loops collapse via the sampling rules from iter 06.

The key property: given the trace, you can **deterministically
re-execute** the fn. Same hash → same code; same args → same
inputs; same side-effect results → same external state. The
output must match.

## What that enables (in increasing wildness)

### 1. Reproduce any past trace

```
$ dark trace show 8a3f2dd
[handler] /api/users (req-id: 4f2e)
  ↳ Mycorp.api.handle {request: ...} → 500 Error
    ↳ Mycorp.api.parseRequest ... → Ok ...
    ↳ Mycorp.users.lookup "alice" → Ok {id: 42; ...}
    ↳ Mycorp.users.update {id: 42; email: "..."} → Error "DB constraint"

$ dark trace replay 8a3f2dd
... reruns deterministically ...
... gives the same Error ...
```

The user gets to see exactly what happened, even if the bug was
3 days ago in production. No "can you reproduce it locally?" —
the trace IS a reproducer.

### 2. Step through

```
$ dark trace step 8a3f2dd
[step 1/22]  Mycorp.api.handle {request: ...}
  > step
[step 2/22]  Mycorp.api.parseRequest ...
  > vars
  request = {method: "POST"; ...}
  body = "{...}"
  > step into
[step 3/22]    Stdlib.Json.parse "{...}"
  ...
```

Step / step-into / step-out / continue-to-N. CLI or editor pane.
Each step shows local vars. Trace tree visible as collapsible
hierarchy.

### 3. Modify inputs, replay

```
> :replay-with {request.body = "{\"name\":\"alice\"}"}
[handler] /api/users (req-id: REPLAY-1)
  ... new trace, with the modified input ...
  ... result: 200 Ok ...
```

The user changes the input, replay produces a new trace. Two
modes:

- **Pure mode.** Side-effects (DB reads, HTTP) play back from
  the original trace's recordings, even if the modified input
  takes a different code path. If the new path makes a DB
  read the original didn't record, the replay fails with
  "uncovered side effect — switch to live mode or expand
  recording."
- **Live mode.** Side-effects re-execute against current state.
  The trace might do real DB writes. UI shows a "this will
  affect production!" warning before running.

Default: pure mode. Live mode is opt-in per replay.

### 4. Modify code, replay

```
> :edit Mycorp.users.update    # opens editor
   ... user fixes the bug ...
> :save
   (WIP op recorded; new fn hash)
> :replay 8a3f2dd
   ... runs original args through new code ...
   ... result: 200 Ok ...
```

This is the big one. **Write a fix, see if it would have fixed
the trace, before deploying.** Same machinery as #3 (pure or
live mode for side effects).

If the trace pane is open in the editor, this happens
automatically: edit the fn, trace pane re-runs, you see new
behavior in real time.

### 5. Branch reality

A trace can be a branch point. "From step 8 onwards, what would
have happened if condition X?"

```
> :branch-from 8 {user.role = "admin"}
[branched trace b3-fa12]
  ... step 1-7 same as original ...
  ... step 8 onward differs ...
  ... result: granted access ...
```

The branched trace is its own first-class trace, persisted in
the trace stream, replayable, divergence-comparable. Useful
for:
- Hypothetical debugging ("what if the user was an admin?")
- Demos ("here's the path the user took; here's the alternate
  path you might")
- Counterfactual testing ("if input X had been Y, would we
  still have crashed?")

### 6. Diff two traces

```
> :diff 8a3f2dd <vs> b3-fa12
First divergence at step 8:
  Original:  Mycorp.checkRole {user: ...} → Error "not admin"
  Branched:  Mycorp.checkRole {user: ...} → Ok ()
Subsequent paths diverge entirely.
```

UI: two trace trees side-by-side, divergence point highlighted,
var diff at that step. Useful for:
- Pre/post comparison after a fix.
- Customer support: "your trace vs. Alice's working trace."
- Regression testing.

### 7. Star traces for indefinite retention

```
> :star 8a3f2dd "the bug from 2026-04 the on-call paged me about"
```

Starred traces never sample-out, never archive-purge, are kept
across schema changes by replaying through migrators. They're
documentation as much as data. New team member reads
`dark trace show <starred>` to understand a class of bug.

### 8. Trace-driven test generation

```
> :as-test 8a3f2dd
Generated test: tests/regression/8a3f2dd.dark
  let testInputs = ...
  let expectedOutput = ...
  ...
```

A trace becomes a regression test: same inputs, same recorded
side-effects, expected output. Add to test suite. The test
fails if the fn body changes the output.

This is a **massive multiplier** on test coverage. Every bug
report that ships with a trace becomes a regression test in one
command.

## What's distinctive vs prior art

| Tool | Mechanism | Granularity | Persists? | Replayable on different machine? | Edit code & re-run? |
|------|-----------|-------------|-----------|----------------------------------|---------------------|
| Chrome DevTools | JS execution recorder | JS expressions | No | No | No |
| rr | CPU instr recording | Hardware ops | Days | Same machine class | No |
| Replay.io | Browser session record | Frames | Cloud | Yes, on Replay's infra | No |
| Smalltalk halo | Live objects | Object graph | While image lives | No | Yes (if same image) |
| Event sourcing | Event log | Event boundaries | Yes | Yes | Re-derives state, not execution |
| Dark trace | Per-fn-call recording | Fn calls | Indefinite | Yes (content-addressed) | **Yes** |

The combination — language-level + persistent + content-
addressed (so any peer can replay) + live-edit + diff/branch —
is unique. Each prior tool has some of it; none has all.

## The mechanics

### Replay engine

The daemon already has an `ExecutionState`. Add a "replay
mode" flag:

- `Pure`: side-effect builtins consult the trace's recorded
  side-effects table; if a request isn't there, return an
  explicit `ReplayUncovered` error.
- `Live`: side-effect builtins execute normally.
- `MockedDb`: DB reads are mocked from trace; DB writes are
  no-ops (silently swallowed).

The replay engine resolves the fn body by hash (peer asks
package store: "give me fn-hash X"). Runs it with the args.
Compares output to recorded.

If output differs from recorded: the trace was non-
deterministic. This is treated as a bug — daemon logs the
divergence with both outputs. Sources of non-determinism:
- A side-effect we didn't record (escape hatch in builtins).
- A bug in the engine.

### Trace recording cost

Naive: every fn call writes a TraceOp. For a 1000-call handler,
that's 1000 ops written.

Optimization (per iter 06): batch trace ops in memory; flush
per-handler-call as one bundle. ~10× reduction in op overhead.

Sampling for tight loops: a fn called >100× in one trace gets
sample-recorded (every 10th call) past that. The trace marks
itself as "sampled" for those branches; replay shows
"... 90 calls collapsed ..." in the UI.

Sampling for high-QPS handlers: at >100 QPS, record only 1% of
traces (configurable per handler). Critical errors always
recorded (override the sampling).

Disk cost: per iter 06's budget, traces are bounded at ~100MB
per app per day with sampling. 30-day retention default; older
than 30d archived to cold storage; starred kept indefinitely.

### Variable inspection

The trace records args + result. To show **all** local vars at
each step (full inspection), we need to record bindings.

Two options:
- **Record everything.** All `let`-bindings written to the
  trace. Heavy but complete. Storage cost ~3-5×.
- **Record on demand.** Default record only args + result; if
  user requests "vars at step 8," replay until step 8 and
  pause there, dumping bindings. Light but slow.

Recommendation: record args + return only by default; on-demand
replay for full vars. The replay is fast (sub-second for most
traces) so this is fine.

### Trace tree representation

Naive: each fn call is a TraceOp with `parentId` linking to its
caller. Reconstruct the tree by joining on parent.

Optimization: store the tree pre-flattened (depth-first
traversal order, with depth markers). Reading is O(N); no
graph join. Standard trick for log/trace formats.

### Storage layout

`traces` stream, per app:

```
trace-id: <uuid>           -- root trace
  + parent: null
  + children: [trace-id-2, trace-id-3, ...]
  + ...

trace-id-2: <uuid>          -- child trace
  + parent: trace-id
  + children: [trace-id-4, ...]
  + ...
```

Projections (per iter 11):
- `traces-recent`: last 1h, all apps.
- `traces-by-error`: traces with `result.kind == Error`,
  grouped by error class.
- `traces-by-handler`: traces grouped by handler.
- `traces-starred`: pinned-by-user traces.

Each projection is a Dark fn the user can override / extend.

## Distinctive UX moments

### "I just shipped a bug; how do I find what it broke?"

- Open editor, recently-merged branch.
- `dark trace recent --error` lists 12 traces with errors in
  the last hour.
- Click into the most recent. Trace pane shows the call tree.
- Step through, see where it broke.
- Edit the fn in the editor. Trace pane auto-replays.
- Output is now Ok. Commit, ship.

End-to-end "bug → fix → verified" in <5 minutes, no test rerun.

### "Customer support: a user reports a bug"

- User shares a trace ID (or session ID).
- `dark trace show <id>` shows the failing trace.
- Step through to find the failure point.
- Replay with their input on a fixed branch of the code; see
  green.
- Reply with: "Fixed in commit X. Traced your specific bug, my
  fix handles your input correctly. Live in 5 minutes."

### "Demo: here's the path through my code"

- Run the app, do a thing, capture trace.
- `dark trace show <id> --live` opens an interactive pane.
- Walk through the trace step-by-step in front of the audience.
- "And here, if the user had been an admin instead, here's what
  would have happened" → branch the trace.

### "What does this Stdlib.fn actually do?"

- `dark eval 'Stdlib.List.sortBy ...'` produces a trace.
- `dark trace show <id>` reveals the recursive
  Stdlib.List.sortBy implementation, step by step. Educational.
- Same trace data, applied to learning.

## Privacy

Traces include user inputs (request bodies, query params,
authenticated user IDs, etc.). Per iter 03's ACL model:

- Traces inherit the app's ACL by default. Production traces
  visible only to the team.
- Per-trace redaction rules: a Dark fn can mark certain fields
  as `Trace.Redacted`. The trace stores `<redacted>` instead of
  the value.
- Bulk redaction post-hoc: GDPR delete request → daemon walks
  trace stream, redacts all values matching a user ID, emits
  redaction ops. Transparent / auditable.
- Encryption-at-rest for the trace stream: opt-in (per iter 03's
  per-stream encryption story — punted to a later iter, but
  the hooks are there).

## What this means for the architecture

Three architecture-level implications:

### 1. **Determinism is mandatory.**

Every Dark builtin that has side effects must be recordable.
Three categories:

- **Pure fns**: trivial; no recording needed (just args + result).
- **Recordable side effects**: `DateTime.now`, `Random.bytes`,
  `Stdlib.DB.get/set`, `Stdlib.Http.request`. Wrap each in the
  recording layer.
- **Non-recordable side effects** (must not exist!): direct FS
  writes, raw socket access, threads. These would break replay.
  Dark's sandbox already disallows them; reaffirm.

### 2. **Traces tie the system together.**

Traces aren't a debugging aid; they're a first-class data type.
Apps query their own traces (`Stdlib.Trace.recent`, etc.).
Tests can be generated from traces. Refunds can be issued
based on traces. Audit logs ARE traces. The trace stream is
the lifeblood of the system.

This justifies the cost of recording. The trace data isn't
"observability dust"; it's product surface.

### 3. **The cost model needs a trace tier.**

dark.run charges for:
- Trace storage (cheap; archived to cold storage after 30d).
- Trace replay compute (cheap; mostly cached side-effects).
- Indefinite retention (starred traces — small premium for
  >30d kept).

This is reasonable. Most of the value is in recording at all;
storage cost is modest if sampling is sane.

## Open questions

1. **Cross-fn-version replay.** A trace recorded against fn-
   hash X. The user wants to replay against fn-hash Y (their
   fix). Works for `Pure` mode. What if Y refactors the fn
   into multiple smaller fns? The new fns have new hashes; the
   trace's "expected calls" don't match. Solution: replay
   doesn't pre-validate the call structure; it just runs Y
   with the recorded args and intercepts side effects via
   matched recorded values. Different intermediate fn names
   are fine.
2. **Concurrency.** Two parallel fn calls record their traces
   into the same parent. Order matters? Recommendation: record
   each as a separate child with `startedAt`/`endedAt`
   timestamps; replay can see the parallelism explicitly. UI
   shows them as parallel-tree.
3. **External-state divergence in pure-mode.** A trace recorded
   when DB had row R = "old"; replay with new code wants R =
   "new" (the recorded value is stale). User has to choose:
   replay against the time-of-trace state (slow — need to
   reconstruct the full DB at trace time) or replay against
   current state (live mode).
4. **Time-of-trace DB reconstruction.** If we want pure replay
   against the DB-state-at-trace-time, the daemon needs a
   point-in-time snapshot. Doable: ops.db has every DB write;
   replay applies all DB writes up to the trace's startedAt.
   Cost: build time. Optimization: snapshot DB state every
   minute; replay applies the delta. ~few seconds for typical
   traces. v2 feature.
5. **Trace mutation.** Can a user edit a trace? E.g., redact
   data, change inputs, fix a recording bug. Recommendation:
   no — traces are immutable like all ops. To "modify" a
   trace, branch from it (which creates a new derived trace).
   Redaction is a separate op type that overlays.
6. **Replay across daemon versions.** Daemon v0.7 records
   trace with a side-effect format X; daemon v0.8 changes
   format. Replay an old trace? Yes, with a per-version
   migrator (small Dark fn). Same kill-and-fill pattern as
   schema migrations.

## TL;DR

Time-travel debugging falls out of the architecture for free:
trace stream + content-addressed code + deterministic builtins
= replayable execution. Not a separate tool — built into the
daemon, surfaced through the editor and CLI.

Distinctive vs prior art: language-level granularity, indefinite
persistence, replayable on any peer, **edit-and-re-run live**,
trace-as-test-generator, trace-as-branch-point.

Determinism is mandatory; trace storage is bounded by sampling;
privacy is per-stream ACL.

The product story: "every bug report comes with a trace; every
trace is a reproducer; every reproducer is one command from
becoming a regression test." That's a developer workflow other
platforms can't easily copy.

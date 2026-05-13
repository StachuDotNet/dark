# 08 — Tracing as Artifact

> The trace *is* the program. The source is sketch + cache.

## What this doc gives you

A full event schema, the storage shape, the replay protocol, the diff design, and the relationship to the existing Darklang tracing infra.

## The mental model

In a normal language, you have:
- Source (durable, primary)
- Bytecode (derived)
- Runtime state (ephemeral)
- Logs / traces (observability, optional)

In PDD:
- Sketch (durable, but partial — names + sigs + maybe bodies)
- **Trace** (durable, primary — what actually happened)
- Cache of materializations (derived from traces, content-addressed)
- Runtime state (ephemeral)

The trace is *not* observability. It's the **authoritative record of execution**. You don't read the source to know what ran — you read the trace. The source is hints.

## Event schema (PDD-specific events)

JSONL, one line per event. Stored at `rundir/traces/<sessionId>/<traceId>.jsonl`.

```jsonc
// Session boundary
{ "t": 0, "ev": "session_start", "sessionId": "...", "cmd": "dark pdd run ...", "env": {...} }
{ "t": 8432, "ev": "session_end", "result": "ok|err", "recoveries": 2, "humanAsks": 1 }

// Program load
{ "t": 12, "ev": "program_loaded", "instrCount": 47, "pendingsAtLoad": ["h7a8", "h2c4"] }

// Materialization
{ "t": 18, "ev": "materialize_start", "handle": "h7a8", "name": "foo", "sigHint": {...}, "opts": {...} }
{ "t": 95, "ev": "candidate", "handle": "h7a8", "source": "find", "elapsedMs": 77, "sig": "...", "hash": "..." }
{ "t": 312, "ev": "candidate", "handle": "h7a8", "source": "generate", "elapsedMs": 294, "sig": "...", "hash": "..." }
{ "t": 96, "ev": "candidate_rejected", "handle": "h7a8", "source": "generate", "reason": "find already won, sig conflict" }
{ "t": 97, "ev": "materialize_done", "handle": "h7a8", "winningSource": "find", "hash": "..." }

// Frame parking + resumption
{ "t": 22, "ev": "frame_park", "frameId": "f1", "blockedOnHandle": "h7a8" }
{ "t": 100, "ev": "frame_resume", "frameId": "f1", "withHash": "..." }

// Calls (rolled up — not every Instruction)
{ "t": 105, "ev": "call_start", "frameId": "f1", "fn": "foo", "fnHash": "...", "argsHash": "..." }
{ "t": 142, "ev": "call_end",   "frameId": "f1", "fn": "foo", "resultHash": "..." }

// Recovery
{ "t": 220, "ev": "recovery", "loc": "Function(Package h2c4)", "policy": "EmptyBody", "reason": "MaterializationFailed", "substitutedDvalKind": "DUnit" }

// Capabilities
{ "t": 240, "ev": "capability_check", "fn": "HttpClient.get", "requested": ["CapReadNet"], "granted": ["CapReadFile"], "decision": "Denied" }
{ "t": 250, "ev": "capability_grant", "cap": "CapReadNet", "scope": "session", "source": "interactive_ask" }

// Human
{ "t": 245, "ev": "human_ask", "queryId": "q1", "kind": "AskCapability", "details": {...} }
{ "t": 8600, "ev": "human_answer", "queryId": "q1", "response": "Always", "latencyMs": 8355 }

// Errors that escaped recovery
{ "t": 700, "ev": "rte_uncaught", "rte": "TypeError(...)", "callStack": [...] }
```

`t` is milliseconds since `session_start`. Monotonic. Cheap to compare.

## The minimum extensions to existing Tracing.fs

From `dl-tracing.md`, there's already a tracing struct. We add fields:

```fsharp
type Tracing =
  { // -- existing --
    loadFnResult : ... -> Option<...>
    storeFnResult : ... -> ...
    storeFrameEntry : ... -> ...
    storeLambdaResult : ... -> ...
    skipTracing : bool

    // -- new (PDD) --
    materializeStart : FQFnName.Pending -> unit
    candidateArrived : FQFnName.Pending * MaterializeCandidate -> unit
    candidateRejected : FQFnName.Pending * MaterializeCandidate * reason : string -> unit
    materializeDone : FQFnName.Pending * MaterializeResult -> unit
    frameParked : uuid * FQFnName.Pending -> unit
    frameResumed : uuid * FQFnName.Pending -> unit
    recovery : string * RecoveryPolicy * string * Dval -> unit
    capabilityCheck : FQFnName.FQFnName * Set<Capability> * CapabilityDecision -> unit
    capabilityGrant : Capability * GrantScope -> unit
    humanAsk : HumanQuery -> unit
    humanAnswer : queryId * HumanResponse * latencyMs : int -> unit }
```

Default impl: append-write each as a JSONL line. Configurable destination: stdout (debug), file (default), socket (for live UI).

## Trace size

A program with 100 materializations + 1000 calls + 20 recoveries: ~30-100KB JSONL. Easy.

A long-running session might generate MB/hour. Pruning:
- Rotate every N MB.
- Optionally roll up frequent `call_start`/`call_end` pairs into a single `call` event after the fact.
- Stream-compress (zstd) — cheap CPU.

Not a Day-1 concern. Disk is cheap.

## Replay

Given a trace, can we re-run and get the same result?

**Yes, if:**
- All materializations are cached (their hashes are content-addressed and reproducible).
- Builtins are deterministic *or* their inputs are recorded.
- No external side effects (HTTP, time-of-day) that differ between runs.

**The replay procedure:**

```
1. Parse the trace, build a map: handle → hash (winning materializations).
2. Pre-populate the package cache with the recorded hashes.
3. Re-execute the program. Materialization is short-circuited because every
   Pending's preferred hash is already in cache.
4. For builtin calls that record their result, intercept the call and return
   the recorded result instead of re-invoking.
5. Compare the resulting trace to the original. Equal? Replay succeeded.
```

Caveats: nondeterminism (random, time, network) needs `loadFnResult`-style record-and-replay, which the existing tracing struct already has. PDD inherits it.

## Diff

Two traces of the same input, possibly with different materializations:

```
$ dark pdd trace diff <traceA> <traceB>

Trace diff (input identical: hash=abc...)
  + traceA used generate-fn for `foo` (hash=def)
  - traceB used find-fn for `foo`     (hash=789)
  ! result differs: traceA="Hello", traceB="hello"
```

This is the natural review surface. Stachu opens a diff in his editor, sees that one materialization changed the result, picks the right one, promotes it.

Implementation: walk both JSONL streams, align by `t` and `ev` kind, emit diffs.

## Promote

```
$ dark pdd promote <traceId> --fn foo
Locking `foo` to hash `def...` (from traceA).
This hash becomes the canonical materialization for the name `foo`.
Future runs will use it without re-materializing.
```

Under the hood: writes a row to a `pdd_pinned_fns` table mapping `name → hash`. The `find` path consults this first.

## Connection to SCM

A branch's state = (sketch sources) + (cache of pinned hashes) + (set of traces).

**Branches diverge** when:
- Sketches differ (normal source diff).
- Pinned hashes differ (different fn bodies for the same name).
- Traces differ (different runtime behavior).

**Merging** compares each axis independently. Sketch merge = normal git-style. Hash merge = pick one, or interactive. Trace merge = hardest, probably "keep one, discard the other unless they're identical."

This is a big design space. Out of scope for the spike, but the trace format must be **stable enough today** that we can build this on top tomorrow. JSONL with versioned schemas is fine.

## Why JSONL specifically

- One line per event = trivially streamable.
- Append-only = no locking.
- Human-readable when needed.
- Easy to grep / awk in emergencies.
- Migrates cleanly to a binary format later (each line gets a record kind).

Don't use protobuf or msgpack yet. JSONL until we hit a performance issue we can't ignore.

## Tooling we want

```
dark pdd trace show <id>       # pretty-print the JSONL with colors
dark pdd trace events <id>     # event histogram (10× materialize, 3× recovery, ...)
dark pdd trace fns <id>        # which fns ran, with timing
dark pdd trace recoveries <id> # only the recoveries
dark pdd trace human <id>      # only the human-decision points
dark pdd trace replay <id>     # re-run from this trace
dark pdd trace diff <a> <b>    # compare two
```

All thin wrappers over JSONL grep + format. Day-2 work.

## Day-1 priorities

For the very first day:
1. Write `materialize_start`, `materialize_done`, `recovery` to a JSONL file.
2. Print the file path at end-of-run.
3. Have a `--no-trace` flag.

That's it. Tooling later.

## The deeper claim

The reason this is important: **once the trace exists, the "what does this program do" question becomes empirical, not theoretical**. You don't reason about the source. You run it, you read the trace, you make decisions based on what happened.

That is, materially, a different way to write software.

## Connection to other docs

- `02-libexecution-changes.md` — adds tracing hooks at the same spots as other RTE checks.
- `04-signature-consensus.md` — every `candidate_rejected` is a sig-consensus event.
- `05-tolerant-runtime.md` — every recovery is a trace event.
- `06-builtin-permissions.md` — every cap check is a trace event.
- `07-human-in-loop.md` — every human ask/answer is a trace event.

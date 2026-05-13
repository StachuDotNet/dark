# 07 — Human in the Loop

> Stachu's directive: "sometimes we need the human - for what? how will that fit in?"

## The core stance

**The human is a fallback materializer.** Same protocol as find/generate: they produce a `MaterializeResult`. Their output is first-class — cached as a real `PackageFn`, re-used forever, becomes part of the trace.

This framing is the load-bearing thing in this doc. Most "human in the loop" systems make humans a *workflow step*. PDD makes them an *execution participant*. That changes the design surface.

## When the runtime asks for a human

Concrete triggers, with the RTE / RecoveryPolicy outcome that gets us there:

### 1. Materialization fully fails

Both `find` and `generate` failed (timeouts, parse errors, sig conflicts), and `allowEmptyBody = false` for this call (e.g. the user marked it `@must_materialize`).

**Trigger event:** `MaterializationFailed` RTE → `AskUser` policy.

### 2. Capability not yet granted, `--ask` mode

A builtin needs `CapWriteNet`, and the session's mode is "ask on first use." First call surfaces a prompt:

```
[pdd] fn `fetchAndPost` wants to call HttpClient.post.
      Capability CapWriteNet is not granted.
      Allow this call? [y]es / [a]lways / [n]o / [N]ever
```

**Trigger event:** `CapabilityDeniedAsk` RTE → `AskUser` policy (per `06-builtin-permissions.md`).

### 3. Signature consensus disagreement

`find` returned `foo: List<'a> -> 'a` and `generate` returned `foo: Int -> String`. Both within budget. Default policy is first-wins, but for cases marked `@require_consensus`, the runtime asks:

```
[pdd] Two materializations for `foo`:
  [1] From package store:  foo: List<'a> -> 'a   (used 12× in stdlib)
  [2] From LLM:            foo: Int -> String    (description matched call-site context)
  [w] Write it yourself
Pick one:
```

**Trigger event:** `ConsensusRequired` → `AskUser`.

### 4. Repeated materialization failure on the same handle

Third time `foo` fails — we stop trying and ask. The session may be wrong about something (wrong corpus, missing context).

**Trigger event:** `RepeatedFailure(handle, attempts: 3)` → `AskUser`.

### 5. Trace divergence

Re-running the same input produces a different result. Common cause: LLM nondeterminism on materialization. The user should pick which is canonical.

**Trigger event:** `TraceDivergent(traceA, traceB)` → `AskUser`. (This is a *post-run* trigger, not mid-run.)

### 6. Explicit annotation

`@ask_user` on a fn → always pause at first call, even if find/generate succeed:

```
[pdd] `criticalDecision` is annotated @ask_user. The runtime synthesized this body:
  fun (n: Int) -> ...
Run it? [y]es / [n]o / [e]dit
```

**Trigger event:** `AskUserAnnotation(fn)` → unconditional `AskUser`.

### 7. The `breakpoint()` analogue

User-inserted in source: `Pdd.pause "explain this"`. Like `breakpoint()` in Python but with a free-form message.

## How the human enters — three modes

### Mode A: Synchronous (interactive CLI)

The default for the spike. Runtime pauses the parked frame, writes a prompt to stdout, reads from stdin. Other parked frames keep running if they have unrelated dependencies (the scheduler skips around the blocked one).

Implementation hook: a `humanResolver : HumanQuery -> Ply<HumanResponse>` field on `ExecutionState`. The default impl is a TTY prompt; tests inject a deterministic fake.

```fsharp
type HumanQuery =
  | AskMaterialize of Pending * candidates : List<MaterializeCandidate>
  | AskCapability of fn : FQFnName.FQFnName * cap : Capability
  | AskTraceDivergence of traceA : TraceId * traceB : TraceId
  | AskAnnotation of fn : FQFnName.FQFnName * synthesizedBody : Instructions
  | AskBreakpoint of message : string * locals : Map<string, Dval>

type HumanResponse =
  | RespAccept of MaterializeResult option
  | RespReject of reason : string
  | RespEdit of newBody : string
  | RespGrant of cap : Capability * scope : GrantScope
  | RespAlways of decision : string
```

### Mode B: Asynchronous (queue + inbox)

For longer sessions / batch jobs. Runtime parks the frame, writes a `pending-decision` record to `rundir/inbox/<sessionId>/<queryId>.json`. User runs `dark pdd inbox` later, answers each one, resumes:

```
$ dark pdd inbox
2 pending decisions for session abc123:
  [1] criticalDecision: 3 candidates  (parked 4m ago)
  [2] HttpClient.post: capability ask  (parked 2m ago)

$ dark pdd resolve 1
... interactive picker ...

$ dark pdd resume abc123
... runtime picks up parked frames ...
```

For PoC: Mode A only. Mode B is built on the same primitive — just an alternate `humanResolver` that persists queries instead of prompting.

### Mode C: Out-of-band (webhook / push)

Production-shaped: the runtime POSTs the query to a configured webhook (Slack, email, mobile push). User responds via reply or web UI. Runtime polls or receives webhook.

Out of scope for the experiment.

## What the human's answer *becomes*

The crucial design choice. Three options:

### Option 1: One-shot
The answer is used for this call, then discarded. Re-running the same program asks again.

### Option 2: Cached in the session
The answer is remembered for this session. Same call later → same answer, silently.

### Option 3: Cached in the package store (recommended)
The answer becomes a real `PackageFn`. The next run of *any* program that calls this name finds it via the normal find path. Persistent across sessions.

**Default for the spike:** Option 3 for materialization decisions. Option 2 for capability grants (sessions get to re-decide HTTP access; we don't want one "Always" to silently leak across days/projects). Option 1 for breakpoints.

## The trace shape

```
human_ask    queryId=q4f2 kind=AskMaterialize fnName=foo candidates=[...] elapsedSoFarMs=120
human_answer queryId=q4f2 chose=candidate2 response=Accept latencyMs=8400
materialize_done handle=h7a8 winningSource=Human hash=...
```

Note the latency — human responses are *slow*. The trace makes this visible so optimization later can target "fns that always need the human" for review-and-promotion.

## CLI commands (target)

```
dark pdd inbox                  # list pending decisions
dark pdd resolve <queryId>      # interactive resolution
dark pdd resume <sessionId>     # un-park frames after resolutions
dark pdd review <traceId>       # walk through trace, see all human-decision points
dark pdd promote <fnHash>       # mark a human-authored body as 'official' (becomes default for find)
```

## What about the inverse — when does the human ask the runtime?

The flip side. The user types in the CLI:
- "Materialize all pending fns" — force eager.
- "Show me the current state of `foo`" — dump candidate set.
- "Re-materialize `foo` with model=sonnet" — explicit upgrade.
- "Reject the current materialization of `foo`, try again" — invalidate cache.

This is the *control* side of the human-runtime interface. It's symmetric — the runtime asks queries, the user issues commands. Same data shape.

## Skipping it on Day 1

For Day 1 of hacking, you can ignore this entire doc. The default policy is `EmptyBody` on materialization failure, full grants on all capabilities. The human-resolver is just `failwith "TODO"`. Add the infra later when the cases come up.

But: **building the `humanResolver` field on `ExecutionState` is cheap** (one record field, one impl, one test). Putting it in early means later additions don't need an interpreter change. Worth doing in week 1.

## Connection to other docs

- `02-libexecution-changes.md` — add `humanResolver : HumanQuery -> Ply<HumanResponse>` to `ExecutionState`.
- `05-tolerant-runtime.md` — `AskUser` is one of the four recovery policies.
- `06-builtin-permissions.md` — capability-ask is the main human-trigger.
- `08-tracing-as-artifact.md` — `human_ask` / `human_answer` are trace events. The trace records human contribution alongside find/generate.

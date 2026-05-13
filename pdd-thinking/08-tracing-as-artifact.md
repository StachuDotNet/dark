# 08 — Tracing as Artifact

> The trace is the program. The source is a sketch + cache.

**Status:** Stub. To be deepened.

## What the trace must capture

A trace is an append-only JSONL of events. Event kinds:

```
session_start    sessionId, timestamp, command, env
program_loaded   instrCount, pendingHandlesAtLoad: [...]
materialize_start handle, name, sigHint
candidate        handle, source (find|generate), elapsedMs, sig, hash
materialize_done  handle, winningSource, winningHash
frame_park        frameId, blockedOnHandle
frame_resume      frameId, withHash
call              frameId, fnHash, args
return            frameId, value
recovery          loc, policy, reason, substituted
capability_check  builtin, capability, granted
human_ask         frameId, prompt, candidates
human_answer      frameId, chosen
session_end       result, elapsedMs, recoveryCount
```

## Why traces are the artifact (not just observability)

1. **Replay** — given a trace, you can re-run with the same materialization choices and get the same answer. Caching the materialized hashes makes this fast.
2. **Diff** — two runs of the same program can be diff'd at the trace level. Useful for review.
3. **SCM** — branches store traces. Merging compares cache entries (same hash = same body).
4. **Distribution** — ship the trace + the cache. Recipient's runtime can re-materialize anything missing.

## Implementation

- Trace lives at `rundir/traces/<sessionId>.jsonl` for the PoC.
- Streamed write — every event flushes (cheap, single-process).
- The existing tracing infra (`Tracing.fs`?) plus new event kinds.
- The CLI gets `pdd trace show <id>`, `pdd trace replay <id>`, `pdd trace diff <a> <b>`.

## Connection to Stachu's existing tracing work

The `dl-tracing.md` vault note already says:
- Every CLI script run, HTTP handler exec records a trace
- Traces are replayable
- Conversations as CRDT-like mutable list

PDD just makes the materialization events first-class in this same format. **Don't build a new trace system — extend the existing one with the new event kinds.**

## Trace size

A program with 100 materializations and 1000 calls might produce a 1MB JSONL. Fine for the PoC. Compression and pruning later.

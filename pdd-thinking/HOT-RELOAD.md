# Hot Reload — From First Principles

The spike had hot-reload via mtime polling on JSONL files. Worked
for a demo; not principled. This is the from-scratch version.

## What hot-reload is for

In recursive live development, the user is steering a process. Code
under their cursor changes mid-execution. The runtime should pick
up changes without restart. Same applies for AI-materialized
refines, SCM sync pulls, remote pair-programmer edits, and even
the language itself being updated mid-session.

The right framing: **hot-reload is just one consumer of the
`BodyChanged hash` event** from `EVENT-STREAMS-AND-PARKING.md`. It
isn't its own subsystem; it's a reactive policy layered on the
existing event stream.

## What triggers a reload

All reduce to "this name now refers to a different body":

- **Local edit** (user typed) → produces an Op, applied to local
  state, emits `BodyChanged`
- **Refine** (PDD or user-initiated) → mutates a `PackageID` body
  → emits `BodyChanged`
- **Materialization** (PDD-completed for first time) → emits
  `Materialized name body` (which subsumes `BodyChanged` for the
  first-ever publish of that name)
- **SCM commit** (Op accepted into a branch) → emits
  `BodyChanged` for affected locations
- **Remote sync** (an arriving SyncOp updates a location locally)
  → emits `BodyChanged`
- **Branch switch** (user moves between branches) → bulk
  `BodyChanged` for all locations that differ
- **Snapshot bootstrap** (first install or major upgrade) →
  initialization, not really "hot-reload" — frames don't exist
  yet to be affected

The unifying point: there's one event kind, multiple producers,
the same subscriber semantics for all.

## Granularity — per-fn (and per-something-else?)

The natural granularity is **per-fn-by-location** for user-defined
code and **per-hash** for committed code:

- A `PackageID`-referenced location changes → frames using that
  location are affected.
- A `Package(hash)` value never changes (content-addressed). If
  a location *was* pointing at hash X and now points at hash Y,
  that's an Op on the location, not a change to hash X. Frames
  holding X continue to use X (correct — hashes are immutable);
  frames resolving by location see Y.

This is the **right** granularity story for free, once you commit
to PackageID-by-location + Package-by-hash from `CLAIMS.md`.

Other granularities also exist and reduce to the same model:
- Per-type: same — type definitions are locations too
- Per-value: same
- Per-trace: traces are values; same
- Per-DB-schema: needs migration, not just reload — separate
  flow

## The interaction with parked frames

This is the subtle part. A frame may be parked on a `Pending` that
just got materialized. Or — harder — a frame may be *currently
executing* an old body when a new one is published.

Case 1: **Parked frame, body arrives.** Trivial. The parked frame
was already waiting on `Materialized` for that name. The
event fires, the frame wakes, it uses the new body. Standard
event-stream behavior. (See `EVENT-STREAMS-AND-PARKING.md` §
parking semantics.)

Case 2: **Frame mid-execution of old body when new body is
published.** This is the interesting one. Three choices:

- **Finish-then-update** (default). Current frame runs to
  completion with the old body. Future calls hit the new body.
  Safe; predictable; matches Erlang/BEAM semantics. Probably right.
- **Preempt** (force). Mid-execution frame is killed; new
  evaluation starts with new body. Aggressive; useful for
  development; not safe in production.
- **Both** (race). Run old frame to completion AND start a new
  frame; user sees both results. Useful for trace diffing.
  Niche.

Default: finish-then-update. The viewer should surface that
"frame X is using the old body; will pick up the new on next
call." Make staleness visible, not silent.

## The contract — what guarantees does reload give

What an event consumer can rely on:

- **Atomic body update.** A `BodyChanged` event names exactly
  one new body. No partial updates. Either the new body is
  installed everywhere, or nowhere.
- **Causal ordering.** Events from one source are sequenced. If
  refine then refine arrives in order, both reloads apply in
  order. Cross-source ordering is by timestamp + tie-breakers
  (see `EVENT-STREAMS-AND-PARKING.md`).
- **Idempotence.** Replaying a `BodyChanged` to its same value
  is a no-op. (Important for sync replay.)
- **Trace recorded.** Every reload appears in the trace. "At T=5
  the body of `renderHome` changed from X to Y."

What the consumer **doesn't** get for free:

- Old frames retroactively re-evaluated. (No time-travel
  evaluation.)
- Cached results invalidated. (Caching policy is separate; the
  reload says "this changed" but doesn't tell every cache to
  flush. Caches subscribe and invalidate themselves.)
- Tests re-run. (Test runner subscribes; it's a choice.)
- Dependents recompiled. (Compilation is per-call; nothing to
  recompile.)

## How it relates to SCM branch ops

Switching branches is a bulk reload: many locations change at
once. Mechanically, the branch-switch produces a stream of
`BodyChanged` events (one per affected location), atomically as a
single transaction in the underlying store. Subscribers see them
as a batch (the stream protocol can carry a "transaction-end"
marker so subscribers can react after the full set arrives).

Merge ops, rebase ops, approval-acceptance — all produce
`BodyChanged` events for their affected locations. The reload
machinery doesn't care what produced the event.

## The viewer

The viewer is a heavy `BodyChanged` subscriber. Every time a body
changes, the fn-card in the viewer updates. Materialization
animations, refine highlights, branch-switch transitions — all
just renderings of the event stream.

The viewer should also subscribe to `FrameParked` / `FrameWoken`
so users can see what's stuck. And to `ConflictResolved` so users
can see what was decided.

This is straight composable-MVU (B7). The Model is the visible
state; the Msgs are events from the bus.

## How it interacts with capabilities

A reload can install a body with different effective caps. The
old body had caps `{CapPure}`; the new body now needs
`CapWriteNet`. Three cases:

- The session already grants `CapWriteNet`: next call works fine
- The session doesn't grant `CapWriteNet`: next call hits the
  cap-dispatch (B2 conflict, AskHuman resolution) — the user
  is asked, can grant or deny
- The new caps are *less* (the new body is more restrictive):
  always fine

The viewer should surface "this reload changed the cap surface"
when the difference is visible (especially when caps grew).

## How it interacts with conflicts

A reload that would break callers (signature changed, types
incompatible) should trigger conflict-resolution rather than
silently break things. Mechanically:

- Old body had sig `String -> Int64`
- New body has sig `String -> Result<Int64, Error>`
- Callers using `String -> Int64` now have a type-mismatch
- → emit `Conflict.TypeMismatch` for each caller
- → dispatch decides: substitute (e.g. assume Ok), park (await
  user fix), ask-human, fail-loudly

This makes "I changed my fn's signature" into a manageable
flow, not a sudden runtime explosion. Same dispatch, different
producer.

## Open questions

- **Cross-instance reload latency.** A sync-arrived `BodyChanged`
  travels over the network. What's the SLA? Sub-second target
  per the speed concerns in FRONTIER.
- **Mid-frame preemption knob.** Should the user be able to
  toggle between finish-then-update and preempt globally, per
  session, per fn? Probably session-level for v1.
- **GC of old bodies.** If a frame is using old hash X and we
  install hash Y, can we GC X? Only when no frame holds it.
  Standard reference counting; tractable.
- **Hot-reload of the runtime itself.** Can we hot-reload the
  interpreter? The materializer? The viewer? Each is increasingly
  hard but increasingly desirable. The "Dark interpreter in Dark"
  long-term goal (FRONTIER) makes this real — once the interpreter
  is Dark code, reloading it is the same machinery.
- **What about types?** A type definition changes — does every
  value of that type need migration? Probably separate flow:
  type-changes are a SCM op that emits a migration request
  conflict, not a silent reload. Type-stability is part of
  contract stability.

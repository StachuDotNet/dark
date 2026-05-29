# PDD — Algorithm Sketch

> **INCOMPLETE.** This is a working sketch, not a settled algorithm.
> We're really building a *recursive coding agent* on top of
> Darklang's strengths (types, package store, traces, capabilities,
> SCM). Find and Generate are two strategies among many we'll need.
> Think ground-up. See `FRONTIER.md` for the broader strategy
> space.

## In one paragraph

The interpreter parses pseudocode (names + signatures, often
without bodies). For each unrecognized name it asks a **coordinator**
for a body. The coordinator races a set of strategies in parallel —
search the package store, search broader corpora, ask an LLM,
synthesize from examples, ask a human, replay from a prior trace.
First non-failure wins. Meanwhile, eval *starts* as soon as
anything is runnable; when a call hits a still-unresolved name, the
frame **parks** and the scheduler runs other ready frames. When
materialization completes, parked frames wake. If nothing succeeds
within the budget, the runtime substitutes a typed default
(`defaultFor returnType`) — or asks the human, or fails — per the
resolved conflict-resolution policy. Generated bodies are themselves
pseudocode with their own holes, so the process is fractal.

## The coordinator

Single dispatch point. Takes a `Pending` (name + sig + call-site
hints) and returns a `MaterializeResult` (a body, an EmptyBody, a
Failure, or a Parked signal that more time is needed).

Internally it consults strategies in some order, possibly in
parallel:

- **Cache** — already materialized in this session/branch
- **Corpus search** — name+arity match against the package store
  + curated external corpora
- **Synthesis from examples** — if the user has provided input/output
  pairs, search Dark's grammar for a body that satisfies them
- **LLM generate** — prompt-based body generation, ground the LLM
  with sig + nearby type defs from `package_functions`
- **Human ask** — surface a query, park until a response
- **Trace replay** — if a prior trace covered this name, reuse its
  materialization

Order and parallelism are policy. The coordinator is the
mechanism. (Today only "LLM generate" is implemented; everything
else is sketched.)

## Sig consensus

When parallel strategies on the same name produce different
signatures — which wins?

- **First-non-failure wins** (default) — whoever races to a
  materialization first claims the name. Other results are logged
  but discarded.
- **Constraint-driven** (when call sites constrain the type) —
  candidates that violate call-site type expectations are rejected
  before claiming. Triggers when first-wins thrashes.

Identity rule: same name + same lexical scope = same `Pending`
handle. Don't create new handles for repeat references in one
session. Canonicalization happens at parse/lower time.

Recursion through pending: if `foo`'s materialized body references
`foo`, the in-flight handle is reused — no thrash, no infinite
loop.

## Parking and event flow

Eval is a scheduler. Frames advance until they hit a pending name;
then they park on a wait-list keyed by that handle. When the
coordinator publishes a materialization event for that handle, all
parked frames wake.

The wait/wake protocol wants to be a low-level Darklang concept —
ideally event streams or event graphs with waiters — so the same
machinery serves the materializer, the SCM sync, the human-async
flow, and (eventually) other agentic operations. The current F#
`EventSink` is a simpler shape; `FRONTIER.md` sketches what it
should become.

## Conflicts and resolutions

When something is unresolvable, the runtime needs a *policy* to
decide what to do — not a hardcoded "raise FnNotFound." Today's
behavior is mostly "fail." We need a richer system that supports:

- Substitute a default (`EmptyBody`) and keep going
- Park the frame and wait longer
- Ask the human
- Try a different strategy
- Fail loudly with a typed error

This conflicts+resolutions system should be a base concept inside
LibExecution, used by both PDD and SCM ops. `FRONTIER.md` has more.

## What's recursive

- **Bodies materialize bodies.** A generated function calls other
  pending functions, which materialize their own bodies — fractal.
- **Tests materialize.** When the agent decides a body needs
  verification, it can materialize tests (or ask the human to
  contribute them).
- **The materializer itself should be materializable.** Eventually,
  the strategy that picks strategies is itself a Dark function the
  user can refine.

## What's deliberately out of scope here

Anything implementation-specific in F# — that's separate scope. The
*algorithm* is the policy + dataflow; the *substrate* is the F#
code that hosts it. We want as much of the algorithm as possible
written in Dark itself, with the F# substrate doing only what it
must (low-level event/wait primitives, conflict resolution
plumbing, capability checks at builtin call sites).

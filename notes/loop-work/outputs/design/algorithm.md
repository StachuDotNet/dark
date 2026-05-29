# PDD — Algorithm sketch

> **INCOMPLETE.** A working sketch, not a settled algorithm. What
> we're really building is a *recursive coding agent* on top of
> Darklang's strengths — types, package store, traces, capabilities,
> SCM. Find and Generate are two strategies among many we'll need.
> Think ground-up. See `FRONTIER.md` for the broader strategy space.

## In one paragraph

The interpreter parses pseudocode — names and signatures, often
without bodies. For each unrecognized name it asks a **coordinator**
for a body. The coordinator runs a set of strategies, possibly in
parallel: search the package store, search broader corpora, ask an
LLM, synthesize from examples, ask a human, replay from a prior
trace. It then *weighs* the results rather than taking whoever
finished first (see "Sig consensus"). Meanwhile, eval *starts* as
soon as anything is runnable; when a call hits a still-unresolved
name, the frame **parks** and the scheduler runs other ready frames.
When materialization completes, parked frames wake. If nothing
succeeds within the budget, the runtime substitutes a typed default
(`defaultFor returnType`) — or asks the human, or fails — per the
resolved conflict-resolution policy. Generated bodies are themselves
pseudocode with their own holes, so the process is fractal.

## The coordinator

Single dispatch point. Takes a `Pending` (name + sig + call-site
hints) and returns a `MaterializeResult` — a body, an `EmptyBody`,
a `Failure`, or a `Parked` signal that more time is needed.

Internally it consults strategies in some order, possibly in
parallel:

- **Cache** — already materialized in this session/branch
- **Corpus search** — name + arity match against the package store
  plus curated external corpora
- **Synthesis from examples** — if the user has provided input/output
  pairs, search Dark's grammar for a body that satisfies them
- **LLM generate** — prompt-based body generation, grounding the LLM
  with the sig + nearby type defs from `package_functions`
- **Human ask** — surface a query, park until a response
- **Trace replay** — if a prior trace covered this name, reuse its
  materialization

Order and parallelism are policy. The coordinator is the mechanism.
(Today only "LLM generate" is implemented; everything else is
sketched.)

## What a materialized body actually is

A body need not be Dark code. Materialization can crystallize into
Dark, but it doesn't always — and sometimes it *deliberately never
does*. The coordinator may return any of:

- **Dark code** — the body is concrete Dark, with its own holes that
  recurse. The crystallized case.
- **An LLM wrapper** — the body *is* a delegated LLM call. Each
  invocation prompts the model; the function's behaviour lives in
  the prompt + model, not in checked-in Dark.
- **An LLM-agent wrapper** — the body delegates to an agent (an LLM
  with tools/loops), again resolved per-call rather than crystallized.
- **Text with an expected type** — a dummy value: a typed
  placeholder standing in for a real body, enough to type-check and
  keep eval moving.

The wrapper cases are **"forever lazy."** They don't converge to
Dark on a later pass — delegation *is* the body. This is a feature,
not a stalled materialization: some functions are best left as a
model call. But it has a cost. A forever-lazy body re-runs the model
on every call, so its behaviour can drift between invocations, it
can't be statically read or diffed, and tightening it means more
iterations and real rework rather than a one-time crystallization.
Plan for that rework; it's not a transient.

(This mirrors `CLAIMS.md` Claim 1, "the source often starts as
lazy": lazy is not merely an early phase that always burns off — for
wrapper bodies it's the resting state. Keep the two in sync.)

## Sig consensus

When parallel strategies on the same name produce different
signatures — which wins?

The blunt rule, "first non-failure wins," is wrong as a default:
race order is mostly an artifact of latency, not quality. The
coordinator is a **judge**, not a stopwatch. It scores the candidate
results and picks among them, weighing signals such as:

- **Test-pass strength** — how many of the available tests pass, and
  how load-bearing they are. A candidate that passes the property
  tests beats one that merely returns first.
- **Type-fit** — agreement with call-site type expectations (see the
  constraint-driven mode below); candidates that violate them are
  rejected, not ranked.
- **Reviewer/judge signal** — a human's or an LLM-judge's preference,
  where one is available.
- **Cost** — a cheap corpus hit that satisfies the constraints can be
  preferred over an expensive generation that's only marginally
  better; conversely, spend more when the call site is load-bearing.

Two modes fall out of this as named policies:

- **Constraint-driven** (when call sites constrain the type) —
  candidates that violate call-site type expectations are rejected
  before they can claim the name. This is type-fit as a hard gate.
- **First-non-failure** — take the first runnable result. A
  degenerate, latency-optimal policy worth keeping only for the
  cheap path where candidates are interchangeable; it thrashes the
  moment they aren't, at which point the judge should weigh instead.

Scoring vs. race-order is itself policy. The coordinator supplies
the mechanism — collect candidates, expose their signals — and a
(materializable) policy decides how to weigh them.

Identity rule: same name + same lexical scope = same `Pending`
handle. Don't mint new handles for repeat references in one session.
Canonicalization happens at parse/lower time.

Recursion through pending: if `foo`'s materialized body references
`foo`, the in-flight handle is reused — no thrash, no infinite loop.

## What "stable" means during PDD iteration

The judge above reaches for *stable* results — but "stable" is a
property of a materialized item, not of the algorithm's mood. A
materialized body is **stable** when all of the following hold:

- **Named** — it has a `location` (owner + modules + name) and/or a
  content-hash.
- **Hashed** — content-addressable; the bytes hash to the same thing
  on any instance.
- **Validated** — it passed conflict + type checks at op-apply time
  (via `PackageOpPlayback`).
- **Durable** — persisted via a committed `package_op`
  (`commit_hash IS NOT NULL`).
- **Attributable** — the commit that introduced it carries an
  `account_id`.

WIP — uncommitted ops, speculative candidates mid-race, a body the
human hasn't accepted — is *not yet stable*. It's working state. The
PDD loop runs over working state and converges toward stable items;
each accepted materialization is the moment a candidate crosses that
line. This is what the coordinator is reaching for when it weighs
candidates: not just "runs," but "ready to be named, hashed, and
committed."

(Forever-lazy wrapper bodies complicate this: a wrapper can be
named, hashed, durable, and attributable — the *delegation* is
stable even though its per-call output isn't. Stability is a
property of the op that defines the body, not of the values it
produces.)

(This definition is the PDD-facing half of the "stable" notion in
`STABILITY-AND-SHARING.md`; that doc carries the sharing/wire side.)

## Parking and event flow

Eval is a scheduler. Frames advance until they hit a pending name,
then park on a wait-list keyed by that handle. When the coordinator
publishes a materialization event for that handle, all parked frames
wake.

The wait/wake protocol wants to be a low-level Darklang concept —
ideally event streams or event graphs with waiters — so the same
machinery serves the materializer, the SCM sync, the human-async
flow, and (eventually) other agentic operations. The current F#
`EventSink` is a simpler shape; `FRONTIER.md` sketches what it
should become.

## Conflicts and resolutions

When something is unresolvable, the runtime needs a *policy* to
decide what to do — not a hardcoded "raise FnNotFound." Today's
behaviour is mostly "fail." We need a richer system that can:

- Substitute a default (`EmptyBody`) and keep going
- Park the frame and wait longer
- Ask the human
- Try a different strategy
- Fail loudly with a typed error

This conflicts + resolutions system should be a base concept inside
LibExecution, used by both PDD and SCM ops. `FRONTIER.md` has more.

## What's recursive

- **Bodies materialize bodies.** A generated function calls other
  pending functions, which materialize their own bodies — fractal.
  (Except forever-lazy wrappers, which terminate the recursion by
  delegating instead of crystallizing.)
- **Tests materialize.** When the agent decides a body needs
  verification, it can materialize tests — or ask the human to
  contribute them. Those tests then feed the judge's test-pass
  signal above.
- **The materializer itself should be materializable.** Eventually
  the strategy that picks strategies — including the candidate-
  weighing policy — is itself a Dark function the user can refine.

## What's deliberately out of scope here

Anything implementation-specific in F# — that's separate scope. The
*algorithm* is the policy + dataflow; the *substrate* is the F# code
that hosts it. We want as much of the algorithm as possible written
in Dark itself, with the F# substrate doing only what it must:
low-level event/wait primitives, conflict-resolution plumbing, and
capability checks at builtin call sites.

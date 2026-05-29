# Capabilities

LLM-generated code will try to delete files, hit arbitrary network endpoints,
and exfiltrate secrets. An ungated runtime is a footgun. Capabilities are how the
runtime says "this code may do exactly these effects and nothing else."

The grounding fact on main: **builtins are the only impure boundary.** Pure Dark
code cannot touch the world — it can only compute. So gating effects means gating
builtins, and the existing 9-assembly Builtin split (`Pure`, `Http.Client`,
`Http.Server`, `Random`, `Time`, `Cli`, `CliHost`, `Language`, `Matter`) already
sorts builtins by effect category. We are not annotating from scratch; we are
reading the structure that already exists.

## Ops vs projections

A capability grant is an **op** — a fact written once ("this session may write
files"). Everything downstream is a **projection** of that fact: the runtime
gate's allow/deny decision, the LLM prompt's filtered builtin list, the audit
view of what was used. Keep the op small and authoritative; let the projections
be cheap and many. The mistake to avoid is letting a projection (e.g. a prompt
filter) become a second source of truth about what is allowed.

## PURE is free

Pure functions need no capability. Ever. There is no `CapPure` token to grant
because there is nothing to gate — a pure builtin, by definition, has no
observable effect. The capability machinery only ever asks a question when a
builtin reaches for the world. This keeps the common case (the overwhelming
majority of calls) entirely off the gating path.

## The Capabilities type — with nuance

The first instinct is a flat enum of effect tags. That is roughly right, but each
category carries its own shape, and several of them are not yes/no. Working
through them:

**HttpClient — sophisticated.** Outbound network is not one capability. The real
model wants host allowlists, method restrictions (read-only GET vs.
mutating POST/PUT/DELETE), and a separate gate for requests that carry secrets
(auth headers, API keys the caller may not realize are being shipped). The
detailed restriction grammar already lives in vault notes — *exact location
still to be found* — so this section captures the **shape** and flags the gap:

```fsharp
| HttpClient of HttpClientCap
// HttpClientCap should carry: allowed hosts, allowed methods,
// whether secret-bearing headers may be sent. TODO: reconcile with
// the existing vault restriction spec once located.
```

**HttpServer — probably modeled on HttpClient.** Inbound network plus a
server lifecycle (port binding is a long-running surface, heavier than a single
request). Until we know better, assume the same structured shape as HttpClient —
an allow-shape over bind targets and ports — rather than a bare boolean. Per-request
handlers run with the *caller's* capabilities, not the server's.

**Random / Time — yes/no.** Non-determinism, but low-stakes. A plain boolean each.
No structure needed.

**FileSystem — yes/no for now.** A single boolean gate for the first cut. But
this is the category with the most obvious future sophistication: per-directory
scoping (`read ~/project`, never `~/.ssh`), read-vs-write split, size/quota caps,
ephemeral-vs-persistent. Ship the boolean; leave a clear seam for the structured
version (mirroring the HttpClient shape) when it earns its place.

**Language — pure/safe needs nothing; eval is the hard case.** Most of
`Builtins.Language` is reflection (parse, typecheck, format) — pure, no gate. The
sharp edge is reflective `eval`: running code constructed at runtime. The question
is not "may you call eval" but "with what capabilities does the *evaluated* code
run." The answer that keeps the model sound: **evaluated code runs with no more
than the caller's own capabilities** — eval cannot launder an effect the caller
lacks. So `eval` itself needs no special grant; it simply propagates, never
amplifies.

**Matter — same shape as Language.** Package-store / SCM operations are mostly
reflective reads (cheap, ungated) with a few genuine effects (writing the package
store, AI-driven materialization). Same rule: the reflective surface is free; the
writing surface is gated; anything that *runs* stored code propagates the caller's
caps without amplifying them.

**Other CLI stuff — same? flag.** `Cli` / `CliHost` cover stdin/stdout/args and
host process management (subprocess spawn is the heavy one). These plausibly
follow the same pattern — boolean gates now, structured later — but the surface is
ragged enough that it warrants its own investigation pass before committing to a
shape. Flagged.

So the type is not a clean flat enum: a couple of booleans (Random, Time,
FileSystem-for-now), a couple of structured records (HttpClient, and likely
HttpServer), and a propagation rule rather than a token for eval-shaped things
(Language, Matter).

## Implications for LibExecution, top to bottom

With the nuance settled, here is how it threads through the runtime.

1. **Builtins carry their requirement.** A builtin must be able to state what it
   needs — `HttpClient` with a method, `FileSystem`, etc. Where that declaration
   lives (a field on `BuiltInFn` vs. a function the builtin owns) is the open
   representation question below; resolve it before retrofitting.

2. **The granted set lives on `ExecutionState`.** Conceptually `state.capabilities`
   — the authoritative op, read by the gate. Per-session, seeded from per-account
   defaults.

3. **The interpreter routes denials, it does not invent them.** When a builtin's
   requirement is not satisfied, the gate does not throw its own bespoke error. It
   constructs a `Conflict.CapabilityDenied` and hands it to `state.conflictDispatch`
   — the same dispatch that handles every other runtime conflict (see
   design/conflicts.md). Capability denial is a *producer of standard conflicts*,
   not new machinery. The dispatch returns one of the existing resolutions:

   ```fsharp
   match resolution with
   | Resolution.Substitute dval -> return dval        // skip the call, fill a value
   | Resolution.FailLoudly err  -> return! raiseRTE err // strict-mode default
   | Resolution.Park selector   -> return! park selector // wait for a grant event
   | Resolution.AskHuman query  -> return! askThenResume query
   ```

   Strict mode defaults to `FailLoudly`, which is exactly today's behavior for a
   gated builtin. Reverse-compatibility falls out for free: until a builtin
   declares a requirement, there is nothing to deny and the call fast-paths
   through untouched.

4. **Audit is a projection, recorded at the gate.** Every decision (granted,
   denied, substituted) is recordable at this one site. The schema for that lands
   *after* the design is settled, not now.

## Is `Set<Capability>` even the right representation?

The sketch put `capabilities : Set<Capability>` directly on `BuiltInFn` and had
the interpreter diff it against the granted set at every call. Two problems with
treating that as obvious:

- A flat `Set` cannot express the structured caps above (which host? which
  method?). A set-difference is the wrong operation for "is `example.com` in the
  allowed-hosts list."
- It forces the *interpreter* to know how to check every capability shape, which
  centralizes effect-specific logic in the wrong place.

Two candidate models, thought through:

**(A) Interpreter checks nothing; the builtin checks.** The builtin reaches into
`state.capabilities` itself and decides whether it may proceed — it already has
`state` in hand. This is maximally honest: the code that knows what
`HttpClient.get url` actually does is the same code that decides whether `url` is
allowed. No central enum the interpreter must keep in sync. The cost: the check is
invisible from the outside — you cannot compute a function's effective capability
set without *running* it, which defeats prompt-filtering and static audit, and
risks each builtin hand-rolling its own (inconsistent) check.

**(B) Each builtin registers a `checkCapabilities` function.** The builtin
declares, alongside its implementation, a pure predicate
`granted -> args -> CapDecision`. The interpreter calls it at the gate without
needing to understand the capability's internal shape; the builtin supplies the
shape-specific logic. This is *cacheable* — the decision for fixed
`(granted, args)` is stable — and it is *inspectable*: the registered function can
be consulted to derive effective caps and to drive prompt-filtering without
running the body. The cost: a little more code per builtin, and some redundancy
between the check and the implementation (both reason about the same args).

**Decision: lean toward (B), the registered `checkCapabilities` function.** The
inspectability is decisive — prompt-filtering, effective-cap computation for
user-defined functions, and static audit all need to ask "what would this need?"
*without* executing. (A)'s invisibility forecloses all three. The redundancy cost
of (B) is real but bounded, and a shared helper between the predicate and the
implementation collapses most of it. So: `BuiltInFn` gains a `checkCapabilities`
function (not a `Set`), the interpreter calls it at the gate, and the result flows
into the conflict dispatch as above.

**Two things, not one — a static category plus the dynamic check.** The function
alone is not enough, because prompt-filtering, effective-caps, and static audit all
need an answer *without args*. So a `BuiltInFn` declares both:

- A static **`caps : Set<CapCategory>`** — the coarse categories the builtin may
  touch (`HttpClient`, `FileSystem`, …). Arg-independent, cheap to read, this is
  what gets unioned for effective-caps and shown to a prompt filter. It answers
  "what *could* this need?"
- The dynamic **`checkCapabilities : granted -> args -> CapDecision`** — run only at
  the gate, with args in hand, for the precise decision ("is `example.com` in the
  allowed-host list?"). It answers "given *these* args, allowed?"

The static category is a sound over-approximation of the dynamic check: if `caps`
omits `HttpClient`, the builtin provably never needs it, so inspection is safe. A
pure builtin has `caps = {}` and no `checkCapabilities`. This is the split the
effective-caps computation below relies on — it unions the static `caps`, never the
dynamic predicate.

## Human interaction is async — and, for now, absent

The sketch's `--ask` flag undersells the problem. Asking a human is not a
synchronous prompt: it is an **async event** that may time out, and on timeout the
runtime must fall back to a non-human resolution (deny, or substitute). That is
real coordination, not a dialog box.

**For now: no interactive grants.** They complicate the runtime and, worse, make
testing hard (a test cannot block on a human). We defer them. But sketch the
structure so the seam is right:

- A would-be-denied call with interactivity enabled emits `Conflict.CapabilityDenied`.
- The dispatch returns `Resolution.AskHuman query`; the frame parks on the
  human-answer event (design/event-bus.md, design/async.md).
- A timeout on that park resolves to the non-human fallback — this is the part the
  naive `--ask` misses.
- On answer, capability state updates, the parked frame wakes, the gate re-runs.

Because interactive grants are deferred, **builtin grants are instance-specific
for now**: a capability granted on one instance is local to it and does not sync.
The audit of *uses* still syncs (it rides the trace); the grant itself does not.

## Frame parking

When a denial resolves to `Park` (or `AskHuman`, which is a park on a human
event), the affected frame suspends and the scheduler runs other ready frames.
This is the same parking mechanism the event bus uses for async I/O and for
grants flowing over the bus (design/event-bus.md). A capability grant arriving as
an event is just another value a parked frame can wake on. Nothing
capability-specific is needed in the scheduler — caps reuse the substrate.

## User-defined functions — effective caps

User-defined and PDD-materialized functions do not *declare* requirements; they
have an *effective* set computed from the builtins (and other functions) their
body reaches. This is buildable quickly and is itself an ops-vs-projections move:
the builtin requirements are the ops; a function's effective set is a projection
over its call graph.

```fsharp
effectiveCaps fn =                                  // : Set<CapCategory>
  fn.body
  |> walkCalls
  |> unionOver (function
       | Builtin name      -> (lookupBuiltin name).caps  // the static category set
       | Package hash      -> effectiveCaps (lookup hash)   // memoize by hash
       | PackageID id      -> effectiveCaps (lookupById id) // recompute on change
       | _                 -> empty)
```

It unions the static `caps` categories, never the dynamic `checkCapabilities` — so
the effective set is "which categories could this reach," not "is this specific call
allowed." The latter stays a runtime decision at the gate. This is exactly why the
two-part declaration above is needed.

Open within this: recursion is a fixed point (start empty, iterate to convergence
— one pass in practice); `Package(hash)` bodies are immutable so cache by hash;
`PackageID` recomputes when the body changes. A user may *tighten* a declared set
below the computed one (defense in depth) but never loosen it (no laundering — the
same rule as eval).

## Meta-connection

Step back and the shape rhymes with something larger. Grants are append-only ops;
runtime decisions are projections; frames branch, park, and rejoin around events.
That is **distributed event sourcing plus branched MVU** — capabilities may be one
facet of that single substrate rather than a standalone subsystem. See
design/distributed-event-sourcing.md.

---

*Summary:* Capabilities gate the only impure boundary (builtins) — pure code is
always free; nuanced per-category shapes (structured HttpClient/HttpServer,
boolean Random/Time/FS, propagate-don't-amplify for eval) are checked via a
per-builtin registered `checkCapabilities` function whose denials route through
the standard conflict dispatch, with interactive grants deferred and the whole
thing reading as distributed event sourcing plus branched MVU.

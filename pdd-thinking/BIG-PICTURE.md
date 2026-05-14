# PDD — Big-Picture Reflections

*Things we haven't thought about yet. How to lean harder into the
recursive nature of the experience. How to actually use Dark's
strengths instead of treating it as a deployment target.*

Companion to SPIKE-LEARNINGS.md (what we learned) and INTEGRATION-PLAN.md
(how to merge). This doc is the **horizon scan** — what could PDD be
in 6 months that it isn't today?

## Part 1 — Things I haven't thought about

### 1.1 PDD without an LLM

Today the materializer is welded to OpenAI. But "pending → real" is a
generic substitution; the LLM is just one strategy. Other strategies:

- **Search a corpus of known bodies.** If a fn called `factorial` is
  pending, look it up in the existing Stdlib + package_fns first.
  Cheap. Deterministic. Often correct.
- **Synthesize from examples.** If the user has provided
  `factorial 5L = 120L` as a test, run a small synthesis search over
  Dark's grammar. For arithmetic this is fast; for HTML rendering
  it's infeasible. But the **same Pending interface** supports both
  strategies — the runtime doesn't know or care.
- **Ask the user.** A REPL mode: pending fn shows up, user types the
  body. The "LLM" is the human. Still works.
- **Replay from a recording.** If you've seen `renderHome` materialized
  to body B before, just use B. No LLM call.

This means **the materializer should be a `List<Strategy>`**, tried in
order: cache → corpus → synthesis → human → LLM. Each strategy has a
predicate ("can I produce a body for this signature?") and an action
("here's the body"). LLM is the last resort, not the only resort.

We never built this. Worth doing in Wave 2.

### 1.2 PDD for *types*, not just fns

`FQTypeName.Pending` is conspicuously absent. Pending types would let
the materializer produce **a type definition + a default value + an
implementation of common typeclass-shaped fns** (Eq, Show, Json codec)
in one shot.

Example: `type Address = Pending` with site usage
`{ street = "1 Main"; city = "SF" }`. The materializer infers the
record shape from usage, names the fields, emits a record type, and
emits printers/parsers. Then the field accesses just work.

This is *a much harder problem* (the LLM has to infer structure from
multiple sites, not from a name + signature). But it's where the
real ergonomic win lives — `Pending fn` is a productivity tool;
`Pending type` is a paradigm shift.

### 1.3 The reverse direction: deletion as a first-class operation

We have `promote` (Pending → committed). We don't have **demote**
(committed → Pending). Why would we want it?

- "This fn worked but I want the LLM to redo it with a new prompt."
- "This fn's behavior is wrong — strip the body, keep the signature,
  let it regenerate."
- "I want to A/B two bodies: the current one and a fresh
  materialization. Demote, materialize, score."

`dark pdd demote <name>` → set the body to `Pending`, re-trigger on
next call. Cheap to add. Would change how people iterate.

### 1.4 Multi-LLM consensus

Today: one call to gpt-4o-mini per fn. Sometimes: a 2nd call if
QA-tests fail.

What if: **N LLMs in parallel** (4o-mini + gpt-4o + claude-haiku +
claude-sonnet), keep the body that passes the most QA tests +
shortest length? Or N calls to the *same* LLM with different temps,
then pick.

This is "self-consistency decoding" at the architecture level.
Probably 3-4× the cost per fn but **much** higher first-try success
rates, which matters in a long pipeline (10 fns × 90% = 35% chance
of one failing; 10 fns × 99% = 90% chance all succeed).

### 1.5 PDD as a build artifact, not a runtime decision

Today PDD happens at runtime, on first call. But we could have
**`dark pdd build`** → walk the source, find every Pending, batch-
materialize all of them, write committed snapshots. Then run with a
no-materializer Dark binary.

Production deployments don't *want* surprise LLM calls on the hot path
(latency, billing, non-determinism). They want PDD to be a **build-
time tool**, like a code generator. The runtime sees only "real" fns.

This is a huge mode that we haven't built. The same materializer
machinery, just hoisted out of the request path.

### 1.6 PDD audit logs as training data

Every PDD materialization writes a log:
`(name, sig, prompt → body, QA-pass/fail, refines, finalBody)`.

After 10K uses across many users, **that's a training set**. A small
fine-tuned model (or even just a retrieval index) on user's own PDD
history would beat a generic LLM at "produce a body in *my* style"
within months. The spike already has the data pipeline; nothing
captures and uses it.

### 1.7 Versioned migrations

Pending today = "no body yet." But the same shape generalizes to
"the body has changed; old data needs migration":

```dark
let MyConfig = Pending(reason = "schema-changed-2026-05-14")
```

When loading old persistent state shaped by the *old* `MyConfig`, the
materializer is invoked with the OLD shape + new shape and asked to
generate a migration fn. This is a *very* Dark-shaped thing — Dark
already has typed package fns + a hash-keyed registry, so you can
literally store `migrate_v1_to_v2 : OldShape → NewShape` as a Pending,
LLM materializes it, QA-test it on a sample, run on all rows.

Solves a real pain (schema migrations) using exactly the spike's
machinery. We haven't even sketched this.

### 1.8 Speculative materialization

While the user is typing in the REPL, materialize fn bodies for
**plausible-but-not-yet-used** fn names. Like predictive prefetch.
The user types `let html = renderHo` — kick off a materialization
for `renderHome`, `renderHomepage`, `renderHotProduct`. By the time
they hit enter, one is ready.

Wasteful in tokens. Fast in UX. Worth experimenting with at high
PDD_PARALLEL settings.

### 1.9 PDD-as-LSP

Today the loop is REPL-style. But a `dark` LSP server with PDD support
would let editors:

- Show "this fn is Pending" as a code action.
- Trigger materialization inline with a single keypress.
- Show the LLM's pending-fn-body suggestion as a ghost suggestion
  (cursor preview), accept-on-tab.
- Show the QA tests in a side pane; let user accept/reject one at a
  time.
- "Refine this fn" as a code action with diff preview.

This is the IDE story. We have the runtime; we don't have the LSP.
This is **where Dark's competition lives** (Copilot, Cursor) — and
PDD has a fundamental advantage: the result is *evaluated*, not just
suggested.

### 1.10 The economic question

We've spent zero time thinking about cost. A 30-route HTTP server with
all-creative renderers, refined to settled at gpt-4o, is **maybe $0.50
to materialize from scratch**. A user iterating on a 200-fn project
for a week is **maybe $20 of LLM**. The cache hit rate is high (cold
boot is one-time per fn version) so steady-state cost is low.

But: who pays? If PDD is a Darklang Cloud feature, Darklang eats it
and charges per-app. If PDD is local-only with user-provided keys,
the user eats it but feels in control. We've been in local-only mode
because it's simpler — but the multi-tenant pricing model is
unresolved.

## Part 2 — How to make the recursive nature better

The spike's most interesting moment was when we realized `pdd promote`
was a stand-in for `git commit`. That observation cascaded — every
SCM concept maps onto PDD. We've barely used the implications.

### 2.1 Branches

Today there's one `promoted.jsonl`. So there's one "branch."

But what if `dark pdd branch experimental` forked the working state,
let you iterate freely, then `dark pdd merge experimental → main`?

The mechanism is dead simple — append-only files plus a per-branch
manifest. The UX is: "I'm trying a riskier prompt for `renderHome`,
let me do it on a branch, compare." This is **the missing primitive**
for serious PDD experimentation.

### 2.2 Bisect

`git bisect` finds the commit that introduced a bug. The PDD analog:
`dark pdd bisect <fnName> <test>` — given a test that fails today and
passed at some prior committed snapshot, binary-search the snapshot
history to find which refine broke it.

This is unique to PDD because **we have a body-version history per
fn**, not just per-file. Per-fn bisect is finer-grained than git's.

### 2.3 Blame

`dark pdd blame <fnName> <line>` — which refine introduced this line?
The diff command gets you halfway there but doesn't attribute to a
specific session/timestamp.

Useful when reviewing: "who/what said to use a list comprehension here
instead of `map`?" Answer: "refine session 0x4a3b on 2026-05-14 at the
prompt `<text>`."

### 2.4 PDD-on-PDD: the materializer materializes the materializer

This is the **most recursive** thought.

`materializeFn : (Name × Sig × Hints) → Task<Body>` is itself a
function. Today it's an F# function with hand-tuned prompts. But it
could be **a Dark fn that the user can refine** — a Pending fn with
a thin F# wrapper.

`Stdlib.PDD.materialize` would be a Pending fn whose body, today,
calls `Stdlib.OpenAI.complete` with a prompt and parses the response.
A user can `dark pdd refine Stdlib.PDD.materialize` to change *their*
materialization strategy — better prompts, different model, retry
logic, multi-LLM consensus from 1.4. Their materializer evolves with
their codebase.

This is the path to making PDD a **Dark library** that's user-
modifiable, not an F# subsystem that's vendor-controlled.

### 2.5 Test-as-spec, body-as-prediction

We use QA tests as a gate ("did the LLM get it right?"). But tests
are also a **spec** ("here's what 'right' means"). Recursive use:

1. User writes 3 input/output examples for `factorial`.
2. PDD materializes the body.
3. The QA test runner *generates* more tests by mutation
   (`factorial 0`, `factorial 1`, big numbers, negative).
4. PDD materializes a body that passes both the user's tests AND the
   generated tests.
5. The user inspects the generated tests; promotes the ones that
   capture intent; deletes the ones that don't.
6. Loop.

This is the **Hindley-Milner-meets-LLM** pipeline. Specs grow
alongside bodies. The spike has the test runner; it doesn't generate
new tests; it doesn't ask the user to curate.

### 2.6 Refine-on-failure (not just refine-on-demand)

Today `pdd refine --watch` runs in the background and refines the
**least-refined fn**. Naïve. Better: refine the fn that **most
recently failed in production** (caught error, ran-over-budget, QA
test regressed). The runtime has all this signal; it isn't fed back.

The materializer should subscribe to a feedback stream:
`<name> failed with <reason> at <site>`. That signal weights which
fn gets the next refine cycle. **Reinforcement learning at the
codebase level.**

### 2.7 Cross-fn refactor as a single PDD call

Today: refine 1 fn at a time. But: "make these 5 fns more
consistent" is also a refine. The prompt would be "given these 5
bodies, produce 5 new bodies that share helpers / agree on naming /
factor common patterns." Submit, replace all 5 working revs in one
session.

This is a **multi-body refine**. New shape, but the same machinery —
you just bundle 5 fns into one prompt + 5 bodies in one response.

### 2.8 The history-as-explanation

`dark pdd history` shows working revs and committed snapshots. What
it doesn't show: **why** each rev exists.

If every refine recorded its triggering prompt (or the user's
free-text reason), history becomes a **changelog**. "Rev 3:
'add a nav bar with anchored sections' → here's the body." This
turns PDD into a self-documenting development log — better than git
log because it's at the fn level.

We have the data (the prompts go through the materializer); we just
don't expose them on history.

## Part 3 — Using Dark's strengths

The spike treats Dark as **a runtime to ship the materializer into**.
That's leaving leverage on the floor. Here's what Dark uniquely
offers that we haven't used.

### 3.1 Dark has structural canonical hashing already

`PT.PackageFn.Hash` is a content-hash of the AST. **PDD body
deduplication is free** — if two materializations produce
structurally identical bodies (modulo formatting), they hash equal,
and the cache hits.

Today our cache keys on name. It should key on hash. Two different
PDD users in two different projects writing `factorial` would share
a cache entry. **Network-effect benefits compound** — every user
makes everyone faster.

### 3.2 Dark's package_fns are content-addressable and durable

We're storing PDD fns in `promoted_hashes.jsonl` (a sidecar). But
Dark's `package_functions` SQLite table is already content-addressable
+ durable + queryable. **Wave 3** moves to it; but the deeper move
is: every materialized PDD fn flows through the same store. There's
one fn registry, not two.

This isn't just integration — it's **PDD becoming Dark's preferred
authoring path**. Hand-authored fns and PDD-materialized fns share
storage; queries don't distinguish; future tooling (search, blame,
package install) Just Works.

### 3.3 Dark's tracing system is *built* for materialization observability

Dark already records per-fn-call traces (TLID → values seen). The
materializer is a perfect tracing consumer — every materialization
should leave a trace, so when a fn fails 3 calls later you can see
"materialized at T0 by prompt P, then called at T1 with values V,
failed at T2." We log to JSONL by hand. Dark's tracing would index
it natively.

### 3.4 Dark's types as the prompt's grounding

Today the prompt to gpt-4o-mini contains the fn signature as a
string. But the fn signature is **a Dark type expression** — the
LLM could be given the type ground-truth in Dark's own type syntax,
plus links to other types in the package_fns registry. The LLM can
*resolve* references it doesn't know by name-lookup, not by guessing.

This is much harder to do in F# (you'd hand-walk the type
expression). It's a one-liner in Dark: `prettyPrintType ty`. So:
**the materializer wants to be in Dark.**

### 3.5 Dark's HTTP server makes the materializer addressable

`dark pdd run "Builtin.httpServerServe 9876L (fun req → renderHome)"`
already works. The next move: **materialize as an HTTP endpoint**.

```dark
let materialize = fun req →
  let body = req.body |> parseJson<MatRequest>
  let result = Stdlib.PDD.materialize body.name body.sig body.hints
  result |> Json.print
```

A 6-line Dark program that exposes the materializer as a service.
Other services (or other languages, like Python) can call it without
caring it's F#. Eventually: this **is** the materializer, hosted on
Dark Cloud, multi-tenant.

This is what "Dark's strength" means. We've been writing the
materializer in F# inside Dark. The leverage is writing it in Dark
on top of Dark — and shipping it as a normal Dark program.

### 3.6 Dark's hash-as-identity for distributed materialization caches

If `fnHash → body` is the cache key, and the body is just a string,
then the cache **is a CDN**. Different users hit the same hash, hit
the same cached body, no LLM call. Darklang Cloud could host a
shared cache; users opt in; the long tail of "we've all written
factorial" goes from $0.0002/call to free.

This is content-addressable storage 101 + LLM economics. Dark gives
it for free.

### 3.7 Self-hosted materialization roadmap (F# → Dark)

Today everything PDD lives in F#. The end-state has these layers:

```
F# (today)              →   Dark (end-state)
─────────────────────       ─────────────────────────
PDDMaterializer.fs      →   Stdlib.PDD.materialize
HTML prompt building    →   Dark string concat
OpenAI HTTP call        →   Stdlib.LLM.complete (Pending →
                            materializes itself)
Body parser hook        →   stays F# (LibParser is F#-native)
Test runner hook        →   stays F# (Interpreter is F#-native)
QA test generation      →   Stdlib.PDD.generateTests (a fn!)
Refine algorithm        →   Stdlib.PDD.refine (a fn!)
Pending → real registry →   stays F# (it's runtime state)
JSONL persistence       →   Dark Datastore (when Datastore lands)
HTML view               →   Dark HTTP handler over Datastore
Watch loop              →   Dark cron + httpServerServe
CLI dispatch            →   Dark fn registered as `dark.pdd.*`
```

The thin remaining F# layer is just: **state holding the registry,
hooks the body-parser, plumbing into the type system.** Everything
else is Dark code that users can read, refine, and replace.

**This is the PDD-on-PDD recursion** — once `Stdlib.PDD.materialize`
is a Dark fn, users can refine *it* with PDD itself. Materializer
improves materializer.

See `F-SHARP-TO-DARK.md` (next iter) for the concrete sequencing.

## Part 4 — What this means for the spike wrap-up

The spike answered: **can PDD work at all?** Answer: yes, end-to-end,
across 100+ materializations.

The spike did NOT answer:

- Can PDD scale to a large codebase? (We've never tested >50 pending
  fns at once.)
- Is the workflow good *for users other than me*? (No user testing.)
- What's the right model size / cost tier? (We've burned through
  several but no rigorous comparison.)
- How does PDD interact with the package_fns registry at scale?
  (Wave 3 will tell us.)
- Does the recursive UX (materializer-refines-itself) actually
  delight, or does it confuse? (Untested.)
- Branches? Bisect? Blame? (All unbuilt.)

The horizon is enormous. Wave 1+2+3 (INTEGRATION-PLAN.md) lands the
**foundation**. Everything in this doc is the **roadmap on top**.

If we wrap the spike today with these docs + the merged integration
plan + the F# → Dark roadmap, we've turned a 131-commit branch into
a multi-quarter product direction. Worth the time.

## Open questions for follow-up sessions

1. Is "PDD as build artifact" (1.5) more important than "PDD at
   runtime"? My gut: yes, eventually. The runtime case is the demo;
   the build case is the product.
2. How much of Part 2 (recursion improvements) should land in spike-2
   vs. wait for the foundation to merge?
3. What's the smallest credible "PDD scales" experiment? Maybe: a
   500-fn codebase, all Pending, materialize the whole thing, count
   how often we hit the human-curation pipeline.
4. What's the right name for `Stdlib.PDD.materialize` once it's a
   Dark fn? `Stdlib.PDD.materialize`? `Stdlib.Code.synthesize`?
   `Stdlib.LLM.synthFn`? The naming locks the mental model.
5. Is the LLM the right primitive at all, or are we 6 months from
   "the materializer is a fine-tuned model trained on this user's
   codebase"? (Section 1.6.) Either way, the **interface** (Pending
   → Body via a Strategy) is what matters; LLM is just today's
   strategy.

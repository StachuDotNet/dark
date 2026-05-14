# PDD Spike — Consolidated Wrap-Up

*Single dense doc covering: what we built, what we learned, how to
merge it, where it could go, how to self-host it. Print this; mark
it up; decide next moves. Supersedes the per-topic reflection docs
(archived).*

---

## TL;DR

Source files are sketches; the LLM materializes function bodies on
first call; the runtime caches, refines, and hot-reloads. End-to-end
live across HTML rendering, recursion, and CSV parsing. 131 commits,
57/57 tests, ~$0.30 total LLM spend. Branch never pushed.

**Three-state lifecycle:** `Pending` (unresolved name) → `PackageID`
(working-copy slot, mutable) → `Package(hash)` (committed, immutable).
`promote` is the boundary, like `git commit`.

**Spike answered:** can this work? Yes.
**Spike didn't answer:** does it scale, does the UX delight, does
the recursive nature confuse, what's the right model tier, and
which of the dozens of follow-ons matters most.

**Bottom line:** Merge in 3 sequential waves (~3-4 weeks).
Self-host the materializer in Dark itself over the following ~3
months. Beyond that: PDD-as-build-tool, PDD-on-PDD recursion,
`Pending` types, multi-LLM consensus, SCM-style branches and bisect.

## Terminology (pinned)

- **Sketch** — user-written source: names + sigs + (maybe) bodies, with holes
- **Pending** — a fn reference without a body yet; identified by stable `handle : Guid`
- **PackageID** — a materialized but mutable fn slot (the working copy)
- **Package(hash)** — a committed, content-addressed fn (immutable)
- **Materialize** — turn a `Pending` into a `PackageID` (typically via LLM)
- **Refine** — mutate the body of a `PackageID` in place
- **Promote** — snapshot a `PackageID`'s current body and mint a `Package(hash)`
- **Trace** — append-only JSONL of execution events; the authoritative record
- **Handle** — stable Guid identifying a Pending across speculation attempts
- **Verifiable vs Creative** — fn classifier (by name prefix + return type)
  that decides QA-test-gate vs thin-body retry policy

---

## The five claims (memorize)

1. **The source is lazy.** Names + sigs; bodies materialize on demand.
2. **The trace is the program.** Source files are sketches; the
   trace is the authoritative record.
3. **Types are the coordination protocol.** Pending refs carry sig
   hints; parallel materializations agree via type unification.
4. **The runtime is tolerant.** Missing things substitute defaults;
   eval keeps moving; recoveries are auditable.
5. **The human is a materializer.** When find/generate fail, the
   human is the third path.

Anti-pitch: don't say "Copilot for runtime" — misses every
interesting claim.

---

## What got built (state of the branch)

| # | Capability | Status |
|---|---|---|
| H1 | `dark prompt "<freeform>"` CLI | ✅ |
| H2 | Implicit `Pending` from unresolved parser names | ✅ |
| H3 | Interactive annotated HTML view (zero deps) | ✅ + sessions index + fns registry |
| H4 | Promotion of materialized fns to cache | ✅ (sidecar JSONL) |
| H5 | LibParser as primary body-parse path | ✅ (mini-parser fallback only) |
| H6 | Tests-as-gate with independent verification | ✅ (recursion-aware skip) |
| H7 | Recursion via canonicalized handles | ✅ |
| H8 | Safety rails (wall-clock budget, per-handle LLM cap) | ✅ |
| H9 | Model override (`PDD_MODEL`) | ✅ |
| H10 | `FQFnName.PackageID` variant — equal treatment | ✅ (15+ match sites) |
| H11 | Hot-reload on `promoted.jsonl` mtime | ✅ |
| H12 | `dark pdd refine --watch` background daemon | ✅ |
| H13 | `dark pdd promote` — SCM commit step | ✅ |
| bonus | Parallel materialization scheduler | ✅ |
| bonus | Decompose-step cache | ✅ |
| bonus | `dark pdd refine/history/diff/revert/status` | ✅ |

**Live demos verified:** `addOne`, `myAbs`, `myMaxOf`, `factorial`,
`fibonacci+factorial` (parallel), `sumList`, `doubleAll`, CSV
`longestRow` + `parseRows` (gpt-4o), 32-route darklang.com clone
(render*Page Pendings, hot-reload propagation, refine watcher
running in parallel).

**Still trips:** filter+sum in pipes (LibParser declines nested
pipe-in-lambda), tuple-heavy bodies + invented stdlib names
(List.foldi, Tuple.second, etc.), Option<Int64> arithmetic chains.

**Code surface:**
- `PDDMaterializer.fs` — 2058 LoC
- `PDDHTMLView.fs` — 597 LoC
- `Cli/PddCommand.fs` — 1323 LoC
- Plus ~50 LoC scattered through Interpreter, RuntimeTypes,
  binary serializer for the 15+ match arms.

---

## What worked (the wins worth keeping)

1. **The Pending → PackageID → Package(hash) lifecycle.** Three
   states map cleanly onto git's "unborn / working / commit" model.
   The naming hints at the role; the implementation respects it.
   The architectural keeper.
2. **LibParser as the primary body parser, mini-parser as fallback.**
   The mini-parser bought velocity early; switching primary path to
   LibParser unlocked everything from `sumList` onward. Anything
   richer than `x + 1L` needs LibParser.
3. **Canonicalized Pending handles.** name → single Guid per body.
   Without this, self-recursion thrashes the LLM endlessly. With it,
   `factorial` materializes once and references itself — no
   cycle-detection, no depth limit, no `currentlyMaterializing` set.
   The handle-identity is enough.
4. **Verifiable vs creative classifier + dual-gate policy.** Name-
   prefix heuristic (`render*`, `generate*`) + return type splits
   fns into two streams. Verifiable: independent QA tests gate
   acceptance. Creative: thin-body retry only (no QA — hallucinated
   "should contain 'Welcome'!" expectations destroy creative output).
   The single most leverage-creating decision; without it every
   creative renderer fails the gate and gets retried into junk.
5. **JSONL append-only cache** (`promoted.jsonl`,
   `promoted_hashes.jsonl`). Crash-safe, log-structured, last-write-
   wins on read. Hot-reload via mtime polling. Cheap and correct.
6. **Hot-reload via `pddRefreshHook`.** Refining in one process →
   running server picks up on next request. Surprisingly easy to
   wire; transformative for the demo loop.
7. **The decompose cache.** Free-text "compute factorial of 5" →
   Dark expression. Caching this means second runs are sub-100ms
   and skip the LLM entirely.

## What didn't work

1. **The regex mini-parser era.** ~330 LoC across 5 cases. Every
   case was a workaround for "we don't have LibParser wired yet."
   Each new shape (literal, identity, `x + 1L`, lambdas, lists)
   needed its own regex. Should have skipped to LibParser
   immediately.
2. **Hardcoding everything to OpenAI's HTTP shape.** No abstraction.
   Should have been a `Stdlib.LLM.complete` shim from day 1.
3. **The JSONL paths hardcoded in PDDMaterializer.fs.** Should
   route through LibConfig for path resolution. Today they're
   string literals.
4. **Inline CSS in PDDHTMLView.fs.** It's a 200-line CSS block in F#
   source. Should have been an EmbeddedResource from day 1.
5. **No LLM stub harness.** Means CI can't test materialization
   end-to-end. Every CI run skips the most important code path.
6. **Conflating Pending with "no fn at all" early on.** Adding
   PackageID late meant the runtime briefly had a confused mental
   model where "fn missing" and "fn pending" weren't distinct.
   PackageID-first would have been cleaner.

## Surprises

1. **The match-site work was 15+ sites — less explosive than
   feared (we predicted 74), but more than the first count of 9
   for Pending alone.** Adding PackageID alongside roughly doubled
   the arms. Mechanical but non-trivial — and that's *with* a
   sympathetic type system. A less-mature one would have meant 30+.
2. **Recursion was easier than expected.** Canonicalized handles
   alone solve it. No cycle-detection, no depth limit, no
   `currentlyMaterializing` set. The handle-identity is the trick.
3. **Refine is more useful than initial generate.** First-shot
   `renderHome` is OK; after 3 refine cycles it has nav, structured
   sections, richer semantics. The iteration loop is where PDD earns
   its keep — not the first materialization.
4. **The match between LLM creative-fn behavior and `<details>`
   collapse in the HTML view.** Long creative bodies are useful when
   you want them but visually disruptive otherwise. `<details>` makes
   the view scannable without losing information. Discovered late
   (iter 12); should have been baseline.

---

## Cross-cutting decisions to lock before any wave merges

These shape the canonical types and must be pinned before Wave 1.

**1. Naming.** `FQFnName.PackageID` vs `FQFnName.Working` vs
`FQFnName.Mutable`. Current name stresses *mechanism* (it's an ID);
alternatives stress *role* (it's the working state). Recommendation:
`PackageID`. It's the more honest name once you understand the
implementation; "Working" is friendlier but pushes a metaphor.

**2. PackageID forwarding semantics on promote.** When a PackageID
is promoted to a hash, what happens to in-flight callers holding
the Guid?

- (a) **Stay alive forever.** PackageID slot persists; promote just
  *adds* a hash-locked copy. Old refs keep working. New refs target
  the hash directly. Git working-copy semantics — after `git
  commit`, the working copy isn't frozen; it remains live-editable.
- (b) **Forward to hash.** PackageID becomes a redirect; subsequent
  edits fork to a new PackageID. Git branch semantics.

Recommendation: **(a)**. More honest to git; runtime stays simple.

**3. Namespace for promoted PDD fns.** `Stdlib.PDD.X`? `User.PDD.X`?
`Pdd.X`? Or thread through whatever module the original Pending was
in (today: no module info)? Recommendation: `Stdlib.PDD.X` if the
cache is shared cross-user; `User.PDD.X` if user-local. Lock before
Wave 3.

**4. `AllowPending` lexical scope.** Today `let x = 5L in x + y`
with AllowPending: `y` is treated as a free variable, not Pending
(EVariable, not EFnName). Recommendation: confirmed correct; missing
variable in a let-scope is a bug, not an opportunity to materialize.
Document explicitly.

---

## 3-wave integration plan

### Wave 1 — Bedrock (no behavior change)

Land type-system + parser scaffolding. New variants exist; nothing
in mainline references them; all match sites have arms that no-op
or raise. ~400 LoC across 13 files. Mechanical, review-friendly.

- `PT.FQFnName.Pending`/`PackageID` + `RT.FQFnName.Pending`/
  `PackageID` variants
- `RT.pddIDRegistry` (name → Guid stable map)
- `RT.pddIDFnCache` on ExecutionState (Guid → PackageFn)
- `RT.pddRefreshHook` (mutable, default no-op)
- Match-exhaustiveness arms in: Interpreter, RTQueryCompiler,
  RT/PT→DarkTypes, Binary serializer, LibDB/PackageItem, LibDB/
  Tracing, Execution.fs prettyName
- `NameResolver.OnMissing.AllowPending`
- WT2PT threading of `onMissing` through EApply, EFnName, pipe
  stage, EVariable fallback chains
- PT2RT.Expr lowering for new variants

**Risk:** binary-compat on Canonical hashing? No — new tag bytes
(3 + 4) added; existing hashes don't shift.

**Tests:** existing 57 PDD tests pass + smoke "AllowPending parses
pending; default doesn't."

### Wave 2 — Materializer (opt-in module)

Land `LibExecution.PDDMaterializer` + CLI surface, gated by a
flag (env var, build flag). Default CLI doesn't load it.

- `LibExecution/PDDMaterializer.fs` — orchestrator + body-parser
  hook + test-runner hook + refine + promote. ~1900 LoC.
- `LibExecution/PDDHTMLView.fs` — view + session sidecar + index +
  fns registry. ~520 LoC.
- `Cli/PddCommand.fs` — `dark pdd ...` dispatcher. ~900 LoC.
- Drop regex mini-parser cases (LibParser is primary path; ~330 LoC
  dies).
- Route JSONL through `LibConfig` (configurable + versioned
  `version: 1` field per line).
- Move prompts to `EmbeddedResources` (no F# rebuild for prompt
  edits).

**Tests:** LLM-stub harness exercising 10 integration cases:
materialize-end-to-end, retry-on-thin-body, retry-on-JSON-fail,
recursion-skip, refine, promote.

### Wave 3 — SCM integration

Land `dark pdd promote` writing to the real `package_functions`
SQLite table. Smallest wave, hardest by requiring Decisions 2 and 3
above.

- `parseFullSigPT : string → Option<List<...> × PT.TypeReference>`
- `ptPackageFnOf : name → sig → body → Task<Option<PT.PackageFn>>`
- Expose `applyAddFn` from LibDB/PackageOpPlayback
- Invoke `applySetName` for `name → hash`

**Risk:** dev DB contamination. Want a "PDD scratch branch"
pattern or a `dark pdd unpromote <hash>` surgical-delete.

### What does NOT merge

- Inline CSS in PDDHTMLView (replace via EmbeddedResources or
  extract to dev-only package)
- 32-route darklang.com demo router + the Python script (stays
  in `pdd-thinking/scripts/`)
- Hardcoded JSONL paths (Wave 2 routes through LibConfig)
- Direct OpenAI HTTP call in F# (route through `Stdlib.LLM` —
  see F# → Dark roadmap below)

### Order of operations

```
Decisions → Wave 1 → Wave 2 → Wave 3
(1-2 days)  (1 wk)   (1-2 wks) (1-2 wks)
                      │            │
                      │            └→ user-visible: PDD fns in `dark search`
                      └→ user-visible: `dark pdd ...` commands live
Wave 1 is invisible at user level.
```

Full integration realistic in 3-4 focused weeks, or quarter-paced
if not on the critical path.

---

## Big picture — what we haven't thought about

### Materializer as `List<Strategy>`, not LLM-only

Today: LLM is the only path. Should be:

```
cache → corpus search → synthesis from examples → human → LLM
```

Each strategy = a predicate ("can I produce a body?") + an action
("here's the body"). LLM becomes last-resort. Probably 60% of
materializations hit cache/corpus and never burn a token.

### `Pending` for types, not just fns

`FQTypeName.Pending` is conspicuously absent. Pending types
materialize a type definition + default value + common typeclass
fns (Eq, Show, Json codec) in one shot from usage. *Much harder*
(infer structure from multiple sites) but where the real
ergonomic win lives. `Pending fn` = productivity; `Pending type` =
paradigm shift.

### `dark pdd demote` — the reverse of promote

Set body to `Pending`; re-trigger on next call. Lets users
A/B-compare an old body against a fresh materialization. Trivial
to add; would change how people iterate.

### Multi-LLM consensus

N parallel LLM calls (4o-mini + gpt-4o + claude-haiku +
claude-sonnet), pick the body that passes the most QA tests +
shortest length. Self-consistency decoding at the architecture
level. 3-4× cost; *much* higher first-try success rates. Matters
in long pipelines (10 fns × 90% = 35% one-failure; 10 fns × 99% =
90% all-pass).

### PDD as build artifact, not runtime decision

`dark pdd build` walks source, finds every Pending, batch-
materializes all, writes committed snapshots. Production runs a
no-materializer binary. **Production doesn't want surprise LLM
calls** (latency, billing, non-determinism). PDD = code generator.
Same machinery, hoisted out of request path.

### PDD audit logs as training data

Every materialization writes `(name, sig, prompt → body, QA-pass,
refines, finalBody)`. After 10K uses, that's a training set.
Fine-tuned small model beats generic LLM at "produce a body in
*my* style" within months. Spike has the data pipeline; nothing
captures it.

### Versioned migrations

`let MyConfig = Pending(reason = "schema-changed-2026-05-14")`.
When loading old persistent state shaped by *old* `MyConfig`, the
materializer is invoked with old × new shape, generates a migration
fn. Solves a real pain using exactly the spike's machinery.

### Speculative materialization

User typing `let html = renderHo` — kick off materialization for
`renderHome`, `renderHomepage`, `renderHotProduct`. By the time
they hit enter, one is ready. Wasteful in tokens; fast in UX.

### PDD-as-LSP

Editor code action for "this fn is Pending"; inline materialize;
ghost-suggestion preview; QA tests in side pane. **Where Dark
competes with Copilot/Cursor.** Fundamental PDD advantage: the
result is *evaluated*, not just suggested.

### Pricing model (unresolved)

Local-only with user-provided keys = simple; multi-tenant
Darklang Cloud caching = network effects + ops complexity. Spike
deferred this; needs answering eventually.

---

## Making the recursive nature better

The spike's most interesting moment: realizing `pdd promote` is
`git commit`. The cascade — every SCM concept maps onto PDD —
remains underused.

### Branches

Today: one `promoted.jsonl`. One "branch."
`dark pdd branch experimental` forks working state; iterate freely;
`dark pdd merge experimental → main`. Mechanism: append-only files
+ per-branch manifest. UX: "trying a riskier prompt for
`renderHome` — let me do it on a branch." **Missing primitive for
serious experimentation.**

### Bisect

`dark pdd bisect <fnName> <test>` — given a failing test today
that passed at some prior snapshot, binary-search history. Unique
to PDD: **per-fn body history**, finer-grained than git's per-file.

### Blame

`dark pdd blame <fnName> <line>` — which refine introduced this
line. `diff` gets halfway; attribution to session/timestamp is
missing.

### PDD-on-PDD: the materializer materializes the materializer

*The most recursive thought.* `materializeFn : (Name × Sig ×
Hints) → Task<Body>` is itself a function. Today it's F# with
hand-tuned prompts. Should be a *Dark fn the user can refine*.

`Stdlib.PDD.materialize` = Pending fn with thin F# wrapper. User
runs `dark pdd refine Stdlib.PDD.materialize` to change *their*
strategy — better prompts, different model, retry logic,
multi-LLM consensus. **Their materializer evolves with their
codebase.**

This is the path to making PDD a Dark library that's user-
modifiable, not an F# subsystem.

### Test-as-spec, body-as-prediction

Today QA tests = gate ("did LLM get it right?"). Tests are also a
*spec*. Recursive use:

1. User writes 3 in/out examples for `factorial`.
2. PDD materializes body.
3. QA runner *generates* more tests by mutation.
4. PDD materializes a body passing both user's + generated tests.
5. User curates generated tests; promotes ones that capture
   intent; deletes ones that don't.
6. Loop.

**Hindley-Milner-meets-LLM.** Specs grow alongside bodies. Spike
has the test runner; doesn't generate; doesn't ask user to curate.

### Refine-on-failure (not just on-demand)

`pdd refine --watch` picks least-refined fn — naïve. Better: refine
the fn that **most recently failed in production** (caught error,
budget overrun, QA test regressed). Runtime has all this signal;
isn't fed back. **RL at the codebase level.**

### Cross-fn refactor as single PDD call

"Make these 5 fns more consistent" — bundle 5 bodies into one
prompt + 5 in response. Multi-body refine. New shape, same
machinery.

### History-as-explanation

`dark pdd history` shows working revs + snapshots. Missing: **why**
each rev exists. If every refine recorded its triggering prompt,
history becomes a self-documenting changelog at *fn level* —
better than git log.

---

## How Dark's strengths get used (and don't, yet)

### PT.PackageFn.Hash is content-addressable — use it for dedup

Today: cache keys on name. Should: key on **structural hash**. Two
users writing `factorial` in different projects share a cache
entry. **Network-effect benefits compound** — every user makes
everyone faster.

### `package_functions` is the registry; PDD should use it

Today: PDD fns in `promoted_hashes.jsonl` sidecar. Wave 3 moves to
the real table. Deeper move: every materialized PDD fn flows
through one store. Hand-authored + PDD-materialized share storage;
queries don't distinguish; tooling (search, blame, package install)
Just Works.

### Dark's tracing system is built for this

`Tracing` struct on ExecutionState records package + builtin
calls. Materializer = perfect tracing consumer — every
materialization leaves a trace; failures at T+3 can be
explained as "materialized at T0 by prompt P, called at T1 with
V, failed at T2." Currently logged to JSONL by hand.

### Dark's types as the prompt's grounding

Today: fn signature in prompt = string. Should: **Dark type
expression**, plus links to other types in `package_functions`.
LLM resolves references by name-lookup, not by guessing. Hard in
F#; one-liner in Dark (`prettyPrintType ty`). The materializer
*wants* to be in Dark.

### HTTP server makes the materializer addressable

```dark
let materialize = fun req →
  let body = req.body |> parseJson<MatRequest>
  Stdlib.PDD.materialize body.name body.sig body.hints |> Json.print
```

6 lines of Dark = materializer as a service. Other services/
languages call it without caring it's F#. Eventually: this **is**
the materializer, hosted on Dark Cloud, multi-tenant.

### Hash-as-identity makes the cache a CDN

`fnHash → body` cache key + bodies are strings → cache IS a CDN.
Different users hit same hash, same cached body, no LLM call.
Darklang Cloud could host shared cache; opt-in; the long tail of
"we all write factorial" goes from $0.0002/call to free.

---

## F# → Dark self-hosting roadmap

Today: ~4,000 LoC F# (PDDMaterializer 2058, PDDHTMLView 597,
PddCommand 1323).

End-state: **F# substrate ~250 LoC** (registry, hooks, interpreter
dispatch), **Dark library ~2,500 LoC**. 90% reduction in F#.

### Layering

```
       Application code (Dark) — uses Pending fn
                      │
       ┌──────────────────────────────┐
       │   Stdlib.PDD (Dark library)  │  ← all moveable logic
       │     materialize / refine /    │
       │     promote / generateTests /  │
       │     pickStrategy / buildPrompt /│
       │     parseLLMResponse / scoreBody│
       │     / decomposeFreeText        │
       └──────────────┬───────────────┘
                      │
       ┌──────────────────────────────┐
       │   Stdlib.LLM (Dark library)  │  ← thin shim, swappable
       │     complete / stream         │
       └──────────────┬───────────────┘
                      │ HTTP
                      ▼
              (OpenAI / Anthropic / local)

       ┌──────────────────────────────┐
       │   F# substrate                │  ← what stays
       │     FQFnName.Pending variant  │
       │     FQFnName.PackageID         │
       │     pddIDRegistry              │
       │     pddIDFnCache               │
       │     pddRefreshHook             │
       │     bodyParser hook            │
       │     testRunner hook            │
       └──────────────────────────────┘
```

**Rule:** if it touches runtime state, it stays F#. Logic moves.

### 6-phase migration

| # | Phase | F# shrink | Dark grow | Time | User-visible |
|---|---|---|---|---|---|
| 0 | Prereqs (HttpClient/Json/File audit) | 0 | +50 | 1d | no |
| 1 | `Stdlib.LLM.complete` shim | –95 | +80 | 1-2d | indirect |
| 2 | Prompts → Dark | –570 | +570 | 1d | prompts user-overridable |
| 3 | Response parsing + scoring → Dark | –200 + –330 mini-parser | +150 | 2d | mini-parser drops |
| 4 | Orchestrator → Dark (~400 LoC fn) | –1200 | +400 | 3-5d | **PDD-on-PDD unlocked** |
| 5 | CLI subcommand dispatch → Dark | –1200 | +700 | 2d | each cmd overridable |
| 6 | HTMLView → Dark HTTP handler | –600 | +500 | 1-2d | view = live Dark service |

Total: ~3 weeks of focused work. End state unlocks PDD-on-PDD —
user refines the materializer with the materializer.

### Trade-offs

- **Latency:** Dark→F# Dark-fn call adds 1-10ms. LLM call dominates
  at 1-10s. Irrelevant in practice; measure.
- **Debuggability:** F# stack traces > Dark error messages today.
  Until Dark traces match, debugging is harder in Dark.
- **CI mocking:** F# unit tests stub `callOpenAI` directly. Dark
  needs a stub builtin under test harness control.
- **Boot ordering:** `Stdlib.PDD.materializeOne` is itself a Dark
  fn loaded before any Pending dispatches. Package load order
  matters. **Real foot-gun.** Needs design before Phase 4.

---

## Prioritized TODOs by time budget

### 1 hour
Stop. Print this doc. Read on paper. Don't write code.

### 1 day
Lock the 4 cross-cutting decisions (naming, forwarding, namespace,
AllowPending scope). Write a one-page decisions doc Wave 1 PR
references.

### 1 week
**Wave 1 PR.** Bedrock. ~400 LoC mechanical. No user-visible
change. Unblocks everything else.

### 1 month
Wave 1 + Wave 2 + start of Wave 3. PDD opt-in on main. Drop mini-
parser; route JSONL through LibConfig; prompts → EmbeddedResources.
LLM-stub harness for CI.

### 3 months
+ F# → Dark Phases 0-2. `Stdlib.LLM.complete` lives. Prompts move
to Dark. Users override prompts without F# rebuild.

### 6 months
+ Phase 4. Orchestrator is Dark code. F# substrate ~300 LoC.
**PDD-on-PDD unlocks** — user refines materializer with
materializer.

### 1 year
+ Selected BIG-PICTURE items, by priority:
1. `List<Strategy>` materializer (cache → corpus → human → LLM)
2. PDD-on-PDD recursion infrastructure
3. Branches + bisect + blame
4. `dark pdd build` (PDD as build artifact)
5. Test-as-spec generative loop
6. `Pending` for types (hardest, biggest)

Each 1-3 weeks of focused work once foundation exists.

### Not on the roadmap (deferred, with reasons)

- **Multi-user cloud cache.** Auth/billing/trust mess. Defer until
  single-user value proven.
- **PDD-LSP IDE integration.** Wait for Dark LSP to land in its
  own right.
- **Speculative materialization on keypress.** Wasteful tokens.
  Experiment, don't productize.
- **Fine-tuned per-user models.** Real value but not a Darklang-
  team capability. Partner with inference vendor.
- **Mid-program fn iteration** (re-derive when result doesn't
  satisfy downstream). Important but design-space murky. Own spike.

---

## What we'd do differently

1. **Start with `FQFnName.PackageID`, not `Pending`.** PackageID is
   harder conceptually; we added it late. Pending alone briefly
   meant "no fn at all," wrong mental model.
2. **LibParser from day 1.** Regex mini-parser ate 330 LoC and was
   never the right call.
3. **HTML view `<details>` from day 1.** Long expressions exposed
   an ugly view; patched at iter 12+.
4. **Decide PackageID-forwarding semantics on paper before coding
   `promote`.** Today's working `promote` hasn't committed to a
   model. Load-bearing for years.
5. **Build the LLM-stub harness early.** No CI integration tests
   means materialization is the most-important untestable code
   path.
6. **Don't reinvent `promoted_hashes.jsonl`.** `package_functions`
   was always the destination; sidecar bought velocity but locks
   in a path we now have to migrate from.

---

## Open meta-question — single PR vs 3-PR sequence

**Option A** — merge spike as one big PR. Closure, but reviewers
glaze at 4000+ LoC with mixed bedrock/opt-in/experimental edges.

**Option B** — 3 sequential PRs per integration plan. Reviewable;
spike branch stays as reference; mainline absorbs only what
survived reflection. Higher PR overhead, more context-switching.

**Recommendation: Option B.** The integration plan was written to
make this tractable. Spike becomes a historical artifact like a
research paper; mainline takes the survivors.

---

## What the spike did and didn't answer

**Answered:**
- Can the runtime materialize source on demand via LLM? **Yes.**
- Can it handle recursion? **Yes** (canonicalized handles).
- Can it handle creative vs verifiable fns differently? **Yes**
  (classifier + thin-body retry vs QA gate).
- Does hot-reload work in a live HTTP server? **Yes.**
- Does the SCM-style promote step work? **Yes** (JSONL sidecar
  today; real `package_functions` is Wave 3).

**Not answered:**
- Can PDD scale beyond ~50 pending fns at once?
- Is the workflow good for users other than me?
- What's the right model size/cost tier? (Several burned through;
  no rigorous comparison.)
- How does PDD interact with `package_functions` at scale? (Wave
  3 will tell.)
- Does the recursive UX (materializer-refines-materializer)
  delight or confuse? Untested.
- Branches? Bisect? Blame? All unbuilt.
- LSP integration. Build artifact mode. Speculative
  materialization. Pricing.

---

## Hard rules (from CLAUDE.md / branch hygiene)

- **Never push `pdd`.** Local-only. Cherry-pick later via the
  3-wave integration plan, not by pushing.
- **Commit after every successful compile.** Free, atomic, easy
  to revert.
- **30-min stuck rule:** revert and try a different angle.
- **OpenAI key** at `~/.config/darklang/llm-keys.env` (mode 600).
  Never in any repo file. Pass via `docker exec -e
  OPENAI_API_KEY=...`.
- **Build is two-pass** after Dark type changes: `touch backend/
  src/LibExecution/package-ref-hashes.txt && build`.
- **No `failwith`** — `Exception.raiseInternal "msg" []`.
- **No `printfn`** — `Prelude.print`.
- **Cumulative spend ~$0.30** of $10 budget. Effectively
  inexhaustible at cheap-model rates.

---

## CLI reference (terse)

```
dark prompt "<free-text>"        # decompose + run + visualize
dark pdd run "<expr>"            # parse user-written Dark + run
dark pdd demo <fn> <Int64-arg>   # hand-built Apply test surface
dark pdd cache (list|clear|paths)
dark pdd trace (list|last)
dark pdd refine <fn> | --all | --watch [sec]
dark pdd promote <fn> | --all | list
dark pdd history <fn>            # working + committed revs
dark pdd diff <fn>               # what `refine` last changed
dark pdd revert <fn> [rev]       # roll back; appended as new rev
dark pdd status                  # one-glance health
```

**Env vars:**

| Var | Default | Meaning |
|---|---|---|
| `OPENAI_API_KEY` | — | required |
| `PDD_MODEL` | `gpt-4o-mini` | LLM (gpt-4o for picky syntax) |
| `PDD_BUDGET_MS` | 300000 | wall-clock per run |
| `PDD_PARALLEL` | 3 | concurrent materializations |
| `PDD_SKIP_QA` | unset | skip QA gate for all fns |

**Files:**

| Path | Role |
|---|---|
| `rundir/pdd-cache/promoted.jsonl` | working-copy stream (append-only) |
| `rundir/pdd-cache/promoted_hashes.jsonl` | committed snapshots |
| `rundir/pdd-cache/decomposed.jsonl` | free-text → Dark-expr cache |
| `rundir/pdd-view/<id>.html` | per-session HTML view |
| `rundir/pdd-view/index.html` | sessions index |
| `rundir/pdd-view/fns.html` | fn registry across sessions |
| `rundir/logs/pdd-materialize.jsonl` | every LLM call |

---

## See also (deep-dives still in this directory)

- `README.md` — top-level entry, build state
- `DESIGN.md` — sectioned design depth (LibExecution, scheduler,
  sig, tolerance, capabilities, human, tracing, HTML view)
- `EMPIRICAL.md` — LLM-behavior observations, prompt iteration,
  cost numbers, red-team risks
- `DEMOS-AND-BUDGETS.md` — concrete programs + envelopes
- `PDD-CLI-REFERENCE.md` — every CLI command in detail
- `REAL-PACKAGE-FNS.md` — Wave 3 scope
- `archive/` — historical session reports + earlier iter-by-iter
  notes + the original SCM-INTEGRATION pivot sketch

The four per-topic reflection docs (SPIKE-LEARNINGS,
INTEGRATION-PLAN, BIG-PICTURE, F-SHARP-TO-DARK) have been
consolidated into this file and archived. If you need the
expanded form: `pdd-thinking/archive/reflection-layer/`.

---

## Closing

Eat the spike. Pieces worth keeping → merge plan. Pieces worth
dropping → archive. Horizon → mapped. Close the loop.

| Time from now | State |
|---|---|
| 3 weeks | PDD opt-in on mainline |
| 3 months | Prompts user-customizable (Phase 2 of self-host) |
| 6 months | Materializer is Dark code; PDD-on-PDD real |
| 1 year | PDD has its own SCM tooling, build mode, strategies beyond LLM |

The spike was the proof. The integration plan is the foundation.
The self-hosting roadmap is what makes PDD a *Dark* feature, not an
F# feature. The big-picture roadmap is the recursive vision —
where the materializer materializes itself, and SCM concepts (bisect,
blame, branches) gain per-function granularity.

The branch never pushed; the code merges deliberately.

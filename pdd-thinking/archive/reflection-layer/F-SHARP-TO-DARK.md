# PDD — Moving from F# to Dark (Self-Hosting Roadmap)

*Concrete sequencing for migrating ~4,000 LoC of F# materialization
machinery into Dark source. Companion to SPIKE-LEARNINGS.md (what
worked) and BIG-PICTURE.md (where this fits in the bigger story).*

## Why move it at all?

Three reasons, in priority order:

1. **PDD-on-PDD recursion.** If `Stdlib.PDD.materialize` is a Dark
   fn, users can refine *it* with PDD itself. The materializer
   evolves with the user's codebase. This is the central recursive
   beauty BIG-PICTURE 2.4 talks about; it requires Dark code.
2. **Hand-tuned prompts don't belong in F#.** Updating the v4 system
   prompt today requires an F# rebuild + new binary. Embarrassing
   for a "language built for hot iteration." Even a 1-character
   prompt tweak is currently a backend change.
3. **Dark exists to host high-level glue code.** Materialization is
   glue: parse → LLM-call → parse-response → run-tests → cache. That
   is *exactly* the workload Dark is for. Keeping it in F# admits
   "we don't trust Dark to host real workloads yet," which becomes
   a self-fulfilling lack-of-eat-our-own-dogfood.

## Inventory: what lives in F# today

```
backend/src/LibExecution/PDDMaterializer.fs   2058 LoC
backend/src/LibExecution/PDDHTMLView.fs        597 LoC
backend/src/Cli/PddCommand.fs                 1323 LoC
                                          ─────────
                                              3978 LoC total
```

Within PDDMaterializer, by function group (from `grep '^let'`):

| Group | Lines (approx) | What it does |
|---|---|---|
| EventSink + emit | 5–95 | observability hooks |
| Prompts (v4System, decomp, testGen, fixUp) | 95–650 | hand-typed prompt strings |
| BodyParser/TestRunner hooks | 205–230 | F# closures injected by Cli |
| Sig parsing (parseSimpleType, parseFullSig) | 273–350 | Dark type syntax → RT.TypeReference |
| Pending-handle canonicalization | 352–390 | name → stable Guid |
| Self-recursion detection | 420–435 | AST walk |
| Arg-hint capture | 436–470 | runtime values from call sites |
| Creative-fn heuristic | 472–480 | name prefix → skip-QA |
| Prompt building | 481–660 | format strings + interpolation |
| LLM response parsing | 662–805 | JSON → ClaimedTest / GeneratedFn |
| OpenAI HTTP client | 805–895 | actual API call |
| Logging | 900–925 | append to JSONL |
| Hardcoded identity fallback | 926–975 | `λx. x` for no-LLM case |
| Mini-parser cases 1a–5 | 978–1335 | regex-based body parser |
| Promote cache (load/lookup/persist) | 1337–1395 | working-revs JSONL |
| Refine logic | 1399–1445 | refinement loop |
| Decompose cache | 1445–1495 | free-text → expr JSONL |
| **materialize (the main fn)** | **1495–1900** | **the 400-line orchestrator** |
| Promote-snapshot / hashes | 1900–1985 | committed-revs JSONL |
| Hot-reload hook installer | 1985–2058 | mtime polling |

About **30%** of this is hand-tunable behavior (prompts, heuristics,
caching policy). The rest is plumbing — language interop, JSON, HTTP,
hot-reload, registry — most of which Dark can do natively.

## Layering: what stays in F#, what moves to Dark

The end-state has a *thin* F# substrate and a *thick* Dark library:

```
       Application code (Dark)
              │
              ▼ uses Pending fn
       ┌──────────────────────────────┐
       │   Stdlib.PDD (Dark library)  │ ← all moveable logic
       ├──────────────────────────────┤
       │  materialize                 │
       │  refine                      │
       │  promote                     │
       │  generateTests               │
       │  pickStrategy                │
       │  buildPrompt                 │
       │  parseLLMResponse            │
       │  scoreBody                   │
       │  decomposeFreeText           │
       └──────────────┬───────────────┘
                      │ calls
                      ▼
       ┌──────────────────────────────┐
       │   Stdlib.LLM (Dark library)  │ ← thin shim, swappable
       │     complete                 │
       │     stream                   │
       └──────────────┬───────────────┘
                      │ HTTP
                      ▼
              (OpenAI / Anthropic / local)

       ┌──────────────────────────────┐
       │   F# substrate (Pure plumbing)│ ← what stays
       ├──────────────────────────────┤
       │  RT.FQFnName.Pending variant │
       │  RT.FQFnName.PackageID       │
       │  RT.pddIDRegistry            │ ← runtime mutable state
       │  RT.pddIDFnCache             │
       │  pddRefreshHook              │ ← invoked on cache reload
       │  bodyParser hook             │ ← LibParser glue
       │  testRunner hook             │ ← Interpreter glue
       └──────────────────────────────┘
```

**Rule of thumb:** if it touches runtime state (the fn-cache, the
registry, the hooks) it stays F#. If it's logic — prompt building,
response parsing, strategy decisions, scoring — it moves to Dark.

The F# layer shrinks to **maybe 200–300 LoC**, a 90% reduction.

## Phase-by-phase migration

I'm going to lay this out in 5 phases, each independently shippable,
each gated by a milestone that proves the previous one works.

### Phase 0 — Prerequisites (must exist before we start)

These are landed-on-`main` capabilities that the migration assumes:

1. **`Stdlib.HttpClient.request`** with bearer-token auth. Currently
   `Builtin.httpRequest` exists; needs to be confirmed and signature-
   stable.
2. **`Stdlib.Json.parse<T>` + `Stdlib.Json.encode`** with `Result`
   return types. Used everywhere in the LLM response parsing.
3. **`Stdlib.String.tokenize`** or equivalent — Dark needs a regex
   library or a parser combinator to do `parseLLMResponse`. The F#
   code uses `System.Text.RegularExpressions.Regex.Replace` heavily;
   we need a Dark equivalent or rework as a small parser.
4. **`Stdlib.Datetime`** for log timestamps + cache TTLs (mostly
   exists).
5. **`Stdlib.File` or `Stdlib.Datastore`** for cache persistence.
   Today JSONL goes through F# `IO.File`. To remove that F# coupling
   we need either:
   - (a) `Stdlib.File.appendAllText` (simpler; less safe)
   - (b) `Stdlib.Datastore` (better; doesn't yet exist on `main` for
     CLI mode)
   Pick (a) for first cut; revisit when Datastore lands.

**Gate Phase 0:** A 50-line Dark program that calls OpenAI, parses
JSON, appends to a file, and reads it back. If that doesn't work,
nothing downstream works.

### Phase 1 — `Stdlib.LLM.complete` (the foundational shim)

Wrap the OpenAI HTTP call as a single Dark fn:

```dark
let complete
  (model : String)
  (system : String)
  (user : String)
  (maxTokens : Int64)
  : Task<Result<String, String>> =
  let body = {
    model = model
    messages = [
      { role = "system"; content = system }
      { role = "user"; content = user }
    ]
    max_tokens = maxTokens
    temperature = 0.2
  }
  let req = {
    method = "POST"
    url = "https://api.openai.com/v1/chat/completions"
    headers = [
      ("Authorization", "Bearer " ++ Stdlib.Env.get "OPENAI_API_KEY")
      ("Content-Type", "application/json")
    ]
    body = body |> Stdlib.Json.encode
  }
  let response = req |> Stdlib.HttpClient.request
  // ... parse, extract choices[0].message.content
```

**Replaces:** `callOpenAI`, `callOpenAIWithMode`, `extractContent`
(~95 LoC of F#).

**Test:** F# materializer keeps calling its old `callOpenAI` for now;
in parallel, build the Dark `complete` and test via
`dark pdd run "Stdlib.LLM.complete \"gpt-4o-mini\" ..."` end to end.
Same prompt, same response shape, byte-for-byte parity.

**Gate Phase 1:** `Stdlib.LLM.complete` produces the same response
as `Mat.callOpenAI` for 10 example calls. Once confirmed, swap the
F# materializer's `callOpenAI` to invoke `Stdlib.LLM.complete` via
the runtime (Dark fn callable from F#).

**Risk:** Calling Dark from F# requires an `ExecutionState` and
type-checking the result. This is doable today (it's how builtins
get called) but adds latency on the hot path. The latency is
dominated by the LLM round-trip, so it's probably fine.

### Phase 2 — Move prompts out of F# into Dark string constants

The v4 system prompt is 100+ lines of F# string. Hard to iterate.

Move them to Dark, where they're just `let` bindings:

```dark
let v4SystemPrompt = """
You generate Darklang function bodies AND example tests.
...
"""

let decomposeSystemPrompt = """..."""
let testGenSystemPrompt = """..."""
let buildFixUpPrompt (name : String) (sig_ : String) (failedBody : String) : String =
  $"The function {name} : {sig_} returned this body which failed:\n{failedBody}\n\nProduce a corrected body."
```

**Replaces:** prompt-string blocks in `PDDMaterializer.fs:94–660`
(~570 LoC; mostly string content, ~50 LoC of logic).

**Why this matters:** users can now refine prompts without rebuilding
F#. A user who wants different prompts for their domain (legal? game
logic? medical?) can override `Stdlib.PDD.v4SystemPrompt` in their
own package, and PDD will use theirs.

**Gate Phase 2:** All prompt content is in Dark; the F# materializer
calls into Dark to fetch them by name. Easy to verify — diff the
prompts that get sent before/after; should be identical.

### Phase 3 — Response parsing + body scoring move to Dark

`parseLLMResponse`, `parseTestGenResponse`, `extractContent`,
`bodyHash`, and the body-richness scoring used in `refineFn` all
move:

```dark
let parseLLMResponse (raw : String) : Result<GeneratedFn, String> =
  raw
  |> Stdlib.String.findCodeFenced "darklang"  // extract ```darklang ... ``` block
  |> Stdlib.Result.andThen (fun block ->
       block |> Stdlib.Json.parse<GeneratedFn>)

let scoreBody (body : String) : Int64 =
  // length + semantic-richness heuristic
  let lenScore = body |> Stdlib.String.length
  let semanticTags = body |> Stdlib.String.matchesCount Stdlib.PDD.semanticTagsPattern
  lenScore + (semanticTags * 50L)
```

**Replaces:** ~200 LoC of F# parsing + scoring.

**Bonus:** the regex-heavy mini-parser cases (Case 1a through 5,
~330 LoC at PDDMaterializer.fs:978–1335) can be dropped now — they
were only there because we couldn't easily call LibParser from inside
the materializer. With LLM-roundtrip now in Dark, body parsing
goes through LibParser via the existing F# `bodyParser` hook. Mini-
parser is dead weight.

**Gate Phase 3:** All non-state functions in PDDMaterializer have
Dark equivalents. The F# materializer's `materialize` orchestrator
still exists but is *thin* — it sets up the cache lookup, then calls
into a Dark `Stdlib.PDD.materializeOne` function for the actual
logic.

### Phase 4 — The orchestrator itself moves to Dark

This is the meaty step. Today's `materialize` (PDDMaterializer.fs:
1495–1900, ~400 LoC) does:

1. Check `promotedCache` for a hit.
2. If miss, run `decomposeFreeText` or use direct sig.
3. Build user prompt, call LLM, parse response.
4. Run QA tests (skip for creative fns).
5. If tests fail, fix-up retry (up to 3 LLM calls per handle).
6. On success: persist to JSONL, emit events, return PackageFn.

All six steps are pure orchestration. They become a Dark fn:

```dark
let materializeOne (p : PendingHandle) : Task<Result<PackageFn, String>> =
  let cached = p.name |> Stdlib.PDD.cacheLookup
  match cached with
  | Some fn -> Task.lift (Ok fn)
  | None ->
    let userPrompt = p |> buildUserPrompt
    let response =
      Stdlib.LLM.complete Stdlib.PDD.model
                          Stdlib.PDD.v4SystemPrompt
                          userPrompt
                          2000L
    response
    |> Task.map parseLLMResponse
    |> Task.andThen (runQATests p)
    |> Task.andThen (cacheAndReturn p)
```

**Replaces:** the 400-line F# `materialize`. F# now keeps only:

- The hook (`pddRefreshHook`) that's invoked when the cache reloads.
- The match arm in the interpreter that recognizes `FQFnName.Pending`
  and dispatches to `Stdlib.PDD.materializeOne` via a runtime call.
- The mutable registry state.

**Gate Phase 4:** `dark pdd run` on a fresh expression with 3
Pending fns materializes them via the Dark `materializeOne`. All 57
existing PDD unit tests still pass. End-to-end byte-identical
behavior.

**Estimated F# shrinkage:** 2058 LoC → ~250 LoC (~88%).

### Phase 5 — CLI surface moves to Dark

`Cli/PddCommand.fs` (1323 LoC) is the `dark pdd ...` subcommand
dispatcher: `run`, `cache`, `trace`, `refine`, `promote`, `history`,
`diff`, `revert`, `status`. Each subcommand is at most 50–100 LoC of
F# orchestration.

Today's Dark CLI uses F# `argu`-style argument parsing. Tomorrow, the
PDD subcommands can be Dark fns registered through a CLI plugin
interface. Until that plugin interface exists, `PddCommand.fs` is a
thin shim — each F# subcommand calls into a Dark fn:

```fsharp
| "refine" :: [name] ->
    Cli.runDarkFn "Stdlib.PDD.refineCmd" [DString name]
| "status" :: [] ->
    Cli.runDarkFn "Stdlib.PDD.statusCmd" []
```

Each Dark fn produces a string (the rendered output). F# prints it.

**Replaces:** 1323 LoC of F# CLI dispatch → ~100 LoC F# shim + ~600
LoC Dark fns.

**Gate Phase 5:** All `dark pdd <subcommand>` outputs are produced
by Dark fns. F# only routes args + prints results. Plugin-style
registration of CLI commands becomes the next-next step (out of
scope for the spike wrap).

### Phase 6 (optional) — HTMLView moves to Dark

`PDDHTMLView.fs` (597 LoC) builds HTML from session sidecars. It's
mostly string concatenation. Moving it is pure churn — F# is fine at
strings — except that **a Dark version becomes a real HTTP handler**:

```dark
let serveSession = fun req ->
  let sessionId = req.path |> Stdlib.String.dropPrefix "/" |> Stdlib.String.dropSuffix ".html"
  let session = sessionId |> Stdlib.PDD.loadSession
  Stdlib.PDD.renderSession session |> htmlResponse
```

You'd run a single `dark pdd run "Builtin.httpServerServe 8765L ..."`
and the view is live, hot-reloadable, refinable. This is the
**materializer-as-Dark-program** vision from BIG-PICTURE 3.5.

Realistically Phase 6 is a follow-up, not part of the foundation.

## Summary table — phase progression

| Phase | F# shrink | Dark grow | Time | Risk | User-visible? |
|---|---|---|---|---|---|
| 0: Prereqs | none | ~50 LoC test | 1d | low | no |
| 1: `Stdlib.LLM.complete` | –95 | +~80 | 1–2d | low | indirectly faster prompt updates |
| 2: Prompts → Dark | –570 (mostly strings) | +~570 | 1d | low | users can override prompts |
| 3: Parsing + scoring | –200 | +~150 | 2d | medium | mini-parser drops |
| 4: Orchestrator → Dark | –1200 | +~400 | 3–5d | high | indirect; CLI behavior identical |
| 5: CLI → Dark | –1200 | +~700 | 2d | medium | each cmd potentially user-overridable |
| 6: HTMLView → Dark | –600 | +~500 | 1–2d | low | view becomes a live Dark service |
| Total | –3865 LoC F# | +~2450 LoC Dark | ~3 weeks | mixed | huge cumulative |

Even being conservative, **the F# side ends up under 300 LoC.**
The Dark side ends up around 2500 LoC of Dark — most of it would be
package fns that other Dark programs could reuse.

## What the thin F# layer looks like at the end

About 250 LoC, all in `LibExecution`:

```fsharp
module LibExecution.PDDSubstrate

// State (mutable, owned by F#)
let pddIDRegistry : ConcurrentDictionary<string, Guid> = ConcurrentDictionary()
let pddIDFnCache : ConcurrentDictionary<Guid, PackageFn> = ConcurrentDictionary()

// Hot-reload hook (invoked by Cli on JSONL mtime change)
let mutable pddRefreshHook : (unit -> unit) = id

// Body parser hook (LibParser → Dark uses this)
let mutable bodyParser : (string -> Ply<Result<PT.Expr, string>>) option = None

// Test runner hook (Interpreter → Dark uses this)
let mutable testRunner : TestRunner option = None

// Interpreter dispatch: Pending → call Stdlib.PDD.materializeOne via runtime
let dispatchPending (handle : Guid) (name : string) : Ply<Option<RT.PackageFn>> =
  // pseudo:
  // 1. cache lookup (concurrent dict)
  // 2. if miss, invoke Stdlib.PDD.materializeOne via ExecutionState
  // 3. on success, cache and return
  // 4. notify pddRefreshHook
  uply { ... }
```

That's it. Everything else is in Dark.

## What we lose by doing this

Honest trade-offs:

- **Latency overhead.** Calling a Dark fn from F# adds maybe 1–10ms
  per call. The LLM call is 1000–10000ms. So irrelevant in practice;
  measure to confirm.
- **Debuggability for the developer.** F# stack traces are crisp;
  Dark error messages aren't yet at parity. Until Dark traces are as
  good as F# stack traces, debugging materialization issues in Dark
  is harder.
- **CI mocking.** F# unit tests can stub `callOpenAI` directly. Dark
  unit tests need a way to stub `Stdlib.LLM.complete`. Possible via
  a builtin that the test harness controls, but requires plumbing.
- **Boot ordering.** `Stdlib.PDD.materializeOne` is itself a Dark fn
  that needs to be loaded before we can dispatch any Pending. So
  package loading order matters; bootstrap could be fragile.

The latency loss is negligible. Debuggability and CI mocking are
real but solvable. Boot ordering is the real foot-gun — needs design
attention before Phase 4.

## What we gain (revisited)

1. **PDD-on-PDD becomes possible.** User refines the materializer
   with the materializer. The "AI that improves itself" loop —
   actually achievable here, not handwavy.
2. **Prompts are user-overridable** without rebuilding F#.
3. **The materialization strategy** (LLM, search, synthesis, human)
   from BIG-PICTURE 1.1 becomes a List<Dark-fn>. Adding a strategy
   = adding a Dark fn.
4. **The whole materializer becomes a Dark package.** Reusable.
   Shareable. Versionable through the package registry.
5. **PDD's prompts and heuristics are auditable as Dark code** —
   the user can read them, refine them, fork them.
6. **Dark exists to host this kind of thing.** Building it elsewhere
   was a tactical choice for spike velocity; landing it in Dark is
   the strategic move.

## Recommended order if we're starting this in spike-2

If the next spike (or first post-spike sprint) starts here:

1. **Phase 0 first** — prove the prereqs. Ship `Stdlib.HttpClient`,
   `Stdlib.Json`, `Stdlib.File` to a comfortable state. (May already
   be done; needs audit.)
2. **Phases 1 + 2 in parallel** — they're independent. LLM-shim is
   plumbing; prompts are content. Each can ship in a few days.
3. **Phase 3 then** — once 1 and 2 are merged, do the parsing +
   scoring move. Drops the mini-parser cleanly.
4. **Phase 4 is the big one** — orchestrator. Allow 1 week. This is
   where the boot-ordering question gets answered.
5. **Phase 5** — CLI shim. Mechanical once 4 is done.
6. **Phase 6** — defer until there's user demand for HTMLView-as-
   Dark-service.

After Phase 4 lands, **the spike's purpose has been fully
discharged**. We started with "can we even do this?" We ended with
"PDD is a Dark library that users can fork."

## Bottom line

Today: PDD is an F# subsystem with a Dark deployment target.

End of this roadmap: PDD is a **Dark library** with a thin F#
substrate just for runtime state and parser/interpreter glue. The
materializer, prompts, strategies, scoring, CLI commands, and HTML
view all live as user-readable, user-refinable Dark code.

This is the destination. Three weeks of focused work after the
spike merges. The PDD-on-PDD recursion gets unlocked the moment
Phase 4 ships; everything before is foundation.

# Pseudocode-Driven Development — synthesis & how to hack on it

**Date:** 2026-05-13
**Author:** Claude (synthesizing Stachu's notes + new ideas)
**Inputs read:**
- `https://stachu.net/psuedocode-driven-development` (the original F# post)
- `~/vaults/Darklang Dev/05.Implementation/AI/dl-pdd.md`
- `~/vaults/Darklang Internal/Meeting Notes/dl-mar15-advisor-call.md`
- `~/vaults/Darklang Dev/05.Implementation/Tracing/dl-tracing.md`
- `~/vaults/Darklang Dev/90.Stachu/dl-prompt.md`
- `https://github.com/StachuDotNet/ai-pdd` — your earlier Python PoC (`pdd.py` + `example.py`)
- The chat fragment above (the most recent + clearest formulation)

---

## 1. What the idea has *become*

The original F# post is small and elegant: F# lets you write what reads like pseudocode, and once it compiles, it's probably right. The whole pitch is that *naming + type-checking + pipe operators* let you say "what I mean" instead of "how to do it," and then you slowly fill in the bodies.

What you've drafted in the chat above is a much more ambitious thing. It's not "humans write pseudocode and fill it in." It's an **algorithm for the runtime itself**:

1. Generate pseudocode fast — mostly garbage. (LLM)
2. Parse it leniently.
3. For every name you don't recognise, fork two tasks in parallel:
   - a *find* task (does this exist somewhere already?)
   - a *generate* task (write it from scratch)
4. Start evaluating as soon as anything is runnable. Don't wait for the whole tree to be resolved.
5. When eval hits an unresolved name, **suspend that branch** — do other useful "future" work in the meantime.
6. When the name resolves, plug it in and resume.
7. Recurse: generated code is itself pseudocode, with its own holes.

That's a different beast. It's **speculative execution + lazy code synthesis + parallel name resolution**, with the LLM acting as both the source of new code and the search index. The "type signatures as contracts" thing from the F# post is now the *coordination protocol* between the speculative threads.

I think this is the most interesting framing of PDD you've written down so far. The dl-pdd.md vault note hints at the same shape ("runtime-generated code", "AI-driven laziness") but doesn't crisply describe the *concurrency model*. That's what the chat fragment adds.

---

## 2. Why this is genuinely new (and where it isn't)

**Not new:**
- Lazy evaluation. Haskell's been doing this since the '80s.
- JIT compilation of unresolved references — every dynamic language has some version.
- Futures / promises for parallel work — every modern runtime.
- LLM-fills-in-the-body — this is just "AI code completion" at function granularity.

**The combination is new, and interesting:**
- Treating *the source program itself* as a partially-resolved, lazily-evaluated value.
- Letting the **interpreter drive** the LLM (not the IDE, not the human) — the runtime is asking for the next chunk of code based on what it actually needs to evaluate next.
- The signature/type is the **handle**: speculative threads coordinate through "I promise to produce a fn with this shape" the same way Haskell threads coordinate through MVars.
- **Recursive pseudocode** — bodies are pseudocode too, so the process is fractal.

The thing that makes me think this is actually a paradigm shift rather than a clever pattern: in normal dev, the source is the slowest moving artifact and runtime is fast. Here, the source is **constantly being materialized** at runtime speed. The dev loop and the eval loop merge.

---

## 3. The hard problems

Worth being honest about these up front. None are dealbreakers, but each will eat a week of your life if you don't have a plan:

### 3.1 Signature consensus
The "find" task and the "generate" task may both succeed with **different signatures**. Whoever wins, the *other* speculative work downstream may be invalidated. You need either:
- A coordinator that locks signatures early ("first one wins, others abort"), or
- A unification step (rare, hard), or
- Cheap restartable speculation (probably right — Dark's content-addressed packages help here).

### 3.2 When to give up
The find-or-generate fork is unbounded. The find side may search forever. The generate side may produce hallucinated nonsense. You need budgets — time, tokens, retries — at every level. The advisor call notes already say "optimize for prompt-to-tested-and-live" — that metric is essentially **what you bound by**.

### 3.3 "Future work" while waiting
The fragment says "if there's any 'future' code you can do in the meantime, do that." This is great in principle, brutal in practice. You need an explicit dependency graph of which work *can* proceed without the blocked branch. In a pure-functional language this is tractable (Dark!). In anything with effects, you need an effect system to know what's commutative.

### 3.4 Garbage-tolerant parsing
The post says "mostly garbage." A real parser will reject most of it. You need a parser that returns *partial trees with hole-markers* on failure, not a syntax error. This is doable — combinators are good at this — but it's a non-trivial project on its own. Or: skip parsing, let the LLM emit structured output (signature + body separately) and bypass the parser for the skeleton.

### 3.5 Trace + replay
Without tracing, this becomes un-debuggable. The dl-tracing.md note already calls this out: agents traced, replayable, conversations as CRDT-like ordered op-lists. The PoC must produce a trace from day one or you will not understand what your own system is doing.

### 3.6 Type checking under speculation
If you commit to type-driven coordination (which you should), the type-checker has to handle "I don't know yet" cleanly. F#'s type system is too eager; you'd need something more like *bidirectional* typing where types can be inferred from use sites *while* declaration sites are still pending.

---

## 3.5 What your existing `ai-pdd` Python repo already gives you

You said "could be garbage" — it isn't. It's a clean, tiny seed. Quick recap so you don't have to re-read it:

- `@ai("description")` decorator over a Python function with type hints and a `...` body.
- A global `_registry: dict[str, FnMeta]` storing each fn's signature, description, hints, source, and compiled impl.
- On first call, `_generate()` shells out to `claude -p` with a prompt containing: signature, description, hint examples, the calling args, and the signatures of *other* registered fns. The returned body is compiled with `exec()` and cached.
- `hint(fn, input=..., output=...)` adds example I/O pairs to the prompt.
- `show()` and `source()` for introspection.

That's a **working v0** of the F#-blog-post version of PDD: pseudocode = a typed stub, body filled lazily by LLM on first call, cached forever. ~180 lines.

What it does **not** have (and what the chat-fragment vision wants):

| ai-pdd today | Chat-fragment vision |
|---|---|
| Body generated on first call (lazy, sequential) | Body generated speculatively in parallel, *before* called |
| One LLM path: generate | Two paths: find-existing **or** generate |
| Generation blocks the call site | Call site suspends; other "future work" proceeds |
| Bodies are real Python (no further holes) | Bodies are themselves pseudocode — recursive |
| No tracing beyond `print()` | First-class trace as the artifact |
| Single signature, no consensus issue | Two parallel attempts may diverge → consensus needed |
| Python syntax = parser comes free | "Mostly garbage" pseudocode → needs lenient parser or JSON-shaped LLM output |

**Read:** the gap between what you have and what you want is exactly the **concurrency + speculation + recursion** layer. The lazy-generation + caching + registry layer is already done. That's a much smaller spike than starting cold.

I'd start the new spike as a fork of `ai-pdd` — keep the registry, decorator, and prompt shape; replace the synchronous `_generate` with the find-or-generate scheduler; add a trace log; teach the registry to hold `Future[Impl]` not just `Impl`. Maybe a week-end.

The example.py stock-analysis demo is also a perfect target program — it's literally the F# blog post's example, and you already wrote it. Re-run it under the new scheduler and compare traces.

---

## 4. Where to build the PoC

I'd push hard against building this in Dark first. Not because Dark is wrong — it's probably the eventual home — but because the runtime primitives you need (suspendable interpreter, futures, speculative eval, partial parsing) **don't exist there yet**, and you'll end up doing two research projects at once.

My recommendation, in priority order:

### Option A — Python, hacky, fast — fork your own `ai-pdd` (recommended)
- You already have it. Bring it back, fork it.
- Keep: `@ai` decorator, `FnMeta`, `_registry`, prompt-building, `hint()`, `show()`, `source()`.
- Replace: `_generate` becomes async. Registry stores `asyncio.Future[Impl]` not `Impl`.
- Add: a *find* coroutine that greps a local stdlib-equivalent corpus by name + sig (no embeddings needed for v1 — keyword match is enough).
- Add: a scheduler that, when an `@ai` fn is first *imported* (not first-called), fires both find + generate in the background. By the time it's called, the future is hopefully already resolved.
- Add: a trace log — append-only JSONL of `{t, event, name, sig, source?, latency_ms, won_by: "find"|"generate"}`.
- Recursion: have the LLM emit bodies that themselves use `@ai` for unknown helpers (or auto-wrap unresolved names — sketchier but more aggressive).
- **Goal:** re-run `example.py` (stock analysis) under the new scheduler, and dump a trace showing what fired in parallel, what was found vs generated, and the end-to-end latency vs the original sequential version.

### Option B — F#, in this repo, leaning on Dark's interpreter
- You already have most of what you need: ProgramTypes, an interpreter, a package store, LLM bindings.
- The interpreter would need a "suspended on missing name" return path, not just "error: not found."
- Concurrency story is less clean (Ply/async-mess), but workable.
- **Goal:** same demo, but the artifacts produced are real Dark package items that go into your live env.
- **Risk:** you'll get sucked into Dark engineering for two weeks before you ever run the LLM loop.

### Option C — Dark itself
- The dl-prompt.md note already mentions "build minimal `prompt` command." That's basically this.
- Two blockers from the advisor call: agents aren't quite modelled yet, and async/concurrency in Dark is still TODO.
- Don't start here, *but* keep it as the target. Every primitive you build in option A or B should be sketched as "and here's the Dark fn this would become."

### Option D — Hybrid (sneaky-best for the long run)
- Python PoC for the algorithm.
- Once the algorithm shape is stable, **port the orchestrator to Dark**, keeping the LLM provider + interpreter in F# (you already have both).
- The Dark side becomes the live coding/inspection surface; F# stays as the engine.
- This matches your "stop treating the monorepo as source of truth — the live Dark instance is the central thing" line from the advisor call.

**My pick:** A, then D.

---

## 5. A concrete PoC plan — one weekend, starting from `ai-pdd`

**Day 1 — async-ify the registry**
- Fork `ai-pdd`. `FnMeta.impl: Callable | None` → `FnMeta.impl: asyncio.Future[Callable]`.
- Decorator fires generation eagerly on import (not lazily on first call).
- Wrapper does `await meta.impl` before invoking.
- Re-run `example.py` — confirm still works. **Should be slower** because nothing's parallel yet.

**Day 2 — parallel find-or-generate**
- Add `_find(meta)` that greps a local corpus (toss `cpython/Lib/*.py` or similar into `corpus/`).
- Fire `_find` and `_generate` as two coroutines per fn; `asyncio.wait(..., return_when=FIRST_COMPLETED)`; cancel the loser.
- Add a winning-source field to `FnMeta` for later analysis.
- Re-run `example.py` — should now be roughly as fast as max(find, generate), not sum.

**Day 3 — speculative eval + trace**
- The "future work" idea: when one fn's future is pending, allow callers of *other* fns whose futures are ready to proceed. Python's eager evaluator basically does this for free with asyncio; the work is making sure your fn calls are `await`ed concurrently in `gather()` where possible.
- Add a `Trace` class: append-only JSONL, every generate-start, generate-end, find-start, find-end, call-start, call-end gets a line. Include latency.
- Add a tiny viewer: just `python -m pdd.trace replay <file>` that pretty-prints.

**Day 4 — recursive pseudocode**
- Teach `_generate` to emit bodies that may call functions *not yet in the registry* — and auto-register them as `@ai` stubs with inferred signatures from the prompt.
- Pick a deeper demo: "given a URL, fetch it, parse the HTML, return the headline with the highest sentiment." Three or four levels of generated helpers.
- Watch the recursion happen in the trace.
- This is the moment the "paradigm" claim becomes real or doesn't.

Anything past day 4 is iteration. Once you have the trace, you'll see the actual research questions (which usually aren't the ones you predicted).

---

## 6. The questions I'd want answered after the spike

Not "did it work" — "did it teach me something." Specifically:

1. **Is signature consensus actually a problem in practice?** Or do LLMs converge on near-identical sigs when given the same context? (Empirical — find out.)
2. **What's the eval-to-generation latency ratio?** If generation dominates, parallel speculation is free; if eval dominates, you don't need much parallelism.
3. **How much "future work" is actually available?** I suspect: a lot in pure code, near-zero in I/O-heavy code. This tells you what kinds of programs PDD is good for.
4. **Does the trace become *the* artifact?** I think yes — and the "program" you save to a package is just a stable snapshot of a successful trace.
5. **What does the human do here?** Just review final traces? Edit pseudocode mid-stream? Adjust prompts? This shapes the UX you eventually build in Dark.

---

## 7. Connections to other work you have

Things from your existing notes that this would slot into nicely:

- **Tracing** (`dl-tracing.md`): the trace *is* the program in PDD. Build the trace format with PDD in mind.
- **Content-addressed packages** (Mar-15 call): once a generated fn has a hash, it's free to dedupe. "Find" should hit the package store, not just a corpus.
- **`prompt` command** (`dl-prompt.md`): minimum useful surface — the PDD loop bound to a single `prompt "do X"` CLI invocation.
- **AI fns** (advisor call): a fn whose body is "LLM-fill the I/O contract" is a degenerate case of PDD where generation never gets cached — useful in its own right, and the algorithm here subsumes it.
- **Agents framework** (Feriel's work): the PDD loop is *a* kind of agent, and probably the most important one. Don't build a separate framework — express PDD as an agent.
- **Issue tracking in Dark** (advisor call): same shape! Pseudocode work-items, resolved lazily by AI agents, with type-like signatures.

---

## 8. The thing I'd most want you to think about

The chat fragment's most underdeveloped line is: *"do this all recursively."*

Recursion turns this from a clever speedup into a paradigm. Every generated body is itself pseudocode. Every reference inside that body is itself pending. The fixed point of this process is "a fully materialized program," but **you may never reach it** — you may always be running with some references still in flight.

That's the actual claim PDD is making, if you push it hard: **programs are never finished, only executed enough.** The package store is a cache of the bits that turned out to be needed. Everything else is still a sketch.

If you believe that, it informs every decision: how you do SCM (you don't version "the program," you version traces and caches), how reviews work (you review traces, not diffs), how distribution works (you ship a sketch + a cache, the recipient fills in the rest).

I think the F# blog post hinted at this without quite saying it, and the chat fragment above gets close. Worth writing one more pass — even a paragraph — pinning down what you actually believe about this. The "what's the program?" question.

---

## 9. Concrete next action

If you have an evening: start a fresh directory — `~/code/pdd-spike/` — and write the Day-1 interpreter. ~150 lines of Python. Don't touch LLMs yet. Just prove the suspend-on-missing-ref shape.

If you have a week: do days 1–4 above.

If you have a month: do days 1–4, then port the orchestrator to Dark (Option D), and the demo becomes "watch a Dark CLI session materialize a working program from a one-line prompt." That's the thing you'd show at a conference.

---

*End of report. Next step: print-md this and read it on paper.*

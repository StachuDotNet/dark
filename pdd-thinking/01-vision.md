# 01 — Vision

## The one-sentence pitch

**The interpreter materializes its own source code on demand, in parallel, speculatively, with the LLM as both author and search index — and traces are the artifact.**

## The algorithm (from Stachu's chat fragment)

1. Generate pseudocode, really quickly. Mostly garbage.
2. Parse it (leniently).
3. Many names won't be immediately found.
4. For each unfound thing — in parallel — either **find** it (in package store / corpus) or **generate** it (LLM).
5. As soon as possible, **start eval**.
6. Eval may reference fns that don't exist yet. As soon as one's ready, use it. While waiting, do any "future" work that's possible.
7. Recurse — generated bodies are themselves pseudocode.

## What makes this a paradigm, not a feature

Three claims, each one debatable. Together they're the bet.

### Claim 1: The source is a lazy value

Traditional dev: source code is the slowest-moving artifact. You write it, compile it, then the runtime executes the compiled form. Code change requires re-build, re-deploy, re-think.

PDD: source code is **computed at runtime speed**, by the runtime itself, on demand. The artifact you ship is a *sketch* plus a *cache*. Most of the program is implicit until it isn't.

This is what Smalltalk hinted at (live objects) but didn't fully take. The missing piece was: who writes the code that doesn't exist yet? Smalltalk said "the programmer, in the moment." PDD says "the LLM, in the moment."

### Claim 2: The trace is the program

If source is materialized on demand, then "what ran" is more durable than "what's written." The trace — input, every call, every body that got materialized, every find-vs-generate result — is the **canonical artifact**.

- SCM is over traces, not source files.
- Reviews are over traces (here's what the system actually did).
- Sharing/distribution is "ship a trace + a sketch" — the recipient's runtime can re-materialize.
- Tests are trace assertions ("this input produces this trace shape").

This connects directly to the existing tracing work — `dl-tracing.md` already says agents are traced, replayable, CRDT-like. PDD makes traces *first-class*, not just observability.

### Claim 3: Types are the coordination protocol

When two parallel processes are racing to materialize a function, they need to coordinate without sharing a body. The **signature** is the contract. "I promise to produce a fn with this name and this type."

This is the part the F# blog post nailed and that the chat-fragment extends: types aren't just for catching errors, they're the **handshake** between speculative threads.

A corollary: the type system needs to handle "I don't know yet" without falling over. We can't be eager.

## What this means for users

Two killer demos to aim for:

**Demo A — Pseudo-script:**
Stachu types `prompt "fetch the top HN headline, sentiment-score it, summarize in one sentence"`. The CLI parses this into pseudocode, fires off parallel materialization, starts eval as soon as anything is runnable, streams partial results back. Trace gets saved.

**Demo B — Pseudo-app:**
Stachu writes a Dark "app" in pseudocode — types + fn skeletons. He runs it. It works. Behind the scenes, every body was materialized at first call, cached as a content-addressed package. He reviews + edits some bodies, commits them. The rest stay as-needed.

## Anti-vision (what this is *not*)

- **Not Copilot.** Copilot suggests; PDD executes.
- **Not autocomplete.** Autocomplete is editor-time; PDD is runtime.
- **Not "AI-generated stubs."** Stubs are bodies waiting to be filled by humans. PDD's bodies are filled at the moment they're needed, by the runtime.
- **Not Cursor's chat-then-apply.** No "apply diff" step. The runtime applies its own diff to itself.
- **Not auto-coding agents** like Devin/Aider. Those build a *project*. PDD builds *the function under your cursor, when the interpreter needs it*.

## Where this fits in Darklang

PDD is the **first feature** that makes Darklang's existing primitives feel non-obvious in a new way:

- **Content-addressed packages**: PDD's "find" step hits this. By-hash dedup is free.
- **Live tracing**: PDD's "trace is the program" claim falls right into your tracing infra.
- **Live programming**: Already the model. PDD makes "live" mean "the source itself is live."
- **Branches as views**: A branch is a (sub)set of cached materializations. Stachu's branch and Claude's branch have different bodies for the same name. Reviewing means seeing the trace diff.
- **CLI-first**: Perfect — the algorithm is `prompt → exec → trace`, command-line shaped.

## What we're going to actually build tonight

**Design.** Not code. Design that's so concrete you can sit down tomorrow and start typing.

Specifically, by 8am:
- A clear F# sketch of the LibExecution changes (the call-resolution path).
- A find-vs-generate scheduler design with concrete budgets.
- A signature-consensus protocol.
- A capability/permissions model.
- A list of F# subprojects to disable in the experimental branch.
- A day-1 hacking plan that points at specific files and types.

## Bonus belief I want to commit to

If forced to bet on one direction: **bias toward `generate` over `find`, and bias toward `signature only, empty body` over "delay and try harder."**

Reasoning: generation is faster than search in many cases (one LLM call vs. corpus walk). An empty body that returns `default(T)` is a runnable hole — the program keeps moving. The hole gets filled when needed *or* when its result is consumed in a way that reveals what the body must do. The trace records that "we made up nothing here" and the next iteration of the agent might fix it.

This is the "tolerant runtime" idea from Stachu's feedback. It generalizes: every kind of missing thing has a `none`/`empty`/`default` form that lets eval keep going.

---

## Addendum (2026-05-13 01:35) — sharpenings from later iterations

The framing above stands; these are sharpenings, not pivots.

### The north-star demo

When you sit down to hack: aim everything at **Demo 6** in `14-demo-programs.md` — the HN headline sentiment program. `prompt → trace → working result` in one CLI invocation. That's the elevator pitch and the acceptance test for the whole spike.

### Parser strategy is settled (tentatively)

Going with **P3** from `09-carving-the-codebase.md`'s parser question: skip parsing pseudocode entirely; the LLM emits structured JSON `{sig: "...", body: "..."}`.

Verified tonight with a gpt-4o-mini sanity call (model returned the exact shape on the first try, deterministic at temperature 0, 66 input + 20 output tokens, ~$0.00003 per call). LibParser still stays for loading `.dark` files; only the generate-path bypasses it. Removes a whole class of parser-error handling.

### The human is a materializer

Per `07-human-in-loop.md`: human input is the *third* materialization path alongside find and generate. Same `MaterializeResult` shape, same caching to package store, same trace events. **Do not** build a separate "ask the user" workflow — fold it into the existing surface.

### Cheap by default

`gpt-4o-mini` (OpenAI) or `claude-haiku-4-5` (Anthropic). Hardcoded for the spike. `@deep_materialize` annotations can opt into pricier models per-fn; the default stays cheap because the OpenAI spike budget is $10. The spike could burn ~50K cheap calls before noticing the bill.

### The four runtime knobs

By Day 4-5, the CLI should expose:

```
--tolerance strict|loose|debug
--allow http,fileread,writeio,exec
--materialize-budget 1000ms     (default; per-call)
--model cheap|deep|<modelname>
```

User-facing surface. Internals are subject to change; these are the public API.

### The five-claim summary

For when you're explaining to anyone else:

1. **The source is lazy.** Names + sigs; bodies materialize on demand.
2. **The trace is the program.** Everything else (source, cache) is derived.
3. **Types are the coordination protocol.** Pending references carry sig hints; type unification is how parallel materializations agree.
4. **The runtime is tolerant.** Missing things substitute defaults; eval keeps moving; recoveries are auditable.
5. **The human is a materializer.** When find and generate fail, the human is the third path. Their answers cache like any other.

Memorize these. Anti-summarize: don't say "PDD is like Copilot but for runtime" — that misses every interesting claim.

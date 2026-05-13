# PDD — Thoughts + Roadmap

*Companion to REPORT-state.md. The architectural decisions, open questions, and the next big idea.*

## The architectural insight from today

You raised it: **Locations should support both hash-refs and ID-refs.**

- **Hash-refs** = today's package store. Content-addressed, immutable, settled. The bedrock.
- **ID-refs** = mutable, evolving. PDD-style fns whose body iterates over time. References by ID give you "whatever the latest version is."

A function's lifecycle:
```
  Pending  →  Provisional  →  Real  →  (eventually)  Hash-locked
   (LLM       (works but    (works    (frozen content-addressed,
   inflight)  rough)        and       can never change without
                            done)     a rename)
```

Today the implementation conflates these. Pending has a `handle: Guid` (effectively an ID) but materialized fns get a content-addressed `hash` AND live in a sidecar JSONL (not the real package store). `refine` mutates by appending — last write wins on cache reload. There's no first-class "this fn IS the one named X, look up the current version" semantic.

**What a proper ID-ref system would unlock:**

1. **References to evolving fns stay live.** If `homePage` is an ID-ref, and we refine `homePage` to a richer body, EVERY caller automatically uses the refined version on next call. No re-materialize-everywhere.
2. **Versioned history per ID.** Each ID has a timeline of bodies. You can `dark pdd revert` to a prior version. The HTML view can show the diff between versions.
3. **Settling via promotion.** `dark pdd promote homePage` snapshots the current body, mints a content hash, and freezes that as the canonical version. Subsequent refines fork to a new ID.
4. **Mixed graphs.** A stable hash-ref'd fn can call into an evolving ID-ref'd helper. The "trustworthy" parts of a program are hash-locked; the "still figuring it out" parts are ID-mutable.

**The migration path** (informal sketch):

- Add `FQFnName.PddRef of {id: Guid; name: string}` (an *evolving* ID-ref), distinct from `Pending` (which still means "not yet materialized") and `Package` (immutable hash).
- The package store grows a second table: `pdd_refs` keyed by ID, with a foreign key to a body row (which gets a fresh row each refinement).
- `dark pdd refine` writes a new body row, updates the `pdd_refs` table's "current" pointer.
- `dark pdd promote <id>` reads the current body, computes its hash, inserts into `package_fns`, returns the hash. Now you can rename references to the hash if you want frozen behavior.

The interpreter's executionPoint match needs an arm for `PddRef`: look up current body via ID → cache InstrData → execute. Already exists in concept for Pending (`pendingFnInstrCache`); generalizing it is mostly renaming + threading through serialization.

**Why this matters more than it sounds:** Today the system thinks "the function is done when it materializes once and passes its tests." That's wrong for creative fns and increasingly wrong as you scale. The right model is "the function is *living*; it improves on observation, and we can choose to freeze a moment of its life." That's the trace-driven dev pitch made concrete.

## The "F# → Dark" eventual migration

You mentioned wanting to move the F# PDD code into Dark source eventually. This makes sense and the system is honestly close to allowing it:

- The materializer is ~1500 LoC of F# that calls OpenAI HTTP, parses JSON, walks Instructions. Most of this *could* be Dark.
- The interpreter hooks (Pending arm in Apply, executionPoint match, etc) need to stay F#.
- The LLM-call orchestration could be a Dark module: `Stdlib.PDD.materialize`, `Stdlib.PDD.refine`. Once `Stdlib.HttpClient` is solid, this is mostly file-shuffling.

For a first chunk: move `refineFn` to Dark. It's the cleanest self-contained piece — read cached entry, call HTTP, score, write back. No interpreter hooks needed.

I haven't done that, per your note. Bookmarking.

## What I learned (the messy version)

**Tests-as-gate is wrong for creative tasks.** The QA-LLM hallucinates expected outputs that don't match the body's perfectly-valid outputs. I fought this for hours before classifying fns as verifiable vs creative and skipping QA for the latter. The deeper lesson: "is this output correct?" has different shapes for math (deterministic) vs HTML (subjective). The system needs to know which it's dealing with.

**LibParser pickiness leaks everywhere.** Multi-line `let` chains. `let f x = ...` (function-binding let). Tuples. Pattern destructuring. Curried lambdas. Each one the LLM produces naturally; each one trips LibParser. I fixed multi-line by normalizing newlines. The rest I worked around via prompt engineering ("no `let f x =`, use `let f = fun x ->`"). At some point the prompt becomes a programming language dialect of its own. Not sustainable; either the parser grows or the LLM-output is post-processed more aggressively.

**Type hints propagating through pipes is real and worth doing.** The runtime arg-type propagation (interpreter writes hints before materialize) unlocked the CSV pipeline. Previously, `getDate(findMaxVarianceRow(parseRows csv))` had downstream fns getting `arg type "?"` because parse-time inference only saw the literal string at the outermost call. Now they get `List<List<String>>` from `parseRows`'s actual return.

**Iteration is the unlock for "real" output.** The single biggest jump in HTML quality came from `dark pdd refine` — same input context, same LLM, but a second pass framed as "make this better." For creative tasks, one-shot is never enough.

**Cost is laughable.** ~$0.10 of $10 budget in a long day of iteration. The cap is friction (rate limits at 10+ parallel), not money.

## What I'd build next

Ranked by what I think would matter most:

1. **ID-refs as a first-class location kind.** The above. Probably 1-2 days of focused work, end-to-end. Unlocks the rest.
2. **Background refine loop.** A daemon that picks Provisional fns and refines them when idle, recording the diff. Today's `refine --all` is the manual version. Auto-version: "while the server runs, the pages get better." This IS the trace-driven dev demo when it works.
3. **Real package promotion.** `dark pdd promote <name>` writes to the actual `package_fns` table. Pending caller-references continue to work; you can also `dark search` or `dark tree` to find the materialized fn. Today it's a JSONL sidecar.
4. **Per-route lazy materialization.** Today all routes pre-fetch at server start. Switching to "materialize on first hit" would (a) make startup instant, (b) better match the user's "prioritize what the user is using" intent. The pre-fetch path stays for `--all`-style ahead-of-time work.
5. **A `dark pdd diff <name>` command.** Show the diff between versions of a refined fn. Useful for trust ("what changed?") and for picking a moment to promote.
6. **Move the regex mini-parser out.** It's still in the codebase as fallback. Every demo now uses LibParser. ~300 LoC of dead weight. Removal is a pure simplification.
7. **`dark prompt` with file-context.** Pass `--context <file>` to inject a file's content into the decompose prompt. For "port the React file at <path> to a Dark route handler", essential.

## The pitch, refined after a day of building it

*"PDD is Darklang where the source code is **partial**. You sketch shapes — function names, signatures, pipelines — and the runtime materializes the bodies on demand via LLM. Some materializations are settled and frozen; others are **living**, refined by usage, with a trace of every version they passed through. Trace IS the program; source is just the seed."*

The proof that this works isn't `factorial 6 → 720`. It's that today, ten routes of a real website serve real HTML, with bodies that *improved over a day of iteration* without anyone editing the source.

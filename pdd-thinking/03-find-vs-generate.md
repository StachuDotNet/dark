# 03 — Find vs Generate (the scheduler)

> Stachu's directive: both paths should give up in <1 second by default. Configurable up to ~1min for "I care about this one." Empty body OK.

## The two paths

| Path | Method | When it wins | Cost | Quality |
|---|---|---|---|---|
| **Find** | Search existing package store + indexed corpora for a fn matching name+sig | The fn already exists (Darklang stdlib has it; or a previous session generated it) | Cheap (<100ms typical) | Best — battle-tested code |
| **Generate** | LLM call with sig + description + nearby context | Novel logic, or no good match in corpus | Mid (~300-800ms with Haiku) | Variable — needs review eventually |

## Default policy

```fsharp
let defaultMaterializeOptions : MaterializeOptions =
  { findBudgetMs = 1000
    generateBudgetMs = 1000
    allowEmptyBody = true
    preferPath = RaceBoth }
```

Both fire. First non-failure wins. Loser is cancelled. If both fail by deadline → `EmptyBody` (signature-preserved, body returns `default(T)`).

## The race, in pseudo-F#

```fsharp
let materializeFn (p : Pending) (opts : MaterializeOptions) : Ply<MaterializeResult> =
  uply {
    let findCts = new CancellationTokenSource(opts.findBudgetMs)
    let genCts = new CancellationTokenSource(opts.generateBudgetMs)

    let findTask =
      Task.Run<MaterializeResult option>(fun () ->
        runFind p findCts.Token) |> Task.WithTimeout opts.findBudgetMs

    let genTask =
      Task.Run<MaterializeResult option>(fun () ->
        runGenerate p genCts.Token) |> Task.WithTimeout opts.generateBudgetMs

    match opts.preferPath with
    | RaceBoth ->
        let! firstWinner = Task.WhenAny [| findTask; genTask |]
        // Cancel the loser:
        if firstWinner = findTask then genCts.Cancel() else findCts.Cancel()

        match! firstWinner with
        | Some result -> return result
        | None ->
            // First finisher didn't win — wait briefly for the other.
            // (It might have a partial result.)
            ...

    | PreferFind ->
        match! findTask with
        | Some result -> return result
        | None ->
            match! genTask with
            | Some result -> return result
            | None when opts.allowEmptyBody -> return EmptyBody (mkSigOnly p)
            | None -> return Failed "both paths failed"

    | PreferGenerate -> // symmetric
  }
```

(Sketch; cancellation propagation needs care.)

## "Find" — what does it actually do?

For the **PoC phase**, three sources, in order of cost:

1. **In-process cache** — already-materialized fns this session. O(1) lookup by name+sig hash.
2. **SCM-resolved by name** — Dark's name-resolution machinery (it already exists, see `searchByPath` etc. — note: verify before relying on this; it might just be search-by-substring, in which case treat as 3 below). Returns the hash if found, then `getFn`.
3. **Substring grep over package_fns** — `SELECT hash, name FROM package_fns WHERE name LIKE '%foo%'` then filter by sig match. Cheap, dirty.

Later: embedding-based fuzzy search via `sqlite-vec` or similar. Not for the PoC.

## "Generate" — the LLM call

One LLM call, with this rough prompt shape:

```
You are generating a Darklang function body.

Signature: {p.name}({sigHint.paramHints}): {sigHint.returnHint}
Context (callsite excerpt): {nearbyCode}

Available helper functions you can call (by name):
{listKnownFnsInScope}

Available types:
{listKnownTypesInScope}

Return ONLY the function body as a Darklang expression. Do not include
the `let {name} = fun ... ->` wrapper. Do not include explanations.
If you don't know what the function should do, return `()` (unit).
```

Returns Darklang source. We parse it (using existing parser machinery) to produce a `PackageFn`. If parsing fails, we have options:
- Retry once with parser error in the prompt.
- Give up, return `EmptyBody`.

**Model choice for the spike:** Haiku 4.5. Fast (~300ms typical), cheap, good enough for the speed-of-light demo. Upgrade to Sonnet for the "I care about this fn" path (~60s budget).

## The "I care about this fn" mode

How does the user mark a fn as "spend more time"?

Two surfaces:

### Source-level annotation (compile-time)
```
@deep_materialize
let fn solveAdventOfCode (input: String): Int64 = ...
```
Translates at PT2RT to a `MaterializeOptions` with a 60s budget.

### Runtime-level override (interactive)
The CLI session has a `materialize <fn-name> --budget 60s --model sonnet` command that forces re-materialization with a richer budget.

For the PoC: skip both. Just have one budget. Add the dial later.

## Cancellation + speculation waste

When the race finishes, the loser may have spent 700ms on work that gets thrown away. Two mitigations:

1. **Cache partial work.** Even if `find` lost to `generate`, the find subsystem can save its sig-match index for the next time someone asks for the same name.
2. **Speculation budget overall.** Across the session, cap parallel speculations at `Environment.ProcessorCount`. If we're already at the cap, new pendings queue instead of speculating immediately.

## What about WHEN to fire speculation?

Two timing strategies:

### Lazy: on first call
Materialize only when eval reaches `Apply(Pending p)`. Simpler, lower waste, but call site blocks until ready.

### Eager: on parse/load
At PT2RT or load-time, walk the program for `Pending` references and start materializing them all in parallel, immediately. By the time eval reaches them, many are ready.

**Recommendation: eager, with lazy fallback.**

Eager has a huge UX win — most pendings are ready before they're needed. Lazy is the bare minimum. The interpreter handles both because the suspend-on-pending path already exists.

Eager kickoff happens in `Execution.execute`:
```fsharp
let execute exeState instrs = task {
    do! eagerlyMaterializePendings exeState instrs  // NEW
    let! result = Interpreter.execute exeState vm
    ...
}
```

## Configurability — the user-facing knobs

Eventually exposed via CLI args / config file:

```yaml
pdd:
  default_budget_ms: 1000
  deep_budget_ms: 60000
  parallel_max: 8
  prefer_path: race  # | find | generate
  allow_empty_body: true
  model_default: claude-haiku-4-5
  model_deep: claude-sonnet-4-6
  retry_on_parse_error: true
```

For the PoC, hardcode. Add CLI args one at a time as you need them.

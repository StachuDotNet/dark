# Demos & Spike Budgets

> **STATUS:** Written mid-spike. Demo 1-3 + variants shipped (see
> README.md "Live demos verified"). Demo 6 (HN headline sentiment)
> still aspirational. Day-N target dates are obsolete (spike ran
> ~2-3 days, not 14). Current verified demos: addOne, myAbs,
> myMaxOf, factorial, fibonacci+factorial parallel, sumList,
> doubleAll, longestRow, parseRows, 32-route darklang.com clone.
> "Current spend" line is also stale (~$0.30 now, not $0.0012).
> For current state see `WRAP-UP.md` and `README.md`.

What we're building toward + the envelopes that keep the spike healthy.

---

## §1 Demos (acceptance gates, in increasing difficulty)

### Demo 1 — `addOne` (trivial — proves the wire)
```darklang
let myFn (x: Int64): Int64 = addOne x
```
- Pending: `addOne` → LLM materializes with `"x + 1L"` → mini-parser emits Int64.add instructions
- Expected: `myFn 5L` returns `6L`
- Stresses: Pending → materialize → execute pipeline
- **✅ Verified live** (`addOnePendingActuallyComputes` integration test)

### Demo 2 — Stock variance pipeline (F# blog post canon)
```darklang
let getDateForHighestVarianceInStock (csv: String): String =
  csv
  |> parseCsv
  |> skipHeader
  |> List.map calculateVariance
  |> sortByVarianceDescending
  |> takeHead
  |> getDateField
```
- 6 pendings in parallel; mixed find (Stdlib has takeHead, skipHeader) + generate
- Stresses: race scheduler, sig consensus, parallel materialization, multi-step trace

### Demo 3 — Recursive Fibonacci
```darklang
let fib (n: Int64): Int64 =
  if n <= 1L then n
  else fib (n - 1L) + fib (n - 2L)
```
- Self-referencing Pending; needs `currentlyMaterializing` registry to avoid thrashing
- Stresses: cycle handling, termination

### Demo 4 — Mixed materialized + pending
```darklang
let process (xs: List<Int64>): Int64 =
  xs |> Stdlib.List.map double |> Stdlib.List.sum |> normalize
```
- `normalize` is the only Pending; others resolve through normal name resolution
- Stresses: pending coexists with normal calls without interference

### Demo 5 — Tolerance under failure
```darklang
let pipeline (n: Int64): Int64 =
  n |> fnThatWillFail |> doubleIt |> Stdlib.Int64.add 1L
```
- Force `fnThatWillFail` to error during materialize (budget=0)
- Loose mode: runs to completion; trace shows recovery; final value is `1L` (0 doubled + 1)
- Strict mode: crashes
- Stresses: RecoveryPolicy swap, EmptyBody substitution

### Demo 6 — HN headline sentiment (THE HEADLINER)
```darklang
let analyze (): String =
  let url = "https://news.ycombinator.com" in
  let html = fetchUrl url in
  let topHeadline = extractTopHeadline html in
  let sentiment = sentimentScore topHeadline in
  summarize topHeadline sentiment
```
- 4+ pendings; `fetchUrl` needs `CapReadNet`; mixed pure + impure
- Stresses: everything — capabilities, parallel materialization, real interpreter run, trace as artifact

### Demo 7 (stretch) — Recursive descent
```darklang
let solveAdventOfCode (input: String): Int64 = doIt input
```
- Materialize `doIt`; its body refers to `parseInput`, `findAnswer`, …; recursive materialization
- Stresses: fractal pseudocode, depth limits, real research territory


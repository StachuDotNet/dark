# Demos & Spike Budgets

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
- **Day-5 target**

### Demo 3 — Recursive Fibonacci
```darklang
let fib (n: Int64): Int64 =
  if n <= 1L then n
  else fib (n - 1L) + fib (n - 2L)
```
- Self-referencing Pending; needs `currentlyMaterializing` registry to avoid thrashing
- Stresses: cycle handling, termination
- **Day-6 target**

### Demo 4 — Mixed materialized + pending
```darklang
let process (xs: List<Int64>): Int64 =
  xs |> Stdlib.List.map double |> Stdlib.List.sum |> normalize
```
- `normalize` is the only Pending; others resolve through normal name resolution
- Stresses: pending coexists with normal calls without interference
- **Day-3 target**

### Demo 5 — Tolerance under failure
```darklang
let pipeline (n: Int64): Int64 =
  n |> fnThatWillFail |> doubleIt |> Stdlib.Int64.add 1L
```
- Force `fnThatWillFail` to error during materialize (budget=0)
- Loose mode: runs to completion; trace shows recovery; final value is `1L` (0 doubled + 1)
- Strict mode: crashes
- Stresses: RecoveryPolicy swap, EmptyBody substitution
- **Day-4 target** (needs the recovery wire that's design-only today)

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
- **Day-10 target** — the elevator demo

### Demo 7 (stretch) — Recursive descent
```darklang
let solveAdventOfCode (input: String): Int64 = doIt input
```
- Materialize `doIt`; its body refers to `parseInput`, `findAnswer`, …; recursive materialization
- Stresses: fractal pseudocode, depth limits, real research territory
- **Day-12+ target**

## §2 Acceptance summary

| # | Day target | Verified? |
|---|---|---|
| 1 | 2-3 | ✅ |
| 4 | 3 | partial (unit) |
| 5 | 4 | not yet (no recovery wire) |
| 2 | 5 | not yet (no scheduler) |
| 3 | 6 | not yet (no recursion handling) |
| 6 | 10 | not yet (no real LLM via CLI yet) |
| 7 | 12+ | research |

**Demo 6 is the elevator pitch** — `prompt → trace → working result` in one CLI invocation, with the HTML view visualizing what materialized.

Where the demos live (planned): `backend/dark-packages/pdd_demos/*.dark`.

---

## §3 Spike budgets

### Engineering time
- **Day 3 = health checkpoint.** Demos 1 + 4 green by Day 3 means the spike is on track. If not, stop adding features and figure out what's structurally wrong.
- **Day 10 = Demo 6 target.** If green: write the blog/video.
- **Day 14 = hard stop.** Postmortem + pivot or abandon. Don't muscle through.

### OpenAI dollars
- Hardcoded `gpt-4o-mini`. ~$0.00005/call.
- $10 budget = ~125K calls if you cap before tripwires.
- **Trip-wire** at $7 spent; **hard stop** at $9.50.
- Add live $-counter that prints estimated spend after each call. Abort if estimated spend ever crosses $5 in a single session.
- Sonnet via `@deep_materialize` opts into 30x cheap-rate; reserve for fns hitting AST-retry repeatedly.
- **Current spend: ~$0.0012.** ~$9.998 of $10 remains.

### Cognitive load
- If you're reading 4 docs to remember why X, stop. Re-read DESIGN.md and the 5 claims.
- If a single problem stays unsolved for 30 minutes, revert + try a different angle.
- If the spike is no longer fun, stop. Take a day. Re-evaluate the 5 claims.

### Telemetry per LLM call
Each call emits a `cost` event into the trace:
```json
{"t":...,"ev":"cost","model":"gpt-4o-mini","in":140,"out":34,"$":0.000041}
```
`dark pdd trace cost <id>` sums + breaks down. (Not yet built.)

---

## §4 Success criteria (three levels)

### Bronze
The runtime materializes one fn via LLM and executes it correctly. The trace records the materialization. (Day 2-3.) **✅ Done.**

### Silver
A multi-step program (Demo 2: stock variance) runs with all pending fns materialized. Materialization happens in parallel (scheduler works). Some recovery happened along the way (tolerance works). (Day 5-7.)

### Gold
Demo 6 runs. The user types one prompt and gets a working answer. The trace is the artifact you keep — source + cache + trace shipped together. Reviewing the trace feels like a *natural* dev activity. (Day 10+.) **This is the paradigm claim.**

---

## §5 Failure modes that count as success

- **Spike fully works but the paradigm feels wrong.** Most valuable outcome. Write the postmortem. (Per EMPIRICAL §5 R4.)
- **Demo 6 doesn't go green by Day 14.** Write what blocked. The design docs are still durable.

---

## §6 Decision: when to push the branch

**Never during the spike.** This is forcing function as much as safety.

After the spike, two paths:
- **Spike works, paradigm feels right:** cherry-pick the LibExecution interventions (Pending variant, materializeFn, scheduler) onto a clean branch off main. Build LibPDD properly. Start landing real users.
- **Spike works, paradigm feels off:** write the postmortem. Maybe the trace UX is wrong, or sig consensus thrashes, or tolerance hides too much. Document and pivot.

Either way: **plan to rewrite from scratch**, not surgically extract. Cross-cutting concerns make clean extraction painful.

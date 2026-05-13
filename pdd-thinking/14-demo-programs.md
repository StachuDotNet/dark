# 14 — Demo Programs

Six concrete programs to drive the spike, in increasing order of difficulty. Each one stresses different parts of the design. Tests them all and the spike is "real."

Use these as your acceptance criteria: when each one runs green, you've crossed a milestone.

---

## Demo 1 — `addOne` (trivial)

**Purpose:** prove the Pending → materialize → execute pipeline works at all.

**Source (sketch):**
```darklang
let myFn (x: Int64): Int64 =
  addOne x
```

**The pending:** `addOne`. The runtime hits `addOne(x)` and there's no body. Materializer fires, generates `let addOne (x: Int64): Int64 = x + 1`, runtime executes.

**Expected result:** `myFn 5L` returns `6L`.

**What it stresses:**
- The `FQFnName.Pending` variant
- `materializeFn` returns a real body (not just EmptyBody)
- The interpreter resumes the call with the materialized body
- Tracing captures `materialize_start` → `candidate` → `materialize_done`

**What it doesn't stress:**
- Concurrency (one pending)
- Recovery (it should succeed)
- Find (it's a generated fn)

**Day-3 acceptance:** this demo runs green.

---

## Demo 2 — Stock variance (the F# blog post)

**Purpose:** the canonical PDD pipeline from the original blog post.

**Source (sketch):**
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

**The pendings:**
- `parseCsv : String -> List<Dict<String, String>>`
- `skipHeader : List<'a> -> List<'a>`
- `calculateVariance : Dict<String, String> -> Dict<String, String>`
- `sortByVarianceDescending : List<Dict<...>> -> List<Dict<...>>`
- `takeHead : List<'a> -> 'a`
- `getDateField : Dict<String, String> -> String`

**Expected result:** given a sample CSV, returns the right date.

**What it stresses:**
- Six pendings in parallel — confirms the scheduler runs them concurrently rather than serially
- Find vs generate — `takeHead`, `skipHeader` should resolve via find (Stdlib.List has them), the rest via generate
- Signature consensus — the pipeline shape constrains each fn's I/O type
- Trace shows the materialization waves

**Day-5 acceptance:** runs end-to-end.

---

## Demo 3 — Recursive Fibonacci

**Purpose:** prove self-referencing pending references work.

**Source:**
```darklang
let fib (n: Int64): Int64 =
  if n <= 1L then n
  else fib (n - 1L) + fib (n - 2L)
```

**The pending:** `fib` itself. The materialized body of `fib` references `fib`. The generator must:
- Know that `fib` is being materialized in this very call
- Reuse the in-flight handle when it sees the recursive reference
- Not create a new pending and infinite-loop

**Expected result:** `fib 10L` = 55.

**What it stresses:**
- The `currentlyMaterializing` registry (per `04-signature-consensus.md`)
- Termination — the runtime doesn't loop trying to materialize `fib` repeatedly
- Cache hit on recursive calls

**Day-6 acceptance.**

---

## Demo 4 — Mixed materialized + pending

**Purpose:** show that pending fns coexist with already-materialized ones cleanly.

**Source:**
```darklang
let process (xs: List<Int64>): Int64 =
  xs
  |> Stdlib.List.map double      // exists
  |> Stdlib.List.sum              // exists
  |> normalize                    // PENDING
```

**The pending:** `normalize`. Everything else resolves through normal name resolution.

**Expected result:** correctly computed answer.

**What it stresses:**
- Normal name resolution side-by-side with pending materialization
- The interpreter doesn't get confused by mixed FQFnName variants
- The Stdlib calls don't trigger the materializer at all (they should fast-path to existing)

**Day-3 acceptance** (alongside Demo 1).

---

## Demo 5 — Tolerance under failure

**Purpose:** prove the tolerant runtime actually keeps running.

**Source:**
```darklang
let pipeline (n: Int64): Int64 =
  n
  |> fnThatWillFail              // PENDING — generator returns garbage
  |> doubleIt                    // PENDING — should still run on default
  |> Stdlib.Int64.add 1L
```

**Setup:** force `fnThatWillFail` to error during materialization (or set its budget to 0ms so it always EmptyBody's).

**Expected result with tolerant mode:** runs to completion. Trace shows the recovery. Final value is `0L + 1L = 1L` (because `EmptyBody` returns `0L`, doubled = `0L`, +1 = `1L`).

**Expected result with strict mode:** crashes on `fnThatWillFail`.

**What it stresses:**
- `recoveryPolicy` swap
- Recovery events in the trace
- Substituted values propagate correctly

**Day-4 acceptance** (after recovery is wired up).

---

## Demo 6 — Stachu's chat fragment ("HN headline sentiment")

**Purpose:** the headliner. The thing you'd put in a video/blog.

**Source:**
```darklang
let analyze (): String =
  let url = "https://news.ycombinator.com" in
  let html = fetchUrl url in
  let topHeadline = extractTopHeadline html in
  let sentiment = sentimentScore topHeadline in
  let summary = summarize topHeadline sentiment in
  summary
```

**The pendings:** all of `fetchUrl`, `extractTopHeadline`, `sentimentScore`, `summarize`. None exist in stdlib.

**Capability requirements:**
- `fetchUrl` needs `CapReadNet`. Granted at session start.
- Others are pure.

**Expected result:** something like *"The top HN headline is currently negative; people are complaining about Y Combinator's latest AI bet."* Or whatever the actual headline produces.

**What it stresses:**
- **Everything.** Eager materialization at load, concurrent generation, capability check on `fetchUrl`, mixed pure + impure pendings, trace as the artifact.
- LLM does the heavy lifting — `sentimentScore` is itself basically a wrapper around another LLM call, fun recursion.
- The user sees a "watch the runtime materialize itself" effect — pending names start unfilled, the trace fills them in, finally executes.

**Day-10 acceptance.** This is the demo that'd convince an audience.

---

## Demo 7 (stretch) — Recursive descent of pending

**Purpose:** stress the recursive-pseudocode claim from the chat fragment.

**Source:**
```darklang
let solveAdventOfCode (input: String): Int64 =
  doIt input
```

**The flow:**
- Materialize `doIt`. LLM writes body that calls `parseInput`, `findAnswer`.
- Both are now pending. Materialize them.
- `parseInput` is fine. `findAnswer` calls `pairWise` which calls `delta`.
- All pending. All materialize.
- Eventually executes.

**What it stresses:**
- Recursive materialization
- Depth limits (if we hit infinite descent, we need a circuit-breaker)
- Performance — does it complete in a reasonable time

**Day-12+ acceptance.** Real research territory.

---

## Where to put these

Suggestion: `backend/dark-packages/pdd_demos/` (or wherever .dark files live in the new world). Each demo as a separate `.dark` file with comments at the top:

```darklang
// Demo 1: addOne — proves the pipeline
// Acceptance: ./scripts/run-cli pdd run pdd_demos/01-add-one.dark returns 6L

let myFn (x: Int64): Int64 = addOne x

myFn 5L
```

A test harness file `backend/tests/Tests/PDD.Demos.Tests.fs` runs each demo against the LibPDD-equipped CLI and asserts the expected output.

## What I'd actually do first

Don't start with Demo 1 verbatim. Start with **the F# test directly** that constructs a `FQFnName.Pending` and confirms the runtime returns the materialized result. That's per `10-day-1-hacking-plan.md` Phase F. It's tighter and avoids parsing complexities. The `.dark` file demos come later, once the parser story is settled.

So the order is:
1. F# unit test (Phase F of Day 1)
2. Hardcoded Pending in F# wrapping a real LLM call (Day 2-3)
3. Demo 1 (.dark) — Day 3+
4. Demo 4 (mixed) — Day 3+
5. Demo 5 (tolerance) — Day 4
6. Demo 2 (stock) — Day 5
7. Demo 3 (fib) — Day 6
8. Demo 6 (HN) — Day 10
9. Demo 7 (recursive) — Day 12+

## Telling the story

If/when you make a video or blog about the spike, the demo order matters. Suggested narrative arc:

1. **The trivial demo** (Demo 1) — "Look, the runtime made up a function."
2. **The pipeline** (Demo 2) — "Now it materializes a whole program."
3. **The trace** — show what just happened. Materializations, find-vs-generate wins, latencies.
4. **The recovery** (Demo 5) — "Watch it not crash."
5. **The headliner** (Demo 6) — "And now: from one sentence to a working program."

End on Demo 6. That's the elevator pitch.

## Connection to other docs

- `01-vision.md` — Demo 6 *is* the vision.
- `02-libexecution-changes.md` — Demos 1, 3, 4 directly exercise the new interpreter paths.
- `03-find-vs-generate.md` — Demo 2 stresses the race.
- `04-signature-consensus.md` — Demo 2's pipeline forces sig coordination.
- `05-tolerant-runtime.md` — Demo 5 is the validation.
- `06-builtin-permissions.md` — Demo 6 exercises a capability check on the HTTP call.
- `08-tracing-as-artifact.md` — every demo writes a trace; reviewing them is the UX.

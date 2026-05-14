# PDD Spike — Learnings

*The wrap-up. What this spike actually taught us about pseudocode-driven
development as a runtime feature in Darklang. Written after 131 commits
on the `pdd` branch — local-only, never pushed.*

## TL;DR

PDD works. The "runtime materializes its own source code on demand"
loop is real infrastructure, not a slide. The architecture that
crystallized — `Pending → PackageID → Package(hash)` as a three-state
lifecycle — is sound and threads cleanly through the existing PT/RT
type system. The "ID while WIP, hash at commit" framing turns out to
match Dark's content-addressed package store like it was always
waiting for a working-copy regime.

The honest delimiter on what works: arithmetic + small recursive +
single-step string/list ops work end-to-end fresh from any LLM call;
HTML page rendering works with a refine loop; **multi-step typed
pipelines with complex stdlib semantics still trip on LibParser's
syntax constraints and the LLM's lack of Darklang-specific muscle
memory.**

The biggest unblocker tonight: **classify fns as Verifiable vs
Creative** and give creative fns a different feedback loop (no QA
tests; thin-body retry; refine-until-rich). Independent QA test
generation works great for `factorial(5)=120`-shaped functions and is
actively destructive for `renderHomePage(content): String` ones. One
gate doesn't fit both.

## What worked

### 1. The wire was the right first proof-point

Day-1: just `LLM body → hardcoded identity fn → execute`. The point
wasn't a working `doubleIt` — it was proving the runtime can call
code it didn't have, materialize it via the LLM, and execute
something. Once that compiled and ran, every subsequent piece
plugged into a known shape.

If we had started by trying to make `factorial` work, we'd have spent
the first three days on lowering and never proven the wire.

### 2. `OnMissing.AllowPending` was a 20-line unlock

A single new name-resolution policy in `LibParser/NameResolver.fs` —
unresolved names become `PT.FQFnName.Pending` instead of NotFound —
turned every existing Dark expression into a PDD candidate. No new
syntax. No parser fork. The same source compiles in "strict" mode
(today's behavior) or "PDD" mode (Pendings allowed).

This is the kind of change that should live in mainline. It costs
nothing when unused (the policy default is unchanged) and unlocks a
whole class of runtime behaviors.

### 3. Routing LLM bodies through LibParser (not a regex mini-parser)

The first week's mini-parser handled `42L`, `x + 1L`, identity, then
if-else, then unary minus, then string concat, then... each new
demo class needed a new regex case. Treadmill.

The pivot — feed the LLM body through `LibParser.Parser.parsePTExpr`
with `AllowPending` — instantly unlocked the entire Dark language.
Lambdas, let-chains, recursion, list ops: all just worked through
the existing parser. The mini-parser stays as a fallback for trivially-
shaped bodies (faster path, no LibParser invocation per materialize).

The lesson: **don't write a second parser. Reuse the real one.**

### 4. Canonicalizing Pending handles by name

PT2RT's `fqPending` minted a fresh `Guid` for every reference, so
two `factorial` references in one body had different RT handles. The
runtime re-materialized each one. Recursive fns exploded LLM cost.

Fix: after PT2RT, walk the RT.Instructions and rewrite all
`FQFnName.Pending` handles so same-name → same-handle. One-liner
helper. Recursion now works at zero extra cost.

### 5. The hot-reload hook

The single most demo-worthy piece: refine in one terminal → running
server picks up the new body on the next request. No restart. The
mechanism is just an mtime check on `promoted.jsonl` from inside the
interpreter's Pending Apply path, with the materializer installing
the actual reload function via a mutable hook in RuntimeTypes.

This is what made "pages improve while the server runs" go from
*possible* to *visible*.

### 6. Verifiable vs Creative classification

This was the biggest "duh-but-non-obvious" moment. The independent
QA test gate (a second LLM call generates tests from the fn name +
sig, not the body, then we verify) is **perfect for math** and
**destructive for creative fns**. The QA-LLM hallucinates expected
strings; the body's perfectly-valid HTML doesn't match; we mark Fake
and fall back to identity.

The split:
- **Verifiable** (arithmetic, parsers, validators): QA tests → 5-test
  gate → retry-with-feedback if any fail.
- **Creative** (`render*`, `generate*`, `synthesize*`, `format*`...):
  skip QA; do **thin-body retry** instead (if the body is `x` or
  `x ++ "..."` with no HTML structure, retry with a prompt that
  demands semantic HTML structure).

The classifier is a name-prefix heuristic today. Crude but works.
Could be smarter (signature-shape based; user annotation).

### 7. SCM-integrating PackageID

You raised this as a follow-up question after the first wave shipped:
*"While work-in-progress, reference by ID. When ready to commit,
migrate to hash."*

This reframing — PackageID as **working-copy**, Package(hash) as
**committed**, `dark pdd promote` as the **boundary** — collapsed
two-thirds of the design questions. It maps onto Dark's existing
content-addressed package store as a normal staging→commit step.
PackageID isn't a parallel store; it's just where a fn lives before
the human signs off on the hash.

The implementation cost is moderate (~half-day) because the
material is already there: the interpreter has a `pddIDFnCache`,
refine mutates it, promote freezes a hash. Wiring it into the real
`package_fns` table is scoped in REAL-PACKAGE-FNS.md.

### 8. Cost discipline via budgets + caps

- `PDD_BUDGET_MS` wall-clock per `dark pdd run` (default 5 min,
  4h for HTTP servers).
- `llmCallCap = 3` per Pending handle (across all retry paths).
- `PDD_PARALLEL` cap on in-flight materializations (default 3 — keeps
  OpenAI rate-limit comfortable).

Without these, a runaway retry loop (LLM keeps producing the same
unparseable body) could burn the whole budget in minutes. With them,
total spend tonight: **~$0.30 of $10**. Most of that was prompt-
engineering iteration during the design days; live runs are sub-
$0.0001 each thanks to caching.

## What didn't work

### 1. Tests-as-gate for creative fns

Already covered above. Took longer than it should have to recognize
the gate was the wrong tool. Repeatedly retried-on-fail when the
"fail" was a tests-vs-body mismatch with no objective right answer.
The fix (skip QA for creative) is one branch but the *insight* took
hours of refining wasted on the wrong loop.

### 2. The mini-parser treadmill

Spent days widening regex cases (Case 1, 2, 3, 4, 5...) before
realizing the architecturally-right move was LibParser + a callback
hook. Should have hit that earlier. Sign: when you're writing
"Case N+1: handle one more body shape" for the third time, stop and
look for the parser you're avoiding.

### 3. LLM output occasionally lies past the gate

When tests-as-gate was on and the LLM wrote BOTH the body AND the
tests, it could be self-consistent without being correct. Example
during development: `factorial` body was `if x <= 1L then 1L else
x * (x - 1L)` (NOT factorial — just n×(n-1)) with claimed tests
`fact(0)=1, fact(1)=1, fact(5)=20`. All tests passed. fn marked Real.
Wrong by ~5×.

Fix: independent test gen via a second LLM call framed as a QA
reviewer who hasn't seen the body. Worked for `myAbs` and similar.
Still fragile for fns whose name is ambiguous.

### 4. PT2RT lowering throwing on shapes I didn't anticipate

The LLM occasionally produces `let f x = body in rest` (Dark's
top-level fn-def syntax, not legal as an inner expression) or
tuple-destructuring in a lambda. LibParser parses some of these;
PT2RT throws. The retry path catches the throw and asks for a
simpler body, but the LLM doesn't always comply.

Each shape became a prompt-engineering bandaid. Not principled.
A better long-term fix: support tuples in PT and add `let-fun`
desugaring (`let f x = ... in rest` → `let f = fun x -> ... in rest`).

### 5. The `--no-build` rebuild problem

When I edited LibExecution.fs and rebuilt that project alone,
`dotnet run --project src/Cli --no-build` used a stale Cli binary
that was linked against the OLD LibExecution. Wasted ~15 min once
chasing a "bug" that was actually a stale build.

Lesson: always `dotnet build src/Cli/Cli.fsproj` after touching
anything LibExecution-shaped — that rebuilds the whole transitive
graph.

### 6. The "did the daemon settle?" question

Refine-watch settles a fn after 5 successful refines or 2 stuck-in-a-
row. That's reasonable but arbitrary. Many fns hit "stuck" after 2-3
refines (LLM keeps producing the same thing) and the daemon stops
touching them — but the rendered HTML is still rough. Need a richer
"is this good enough?" signal than a token-count heuristic.

Real answer is probably: human acceptance, not auto-stop.

## Surprises

### "Trace is the program" became literal

Going in, I thought "trace-driven dev" meant *observability* over a
running program. What actually emerged is **the trace IS the
authoritative artifact**: source files are seeds; the materialized
versions in promoted.jsonl + promoted_hashes.jsonl are what runs.
The HTML view's session log + fn registry are the user-facing
expression of the trace.

This is a different mental model than "code is the artifact, traces
are about diagnosing it." Worth surfacing in design docs.

### LLMs are surprisingly good at Dark prefix syntax once told

The LLM gets `f x y` (not `f(x,y)`) once. It gets `let x = y in z`
once. It gets non-curried lambdas once. The "Dark dialect" prompt
addendum is ~30 lines and after that, gpt-4o composes valid Dark
for arithmetic, string ops, list ops, recursion, if-else. The
remaining failure modes are mostly **stdlib name ambiguity**
(`Stdlib.List.fold_left` vs `Stdlib.List.fold`) and **types it
hasn't been told about** (tuples).

So a tight Dark-style-guide as an LLM prompt addendum could mostly
solve the "LLM doesn't know Darklang" problem without fine-tuning.

### How much faster gpt-4o is than gpt-4o-mini for picky syntax

Mini works for arithmetic and simple bodies. For lists + lambdas it
produces curried `fun acc -> elem -> ...` and parens-style fn
application despite the prompt. gpt-4o gets these right first try.
The cost difference (~10×) is irrelevant at $0.30 total spend.

`PDD_MODEL=gpt-4o` for anything beyond toy Int64 ops.

### The refine loop has real teeth

Watching `renderHome` go from `x ++ " - Home"` (377 chars) through
`<html><head><title>Home</title>...` (584 chars) through `<header>
<nav>... <main><section id="hero">...` (1481 chars) over three
refine cycles, with each version verified before kept — that's the
demo. The "fns iterate over time" idea isn't a hand-wave; it's a
~30-second loop you can watch in the HTML view.

### Hot-reload felt magical

`dark pdd refine renderHome` in one terminal; `curl /` in another;
HTML changes mid-session. The mtime polling is ugly engineering
(why not a proper file watcher?) but the effect is exactly the
"live program editing" pitch.

## What we're still missing

| Gap | Impact | Effort |
|---|---|---|
| Tuples / pattern destructuring in PT+LibParser | LLM produces these naturally for max-by, key-value pairs, etc. Today: parser declines. | Medium (parser work) |
| `let f x = … in rest` desugaring | Same as above. LLM uses this idiom for local helpers. | Small (a WT2PT rewrite) |
| Real `package_fns` integration on promote | Promoted fns don't show in `dark search` / `dark tree`. | Half-day (scoped in REAL-PACKAGE-FNS.md) |
| Per-session lazy materialization | Today's HTTP demo pre-fetches all 32 routes at server start. Should defer to first hit. | Small |
| `dark pdd diff <a> <b>` with arbitrary rev/hash refs | Today only diffs latest-2-revs. | Small |
| Browser-renderable `dark pdd trace` viewer | The JSONL log exists but no UI. | Medium |
| Capability gates | Materialized fns can call any builtin. No `--allow-http` style restriction. | Medium |
| Sig consensus (multi-model agreement) | Single-LLM materialization. Two LLMs racing would surface ambiguity earlier. | Medium |
| Recovery policy beyond raise-FnNotFound | When materialize fails after retries, we raise. Could `EmptyBody`-fall-back or human-prompt. | Small |

The first two would significantly unblock LLM-natural body shapes.
The third is the SCM integration scope doc. The rest are individually
small.

## Numbers

- **131 commits** on `pdd` branch (never pushed)
- **20 iters** during the overnight loop
- **~$0.30** of $10 budget spent total
- **57/57** PDD unit tests green throughout
- **74 working revs** + **29 committed snapshots** in the demo state
- **32 routes** on the darklang.com port, all serving rich HTML
- **15+ match sites** threaded through with PackageID variant
- **~600 LoC** in `PDDMaterializer.fs`; ~150 LoC of new code in `PddCommand.fs`; ~50 LoC scattered through Interpreter/RuntimeTypes/HTMLView; rest is doc/tests

## See also (in this directory)

- `WRAP-UP.md` — closure doc + prioritized roadmap (start here)
- `INTEGRATION-PLAN.md` — 3-wave plan to merge onto `main`
- `BIG-PICTURE.md` — horizon scan: strategies, recursion, Dark strengths
- `F-SHARP-TO-DARK.md` — self-hosting roadmap (F# → Dark)
- `REAL-PACKAGE-FNS.md` — what real promote integration needs (Wave 3)
- `PDD-CLI-REFERENCE.md` — every command
- `archive/` — historical session reports + the SCM-INTEGRATION sketch
  (the architectural pivot is captured live in the code + Decision 2
  of INTEGRATION-PLAN)

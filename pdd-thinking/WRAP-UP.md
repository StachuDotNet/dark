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

## Claims

See `CLAIMS.md` (reframed since the spike).

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

Land the materializer + viewer behind a flag. Default Dark doesn't
load them.

- Drop the regex mini-parser (LibParser is the primary body parser
  now; ~330 LoC dies)
- Route cache state through a proper persistence layer (no JSONL
  sidecar — per feedback, all in SQLite/UserDB)
- Move prompts out of F# strings into a first-class form (per
  feedback, prompts want to become a pinned Dark type — see
  `FRONTIER.md`)
- LLM-stub harness for CI integration tests

CLI surface to be reconsidered per feedback — fewer commands, more
interactive. The spike-era `dark pdd run/demo/cache/refine/promote/
history/diff/revert/status` is too much.

### Wave 3 — SCM integration

Wire the commit step (PackageID → Package(hash)) to the real
`package_functions` SQLite table. Smallest wave, hardest by
requiring Decisions 2 and 3 above to be locked first. Should
arrive *as part of* normal SCM ops rather than as a separate PDD
command.

**Risk:** dev DB contamination. Want a "PDD scratch branch"
pattern or a surgical-undelete for individual hashes.

### What does NOT merge

- Inline CSS in PDDHTMLView (view should be served by Dark — see
  `FRONTIER.md`)
- 32-route darklang.com demo router + the Python script (stays
  in `pdd-thinking/scripts/`)
- Hardcoded JSONL paths (replaced by proper persistence)
- Direct OpenAI HTTP call in F# (routes through a Dark-level
  `Stdlib.LLM` shim — see `FRONTIER.md`)

### Order of operations

```
Decisions → Wave 1 → Wave 2 → Wave 3
(1-2 days)  (1 wk)   (1-2 wks) (1-2 wks)
```

Full integration realistic in 3-4 focused weeks, or quarter-paced
if not on the critical path.

---

## Beyond the integration plan

The big-picture roadmap (what we haven't thought about, recursive-
nature improvements, how Dark's strengths should be used, the path
to a Dark-hosted materializer) lives in `FRONTIER.md`. That doc
supersedes the spike-era "F# → Dark migration phases" thinking.

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

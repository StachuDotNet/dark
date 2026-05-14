# PDD Spike — Wrap-Up & Prioritized Roadmap

*The "we're done with the spike" closure document. Reads the four
reflection docs as one and tells you what to do next at each time
budget.*

## What this document is

The PDD spike ran for 131 commits on the `pdd` branch (local, never
pushed). It answered the core question: **can the runtime materialize
its own source code on demand?** Yes. End-to-end. Across creative
HTML rendering, recursive math, and CSV parsing. With a working
HTML view, hot-reload, refine loop, and SCM-style promote.

By the end of the spike we had a working artifact AND a set of
reflections about what to do with it. Four docs to read in order:

1. **[SPIKE-LEARNINGS.md](SPIKE-LEARNINGS.md)** — what worked, what
   didn't, surprises, what we're still missing
2. **[INTEGRATION-PLAN.md](INTEGRATION-PLAN.md)** — 3 waves to merge
   the spike's good parts onto `main`
3. **[BIG-PICTURE.md](BIG-PICTURE.md)** — what we haven't thought
   about; the recursive nature; how to use Dark's strengths
4. **[F-SHARP-TO-DARK.md](F-SHARP-TO-DARK.md)** — 6-phase self-
   hosting roadmap (F# → Dark)

This doc sits on top of those four. Read those for depth; read this
for orientation + the prioritized next-step list.

## The story in one paragraph

We started with the bet: **Pending fns + LLM materializer + Dark
runtime + caching + a feedback loop = code that writes itself.** We
built the type-system substrate (15+ match sites threaded through PT
+ RT + binary serializer + interpreter), the runtime (cache, hooks,
hot-reload), the LLM machinery (prompt builder, response parser, QA
test gate, fix-up retries, refine loop), and the SCM-style commit
step (`pdd promote`). It works. The artifact runs. We then took two
days off the keyboard to reflect: **what did we actually learn? How
do we land it on `main`? Where could this go in a year?**

The answers were unexpectedly rich. The merge plan is bounded
(3 waves, ~3-4 weeks). The horizon plan is vast (PDD as build tool,
PDD-on-PDD recursion, type-level Pending, multi-LLM consensus,
branches + bisect + blame). The F# code is on a path to becoming a
~250-LoC substrate with a ~2500-LoC Dark library on top.

The spike answered "can it work?" We have four docs of new questions
that are more interesting.

## Where we landed

**Code state:**

- ~4,000 LoC F# across PDDMaterializer, PDDHTMLView, PddCommand
- 131 commits, never pushed
- 57/57 PDD unit tests green
- 100+ successful materializations across the demos
- Working 32-route darklang.com clone with `render*Page` Pendings
- HTML view + sessions index + fn registry all live

**Document state (this directory):**

- `README.md` — top-level entry, what's built
- `DESIGN.md` — sectioned design depth
- `EMPIRICAL.md` — LLM-behavior observations
- `DEMOS-AND-BUDGETS.md` — concrete programs + envelopes
- `PDD-CLI-REFERENCE.md` — every CLI command
- `REAL-PACKAGE-FNS.md` — scope sketch for Wave 3
- **Reflection layer (this spike-end pass):**
  - `WRAP-UP.md` (this doc)
  - `SPIKE-LEARNINGS.md`
  - `INTEGRATION-PLAN.md`
  - `BIG-PICTURE.md`
  - `F-SHARP-TO-DARK.md`
- `archive/` — historical session reports, the original SCM-INTEGRATION
  sketch, and earlier iter-by-iter notes

## Prioritized TODOs — pick by time budget

What to do next, indexed by how much time you have.

### If you have 1 hour

**Stop. The spike is done. Read this doc + SPIKE-LEARNINGS.md.**

That's it. Don't write code. The right move is to internalize the
learnings before any new code lands.

### If you have 1 day

**Lock cross-cutting decisions (INTEGRATION-PLAN §"Cross-cutting
decisions to lock"):**

1. **Naming.** Is the new variant `FQFnName.PackageID` or
   `FQFnName.Working`? Pick one; document why. (My read:
   `Working` reads cleaner in match arms but `PackageID` is more
   honest about the mechanism. Probably `PackageID`.)
2. **PackageID-on-promote forwarding semantics.** Does promote
   leave the PackageID alive (option a, git-working-copy) or
   redirect to the hash (option b, git-branch)? (My read: option
   a — more honest, keeps the runtime simple.)
3. **Namespace for promoted PDD fns.** `Stdlib.PDD.X`?
   `User.PDD.X`? (My read: `Stdlib.PDD.X` if the cache is shared
   across users; `User.PDD.X` if user-local.)
4. **`AllowPending` lexical scope.** Variables not Pending (only
   fn-names go Pending). (My read: confirmed; document.)

Output: a one-page decisions doc that Wave 1 PR can reference.

### If you have 1 week

**Wave 1 PR (INTEGRATION-PLAN.md).** The bedrock. ~400 LoC of
type-system + parser scaffolding. PT/RT `PackageID` + `Pending`
variants. Match-arm threading. `OnMissing.AllowPending` policy.
PT2RT.Expr lowering for the new variants.

Self-contained, mechanical, no behavior change unless invoked.
Reviewable by a competent reviewer in 1-2 sittings. Tests: existing
57 PDD tests + smoke "AllowPending parses pending; default doesn't."

Doesn't unlock anything user-visible yet. But it's a strict
prerequisite for everything else.

### If you have 1 month

**Land Wave 1 + Wave 2 + (start) Wave 3** (INTEGRATION-PLAN.md).

- Wave 1: bedrock (week 1)
- Wave 2: opt-in materializer module + CLI shim (weeks 2-3)
  - Drop the regex mini-parser (we have LibParser)
  - Route JSONL through LibConfig (configurable paths)
  - Move prompts to EmbeddedResources
  - LLM-stub harness for CI tests
- Wave 3 (start): real `package_functions` SQLite integration. May
  ship by end of month, may slip — depends on resolving the
  PackageID-on-promote question with conviction.

At the end of this month: PDD merged on main, opt-in via env flag,
no surprises, three reviewed PRs.

### If you have 3 months

**Full INTEGRATION-PLAN + Phases 0-2 of F-SHARP-TO-DARK.**

- All three waves merged + soaked for a few weeks
- `Stdlib.LLM.complete` lives (Phase 1 — F# delegates HTTP to Dark)
- All prompts move to Dark (Phase 2 — users can override prompts
  without rebuilding F#)
- Optionally: response parsing + body scoring move to Dark (Phase 3)

User-visible: prompt updates ship as Dark package updates. The
materializer's behavior is partly user-modifiable.

### If you have 6 months

**INTEGRATION-PLAN + F-SHARP-TO-DARK Phases 0-4.**

- Orchestrator is now Dark code (`Stdlib.PDD.materializeOne`)
- F# substrate ~300 LoC: registry + hooks + interpreter dispatch
- The materializer is reading-friendly Dark code that users can
  fork and refine

**This is the unlock point for PDD-on-PDD recursion.**

Once `Stdlib.PDD.materializeOne` is a Dark fn, a user can
`dark pdd refine Stdlib.PDD.materializeOne` and the materializer
materializes its own next version. The fixed point matters here:
materializer-improving-materializer is *the* recursive vision.

### If you have a year

**Full F-SHARP-TO-DARK + select BIG-PICTURE items.**

By priority (my guess):

1. **Materializer-as-`List<Strategy>`** (BIG-PICTURE 1.1). Each
   strategy is a Dark fn. Cache → corpus → human → LLM. Cheap fast
   path before any LLM call. Maybe 60% of materializations hit
   cache or corpus and never burn a token.
2. **PDD-on-PDD recursion infrastructure** (BIG-PICTURE 2.4). A
   `dark pdd refine` of the materializer itself. Once Phase 4
   lands, this is doable in days.
3. **Branches + bisect + blame** (BIG-PICTURE 2.1, 2.2, 2.3). The
   SCM analogues we missed. The mechanism is simple (per-branch
   manifests, binary search on history); the UX is the value.
4. **PDD as build artifact** (BIG-PICTURE 1.5). `dark pdd build`
   walks the source, materializes every Pending, writes committed
   snapshots. Production runs a no-materializer Dark binary. This
   is the production story.
5. **Test-as-spec generative loop** (BIG-PICTURE 2.5). LLM
   generates more tests; user curates; bodies converge. Reverses
   the QA-test-gate from "lock body to fixed tests" to "tests
   themselves are part of the iteration."
6. **`Pending` for types** (BIG-PICTURE 1.2). The hardest, biggest
   win. Materializer infers a type from usage sites.

Each of these is 1-3 weeks of focused work after the foundation
exists. At a year out, we'd have a story that *most* of the
materializer is Dark code, the architecture supports multiple
strategies, the SCM-style tooling actually matches git's depth, and
there's an emerging "Stdlib.PDD" package ecosystem.

## What's *not* on the roadmap (and why)

A few features the spike could grow but probably shouldn't:

- **Multi-user PDD cloud cache.** Tempting (network-effect
  dedup); messy (auth, billing, trust). Defer until there's
  proven single-user value.
- **PDD-LSP IDE integration.** Big surface area. Wait for the
  Dark LSP to land in its own right.
- **Speculative materialization on keypress** (BIG-PICTURE 1.8).
  Cool but wasteful in tokens. Experiment, don't productize.
- **Fine-tuned per-user models** (BIG-PICTURE 1.6). Real value, but
  *not* a Darklang-team capability. Partner with an inference vendor
  for this.
- **Mid-program fn iteration** (results-don't-satisfy → re-derive).
  Important but the design space is murky. Needs its own spike.

## The "what we'd do differently" list

If we ran this spike again from scratch, knowing what we know now:

1. **Start with `FQFnName.PackageID`, not `Pending`.** The
   PackageID concept (working-copy fn before commit) is the harder
   one and was added late. Building Pending without PackageID meant
   Pending was conflated with "no fn at all" for a long time.
2. **LibParser from day one, not as a follow-up.** The regex
   mini-parser cases ate ~330 LoC and were never the right call.
3. **HTML view should have used `<details>` from day one.** Long
   expressions exposed an ugly view; we patched it at iter 12+.
4. **Decide PackageID-forwarding semantics on paper before coding
   promote.** We have working promote that hasn't committed to a
   forwarding model. That decision will be load-bearing for years.
5. **Build the test-stub framework early.** The reason CI doesn't
   exercise materialization today is that there's no LLM stub. With
   one, we could've had ~30 more integration tests.
6. **Don't reinvent `pdd promoted_hashes.jsonl`.** Real
   `package_functions` was always the destination; the JSONL
   sidecar bought velocity but locked in a path we now have to
   migrate from.

## Open meta-question: when does the spike merge?

The spike branch has 131 commits and is never pushed. Two options:

**Option A — merge as one big PR.** Pro: closure. Con: 4,000+ LoC of
review with two ongoing parallel rewrites (Wave 1 → 2 → 3 in the
INTEGRATION-PLAN map cleanly onto coherent slices, but a single PR
ignores them). Reviewers will glaze.

**Option B — three sequential PRs per INTEGRATION-PLAN.** Pro:
reviewable; landable; spike branch stays as a *reference* but the
mainline diff doesn't carry the spike's experimental edges. Con: 3x
the PR overhead, more days of context-switching.

**Recommendation: Option B.** The integration plan was written
precisely to make this tractable. The spike branch becomes a
historical artifact — like a research paper — and the mainline
absorbs only the parts that survived reflection.

## Bottom line

The spike is **done**. Not "abandoned" — done. We built the thing,
we proved it works, we have a defensible merge plan, we have a
direction for the year ahead.

The next move is **not** "more spike work." The next move is:

1. Stop hacking on `pdd` branch.
2. Lock the cross-cutting decisions (a day's work).
3. Open Wave 1 PR (a week's work).
4. Re-read the four reflection docs in 3 weeks before opening Wave 2.

Three weeks from now, PDD is opt-in on mainline. Three months from
now, the materializer's prompts are user-customizable. Six months
from now, the materializer is Dark code and PDD-on-PDD is real.

That's the shape. The spike was the first 10% (proof). The
integration plan is the next 30% (foundation). The self-hosting
roadmap is the next 40% (Dark-native). The big-picture roadmap is
the last 20% (the recursive vision).

Eat the spike. The pieces worth keeping are in the merge plan; the
pieces worth dropping are tagged for `pdd-thinking/`'s archive; the
horizon is mapped. Close the loop.

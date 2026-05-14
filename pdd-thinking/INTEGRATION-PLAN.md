# PDD → Mainline Integration Plan

*How to land the spike's good parts on `main` without dragging the
spike's experimental edges. Companion to SPIKE-LEARNINGS.md.*

## Framing

131 commits is a lot to merge as-is. Some of it is **bedrock** (the
type system additions, the parser policy), some is **opt-in**
(materializer, refine, promote), some is **spike-only** (the regex
mini-parser, the bespoke HTML view CSS, the JSONL sidecars).

The merge isn't "rebase pdd onto main and hope." It's a deliberate
re-landing in three waves, each independently mergeable, each
behind a feature flag at the call-site if needed.

## Wave 1 — Bedrock (merge without behavior change)

Land the type-system + parser scaffolding. The new variants exist;
nothing in mainline references them; all match sites have arms that
no-op or raise sensible errors. Strict mode default — `AllowPending`
policy is just a new variant of `OnMissing` that nothing uses by
default.

What goes:

- `PT.FQFnName.Pending { name }` + `RT.FQFnName.Pending { handle; name }`
- `PT.FQFnName.PackageID { name }` + `RT.FQFnName.PackageID { id; name }`
- `RT.pddIDRegistry` (name → Guid stable map)
- `RT.pddIDFnCache` on ExecutionState (Guid → PackageFn)
- `RT.pddRefreshHook` (mutable, default no-op)
- All match-exhaustiveness arms in: Interpreter, RTQueryCompiler,
  RT/PT→DarkTypes, Binary serializer (RT tags 2/3, PT tags 2/3,
  Canonical tags 3/4), LibDB/PackageItem, LibDB/Tracing,
  Execution.fs prettyName.
- `NameResolver.OnMissing.AllowPending`
- WT2PT threading of `onMissing` through EApply, EFnName, pipe-stage,
  and EVariable fallback chains.
- PT2RT.Expr lowering for the new variants.

Why first: it's pure scaffolding. Everything compiles. No behavior
changes unless a caller opts in via `OnMissing.AllowPending`.
Reverting is trivial. Reviewing is straightforward — match-arm work
is mechanical.

**Risk:** breaks binary compat on Canonical hashing? No — we add new
tag bytes (3 + 4); existing hashes don't change because existing
fn names don't take those tag paths.

**Test coverage to add before merge:** none beyond the existing 57
PDD unit tests. The bedrock has no behavior unless invoked.

**Estimated diff size:** ~400 LoC across 13 files. Big-but-mechanical.

## Wave 2 — The runtime materialization machinery (opt-in module)

Land `LibExecution.PDDMaterializer` as a real module + the CLI
surface to drive it. Hide it behind a flag so the default Dark CLI
doesn't load it.

What goes:

- `LibExecution/PDDMaterializer.fs` — the materializer + body parser
  hook + test runner hook + refine + promote logic. ~1900 LoC.
- `LibExecution/PDDHTMLView.fs` — the HTML view + session sidecar
  + index + fns registry. ~520 LoC.
- `Cli/PddCommand.fs` — `dark pdd ...` subcommand dispatcher. ~900 LoC.
- A flag (env var, like today's `PDD_ENABLED=true`, or a build flag)
  gating CLI registration of `pdd` subcommands.

What gets dropped / replaced:

- **The regex mini-parser cases** (Case 1a through 5). LibParser is
  the primary path; the mini-parser is a 30-line fallback for
  `42L`/`x`/`x + 1L`-shapes. Anything more goes through LibParser.
- The `hardcodedIdentityFn` / `hardcodedIdentityFnArity` stubs — keep
  them but rename to `pddIdentityFallback` so the name signals
  they're a last-resort and not a default behavior.

What changes shape:

- **JSONL sidecars become opt-in or move into a config-driven path.**
  Today's `rundir/pdd-cache/*.jsonl` is hardcoded. For mainline, route
  through `LibConfig` so the path is configurable + the format is
  versioned (a `version: 1` field on every line).
- **The materializer's prompt strings** become resources (load from
  `EmbeddedResources` like the seed DB), so updates don't require an
  F# rebuild.

Why second: this is the meaty part. Self-contained — the type-system
work (Wave 1) is already in place. Can land independently and lay
dormant until a user runs `dark pdd ...`.

**Risk:** the materializer pulls in `System.Net.Http`. Already in
mainline (HttpClient is a builtin). No new dependency.

**Risk:** an LLM-API key on disk. The current pattern
(`~/.config/darklang/llm-keys.env`) is fine but should be documented
in CONTRIBUTING.

**Test coverage to add:** Wave 2 needs ~10 integration tests
exercising the materialize-end-to-end path. Stub the LLM (don't
actually call OpenAI in CI) — fake `bodyParser` + `testRunner` to
exercise the lifecycle.

**Estimated effort:** 1-2 days of cleanup before merge.

## Wave 3 — SCM integration (the promote step lands in package_fns)

Land the `dark pdd promote` step writing to the **real**
`package_functions` SQLite table (not the sidecar). See
`REAL-PACKAGE-FNS.md` for the full sketch.

What goes:

- `parseFullSigPT : string -> Option<List<string * PT.TypeReference> * PT.TypeReference>`
  (currently RT-typed; promote needs PT for canonical hashing).
- `ptPackageFnOf : name -> sig_ -> body -> Task<Option<PT.PackageFn.PackageFn>>` —
  invokes the bodyParser, wraps in a PT.PackageFn with `hash = Hash ""`.
- Exposed `applyAddFn` from LibDB/PackageOpPlayback so PDD can call it.
- `applySetName` invocation so the location maps `name → hash`.
- A namespace prefix: PDD-promoted fns live under
  `Stdlib.PDD.<original-name>` (or `User.PDD.<name>`?). Pinned in
  Wave 3 before merge.

What we hold off on:

- **The PackageID-on-promote forwarding question** (REPORT-overnight).
  Whichever answer we pick, we live with for a long time. Best to
  decide explicitly with a one-page design doc + discussion, not
  let it leak in via implementation.

Why third: hardest, smallest. By the time we get here, Wave 1+2
have been on main for a bit; we know what's actually used.

**Risk:** dev DB contamination. Today PDD-promoted bodies aren't in
the canonical DB; with this change they would be. We'd want a
"PDD scratch branch" pattern so experimental promotes don't muddy
main, or a `dark pdd unpromote <hash>` that surgically deletes a
single hash row.

## What's spike-only (does NOT merge)

- The hand-written CSS in PDDHTMLView. The HTML view is useful as a
  demo; merging it as-is would be weird. Either:
  (a) extract the view into a separate dev-only package, or
  (b) replace the inline CSS with a single stylesheet shipped via
      EmbeddedResources.
- The 32-route darklang.com demo expression + Python script that
  generates it. Useful spike artifact; doesn't belong in mainline.
  Stays in `pdd-thinking/scripts/`.
- The JSONL log file paths hardcoded. Wave 2 routes through LibConfig.
- `Mat.callOpenAI` / `Mat.callOpenAIWithMode` directly hitting
  OpenAI's HTTP endpoint. For mainline, this should route through
  `Stdlib.OpenAI` (or a `Stdlib.LLM` namespace) — currently those
  Stdlib fns don't exist but should. See F-SHARP-TO-DARK.md (next).

## Cross-cutting decisions to lock before any wave merges

These are NOT implementation; they're design calls that shape Wave 1's
canonical types. Worth pinning before the first PR.

1. **Naming.** Is it `FQFnName.PackageID`? Or something more evocative —
   `FQFnName.Working` (vs `FQFnName.Committed`)? Or `Mutable` vs `Settled`?
   The current name (`PackageID`) is fine but stresses the *mechanism*
   (it's an ID); the alternatives stress the *role* (it's the working
   state). The latter reads better in match arms.

2. **PackageID forwarding semantics** (REPORT-overnight's open question).
   On promote, does the PackageID:
   (a) stay alive forever (git working-copy semantics — committed hash
       is just an *additional* state)
   (b) become a redirect to the hash (subsequent edits fork to a new
       PackageID — git branch semantics)
   Lock before Wave 3 starts. My read: **(a)** is more honest to how
   git actually behaves and lets the runtime stay simple.

3. **Namespace for promoted fns.** `Stdlib.PDD.X`? `User.PDD.X`?
   `Pdd.X`? Or thread through whatever module the original Pending was
   in (today: no module info)? Lock before Wave 3.

4. **`AllowPending` semantics around lexical scope.** Today,
   `let x = 5L in x + y` with AllowPending — `y` is treated as a free
   variable, not a Pending (because EVariable, not EFnName). Is that
   right? Probably yes (a missing variable in a let-binding scope is
   a bug, not an opportunity to materialize), but worth saying out loud.

## Order of operations

```
Decisions   →   Wave 1 PR    →    Wave 2 PR    →    Wave 3 PR
(1-2 days)      (review-friendly,  (review-light,    (review-careful,
                 mechanical)        opt-in module)    touches dev DB)
                       │                  │                  │
                       │                  │                  ├→ user-visible:
                       │                  │                  │   promoted PDD fns
                       │                  │                  │   appear in
                       │                  │                  │   `dark search`
                       │                  │                  │
                       │                  └→ user-visible:
                       │                      `dark pdd …` commands
                       │                      live; HTTP-server PDD demos
                       │                      possible
                       │
                       └→ user-visible: none. Types exist; nothing uses
                          them by default.
```

Each wave can ship 1-2 weeks apart. The full integration is realistic
in a focused 3-4 week sprint, or trickle-merged over a quarter if it's
not on the critical path.

## What I'd test before declaring Wave 1 ready

- All 57 existing PDD tests pass.
- `dark pdd run "x + 1L"` (where `x` doesn't bind) parses and produces
  a Pending. Without `AllowPending`, same expression errors. Two-mode
  test.
- Binary round-trip for PackageID + Pending variants — write, read,
  field-by-field assert.
- Canonical hash unchanged for an existing builtin/package fn (the new
  tags don't accidentally shift other tag bytes).
- `Stdlib.Json.parse<PT.FQFnName.FQFnName>` and round-trip via Dark
  types.

For Wave 2: add an LLM-stub harness for the materializer end-to-end,
covering: cache hit, fresh materialize, retry-on-thin-body, retry-on-
JSON-fail, recursion-skip, refine flow, promote flow.

For Wave 3: smoke test that promoted fns survive a `dark` restart
(persisted in package_functions, locations point at them, can be
called by name from a fresh expression).

## Bottom line

The PDD spike is **structurally close to merge-ready**. The hard work
was clarifying the architecture (Pending → PackageID → Package(hash))
and threading it through ~15 match sites. The remaining work to ship
is mostly:

1. Decisions (names + forwarding + namespace)
2. Cleanup (mini-parser drop, JSONL → LibConfig, prompts → EmbeddedResources)
3. Tests (especially LLM-stubbed integration)
4. Three sequential PRs

None of that is large. None of that is risky. The spike's experimental
edges (the regex mini-parser, the demo router, the hand-rolled HTML view
CSS) can be left behind in `pdd-thinking/` as the historical record.

The pieces worth keeping, kept. The pieces worth dropping, dropped.
The branch never pushed; the code merges deliberately.

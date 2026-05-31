# Capabilities

LLM-generated and shared code will try to delete files, hit arbitrary endpoints, and
exfiltrate secrets. Capabilities gate that: "this code may do exactly these effects."

> **Integration check — the FULL composed backend suite is green** (`compose-check`, where every
> pre-S&S foundation coexists): **9,416 passed, 4 ignored, 0 failed**. This is the strongest
> validation of the riskiest change here — the cap gate runs on *every* builtin call across all
> 9,416 tests under the permissive default grant with **zero regressions** (pure builtins fast-path
> on `caps={}`; the 628-site `caps` codemod + the interpreter call-site gate are invisible). It also
> confirms all foundations compose without cross-interaction (EventBus + async Stage A + scheduler +
> conflict-dispatch + capabilities, all wired, all green at scale).

> **Prototyped in prework** (`loop-fun:prework/capabilities`). The cap **types + gate** are built
> and tested (`LibExecution.Capabilities`, RT-independent like the bus — compiles before
> RuntimeTypes; **6/6 tests PASS**): `CapCategory` (the 6 domains), the structured `Capabilities`
> grant + `noCaps` strict default, `CapDecision = Allowed | Denied of CapCategory`, the coarse
> `checkCaps granted needs` (first uncovered category wins; empty `needs` = the pure fast-path =
> today's behavior), and the structured `hostAllowed` HttpClient allow-list (exact / `*.suffix` /
> `*`, with the lookalike-suffix `googleapis.com.evil.com` correctly rejected). So **model B (coarse
> static caps + a dynamic structured check) is implementable as specified.**
>
> **The gate→policy wiring is now BUILT + tested too** (`compose-check`, where capabilities,
> conflict-dispatch, and the scheduler coexist). Added `Conflict.CCapabilityDenied of CapCategory`;
> `createState`'s default dispatch fails a denial loudly (strict default = today); and
> `CapabilityGate.gate dispatch ctx granted needs` runs `checkCaps` and, on `Denied`, routes a
> `CCapabilityDenied` through the **conflict-dispatch policy**, returning its `Resolution` (`None` =
> allowed, the call proceeds). **+4 tests (ConflictDispatch 11/11 PASS):** a granted cap proceeds; a
> denied cap **fails loudly** under the default; an installed policy **substitutes**; and a policy
> can **park** the denied cap on a grant event (`RPark` on an `EventSelector`) — so "park waiting for
> a grant" is real, riding the scheduler. **A denied capability is now a genuine, policy-resolved
> runtime conflict, not an invented error.** The **`caps : Set<CapCategory>` field on `BuiltInFn` is now DONE too** (prework). A per-assembly
> codemod (anchored on the `sqlSpec` line, ~628 sites) added it to every builtin:
> Pure/Language/Matter = `Set.empty` (the pure fast-path, ~530 of them), and the effectful
> assemblies their category — Http.Client → `{HttpClient}`, Http.Server → `{HttpServer}`,
> Cli/CliHost → `{CliHost}`, Random → `{Random}`, Time → `{Time}`. **The full backend (all 9
> builtin assemblies + LibExecution + Tests) builds clean**, and a runtime test confirms the caps
> landed (>300 pure; Http.Client carries HttpClient; Cli/CliHost carry CliHost) — Capabilities is
> **13/13**. It mirrors the proven `effects` codemod and is orthogonal to it (they coexist on merge).
> So static caps are real on every builtin.
>
> **The call-site wire-up is now DONE too — capabilities is end-to-end** (prework, `compose-check`).
> `ExecutionState` gained `grantedCaps` (default `Capabilities.allCaps` = permissive, so wiring the
> gate in changes nothing); at the interpreter's builtin-call site (`Interpreter.fs`, just before
> `fn.fn`) it runs `CapabilityGate.gate exeState.grantedCaps fn.caps`, and the **gate produces the result**:
> `None` → `return! fn.fn` (the real call); **`RSubstitute dval` → `return dval`** (the builtin is
> bypassed and the policy's default value flows through the normal type-check + register store);
> `RFailLoudly` → raise; **`RPark selector` → `do! Scheduler.awaitSelector exeState.buses selector`
> then `return! fn.fn`** (the call *suspends* on the grant event — a parked builtin call is a
> `waitForOne` subscriber, the same primitive as any frame — and runs once granted). **No regression:
> 88/88 Interpreter tests pass** under the permissive default (the gate runs on every builtin call
> and always allows; pure builtins fast-path on `caps={}`). And **three end-to-end tests** prove all three
> resolutions bite at the real call site: (1) `dateTimeNow` (`caps={Time}`) **fails "capability
> denied"** when Time is denied; (2) a **Substitute** policy makes `timeNowMs` return `-1` (a value
> the real monotonic clock never yields), proving the builtin was **bypassed**; (3) a **Park**
> policy makes `timeNowMs` **suspend** (its task stays pending) until a grant event is published on
> the conflict bus, then **resume** and return a real timestamp. **So a denied capability now
> actually stops, substitutes for, or suspends a real builtin call, resolved by the conflict
> policy.** The whole capabilities chain (per-builtin caps → gate → conflict policy →
> **fail/substitute/park**) is built and tested end-to-end. The `effectiveCaps` adapter over real fns is **now built too — capabilities is COMPLETE end-to-end.**
> **Grounding win:** a named fn is loaded as a `DApplicable (AppNamedFn …)` *before* its `Apply`, so
> finding a fn's calls is a **flat scan of `LoadVal` instructions** — not the 30-arm PT tree walk
> mirroring `DependencyExtractor` — and the flat scan **captures builtins** (which
> `DependencyExtractor` discards). `CapabilityGate.callTargetsOf` maps each named-fn ref to
> `CallsBuiltin(its caps)` or `CallsFn(hash)`; `effectiveCapsOfPackageFn` feeds those to
> `Capabilities.effectiveCaps` over an `instrsOf` resolver. **+2 tests (Capabilities 15/15):**
> `callTargetsOf` finds builtin + package refs (ignoring literals), and `effectiveCapsOfPackageFn`
> unions `Time` (direct) + `Random` (via a package-fn edge) across the **real** instruction call
> graph. So the whole capabilities PR is implemented: types + coarse gate + structured host check;
> per-builtin static caps on all 628 builtins; the interpreter call-site gate with all three
> resolutions (fail/substitute/park) wired and tested; the `effectiveCaps` projection + the real
> RT-instruction adapter; and the conflict-dispatch integration. (Future refinements, not blockers:
> recursing into lambda-body instruction streams in `callTargetsOf`; Park re-gating against a
> mutable, updated grant rather than "grant event ⇒ run".)

**Builtins are the only impure boundary** (pure Dark code can only compute), and `main`
already splits builtins into 9 effect assemblies (`Pure`, `Http.Client`, `Http.Server`,
`Random`, `Time`, `Cli`, `CliHost`, `Language`, `Matter`). So gating effects = gating
builtins, and the categories already exist. **Pure code is always free** — no `CapPure`
token, nothing to gate.

## Grants are per-instance settings — NOT ops

A grant ("this instance may write files / call `api.github.com`") is a **per-instance
setting**, not an op and not a projection. It does not sync, it is not in the op stream;
it is local config a user sets on a Dark install. (The *audit of what got used* can ride
the trace, which does sync — but the grant itself is a local setting.) Settings like
these are simple fields, so they live as a serialized Dark value / JSON blob in the
instance's local config, not as SQL rows.

## The Capabilities type — per-category, not a flat enum

Each category has its own shape; several are not yes/no:

```fsharp
type Capabilities =
  { httpClient : HttpClientCap option   // structured: allowed hosts, methods, secret-bearing?
    httpServer : HttpServerCap option   // structured: bind targets/ports (HttpClient-shaped)
    fileSystem : bool                   // yes/no now; future: per-dir, read/write, quota
    cliHost    : bool                   // stdin/args + subprocess spawn; yes/no now,
                                        //   structured later (a spawn allow-list, e.g. print-md)
    random     : bool                   // yes/no
    time       : bool }                 // yes/no
// Language & Matter need no grant token: their reflective surface is free, and
// anything that RUNS code (eval, materialize) PROPAGATES the caller's caps — never
// amplifies. eval cannot launder an effect the caller lacks.

and HttpClientCap = { hosts : HostPattern list; methods : Method list; allowSecrets : bool }
```

## Representation: model B (static category + dynamic check)

`Set<Capability>` on the builtin is wrong — a set-difference can't answer "is
`example.com` in the allowed hosts?", and it forces the interpreter to know every cap
shape. Instead a `BuiltInFn` carries **two** things:

- static **`caps : Set<CapCategory>`** — coarse, arg-independent ("could touch
  HttpClient"). Cheap to read; this is what gets unioned for effective-caps and shown to
  a prompt filter.
- dynamic **`checkCapabilities : granted -> args -> CapDecision`** — run only at the gate,
  with args in hand ("is *this host* allowed?").

The static set is a sound over-approximation (omitting `HttpClient` ⇒ provably never
needs it). A pure builtin has `caps = {}` and no `checkCapabilities`.

`caps` is the resource **domain** axis (for *gating*); it is *orthogonal* to the concurrency
**character** (`Pure`/`AsyncRead`/`Blocking`…) that [async.md](async.md) puts in a separate
`effects` field (for *scheduling*). A builtin declares both; they're not the same metadata.

## The gate

The interpreter doesn't invent errors — at a builtin call it runs the builtin's
`checkCapabilities granted args`; on deny it produces a standard runtime conflict and
the resolution policy decides: **fail loudly** (strict default = today's behavior),
**substitute** a default, or **park** waiting for a grant. **No interactive grants for
now** (they make tests block on a human); the seam is left for an `AskHuman` resolution
that times out to a non-human fallback, parking on the event bus
([event-bus.md](event-bus.md)). Until a builtin declares `caps`, nothing is denied and
the call fast-paths.

## Effective caps for user / materialized fns (a projection)

User-defined and materialized fns don't declare caps; their **effective** set is a
projection over their call graph — union the static `caps` of every builtin reached:

```fsharp
effectiveCaps fn =                       // : Set<CapCategory>
  walkCalls fn.body |> unionOver (function
    | Builtin n  -> (lookupBuiltin n).caps
    | Package h  -> effectiveCaps (lookup h)   // immutable, memoize by hash
    | _          -> empty)
```

A user may *tighten* below the computed set (defense in depth) but never loosen it.

> **Built + tested (prework).** `Capabilities.effectiveCaps` is a **generic call-graph fold** —
> `CallTarget = CallsBuiltin of Set<CapCategory> | CallsFn of 'fn` — unioning the static caps of
> every builtin reachable from a fn, with a **visited set** so mutual recursion terminates and each
> fn is unioned once (the "memoize by hash" the sketch notes, here as dedup). `tightenedCaps =
> Set.intersect` enforces tighten-but-never-loosen. It's **abstracted over the call graph** (the
> `calls` adapter), so it's testable without the interpreter — the RT instantiation (walking real
> `Instructions` for builtin/package calls) is a thin adapter the call site supplies. **+6 tests
> (Capabilities 12/12 PASS):** leaf union, multi-builtin union, transitive inheritance, pure ⇒
> empty, mutual-recursion termination, and declaring an unreachable cap being dropped (can't
> loosen). So the projection is implementable as specified; only the `Instructions`-walking adapter
> is left, and it shares the call-graph walk the existing `Propagation`/dependency-extraction code
> already does.

## CLI UX — set allowances, see failures

Grants are set and inspected from the CLI (the typed `Capabilities` value is the model;
these commands are its surface):

```text
$ print-md report.md
✗ blocked: HttpClient → fonts.googleapis.com (GET)
    print-md needs: FileSystem(read), Cli(spawn:weasyprint), HttpClient(GET fonts.*)
    grant with:  dark caps grant print-md --http-get 'fonts.googleapis.com' --fs read --spawn weasyprint

$ dark caps grant print-md --http-get 'fonts.googleapis.com' --fs read --spawn weasyprint
✓ granted (instance setting, local; not synced)

$ dark caps print-md
print-md  fs:read  spawn:weasyprint  http:GET fonts.googleapis.com

$ print-md report.md
✓ printed report.pdf
```

A denial is actionable: it names exactly what was needed and the one command to grant
it. `dark caps <app>` shows the current grant; `dark caps revoke …` removes it.

> **The actionable-denial surface is real, tested Dark (prework, `capabilities-cli.dark` 3/3).**
> `capDenialMessage app category` (`print-md needs Cli(spawn) — not granted`), `grantCommand app
> allowList` (`dark caps grant print-md --spawn pandoc,weasyprint,lp`), and `denialWithRemedy`
> (the denial + the one command to fix it). So the capability loop is now real **end to end**:
> runtime-denial (the F# `CCapabilityDenied` → policy gate built earlier) → user-facing remedy
> (this Dark CLI surface). The structured multi-category grant line (`--http-get … --fs read …`) is
> a later elaboration; the spawn case (what print-md needs) is the proven core.

## Discovered

- The denial→resolution policy is the same "conflict dispatch" sync and runtime errors
  use. That shared dispatch may belong **below** S&S (a pre-S&S substrate primitive) so
  pre-S&S docs can reference it without an up-link — revisit bucket placement.

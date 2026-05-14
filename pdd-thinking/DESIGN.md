# PDD Design

> **STATUS:** Written mid-spike. Sections describing design *intent*
> are still accurate; sections describing *implementation status*
> (especially §8 "promote/diff/replay are deferred" and §9 "not yet
> wired to a CLI command") are out of date — `promote`, `diff`,
> `refine`, `revert`, `history`, `status` shipped. For current state,
> see `WRAP-UP.md` and the live capability table in `README.md`.

Consolidated design notes. Sections roughly correspond to the original numbered docs (in `archive/` if you want trail-of-thought).

---

## §1 Vision

The interpreter materializes its own source code on demand, in parallel, speculatively, with the LLM as both author and search index.

Three claims that make this a paradigm, not a feature:

**Source is lazy.** Traditional dev: source is the slowest-moving artifact. PDD: source is computed at runtime speed by the runtime itself. The artifact you ship is a *sketch + a cache*.

**Trace is the program.** If source is materialized on demand, what *ran* is more durable than what's written. The trace — input, every call, every body materialized, every find-vs-generate result — is the canonical artifact. SCM tracks traces; reviews diff traces; distribution ships sketch + cache.

**Types are the coordination protocol.** When parallel materializations race on the same name, they need to coordinate without sharing a body. The signature is the contract: "I promise to produce a fn with this name and this type." Types are how speculative threads handshake.

Two subclaims that follow:

**Runtime is tolerant.** Anything that would have crashed substitutes a default and records the substitution in the trace. The program reaches the end; the user iterates on what was substituted. Like NaN propagation but for "made-up values."

**Human is a materializer.** When find and generate both fail, the human is the third path. Their answers cache as real package fns. Don't build a separate "ask the user" workflow — fold it into the existing surface.

The algorithm, one paragraph: the interpreter parses pseudocode; for each unrecognized name it races find (corpus search) against generate (LLM); first non-failure wins; eval starts as soon as anything is runnable; when a call hits an unresolved name the frame parks and the scheduler runs other ready frames; when materialization completes parked frames wake; if both paths fail within budget, the runtime substitutes `defaultFor returnType` and keeps going. Generated bodies are themselves pseudocode → recursive → fractal.

---

## §2 LibExecution changes (the load-bearing part)

The pivot is `Interpreter.fs:317` — the line `raiseRTE (RTE.FnNotFound …)`. Change "give up" to "wait." Three surgical edits, all live in code:

1. **`RuntimeTypes.fs:88-115`** — `FQFnName.Pending of { handle: Guid; name: string }` alongside `Builtin` and `Package`. Stable handle, simple name. (SignatureHint deferred.)
2. **`RuntimeTypes.fs:1267`** — `PackageManager.materializeFn : FQFnName.Pending → Ply<Option<PackageFn>>`. Default returns None.
3. **`Interpreter.fs:325` (executionPoint match) + `Interpreter.fs:1049` (Apply match)** — new `Pending` arms. Call `materializeFn`, build a synthetic InstrData, push a frame with `executionPoint = Function(Pending p)`. Cache the materialized fn under both its hash and the Pending handle to avoid re-materialization.

Plus minor:
- `Functions.materialize` on `ExecutionState` mirrors `.package`'s shape.
- `VMState` gains `pendingFnInstrCache : Dict<Guid, InstrData>` — keyed by handle so the second frame-enter skips re-calling materializer.
- `Dval.defaultFor : TypeReference -> Dval` for the future EmptyBody recovery path. Covers all primitives + lists/dicts/tuples; custom types fall to `DUnit`.
- Pattern-match exhaustiveness fixes in `RuntimeTypesToDarkTypes`, `RTQueryCompiler`, `Execution`, `LibDB/Tracing`, `LibSerialization/Binary/RT/Common`. ~9 sites total (much less than the predicted ~74).

The interpreter's happy path is unchanged — Pending arms only fire when Pending refs appear.

**Frame-return type-check** (Interpreter.fs ~1148) has a Pending arm that returns `TVariable "pdd_pending_return"` — accept anything. Real sig-hint check comes when SignatureHint lands on Pending.

---

## §3 Find vs Generate (the scheduler)

**Default policy** (per `MaterializeOptions`): 1s budget per path, both fire, first-non-failure wins, cancel the loser, fall back to `EmptyBody` if both empty.

**Find** — corpus search. Priority: pinned hashes (manually promoted) → exact package-store name match → name+arity loose match. No embeddings; keyword match suffices for v1.

**Generate** — one LLM call with the v4 system prompt (see EMPIRICAL.md). Returns JSON `{sig, body}`. Cost ~$0.00005 per gpt-4o-mini call.

**`@deep_materialize` annotation** (deferred) opts into 60s budget and Sonnet. Default stays cheap.

**Implementation today:** synchronous, generate-only. The race + parking lives in design only; doesn't block any demo we want to build first.

---

## §4 Signature consensus

When parallel materializations on the same name produce different signatures — which wins?

**Strategy A (default, in spirit):** first-to-write-wins. Whoever races to the cache first claims the handle. Other materializer's result is discarded. Rejected attempts get logged so the user can see in the trace.

**Strategy B (deferred, v2):** call-site-constraint-driven. The call site `let r: Int = foo(x)` carries an implicit constraint `ReturnMustBe Int`. Materialization candidates that violate the constraint are rejected before claiming. Triggers when first-wins thrashes.

**Identity rule:** same name + same lexical scope = same Pending handle. The parser/PT2RT maintains a per-execution `Dict<scope * name, Pending>`. Don't create new handles for repeat references.

**Recursion through pending:** if `foo`'s materialized body references `foo`, the parser's `currentlyMaterializing` set lets it reuse the in-flight handle. Otherwise we'd thrash.

**Types of types (pending TYPE refs):** out of scope. Fns only.

---

## §5 Tolerant runtime

Most errors substitute defaults instead of crashing.

**`RecoveryPolicy`** — pluggable per-RTE decision. Variants:
- `KillFrame` — current behavior; unwind on error
- `EmptyBody` — substitute `Dval.defaultFor returnType` for missing fn body
- `EmptyFrame` — pop frame, give caller a default
- `AskUser` — surface to human-in-loop

**`tolerantPolicy`** (the default for `--tolerance loose`) routes:
- `MaterializationFailed` → `EmptyBody`
- `FnNotFound` → `EmptyBody`
- `MatchUnmatched` → `EmptyBody`
- `DivideByZero` → `EmptyBody` (returns 0)
- `UncaughtException` → `EmptyFrame`
- `DeprecatedItemHalted` → `AskUser`
- everything else → `KillFrame`

CLI flag: `--tolerance strict|loose|debug`. Default `loose`. Tests run `strict`. `debug` is `AskUser` for everything (interactive).

**Risk:** tolerance hides bugs in *your* code, not just LLM hallucinations. Mitigation: run tests in `strict` periodically; if same test passes loose but fails strict, you've been hiding behind recovery. Eventually: tag recovered Dvals (`DRecovered`-style) so downstream consumers know the value was substituted.

**Not yet wired:** the interpreter's Pending arm currently raises `FnNotFound` on None. The recovery policy hook is design-only. Wiring is a ~30-line change once we want this.

---

## §6 Builtin permissions / capabilities

LLM-generated code will eventually try to `File.delete "*"` or `HttpClient.post` to random places. Capability tags gate this.

**`Capability` enum:** `CapPure` | `CapReadFile` | `CapWriteFile` | `CapReadNet` | `CapWriteNet` | `CapReadEnv` | `CapReadTime` | `CapReadRandom` | `CapWriteDB` | `CapExec` | `CapSendSecret` | `CapAny`.

**Where enforced:** at the call site in `Apply` for `Builtin` calls. Each `BuiltInFn` gains `capabilities : Set<Capability>`. Default everything to `{CapPure}` then bump the ~dozen that need more.

**Decision:** `Granted | Denied of reason | DeniedAsk`. Plugged onto `ExecutionState.capabilityCheck`. Denied calls trigger `RecoveryPolicy` (substitute default, or ask human, or raise).

**LLM-side:** the generate prompt only lists builtins the session has granted. Model won't know HttpClient.post exists if `CapWriteNet` isn't granted. (Belt-and-suspenders — the runtime gate is the source of truth.)

**CLI surface:**
- Install-time interactive `dark install` prompt for capability defaults.
- Per-invocation `--allow http,fileread` / `--deny exec`.
- Interactive `--ask` mode: prompt y/a/n/N on first use.

**Implementation today:** none. CapAny implicit. Wire before any non-dev usage.

---

## §7 Human in the loop

The human is a *fallback materializer*, not a separate workflow. Their answer produces a `MaterializeResult`, caches like find/generate.

**Triggers (when to ask):**
1. Both find and generate fail and `allowEmptyBody = false`.
2. Capability not yet granted (`--ask` mode).
3. Signature consensus disagreement on a `@require_consensus` fn.
4. Repeated failure on the same handle (3 attempts).
5. Trace divergence — same input, different output across runs.
6. `@ask_user` annotation forces interaction always.
7. `Pdd.pause "msg"` breakpoint in source.

**Modes:**
- **Sync (interactive CLI):** stdout prompt, stdin response. Default for the spike.
- **Async (inbox file):** runtime writes a `pending-decision` record to `rundir/inbox/`; user runs `dark pdd inbox` later.
- **Out-of-band (webhook):** production-shape; out of scope.

**`humanResolver : HumanQuery -> Ply<HumanResponse>`** field on `ExecutionState`. Default impl is a TTY prompt; tests inject a deterministic fake.

**Caching of human answers:** materialization answers → package store (Option 3, durable across sessions). Capability grants → session-only. Breakpoints → one-shot.

---

## §8 Tracing as artifact

The trace is the program. JSONL, append-only, in `rundir/traces/<sessionId>.jsonl`.

**Event kinds:** session_start | program_loaded | materialize_start | candidate | candidate_rejected | materialize_done | frame_park | frame_resume | call_start | call_end | recovery | capability_check | capability_grant | human_ask | human_answer | cost | rte_uncaught | session_end.

Each line: `{t: <ms-since-session-start>, ev: <kind>, …kind-specific-fields}`.

**Replay:** given a trace, pre-populate the package cache with the recorded hashes, intercept builtins that record their result, re-execute. Diff the result trace against the original. Deterministic if no nondeterminism (random, time, network) is involved.

**Diff:** walk two JSONL streams, align by `t` and event kind, emit per-line differences. Natural review surface.

**Promote:** `dark pdd promote <fnHash>` writes to a `pdd_pinned_fns` table mapping `name → hash`. The find path consults it first. Future sessions hit the cache.

**SCM:** branches store sketches + pinned hashes + trace sets. Merging compares each axis independently.

**Implementation today:** PDDMaterializer writes a `pdd-materialize.jsonl` log per call. Replay/diff/promote are deferred. Live `Tracing` struct on `ExecutionState` records package + builtin calls.

---

## §9 HTML view (H3 — the visualization payoff)

A live HTML file at `rundir/pdd-view/<sessionId>.html`. Open it once in a browser; it self-refreshes every 1s via `<meta http-equiv="refresh">` until the session closes.

**Two-pane layout:**

```
┌──────────────────────────┬──────────────────────────┐
│ functions                │ events                   │
│                          │                          │
│  ✓ addOne (real, 312ms)  │  16:03:12 start: addOne  │
│      sig: (x: Int64): I64│  16:03:13 llm: "x + 1L"  │
│      body: x + 1L        │  16:03:13 compiled       │
│  ⋯ greet (in-progress)   │  16:03:14 done: addOne   │
│                          │                          │
└──────────────────────────┴──────────────────────────┘
```

**State badges** (color + glyph + CSS class):
- ⋯ **in-progress** (yellow) — LLM call in flight
- ✓ **real** (green) — materialized + body translated successfully
- ▼ **fake** (gray) — fallback identity / EmptyBody (LLM body wasn't parsable by mini-parser)
- ↻ **cached** (blue) — hit `pendingFnInstrCache`, no LLM call
- ✗ **failed** (red) — materialization errored (cap denied, parse failure, etc.)

**Implementation** (live in `backend/src/LibExecution/PDDHTMLView.fs`):
- `createSession id path → Session` (per-session: fns dict + event log)
- `sinkFor session → EventSink` (consumes `PDDEvent`s, updates state, rewrites HTML each event)
- `install session → previous-sink` (sets `currentSink`)
- `close session` flips off the meta-refresh

Hand-written CSS, no JS, no deps. Path is `rundir/pdd-view/<sessionId>.html`.

**Not yet wired to a CLI command.** That's H1.

---

## §10 EventSink (the substrate beneath stderr-log + HTML view)

A single `currentSink : PDDEvent -> unit` mutable on `PDDMaterializer`. Default `nullSink`. CLI/tests install their own sink before invoking `materialize`.

**Events emitted by `PDDMaterializer.materialize`:**
- `MaterializeStart of name * model`
- `LLMResponse of name * elapsedMs * rawBody`
- `ParseOk of name * sig * body`
- `CompileBody of name * kind * registerCount` — kind = `"constant" | "identity" | "arith" | "fallback-identity"`
- `MaterializeDone of name * FnState * elapsedMs`
- `MaterializeFailed of name * reason`

`FnState` matches the HTML view badges: `InProgress | Real | Fake | Cached | Failed`.

Sink failures are swallowed — a buggy view never takes down the runtime.

---

## §11 Glossary (terminology, pinned)

| Term | Meaning |
|---|---|
| **PDD** | Pseudocode-Driven Development — the approach. |
| **Sketch** | The user-written or LLM-emitted source: names + sigs + (maybe) bodies, with holes. Contrast cache. |
| **Cache** | Package store's collection of materialized fns/types/values, content-addressed. Derived from past materializations. |
| **Trace** | Append-only JSONL of execution events. The authoritative record. |
| **Materialization** | The act of turning a `Pending` reference into a concrete `PackageFn` (or `EmptyBody`, or `Failed`). |
| **Pending** | A reference in source/RT to a fn without a body yet. Identified by stable `handle : Guid`. |
| **Handle** | The stable Guid of a Pending. Survives speculation attempts. |
| **Pinned hash** | A `name → hash` mapping declaring "this is the canonical materialization." Written by `dark pdd promote`. |
| **Find / Generate** | The two materialization paths: search corpus / call LLM. |
| **Race** | Default scheduling: both paths fire, first-non-failure wins. |
| **EmptyBody** | Synthetic body returning `Dval.defaultFor returnType`. Tolerant fallback. |
| **Tolerant runtime** | The default execution mode: substitute defaults on errors, record in trace. |
| **RecoveryPolicy** | `KillFrame | EmptyBody | EmptyFrame | AskUser` — per-RTE decision. |
| **Capability** | An effect tag on a builtin: `CapReadNet`, `CapWriteFile`, etc. |
| **Grant** | A capability the current session can use. |
| **EventSink** | `PDDEvent → unit`. Mutable global. CLI / HTML view / tests install their own. |
| **FnState** | Badge in the HTML view: `InProgress | Real | Fake | Cached | Failed`. |

**Anti-glossary** (terms we *don't* use, by design):
- "Agent" — overloaded; the materializer isn't an agent in the multi-turn sense.
- "Stub" — implies "human will fill this in." Pending bodies are filled by the runtime by default.
- "Lazy" alone — overloaded with normal lazy eval. Say "deferred" or "pending."

---

## §12 The five-claim summary (memorize)

1. The source is lazy.
2. The trace is the program.
3. Types are the coordination protocol.
4. The runtime is tolerant.
5. The human is a materializer.

If you can defend the 60-second pitch using just these five sentences, the rest of the design follows. If you can't, no amount of doc-reading will help — go re-read §1.

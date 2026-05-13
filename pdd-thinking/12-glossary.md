# 12 — Glossary

Consistent terminology for the docs and future code. Pin these down so reviewers don't argue about names.

---

## Core terms

**PDD** — Pseudocode-Driven Development. The overall approach.

**Sketch** — what the user (or LLM) writes. Names + sigs + maybe bodies, with holes. The thing committed to source control. Contrast: *cache*.

**Cache** — the package store's collection of materialized fns/types/values. Content-addressed. Derived from past traces, not source.

**Trace** — append-only JSONL of execution events. The authoritative record of what happened. (See `08-tracing-as-artifact.md`.)

**Materialization** — the act of turning a `Pending` reference into a concrete `PackageFn` (or `EmptyBody`, or `Failed`). Has a source (find / generate / human) and a result.

**Pending** — a reference in source/RT to a fn that doesn't yet have a body. Identified by a stable `handle : Guid`. Carries a `SignatureHint`.

**Handle** — `System.Guid`. The stable identifier for a pending fn across the lifetime of a session. Survives speculative-attempt cancellations.

**Sketch source** — `.dark` files (or LLM output) containing pseudocode. May parse partially.

**Pinned hash** — a `name → hash` mapping declaring "this is the canonical materialization." Written by `dark pdd promote`. Consulted by `find` first.

---

## The two paths (per `03-find-vs-generate.md`)

**Find** — search-existing path. Looks in package store, then pinned hashes, then name-keyed substring index, then (later) embeddings. Returns `Some PackageFn` or `None`.

**Generate** — LLM-call path. One Anthropic call (default model Haiku 4.5 for speed) with sig + description + ambient context. Returns `Some PackageFn` (after parsing the LLM output) or `None`.

**Race** — the default scheduling mode. Both find and generate fire; first non-failure wins. (Per `MaterializeOptions.preferPath = RaceBoth`.)

**Budget** — wall-clock budget per path, default 1000ms. Configurable up to ~60s for `@deep_materialize` fns.

**EmptyBody** — fallback when both paths fail. Returns `Dval.defaultFor returnType`. The program keeps moving.

---

## Runtime concepts

**Tolerant runtime** — the default execution mode. Recovers from most RTEs by substituting defaults; recoveries are logged to the trace. (Per `05-tolerant-runtime.md`.)

**Strict mode** — opposite. RTEs crash the frame. Tests run here. Used for graduation-from-PDD code.

**RecoveryPolicy** — per-error decision: `KillFrame | EmptyBody | EmptyFrame | AskUser`. Encoded as `ExecutionState.recoveryPolicy : RTE.Error -> RecoveryPolicy`.

**Recovered value** — a `Dval` produced by recovery rather than by genuine evaluation. Optionally tagged for downstream "did this answer involve substitutions?" tracking. (V2 feature — see `05`.)

---

## Capability terms (per `06-builtin-permissions.md`)

**Capability** — an `enum`-style tag on a builtin describing the kind of effect it has. `CapPure`, `CapReadFile`, `CapWriteNet`, etc.

**Grant** — a capability the current session can use. Specified at install time, at invocation (`--allow`), or interactively.

**Grant scope** — `Session | Always | Once | Never`. Determined by user response to a capability ask.

**Capability check** — runtime gate at the `Apply(Builtin _)` site that compares the fn's capabilities to the session's grants. Returns `Granted | Denied of reason | DeniedAsk`.

---

## Sig + consensus (per `04-signature-consensus.md`)

**SignatureHint** — parser's / LLM's best guess at a fn's sig. Includes typeParams, paramHints, returnHint. Optional everywhere.

**Signature constraint** — a hard requirement extracted from the call site: `ParamMustBe(idx, T) | ReturnMustBe T | ArityIs n`. Used by Strategy B consensus.

**Strategy A (consensus)** — first-to-write-wins. Default. Rejected candidates get logged.

**Strategy B (consensus)** — constraint-aware: candidates that don't satisfy call-site constraints are rejected. v2 design.

---

## Human-in-loop (per `07-human-in-loop.md`)

**Human as materializer** — the framing. Human output is a `MaterializeResult`, same as find/generate.

**HumanQuery** — the runtime's question. Types: `AskMaterialize | AskCapability | AskTraceDivergence | AskAnnotation | AskBreakpoint`.

**HumanResponse** — the user's answer. Types: `RespAccept | RespReject | RespEdit | RespGrant | RespAlways`.

**Resolver** — `humanResolver : HumanQuery -> Ply<HumanResponse>`. The pluggable surface. Default: TTY prompt. Alternates: inbox file, webhook.

**Inbox** — the file-based queue for async human-resolver mode (Mode B).

---

## Trace (per `08-tracing-as-artifact.md`)

**Trace event** — one JSONL line in a trace file. Has `t` (ms since session start) and `ev` (event kind).

**Session** — one execution from start to finish. Identified by a `sessionId`.

**Replay** — re-running an execution using a recorded trace, pre-populating the cache and intercepting builtins to return recorded results.

**Diff** — pairwise trace comparison, useful for review.

**Promote** — mark a materialization as canonical (pinned hash). Future runs find it instead of re-materializing.

---

## F# nouns (introduced by this design)

| Name | Where | What |
|---|---|---|
| `FQFnName.Pending` | `RuntimeTypes.fs` | New variant for unmaterialized fn refs |
| `SignatureHint` | `RuntimeTypes.fs` | Sig guess attached to a Pending |
| `PackageManager.materializeFn` | `RuntimeTypes.fs` | New field; turn Pending → PackageFn |
| `MaterializeOptions` | `LibPDD/Materializer.fs` | Per-call budget + preferences |
| `MaterializeResult` | `LibPDD/Materializer.fs` | `Materialized | EmptyBody | Failed` |
| `MaterializeSource` | `LibPDD/Materializer.fs` | `FromFind | FromGenerate | FromHuman` |
| `Capability` | `RuntimeTypes.fs` | Capability tag enum |
| `CapabilityDecision` | `RuntimeTypes.fs` | `Granted | Denied | DeniedAsk` |
| `RecoveryPolicy` | `RuntimeTypes.fs` | `KillFrame | EmptyBody | EmptyFrame | AskUser` |
| `VMState.pendingFrames` | `RuntimeTypes.fs` | Frames parked on materialization |
| `ExecutionState.recoveryPolicy` | `RuntimeTypes.fs` | Pluggable RTE → policy fn |
| `ExecutionState.capabilityCheck` | `RuntimeTypes.fs` | Pluggable cap check |
| `ExecutionState.humanResolver` | `RuntimeTypes.fs` | Pluggable human prompter |
| `LibPDD` | new project | Materializer + capabilities + tracing-extensions |

---

## Anti-glossary (terms we are deliberately NOT using)

- **"Agent"** — already overloaded in Stachu's notes. PDD's materializer isn't an "agent" in the multi-turn-conversation sense. It's an effect.
- **"Hole"** — Haskell-ism for "type-driven gap." `Pending` is more specific (it's a name we'd resolve, not a type-of-anything).
- **"Stub"** — implies "human will fill this in." `Pending` is filled by the runtime, by default.
- **"Lazy"** — overloaded with normal lazy eval. We say "deferred" or "pending."
- **"Sketch"** — used only for the source-code-with-holes thing, not as a verb.

---

## Style notes

- Function names in the new code: `materialize*`, `findFn`, `generateFn`, `defaultFor`, `runWithRecovery`.
- Module names: `LibPDD.Materializer`, `LibPDD.Find`, `LibPDD.Generate`, `LibPDD.Capability`, `LibPDD.Defaults`, `LibPDD.TraceEvents`.
- F# casing: PascalCase for types, camelCase for values, matching the rest of the codebase.
- Dark casing: matches existing Darklang conventions (PascalCase modules, camelCase fns).

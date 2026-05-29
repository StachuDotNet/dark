# Claims

Five claims that make PDD a paradigm rather than a feature.

## 1. The source often starts as lazy

Source begins as little more than a name and an intent — gradually
existent, expanded, typed. The artifact you ship is a *sketch + a
cache* (or, more accurately, *sketch + traces + a growing package
store*). Traditional dev treats source as the slowest-moving thing.
PDD treats it as something that grows alongside execution, becoming
more concrete with each call, edit, refinement.

Some functions never crystallize at all. A function can be *fully
delegated* to an LLM system — its body is "forever lazy," resolved
on every call rather than written down as Dark code. (The ALGORITHM
doc makes the same point.) Laziness is a spectrum, not a phase you
graduate from.

> Source code is gradually thought of, written, tested, typed,
> iterated, completed, distributed, available — all in branches,
> sandboxed and available to everywhere you want it to be.

## 2. Traces, interpreter, and agent gradually turn a prompt into working software

Not "the trace is the program" — that's overstated. The program is
a growing set of package items. Traces are *used in conjunction with*
the interpreter and an agent to drive that growth: each run records
what materialized, what substituted, what failed, what the user
accepted. Replays + diffs are tools for steering; they aren't the
artifact themselves.

## 3. Types are the coordination protocol

When parallel materializers race on the same name, they coordinate
without sharing a body. The signature is the contract: "I promise
to produce a fn with this name and this type." Type unification
lets speculative threads handshake before they finish.

## 4. The runtime is tolerant — with intent

Anything that would crash substitutes a default (`Dval.defaultFor
returnType`) and records the substitution. The program reaches the
end; the user iterates on what was substituted. NaN-propagation for
"made-up values."

But tolerance is intentional, not blind. A robust conflicts +
resolutions system (see [conflicts.md](../stable-and-syncing/conflicts.md)) decides
per-call whether to: substitute a default,
park and wait, ask a human, or fail loudly. Loose-mode-by-default
hides bugs; the policy must be deliberate.

## 5. The human is involved throughout

Not just a fallback materializer. The human is the interpreter +
agent's collaborator for:

- **Initial prompting and spec adjustments** — telling the system
  what to build
- **Review of materialized code, types, and tests** — accepting,
  rejecting, refining
- **Writing code directly** — when the agent's output isn't what's
  needed
- **Type choices** — when sig consensus is genuinely ambiguous
- **Approving capability grants** — when the materializer hits a
  permission boundary

Crucially, humans are *async* on a different cadence than other
materializers. The same parking mechanism that waits for an LLM
call must wait for a human review — but with different SLAs.
See [event-bus.md](../stable-and-syncing/event-bus.md) and [async.md](../stable-and-syncing/async.md) for what
this implies for the eval scheduler.

---

## Foundational constraint — AI is opt-in

The five claims above describe what becomes *possible* when AI
participation is enabled. **None of them imply AI participation
is mandatory.** Darklang must work fully without any AI
involvement:

- A user installs Dark, never touches an LLM key, has a complete
  development experience. The package store, the SCM, the
  viewer, the language all function.
- "The source often starts as lazy" doesn't require AI to fill in
  the body. The human can. So can a corpus search. So can a
  test-driven synthesis tool. LLM is one materializer; the human
  is another; future strategies are others.
- "The trace + interpreter + agent" framing in claim 2 — "agent"
  there generalizes: it could be a human stepping through the
  code, a non-LLM tool, or an LLM. Same machinery; different
  participants.
- "The runtime is tolerant" doesn't say *who* the recovery asks.
  The conflict-resolution dispatch lets the user choose:
  fail-loud-by-default in production, ask-human in dev, ask-LLM
  only when the user has opted into that strategy.
- "The human is involved throughout" is the *minimum*; AI
  involvement is layered on top.

PDD and related AI features are an opt-in extension to the
substrate, not the substrate itself.

---

## Pitch (60s)

> Pseudocode-Driven Development: an *opt-in* layer over Darklang
> where the interpreter materializes its own source code on
> demand, in parallel, speculatively, with the LLM as both
> author and search index. You ask for software. The runtime
> races corpus search against LLM generation. Tolerant: if both
> fail, returns a typed default and keeps moving. Recursive:
> generated bodies have their own holes. The trace records every
> materialization, every substitution, every human decision — the
> artifact you commit and share is sketch + trace + accepted
> package items, not source files alone. Darklang itself works
> without any of this; PDD is one extension layered on top.

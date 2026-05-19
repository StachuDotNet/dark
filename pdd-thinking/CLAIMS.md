# PDD — Core Claims

Five claims that make PDD a paradigm rather than a feature.

## 1. The source often starts as lazy

Source begins as little more than a name and an intent — gradually
existent, expanded, typed. The artifact you ship is a *sketch + a
cache* (or, more accurately, *sketch + traces + a growing package
store*). Traditional dev treats source as the slowest-moving thing.
PDD treats it as something that grows alongside execution, becoming
more concrete with each call, edit, refinement.

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
resolutions system (which we still need to design properly — see
`FRONTIER.md`) decides per-call whether to: substitute a default,
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
`FRONTIER.md` has more on what this implies for the eval scheduler.

---

## Pitch (60s)

> Pseudocode-Driven Development: the interpreter materializes its
> own source code on demand, in parallel, speculatively, with the
> LLM as both author and search index. You write names and
> signatures. The runtime races corpus search against LLM
> generation. Tolerant: if both fail, returns a typed default
> and keeps moving. Recursive: generated bodies have their own
> holes. The trace records every materialization, every
> substitution, every human decision — the artifact you commit
> and share is sketch + trace + accepted package items, not
> source files alone.

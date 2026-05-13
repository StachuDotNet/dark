# 06 — Builtin Permissions / Capability Model

> Stachu's directive: "we may need to figure out 'builtin restrictions' sooner than later — lots of wild code generation, don't want to get into issues. CLI installers should be able to have users choose what the agents are allowed to do."

**Status:** Stub. To be deepened in a later iteration.

## Open

- Capability tags on each builtin: `Pure`, `FileRead`, `FileWrite`, `NetGet`, `NetPost`, `Exec`, `Time`, `Random`, etc.
- CLI install flow: a `--allow http,fileread` flag; persisted in config; runtime enforces.
- Per-call prompt-style escalation: "this fn wants to call HttpClient.get — allow once, allow always, deny."
- Tying back to LibExecution: a `capabilityCheck : Builtin -> Capability -> Result<unit, Denied>` in `ExecutionState` that builtins consult before doing the effect.
- The existing `isHarmful` field on `PackageManager` is the closest analog — extend that idea.
- LLM-side: the prompt should tell the LLM which builtins are available, hiding the ones the user disallowed.

## Why now

If we materialize unbounded LLM code with full filesystem + network access, we will eventually do something dumb (rm -rf, leak a secret, post to a webhook). The capability model is part of the *threat model* for PDD, not an optional polish.

## Connection to other docs

- `02-libexecution-changes.md` — capability check is a new hook in `ExecutionState`.
- `05-tolerant-runtime.md` — capability denial is a recovery point (return `default(T)`, log).

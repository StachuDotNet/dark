# 07 — Human in the Loop

> Stachu's directive: "sometimes we need the human - for what? how will that fit in?"

**Status:** Stub. To be deepened.

## When does the human enter?

Candidate triggers (each one merits a paragraph in the deepened version):

1. **Ambiguity at materialization** — sigHint says nothing; multiple find candidates with different sigs; LLM hallucinated.
2. **High-stakes effects** — about to call a builtin tagged `Destructive` (delete file, post to network, etc.). Per `06-builtin-permissions.md`.
3. **Repeated failure** — same pending fn has failed materialization N times in a row.
4. **Trace divergence** — same trace input now produces a different output than last run.
5. **Explicit annotation** — `@ask_user` on a fn forces interaction every time.
6. **Speculation budget exhausted** — N concurrent pendings, none resolved.
7. **Type mismatch unresolvable** — sig consensus couldn't reconcile.

## How does the human enter?

Modes:

- **Async** — runtime pauses the parked frame, writes a "needs human" entry to a queue; user sees it next time they open the CLI. Good for batch sessions.
- **Sync (interactive)** — runtime prompts in the terminal: "fn `foo` needs disambiguation, here are the candidates. Pick 1/2/3 or write your own."
- **Out-of-band** — webhook / phone push for serious things (production). Not for PoC.

## Surfaces

- A `frameParkedForHuman` event in the trace.
- A CLI command `pdd inbox` listing pending decisions.
- A "review trace" UI (later) that highlights human-decision points.

## The right default for the PoC

Sync mode, prompting in the terminal, for these triggers only:
- Builtin capability not yet granted
- Pending fn that failed twice

Everything else falls through to tolerant defaults.

## The deeper question

The right vibe is: **the human is a fallback materializer.** When find fails and generate fails, ask the human. They're slow but high-quality. Same protocol — they produce a `MaterializeResult`, the runtime caches it forever (it becomes a real package fn). Human's contributions are first-class.

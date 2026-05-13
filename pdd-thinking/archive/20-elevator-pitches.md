# 20 — Elevator Pitches

For when you have to explain PDD to someone. Three lengths, one structure: claim → algorithm → why-it's-new → ask.

---

## 60 seconds (tweet / hallway)

> Pseudocode-Driven Development: the interpreter materializes its own source code on demand, in parallel, speculatively, with the LLM as both author and search index — and traces are the artifact.
>
> You write names + signatures. The runtime races a corpus search and an LLM call (1-second budget each) to fill in each body when needed. Tolerant: if both fail, returns a typed default and keeps moving. Recursive: generated bodies have their own holes. The trace records every materialization — that's what you commit and share, not source files.
>
> Different from Copilot (which suggests; PDD executes), from agentic coders (they build projects; PDD builds the function under your cursor at the moment the runtime needs it), from autocomplete (editor-time; PDD is runtime).

---

## 3 minutes (chat with another engineer)

**The setup:**
Programs today: source is durable, runtime is fast, you compile source → bytecode → run. Smalltalk-style live programming kept everything mutable but assumed the programmer wrote it all by hand. AI code completion writes individual functions but stops there — humans glue them together.

**The pivot:**
What if the interpreter could call code that doesn't exist yet, materializing it the moment it's needed? The function name plus a type signature is the contract. Behind the scenes, two things race: a corpus search ("does this exist somewhere?") and an LLM generation ("write a body matching this name and sig"). 1-second budget each. First non-failure wins. If both fail, substitute a typed default — the program keeps moving.

**Why it's a paradigm shift, not a feature:**
- Source code is no longer the durable primary artifact. You commit *sketches* (names + sigs) and *traces* (records of what materialized).
- The trace is the authoritative thing — replay it and you get the same program. Diff two traces to review changes. Promote a trace's materializations to make them canonical.
- The runtime is *tolerant*: anything that would have crashed instead substitutes a default and records it. The trace tells you what was substituted; you decide whether to fix it. Programs that previously couldn't run halfway can now run all the way and report.
- Recursive: generated bodies are themselves pseudocode with their own holes. The materialization process is fractal.

**The bet:**
Most of a typical program is *boilerplate the LLM can write*. The interesting bits are the calling structure (which the human wrote as pseudocode), the data (which the runtime captured), and the *trace* (which lets you reproduce the result). Materializing on demand makes everything else cheap.

**The first demo:**
`addOne` → returns 6 when called with 5. Boring but proves the pipeline. Real demo: type one prompt — "fetch the top HN headline, sentiment-score it, summarize in one sentence" — and the runtime materializes `fetchUrl`, `extractHeadline`, `sentiment`, `summarize` from scratch, calls them, produces the answer. Trace gets saved. You read it, decide what to keep.

**What I'm asking for:**
*<insert: collaborator, advisor input, GPU credits, or "just thoughts">.*

---

## 10 minutes (technical deep dive)

### The three claims that make this a paradigm

1. **The source is lazy.** Names + sigs are durable; bodies are computed at runtime speed by the runtime itself. The artifact you ship is a sketch + a cache.
2. **The trace is the program.** What ran is more durable than what's written. SCM tracks traces. Reviews diff traces. Distribution ships sketch + cache.
3. **Types are the coordination protocol.** Pending references carry sig hints. Parallel materialization attempts agree via type unification at the call site. Two candidates with conflicting sigs → first-wins (Strategy A) or constraint-driven (Strategy B).

Subclaims that follow:
4. **The runtime is tolerant.** Missing things substitute defaults; recoveries are auditable in the trace. Tightens to strict mode for tests and production.
5. **The human is a materializer.** When find and generate both fail, the human is the third path. Their answers cache as real package fns.

### The algorithm

```
parse(source) → AST with Pending(name, sigHint) leaves
for each Pending leaf:
    fire: find(name, sigHint)     # corpus search, 1s budget
    fire: generate(name, sigHint) # LLM call, 1s budget
    when first completes: cancel the other, cache result
start eval immediately
when eval hits a Pending(name):
    if materialized → use it
    if in-flight → park frame, switch to another runnable frame
    if all parallel work fails:
        substitute default(returnType), record recovery in trace, continue
generated bodies are themselves pseudocode → recurse
```

### The runtime intervention (where this lives)

One file matters most: `LibExecution/Interpreter.fs:317`. Where today we `raiseRTE (FnNotFound ...)`, we instead `await materializeFn(pending)`. Three new types on `FQFnName`: `Pending`, with a stable `handle: Guid` and a `SignatureHint`. New field on `PackageManager`: `materializeFn`. New dictionaries on `VMState` for parked frames. The interpreter's happy path is unchanged.

### The four runtime knobs (user-facing)

```
--tolerance strict|loose|debug
--allow http,fileread,writeio,exec
--materialize-budget 1000ms
--model cheap|deep|<name>
```

### Capability model (because we're letting LLMs generate code)

Builtins are tagged with capabilities (`CapPure`, `CapReadFile`, `CapWriteNet`, `CapExec`, `CapSendSecret`, ...). Default install asks the user what to grant; per-call escalation in `--ask` mode. Denied calls trigger recovery (substitute default) or pause for the human, depending on mode.

### The trace format

Append-only JSONL. Event kinds: `materialize_start`, `candidate`, `candidate_rejected`, `materialize_done`, `frame_park`, `frame_resume`, `call`, `recovery`, `capability_check`, `capability_grant`, `human_ask`, `human_answer`, `cost`. Plus the existing call/result tracing.

Tooling on top: `dark pdd trace show/replay/diff/promote/cost`. The CLI surfaces traces as first-class. Promote turns a session's materializations into canonical package fns.

### What the spike will prove (or disprove)

In two weeks of focused hacking with a $10 OpenAI budget:

- **Bronze**: one fn materialized via LLM and executed correctly. (Day 2-3.)
- **Silver**: multi-step program with parallel materialization. (Day 5-7.)
- **Gold**: HN headline sentiment demo — one prompt to working result. (Day 10.)

Failure modes I'm explicitly watching for:
- Sig consensus thrashing (Strategy A insufficient → need B).
- Recovery hiding logic bugs (separate from materialization failures).
- Async/parking scheduler more complex than expected (fallback: serial materialization).
- The spike succeeds but the paradigm "feels wrong" → most valuable failure; write up why.

### What I'm not claiming

- Not a replacement for typed languages: types are *more* important here, not less.
- Not "AI writes the whole program": humans write the calling structure (the sketch); the LLM fills in plumbing.
- Not faster than just-writing-code-yourself for code you know how to write: PDD is for code you can describe but don't want to write.

### Open questions worth your input

(Whichever apply to your audience.)
- Should `Pending` live in PT (source-level) or RT-only?
- Eager vs lazy materialization at program load?
- Right way to handle pending types, not just pending fns?
- The trace UX — is "diff two traces" really a tractable review surface?

### What I'm asking for

Specifically:
- 30 minutes of your time to react to the 5-claim summary above.
- A pointer to anyone else thinking about this problem.
- Skepticism. Tell me what's wrong with it.

---

## Pitch picker by audience

| Audience | Pitch length | What to emphasize |
|---|---|---|
| Tech twitter / blog | 60s | The five claims; "trace is the program" |
| A friend at dinner | 60s | "The runtime makes up code" |
| Another language designer | 3min | Coordination via types; lazy source; recovery |
| Funder / partner | 3min | The bet; the demo arc; the $10 verification |
| Other Darklang dev (Ocean, Feriel, Paul) | 10min | The full deep dive; what changes in LibExecution |
| Yourself, when stuck | The five claims | Read them out loud |

---

*If anything in the 60-second version is hard to defend in detail, the longer pitches won't help. Practice the 60-second.*

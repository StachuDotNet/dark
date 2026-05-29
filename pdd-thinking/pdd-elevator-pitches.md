# Elevator Pitches

Two lengths.

## 60 seconds (tweet / hallway)

> Pseudocode-Driven Development: the interpreter materializes its
> own source code on demand, in parallel, speculatively, with the
> LLM as both author and search index — and traces are the artifact.
>
> You write names + signatures. The runtime races a corpus search
> and an LLM call (1-second budget each) to fill in each body when
> needed. Tolerant: if both fail, returns a typed default and keeps
> moving. Recursive: generated bodies have their own holes. The
> trace records every materialization — that's what you commit and
> share, not source files.
>
> Different from Copilot (which suggests; PDD executes), from
> agentic coders (they build projects; PDD builds the function
> under your cursor at the moment the runtime needs it), from
> autocomplete (editor-time; PDD is runtime).

## 3 minutes (chat with another engineer)

**The setup:**
Programs today: source is durable, runtime is fast, you compile
source → bytecode → run. Smalltalk-style live programming kept
everything mutable but assumed the programmer wrote it all by hand.
AI code completion writes individual functions but stops there —
humans glue them together.

**The pivot:**
What if the interpreter could call code that doesn't exist yet,
materializing it the moment it's needed? The function name plus a
type signature is the contract. Behind the scenes, two things race:
a corpus search ("does this exist somewhere?") and an LLM
generation ("write a body matching this name and sig"). 1-second
budget each. First non-failure wins. If both fail, substitute a
typed default — the program keeps moving.

**Why it's a paradigm shift, not a feature:**
- Source begins as little more than a name. You commit *sketches*
  (names + sigs), *traces* (records of what materialized), and a
  growing package store. Source is gradually thought of, written,
  tested, typed, iterated, completed, distributed, available.
- Traces, interpreter, and agent gradually turn a prompt into
  working software. Replay them, diff them, promote what you want
  to keep.
- The runtime is *tolerant*: anything that would have crashed
  instead substitutes a default and records it. You decide whether
  to fix it. Programs that couldn't run halfway can now run all
  the way and report.
- Recursive: generated bodies are themselves pseudocode with their
  own holes. The materialization process is fractal.

**The bet:**
Most of a typical program is *boilerplate the LLM can write*. The
interesting bits are the calling structure (the human wrote it as
pseudocode), the data (the runtime captured), the prompts (a
first-class type), and the *trace* (lets you reproduce the
result). Materializing on demand makes everything else cheap.

**The first demo:**
`addOne` → returns 6 when called with 5. Real demo: type one
prompt — "fetch the top HN headline, sentiment-score it, summarize
in one sentence" — and the runtime materializes `fetchUrl`,
`extractHeadline`, `sentiment`, `summarize` from scratch, calls
them, produces the answer.

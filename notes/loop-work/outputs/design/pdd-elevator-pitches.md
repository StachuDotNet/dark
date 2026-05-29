# Elevator Pitches

Two lengths.

## 60 seconds (tweet / hallway)

> Pseudocode-Driven Development: you ask for software, and the
> interpreter materializes its own source on demand — in parallel,
> speculatively, with the LLM as both author and search index. The
> trace is the artifact.
>
> You type `dark prompt "..."`. A background agent starts building
> the thing while the CLI watches it happen. Each missing function
> is filled by racing a corpus search against an LLM call. Tolerant:
> if both fail, return a typed default and keep moving. Recursive:
> generated bodies have their own holes. Some bodies never crystallize
> into Dark code at all — they stay "forever lazy," delegated to an
> LLM every call. What you commit and share is the trace, not source.
>
> Different from Copilot (suggests; PDD executes), from agentic
> coders (they build a repo of files; PDD builds the function the
> runtime needs at the moment it needs it), from autocomplete
> (editor-time; PDD is runtime).

## 3 minutes (chat with another engineer)

**The setup:**
Programs today: source is durable, runtime is fast, you compile
source → bytecode → run. Smalltalk-style live programming kept
everything mutable but assumed the programmer wrote it all by hand.
AI code completion writes individual functions but stops there —
humans glue them together.

**The pivot:**
You ask for software. `dark prompt "fetch the top HN headline,
sentiment-score it, summarize in one sentence"` starts a background
agent that decomposes the request and begins building it; the CLI
drops into a watching state (or runs it in the background) as the
pieces come alive. When the interpreter hits a function that doesn't
exist yet, the name plus a type signature is the contract, and two
things race to fill it: a corpus search ("does this exist somewhere?")
and an LLM generation ("write a body matching this name and sig").
First non-failure wins. If both fail, substitute a typed default —
the program keeps moving.

**Why it's a paradigm shift, not a feature:**
- Source begins as little more than a request. You commit *sketches*
  (the calling structure), *traces* (records of what materialized),
  and a growing package store. Source is gradually thought of,
  written, tested, typed, iterated, completed, distributed.
- Some functions never need to crystallize. A body can stay
  **forever lazy** — delegated to an LLM on every call instead of
  freezing into Dark code. You promote a body to real source only
  when you want to pin its behavior.
- The runtime is *tolerant*: anything that would have crashed instead
  substitutes a default and records it. Programs that couldn't run
  halfway now run all the way and report.
- Recursive: generated bodies are themselves pseudocode with their
  own holes. Materialization is fractal.

**The bet:**
Most of a typical program is *boilerplate the LLM can write*. The
interesting bits are the calling structure (what you asked for, made
concrete), the data (the runtime captured), the prompts (a first-class
type), and the *trace* (lets you reproduce the result). Materializing
on demand makes everything else cheap — and the trace, not a tree of
source files, is what you keep.

**The state of it:**
This is a spike, currently resting. The proof-of-concept ran end to
end; the next pass moves the materializer, orchestration, and viewer
out of the F# substrate and into Dark, where the substrate provides
only the primitives (events, capabilities, conflicts) and Dark
provides the policy.

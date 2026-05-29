# Visibility Into The Black Box vs Dark

*research note · matgreten.dev/posts/visibility-into-the-black-box/*

## The post

Mat Greten built **ADW**, a pipeline shipping AI-generated PRs into a legacy Rails monolith. After 263 runs it reliably ships code, but the *pipeline* is a black box: he can't answer "how often does PR submission fail?" or "when review triggers a second patch round, does it usually succeed?" The deterministic glue is Ruby; agent runs produce logs, not queryable data. He migrates the pipeline to Swamp — every phase produces a typed, versioned, immutable artifact, queryable with CEL; a 268-line Ruby provider-resolution class moves into TypeScript extensions whose inputs and outputs are first-class data. Now he can measure: an 11.8% build-phase failure rate, per-session cost, calls per session, throughput.

## The thesis

The black box is **not the LLM** — it's the **pipeline around it**. The agent does intrinsically non-deterministic creative work; the pipeline does deterministic work (phase transitions, retries, provider selection, PR submission, patch-and-re-review). The deterministic part is observable in principle but buried in glue. Instrumenting it turns "ships code most of the time" into "ships code with measurable confidence."

## Why it lands for Dark

Dark's design heads at the same problem: agents materialize Dark code, caps get checked, conflicts dispatched, events emitted. Every op carries identity; every cap-check and conflict resolution is recorded. That is **trace-as-log** — a complete record exists.

But Greten's question — "across 263 runs, what's this phase's failure rate?" — is **trace-as-projection**, and that's exactly the distinction the substrate is built around. The ops are the record of what happened; the answer is a projection folded over them. Dark already records the ops; what's thin is the projection layer that turns emissions into answers.

## Three moves, in Dark terms

### 1. Phase outcomes as typed ops, not log lines

Greten's Ruby printed "failed to submit PR" but emitted no typed `PRSubmissionAttempted { outcome, error_kind, duration_ms, cost_usd }`. In Dark every materialization, cap-check, and conflict resolution should emit an op carrying **outcome metadata** — success / error_kind / duration / cost — not just a "something happened" signal. The agent runtime default-instruments, the event-bus carries it, the viewer aggregates. [capabilities.md](../capabilities.md) already sets this discipline for caps; PDD needs it for materialize + refine cycles.

### 2. Decisions through traced fn calls, not glue

Greten's 268-line resolution class became extensions whose inputs and outputs are themselves artifacts — so the decision is queryable ("for this prompt shape, which provider, and how often did it succeed?"). Dark's analog: any decision living in a prompt string or F# `if`/`match` should be a Dark fn whose calls land on the op stream. Push more **decisions** through ops and the projection can ask "for materializations where the agent picked strategy X, what's the success rate vs Y?" This is also the forcing function for cohabitation's "intent as first-class" — decisions are how intent operationalizes, and they should be the same op shape humans and agents both produce.

### 3. Measurable hypotheses

Greten ends with "does low ideation confidence predict build failure?" — now *answerable* because both signals are typed and queryable. Every speculative design claim ("cap denials route to owner sub-second," "refine cycles converge in ≤3 rounds," "hot-reload stays under 100ms at depth N") becomes a measurable hypothesis once the op stream is projected. Today they're validated by feel; with cap-log, materialize-log, conflict-log, and the event-bus all projectable, they're validated by query. This is the strongest argument for landing the audit/cap infra before PDD: **the audit infra is also the iteration infra.**

## The honest tension

Swamp's instrumentation took real work — the 268-line class didn't move itself, and Dark faces the same cost. The event-bus + parking get this right by construction (typed ops, recorded as they go); the discipline is keeping PDD on those rails rather than adding a side-channel of untyped log lines. The temptation to skip it for v0 ("we'll just read the code") is exactly what fails at scale — and the moment you wish you'd been recording, the data is already gone. See [event-bus.md](../event-bus.md), [conflicts.md](../conflicts.md).

## What visibility looks like, Dark-flavored

The viewer ([view-sketches.md](../view-sketches.md)) is the home — a telemetry strip, each entry a projection over the op stream:

- **Per-session** — materializations attempted / successful / refined / abandoned.
- **Per-cap** — top uses, top denials, grant-prompt latency.
- **Per-conflict** — kinds by frequency, by resolution outcome, by author.
- **Per-fn** — "materialized 47 times across 12 sessions; 85% success; mean 1.3 refine-rounds; LLM cost per attempt."

All are projections over ops the substrate already records. The work is folding them and shipping the view.

## Headline

Greten's post is the case study Dark's design implicitly argues for. The ethos already says "show what's happening, by default" ([cohabitation.md](../cohabitation.md)); the ops are already recorded. The open work is making **every phase of every agent's work** a projection on the same surface — not just caps and conflicts, but materialization outcomes, plan revisions, refine cycles. Instrumentation isn't an optional epic; it's the difference between a black box and a substrate. Ship PDD without it and you ship the system Greten spent his post arguing against.

---

**Sources**

- https://matgreten.dev/posts/visibility-into-the-black-box/
- https://swamp.club/
- [capabilities.md](../capabilities.md), [conflicts.md](../conflicts.md), [event-bus.md](../event-bus.md), [cohabitation.md](../cohabitation.md), [view-sketches.md](../view-sketches.md)

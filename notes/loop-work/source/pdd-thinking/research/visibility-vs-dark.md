# Visibility Into The Black Box vs Dark

*2026-05-26 · research note · re: matgreten.dev/posts/visibility-into-the-black-box/*

## The post in one paragraph

Mat Greten built **ADW** (Agentic Development Workflow) — a pipeline that ships AI-generated PRs into a legacy Rails monolith, *"high-ownership, high-craftsmanship context."* After **263 runs** the pipeline reliably ships code, but the pipeline itself is a black box: he can't answer *"how often does Graphite PR submission fail?"* or *"when review triggers a second patch round, does the second round usually succeed?"* The deterministic glue is in Ruby; the agent runs produce logs, not queryable data. He fixes it by migrating the pipeline to **Swamp**: every phase produces a typed, versioned, immutable artifact queryable with CEL. Decision logic — including a **268-line Ruby provider-resolution class** — moves into TypeScript extensions whose inputs and outputs are first-class swamp data. Now he can measure: **11.8% build-phase failure rate** (31/263), **$0.42 avg session cost**, **23 agent calls per session**, **12x PR throughput**.

## The thesis

The black box is **not the LLM**. It's the **pipeline around the LLM**.

The agent does creative work that is intrinsically non-deterministic. The pipeline around the agent does deterministic work — phase transitions, retries, provider selection, PR submission, patch-and-re-review cycles. The deterministic part is observable in principle but is currently buried in glue code. Instrumenting it (typed schemas + versioned artifacts) turns the system from *"ships code most of the time"* to *"ships code with measurable confidence."*

## Why this lands hard for Dark

Dark's pdd-thinking corpus is heading toward exactly the same problem:

- Agents materialize Dark code (PDD).
- Capabilities are checked, conflicts dispatched, events emitted.
- Every op carries `account_id`; every cap-check goes into `capability_log_v0`; every conflict resolution emits `ConflictResolved`.

This is **trace-as-log**: a complete record exists. But the question Greten asks — *"across 263 runs, what's the failure rate of this phase?"* — is **trace-as-database**, and Dark's current design doesn't yet make that easy.

**The substrate is being designed to record the data; nothing yet queries it.**

## What Dark already has in this neighborhood

- **`capability_log_v0`** (CAPABILITIES §"Schema") is already designed as queryable. Its audit-pivots section literally lists Greten-style questions: *"what caps does my agent actually use?"* / *"what cap-grants would unblock current work?"* / *"who recently exfil'd what?"* This is the right shape; the question is whether *every* substrate event gets the same treatment.
- **Traces** — every fn call recorded. Cardinality is huge; query surface is small.
- **`ConflictResolved`** events — emitted on every conflict resolution, carry resolution kind, author, timestamps. Queryable in principle.
- **`MaterializeAttempted` / `RefineRequested`** — planned events from EVENT-STREAMS-AND-PARKING, exactly Greten's *"phase outcomes."*

The pieces are present in the design. What's missing is the **observability layer** that turns them from emissions into answers.

## Three concrete moves the post argues for, in Dark terms

### 1. Phase outcomes as first-class typed events, not log lines

Greten's Ruby pipeline printed *"failed to submit PR"* but didn't emit a typed `PRSubmissionAttempted { outcome, error_kind, duration_ms, cost_usd }` event. The fix: every phase wraps its outcome in a typed event, recorded immutably, tagged with session ID.

**Dark equivalent.** Every materialization, every cap-check, every conflict resolution should emit a typed event with **outcome metadata** — success/error_kind/duration/cost — not just the "something happened" signal. The agent-side runtime should default-instrument; the substrate should store; the viewer should aggregate. CAPABILITIES already sketches this discipline for caps; PDD needs the same discipline for materialization + refine cycles.

### 2. Move decision logic out of glue, into the queryable layer

Greten's 268-line Ruby provider-resolution class was replaced by TypeScript extensions whose **inputs and outputs are themselves swamp artifacts**. The decision becomes queryable: *"for this prompt shape, which provider was picked? How often did each choice lead to a successful patch round?"*

**Dark equivalent.** Any decision that currently lives in agent-prompt-string or F#-side orchestration should be expressible as a Dark fn whose calls are traced. The viewer can then ask *"for materializations where the agent picked strategy X, what's the success rate vs strategy Y?"* The substrate already records every fn call; the move is to push more **decisions** through fn calls — instead of letting them implicitly happen in prompt text or F# `if`/`match` blocks where the trace is invisible.

This is also a forcing function for COHABITATION's *"intent as first-class"* claim — decisions are how intent operationalizes, and they need to be the same shape humans and agents both produce.

### 3. Measurable hypotheses

Greten ends with an open question — *"does low ideation confidence predict build failure?"* — that is now **answerable** because both signals are typed + queryable. Before swamp, the question wasn't even askable.

**Dark equivalent.** Every speculative design in pdd-thinking (*"cap denials should route to owner with sub-second latency"*, *"refine cycles converge in ≤3 rounds for X% of cases"*, *"hot-reload latency stays under 100ms for Y package-graph depth"*) becomes a measurable hypothesis once the substrate is instrumented. Today these are validated by feel; with cap-log + materialization-log + conflict-log + event-bus all queryable, they're validated by query.

This is the **strongest argument for landing CAPABILITIES Phase 1 before PDD Phase 1** as the ROADMAP already says — the audit infra is also the **iteration infra**. Greten's post is a real-world reminder that without it, you can't tell whether you're improving.

## The honest tension

Greten's case study is also a warning: **swamp's instrumentation took real work.** The 268-line Ruby class didn't move itself. Dark would face the same cost. The substrate roadmap already plans for it (`capability_log_v0` is in scope before PDD ships), but the post is a useful nudge that *every* agent-touching event needs the same care — not just caps. EventBus + parking get this right by construction (typed events on a bus, recorded as they go); the discipline is to keep PDD on the same rails when it lands rather than adding a side-channel of untyped log lines.

There is also a temptation to skip this for v0 *"because we'll just look at the code."* Greten's whole point is that at scale (263 runs), looking at the code stops scaling — and the moment you wish you'd been recording, you've already lost the data.

## What "visibility" should look like, Dark-flavored

The viewer (per VIEW-SKETCHES) is the natural home. A "telemetry strip" alongside the existing sessions strip:

- **Per-session.** Last N agent sessions, with materializations attempted, successful, refined, abandoned. Click to dive in.
- **Per-cap.** Top-cap-uses, top-cap-denials, latency on grant prompts.
- **Per-conflict.** Conflict kinds by frequency, by resolution outcome, by author.
- **Per-fn.** *"This fn was materialized 47 times across 12 sessions; success rate 85%; mean refine-rounds 1.3; CapInvokeLLM cost ~$0.12 per attempt."*

All of these are queries against tables that the substrate plan already includes or implies. The work is connecting the wires and shipping the view.

## The headline

Greten's post is the **case study Dark's roadmap is implicitly arguing for**. The substrate ethos already says *"show you what's happening, by default"* (COHABITATION). The substrate plan already includes a cap-audit log. The open work is to make **every phase of every agent's work** observable on the same query surface — not just caps, not just conflicts, but materialization outcomes, plan revisions, refine cycles, the whole pipeline.

The pdd-thinking docs should call this out explicitly: **instrumentation is not an optional epic; it is the difference between a black box and a substrate.**

If Dark ships PDD without it, Dark ships exactly the system Greten spent his post arguing against.

---

**Sources**

- https://matgreten.dev/posts/visibility-into-the-black-box/
- https://swamp.club/
- `pdd-thinking/CAPABILITIES.md` (§Schema, `capability_log_v0`)
- `pdd-thinking/CONFLICTS-AND-RESOLUTIONS.md`
- `pdd-thinking/EVENT-STREAMS-AND-PARKING.md`
- `pdd-thinking/COHABITATION.md`
- `pdd-thinking/VIEW-SKETCHES.md`

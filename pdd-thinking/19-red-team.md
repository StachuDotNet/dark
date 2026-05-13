# 19 — Red Team

> What could go wrong with this spike — design problems, implementation footguns, project failures. Watch list.

## Design-level risks

### R1 — The trace is a fig leaf, not a program

**Claim under risk:** "the trace is the program."

**What could be wrong:** if traces aren't actually replayable — if too many things depend on nondeterministic context not captured in the trace — then the "trace is the program" claim is rhetoric. Replay would diverge wildly run-to-run, making traces less than useful.

**How to test:** Day 5-7, after Demo 2 runs reliably, take a trace and replay it 10×. Compare outcomes. If 9/10 match, fine. If 5/10, the framing is broken.

**Mitigation:** ensure every nondeterminism source (LLM call, time, random, network) records its result in the trace and replays from cache.

### R2 — Tolerance hides bugs in your code, not just hallucinations

**Claim under risk:** "the runtime is tolerant."

**What could be wrong:** in `--tolerance loose` mode, your own logic bugs get papered over too. A division-by-zero in *your* Dark code returns 0 instead of crashing. You think the program works; it's silently wrong.

**How to test:** run the test suite in strict mode regularly. If the same test passes loose but fails strict, that's the smoking gun — you've been hiding behind recovery.

**Mitigation:** the *default* policy for any RTE that *isn't* a materialization-failure should remain `KillFrame` even in loose mode. Tolerance is for *missing* stuff, not for *wrong* stuff.

### R3 — Sig consensus is constantly broken in practice

**Claim under risk:** "types are the coordination protocol."

**What could be wrong:** if the LLM-generated sig disagrees with the find-result sig disagrees with the call-site expectation 30%+ of the time, the consensus system thrashes.

**How to test:** instrument `materialize_done` events with the sig actually selected; track first-wins vs would-have-been-rejected ratios.

**Mitigation:** Strategy B (constraint-driven) becomes urgent. Have the design ready to deploy if Strategy A is too noisy.

### R4 — The spike succeeds but feels bad

**Claim under risk:** the paradigm itself.

**What could be wrong:** Demo 6 runs end-to-end. Stachu uses PDD for a real task. It works. But it feels *wrong* — uncomfortable, unpredictable, hard to debug, anxiety-inducing.

**How to test:** notice the feeling. Don't override it.

**Mitigation:** **most valuable failure mode.** Write up *why* in detail. Probably indicates a missing primitive (better debugging? better trace UX? different default policy?). Pivot.

## Implementation-level risks

### R5 — F# match exhaustiveness will eat 2+ hours of Day 1

**Why:** adding `Pending` to `FQFnName` forces ~74+ match sites in LibExecution alone to update, plus another ~50 across Builtins, Tests, etc.

**Mitigation:** use the `failwith "TODO pending"` template across the board, mechanically. Don't think about each one. Compile, iterate, until green. *Then* go back and replace the failwiths with reasonable handlers (most of them: just propagate or ignore).

### R6 — Two-pass build forgotten, mystifying errors

**Why:** `package-ref-hashes.txt` is embedded. Type changes invalidate it; the first build can produce a stale-hash error.

**Mitigation:** *always* `touch backend/src/LibExecution/package-ref-hashes.txt` after any type change to RT/PT, then rebuild. From the memory: this is `feedback_dark_type_change_two_builds.md`. Don't forget.

### R7 — `Ply` cancellation is subtle

**Why:** the spec says "cancel the loser of the race," but `Ply` doesn't model cancellation natively the way `Task` does. If we bridge through `Task` with `CancellationTokenSource`, we have to be careful about what's actually cancellable.

**Mitigation:** for the spike, *don't actually cancel*. Just ignore the loser's result. The wasted work is real but not catastrophic; budget is 1 second per path, so worst-case waste is 1 second of background CPU per pending. Real cancellation comes later.

### R8 — The "find" path is slow because the package store has no name index

**Why:** existing `PackageManager.getFn` is by-hash. Looking up by name requires either a name→hash index (might not exist) or scanning all rows.

**Mitigation:** for v0, skip find. Generate-only. Add a `pdd_pinned_fns` table later that maps name → hash for the user-promoted ones; that's a faster path.

### R9 — `Dval.defaultFor` is wrong for custom types

**Why:** for `TCustomType("MyRecord", ...)` we'd need to look up the package type's fields and default each. The PoC just returns `DUnit`, which type-checks as nothing.

**Mitigation:** accept that demos hitting `defaultFor` on custom types will be wonky for v0. The headline demos (`addOne`, `fib`, `stock-variance`) use only Stdlib types, so we sidestep this. When custom-type defaults hurt, *then* solve it.

### R10 — The LLM returns syntactically-invalid Darklang

**Why:** even with the v3 prompt, gpt-4o-mini gets it wrong ~25% of the time per tonight's tests.

**Mitigation:** retry-with-AST-feedback loop. After the first generate fails to parse, re-prompt: "Your previous output had these syntax errors: …. Please retry." Costs 2× per attempt but should halve the failure rate.

## Project-level risks

### R11 — The branch becomes a tarpit

**Symptom:** after 2 weeks, the branch has 200 commits but Demo 6 isn't green. You can't tell what's broken vs what's WIP.

**Mitigation:** **revisit `15-spike-budgets.md`** every 3-4 days. If Day-3 health check fails, *stop adding features* and figure out what's structurally wrong.

### R12 — Demo cadence vs. design cadence diverge

**Symptom:** you've shipped Demos 1-3 but they take so long to set up (config flags, capability grants, etc.) that adding Demo 4 means re-doing 90% of the harness work.

**Mitigation:** invest in a *demo runner* early — Day 3-4 — that takes a `.dark` file path and runs it with sane defaults. `dark pdd run <path>` is the contract.

### R13 — Burnout

**Symptom:** the spike is no longer fun. You're forcing iterations.

**Mitigation:** *stop*. Take a day. Re-read the five claims. If they still feel right, come back. If not, write the postmortem.

### R14 — Cherry-picking is harder than rewriting

**Symptom:** at end of spike, you decide to merge useful bits to `main`. But every file has cross-cutting concerns and you can't cleanly extract just one piece.

**Mitigation:** at end of spike, **plan to rewrite from scratch on a new branch.** Use the spike's notes + commits as a reference, but don't try to surgically extract. Cleaner result, less integration risk.

## "We should have known" risks

The really painful ones — they look obvious in retrospect:

- **Async/concurrent code in F# is fundamentally trickier than the spike will assume.** Plan for 2× the time on the scheduler / parking work.
- **The LLM will hallucinate stdlib fns that look real.** `Stdlib.List.contains`, `Stdlib.String.split` etc. — verify each post-materialization.
- **JSONL grows fast.** A 10K-call trace is ~3MB. Plan for rollup early.
- **Type inference across speculative materializations is research-level.** Avoid it for the spike — rely on call-site explicit types.
- **Streaming LLM outputs vs. batch return.** OpenAI returns the full message at once; streaming costs the same. Don't optimize for streaming early.

## Smoke detectors to wire up Day 4

After Day 1-3 plumbing works, add these to your local feedback loop. Each is a one-liner.

```bash
# Are we leaking $$ to expensive models?
grep '"model": "gpt-4' rundir/traces/*.jsonl | grep -v 'gpt-4o-mini' | head

# Are we recovering more than we're succeeding?
jq -r '.ev' rundir/traces/<id>.jsonl | sort | uniq -c | sort -rn

# Trace getting huge?
ls -lh rundir/traces/ | tail

# How much have we spent?
jq '[.[] | select(.ev=="cost") | .["$"]] | add' rundir/traces/*.jsonl
```

(Last one assumes you're writing one JSONL per session and using `jq` over the array; tweak to your actual format.)

## A note on premature optimization

A *lot* of what's listed above will tempt you to over-engineer. **Don't.** Build the simplest thing first. The risks above are *known unknowns* — anticipate them, don't preempt them.

The spike's job is to discover which risks are real. Pretending you know already is the most common research mistake.

---

## Connection to other docs

- `15-spike-budgets.md` — budget guardrails complement these risks.
- `11-open-questions.md` — actual open questions, not just risks.
- `18-minimum-viable-spike.md` — defensive smallest-cut for when risks materialize early.

# PDD Feedback — Processing Checklist

Loop-driven consolidation queue. Each bucket is one iter (~10 min).
The raw feedback is preserved per-bucket so motivations stay visible.
Original feedback (verbatim): `feedback-original.md`.

## Status

**NEXT:** `B5` — Write FRONTIER.md (consolidating speculative items + source-code thoughts)

_Note: reordered after B3 so CLAIMS/ALGORITHM/FRONTIER exist before B6 wants to absorb DESIGN sections into them. New order: B4=extract → B5=FRONTIER → B6=DESIGN+CLI-REF absorb → B7=WRAP-UP final → B8=archive tidy → B9=verify._

## Snapshot — before consolidation

| | files | LOC |
|---|---|---|
| Top-level | 7 | 2098 |
| archive/ | 15 | 2559 |
| archive/reflection-layer/ | 5 | 1506 |
| **Grand total** (excl feedback) | **27** | **6023** |

## Target — after consolidation

5 working files + feedback driver:
- `README.md` — slim entry, what's in code
- `WRAP-UP.md` — consolidated learnings, integration plan, slimmer
- `CLAIMS.md` — extracted claims doc (reframed per feedback)
- `ALGORITHM.md` — extracted algorithm sketch, high-level, marked **incomplete**
- `FRONTIER.md` — all speculative + source-code-changes thoughts
- `feedback.md` — this file (loop driver; historical record)

`DEMOS-AND-BUDGETS.md`, `EMPIRICAL.md`, `DESIGN.md`, `PDD-CLI-REFERENCE.md`, `REAL-PACKAGE-FNS.md` get absorbed into the above and deleted.

`archive/` prunes to just what's not captured elsewhere.

## Loop protocol (for the next iter)

1. Read this file. Find `NEXT:` line.
2. Read the bucket section below; do all unchecked items.
3. Tick items as completed.
4. Update the `NEXT:` line to point at the next unchecked bucket (or set to `DONE`).
5. Commit with a short message naming the bucket.
6. Schedule next wake (10 min) — or stop if `DONE`.

---

## B1 — Setup

- [x] Snapshot LOC + file count (top-level + archive)
- [x] Backup raw feedback → `feedback-original.md`
- [x] Propose target structure (above)
- [x] Restructure feedback.md as bucket checklist
- [x] Initial commit

## B2 — Pure deletions across top-level

Low-risk cuts. Each is a delete or one-line replacement.

- [x] **README.md**: kill "heavy-hitters status" table section
- [x] **WRAP-UP.md**: remove "anti-pitch: don't say 'Copilot for runtime'..." (no "anti-vision" mentions)
- [x] **WRAP-UP.md**: kill the "Terminology (pinned)" glossary block
- [x] **WRAP-UP.md**: kill "See also (deep-dives still in this directory)" section
- [x] **DESIGN.md**: kill §11 Glossary entirely
- [x] **DESIGN.md**: kill "anti-glossary" sub-block
- [x] **EMPIRICAL.md**: kill §1 (v1→v4 prompt iteration history — user doesn't care about v1/v2/v3)
- [x] **EMPIRICAL.md**: kill §5's project-level R11-R14 + smoke detectors sub-section
- [x] **EMPIRICAL.md**: kill §6 (pithy line worth keeping)
- [x] **DEMOS-AND-BUDGETS.md**: kill §2 "Acceptance summary" through end of file
- [x] Grep for "by 8am" / "by Day N" / "tonight" style time markers; remove
- [x] Grep for any remaining "anti-vision" / "anti-pitch" / "anti-X" framing; remove
- [x] Commit: "trim — pure deletions per feedback"

Raw feedback motivating these:
> don't mention "anti-vision" anywhere
> remove any mentions of like "by 8am" — we're beyond the spike
> I hate glossaries, remove
> I'm not a huge fan of "connection to other docs" sections
> in README.md, I think we can kill the heavy-hitters status section
> in EMPIRICAL.md, I don't care about the project-level section, or the smoke detectors section, or pithy line
> in DEMOS-AND-BUDGETS.md: "acceptance summary" and later are all garbage, delete
> I don't care about v1/v2/v3 of the system prompt. or generally, any history of how our research on this topic has gone

## B3 — Small-file trims (README, EMPIRICAL, DEMOS-AND-BUDGETS)

After B2 these are mostly empty. Decide: keep as slim files, or absorb-and-delete?

- [x] **EMPIRICAL.md**: deleted. Failure-mode R1-R10 and a few open Qs can be re-extracted from git if needed in B6 (FRONTIER). The cost numbers and v4-prompt findings are noise per feedback.
- [x] **DEMOS-AND-BUDGETS.md**: deleted. Live demos already in README's "Demos verified live" + "Demos that still trip"; aspirational demos (Demo 5/6/7) skipped per feedback to skip "spike-budget" framing.
- [x] **README.md**: removed "Hard rules" (repo-local context, not deliverable); updated "How to enter" pointer list to the target 5-file structure.
- [x] Commit: "absorb EMPIRICAL + DEMOS into WRAP-UP/README; slim README"

## B6 — DESIGN.md + PDD-CLI-REFERENCE.md (absorb/delete)

- [ ] **DESIGN.md**: redistribute remaining sections
  - §1 Vision → already in CLAIMS (deferred to B5)
  - §2 LibExecution changes → kill per feedback ("LibExecution shouldn't know about PDD") + extract to FRONTIER as "what F# changes would need to happen"
  - §3 Find vs Generate scheduler → ALGORITHM.md (B5)
  - §4 Sig consensus → ALGORITHM.md
  - §5 Tolerant runtime → FRONTIER (it's design-only; needs more thought per feedback)
  - §6 Capabilities → FRONTIER, framed as "MUST happen before PDD" per feedback
  - §7 HITL → FRONTIER, rewritten per feedback ("not really a fallback materializer")
  - §8 Tracing → FRONTIER, reduced surface area per feedback
  - §9 HTML view → FRONTIER, "should be served via dark" per feedback
  - §10 EventSink → FRONTIER, "lower-level concept; event graphs with waiters"
  - §12 5-claim summary → CLAIMS
- [ ] Delete DESIGN.md
- [ ] **PDD-CLI-REFERENCE.md**: most commands killed per feedback (`pdd run`, `pdd demo`, `pdd cache`, `pdd promote`, `pdd history` should be auto / part of SCM). Delete file; surviving commands (`dark prompt` + whatever) go into README as a small block.
- [ ] Commit: "absorb DESIGN + CLI-REF; both deleted"

Raw feedback motivating these:
> the real implementation of materialization and such should really be fully written in Dark... lower-level F# core stuff will need to change to allow for this
> builtin permissions/capability should be done before starting the real PDD efforts. and the document should be framed as such
> "human in the loop" needs more thought — not really a "fallback materializer"
> Tracing.fs has been changed quite wildly. I think we need fewer changes, with exposure via builtins. reduce surface area
> we don't actually want so many 'pdd' commands — we want something more like a claude code experience, but more interactive
> hate 'pdd run' and 'pdd demo' and 'pdd cache' commands. should be auto
> 'dark pdd promote' shouldn't exist — should just be part of "normal" SCM flow
> same for dark pdd history. PDD is "considered" as we build baseline SCM but it's also optional
> the "html view" should really be written in dark, served via dark
> event streams should be a lower-level thing in LibExe as well
> LibExecution shouldn't know anything about PDD

## B4 — Extract CLAIMS.md and ALGORITHM.md

- [x] Write **CLAIMS.md** with all 5 claims reframed per feedback + 60-sec pitch
- [x] Write **ALGORITHM.md** (high-level, marked INCOMPLETE, recursive-coding-agent framing, coordinator + strategies + parking + conflicts sketches)
- [x] Remove claim duplication from README, WRAP-UP, DESIGN
- [x] Replace duplicated content with one-line link to CLAIMS / ALGORITHM
- [x] Commit: "extract CLAIMS.md + ALGORITHM.md; reframe per feedback"

Raw feedback:
> we should extract the core claims (e.g. source is lazy) to its own document, and remove from others. lots of duplication currently, and some conflicts
> reframe "the source is lazy" to "the source often starts as lazy. gradually existent, expanded, typed"
> "the trace is the program" is really overstated. The program is still a growing set of package items, and the traces are used in conjunction with the interpreter and agent to turn the initial prompt gradually into working software
> "human in the loop" needs more thought — not really a "fallback materializer" — the human is useful for review, initial prompting and spec-adjustments, review of initial types, writing code, etc.
> the algorithm idea should be extracted to its own document, with notes that it's incomplete. we're just building a recursive coding agent... Find and Generate are some strategies, but we'll need more
> the pseudocode we include, anywhere, should be higher-level and not very F#y
> elevator pitches are nice. could tighten a bit. just keep that doc to just the pitch(es) though, without "what I'm not claiming"

## B5 — Write FRONTIER.md

The consolidated "things to think about / build" doc. Pull from current docs and the feedback's "misc notes" list.

- [ ] Sections to include:
  - **Capabilities first** — must precede PDD; PDD hooks into capability requests throughout
  - **Conflicts + resolutions** — low-level LibExe concept; default resolutions; some scenarios fail, others park-and-wait
  - **Event streams (graphs)** — low-level concept; waiters; agent infra + SCM sync rest on top. 404 events for unresolved names that get evaluated.
  - **Composable MVU apps infra** — whatever it means; cross-ref relevant notes
  - **Recursive live development** — not just fns; AI, notes, types, values, traces; full-stack
  - **Refactors in the lang** — built into PDD process
  - **Done-ness tracking** — idea → name → sig → body → tests → connected code → description; iterate until "feels good"
  - **WIP refs by location**, not new ID concept (simplification)
  - **WIP → committed**: update refs to hash for long-term stability
  - **WIP separated from committed**, syncs with branch ops / package ops.
    Open tension: WIP doesn't *need* to get synced — keeping it
    unsynced sidesteps a lot of op-semantics questions, *as long as*
    WIP is stored separately from ops. But what if you want to share
    your WIP with yourself on another machine, or with a coworker?
    Then you need some sync story — maybe lightweight (gist-like
    snapshots), maybe full ops semantics. Worth thinking through.
  - **Speed benchmarks** — searching dark matter, drafting v0
  - **Prompt as low-level pinned type** (`Prompt`) — not in F#, in Dark
  - **Search-by-type** + surrounding values; supports agent
  - **`dark prompt` as daemon** — thread id, watcher, viewer; beautiful + customizable
  - **Coordinator** — super-core; sketches needed
  - **Dark interpreter in Dark** — default one + fancy expanding one
  - **Hot-reload from first principles** — tight section
  - **HTML view in Dark, served by Dark**
  - **darklang.com/gradual** — sketch content (maybe in vault)
  - **Highest-level fn in focus view** — sketches at various points in time; "see what's going on"
  - **Re-eval until results feel good** — keep faking impl and "continuing" traces
  - **CSV example: implicit early extraction** of csv as value/file
  - **Each eval is separately debuggable as it's going**; traces can be replayed + debugged
  - **F# substrate changes needed** to allow Dark-hosted materializer:
    - LibExecution shouldn't know about PDD
    - Tracing surface area reduced; exposed via builtins
    - SQLite-only state (no JSONL sidecars); UserDB-like construct
    - Conflicts/resolutions + event streams as low-level LibExe primitives
- [ ] Commit: "write FRONTIER.md — speculative + source-code thoughts"

Raw feedback (heavy):
> an unresolved/unfound name that we try to run the body of should likely yield some sort of 404 'event' in some broad 'event stream'
> on a conflict of "doesn't exist," currently we (usually) just fail. we need to build our conflicts+resolutions system better
> the conflicts and resolutions system should be trickled throughout the whole of LibExecution
> speaking of low-level concepts, event streams should be a lower-level thing in LibExe as well
> are event _graphs_ a thing? with waiters, etc.
> this system should all sit on some 'composable MVU apps' infra
> the real point here isn't just fns - it's recursive live development, powered by and integrated with AI, notes, types, values, traces
> write somewhere: "need to build refactors into the lang/system, and involve them in this process somehow"
> darklang.com/gradual should exist. maybe sketch the content somewhere
> we should track "done-ness" of some fn... at first just an idea, eventually has signature, body, tests, connected code, description. we iterate on it all until it feels good
> maybe we don't need a new ID concept... WIP could refer to other things by location
> when WIP becomes real/committed, we should update references to be by hash
> we should do a better job of separating WIP from committed stuff. while also considering we need to sync branch ops, package ops, etc.
> searching for dark matter needs to be SO fast. drafting v0 of ANY code needs to also be SO fast. we need benchmarks
> prompts need to be some kinda low-level concept we respect (not in F# but lower-level darklang code — Prompt is a special/pinned type)
> more stuff like "search for values by type" will likely be involved
> dark prompt <text> starts a _daemon_ of a PDD recursive coding agent. and we watch it. spawn it and get back a thread id, start a watcher and some viewer
> we need to make that beautiful, with minimal code and very customizable per-user and environment
> the coordinator or whatever you called it - that's super core
> I suspect we'll need to write a Dark interpreter in Dark before long
> idk how hot reloading applies to all of this. think from 'first principles' towards the end, add a .md, tight
> this whole system should work on ops, conflicts, resolutions
> the "html view" should really be written in dark, served via dark
> somehow, the user should _see_ the highest-level fn, in focus, as parts of it are being filled in
> basically, we want to re-eval until the results feel good. keep faking impl and "continuing" traces
> our csv example, after the prompt, should likely _know_ somehow (implicitly) to extract the csv as a value or file early on
> where somewhere: "each eval is separately debuggable, as it's going. and traces can be replayed+debugged"
> I hate the jsonl sidecar. all should exist in the sqlite .db, one way or another (raw sql tables, or more likely a UserDB or other UserDB-like new construct)
> Tracing.fs has been changed quite wildly. I think we need fewer changes, with exposure via builtins

## B7 — WRAP-UP.md final pass

After CLAIMS + ALGORITHM + FRONTIER exist, WRAP-UP should be much smaller — just the spike-end retrospective + integration plan, with claims/algorithm/frontier content removed.

- [ ] Remove all 5-claim repetition (point to CLAIMS)
- [ ] Remove algorithm-shape descriptions (point to ALGORITHM)
- [ ] Remove big-picture / frontier sections (point to FRONTIER)
- [ ] Remove "F# → Dark roadmap" (now part of FRONTIER)
- [ ] Trim the integration plan — calm down on `dark pdd` command-by-command listing; commands per feedback are killed
- [ ] Remove the "Hard rules" section (this is repo-local context, doesn't belong in a deliverable doc)
- [ ] Remove the CLI Reference at bottom (per B4 — most commands gone)
- [ ] Keep: what got built, what worked, what didn't, surprises, decisions to lock, 3-wave merge plan, prioritized TODOs, what we'd do differently, what spike did/didn't answer, closing time-horizon table
- [ ] Commit: "WRAP-UP final pass — de-dup with CLAIMS/ALGORITHM/FRONTIER, trim"

## B8 — Archive tidy

Archive has a lot already absorbed into WRAP-UP. Prune.

- [ ] `archive/reflection-layer/` — these 4 docs (SPIKE-LEARNINGS, INTEGRATION-PLAN, BIG-PICTURE, F-SHARP-TO-DARK) were absorbed into WRAP-UP per the consolidation a week ago. Delete the subdirectory entirely.
- [ ] `REPORT-state.md`, `REPORT-thoughts.md`, `REPORT-overnight.md`, `REPORT-morning.md` — session reports, all absorbed. Delete.
- [ ] `SESSION-2-REPORT-2026-05-13.md`, `CODING-LOOP.md` — historical session notes, absorbed. Delete.
- [ ] `SCM-INTEGRATION.md` — captured in CLAIMS/ALGORITHM/code. Delete.
- [ ] `09-carving-the-codebase.md`, `10-day-1-hacking-plan.md`, `13-libpdd-materializer.md`, `17-day-1-quick-reference.md`, `18-minimum-viable-spike.md` — early planning, all absorbed. Delete.
- [ ] `FINAL-REPORT-2026-05-13.md` — per feedback:
  - Remove For + TLDR opening
  - Extract claims → already in CLAIMS via B5
  - Extract algorithm → already in ALGORITHM via B5 (feedback liked this wording)
  - Remove LibExe changes section
  - Remove everything after "find vs generate — the scheduler"
  - Whatever remains: delete (per "eventually consolidate into other doc(s)")
- [ ] `20-elevator-pitches.md` — keep, tighten:
  - Remove "what I'm not claiming" / anti-pitch content
  - Keep just the pitches
  - Move to top level as `PITCHES.md`? Or leave in archive? Feedback says "could tighten" not "move."
  - Leave in archive, tighten.
- [ ] Update `archive/README.md` to reflect what remains
- [ ] Commit: "archive tidy — prune absorbed docs"

## B9 — Verify + final report

- [ ] Re-run snapshot (top-level + archive)
- [ ] Compute before/after delta (files, LOC)
- [ ] Update this file's status to `DONE`
- [ ] Append "After" section to this file with final numbers
- [ ] Final commit: "consolidation done — N files, M LOC (from 27/6023)"
- [ ] Propose to user: push branch?

---

## After consolidation

*Filled in by B9.*

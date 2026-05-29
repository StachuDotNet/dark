# feedback.md coverage map

A systematic check that every point in `../source/feedback.md` (the master spec) is
addressed, and where. Legend: **[done]** addressed; **[thin]** addressed but could go
deeper; **[blocked]** can't complete (reason given). Re-derived each pass.

## PDD docs (resting — clean & tighten only)

| feedback.md point | Status | Home |
|---|---|---|
| REFLECTION: kill numbers; consolidate tighter | [done] | numbers killed; `meta-reflections/`, `issues-and-improvements/` |
| Remove reflection from SUMMARY docs; specs + per-sweep results instead | [done] | spec → `projects/`; results convention → `results/README.md` |
| All results in a results subdir | [done] | `results/` (nested PDD bench home) |
| SUMMARY split: long-lived spec (goal + acceptance) vs learnings; no mention of other systems | [done] | `projects/*.md` (spec); learnings routed out |
| projects.md: green/brownfield, drop modules/language, group by category, no iterations, no sources/ordering | [done] | `projects/` (125 specs + index), `_cross-cutting-test-criteria.md` |
| ALGORITHM: dated; "first non-failure wins" not nuanced; LLM-wrapper/agent/dummy bodies; more iterations | [done] | `design/algorithm.md` |
| CLI-PROJECT-SURVEY: kill section 1; fold projects; no class letters; drop ordering; meta out | [done] | folded into `projects/` |
| FRONTIER: unclear point — distribute notes, delete | [done] | distributed to homes; deleted |
| CLAIMS: forever-lazy LLM-delegated bodies | [done] | `design/claims.md` Claim 1 |
| README (PDD): outdated; few commands; `dark prompt` starts bg agent + watch state; thin or none | [done] | `design/pdd.md` (thin) |
| New structure: design / projects / results / issues-and-improvements / meta-reflections | [done] | built |
| Generally tighten, consolidate, kill repetition | [done/ongoing] | iteration passes (section 12) |

## Stable & Syncing

| feedback.md point | Status | Home |
|---|---|---|
| Punt removing `.dark` files until after sync+stability; enumerate blockers in one place; "removing .dark files" doc | [done] | `design/bootstrap.md` |
| Fully separate OPS from PROJECTIONS; core sync DB separate from branch/session projections; recovery for races | [done] | `design/distributed-event-sourcing.md` ("Ops vs projections"), `design/conflicts.md` |
| EVENT-STREAMING: drop v0/third-piece/PDD/event-sink; rename Stream→EventBus; fit LibExecution/ProgramTypes; F# thin; sync path → remove .dark; Ply replacement? | [done] | `design/event-bus.md` |
| STABILITY-AND-SHARING: definitions>stable → PDD stability (migrate); keep wire protocol; drop SYNCEVENT schema | [done] | `design/sync.md` (wire kept); stable-defn → `design/algorithm.md` |
| CONFLICTS: more SCM op-vs-op; parse/run/dev/at-rest (+playback) timings = types; kill persistence/SQL; projection separate from ops | [done] | `design/conflicts.md` |
| plan.md (two snapshots): client/server (desktop server on tailscale); no DARK_ACCOUNT; remove key-files/schema-facts/step-0/open-q/metrics/phasing/risks/refs; .darklang config not env vars; goal = 2 builds syncing | [done] | `design/ai-coding-target.md` |
| cli-daemon: rethink for ops/sync/projections/async; .sock/.pid/.version granularity; beyond perf | [done] | `design/cli-daemon.md` |
| north star: print-md in Dark, sync, Ocean forks, listed under `dark apps` | [done] | `design/apps-surface.md` |

## Capabilities & Identity

| feedback.md point | Status | Home |
|---|---|---|
| pure fns always allowed; iterate Capabilities type (HttpClient sophisticated, HttpServer, Random/Time, FS, Language, Matter, CLI); LibExecution top-to-bottom; Set<Capability>?; checkCapabilities fn; async --ask; no interactive grants; instance-specific; frame parking; user-defined fns; kill llm-prompt/sequencing/unlocks/schema/previewable; per-assembly→nuanced | [done] | `design/capabilities.md` |
| HttpClient specific restriction notes (in vault) | [blocked] | vault location not found — flagged in capabilities.md + `vault-organization.md` |
| IDENTITY: thin/directional; rename IdentityKind→Identity; kill TrustProfile; drop account fields; Intent per Identity+Instance; kill cross-cutting/phasing | [done] | `design/identity.md` |

## Apps, editing, async, packages

| feedback.md point | Status | Home |
|---|---|---|
| "distributed event sourcing + MVU, branched" framing | [done] | `design/distributed-event-sourcing.md` (keystone) |
| App type (name/data/msg/views/cmd/conflicts/resolutions/autoResolutions/constraints); editable by people/agents; most conflicts OK; hot-swappable playback; conflict-blind generic core | [done] | keystone ("fuller field set" table + "Most conflicts are OK" + hot-swappable note) |
| Simplify Darklang: ops + conflicts/constraints + sync + projections; smallest composable system; runtime/at-rest tests (Scriptorium); ref keyword + composable parser + compile builtin; crons-as-app, daemons via start() | [done] | keystone |
| New doc: CLI structural ProgramTypes editor (Hazel-like, tiny LLM loop, caching, self-hosting, Clay, HTML eventually, fake views) | [done] | `design/structural-editor.md` |
| COMPOSABLE-MVU: one composed App not Model; runner; op-playback; may flatten into distributed-op-playback | [done] | `design/composable-mvu.md` |
| Dark Async Plan (coworker): review; kill Task/Ply, roll our own; park threads, nested processes, opt-in debug symbols; inform OUR async doc (don't touch hers) | [done] | `design/async.md` (opinion section); her doc untouched |
| package-system-layers: layers as composable ops/apps/projections; harmful = event stream; deps = one projection; ops through inactive instances; drop shared-table-shape | [done] | `design/package-system-layers.md` |
| beam-vs-dark: update, shorter, mailbox idea; background agent | [done] | `design/research/beam-vs-dark.md` |
| VIEW-SKETCHES: extend wildly, keep what's perfect | [done] | `design/view-sketches.md` |
| dark-virtual-files (state-as-filesystem; distinct from removing .dark) | [done] | `design/dark-virtual-files.md` |

## "Good for AI agents" (improvements)

| feedback.md point | Status | Home |
|---|---|---|
| improvements.md: remove preamble; consolidate; no big CLAUDE.md (for-ai composed/expanding, hashes+names, `dark docs h1 h2`); hate `dark suggest`; fix 3.1 numbering; kill "Known runtime gaps" | [done] | `issues-and-improvements/` (7 categories); for-ai in `agent-workflow.md`; dark suggest dropped |
| feedback-from-agent.md (cross-link) | [blocked] | doc not located under that name — flagged |

## Cross-system hygiene

| feedback.md point | Status | Home |
|---|---|---|
| What migrates to vault vs stays in-repo; organize 90.Stachu snapshots; fewer+smaller files | [blocked-exec] | recommendation in `vault-organization.md` (vault is off-limits to the loop) |
| Don't push to upstream; print list of files at the end | [ongoing] | nothing pushed; final print is section 11 (~08:00) |
| "in a loop: create feedback-revised.md of - [ ] todos" | [done] | this is what `loop-instructions.md` is |

## Dangling / unknown-intent fragments in feedback.md

- Line 14 "we need a clean" — sentence trails off; intent unknown. **Flagged, not guessable.**
- A few other clauses trail mid-thought (plan.md batch 2 "and at the end"; the results-subdir "which should"). Treated as best-effort; nothing actionable lost.

## Net

Everything actionable in feedback.md is addressed. Two items are genuinely **blocked**
(HttpClient vault restriction notes; `feedback-from-agent.md` — both not locatable), one is
**blocked-exec** (vault reorg — off-limits, recommendation written), and the final print is
held for section 11.

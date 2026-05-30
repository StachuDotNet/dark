# Wave 2 — spec the path from `main` to "print-md stable & syncing"

This is the **living worklist** for the 24h loop. It is a reorganized form of
`more-feedback-raw.md` (frozen original); all content from there is preserved here,
reordered into a dependency-respecting processing order.

## The goal

Spec **everything** between `main` and *"the `print-md` script is stable and syncing
across various Dark instances."* Come Sunday, Stachu does a final review, adjusts a
few small things, and says "go" in a fresh session against the spine doc to start
**pre-S&S** work — and should return confident there's a real plan to print-md sync.

**The spine:** `stable-and-syncing/steps-towards-print-md-sync.md` (renamed from
next-steps) — detailed, step-by-step, references the other docs, so a future AI can
follow it. **For each intended effort/PR: sketch the shape** — high-level *uncompiled*
code, empty fn bodies, pseudocode; identify good types, tests, needs, UX touchpoints,
and prereqs to pull out into *earlier* efforts. Iterate on those sketches hard.

## How this loop works (process — read every pass)

- 5-min loop, work in **chunks**, commit along the way, **never push**.
- **Recovery (if interrupted — quota, crash, reboot):** this file + git history ARE the
  durable state. Each pass commits, so an interruption loses at most the current
  uncommitted chunk; on restart, re-read this file and continue from the remaining todos.
  Quota exhaustion usually just *pauses* (the process stays alive) → the in-session loop
  resumes on its own when the limit resets. For a hard process death, the backstop is the
  OS-cron watchdog `notes/loop-work/loop-watchdog.sh` (if enabled) — it **resumes this
  session warm** (`--resume`, full context, not cold) and only fires when the live loop's
  heartbeat is stale, so it never double-runs.
- **Each pass, `touch /tmp/dark-wave2-loop.heartbeat`** — this tells the watchdog the live
  session is alive so it stays out of the way. (Cheap; do it with the pass's commit.)
- **When a todo is genuinely done — well and correctly — DELETE it from this doc.**
  This file shrinks toward empty; empty = done.
- **Add** newly-surfaced todos under "Discovered" as you go; check/delete them too.
- **Priority: pre-S&S and S&S.** Tighten them as much as possible — "enough to
  implement." Everything else can rest / be roughed-in.
- **Dependency rule (enforce constantly):** a doc may only reference its own bucket or
  *earlier* buckets. S&S must not reference PDD; etc. (See bucket order below.)
- **TIGHT docs, not long.** Tighten the *prose*; **keep code specs and step-by-step
  instructions** — those are the product, not bloat. Cut by: not repeating yourself, not
  narrating what a doc *used to* say, and not cross-referencing other docs more than
  necessary (a reference should earn its place). **Each doc < 1000 LOC, and total volume
  should feel light.** Little content should live in >1 file; end with *fewer* `.md` files
  than we started. If a doc is getting long, tighten or split — don't grow it.
- **Show, don't just tell — include VISUALS.** Embed fake **CLI/TUI experiences** (ASCII mockups of
  the actual terminal session a user would see), **dense code snippets** that show off the design
  (real type definitions, the shape of a fn, an op-stream fold), and small diagrams. These are tight
  and high-signal — they *are* the tight-doc style, not padding. A spec should be reviewable by
  *looking* at it. Prefer a 12-line mocked `dark apps` session over a paragraph describing it.
- **`main` is the source of truth — not this branch.** The `pdd` branch is a research
  **spike** (~368 files differ from `main`); many primitives here exist *only* in the spike.
  Verify every codebase claim against real `main` (`git show main:path`), never the working
  tree. Label spike-only things as spike-only. A clean idea on a real `main` primitive beats
  a slick one resting on something that only exists in the spike.
- Sandbox: edit only under `notes/loop-work/` (the `outputs/` tree + this file). Never
  touch the real `pdd-thinking/` or the Obsidian vault. Never use the section-sign in
  prose (write "section N"). Never `git stash`/`reset --hard`.

## Keep going until Sunday 6pm — perpetual mode (read every pass)

**Do not stop or go idle when the listed todos run out.** Once the initial work is
done, keep working — *forever, until ~18:00 Sunday* — and **add new todos** as you find
them. The deadline is a complete, review-ready spec by **6pm Sunday**; pace toward that.

When the explicit list is empty, the work is not done — there is always genuine depth
here. Rotate through these (and write what you do as new "Discovered" todos):

- **Deepen the PR-shape sketches.** For every effort, push the sketch further: real type
  definitions, the shape of key fns (empty bodies / pseudocode), the test list, UX
  touchpoints, the exact prereqs to pull into earlier efforts — **and a visual** (a fake
  CLI/TUI session, a dense code block, a small diagram) wherever it shows the design better
  than prose. These sketches are the product — they get *sharper*, not longer.
- **Build out the spine.** `steps-towards-print-md-sync.md` should grow into a sequence
  a future AI can execute step-by-step, each step linking the doc + sketch it needs.
- **Adversarial gap-hunt.** Re-read across docs for cross-doc tensions, missing pieces,
  unstated assumptions, and dependency-rule violations. Fix what you find.
- **Tighten + consolidate.** Reduce lines, merge overlapping docs, fewer files.
- **Re-verify against `main`** (not the spike tree) any codebase claim a sketch leans on.

**Focus the perpetual work on pre-S&S and S&S docs.** That's where the depth should go.
It's fine to do the other buckets and think ahead too, but those are secondary — the
bar to clear by Sunday is a tight, implementable pre-S&S + S&S spec.

**Keep docs TIGHT, not long.** Tight beats thorough-but-bloated. If a doc is getting
long, tighten or split it — do not grow it. PR-shape sketches should be dense and
short: the key types, the empty fn bodies, the test list — not prose padding. Deepening
a sketch means making it *more precise*, not *longer*.

Guardrail (per `looping-preferences.md`): don't *churn* already-good prose for its own
sake. The design space is large, but the product is a tight spec — prefer making
pre-S&S / S&S sharper and shorter over adding surface. Leave a one-line status each pass.

## The shape: one goal, what's below it, what's above it

The buckets aren't a flat list — they're **a goal with a stack under it and a stack on
top.** Read them that way:

```
            ┌─────────────────── above the goal (built ON it) ───────────────────┐
   later/             remote-control, P2P, app-fork, hot-reload, distributed liveness
   pdd/               we own the loop (resting — rough-in only)
   good-for-ai-agents/ the CLI as a cohesive tool: AI agents own the loop
            └────────────────────────────────────────────────────────────────────┘
   ════════════════════════ THE GOAL ════════════════════════
   stable-and-syncing/   print-md edited on one Dark instance, SYNCING to the others.
                         (sync, conflicts-and-resolutions, bootstrap; spine threads it all)
   ════════════════════════════════════════════════════════════
            ┌─────────────────── below the goal (the goal NEEDS) ────────────────┐
   pre-s-and-s/          the App model, ops+db architecture, capabilities, core
                         Tailscale, the apps surface, event-bus, async, cli-daemon
            └────────────────────────────────────────────────────────────────────┘
```

- **The goal** is `stable-and-syncing/` — everything is measured against "is print-md
  syncing yet?" The **spine** (`steps-towards-print-md-sync.md`) is the through-line: it
  names the efforts in order and points down into the docs that detail each.
- **Below** (`pre-s-and-s/`) is what the goal *rests on* — foundations that must exist
  before sync is even meaningful. This is also the base that makes the CLI a cohesive tool.
  **Tighten this hardest** — it's the load-bearing layer and the priority.
- **Above** (`good-for-ai-agents/` → `pdd/` → `later/`) is what gets *built on* a syncing
  substrate. Real, but secondary; rough-in and think ahead, don't polish.
- **`meta/`** is off to the side — scratch/cleanup; kill almost all of it after extracting
  the one looping-prefs doc.

**How docs fit together = the dependency rule.** A doc may reference only its own layer or
a *lower* one — never up. So the goal's docs may lean on `pre-s-and-s/`, but `pre-s-and-s/`
must stand alone; `pdd/` may lean on everything under it but nothing leans up into it. The
spine reads top-down (goal → foundations); dependencies point down. Same arrow, both ways:
**lower is more fundamental.**

---

## 0. Setup / structure (do first — unblocks the dependency + dedup work)

> Buckets established + all moves/kills done (pre-s-and-s/, stable-and-syncing/,
> good-for-ai-agents/, pdd/, later/, meta/). README + open-decisions killed; conflicts →
> conflicts-and-resolutions; next-steps → steps-towards-print-md-sync. Remaining structure work:

- [ ] **Dependency pass.** Identify the buckets + their dependencies, then read and
  reread all `.md` until you're *sure* no doc references a higher bucket (S&S ↛ PDD, etc.).
  **Known violations to fix now:** pre-s-and-s docs (esp. distributed-event-sourcing, apps-surface,
  composable-mvu, package-system-layers, capabilities, event-bus, async) currently link UP to
  `stable-and-syncing/` (sync, conflicts), `pdd/`, and `later/` — invert or remove those refs
  (the lower doc should be referenced BY the higher one, not reference it).
- [ ] **Dedup pass.** Same thoroughness: ensure little content lives in multiple files;
  reduce total lines; consolidate/split where clear; end with fewer files.

## 1. pre-S&S (foundations) — PRIORITY, tighten hard

- [ ] **Core Tailscale doc** in `pre-s-and-s/` — only what S&S needs, no more.
- [ ] **`remote-access.md`:** rename → `remote-access-and-control.md`; migrate the core
  Tailscale bits into the pre-S&S Tailscale doc; migrate the *rest* (remote control) →
  `later/`.
- [ ] **`async.md`:** async should be **usually invisible** to Dark users — no syntax or
  difference between async and non-async code, *unless* they have specific scheduling
  needs. Update with **effort estimates / steps involved**. **Evaluate:** is the
  `DarkAsync` / build-our-own-scheduling idea actually good? How does this relate to
  events → op-playback — do we do both efforts together, or async first then
  separate-ops-from-playback? Does EventBus relate? Answer these.
- [ ] **`event-bus.md`:** keep focused — split/punt into "needed for S&S" vs "later";
  enough to get by. **Stop mentioning the `Stream` thing**; talk about EventBus
  independently. Keep iterating re App / async / MVU. Tighten.
- [ ] **`cli-daemon.md`:** drop the doc-history framing. **Split** into (1) supporting
  long-running daemons in the CLI, and (2) the specific per-branch(?) daemon — what it
  needs, its projections, how it interacts with everything. Reconsider
  "one daemon per machine": maybe one **core sync daemon** + one **per projection**?
  Think it through, don't go wild. **Daemons are just Apps** — show in the apps menu,
  managed there.
- [ ] **`composable-mvu.md`:** fold into 1+ other docs (per its own preamble). Rename
  `App.empty` → `init` (and maybe it takes args). **Compare the App structure to mature
  systems (Elm, F#, …)** — is ours reasonable? Does it make distribution of
  event-sourced MVU apps actually work, or need refinement? Iterate, iterate. Replace
  "mapping the PDD viewer onto this model" with the **outliner** (a better, real,
  composed focus); bring it to this world, then `print-md` is easy.
- [ ] **`distributed-event-sourcing.md`:** maybe **merge** with another file. DB model:
  think **one DB per branch/session** we're working with (maybe per dev server), maybe
  **one per active/long-running app**, plus a **core ops/sync DB** (call it `dark.db` /
  `core.db`). The outlined `local.db` may not be ideal — many simple fields aren't great
  in SQL; maybe **JSON / a serialized Dark-value blob** instead. **Punt** the "the App
  is live, forkable" section.
- [ ] **`package-system-layers.md`:** fold its ideas *wholly* into other docs; retire
  the file.
- [ ] **Ops & playback / DB architecture:** imagine a `.db` per **branch**, per
  **branch+app**, plus the **core ops-and-sync DB**. Thread this consistently through
  the docs above.
- [ ] **Integrate the emailed thoughts** (below) into the right pre-S&S/architecture
  docs; flag anything not represented.

### Emailed thoughts to integrate (architecture — mostly pre-S&S)

- Each **app has its own DB**. Most are temporary / GC'd; some stick around.
- **Sync is built on top of** this per-app-DB system.
- There are **core internal tables** that own others. What's the **minimum F#** we
  need? What does **PT** look like? How does this inform the plan toward print-md sync?
- An **instance per app** (maybe); a **core instance coordinates the rest and sync** —
  that's its only job.
- Maybe everything lives in the **root `.darklang` dir** — imagine the whole folder
  structure.
- **Install** first = a boring CLI with a **seed DB** and a few **capabilities**. Then
  add **extensions** / install the **sync app** and other apps, built on each other.
  Could even **remove SCM and rebuild it as a Dark app** — same with PDD, outliner, and
  all sorts of terminal apps / websites / scripts / reMarkable stuff.
- Each **"repo" is an ops DB**. We exist in the central install; for some people, also
  as **other repos across the filesystem**. A system for syncing data **just the way I
  want** — set upstream, etc.
- **Load builtins optionally**, including their capability structure — like **DLLs**.
  What's reasonable/common, where do they live, are these "extensions" / "platforms"?
- Each extension comes with **runners that respect special types**, or just bindings +
  mappings.
- **Skills, MCP servers, and "evals" are dumb** — we just need functions, data, and tests.

## 2. S&S

- [ ] **`conflicts.md` → `conflicts-and-resolutions.md`:** keep **just enough** for S&S;
  split/punt the rest. Good core ideas — but don't go overboard.
- [ ] **`identity.md`:** probably **punt to later** (likely not needed for S&S yet).
  Keep only what's needed to sync safely between Stachu + coworkers. `Intent` should be
  a reasonably **structured** thing, known in **PT** in a nice, stable way.
- [ ] **`bootstrap.md`:** think for a while — once S&S is done, what are the steps here?
  We can't *start* yet, but start thinking.
- [ ] **`next-steps.md` → `steps-towards-print-md-sync.md`:** detailed, step-by-step,
  referencing other docs so a future AI can follow it. Make sure **"separating ops from
  their projections"** is present (it's currently missing). Address **where/how the
  event bus fits** into LibExecution, ProgramTypes, etc. **Identity binding:** punt or
  keep very thin (just enough to sync safely between Stachu + coworkers). **Remove the
  "explicitly not next" section.** Open decisions only in specific docs, not here.
  **WIP sync** is ideal but we don't know how to do it safely → punt.

## 3. good-for-ai-agents (the tool; base for PDD)

- [ ] Make this a **cohesive dir** for "good for AI agents (like Claude Code) using Dark
  as a tool, where *they* own the loop." `ai-coding-target.md` belongs here, considered
  independently of PDD. (The existing `good-for-ai-agents/` improvement docs stay.)

## 4. PDD (we own the loop; resting — rough-in only)

- [ ] **`pdd.md`:** PDD = *we* own the loop. Maybe **no `prompt` command** — just
  `dark "request"` and it goes; some requests are "build/run software that…", some are
  "cd to wherever the json stdlib is." Open intent, we figure it out. **Fold the PDD
  README into `pdd.md`** (or vice-versa).
- [ ] **`example-app.md`:** consider the real CLI **outliner** instead (real, known,
  composed). Adjust the whole doc after reconsidering the App shape (per composable-mvu).
  The **`views`** part especially needs work: we need an **identifier** so an "above"
  app can use 0-N of the UXs as it chooses — maybe `views` is a **record type** the
  above app reaches into. (Coordinate with the outliner focus in composable-mvu.)

## 5. later

- [ ] **`hot-reload.md` → `later/`** (punt). You *may* think about hot-reload needs and
  shape `async` / projections / op-playback toward a solution that will support
  hot-reloading — but **don't mention hot-reloading** in those docs.
- [ ] **remote-control** (the non-core half of `remote-access-and-control.md`) → `later/`.
- [ ] **P2P sync, `dark apps fork`, "the App is live/forkable", distributed-app
  liveness** → `later/`.

## 6. meta (cleanup — do the kills only after section-0 extraction; consolidate at the very end)

- [ ] **Kill:** `meta-reflections/README.md`, `PRINT-LIST.md`, `STATUS.md`,
  `feedback-coverage.md`, `grounding-against-main.md`, `loop-operations.md`,
  `process-risks.md`, `where-the-loop-struggles.md`, `what-the-loop-is-good-at.md`.
  (The looping-prefs doc is already extracted → `notes/loop-work/looping-preferences.md`,
  so these kills are unblocked.)
- [ ] **Consolidate `vault-organization.md` + `overwrite-map.md` into one.** At the **end
  of EVERYTHING**, re-evaluate its content from scratch (the dir structure will have
  changed a lot).

## Cross-cutting (ongoing every pass)

- [ ] For each intended effort/PR, **sketch the shape**: uncompiled high-level code,
  empty fn bodies, pseudocode; good types; tests; needs; UX touchpoints; prereqs to pull
  into earlier efforts. These sketches are the real product — iterate on them.
- [ ] Keep enforcing the **dependency rule** and **reducing total lines / file count**.
- [ ] Keep **tightening pre-S&S + S&S** until "enough to implement, review, and go."

## Discovered (add todos here as they surface)

- (none yet)

# Dark — Working Notes: Stable, Syncing, Composable

This is the consolidated plan for the next phase of Dark. It turns a large pile of recent design
sketches into a focused, ordered, executable body of work, and it is written to be *read* — so a
person (or an agent loop) can pick it up cold and understand both where we're going and what to do
next.

## The north star

The litmus test for "did we build the right thing":

> Stachu's existing **`print-md`** script lives in Dark. He can inspect it, change it, and have those
> changes **sync** to his other computers via the good solution. If Ocean wants, she can **fork** it.
> It shows up under **`dark apps`** as an installed app.

Everything below is measured against enabling that. It is small, concrete, and exercises the whole
spine: a real program stored in Dark, edited live, synced across machines, forkable by someone else,
and surfaced as a first-class app.

## What we're actually building toward

Three ideas thread the whole plan together:

- **Ops vs projections.** State is modeled and mutated as a timestamped stream of **ops**. Everything
  you see — package items, branches, the file view, dependency graphs — is a **projection** of that
  stream. Keeping ops and projections cleanly separated is the lens we apply to every doc; the core
  sync DB is one thing, branch-/session-specific projections are another.
- **One thin `App` type.** A single, deliberately low and thin shape (see section 2) that models an op type
  plus how to replay it. The current CLI experience gets rebuilt *as* an App; users build their own on
  top. This is the sync primitive.
- **Event-streaming as the sync substrate.** Stream events, play them back, detect conflicts (which may
  themselves be streams), compose. Solved with the least total code, this is what eventually lets us
  remove the `.dark` files from the repo.

## The current priority

**Stable & Syncing comes first.** PDD and the later/speculative work *rest* — they get cleaned up and
parked, not advanced. Concretely, the near-term target is:

> Two local **release** builds syncing — one acting as the **server** (Stachu's always-on desktop, on
> the tailscale network), one as the **client** — with branches, efforts, and experiments completed,
> all written in Dark using AI.

Prefer the simplest thing that works: a plain client/server, server = the always-on desktop.

---

## Working sandbox — the safety model (read first)

All of this effort is isolated under `notes/loop-work/` so the loop can **never corrupt the originals**:

- **`source/`** — a **frozen, read-only** copy of every input (the repo `pdd-thinking/` and the relevant
  vault docs, plus `feedback.md`). It is `chmod`'d read-only. **Never edit it; treat it as the reference
  "before".** The real `pdd-thinking/` and the **Obsidian vault are strictly off-limits** — do not write
  to them at all.
- **`outputs/`** — the **workspace**, started as an exact copy of `source/`. **All edits, renames,
  dissolves, and new files happen here.** This is where the new `notes/` structure (`design/`,
  `projects/`, `results/`, …) takes shape.
- **`loop-instructions.md`** — this file, the controlling plan. Check items off here as you go.

Nothing is promoted into the real `notes/` tree (or the vault) until Stachu reviews `outputs/`. Commit
locally as you work (never push); `source/` being read-only means a stray write **fails loudly** instead
of quietly damaging an original.

---

## Context & ground rules

*(Not todos — the standing constraints and the lay of the land. Read once, then keep in mind.)*

**Hard rules**
- **Print ONLY at the very end.** Do **not** `print-md` any doc mid-loop, no matter how meaningfully
  created or revised. This loop runs for hours/overnight and docs get revised repeatedly; per-pass
  printing floods the physical printer. All printing happens once, in section 11, after every checkmark
  is done.
- **Never push to upstream git remotes.** Local branches and commits only. Track and commit freely.
- **Never** `git stash`, `git reset --hard`, or destructive-rebase to dodge an error.
- **Bias to fewer, smaller, tighter `.md` files.** Less repetition, less garbage, no outdated phrasing.
  Little content should need to live in more than one place — when two docs overlap, consolidate.
- **Apply the ops-vs-projections lens everywhere.** When a doc conflates modeling/mutation with the
  projection of it, that's a cleanup.
- **Dead-link sweep.** Several docs still link to files that were deleted or renamed (`WRAP-UP.md`,
  `SYNC-AND-STABILITY.md`, `ROADMAP.md`, `EMPIRICAL.md`). Fix or drop each as you touch a doc.

**Settled decisions** (resolved with Stachu; baked into the sections below)
- **Home = repo `notes/`; work in the sandbox.** Everything lives under `notes/`, in source control
  (tracked + committed, never pushed). For this wave, all work happens in `notes/loop-work/outputs/`
  (see "Working sandbox" above); the cleaned result is promoted into the real `notes/` tree only after
  Stachu reviews it.
- **Async:** when the async doc gets written, do **both** the consolidated design *and* a concrete
  migration sketch (kill-Task/Ply). Skip the heavy ".NET concurrency" reading for now.
- **PDD command waits.** Everything on this branch has been a sketch/spike — the `dark prompt` / PDD
  command waits for real implementation later. The **apps** surface, by contrast, is ready to build
  (may depend on the CLI-daemon work).

**Where the docs live** (frozen copies in `source/`; edit the mirrors in `outputs/`)
- **`source/pdd-thinking/`** (copies of the repo docs; originals untouched): ALGORITHM, BOOTSTRAP,
  CAPABILITIES, CLAIMS, COHABITATION, COMPOSABLE-MVU, CONFLICTS-AND-RESOLUTIONS, EVENT-STREAMS-AND-PARKING,
  FRONTIER, HOT-RELOAD, IDENTITY, README, READY-WORK, REMOTE-ACCESS, STABILITY-AND-SHARING, TOC,
  VIEW-SKETCHES, pdd-elevator-pitches; `research/` (beam-vs-dark, swamp-vs-dark, visibility-vs-dark).
- **`source/vault/`** — copies of the vault docs (provisional input, flagged for Stachu's review; the
  real Obsidian vault is **off-limits** — never write to it):
  - `90.Stachu/even-newer/ai-devloop/` (May 5, latest): plan.md, projects.md, improvements.md,
    plan-analysis.md, phasing.md, reflection-template.md, README.md, and the orchestration/queue docs.
  - `90.Stachu/newest/ai-devloop/` (May 3, older despite the name): plan.md, projects.md, improvements.md.
    → the two `plan.md` are the same doc at two times; keep the May-5 one, dissolve the May-3 one.
  - `02.Project Management/Current Experiment/`: project-survey.md (= "CLI-PROJECT-SURVEY"),
    package-system-layers.md, dark-virtual-files.md (state-as-filesystem — a *different* concept from
    removing `.dark` files), and `Design/Dark Async Plan.md` (the coworker's async doc).
  - REFLECTION.md / SUMMARY.md exist only as vault `reflection-template.md` and `specs/SUMMARY.md`.

---

## 1. Notes structure (the destination to refactor toward)

The new structure takes shape **inside `outputs/` first**; it's promoted into the real `notes/` tree
only after Stachu reviews it. (Originals in `pdd-thinking/` and the vault stay untouched.)

- [ ] Reorganize the copied docs in `outputs/` into the dirs below. Track + commit as you go (never push).
- [x] `design/` — durable design docs.
- [x] `projects/` — spec files: each a simple **goal line** + **acceptance-criteria list**, tagged
  greenfield/brownfield. No phases, no iteration logs, no cross-cutting test criteria.
- [ ] `results/` — **raw** bench data + per-sweep report summaries. One report per sweep (or a dir per
  sweep with a single final summary). **Never** keep the intermediary per-iteration summaries.
- [ ] `issues-and-improvements/` — one `.md` per category of issue-space, each with subsections for
  candidate fixes. Most of REFLECTION.md lands here.
- [ ] `meta-reflections/` — how the *process* itself is going; feeds back into design. Some of
  REFLECTION.md lands here.

The work below is grouped under six themes (also the reading/priority order):
**1) Stable & Syncing** (priority) · **2) Removing .dark files** (punted, section 3) · **3) PDD** (resting) ·
**4) Good for AI agents** · **5) Good for reviewing/managing software** · **6) Good for editing software** ·
plus *Later/Other*.

---

## 2. Cross-cutting design threads

These recur across many docs; settle each in **one** place, then reference it.

- [x] **Ops vs projections split.** Model + view are distributed, but *projections* of an update likely
  happen on specific instances. Need recovery for distribution races (e.g. branches across instances
  pointing a name to different hashes), resolved via the conflicts/resolutions system. The **core
  SQLite DB for sync** is probably separate from branch-/session-specific projections (package items,
  etc.). How to split this cleanly is open — think it through and write it down (home: the new design
  doc in section 8).
- [x] **Event-streaming as the sync substrate.** Pursue the layering where event-streaming *is* sync:
  stream events, replay them, detect conflicts (maybe themselves streams), compose. Least total code.
  Once set, this is the path to removing `.dark` files.

- [x] **The one thin `App` type.** The sync primitive is a single, deliberately *low and thin* generic
  shape — just enough to serve the core goals (replay, distribution, reconciliation), with everything
  richer built on top in Dark. It does **not** hold a *list* of ops; it declares the **op type** and how
  to **play an op back**. Think Elm/Elmish, but where the update unit (`'op`) is the thing that syncs and
  replays across instances.

  ```fsharp
  type App<'state, 'op> =
    { name       : String
      empty      : 'state                     // starting state
      apply      : 'op -> 'state -> 'state     // play ONE op back — the only way state moves
      conflict   : 'op -> 'op -> Bool          // do two concurrent ops clash?
      resolve    : 'op * 'op -> List<'op>      // reconcile a clash (auto where it can)
      views      : 'state -> List<View>        // projections to render (each by id/hash)
      invariants : 'state -> List<Violation> } // at-rest / runtime constraints
  ```

  Tiny sample — a counter:

  ```fsharp
  type CounterOp = | Inc | Dec | SetTo of Int64

  let counter : App<Int64, CounterOp> =
    { name = "counter"
      empty = 0L
      apply = fun op n ->
        match op with
        | Inc -> n + 1L
        | Dec -> n - 1L
        | SetTo v -> v
      conflict = fun _ _ -> false          // increments commute — never clash
      resolve  = fun (a, _) -> [a]
      views    = fun n -> [ Text $"count: {n}" ]
      invariants = fun _ -> [] }
  ```

  **How it distributes (usage):**
  - Ops arrive locally or stream in from a peer over the event bus. The **op stream — not the App
    value — is the durable, synced thing.**
  - State is **derived**: fold `apply` over the ordered op stream. Replay = re-fold. (This is the
    op-playback the rest of the notes lean on.)
  - When two instances produce concurrent ops, `conflict` flags the pair and `resolve` emits
    reconciling ops. Most clashes are fine and auto-resolve; the rest are just data we fix later.
  - `views` are projections of state for the CLI/UI; `invariants` are the runtime/at-rest constraints
    (cf. the runtime-tests / at-rest-tests special types; Scriptorium is a reference).
  - A msg/cmd-style UI loop (the Elmish part) is a *layer on top*: UI intent → ops. Kept out of the thin core.

  **vs Elmish:** `apply` ≈ `update` (an op instead of a msg; effects handled by the substrate +
  capabilities, not a `Cmd` in the core); `views` ≈ `view`; the distributed reconciliation
  (`conflict`/`resolve`) and `invariants` are the additions that make it work across instances. We
  rebuild the current CLI experience as one such App, and users build their own on top.

  - [x] Decide, during design, whether generic op-playback is **conflict-blind** (each projection owns
    conflict handling) or the App carries `conflict`/`resolve` as above. Leaning toward carrying it for
    ergonomics; settle it when the first real App is built.

- [x] **Async / concurrency at the language level.** Open thread (touches EVENT-STREAMS, cli-daemon,
  COMPOSABLE-MVU, beam-vs-dark, the coworker's async doc): **kill Task/Ply and roll our own async** so we
  can really control async behavior — park threads, manage nested processes, let Dark UXs inspect what's
  running via opt-in debug symbols. Write it up as one async doc (design **+** a concrete migration
  sketch; no heavy .NET reading for now). Don't scatter async decisions across docs.
- [x] **`ref` keyword + composable parser + `compile` builtin.** A `ref` keyword to get a reference to
  e.g. a hash — probably just a global function like `print` that we teach the parser/name-resolver. A
  **composable parser written in Darklang**, compilable to tree-sitter or similar (a DSL of types/fns +
  Dark code that compiles down). Get **`compile` as a builtin asap.** Enabling primitives for the
  App/editor work — design inputs, not immediate builds.
- [x] **Crons / daemons as distributed apps.** Crons modeled as a distributed app we officially support,
  as an extension of the default CLI. Daemons via something like `start()`. One projection is "the list
  of conflicts" (usually ignorable). Folds into the App-type thinking.

---

## 3. Theme: Removing .dark files — **PUNT, and consolidate the blockers**

- [x] **Propagate the decision:** removing `.dark` files is **punted until after baseline sync +
  stability**. Not realistic short/medium term — F# and Dark code reference each other tightly/often.
- [x] **Consolidate all blockers in repo `BOOTSTRAP.md`** (package bootstrapping). *(Note:
  `dark-virtual-files.md` is a different concept — Dark state projected *as* a filesystem; keep it
  separate.)* Enumerate the blockers explicitly:
  - Stable environment.
  - Working sync.
  - Migrations of Dark environments — including upgrading core things like the **language** locally
    while still using a backed-up / dev-ready DB of old package code.
  - F#↔Dark mutual references when **lang/ops change**: how does local dev keep old stuff running while
    adding package + F# code back and forth, affecting only the local machine? What about **CI**?
- [x] **BOOTSTRAP.md edits:** lead with the hard part (F#→Dark→F# mutual reference under lang/ops change;
  local dev; CI). State plainly: **get sync working first, then worry about this; assume a central
  server.** Define or kill the unclear **T3/T4/etc.** tier labels.

---

## 4. Theme: Stable & Syncing (the priority cluster)

### 4a. EVENT-STREAMS-AND-PARKING.md (rename Stream → **EventBus**)
*(Verified: "v0 design" blurb @L3; "third substrate piece" @L11; "Compared to the spike's EventSink" @L221; ~67 "stream" occurrences — the rename is a real sweep.)*
- [x] Remove the **v0 design** blurb and the **"the third substrate piece"** sentence.
- [x] Remove **all PDD mentions** — this doc is fully within the "syncing & stable" portion, *before* PDD.
- [x] **Rename `Stream` → `EventBus`** throughout; remove the **"compared to event sink"** section and the
  connections to other substrate sketches / main code.
- [x] **Iterate the implementation design:** does it fit cleanly into **LibExecution**, the **ProgramTypes**
  vibe, and what the CLI wants to be? Make the **F#-side thin but tight and well-designed — "enough" but
  minimal**, with the rest in Dark.
- [x] Make the **sync/stability story explicit:** stream events, replay, detect conflicts as streams,
  compose → eventually remove `.dark` files (least total code). Think about whether async/concurrency
  must be solved at the core language level first (Ply replacement; section 2 async).

### 4b. STABILITY-AND-SHARING.md (mostly dissolve)
*(Verified: "Definitions (T7)/Stable" @L19-36; "Wire protocol (T8)" @L96 is the keeper; "SyncEvent" schema @L124 is droppable.)*
- [x] The "definitions > stable" idea was really about **PDD stability** — what the algorithm reaches for
  during iterative development. **Migrate that to the PDD/ALGORITHM side**, then **kill the rest of this
  file.** **Keep the wire-protocol section** somewhere (it was good); drop the **SYNCEVENT SCHEMA** section.

### 4c. CONFLICTS-AND-RESOLUTIONS.md
*(Verified: "SCM op-vs-op" is only a single example @L384 — needs expansion; "Parse-time errors" @L526; "Persistence — conflicts_v0 …" @L249 is the one to kill.)*
- [x] Expand **SCM op-vs-op** — the most important conflict category. Add the concrete conflicts possible
  there + related thoughts.
- [x] Frame conflicts by the **states where evaluation happens**: parse-time, run-time, dev-time, at-rest
  (and maybe a **playback variant of run-time**). The hypothesis: **conflict *timings* are really the
  conflict *types*.**
- [x] **Don't get bogged down in SQL schemas.** Model conflicts in a **core, composable** way that works
  for all kinds of Dark apps (sync, stability, AI-agent dev — separate *and* composable). **Kill the
  persistence section.** Reinforce: projection is separate from ops, composably.

### 4d. plan.md (the two vault snapshots)
*(Checked both: same "Dark as the optimal AI coding target" doc at two times. `even-newer/` (May 5) is refined; `newest/` (May 3) is the fatter older basis.)*
- [x] Keep `even-newer/ai-devloop/plan.md` as **canonical**; mine `newest/` for anything unique, then
  archive/delete the older snapshot. Treat the canonical one as **~weeks outdated** — evaluate against
  current thinking. *(`DARK_ACCOUNT` confirmed gone from the repo — no follow-up.)*
- [x] **Remove sections:** key files; "schema facts worth remembering"; **step 0** of impl order; open
  questions; specific metrics shortlist; phasing; risks/failure modes; references.
- [x] **Adopt simple client/server** (server = always-on desktop on tailscale). **No env vars** — prefer
  config in the **CLI-adjacent `.darklang` dir** (sweep other docs for env-var assumptions too).
- [x] **Iterate + tighten the impl-order suggestions**, set the goal line to the north-star sync goal,
  and keep the surviving text **well-represented somewhere**, then tighten.

### 4e. cli-daemon-mode.md
- [x] Re-think given recent ops / sync / projections / **async** developments (async especially).
- [x] Resolve: do we need a `.sock` / `.pid` / `.version` set **per session/branch** running? **per
  background service**?
- [x] Reframe beyond perf: perf to CLI interactions is **one** benefit — think through the others. (The
  apps surface may depend on this work.)

---

## 5. Theme: PDD (resting — clean & tighten only, don't advance the program)

### 5a. REFLECTION.md
- [ ] **Kill the numbers** (Stachu generates good ones later). Consolidate tighter. **Remove all
  reflection from the SUMMARY.md docs.** Move most content into `issues-and-improvements/`; some into
  `meta-reflections/`.

### 5b. SUMMARY.md docs (split & mostly retire)
- [ ] Split each into **(1) a long-lived spec** (simple goal line + acceptance-criteria list) and
  **(2) learnings extracted separately** from the design/desire. **No need to mention other systems.**
  Net intent: these shouldn't really exist — replace with per-project specs + per-sweep results docs;
  don't keep intermediary summaries.

### 5c. projects.md (vault `even-newer/ai-devloop/projects.md`)
*(Verified: "Suggested first-pass ordering" @L482; "Sources" @L503; `modules:`/`languages:` fields; grouped by Phase 1/Phase 2; iteration tags throughout.)*
- [x] **Kill sections:** sources; suggested first-pass ordering. Move **cross-cutting test criteria**
  elsewhere (consolidated, not in a design file). **Remove all mentions of specific iterations** ("added
  iter 28"). **Group projects by category only**, not by phase.
- [x] **Format change:** remove `modules` and `language` fields; **add greenfield/brownfield** (most are
  greenfield). *(Emulating brownfield work well is unsolved and important, but don't worry about it much
  for now.)* Fold the projects into `notes/projects/`.

### 5d. ALGORITHM.md
*(Self-labels INCOMPLETE; has a "Sig consensus" section with first-non-failure-wins + constraint-driven.)*
- [x] **"First non-failure wins" is too blunt** — nuance it beyond the two-mode sig-consensus (the
  coordinator should weigh more than race-order).
- [x] Add: a fn **body itself** might be an **LLM wrapper**, an **LLM-agent wrapper**, or **text with an
  expected type (dummy value)** — materialization doesn't always crystallize into Dark code; some bodies
  *stay* a delegated LLM/agent call ("forever lazy"; keep in sync with CLAIMS). More iterations — real rework.
- [x] Absorb the migrated "definitions > stable = PDD stability" thought from STABILITY-AND-SHARING (4b).

### 5e. CLI-PROJECT-SURVEY.md (vault `Current Experiment/project-survey.md`)
- [x] **Kill section 1 "what darklang gives you."** Fold **just the projects** into `notes/projects/`.
  **No letters assigned to classes.** **Remove the suggested-ordering section.** Move the meta
  (cross-cutting test criteria) **out** of this design file.

### 5f. FRONTIER.md (distribute its notes, then delete)
*(It's a "design backlog" of post-spike problems — valuable inline notes that belong in topic docs.)*
- [ ] **Distribute the inline notes to their real homes, then delete the file:**
  - "Tracing: less surface, more primitive" (expose via builtins; traces as queryable/replayable values)
    → async/substrate (section 2).
  - "Storage: kill the JSONL sidecar" (everything in SQLite) → sync/persistence (section 4) + EVENT-STREAMS.
  - "Done-ness as a gradient" (idea→name→sig→body→tests→connected→description) → ALGORITHM/CLAIMS.
  - **"WIP and SCM" — the (a) WIP-doesn't-sync vs (b) WIP-needs-sync tension** → CONFLICTS (section 4); a real
    open decision.
  - "Speed" (search dark-matter <100ms; draft v0 <1s) → `results/` benchmarks, not a design doc.
  - "Prompts as a pinned type" (`Prompt` first-class) → CLAIMS/ALGORITHM.
  - "Search-by-type & agent helpers", "Refactors as a language primitive (a refactor is an op)" → section 7 / section 2.
  - "Risks to watch" (trace-replay divergence, tolerance-hides-bugs, sig thrash) → `meta-reflections/`.
- [ ] Fix its dead links (`SYNC-AND-STABILITY.md`, `EMPIRICAL.md`) as part of the move.

### 5g. CLAIMS.md
*(5 claims + "AI is opt-in" + a 60s pitch; references a dead `ROADMAP.md`.)*
- [x] Extend **Claim 1 ("source often starts as lazy")**: some fns are **fully delegated to an LLM
  system** and so are **"forever lazy"** — the body never crystallizes into Dark code (pairs with
  ALGORITHM 5d).
- [x] In the **60s pitch**, change **"You write names and signatures" → "You ask for software"** (same as
  the elevator pitches, 5j). Fix the dead `ROADMAP.md` reference.

### 5h. README.md (the PDD one)
*(Big "Demos verified live" table full of timings; a thicket of `dark pdd …` refs; dead links.)*
- [x] Reframe: we **don't anticipate many PDD commands** — `dark prompt` just **starts a background agent
  that builds the thing**, and the **CLI enters a watching state** (with an option to run it in the
  background). *(The PDD command itself waits for real implementation — this branch is a spike. Don't
  over-build the command surface now.)*
- [x] **Kill the numbers** in the demo tables (keep at most a tiny qualitative example). Fix/remove dead
  links (`WRAP-UP.md`, `SYNC-AND-STABILITY.md`, `ROADMAP.md`) and the stale "How to enter" order (dedupe
  vs TOC.md). **Don't keep a README at the end — or make it really thin.**

### 5i. READY-WORK.md
- [ ] **Kill theme A** and **theme B** (reasons elsewhere). **Kill this doc**, and **fully rewrite it at
  the very end** of the whole process as a thin **`next-steps.md`**.

### 5j. pdd-elevator-pitches.md (already renamed from `20-elevator-pitches.md`)
- [x] Change **"You write names + signatures" → "You ask for software"** and adjust the pitch. General
  update — it's a bit outdated; fold in other feedback + your own thoughts.

---

## 6. Theme: Capabilities & Identity (LibExecution-facing design)

### 6a. CAPABILITIES.md
*(~566 lines, very heavy. Has the "v0 design"/"fourth substrate piece" framing; "LLM-prompt side", "Sequencing", "What this unlocks", "Per-assembly default caps", "Schema", "Connection to Previewable" sections; two "Where checked" sections; already presupposes `--ask`/interactive grants.)*
- [x] Remove the **"v0 design" blurb** and **"fourth substrate piece"** framing (consistency with 4a).
- [x] **Pure fns are always allowed** (no cap needed).
- [x] **Iterate the `Capabilities` type** with real nuance:
  - **HttpClient** — sophisticated (pull the specific restrictions from the vault notes — *location still
    to find*).
  - **HttpServer** — probably inspired by the HttpClient model.
  - **Random / Time** — yes/no.
  - **File system** — yes/no for now, with commentary about future sophisticated options.
  - **Language** — pure/safe things need no perms; analyze how to reflectively eval, etc.
  - **Matter** — same shape as Language.
  - **Other CLI stuff** — same? needs more investigation.
- [x] **Re-design the implications to LibExecution top to bottom** after settling the nuance.
- [x] Question the model: is **`Set<Capability>`** right? Maybe builtins don't register publicly — they
  reach into `state.`. Maybe the **Interpreter checks nothing** and only the builtin does — or builtins
  register a **`checkCapabilities` function** (cacheable, but possible code/redundancy cost). **Think
  through both, then decide — leaning toward the registered `checkCapabilities` fn.**
- [x] **More nuance than `--ask`:** human interactions are async — may **timeout and fall back** to
  non-human options. **For now: NO interactive grants** (they complicate things and make testing hard) —
  but **sketch the structure**. Maybe builtin grants are **instance-specific** for now.
- [x] Address **how frame-parking works** (ties to EVENT-STREAMS parking + section 2 async). Keep & build out
  **"user-defined fns"** (buildable quickly; part of ops-vs-projections).
- [x] **Remove sections:** "llm prompt-side"; "sequencing"; "what this unlocks"; the **schema** section
  (do schema after the design is nice); "connection to previewable". Replace the **per-assembly
  default-caps** table with a **more nuanced section**, written once.
- [x] Note the meta-connection: this may really be "distributed event sourcing + branched MVU" (section 2 / section 8).

### 6b. IDENTITY.md
*(~400 lines — full Delegation contracts, Agent types, SQL schemas, Phase-2a/2b plan, open decisions, a Cross-cutting section. Far heavier than "thin + directional".)*
- [x] **Thin it out hard, make it directional** — cut the delegation/schema/phasing bulk; keep the core
  model + the intent idea.
- [x] **Rename `IdentityKind` → `Identity`.** Account does **not** include identity as a field. Shape
  (note the recursive `owner`):
  ```fsharp
  type Identity =
    | Human of AccountID
    | Agent of id * owner: Identity
  ```
- [x] **Kill `TrustProfile`.** In the **account record**, drop: `kind`, `ownerID`, `trustProfile`,
  `archivedAt`.
- [x] Tracing cares about the **identity / source of *intent***; strip the rest. Model **Intent
  (/reason/context) per Identity + (Dark) Instance.**
- [x] **Kill the "cross-cutting" section** and any phasing / larger-process framing — keep the document
  **pure**.

---

## 7. Theme: Good for AI agents (and reviewing / managing / editing software)

### 7a. improvements.md (vault `even-newer/ai-devloop/improvements.md`)
*(Preamble blockquote @L3; first numbered heading is "3.1 Discovery"; `dark suggest` @L18; "Known runtime gaps" @L160.)*
- [ ] **Remove the early preamble gray text.** **Consolidate issues & suggestions** into
  `issues-and-improvements/`.
- [ ] **Don't build a big CLAUDE.md.** Make **`dark docs for-ai` much more helpful**, removing most need
  for follow-up calls — a **composed document** with dynamic/expanding content that loops in other docs,
  eventually informed by the specific project/task/user. Possible mechanism: the core `for-ai` doc lists
  **doc hashes + names**, and a follow-up `dark docs hash1 hash2 hash3` **concatenates them beautifully.**
- [ ] **Kill `dark suggest`.** Fix the numbering (don't start at **3.1**). **Kill the "Known runtime
  gaps" section.**
- [ ] Cross-link (don't duplicate) the related topics for this theme: `feedback-from-agent.md`
  (*location still to find*), plus reviewing / managing / running / editing-software.

---

## 8. New documents to write

### 8a. "CLI structural Dark ProgramTypes editor"
- [ ] Structural / projectional editor in the spirit of **dark-classic** or **Hazel** (cross-ref vault
  `05.Implementation/Editing/structured and projectional editing.md`).
- [ ] Powered by a **tiny LLM loop**: given *this keyboard shortcut* + *the current state*, **(how) should
  the rendering of this view change?** **Caching** to make it fast. **The editor's own model is editable
  in the editor** (self-hosting).
- [ ] **Design the UI; produce fake "views" to review;** list the components top-to-bottom as high-level
  bullets. **Steal from generic component-UI lib ideas** like **Clay.** Should **eventually work for
  HTML too.**

### 8b. "Distributed event sourcing + branched MVU"
- [x] Home for the **App-type** thinking (section 2), the ops-vs-projections split, op-playback, and how
  EVENT-STREAMS / CONFLICTS / COMPOSABLE-MVU / CAPABILITIES compose. Capture these raw ideas — don't lose
  them:
  - **"Simplify Darklang greatly."** For now support only: a **timestamped set of ops**, a **modeling of
    their conflicts (or constraints)**, a **way to sync all of it**, and some **projections**. The
    smallest, most composable system for data + apps.
  - **The App value is editable** by either people (via the CLI) or agents.
  - **Most conflicts are OK** — just data/conditions we don't like and can get to later.
  - **"If a user's system implements this App type, we respect it / they run it."** Rebuild a fork of the
    current CLI experience **as an App**, solving sync/dist along the way. Needs baseline views → a
    **view engine.**
  - **Any user of an app can fork or extend it** — its views, its behavior, any experience — and **migrate
    their data** somehow. Not just the author; whoever's using it.
  - **An App's package values "magically get their own management"** — or the CLI provides that around them.
  - **Auto-views** from data — by reflection or LLM code generation.
  - **Smallest system that replicates current CLI functionality**, extracting core + added stuff,
    composed, that eventually lets us **remove .dark files**, and **involves the parser** (`ref`,
    composable Dark parser, `compile` builtin — section 2).
  - **Crons as a distributed app** we officially support; **daemons** via `start()`. One projection is
    the **list of conflicts** (usually ignorable).
  - Respect special types: **runtime tests/constraints**, **at-rest constraints/tests** (cf. Scriptorium).

### 8c. Async design doc
- [x] One place for the Task/Ply-replacement: the consolidated design **and** a concrete migration
  sketch, thread parking, nested-process management, opt-in debug symbols. Informed by the coworker's
  async doc (8e/9e below). **No heavy .NET reading for now.** Don't scatter async decisions elsewhere.

---

## 9. Docs to update in place (lower priority / research)

### 9a. VIEW-SKETCHES.md
- [ ] **"Beautiful — extend it wildly"** with more recent ideas. **Do not remove anything already perfect.**

### 9b. COMPOSABLE-MVU.md
- [ ] **One big composed App, not a composed Model** (section 2 App type) with a runner (F#/Dark/combo) fitting
  the sync picture; relates to op-playback. May flatten into the section 8b distributed-op-playback doc.

### 9c. package-system-layers.md (vault `Current Experiment/`)
- [ ] Feels outdated — salvage the useful. Redesign: the **"layers" should be composed/composable
  ops/apps/projections.** Iterate heavily.
- [ ] **"Harmful" notifications** (flagging bad fns) = an event-stream/system (opt-in extension or
  built-in). **Package dependencies are just one projection.** Ops can be communicated **through an
  instance even if** a specific extension/runner isn't activated on it. **Drop the "shared table
  shape"** — each thing may need its own projection considerations.

### 9d. research/beam-vs-dark.md
- [ ] Update given thoughts elsewhere; **make it shorter.** **Do this in a background agent** so it can't
  accidentally update other docs. Develop the **mailbox** idea — F#'s mailbox processor, used
  plan9/Smalltalk-style, distributed for Dark.

### 9e. Coworker's "Dark Async Plan.md" (review only — **DO NOT EDIT her doc**)
*(`Current Experiment/Design/Dark Async Plan.md`; related: `Execution/Design - Async Execution.md`, `90.Stachu/newest/ply-replacement/`.)*
- [ ] Read & form an opinion: how does it relate to event streams, playback/projections, sync? Use it
  (and the `ply-replacement/` notes) to inform **our** async doc (8c) — not hers. Do it in a background
  agent if it risks touching other docs.

---

## 10. Vault organization & cross-system hygiene

- [ ] Decide what migrates from the repo into the Obsidian vault vs stays in-repo (default: in-repo
  `notes/`, per the settled decision).
- [ ] **Organize the recently-added vault material**, especially the **`90.Stachu/` folder** (messy:
  `even-newer/`, `newest/`, `latest/`, `may8/` — overlapping snapshots). Reconcile/archive superseded
  snapshots.
- [ ] Net target restated: **fewer + smaller .md files, less repetition, no outdated phrasings.**

---

## 11. Final deliverable

- [ ] Produce the list of `.md` files to **`print-md`** and re-review — **everything touched in this
  wave**, plus the rewritten `next-steps.md` (5i). Ensure that `next-steps.md` exists and is
  thin/directional.

---

## 12. Iteration & deepening — keep running until ~8am

This loop is meant to run **all night** (until at least ~08:00), doing **many** passes.
Completing the section 1-11 checkboxes once is **not** the end — it is the start of the
deepening phase. Do **not** print and do **not** stop just because boxes are checked.

After the first full sweep, each pass should pick the weakest/thinnest area and make it
better. Rotate through:

- [ ] **Re-review every doc against `source/feedback.md`** — the master spec. Verify each
  bullet there is genuinely addressed; deepen anywhere that's thin. Re-read feedback.md
  every few passes.
- [ ] **Tighten + de-duplicate** — hunt repeated content across `design/`; consolidate so
  little lives in more than one place. Fewer, smaller, tighter files.
- [ ] **Cross-link cleanup** — fix any links pointing at old UPPERCASE names or dissolved
  files; make `design/` self-consistent.
- [ ] **Deepen the keystone thinking** — the `App` type's full field set (msg/cmd as the
  MVU layer, autoResolutions, constraints, projections/DBs), hot-swappable op-playback,
  the ops-vs-projections storage split. Push these further each pass.
- [ ] **Dissolve remaining un-todo'd source copies** — COHABITATION, HOT-RELOAD,
  REMOTE-ACCESS, TOC, research/swamp-vs-dark, research/visibility-vs-dark have no explicit
  todo: decide per-doc to migrate-as-is into `design/` (tightened) or drop, and note why.

**Only run section 11 (final print) when it is genuinely ~08:00 (or the user returns).**
Until then, keep iterating — quality over completion.

---

## Running the loop

When it's time to execute (the long process), point a fresh session's loop at this file:

```
/loop 5m Execute the open todos in this plan (notes/loop-work/loop-instructions.md). SANDBOX RULES:
edit ONLY files under notes/loop-work/outputs/ (plus this loop-instructions.md, for check-offs and
status); read notes/loop-work/source/ as frozen reference (never edit it); NEVER touch the real
pdd-thinking/ or the Obsidian vault. Work top priority first (Stable & Syncing, then the Removing-.dark
blockers), then continue through ALL remaining todos. In outputs/, build the new notes structure
(design/, projects/, results/, issues-and-improvements/, meta-reflections/) — create, rename, dissolve,
rewrite as each todo says — and check items off here as you complete them. Do a LARGE batch each pass:
keep working until a natural stopping point, then commit locally (never push) and leave a one-line
status. DO NOT print-md anything mid-loop — printing happens ONLY at the very end (section 11), once
every checkmark is done. This loop runs for HOURS/overnight; per-pass printing would flood the printer.
Prioritize thoroughness and correctness over speed — do NOT cut any corners to save time; it is fine for this to
run into tomorrow. Surface blockers. Only stop when every todo is done or genuinely blocked.
Rules: never use the section-sign symbol in prose (write "section N"); never git stash or reset --hard.
```

**On cadence (for overnight throughput):** the interval is the *gap between passes*, not the work done
per pass — and a pass only fires when the session is idle, so it can never overlap itself. A long
interval therefore just **wastes idle time** between passes; it does not make any pass do more. To
maximize overnight work, do the opposite: keep the interval **short (~5 min)** so the next pass kicks
off promptly after the previous one finishes, and make **each pass do as much as it productively can**
before committing. Passes that run longer than 5 min simply chain back-to-back (the next fires at the
first idle moment). Net effect: near-continuous work with a commit checkpoint per pass.

---

### Still to locate
- HttpClient restriction notes (referenced in CAPABILITIES) — not yet found in the vault.
- `feedback-from-agent.md` — not yet located; may not exist under that exact name.
- A few sentences in the original `feedback.md` trailed off mid-thought (the "we need a clean …", the
  results-subdir qualifier, and plan.md batch 2's "…and at the end") — intent of those dangling clauses
  is unknown; worth a glance if anything below feels like it's missing a piece.

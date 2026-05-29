# Open decisions register

Every unsettled design decision across `design/`, gathered into one triage list so
they can be decided deliberately rather than rediscovered per doc. Each links its
home doc and notes the current **lean** where one exists. Ordered by leverage:
the ones that gate the Stable & Syncing path come first.

## Highest-leverage — gate the Stable & Syncing path

- **Conflict-blind vs. conflict-carrying op-playback** — does the generic playback
  engine ignore conflicts (each projection handles its own) or does the `App` carry
  `conflict`/`resolve`? *Lean: carrying, for ergonomics; settle on the first real App.*
  ([distributed-event-sourcing.md](stable-and-syncing/distributed-event-sourcing.md))
- **Does WIP sync, and how does it fold?** The recurring one (appears in three docs).
  Local-only (option a, never folds into cross-instance conflicts) vs. full op
  treatment (option b, folds and can clash). *Lean: local by default, opt-in to share;
  concretely "which store" — `local.db` vs `ops.db`.*
  ([conflicts.md](stable-and-syncing/conflicts.md), [sync.md](stable-and-syncing/sync.md), [distributed-event-sourcing.md](stable-and-syncing/distributed-event-sourcing.md))
- **Async core before or under the event bus** — replace Ply with our own scheduler
  first, or build the bus on Ply now and swap later? *Open; bus should depend on the
  interface, not Ply.* ([async.md](stable-and-syncing/async.md), [event-bus.md](stable-and-syncing/event-bus.md))
- **Default sync target** — auto-sync to the server on install, or opt-in? *Lean:
  opt-in, local-first (mirror git `remote add`).* ([sync.md](stable-and-syncing/sync.md))
- **Sync granularity** — *lean: per-branch with namespace as a filter.* ([sync.md](stable-and-syncing/sync.md))
- **Inbound-conflict dispatch timing** — resolve arriving conflicts immediately, or
  batch and surface on next CLI interaction? *Per-session policy.* ([sync.md](stable-and-syncing/sync.md), [conflicts.md](stable-and-syncing/conflicts.md))

## Conflicts & dispatch

- **Who installs auto-rules** — *lean: a stack, user → session → branch → namespace
  owner → system default.*
- **`Park` granularity** — *lean: per-frame, events through the scheduler.*
- **Resolution strategies vs. a single resolution** — a conflict may have several
  ordered fallback resolutions; likely needs a `ResolutionStrategy`.
- **Parse-time uniformity** — defer-to-runtime (placeholder + run-time conflict) vs.
  an explicit `ParsePolicy`. ([conflicts.md](stable-and-syncing/conflicts.md))

## Event bus & async

- **Backpressure** (producer outpaces consumers), **cancellation** (timeout vs.
  resolving-event race), **ordering** (cross-producer determinism), **GC** (parked
  frame that never wakes). ([event-bus.md](stable-and-syncing/event-bus.md))
- **Multi-emit vs single-emit** type distinction (`EventBus<T>` vs `Promise<T>`).
- **`Stdlib.Bus` / `Stdlib.Promise` surface** — full shape TBD.
- **Branch-failure policy** — cancel siblings eagerly vs. finish-and-aggregate. *Lean:
  structured-concurrency cancel, opt-in aggregate.*
- **Effect metadata** — persisted vs. inferred/cached at load.
- **Streams** — affine at the language level vs. runtime-locked.
- **Auto-parallelization** — default-on vs. flag. ([async.md](stable-and-syncing/async.md))

## CLI daemon

- **Apps-runtime coupling** to the daemon; **idle shutdown** policy; **protocol
  sharing** with the sync wire; **multi-branch per session** confirmation.
  ([cli-daemon.md](stable-and-syncing/cli-daemon.md))

## Capabilities

- **HttpClient restriction grammar** — the detailed spec is said to live in a vault
  note. **BLOCKED: location not found** (see [vault-organization.md](meta/vault-organization.md)).
- **Other CLI capability shapes** (`Cli`/`CliHost`) — need their own investigation pass.
- **FileSystem** — ships as a boolean; structured per-directory/read-write scoping is
  future. ([capabilities.md](stable-and-syncing/capabilities.md))

## Apps surface

- **App declaration** — an explicit `AppManifest` op vs. a derived convention. *Lean:
  explicit op (so it syncs/forks).*
- **Install = trust** — how much the install flow surfaces about caps it grants.
- **Versioning + update**; **discovery** (public namespace / registry / shared ref).
  ([apps-surface.md](stable-and-syncing/apps-surface.md))

## MVU, views, structural editor

- **Render-target abstraction** (`View` node set; F# adapter vs Dark renderer);
  **runner placement** (how much F# vs Dark); **`Stdlib.UI` content**; **real-time
  collaboration** (concurrent intent translators = concurrent ops).
  ([composable-mvu.md](stable-and-syncing/composable-mvu.md))
- **Editor**: op granularity vs. typing latency; where the tiny model runs;
  holes-vs-partial-text; the parser/`compile` dependency. ([structural-editor.md](editing-software/structural-editor.md))

## Remote access

- **`dark on` syntax**; **stream encoding** (*lean: chunked HTTP*); **pair-share device
  control** (*default: cap-denied*); **tailnet-vs-internet boundary**.
  ([remote-access.md](stable-and-syncing/remote-access.md))

## Hot reload

- **Break surfacing** (cause vs. effect site — *lean: both, linked*); **blast-radius
  UX** (branch-only vs. push-to-dependents); **in-flight frames** (*lean: finish on old
  body, apply new on next call*). ([hot-reload.md](stable-and-syncing/hot-reload.md))

## Cohabitation & identity (longer-horizon)

- **Identity at scale** (autonomous agents); **multi-tenant boundaries**; **adversarial
  agents** (revocation as the immune system); **what constitutes "an app"** (continuum).
  ([cohabitation.md](stable-and-syncing/cohabitation.md), [identity.md](stable-and-syncing/identity.md))

## Dark virtual files (implementation-detail tier)

17 open questions, mostly cross-platform implementation choices (file-per-item vs
per-module, frontmatter vs sidecar, branch representation, conflict UX, `git`
interaction, reserved-name/case handling, write-back dances). They live in
[dark-virtual-files.md](editing-software/dark-virtual-files.md) and don't gate anything else —
decide them when that doc is picked up.

## Projects & bench

- **Brownfield emulation.** All 125 project specs are `greenfield`; the bench currently
  cannot measure an agent *changing existing code* — yet the master feedback calls this
  "very important" and "unsolved." Open: add brownfield projects (start from a seeded
  prior state, score the *diff* against a gold change) and figure out how to evaluate
  "modified existing code correctly." Flagged in [projects/README.md](pdd/projects/README.md).
- **Bench size-tiers.** The harness assigns trivial/small/medium/large buckets for
  balancing ([ai-coding-target.md](pdd/ai-coding-target.md)) — how to assign them and
  how many per tier is unsettled (a tuning question, not a spec field).

---

**Blocked (need an external input, not a decision):** the HttpClient restriction-spec
vault note and `feedback-from-agent.md` are not locatable; vault reorganization is
off-limits to the loop (recommendation in [vault-organization.md](meta/vault-organization.md)).

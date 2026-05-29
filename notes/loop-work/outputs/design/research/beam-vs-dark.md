# BEAM / Elixir vs Dark — distribution, hot-load, mailboxes, and "tiny F#, everything Dark"

*research note*

## The question

1. Does BEAM/Elixir distribute **source code**?
2. Which of its ideas apply to Dark?
3. Validate the goal: a **minimal F# substrate**, everything else in Dark on a sync base.

## Short answer: BEAM distributes state, not source

BEAM is famously good at distributing **state and messages** — not **source code**. That gap is what Dark fills.

What BEAM gives you:

- **Per-node hot code loading.** `code:load_file` swaps compiled bytecode at runtime; *"current"* and *"old"* versions coexist, processes transition on a fully-qualified call, and a third load purges the old.
- **`-on_load`** runs an init fn when a module loads — a clean substrate-level registration hook.
- **Releases + `.appup` + `release_handler`** — declarative, machine-checked upgrade scripts moving a release from N to N+1, per node.
- **Cross-node remote load** (`nl/1`) and **Mnesia** for distributed data — both cluster-scoped, rarely the deploy story.

What BEAM does **not** give you:

- A content-addressed, peer-syncable **source-of-truth for code**. Each node deploys independently; "distribution" is the deploy pipeline, not a runtime sync protocol.
- A substrate where *agent X edits a fn on instance Y and instance Z sees it in seconds.*

So Dark goes one layer deeper: the **code itself** is the sync unit — content-addressed values, named locations, ops, and events on the event-bus. This isn't a BEAM defect; BEAM was built for telecom switches redeployed quarterly. Dark is built for agents and humans co-editing live. Different problem, different shape.

## What Dark should steal

BEAM has shipped concurrent, fault-tolerant, hot-upgradable systems for ~40 years. The durable ideas:

### 1. Two-versions-in-flight, with a bound

BEAM's *current* + *old* handles "I changed this fn mid-call." Dark gets this almost free: frames holding `Package(hash)` X keep X; new resolves pick up Y. **What's missing:** an explicit upper bound — at most N old versions live, substrate purges beyond. BEAM's "third load purges old" is the discipline against memory leaks; Dark needs per-frame retention, GC of unreachable bodies, and an observable count of frames pinned to stale hashes.

### 2. `on_load` as a registration hook

Every BEAM module can register itself on load. Dark equivalent: a package declaring `onLoad : unit -> Result<unit, String>` firing when first reachable — an app registering with the app-switcher, a tool-wrapper registering in a registry, a subscription wiring its effects, a migration declaring applicability. Makes default-view-per-type registration explicit and gate-able.

### 3. Supervisor trees + "let it crash"

OTP supervises every process and restarts children by declared strategy. Dark has the primitives (frames, parking, conflict dispatch) but not the organizing principle. Concretely: every long-running app (viewer, agent runtime, materializer) runs under a *supervision strategy expressed as a Dark value*; agent failures route to the owner-as-supervisor whose declared policy decides restart/revise/escalate. Conflict dispatch is already supervisor-shaped — generalize it to app-lifecycle level.

### 4. GenServer as the canonical MVU shape

GenServer is essentially MVU: `init` → `init`; `handle_call`/`handle_cast` → `update msg model`; `handle_info` → bus subscription delivering a Msg; `terminate` → teardown effect; `code_change` → the hot-reload contract. The one to steal explicitly is **`code_change`**: a fn that runs when an app's update logic changes mid-run, taking old Model + new code, returning a migrated Model. "Keep the Model" hand-waves the case where the update fn's shape changes; `code_change` is the contract.

### 5. Releases as upgrade choreography

An `.appup` declares the steps from version A to B: load these, delete these, suspend, migrate, resume. Dark's ops are the building blocks but lack a named-release wrapper. Equivalent: a `Release` entity bundling "these ops, in this order, with these conflict resolutions and this in-flight migration fn" — the natural home for "curated bundle published centrally, instances pull-and-apply" (releases-as-PRs for the substrate itself).

## The mailbox model — inspiration, not commitment

BEAM's deepest idea is the **mailbox**: every process has one, `Pid ! Msg` enqueues, `receive` pulls, and it's the *only* channel. Because `Pid` is location-transparent (local or remote, same API), the mailbox is BEAM's distribution superpower.

F# offers a close native analog in **`MailboxProcessor`** (`agent.Post msg` / `let! msg = inbox.Receive()`) — a serialized, single-consumer queue with its own loop. Taken plan9/Smalltalk-style — *everything is a process with a mailbox, all interaction is message-passing* — and made distributed, this is a coherent alternative framing of Dark's concurrency story.

How it relates to the **event-bus + parking** model we're leaning toward:

- **Mailbox = addressed/point-to-point; bus = published/broadcast.** A mailbox targets one recipient (`appId`); the event-bus publishes to a stream many subscribers can read. Our `Bus.send appId msg` is already mailbox-shaped; `Bus.publish stream event` is the broadcast complement. The two aren't rivals so much as the addressed and broadcast ends of one routing substrate.
- **Parking ≈ selective receive.** Erlang's `receive` can match on message shape and leave others in the mailbox; our parking holds a frame until an awaited event arrives. Parking is selective-receive expressed over durable events rather than transient messages — which is the key difference: **our messages are durable, content-addressed ops/events, not transient mailbox sends.** A parked frame survives disconnect; a message sitting in a dead process's mailbox does not.
- **Location-transparency is the shared win.** If the substrate routes by `appId`, a remote `appId` is the same code path as local — exactly BEAM's superpower, and exactly what the wire protocol wants (see `STABILITY-AND-SHARING.md`).

Where the analogy stops: we are deliberately **not** adopting Ply or a generic async/await for the wait. Dark uses its **own async/parking** so that a suspended computation is a first-class, inspectable, syncable thing — a parked frame in the substrate, not an opaque continuation on a thread. F#'s `MailboxProcessor` is a fine *implementation* primitive for the F# layer's per-app Msg queue, but the *semantics* we expose to Dark are bus + parking, not raw mailboxes. The lesson to steal is the **discipline**: make message-passing the only cross-app channel — no direct calls between apps, no reference-equality on another app's Model — so location-transparency stays cheap.

## Where Dark already wins

- **Content-addressed values.** BEAM modules are name-addressed; `Package(hash)` is immutable-by-construction. No two values with the same hash disagree — obsoletes a class of `appup` machinery.
- **Ops as durable, syncable, conflict-resolvable units.** BEAM's layer carries transient messages and remote calls; Dark's carries ops that survive disconnect-reconnect in a way `gen_server:call` to a partitioned node cannot.
- **Capability gating at the call site.** BEAM has effectively none; Dark's per-builtin cap tags + per-agent grants are stronger.
- **Cohabitation as first-class.** OTP sees a cluster of equal VMs; Dark sees agents and humans with intent, delegation, and identity.

## "Tiny F#, everything else Dark" — strong yes

This is exactly the BEAM/OTP playbook: ~100K lines of C under ~500K lines of Erlang. The C layer stays frozen and auditable; the value lives in the layer free to change. That separation is why BEAM lasts.

The F# layer should do only:

1. **Op application + persistence** — validate, apply to SQLite, fire `BodyChanged`.
2. **Event dispatch + bus routing** — subscribers, batches, transaction-end markers (see `EVENT-STREAMS-AND-PARKING.md`).
3. **The MVU loop + async/parking** — drain a Msg queue, call `onMsg`, apply effects, park/resume frames, render deltas. (`MailboxProcessor` is a reasonable host for the per-app queue.)
4. **Effects executor** — a small routed set: `PublishToBus` / `SubscribeTo` / `SaveState` / `Spawn` / `Exec` (cap-gated).
5. **Capability check** — set-difference at the call site; denials through conflict dispatch.
6. **Sync transport** — read/write ops over the wire, content-addressed for dedup.
7. **Render adapter** — one per target (Terminal first), with the `View` tree Dark-defined.

Everything else is Dark: the thin **App** type and its instances, the materializer/PDD daemon, viewer/trace-inspector/merge UI, the agent runtime + plan/goal/trace machinery, the `Stdlib.UI` view library, conflict-resolution policies, supervision trees, releases, typed-tool wrappers.

Why it works: the sync base + MVU + caps + identity are the substrate's **grammar**; everything above is **vocabulary**. Hot-reload covers everything-above-F# (the F# layer ships per binary upgrade); cap checks stay enforceable because they live in F#; sync stays bounded because the ops vocabulary is small and frozen; and the substrate is auditable in a one-person review.

**The discipline to commit to:** resist pushing logic *down* into F# for performance or convenience. Heuristic — if the new thing can be expressed with existing F# primitives (ops, events, effects, caps, MVU, parking), it goes in Dark. Growing the F# layer is a deliberate "we support this forever" decision. OTP has held this line for 40 years; it's the single biggest reason Erlang systems still run after decades.

## Headline

BEAM solves runtime mutation per node and message-passing between nodes — not source distribution. Dark goes further: the **code itself** is the syncable, content-addressed, conflict-resolvable unit. Steal BEAM's durable lessons — two-versions-in-flight, `on_load` registration, supervisor trees, GenServer's `code_change`, releases as choreography, and the mailbox discipline of message-passing as the only channel — but express the wait with **our own async/parking over durable events**, not Ply or raw mailboxes. And keep the F# layer tiny and frozen; let everything above evolve.

---

**Sources**

- https://www.erlang.org/doc/system/code_loading.html
- https://www.erlang.org/doc/system/release_handling.html
- https://blog.appsignal.com/2021/09/14/application-code-upgrades-in-elixir.html
- https://elixirschool.com/en/lessons/advanced/otp_distribution
- F# `MailboxProcessor` (FSharp.Control)
- `HOT-RELOAD.md`, `COMPOSABLE-MVU.md`, `STABILITY-AND-SHARING.md`, `IDENTITY.md`, `EVENT-STREAMS-AND-PARKING.md`

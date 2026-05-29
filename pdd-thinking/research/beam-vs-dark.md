# BEAM / Elixir vs Dark — Source distribution, hot-load, and the "tiny F#, everything else Dark" idea

*2026-05-26 · research note*

## The question, sharp

1. Does BEAM/Elixir solve for **distribution of source code** at all?
2. How would its ideas apply to Dark?
3. Validate the goal: a **minimal F# Elmish substrate**, with **everything else above** written in Dark on top of a sync base.

## Short answer to (1): no, not really — and that's the gap Dark is filling

BEAM is famously good at distributing **state and messages**. It is **not** designed to distribute **source code**.

What BEAM gives you out-of-the-box:

- **Per-node hot code loading.** `code:load_file(Module)` swaps a `.beam` (compiled bytecode) at runtime. Two versions coexist — *"current"* and *"old"* — and processes transition by making a **fully qualified call** (`Module:function()`). A third load purges the old version and kills any process still pinned to it.
- **`-on_load` directive** runs an init fn the moment a module loads. Returning `ok` activates the module; anything else unloads it. A clean substrate-level "extension registration" hook.
- **Cross-node remote load.** `nl(Module)` broadcasts a module load to every connected node, and `c:nl/1` plus `slave` / `peer` nodes let you remote-compile-and-load. Useful in clusters; rarely the production deploy story.
- **Releases + `.appup` + `release_handler`.** Structured *upgrade scripts* (`relup`) that move a release from version N to N+1, per node, with declarative module-add/delete/reload + start/stop instructions. Distinct from "git syncing source between nodes."
- **Mnesia** for distributed *data* (full-mesh replication, doesn't scale past a handful of nodes, classic split-brain risk).

What BEAM does **not** give you:

- A canonical, content-addressed, peer-syncable **source-of-truth for code.** Each node is deployed independently (rebar3 / mix release / Distillery / Deployex). The "distribution" is the deploy pipeline, not a runtime sync protocol.
- A single substrate where *"agent X edits a fn on instance Y, and instance Z sees it in seconds."* That's the explicit promise of Dark's STABILITY-AND-SHARING + ops-on-the-wire + `matter.darklang.com`.

**So Dark is genuinely beyond BEAM here.** BEAM solves runtime upgrade-per-node + message-passing-between-nodes; Dark is going one layer deeper — **the code itself is the sync unit**, content-addressed values + named locations + ops + events.

This isn't a defect in BEAM. BEAM was designed for *telecoms switches you redeploy quarterly with months of testing*. Dark is designed for *agents and humans co-editing a substrate, live*. Different problem; different shape.

## What BEAM does solve, and what Dark should steal

BEAM has been live-shipping concurrent, fault-tolerant, hot-upgradable systems for ~40 years. The ideas worth stealing aren't about code distribution — they're about what makes a "low-level Elmish app construct" actually durable in the wild.

### 1. Two-versions-in-flight as a runtime invariant

BEAM's *"current"* + *"old"* module versions are how it handles "I just changed this fn while a process is in the middle of calling it."

`pdd-thinking/HOT-RELOAD.md` already names this: the "finish-then-update" policy is explicitly *"matches Erlang/BEAM semantics."* Dark's `PackageID`-by-location vs `Package(hash)` story gets this almost for free — frames holding hash X keep hash X; new resolves pick up hash Y.

**What's missing on the Dark side:** an explicit *upper bound* — at most N old versions live at once; the substrate purges beyond that. BEAM's "third load purges old" is the discipline that prevents memory leaks in long-running systems. Dark needs the same: per-frame retention, GC of unreachable old bodies, observable count of "frames pinned to stale hashes" in the viewer.

### 2. `-on_load` as a substrate hook

Every BEAM module gets a chance to register itself the moment it loads — claim a name, start a process, install a callback, refuse to load if its preconditions aren't met.

Dark equivalent: a package can declare an `onLoad : unit -> Result<unit, String>` that fires when the package first becomes reachable in the substrate. Use cases:

- An app registers itself with the runtime's app-switcher.
- A typed-tool wrapper (per swamp's Models idea) registers itself in a tool registry.
- A subscription package wires its `Subscribe` effects.
- A migration package declares its applicability before being applied.

`Stdlib.UI.view` polymorphism (COMPOSABLE-MVU §"Default view per thing") implicitly needs this: a new type publishes a default view by *being loaded*. `onLoad` makes the registration explicit and gate-able.

### 3. Supervisor trees + "let it crash"

OTP's killer pattern: every process is supervised; supervisors restart children on failure with declared strategies (`one_for_one`, `rest_for_one`, `one_for_all`, escalation upward).

Dark's MVU + EventBus + parking already has the *primitives* (frames, parking, conflict dispatch with `FailLoudly` / `Park` / `AskHuman`) but not the **organizing principle**. The cohabitation framing says every app is an inhabitant; OTP's lesson is that every inhabitant has a *supervisor* that knows what to do when it fails.

Concretely for Dark:

- Every long-running app (PDD viewer, agent runtime, materializer) should run under a *supervision strategy* expressed as a Dark value.
- Agent failures (per IDENTITY §`AgentStatus = Failed of reason`) should route to the agent's *owner-as-supervisor*. The owner's policy (declared once) decides: restart with same goal? Restart with revised plan? Escalate?
- Conflict dispatch is already supervisor-shaped; generalize the pattern to the lifecycle-of-an-app level, not just the call-site level.

### 4. The mailbox as the universal Msg queue

Every BEAM process has its own mailbox; `Pid ! Msg` puts a message on it; `receive` pulls. The mailbox is the **only** communication channel. Combined with location-transparency (`Pid` can be local or remote, the API is identical), this is BEAM's distribution superpower.

Dark's MVU loop already has a Msg queue per app (COMPOSABLE-MVU §"F# substrate sketch"). The BEAM lesson: **make it the only channel.** No direct function calls between apps. Every cross-app interaction is `Bus.send appId msg` or `Bus.publish stream event`. The substrate enforces it; you can't get reference-equality on another app's Model.

This is also what makes location-transparency cheap: if the substrate routes by `appId`, then `appId` resolving to a remote instance is the same code path as local. Aligns directly with `STABILITY-AND-SHARING.md`'s wire protocol.

### 5. GenServer as the canonical MVU shape

OTP's `GenServer` is essentially Elmish:

| GenServer callback | MVU equivalent |
|---|---|
| `init/1` | `init` |
| `handle_call/3` (sync, with reply) | `update msg model` returning a reply |
| `handle_cast/2` (async, no reply) | `update msg model` returning `()`-typed Msg |
| `handle_info/2` (out-of-band messages) | bus subscription delivering a Msg |
| `terminate/2` | teardown effect |
| `code_change/3` | the hot-reload contract |

`code_change` is the one Dark's COMPOSABLE-MVU should explicitly steal: **a fn that runs when the app's update logic changes mid-run**, given the old Model + new code version, returning a migrated Model. Today HOT-RELOAD.md hand-waves "keep the Model"; in reality, Models occasionally need migration when the update fn's shape changes. `code_change` is the contract.

### 6. Releases + `appup` as the upgrade choreography

A BEAM `.appup` file declares the steps to go from version A to version B: load these modules, delete these, suspend these processes, run these migration fns, resume. **Declarative upgrade**, machine-checked.

Dark's ops are *the building blocks* of an upgrade choreography but don't yet have a "named release" wrapper. Equivalent for Dark: a `Release` package-store entity that bundles "these ops, in this order, with these conflict resolutions, with this migration fn for in-flight frames." Allows the network to negotiate *upgrade compatibility* between peers (per STABILITY-AND-SHARING).

This is also the natural home for "matter.darklang.com publishes a curated bundle, instances pull-and-apply." Roughly: releases-as-PRs, but for the substrate's own version.

## Where Dark already wins over BEAM

Worth naming, so the inspiration-borrowing doesn't sound one-directional:

- **Content-addressed values.** BEAM modules are name-addressed; collisions are nominal, evolution is by `.appup`. Dark's `Package(hash)` is immutable-by-construction — no two values with the same hash can disagree. This obsoletes a whole class of `appup` machinery.
- **Ops as durable, syncable, conflict-resolvable units.** BEAM's distribution layer carries messages (transient) and supports remote calls. Dark's sync layer carries ops (durable) with content-addressed identity. Ops survive disconnect-reconnect cycles in a way `gen_server:call/2` to a partitioned node fundamentally doesn't.
- **Capability gating at the call site.** BEAM has none; security is by separate-OS-process or by `safe` mode (rarely used). Dark's per-builtin cap tags + per-agent grants are stronger than what BEAM offers natively.
- **Cohabitation as first-class.** OTP treats nodes as a cluster of equal Erlang VMs; Dark treats inhabitants as agents and humans with intent + delegation + identity. The richer model.

## The "tiny F#, everything else Dark" idea — strong yes, with one BEAM-flavored discipline

The user's proposal: a really low-level Elmish "app" construct, F# kept minimal, **everything else** written in Dark, all riding on a sync base.

This is **exactly the BEAM/OTP playbook.** BEAM is ~100K lines of C. OTP is ~500K lines of Erlang on top. The C layer has stayed remarkably stable over decades; OTP and everything above evolves freely. This separation is what makes BEAM's longevity possible: the substrate is small enough to be **understood, audited, and frozen**; the value lives in the layer that's free to change.

Dark's COMPOSABLE-MVU §"F# substrate sketch" already targets ~500 LoC of F# for the MVU loop, effects executor, render adapters, and persistence. That's the right order of magnitude — and the *discipline* to learn from BEAM is to keep it there. Every primitive you add to the F# layer is one fewer you can change without breaking every Dark app above.

### The minimum the F# layer must do

Borrowing from BEAM's "what's actually in the VM":

1. **Op application + persistence.** Take an op, validate it, apply it to SQLite, fire `BodyChanged`. Already exists.
2. **Event dispatch + bus routing.** Subscribers, batches, transaction-end markers. Per EVENT-STREAMS-AND-PARKING.
3. **The MVU loop.** `tick : Loop -> Loop`, draining a Msg queue, calling `onMsg`, applying Effects, rendering deltas.
4. **Effects executor.** A small set of effects routed to subsystems: `PublishToBus` / `SubscribeTo` / `SaveState` / `Spawn` / `Exec` (cap-gated).
5. **Capability check.** Set-difference at the call site; route denials through conflict dispatch.
6. **Sync transport.** Read/write ops over the wire to peers, with content-addressing for dedup.
7. **Render adapter.** One adapter per target — Terminal first, Web later, reMarkable later. The `View` tree is Dark-defined; the adapter is F#.

That's it. **Everything else in Dark.** Including:

- The materializer / PDD daemon (Dark code).
- The viewer, trace inspector, SCM merge UI (Dark apps).
- The agent runtime + plan/goal/trace machinery (Dark, on top of MVU).
- The `Stdlib.UI` view library + default-view-per-type (Dark).
- The conflict resolution policies (Dark, plugged into the dispatch).
- The supervision trees (Dark, expressed as values).
- Any new typed-tool wrappers, workflows, releases (Dark).

### Why this works

The sync base + MVU + caps + identity are the substrate's *grammar*. Everything else is *vocabulary* written in that grammar. BEAM proved this scales: 40 years of Erlang, billions of telephone calls, and the C layer has barely needed to change. The vocabulary on top is what evolves.

For Dark specifically:

- **Hot-reload works for everything-above-F#** because the F# layer doesn't change at runtime (it ships per binary upgrade), but everything in Dark is fair game for live reload. This is the right asymmetry.
- **AI-opt-in caps stay enforceable** because the cap check is in the F# layer — Dark apps can't sneak around it.
- **Sync stays bounded** because the ops-on-wire vocabulary is small and frozen at the F# layer; Dark apps speak only in terms of ops.
- **The substrate is auditable** by any team that wants to verify it — 1000ish lines of F# is a one-person review, not a quarter-long project.

### The one BEAM-flavored discipline to commit to

**Resist every temptation to push logic *down* into F# for performance, convenience, or "just this once."**

OTP teams have made this rule for 40 years; it's why Erlang/OTP stays small and OTP stays evolvable. Every Dark-side concern that *could* live in F# but *should* live in Dark is a future hot-reload win, a future audit win, a future agent-can-edit-it win.

A practical heuristic: if the new thing **can be expressed using only existing F#-layer primitives** (ops, events, effects, caps, MVU), it goes in Dark. Only when none of those compose to the need do you grow the F# layer — and that needs to be a deliberate, reviewed, "we're committing to support this forever" decision.

## The headline

BEAM doesn't solve source-code distribution; it solves *runtime mutation per node* and *message-passing between nodes*. Dark's substrate is going further — making the **code itself** the syncable, content-addressed, conflict-resolvable unit. But every long-lived idea BEAM has shipped applies: **two-versions-in-flight discipline**, **`on_load` for package registration**, **supervisor trees for app lifecycle**, **mailboxes as the only channel**, **GenServer's `code_change` contract for in-flight upgrades**, **releases as named upgrade choreography**.

And the deepest BEAM lesson — the one that validates the user's whole proposition — is: **keep the C/VM layer tiny and frozen; let everything above evolve freely.** Dark's target of ~500–1000 LoC of F# is in the right zip code. The discipline to learn is not a new design — it's **never letting that number grow without a deliberate substrate-version commitment.** OTP has held this line for 40 years and it's the single biggest reason Erlang systems still run after decades. Dark can have that too.

---

**Sources**

- https://www.erlang.org/doc/system/code_loading.html
- https://www.erlang.org/doc/system/release_handling.html
- https://blog.appsignal.com/2021/09/14/application-code-upgrades-in-elixir.html
- https://medium.com/flatiron-labs/intro-to-distributed-elixir-e8a259bcc8f6
- https://elixirschool.com/en/lessons/storage/mnesia
- https://elixirschool.com/en/lessons/advanced/otp_distribution
- `pdd-thinking/HOT-RELOAD.md` (cites "matches Erlang/BEAM semantics")
- `pdd-thinking/COMPOSABLE-MVU.md` (the ~500-LoC F# substrate sketch)
- `pdd-thinking/STABILITY-AND-SHARING.md` (wire protocol)
- `pdd-thinking/IDENTITY.md` (agent supervision)

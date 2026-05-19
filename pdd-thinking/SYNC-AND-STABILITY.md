# Sync & Stability

How conflicts+resolutions (`CONFLICTS-AND-RESOLUTIONS.md`) turn
into something usable across instances, machines, branches, and
collaborators — and **how that mechanism removes the need for
`.dark` files entirely** (package bootstrapping from a content
snapshot).

This is the doc that turns the substrate work into a real win for
the broader project. Conflicts+resolutions is the *primitive*;
sync is the *application* that justifies building the primitive.

## Definitions

- **Stable**: a thing is named, hashed (content-addressed), and
  persistent enough that others (instances, users, agents) can
  rely on it. The hash never changes; only the location pointing
  at it might.
- **Sharing**: handing a stable thing to another instance / user /
  agent, with conflicts machinery handling any disagreement that
  emerges en route.
- **Sync**: the ongoing exchange of operations between instances
  so that "stable" things converge.

## Sync model — events, not entities

The current LibMatter spec is the right shape: **sync operations,
not objects.** Three streams, each append-only and idempotent:

```
   BranchOps    (which patches in which branches)
       │
   PatchOps     (which parts in which patches)
       │
   PackageOps   (AddFunction, MoveItem, Deprecate*, …)  ← embedded in PatchOps
```

Events have an author, timestamp, monotonic sequence per source
instance, and content hash. Replaying them on any consumer
produces the same merged state — the system is effectively a
content-addressed event store.

**The wire never carries entities.** No "send me the current
`Foo.bar`" — instead "send me the ops; I'll derive `Foo.bar` from
the ops I now have." This is what makes sync robust to
disconnection, partial visibility, and divergence.

## Why this works — the conflicts shape

The set of possible sync-conflicts is **deliberately small** by
design. From the 2025-11-12 thinking:

> "The only conflict possible is name pointing to different
> definitions (hashes)."

Everything else is append-only and order-independent. Two patches
adding the same name to different content is the *only* genuine
sync conflict shape. Everything else — re-ordering, duplicate
events, missed events, late events — is resolvable by replay.

That single conflict shape flows through the dispatch from
`CONFLICTS-AND-RESOLUTIONS.md`. Two ops both want
`Foo.bar`'s location → emit `Conflict.OpVsOp`. The resolution is
chosen by:

- **Auto-rule**: trivial cases (one side is a rename of an
  identical body; ours-vs-theirs identical hash; etc.).
- **Policy**: namespace-owner-decides (if you're not the owner,
  your op becomes an *approval request* per the 2025-11-12
  workflow).
- **Park + ask human**: surface the side-by-side webview.
- **Fail loudly**: e.g. in CI / test mode.

Crucially: **syncing is never blocked by approvals or conflicts.**
The events sync first; the conflicts get surfaced and resolved
afterward. This decouples the "did we get the bytes" question
from the "do the bytes mean what we want" question. Better UX,
better fault tolerance.

## What crosses the wire

```fsharp
type SyncEvent =
  | PackageOpEvent of PackageOp × meta
  | PatchOpEvent   of PatchOp   × meta
  | BranchOpEvent  of BranchOp  × meta
  | PatchMergedEvent of patchId × meta
```

Plus, for **bootstrapping** a new instance (or onboarding a fresh
consumer), the merged state (the package tables + locations) as a
one-shot snapshot. After bootstrap, only events.

## Removing `.dark` files (package bootstrapping)

This is the unlock. Today's setup:

- All package source lives in `.dark` files in the repo
- LocalExec replays them via F# LibParser → seeds the SQLite tables
- A new instance starts by re-parsing the whole world
- Source-files-as-source-of-truth conflicts with the entire op-based model — `.dark` files are a parallel artifact that doesn't compose with sync, approvals, branches, or live edits

The target:

- A new instance bootstraps from a **content snapshot** —
  effectively `GET https://matter.darklang.com/data.db` (or an
  equivalent blob)
- The snapshot is the merged state at some point in time
- From then on, the instance subscribes to the event stream and
  catches up to head
- **No `.dark` files anywhere.** No LibParser at install time
  (it's only used by the editor for parsing user-typed code
  during edits)

Concretely:

```
First-time install:
  1. dark install
  2. → fetches https://matter.darklang.com/data.db (or pinned snapshot)
  3. → applies snapshot to local SQLite
  4. → subscribes to event stream from `last_sync_timestamp`
  5. ready

Subsequent boots:
  1. dark
  2. → pull events since `last_sync_timestamp` (if autosync on)
  3. → apply events; conflicts → dispatch
  4. ready

Dev workflow on the repo:
  - no .dark files
  - changes are ops produced by the editor
  - ops sync to a personal/branch namespace
  - PR-like flow is just "merge these ops to main"
```

The repo loses the `.dark` files entirely. What survives is:
- F# substrate code
- The schema migrations
- Tooling
- (possibly) reference snapshots for tests

This *also* means the LibParser → SQLite seeding logic disappears
from the boot path, which is a meaningful complexity win.

## Open questions on bootstrap

- **What ships in the snapshot?** Just `Stdlib`? Or every public
  package? Per-namespace? User decides at install time?
- **How do upgrades layer on?** A new `dark` binary might
  require a schema migration; the snapshot has to migrate too.
  Migration runs once on apply, then the event stream picks up.
- **How do local edits diff against the snapshot?** A user
  starts editing; their ops are produced relative to the snapshot
  state; on sync those ops are interleaved with whatever
  upstream events have happened since their snapshot timestamp.
  Standard event-sourcing replay.
- **Reproducibility**: pinning a snapshot lets you reproduce a
  state. Snapshots should be content-addressed too (snapshot
  hash + event sequence range = identity).
- **Trust**: how does the consumer verify the snapshot is what
  it claims? Sign it? Reference an event-stream that's
  independently verifiable?

## How PDD fits

The PDD spike learned something important here: **`Pending` →
`PackageID` → `Package(hash)` is the same shape as
WIP → committed-snapshot in SCM.** Now generalized:

- A WIP fn is a `PackageID` — referred to by location, not hash
- A committed fn is `Package(hash)` — content-addressed, stable,
  syncable
- The transition is the same `promote` operation as a normal
  SCM commit

Synced PDD work flows over the same channel as hand-authored
package items. There's no special PDD sync surface. Materialized
bodies are just *more* ops. The viewer (see `VIEW-SKETCHES.md`)
can show them with provenance ("this was materialized by gpt-4o
on 2026-05-13") but the wire format and the sync semantics are
identical.

## Conflicts gate sync

A sync that would introduce a `Conflict` doesn't unilaterally
overwrite — it routes through the dispatch (`CONFLICTS-AND-
RESOLUTIONS.md`). Concretely:

- A remote op arrives saying "`Foo.bar` now points to hash X"
- Locally, `Foo.bar` already points to hash Y (different)
- → emit `Conflict.OpVsOp` to the dispatch
- → auto-rule (e.g. namespace-owner-wins) fires, or
- → policy fires (e.g. "always prefer local for my namespace"), or
- → park + show webview with both sides + manual-merge
- The user picks; a new op is produced that resolves; resolution
  is recorded in trace

Same dispatch, same primitives, same audit trail. Sync becomes
just another consumer of the substrate.

## WIP and sync — the tension

(Carried from feedback.) Two postures:

- **(a) WIP stays local by default.** Doesn't sync. Clean — no
  half-baked state crossing the wire. But: "what if I want to
  share my WIP with myself on another machine, or with a
  coworker?" → needs an opt-in promote-to-shared.
- **(b) WIP syncs as a separate stream.** Different conflict
  rules (WIP-vs-WIP is fine; only WIP-vs-committed-divergence
  matters). But: every keystroke crossing the wire is too noisy.

Likely **hybrid**: WIP stays local; explicit "share my WIP"
publishes it as a *named draft* (a separate branch in the
existing branch model). The draft is a normal patch, just one
the namespace owner hasn't accepted. The 2025-11-12 approval
model already covers this shape.

## What this unlocks

- `.dark` files gone from the codebase (the headline)
- Cross-instance share of package work (matter.darklang.com as
  the central canvas)
- Pair programming on the same package store (real-time-capable
  per the sync-model design)
- PDD-on-a-server materializing for many clients (the daemon
  view from FRONTIER — "dark prompt starts a daemon, you watch
  it")
- Onboarding new users in seconds (snapshot + subscribe)
- Branch-as-namespace: every user has their own namespace; their
  ops sync to `matter.darklang.com`; merging to `main` is just
  approval by the namespace owner

## Open meta-questions

- **Schema for "in-progress"** at the snapshot level: do
  snapshots include WIP from anyone, or only committed? (Almost
  certainly only committed.)
- **What's the granularity of the snapshot blob?** Whole DB?
  Per-namespace? Per-module?
- **Authoritative ordering across instances**: the sequence
  number per source instance handles intra-instance order, but
  cross-instance is by author timestamp + author ID. Good enough?
- **The "deployless" claim** from the 2025-11-12 thinking: with
  ops flowing in real-time, every accepted op is effectively
  deployed. Caching, CDNs, downtimes — all gone. Does this
  actually hold? Probably yes for the language; need to think
  about externally-hosted apps that consume our packages.

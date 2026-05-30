# The apps surface (`dark apps`)

This is the doc the north star points at. The litmus test for the whole effort:

> Stachu's existing **`print-md`** script lives in Dark. He can inspect it, change it,
> and have those changes **sync** to his other computers. If Ocean wants, she can
> **fork** it. It shows up under **`dark apps`** as an installed app.

Everything else in the design tree is substrate; this is the surface a user actually
touches. It is built *on* the [App type](distributed-event-sourcing.md), runs *in* the
[cli-daemon](cli-daemon.md), syncs *over* [sync.md](../stable-and-syncing/sync.md), and is gated by
[capabilities](capabilities.md). The apps surface is ready to build once those land
(it may depend on the daemon work).

## What an "app" is

An app is nothing new — it is an [`App` value](distributed-event-sourcing.md)
(`name/empty/apply/conflict/resolve/views/invariants`) plus the package items it is
made of. `print-md` is an App whose `'op`s are "edit this function," whose `'state`
is the script's current package items, and whose `views` render its CLI surface and
its source. Because it is just an App over an op stream:

- **Installing** it = subscribing the local instance to its op stream (and granting
  its declared capabilities).
- **Running** it = the daemon folds `apply` over the stream and serves its `views`.
- **Editing** it = appending ops; they sync like any other op.
- **Forking** it = branching its op stream and taking ownership of the fork's future
  ops, while keeping the shared history (see "Forking" below).

There is no separate "app package format." An app is a projection of the same op
stream everything else is.

## The `dark apps` command surface

Deliberately small — most of it is a projection of installed `App` values:

```
dark apps                 # list installed apps (a projection: name, owner, version, running?)
dark apps install <ref>   # subscribe to an app's op stream + grant its caps
dark apps <name>          # inspect: source, views, declared caps, sync status
dark apps fork <name>     # branch the op stream; you own the fork's future ops
dark apps run <name>      # ask the daemon to host it (or it auto-starts on use)
dark apps remove <name>   # unsubscribe (local projection only; history is not destroyed)
```

`dark apps` (no args) is a **projection**, not a table to maintain: fold the op stream
into "which App values exist and are installed here." Installing/removing change a
local projection; they do not mutate the canonical stream.

## The print-md walkthrough (the north star, end to end)

1. **Stored in Dark.** `print-md` is authored as package items under Stachu's
   namespace and declared as an App (`name = "print-md"`, its fns, the caps it needs —
   filesystem read, process-spawn for the printer). Authoring is ordinary ops.
2. **Inspect + change.** `dark apps print-md` shows its source and views; editing a
   fn appends ops to the stream. No separate "app build step."
3. **Synced across machines.** Stachu's laptop is [subscribed to his desktop's op
   stream](../stable-and-syncing/sync.md). His edit on the desktop folds into the laptop's projection on the
   next sync — `print-md` updates on both. The op stream is the only thing on the wire.
4. **Ocean forks it.** `dark apps fork print-md` branches the op stream into Ocean's
   namespace. She shares the history up to the fork point; her future ops are hers.
   She can rewrite its `views`, change its behavior, and **migrate her data** — fork is
   a first-class right of any *user*, not just the author (per the App model).
5. **Listed under `dark apps`.** On any instance where it's installed, `dark apps`
   folds the stream and shows `print-md` (and Ocean's fork, on hers) as an installed
   app, with running status from the daemon.

When that walkthrough works end to end, the effort has hit its mark.

## How it rests on the substrate

| Concern | Where it's handled |
|---|---|
| App is an op stream + projections | [distributed-event-sourcing.md](distributed-event-sourcing.md) |
| Edits/installs/forks cross machines | [sync.md](../stable-and-syncing/sync.md) — ops on the wire; fork = a branch |
| Running app is hosted, kept warm | [cli-daemon.md](cli-daemon.md) — the resident host |
| App declares + is gated by effects | [capabilities.md](capabilities.md) — `print-md` needs fs + process caps |
| App's views render (CLI now, HTML later) | [view-sketches.md](../pdd/view-sketches.md), [structural-editor.md](../later/structural-editor.md) |
| Fork-point + concurrent-edit conflicts | [conflicts.md](../stable-and-syncing/conflicts-and-resolutions.md) — branch-vs-branch, name→two-hashes |
| Self-management of package values | the CLI provides listing/history/diff/share *around* an App's values |

## Forking, concretely

A fork is a branch of the op stream plus a transfer of authorship for future ops:

- **Shared history.** The fork keeps everything up to the fork point; only divergent
  future ops differ. Two instances pointing the same name at different post-fork
  hashes is exactly the `Name → two hashes` conflict in [conflicts.md](../stable-and-syncing/conflicts-and-resolutions.md),
  surfaced as data, not silently merged.
- **Data migration.** A user of the app (not just the author) can fork and **migrate
  their data** into the fork. The mechanism is the App's own `apply` replayed over the
  forked stream; where the fork changes `'op` or `'state` shape, a migration is itself
  a sequence of ops the fork author writes.
- **Views and behavior are forkable too.** Because `views` and the `apply`/`resolve`
  logic are package items, forking the app forks them like any other code.

## Open questions

- **App declaration.** Is "this set of package items is an App named X with these caps"
  itself an op (an `AppManifest` op), or a derived projection from a conventional
  module shape? Lean: an explicit op, so the manifest syncs and forks like everything.
- **Install = trust.** Installing grants the app's declared caps. How much does
  `dark apps install` surface about what it's about to grant? Probably a cap summary +
  confirm, with no interactive grants mid-run (per [capabilities.md](capabilities.md)).
- **Versioning + update.** When an upstream app advances, an installed fork sees new
  ops as a sync; updating is folding them in, and a conflict if the fork diverged.
  Whether `dark apps` shows "N upstream ops behind" is a projection choice.
- **Discovery.** How does Ocean find `print-md` to install/fork in the first place —
  a public namespace on `matter.darklang.com`, a shared ref, a registry projection?

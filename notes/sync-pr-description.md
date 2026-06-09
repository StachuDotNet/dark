# Stable & Syncing — a graph of Dark instances that reconcile

## The goal

Use Dark across all my devices. Each device runs a Dark instance; together they form a **graph** on my
tailnet that **reconciles its package changes**. Author a function on the laptop, it shows up on the
desktop. The instances agree on the same code without a central server in the loop.

Two properties make that safe to live in day to day:

- **Convergence without blocking.** When two machines change the same name, sync doesn't stall — it
  auto-resolves and records the race so I can follow up.
- **Resolutions travel too.** A decision I make on one machine (override this race, keep my version) is
  *itself an op* that rides sync to the others, so they converge on my choice — as if I'd resolved it
  there. Nothing is a one-machine-only fact.

The rest of this reads **bottom-up**: the two foundation pieces this rests on, then sync itself.

---

## Foundation 1 — ops ⊥ projections

The op log is the source of truth; everything you *see* is a projection of it.

Every package change (add a fn, rename, deprecate, propagate) is an **op** in a branch-scoped,
append-only log. The five tables you query — package functions/types/values, locations, dependencies —
are **projections** *folded* from that log (`package_blobs` is canonical content, not a projection). A
`projectionRegistry` makes the rebuild set authoritative: `rebuildProjections` clears the projections,
marks the ops unapplied, and re-folds the whole log → byte-identical tables. Surfaced as **`dark status`**
(ops in the log vs folded-through — is the cache current?) and **`dark branch rebuild`**.

This is what makes sync safe: replicating the op log and re-folding it is the *same operation* as a local
edit. Losing a projection costs only the CPU to rebuild; the ops are the thing that matters.

## Foundation 2 — conflict-dispatch (the resolution spine)

A single runtime hook — `ExecutionState.conflictDispatch : Conflict -> CallContext -> Ply<Resolution>` —
the one place the runtime says *"I can't proceed; here are the options."* A `Conflict` (a SetName race, a
missing fn, …) goes in; a `Resolution` (fail loud, substitute a value, later: park) comes out. The default
policy fails loud for everything, so installing the seam changed no behavior.

It is **shared infrastructure, not a sync appendage** — it's wired into two places already:

1. the interpreter's **missing-package-fn** path (today: fail loud, identical to before; teed up:
   *fetch-on-miss* — a policy pulls the fn from a peer instead of failing), and
2. sync's **divergence routing** (below).

New conflict kinds are new `Conflict` cases a policy resolves the same way — so the system grows by adding
cases, not by re-plumbing.

---

## Sync

### How it reconciles

A receiver pulls the ops it hasn't seen (tracked by a per-peer cursor) and folds them through the **same
playback path a local edit uses**. Apply is idempotent (`INSERT OR IGNORE` by content-hash id), so
re-pulling is a no-op and a full replay reproduces the identical projection. Ops fold onto the branch they
belong to; a branch projects only its own ops.

### Conflicts & resolutions

Two machines can bind the same name to different content — a **SetName race**. It auto-resolves by
**choosing whichever op was written later** (each op carries a portable `origin_ts` authoring time, kept
*beside* the op so its content hash is unchanged; every instance computes the same winner regardless of
arrival order). The race is **recorded, never lost** — you follow up when you want:

- **ack** the auto-resolution to say "that was right," or
- **override** (`resolve <id> mine|theirs`) to pick the other side. An override re-stamps the op to *now*,
  which both wins locally and **rides sync** — so peers adopt your choice too.

Other conflict kinds (moves, value updates — below) will be modeled and resolved the same way, all through
the conflict-dispatch spine. `dark conflicts` shows what was auto-kept, marks the winner, and inlines the
exact action:

```
1 auto-resolved conflict(s) awaiting ack — last-write-wins kept one side, nothing lost:

⚠ Stachu.MyApp.greeting
    last-write-wins → kept theirs   you a1b2c3d4  →  them e5f6a7b8 ✓  ·  from desktop  ·  ack 3f8a92c1

  ack <id> (agree)  ·  ack all  ·  resolve <id> mine|theirs (override)
```

A pull that surfaced races nudges you toward `dark conflicts`; `dark sync status` shows the pending count.
The formatting is a pure Dark package (`Sync.Display.conflictReport`) over structured rows from the
builtin — so the UX is package-testable and iterable without an F# rebuild.

### Sync transport options

The reconciliation above doesn't care *how* ops move. Two carriers are wired:

- **File** — `dark sync pull <peer's data.db>`. Direct, offline, no server.
- **HTTP over Tailscale** — a peer serves `/sync/{events,snapshot,blobs,health}`; the client pulls the
  delta since its cursor. **Trust model: machines on the tailnet are trusted** — identity is the
  `Tailscale-User-Login` header, the tailnet is the boundary, and `httpClientGetUnsafe` relaxes SSRF only
  for that. MagicDNS + TLS come for free.

A public client/server (bearer auth, a canonical hub) is designed-for but out of scope here.

### Every op kind rides sync

Nothing you can author silently fails to replicate: `applyOp` has **no wildcard**, so the compiler forces
every `PackageOp` kind to be folded on the receiver; `opsSince` ships every kind; the wire frames the raw
blob byte-exact. A propagation rides as its standalone companion `SetName` ops, so dependents repoint too.

---

## What's coming (ops & conflict types)

The foundation is built to grow (see `sync-future-ops.md`). Next:

- **MoveItem / MoveModule** — reorganize the namespace (a module move = many item moves). Folds through
  the same `locations` machinery as SetName. New conflict: **`CMoveCollision`** (two machines move
  different things to the same place).
- **Long-lived mutable package values** — a value keeps a stable identity while its content is *updated*
  over time (config, counters, the stuff you actually keep across devices), vs today's immutable
  content-addressed values. New conflict: **`CValueUpdateRace`** (concurrent updates — last-write-wins, or
  a merge policy later).
- **Constraints as conflicts** — a rename that orphans dependents, a new fn shadowing a signature, an ACL
  change on merge (`CCapabilityDenied`) — routed through the same spine.

## The apps (sketches toward a multi-view CLI)

Today the CLI is one view: a chat window. These are render-only sketches of three high-level apps you'd
visit often — interactive wiring is a documented follow-up (`cli-ux-redesign-vision.md`). They already
render inline **sync/lifecycle badges**, so new conflict kinds get a UI home for free.

**Explorer** — the package namespace as a dir tree you cd/ls through:

```
  /Stachu/MyApp
  ----------------------------------------
  > Handlers/
    greeting  (fn)   ! conflict
    Config  (type)
    draft  (value)   * wip
    oldHelper  (fn)   ~ deprecated
```

**TreeView** — the whole hierarchy at once, with rename-impact (`dep:N`) and race badges:

```
  packages
  ----------------------------------------
* v Stachu
    v MyApp
        greeting   [conflict]
        render   [dep:7]
      > Lib
    > Scripts   [wip]
```

**Repl** — a full-screen scratchpad that teaches types as you go:

```
  repl (scratch)
  ----------------------------------------
  > 1L + 2L
      3L  : Int64

  > Stdlib.List.length [1L; 2L]
      2L  : Int64

  > Stdlib.String.toUppercase "hi"_
```

## Testing — what each layer proves

The goal was to make the engine's *properties* hold, not to rack up cases. By layer:

- **Foundation.** A `rebuildProjections` reproduces byte-identical tables from the log; the `dark status`
  counters track folded-through vs total. *(OpsProjections)*
- **Conflict-dispatch.** The default policy is byte-identical; a policy's `RSubstitute` / `RFailLoudly`
  verdicts are honored; the seam is live. *(ConflictDispatch)*
- **Sync engine — the safety properties.** Re-applying an op is a no-op (idempotence); a cross-store
  transfer converges; **out-of-order arrival converges to the same winner** (order-independence); a cursor
  resumes where it left off; the wire round-trips byte-exact *and* rejects a truncated body or a
  version-skewed peer (fail-closed); a content blob is fetched on miss; timestamp-LWW orders by **creation,
  not arrival**. *(SyncIdempotency)*
- **Divergence routing — the policy layer.** Default routing is a no-op (LWW stands); keep-local
  re-stamps + re-binds and marks the race overridden; keep-incoming and an unknown substitute are safe
  no-ops; a *type* binding resolves like a fn; a multi-race pull converges each location. *(SyncScenarios)*
- **The race UX, end to end.** A live divergent pull auto-resolves AND records the conflict; `resolve
  mine` re-stamps + re-binds, `resolve theirs` keeps the incoming — both mark it overridden. *(BranchOps +
  SyncIdempotency)*
- **The CLI surface** (fast `.dark` testfiles, no rebuild): the conflict report frames LWW + marks the
  winner + inlines `ack`; `sync check` parses a peer's health and reports caught-up vs behind; the app
  sketches render.
- **End to end.** Two release CLIs converge via a file pull — author on A, B pulls exactly the new ops,
  re-pull is idempotent.

The whole backend suite is green (~9.7k cases).

## Notes from rebasing onto main

A few edits the rebase onto current `main` required, recorded for the reviewer: `applySetName` moved to
main's single-transaction `exec ctx` model (LWW reads stay atomic on one connection); the `composite_pk`
migration now carries `origin_ts` (it was being dropped on rebuild); sync builtins adopt main's
`capabilities` field; a duplicate `Seed.projectionStatus` (sync and ops-projections each added an
identical one) was de-duped; the daemon was dedup'd onto `Stdlib.Cli.Daemon`.

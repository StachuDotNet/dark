# Stable & Syncing — op-log sync between Darklang instances

Author on one machine, see it on another. Two Dark instances reconcile their package **op-logs** and
converge on the same code — the foundation for using Dark day-to-day across devices.

This PR lands the whole **floor** in three coherent layers, bottom-up:

1. **Ops ⊥ projections** — make the op log canonical and every package table a regenerable projection.
2. **Conflict-dispatch seam** — a single dormant runtime hook for "I can't proceed; here are the options."
3. **Sync** — replicate the op log between instances, converge by authoring time, surface divergences for ack.

## How sync works (the process)

Transport-agnostic — true whether ops move over a file or the network.

- **Ops are the truth.** Every package change (add a fn, rename, deprecate, propagate) is an **op** in a
  branch-scoped, append-only log. Everything you *see* — functions, types, locations, dependency graphs — is a
  **projection** folded from that log. Projections are regenerable and disposable; the op log is canonical.
- **Sync = replicate the log.** A receiver pulls the ops it hasn't seen (tracked by a per-peer cursor) and
  folds them through the **same playback path a local edit uses**. Apply is idempotent (`INSERT OR IGNORE`
  by content-hash id), so re-pulling is a no-op and a full replay reproduces the identical projection.
- **Conflicts resolve by creation time.** Each op carries a portable **`origin_ts`** (when it was authored)
  *alongside* it — never inside the op, so its content hash is unchanged. A name→hash binding is ordered by
  **creation, not arrival**: an op authored *earlier* but arriving *later* via sync loses to the newer
  binding. Every instance computes this identically, so they converge regardless of arrival order. The
  losing op stays in the log — it's just not the active name.
- **Divergences surface as data, never block.** When an incoming rename points a name at a different hash
  than this instance has, it's auto-resolved (last-writer-wins) and **recorded** for review — the op still
  applies, nothing stalls on a human. See *Conflict resolution* below.
- **Per-branch, no bleed.** A synced op folds onto the branch it belongs to; a branch projects only its own
  ops. Pulling a peer's branch you're not on receives the ops without auto-folding them into yours.

## Layer 1 — ops ⊥ projections (the regenerable foundation)

The op log is the source of truth; five tables (package functions/types/values, locations, dependencies)
are **projections** of it (`package_blobs` is canonical content, not a projection). A `projectionRegistry`
makes the rebuild set the single source of truth: `rebuildProjections` clears the projections, marks ops
unapplied, and re-folds the whole log → byte-identical tables. Surfaced as **`dark status`** (op count vs
folded-through — is the cache current?) and **`dark branch rebuild`** (recover a projection from the log).
Losing a projection costs only CPU; the ops are safe.

## Layer 2 — conflict-dispatch seam (dormant on purpose)

A single runtime hook — `ExecutionState.conflictDispatch : Conflict -> CallContext -> Ply<Resolution>` —
the place the runtime asks "I can't proceed; here are the options" (a `CSyncDivergence`, a `CFnNotFound`, …).
The default is `FailLoudly` for every conflict, so the build is **byte-identical to before**: the seam is
installed, unused, ready. The types live in `RuntimeTypes.fs` (they reference Dval/RuntimeError *and*
ExecutionState references them — a circular constraint that forces the and-chain).

## Layer 3 — sync, and routing divergences through the seam

Sync detects each `name → two hashes` divergence, auto-resolves it by timestamp-LWW, and **routes it through
the conflict-dispatch seam** as a first-class `CSyncDivergence` (not just its own recording):

- the **default policy** keeps today's behavior exactly — surface-as-data, LWW stands, a pull never aborts
  mid-batch;
- a **sync policy** can return `RSubstitute(local hash)` → keep-local (re-stamp + re-fold, the same move as
  a human `resolve … mine` override).

So sync is both self-contained *and* wired into the generic resolution machinery the other constraint kinds
(merge, propagation, deprecation) can later emit through.

## Conflict resolution (surfaced for ack — nothing silently lost)

Auto-resolution never blocks, so every divergence lands somewhere you **eventually ack** (agree) or
**override**. `dark conflicts` frames the last-write-wins outcome, marks the winning side, and inlines the
exact action:

```
1 auto-resolved conflict(s) awaiting ack — last-write-wins kept one side, nothing lost:

⚠ Stachu.MyApp.greeting
    last-write-wins → kept theirs   you a1b2c3d4  →  them e5f6a7b8 ✓  ·  from desktop  ·  ack 3f8a92c1

  ack <id> (agree)  ·  ack all  ·  resolve <id> mine|theirs (override)
```

A `sync pull` that surfaced divergences nudges you (`… see dark conflicts`); `dark sync status` shows the
pending count. The formatting is a **pure Dark package** (`Sync.Display.conflictReport`) over structured
rows from the builtin — package-testable and iterable without an F# rebuild.

## Transport (the swappable part — deliberately last)

The process above doesn't care *how* ops move. Two transports are wired:

- **File** — `dark sync pull <peer's data.db>`. Direct, offline, no server. (Verified: two release CLIs,
  author on A → B pulls exactly the new ops → idempotent re-pull.)
- **HTTP over Tailscale** — a peer serves `/sync/{events,snapshot,blobs,health}`; the client pulls the delta
  since its cursor. Identity comes from Tailscale's `Tailscale-User-Login` header (the tailnet is the trust
  boundary; `httpClientGetUnsafe` relaxes SSRF only there). MagicDNS + TLS for free.

Public client/server (bearer auth, a canonical hub) is designed-for but not built here.

## Coverage: every op kind rides sync

Nothing a user can author silently fails to replicate: `applyOp` has **no wildcard** — the compiler forces
every `PackageOp` kind (add fn/type/value, name/rename, deprecate, undeprecate, propagate, revert) to be
folded on the receiver; `opsSince` ships every kind; the wire codec frames the raw blob byte-exact. A
propagation rides as its standalone companion `SetName` ops, so dependents repoint on the receiver too.

## Built to grow (more ops, more conflict types)

The foundation is meant to expand without re-architecting — see `notes/sync-future-ops.md`:

- **A new op rides sync for free** — `opsSince` has no kind filter, the wire frames the raw blob
  byte-exact, and `applyOp` has **no wildcard** (a new `PackageOp` case won't compile until the fold
  handles it — so "silently doesn't sync" is impossible).
- **Conflicts are one open enum** — `Conflict` is the meta-model; new kinds are new cases a policy
  resolves the same way. Already named for what's coming: **`CMoveCollision`** (MoveItem/MoveModule
  land on an occupied name), **`CValueUpdateRace`** (concurrent updates to a long-lived *mutable*
  package value — LWW by `origin_ts`, or a merge policy later), **`CCapabilityDenied`**.
- A **SetName race** (this PR) is the first instance of the general pattern: *op in the log → folded →
  race surfaces as a `Conflict` → resolved by policy → reviewable + overridable*.

## Also in this PR (CLI app sketches, for later)

Bare-bones render-only sketches of three high-level CLI apps — **Explorer** (dir-based package browser),
**TreeView** (package-tree nav/adjust), **Repl** (full-screen scratchpad) — toward a multi-view CLI beyond
today's chat-window. They already render **inline sync/lifecycle badges** (conflict / WIP / deprecated /
dependent-count), so new conflict kinds get a UI home for free. Pure render + Model + TODOs; interactive
wiring is a documented follow-up. Vision in `notes/cli-ux-redesign-vision.md`.

## Testing

- `SyncIdempotency` (30) — wire round-trip, cross-store transfer, convergence, order-independence, cursor
  resume, blob fetch, timestamp-LWW ordering.
- `SyncScenarios` (8) — the dispatch-routing policy layer: default no-op, keep-local, keep-incoming,
  unknown-substitute, type-kind, empty, multi-divergence LWW, re-stamp propagation.
- `OpsProjections` (5), `ConflictDispatch` (4), `Remotes` (4), `BranchOps` (7, incl. the same-FQN
  local-authoring case the LWW fix protects).
- `.dark` testfiles (fast loop, no rebuild): `conflicts-list` (19), `sync-check` (11), `apps-sketches` (18),
  plus the existing sync-cli / conflicts-display / status-cli / autosync.
- **Full backend suite — 9,750 passed, 0 failed.**
- Release CLI — two instances sync via file pull end-to-end.

## Notable engineering

Surfaced by rebasing the floor onto current `main`: `applySetName` ported to main's single-transaction
`exec ctx` model (LWW reads atomic on one connection); the `composite_pk` migration now carries `origin_ts`
(it was silently dropping it on rebuild); sync builtins adopt the `capabilities` field; the LWW guard skips
only on a *strict* older-by-creation (a hash tie-break would have broken local same-ms authoring); a
duplicate `Seed.projectionStatus` (sync + ops-projections each added an identical one) de-duped; the
conflict list moved from an F#-baked string to structured rows + a pure Dark formatter;
`sync/daemon.dark` dedup'd ~185→~70 lines onto `Stdlib.Cli.Daemon`.

### Known edge (non-blocking)
Two *keep-local* resolutions in one routing pass: the second re-fold can fail to flip. No keep-local policy
ships (default is `FailLoudly`), and the shipped multi-divergence path (LWW) converges and is tested.

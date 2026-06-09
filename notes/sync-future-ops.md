# Sync — preparing for more ops & conflict types

The op log + the `Conflict` meta-model (`RuntimeTypes.fs`) are built to grow. This note records the
shape of the ops we know are coming so the foundation in this PR stays the right one.

## Why the foundation already accommodates new ops

- **Any op rides sync for free.** `opsSince` has no kind filter and the wire codec frames the raw
  op blob byte-exact, so a new `PackageOp` case replicates with no transport change.
- **The receiver is forced to handle it.** `PackageOpPlayback.applyOp` has **no wildcard** — adding a
  `PackageOp` case won't compile until the fold handles it. So "a new op silently doesn't sync" is a
  compile error, not a runtime surprise.
- **Conflicts are one open enum.** New conflict kinds are new `Conflict` cases; a dispatch policy
  resolves each the same way (`RSubstitute` / `RFailLoudly` / later `RPark`). The recording/review
  surface (`dark conflicts`) generalizes from `sync_conflicts` as needed.

## The ops on the horizon

### MoveItem / MoveModule (reorganize the namespace)
`PackageOp` already stubs these (commented in `ProgramTypes.fs`). A move is a name rebind, so it folds
through the same `locations` machinery as `SetName`. **Module move = many item moves** (preserve order
in one op or emit a batch). New conflict: **`CMoveCollision`** — two instances move different content to
the same destination, or one moves what another renamed. Resolve by `origin_ts` LWW (same as a SetName
race) or surface for manual placement. Needs: the op + fold + a destination-occupied check in the
divergence pass.

### Long-lived mutable package values (value updates over time)
Today a package value is immutable content (hash-addressed). A **mutable value** keeps a stable identity
while its content is **updated** by a new op (e.g. `SetValue`/`UpdateValue`) — config, counters,
accumulators, the things you actually keep across devices. Sync replicates the update ops; the value
converges by `origin_ts` LWW per identity. New conflict: **`CValueUpdateRace`** — two instances updated
the same value concurrently. Default LWW (newest write wins, loser recorded for review), with room for a
**merge policy** later (e.g. CRDT-style for counters/sets). Needs: the identity (not just the content
hash) in the op, a per-identity LWW fold, and the new conflict case.

### Others (further out)
- **Deprecate/propagation as Constraints** — the existing `ProgramTypes` comments already point here;
  route these through the dispatch seam as `Conflict`s too (a rename that orphans dependents, a new fn
  shadowing a signature) so the whole "system noticed X, you commit your intent" loop is uniform.
- **ACL / grant changes on merge** — needs the capability axis; `CCapabilityDenied` is the hook.

## The throughline
Every one of these is: **an op in the log → folded on the receiver → races surface as a `Conflict` →
resolved by a policy → reviewable + overridable**. This PR establishes that spine; the rest is adding
cases, not re-architecting. The CLI sketches (`Explorer`/`TreeView`) already render inline conflict/WIP
badges, so new conflict kinds get a home in the UI for free.

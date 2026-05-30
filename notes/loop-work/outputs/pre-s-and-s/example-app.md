# Worked example: a non-trivial App

The keystone ([distributed-event-sourcing.md](distributed-event-sourcing.md)) defines
the `App` type and shows a `counter`. But the counter is *too* easy — its `conflict`
is `false`, its `resolve` returns one side, its `invariants` are empty. Nothing in it
exercises the members that make distribution hard. This doc works a small App that
does: a **shared key-value store**, where concurrent edits genuinely clash, an
invariant genuinely constrains, and the convergence/replay stories from the keystone
play out on something concrete.

It is illustration, not a spec — the point is to make the abstract members tangible.

## The App

```fsharp
type KvOp =
  | Set    of key: String * value: String
  | Delete of key: String
  | Rename of from: String * to: String

type Kv = Map<String, String>

let kv : App<Kv, KvOp> =
  { name = "kv"
    empty = Map.empty

    // Play ONE op back — the only way state moves.
    apply = fun op state ->
      match op with
      | Set (k, v)       -> Map.set state k v
      | Delete k         -> Map.remove state k
      | Rename (a, b)     ->
        match Map.get state a with
        | Some v -> state |> Map.remove a |> Map.set b v
        | None   -> state                       // renaming a missing key is a no-op

    // Do two concurrent ops clash? Only when they touch the same key
    // in non-commuting ways. Disjoint keys never clash.
    conflict = fun x y ->
      match x, y with
      | Set (k1, v1), Set (k2, v2)  -> k1 = k2 && v1 <> v2   // same key, different value
      | Set (k1, _),  Delete k2     -> k1 = k2               // set-vs-delete on one key
      | Delete k1,    Set (k2, _)   -> k1 = k2
      | Rename (a, _), Rename (c, _) -> a = c                // two renames of one key
      | _                            -> false                // disjoint keys commute

    // Reconcile a clash. Auto where there is a defensible rule; otherwise
    // emit a marker op that surfaces both sides as data (see below).
    resolve = fun (x, y) ->
      match x, y with
      | Set (k, _), Delete _ -> [ x ]              // a write resurrects a concurrent delete
      | Delete _,   Set (k, v) -> [ Set (k, v) ]   // symmetric
      | Set (k, v1), Set (_, v2) ->
        // genuinely concurrent writes to one key: don't silently pick.
        // surface both, keyed under a conflict namespace, for a human/agent.
        [ Set ($"{k}.conflict.ours",   v1)
          Set ($"{k}.conflict.theirs", v2) ]
      | _ -> [ x ]

    views = fun state ->
      [ Table (state |> Map.toList |> List.map (fun (k, v) -> Row [Text k; Text v]))
        // a standard projection: just the conflict rows
        Text $"conflicts: {state |> Map.keys |> List.filter (String.endsWith \".conflict.ours\") |> List.length}" ]

    invariants = fun state ->
      // a hard at-rest constraint: keys are never empty strings.
      state
      |> Map.keys
      |> List.filter (fun k -> k = "")
      |> List.map (fun _ -> Violation { kind = "empty-key"; hard = true }) }
```

## What each member is now doing

- **`apply`** folds one op. `Rename` shows a non-commutative-looking op that is still
  a pure fold over current state.
- **`conflict`** is the interesting one: it is *false for disjoint keys* (the common
  case — most concurrent edits don't touch the same key, so they auto-merge), and
  true only for genuine same-key clashes. This is the "most conflicts are OK" stance
  made literal — the predicate is mostly `false`.
- **`resolve`** auto-resolves the defensible cases (write-beats-concurrent-delete) and
  for the one case with no defensible auto-rule (two different values written to one
  key concurrently) it **does not pick** — it emits ops that surface both sides as
  ordinary data under a `.conflict.*` key. That data shows up in the conflict view
  like any other state; a human or agent resolves it later. No blocking, no data loss.
- **`views`** projects the table *and* a conflict count — the "list of conflicts as a
  standard view" idea, here as a one-liner.
- **`invariants`** enforces a real at-rest constraint (no empty keys), declared
  `hard`, so a violation routes to `FailLoudly` rather than surfacing — per the
  invariant-handling rule in the keystone.

## The cross-cutting stories, on this example

**Convergence (disjoint edits).** Instance A does `Set("title","Hi")`; instance B
concurrently does `Set("author","Stachu")`. `conflict` returns `false` (different
keys), both ops fold on each side, and — because both instances run the *same*
content-addressed `kv.apply` ([the convergence precondition](distributed-event-sourcing.md)) —
they land on the identical two-key map. No coordination needed.

**A real clash.** Both set `"title"` concurrently, to different values. `conflict`
fires; `resolve` emits the two `.conflict.*` ops; both instances fold them and both
show the same two conflict rows. The disagreement became *shared data*, identical on
both sides, rather than a silent divergence — and `dark`'s conflict view surfaces it.

**Replay.** Re-folding the recorded op stream through `kv.apply` reproduces the exact
map, including the conflict rows — because [an op records the result, not the intent
to call](distributed-event-sourcing.md), and `kv` has no nondeterministic producer.
Time-travel (scrub to op N) is just folding the first N ops.

**A forked resolver.** If instance B edits `kv.resolve` to last-writer-wins instead of
surface-both, B is running a *different* `kv` by hash. That fork shows up as a
`Name → two hashes` conflict on `kv.resolve` itself — visible, deliberate divergence,
exactly as the convergence section predicts. The two instances are now different Apps
and are not expected to converge until one re-points to a shared hash.

## Why this is the shape every App takes

Nothing here is kv-specific machinery. Swap `KvOp`/`Kv` for `PackageOp`/the package
tree and you have the SCM; swap for `EditOp`/an AST and you have a structural
editor; swap for the CLI's own ops and you have the
[apps surface](apps-surface.md). The members are always the same seven, and the
substrate (sync, conflict dispatch, replay, the event bus) treats every App
identically. The counter shows the *minimum*; this shows the members earning their
keep.

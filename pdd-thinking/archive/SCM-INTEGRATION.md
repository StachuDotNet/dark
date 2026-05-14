# PDD ↔ SCM Integration — The "ID until commit, then hash" Model

*Captured from convo 2026-05-13 night.*

## The idea

Today in Dark:
- Functions are content-addressed by **hash**. The hash IS the identity.
- A "Location" maps a human name (`Stdlib.List.map`) → hash.
- To change a fn's body: compute new hash, insert new row into `package_fns`, re-point the Location.

The proposal:
- **While work-in-progress**: reference by **ID** (PackageID). Body is mutable — refine in place, callers see latest version automatically.
- **At commit time**: migrate ID → hash. Snapshot current body, mint hash, insert into the real `package_fns` table, create/update a Location.

This is **exactly the working-copy ↔ commit boundary** of git, but at the function granularity.

## Why it's the right shape

- **PDD-aligned**: materialize creates a working PackageID; refine mutates it; users iterate without "every save = new hash" churn. Today every refinement appends a new row to promoted.jsonl with no SCM meaning.
- **SCM-aligned**: hashes only get minted when the human decides the fn is ready. The hash is the unit of commit. Branches, diffs, blame all work on hashes — same as today.
- **No two parallel storage systems**: PackageID lives in the in-memory `pddIDFnCache` + the promoted.jsonl sidecar; once promoted, it's just a normal package fn. No "PDD-store" / "real-store" duality long-term.

## How callers stay stable across the boundary

The Location layer (name → resolution) extends:

```
   Location: "renderHome"
            ┌──────────────────┐
            │ Working: ID=g123 │   ← while PackageID
            └──────────────────┘
                    │
                    │ dark pdd promote renderHome
                    ▼
            ┌──────────────────┐
            │ Committed: hash=abc456 │   ← after promote
            └──────────────────┘
```

Code that called `renderHome` doesn't change. The resolver substitutes whichever side currently exists.

(Future: SAME name could resolve to BOTH — committed hash as the "stable" version, plus ID as the "in-flight refinement that supersedes the committed version while present".)

## Lifecycle (full)

```
   parser sees an unresolved fn name in source
            │
            ▼
   PT.FQFnName.Pending          ← "not materialized yet"
            │
            │ first call → LLM materializes a body
            ▼
   RT.FQFnName.PackageID (g123)  ← "materialized, evolving"
            │           ▲
            │           │   dark pdd refine renderHome
            │           └─────┐  (body mutates, ID stable)
            │
            │ dark pdd promote renderHome
            ▼
   RT.FQFnName.Package (hash:abc456)  ← "committed, immutable"
            │
            │ future edits start a new PackageID off the latest hash
            ▼
   PackageID (g124) again, now branching from the committed snapshot
```

The leftmost two arrows are what we have today. The middle promote step is the missing piece.

## Implementation deltas from current state

| Today | After |
|---|---|
| `PackageID` resolves via `pddIDFnCache` (in-memory) + `promoted.jsonl` (sidecar) | Same |
| No `dark pdd promote` command | Add one: `dark pdd promote <name>` |
| Promoted fns invisible to `dark search` / `dark tree` | Promoted = real package fn; visible everywhere |
| Hash format: `pdd-llm-<name>-<int>` (made up) | Real content hash (SHA over canonical PT body) |
| package_fns table not used for PDD | Promote writes there; Locations created |

## What `dark pdd promote <name>` does

1. Read the current `pddIDFnCache[idForName(name)]` (or load from `promoted.jsonl`).
2. Lower body through the canonical hashing path → real content hash.
3. Insert row into `package_fns` (the SQLite table).
4. Create/update a `Locations` row: `name → Package hash`.
5. Optionally retire the `pddIDFnCache` entry (or keep it for inspection).
6. Subsequent uses of `name` resolve via the new Location → real PackageFn (immutable). The PackageID lifeline ends.

## Branching

Per the diagram: after promote, the next refine of the same name STARTS a new PackageID. The committed hash is the basis (so the LLM can be prompted "improve this committed version" and the result becomes a working PackageID that, when promoted, gets a new hash).

This gives multi-version PDD work: the canonical hash is the latest commit; PackageID is the working delta.

## Plan reordering

What I'm doing tonight, updated:

- ~~iter 5: live-verify refine --watch loop~~ (in progress, debugging env propagation)
- iter 6: **`dark pdd promote <name>`** — the SCM commit step
- iter 7: real package_fns integration (visible to `dark search`)
- iter 8: scale to all 30 darklang.com pages (existing plan)
- iter 9: final reports including this SCM-integration doc

The user's framing — "ID while WIP, hash at commit" — is more architecturally correct than my earlier "make them equal." It's a *transition*, not a parallel structure. Updates the conceptual model: PackageID is the working state, Package(hash) is the committed state, and `promote` is the boundary.

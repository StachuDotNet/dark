# Real package_fns Integration — Scope

*Iter 16 scoping doc. What it takes to make `dark pdd promote` write
to the actual `package_functions` SQLite table (so the promoted fn
shows up in `dark search`, `dark tree`, etc).*

## Current state

`dark pdd promote <name>`:
- Reads body from `pdd-cache/promoted.jsonl` (working stream).
- Computes SHA-256 of the body.
- Appends a snapshot to `pdd-cache/promoted_hashes.jsonl`.
- Done.

The hash is real but the artifact lives in a sidecar — invisible to
the canonical package store.

## What "real" promotion needs

From `LibDB/PackageOpPlayback.fs`:

```fsharp
let private applyAddFn (fn : PT.PackageFn.PackageFn) : Task<unit> = task {
  let hash =
    match fn.hash with
    | Hash "" -> Hashing.computeFnHash Hashing.Normal fn
    | h -> h
  let ptDef = BS.PT.PackageFn.serialize hash fn
  let rtInstrs = fn |> PT2RT.PackageFn.toRT |> BS.RT.PackageFn.serialize hash
  do!
    Sql.query "INSERT OR REPLACE INTO package_functions (hash, pt_def, rt_instrs) VALUES (...)"
    |> Sql.parameters [ ... ]
    |> Sql.executeStatementAsync
  let refs = DE.extractFromFn fn
  do! updateDependencies hash refs
}
```

So a real promote takes a `PT.PackageFn.PackageFn`, hashes via
`Hashing.computeFnHash`, serializes both PT and RT forms, INSERTs.
Then `applySetName` to create the location for `name → hash`.

## What PDD has vs needs

| Have | Need |
|---|---|
| `GeneratedFn { sig_: string; body: string; tests: List<…> }` | `PT.PackageFn.PackageFn` (typed params + return + PT.Expr body) |
| `gen.sig_ = "(x: String): String"` | typed params via `parseFullSig` (already there, returns `RT.TypeReference` — need PT-level) |
| `gen.body` (string) | `PT.Expr` via `LibParser.Parser.parsePTExpr` (bodyParser hook is already installed) |
| SHA-256 of body (sidecar hash) | Real `Hashing.computeFnHash Hashing.Normal fn` (already in LibSerialization) |

## Implementation sketch

1. **`parseFullSigPT : string -> Option<(List<string * PT.TypeReference>, PT.TypeReference)>`**
   — same as today's `parseFullSig` but returning `PT.TypeReference` instead of `RT.TypeReference`. Trivial transform (PT and RT type names are 1:1).
2. **`ptPackageFnOf : name -> sig_ -> body -> PT.PackageFn.PackageFn`**
   — wraps body in `fun <params> -> body`, parses via `bodyParser`,
     extracts ELambda body, builds `PT.PackageFn.PackageFn` with
     `hash = Hash ""` (filled in by computeFnHash) and an empty
     `description`, `typeParams = []`.
3. **`writeToPackageStore : PT.PackageFn.PackageFn -> Task<Hash>`**
   — moves the existing `applyAddFn` logic out of `PackageOpPlayback`
     (or exposes it). Returns the real content hash.
4. **`writeLocation : name -> Hash -> Task<unit>`**
   — calls `applySetName` (or similar) to create
     `Locations(name = "Stdlib.User.renderHome", hash = ..., branch = ...)`.
5. **Update `dark pdd promote`** to call this instead of writing to
   `promoted_hashes.jsonl`. Keep the sidecar as a fallback / log.

## What this unlocks

Once a PDD fn is promoted to the real package store:

- `dark search "renderHome"` finds it.
- `dark tree Stdlib.User.renderHome` shows it.
- The PT body is serialized canonically — diffable across versions
  via `dark diff`.
- Other Dark code can `import` and call it like any other package fn.
- Hash is content-addressed: same body anywhere in the world → same
  hash → trivially shared.

## What this does NOT unlock

- **PackageID stays working-copy.** The promote step doesn't change
  the working semantics of PackageID. Subsequent refines still mutate
  it. The hash is a snapshot, not a forwarding redirect.
- **Cross-branch propagation.** The canonical store works per-branch.
  Promoting on `pdd` branch doesn't auto-show on `main`. That's git.

## Risks / gotchas

- **Dev DB contamination.** The first promote inserts into the active
  package_fns. If the demo is run repeatedly with garbage bodies, the
  DB accumulates them. Workaround: put PDD-promoted fns under a
  distinct `Stdlib.PDD.<name>` namespace + a `dark pdd unpromote` to
  delete them.
- **Dependency extraction.** `DE.extractFromFn` walks the body for
  fn references. If a PDD body uses Pending refs (the original
  materialization), promotion would fail or leave dangling deps.
  Solution: refuse to promote a fn whose body still has Pending refs
  to other un-promoted fns. Or: recursive promote.
- **rt_instrs serialization for PDD-specific shapes.** Some PDD bodies
  use `++` chains that produce many register operations. Need to
  verify `BS.RT.PackageFn.serialize` handles them (it should — same
  RT.PackageFn shape).

## Estimated effort

- Step 1 (parseFullSigPT): trivial (~10 LoC).
- Step 2 (ptPackageFnOf): ~30 LoC, mostly mirrors today's `fnFromTypedBody`.
- Step 3 (writeToPackageStore): expose `applyAddFn` or reimplement
  inline. ~20 LoC + careful test that we don't corrupt the dev DB.
- Step 4 (writeLocation): ~15 LoC.
- Step 5 (CLI update): ~20 LoC.
- Tests: 1-2 hours including dev DB tear-down.

Total: maybe a half-day for a tight, working version. Worth doing
as a focused branch off `pdd`, with the dev DB isolated.

## Recommendation

Hold off until two things land:

1. **A decision** on the PackageID-on-promote question
   (REPORT-overnight.md): does the working copy stay editable
   forever, or does promote forward all callers to the hash?
2. **A namespace strategy** for PDD-promoted fns. `Stdlib.PDD.X` or
   `User.PDD.X` or something — to keep promoted PDD fns from
   colliding with hand-authored ones.

The sidecar (`promoted_hashes.jsonl`) is sufficient for the demo loop
today. Real integration is the right next step but warrants a separate
focused session, not an overnight iter.

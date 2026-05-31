# Prework status — the spike, mapped to implementation

One cross-cutting reference for the `loop-fun` prework: what each PR has as real, compiling,
tested code; the grounding corrections that changed the design; and what a real PR still does.
The per-PR specs carry the detail — this is the map. Everything below is **off `main`** in the
isolated clone; `main`/pdd source was never touched.

## Branch layout

Each PR is a `prework/*` branch off `main`; **`prework/compose-check`** merges **all 7** and is
the one branch holding the *whole* prework — F# floor + Dark surfaces — **full-suite-green at
9,494 / 0 failed**.

`event-bus-primitive · async-stage-a · ops-projections · conflict-dispatch · capabilities ·
sync-read-write · libpm-seam` → all merged into `compose-check`.

## F# floor — per PR

| PR (effort) | Built (F#) | Tests | Remains for the real PR |
|---|---|---|---|
| EventBus (1) | `EventBus.fs` (publish/subscribe/waitForOne) + `RuntimeBuses` on ExecutionState | 7 | durable buses (deferred to sync, per spec) |
| async Stage A (2) | `BuiltInFn.effects` (628-site codemod) · `VMState.spawnChild`+`cancel`+wired `throwIfCancelled` in the eval loop · `Scheduler.fs` (awaitSelector / ParkSet / DarkAsync + run/runReady) | 6 + sched | Stage C eval-loop yielding DarkAsync (the "large core interpreter change") |
| ops⊥projections (3) | `Seed.rebuildProjections` (drop+refold=identity) | (in Seed) | physical core.db/branch.db split |
| conflict-dispatch (4) | `Conflict`(4 cases)/`Resolution`(Fail/Substitute/Park)/`ConflictDispatch` on ExecutionState + default | 15 | route remaining raiseRTE sites |
| capabilities | `Capabilities.fs` (CapCategory/gate/hostAllowed/effectiveCaps) · `caps` field on all 628 builtins · `CapabilityGate.fs` + ExecutionState.grantedCaps · **gate wired at the builtin call site**, all 3 resolutions live (fail/substitute/park) | 15 + caps | per-fn effectiveCaps at its call site (adapter exists); structured multi-cat grant |
| sync read/write (7) | `Inserts.opsSince` (rowid cursor) · `Sync.fs` (opsToSend/snapshot/applyRemoteOps/detectDivergences) · `SyncCursors.fs` · cross-store transfer (op-log **and** projection+name) | 11 | the Dark HTTP handlers |
| identity-thin (8) | `Accounts.upsertAccount` (login→account_id) + authorship chain (commit→login) | 3 | per-op `Intent` (a PT change) |
| LibPM seam | `PackageStore`/`dispatchVia`/`sqliteStore` + **`connStore`** (all 7 handlers fold the whole op stream into ANY store) | 6 | lift `PackageStore`+`dispatchVia` into a `LibPM` project |

## Dark-side floor — pure surfaces (10 testfiles, 53 assertions)

`testfiles/execution/pre-s-and-s/`: tailscale (URL/args/ping/loginHeader) · print-md
(manifest + render-chain args) · apps-surface (alias shim/path/grant) · apps-list
(installed-elsewhere) · sync-cli (summary + op-kind breakdown) · remote-mgmt (RemoteEntry +
add/list) · conflicts-display (divergence) · resolution-display (Resolution enum + match) ·
capabilities-cli (actionable denial + grant cmd) · autosync (adaptive poll interval). These are
the *pure* UX/logic; the builtin/binary-dependent bodies (the App `main`, HTTP handler, autosync
loop, real `tailscale`/`pandoc` spawns) need a live environment.

## Grounding corrections — facts that changed the design

- **No Dark md→PDF to reuse.** The outliner's `markdown.dark` is outline→bullets, the wrong
  direction; print-md must shell out (pandoc→weasyprint→lp), 3 spawns. `posixSpawnAndWait` has **no
  stdin param** → temp-file staging or a builtin extension.
- **`package_ops` has no `account_id`** — authorship is *commit*-grained (`commits.account_id →
  accounts_v0`, a UUID FK). So sync attributes at commit granularity via `upsertAccount`, not a raw
  login string; per-op authorship is a later PT change.
- **No `seq` column needed** — `package_ops`'s TEXT PK gives a free monotonic **rowid** cursor.
- **`package_blobs` is canonical content, NOT a regenerable projection** (the 5 stripped tables in
  `Seed.export` are the real projection list).
- **The LibDB-as-backend refactor is a connString swap** — the fold's writes are pure
  serialization; only the final write is connection-coupled (`connStore` proves it).
- **spawn lives in `Builtins.Cli`, not `CliHost`.** **`effects` ⊥ `caps`** (scheduling vs gating),
  coexist on `BuiltInFn`.

## Dark-syntax findings (for the Dark code)

Named records need the type prefix on construction (`T { … }`); `==` is equality in fn bodies; a
*piped multi-line lambda* trips the parser (use `let x = …` + tuple destructuring `fun (k, _) ->`);
enums: `type X = | A | C of String`, construct `X.C v`, match `| C v ->` (no prefix); the `.dark`
test harness scans **subdirectories** of `testfiles/execution`.

## What a real implementation does next

1. **Land the leaf PRs** (each `prework/*` is already compiling+tested): EventBus, async Stage A,
   ops⊥projections, conflict-dispatch, capabilities.
2. **Land S&S:** sync read/write (wrap `Sync.fs` in Dark HTTP handlers), identity-thin, then the
   Dark `dark sync`/`remote`/`apps`/autosync surfaces over the proven F# + pure Dark.
3. **The capstone:** print-md as an App, riding the above.
4. **Deferred (sized, not blocking):** the `LibPM` project extraction; the core.db/branch.db split;
   async Stage C's eval-loop integration (the one genuinely-large remaining change).

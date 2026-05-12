# CLI NativeAOT — session progress report

**Date:** 2026-05-12
**Branch:** main (dirty; not yet committed)
**Scope:** Unblock + harden the NativeAOT CLI publish path. Static-link
SQLite, tighten trim/AOT settings, accelerate cold start, drop unused
heavyweight packages.

---

## Headline numbers (linux-x64)

| Metric                         | Before this session | After this session | Delta             |
|--------------------------------|---------------------|--------------------|-------------------|
| AOT publishable                | ❌ (sqlite blocker) | ✅                 | unblocked         |
| Binary size                    | n/a (failed)        | **27.1 MB**        | recent JITs 46–77 |
| Runtime deps (`ldd`)           | n/a                 | libc/libm/ld-linux | no libe_sqlite3   |
| Cold start (`eval "1L+2L"`)    | 5.55 s              | **1.61 s**         | **−3.4×**         |
| `seed.applyOps` phase          | 5.25 s              | **1.25 s**         | **−4.2×**         |
| Warm start                     | 0.20 s              | 0.22 s             | unchanged         |
| AOT build warnings             | ~30 (mostly NU1510) | 1 (IL3053 rollup)  | quiet             |

Three distinct rounds of work, each measured:

```
Pre-session                           failed to publish
After SQLite static link              32.0 MB,  cold 5.6 s (apply was slow)
After AOT switches + apply-batching   31.4 MB,  cold 1.5 s
After dropping unused packages        27.1 MB,  cold 1.6 s   ← here
```

The 27 MB binary is smaller than any of the JIT releases sitting in
`clis/` today (range 46–77 MB) and starts in 1.6s cold / 0.2s warm.

---

## 1. SQLite static-linked into the AOT binary

### Decision

Pre-session, the AOT binary crashed at the first SQL call with
`DllNotFoundException: libe_sqlite3.so`. Two fix paths sat in a TODO:
sidecar the `.so`, or static-link via `<DirectPInvoke>` +
`<NativeLibrary>`. We picked static-link: "not looking to poison the
user's filesystem with our dependencies."

### Research summary

- No NuGet package ships a desktop `libe_sqlite3.a`.
  `SQLitePCLRaw.lib.e_sqlite3.*` ships only `.so` / `.dll` / `.dylib`;
  the only `.static` variant is iOS-only.
- Canonical recipes (e.g. [pileofhacks walkthrough](https://pileofhacks.dev/post/a-native-static-binary-with-sqlite-support-in-c/))
  use the distro's `sqlite-static`. Bad for cross-compile.
- The clean recipe: build our own archive from the SQLite amalgamation
  via a single C compile. We already have zig 0.11 in the devcontainer
  driving tree-sitter; same pattern works for SQLite.

### What we built

**`scripts/build/build-sqlite.sh`** (revived from "not currently used")
— fetches the SQLite 3.46.0 amalgamation (SHA256-pinned) and
cross-compiles a static archive per target runtime:

```
linux-x64       linux-musl-x64    linux-arm64    linux-arm
osx-x64         osx-arm64
```

Output: `backend/src/Cli/lib/libe_sqlite3-<rid>.a` (~3.6–5.2 MB each).
Cache check skips the build when all artifacts are already present.

Final compile flags:

```
-Os
-fPIC
-DSQLITE_THREADSAFE=1
-DSQLITE_ENABLE_COLUMN_METADATA      # needed by SQLitePCL bindings
-DSQLITE_ENABLE_MATH_FUNCTIONS
-DSQLITE_DEFAULT_MEMSTATUS=0
-DSQLITE_DQS=0                       # reject double-quoted string literals
```

**`backend/src/Cli/Cli.fsproj`** — added under AOT-conditional
ItemGroups:

```xml
<ItemGroup Condition="'$(PublishAot)' == 'true'">
  <DirectPInvoke Include="e_sqlite3" />
</ItemGroup>
<ItemGroup Condition="... And '$(RuntimeIdentifier)' == 'linux-x64'">
  <NativeLibrary Include="lib/libe_sqlite3-linux-x64.a" />
</ItemGroup>
... (one per RID)
```

`DirectPInvoke` rewrites the managed `[DllImport("e_sqlite3")]` calls
from `SQLitePCLRaw.provider.e_sqlite3` into direct link-time references.
`NativeLibrary` tells the AOT linker where to find them. JIT builds
ignore both items.

### Gotchas (worth saving for next-you)

1. **`OMIT_LOAD_EXTENSION` breaks the AOT link.** SQLitePCLRaw's
   provider `[DllImport]`s `sqlite3_load_extension` and
   `sqlite3_enable_load_extension` *unconditionally*. Under JIT they're
   lazy lookups — never hit if F# doesn't call them. Under NativeAOT +
   DirectPInvoke they're hard link references. Keep extension loading
   compiled in; it's disabled at runtime by default.
2. **`ENABLE_COLUMN_METADATA` is required.** Same pattern. Provider
   binds `sqlite3_column_{database,origin,table}_name`. Off by default
   in SQLite.
3. **`OMIT_AUTOINIT` causes a segfault** on first `sqlite3_open`. The
   provider doesn't call `sqlite3_initialize()` explicitly; relies on
   SQLite's default auto-init-on-first-API-call. Don't suppress.
4. **`tee | tail -1` hides linker failures.** The pipeline's exit code
   is `tail`'s, not `dotnet publish`'s. Use `set -o pipefail` around
   `dotnet publish` invocations.

### Cross-compilation status

```
linux-x64        ✅  built + smoke-tested
linux-musl-x64   ✅  archive built; AOT binary not yet
linux-arm64      ✅  archive built; AOT binary not yet
linux-arm        ✅  archive built; AOT binary not yet
osx-x64          ✅  archive built; AOT binary not yet
osx-arm64        ✅  archive built; AOT binary not yet
win-x64          ❌  archive not built (`.lib` convention; zig can produce
                     it; deferred because the win AOT path is also unproven)
win-arm64        ❌  same as win-x64
```

### What about the bundle's `.so`?

`Microsoft.Data.Sqlite` transitively pulls `SQLitePCLRaw.bundle_e_sqlite3`,
which still drops `libe_sqlite3.so` into the AOT `publish/` dir as a
runtime asset. **But:**

- The published binary doesn't link or load it (`ldd` confirms).
- The build script moves only the bare `Cli` binary into `clis/`; the
  `.so` stays in `publish/` and never reaches the user.
- The install script only copies the bare binary; the runtime
  `EmbeddedResources.extract()` only extracts the gzipped seed db +
  README.

User filesystem stays clean. A possible P2 cleanup: switch to
`Microsoft.Data.Sqlite.Core` + `SQLitePCLRaw.provider.e_sqlite3` to drop
the bundle from the publish dir entirely. Build-output tidiness only.

---

## 2. Trimming + AOT subsystem switches

### Re-added settings that the squash dropped

Per commit `1b32e0827`, these were retained as "safe AOT optimizations"
but didn't survive into the current `Cli.fsproj` after the merge of
#5649:

```xml
<IlcGenerateStackTraceData>false</IlcGenerateStackTraceData>
<IlcFoldIdenticalMethodBodies>true</IlcFoldIdenticalMethodBodies>
```

- `IlcGenerateStackTraceData=false` — drops user-code line metadata.
  Native stack traces still work via DWARF; the
  `AggregateException`-walking diagnostic in `Cli.fs` still surfaces
  inner errors.
- `IlcFoldIdenticalMethodBodies=true` — fold dup method bodies across
  generic instantiations. Free win for any F#-heavy AOT binary.

### Newly added .NET subsystem switches

The CLI doesn't hot-reload, doesn't run startup hooks, doesn't use
built-in COM, doesn't need autorelease-pool semantics, etc. Each
prunes a meaningful slice of metadata + code from the AOT-published
binary; no effect on JIT/Debug builds.

```xml
<MetadataUpdaterSupport>false</MetadataUpdaterSupport>
<StartupHookSupport>false</StartupHookSupport>
<BuiltInComInteropSupport>false</BuiltInComInteropSupport>
<AutoreleasePoolSupport>false</AutoreleasePoolSupport>
<EnableUnsafeBinaryFormatterSerialization>false</EnableUnsafeBinaryFormatterSerialization>
<EnableUnsafeUTF7Encoding>false</EnableUnsafeUTF7Encoding>
<CustomResourceTypesSupport>false</CustomResourceTypesSupport>
```

### Warning hygiene

Before: 30+ warnings, mostly `NU1510` ("PackageReference X will not be
pruned") complaining about packages now shipped in the net10.0 shared
framework — `System.Memory`, `System.Threading.Tasks.Extensions`,
`System.Threading.Channels`, `System.Text.Json`,
`System.Reflection.Emit.Lightweight`, `System.Diagnostics.DiagnosticSource`.
Almost all of these came transitively via Fumble / NodaTime /
SQLitePCLRaw / NReco — not our refs to fix.

Added to `backend/Directory.Build.props`:

```xml
<NoWarn>$(NoWarn);NU1510</NoWarn>
```

After: 1 warning (`IL3053` rollup for FSharp.Core — the trim
analyzer's "this assembly produced warnings" sentinel; the real
warnings are masked by `SuppressTrimAnalysisWarnings=true`).

---

## 3. Cold start — 5.6s → 1.6s

### What "grow" actually does

On first run, the CLI extracts the embedded slim seed (gzipped data.db,
~1.6 MB → 12 MB) into `~/.darklang/data.db` and then `growIfNeeded`
runs:

1. `applyUnappliedOps` — load all `package_ops` with `applied=0`,
   group by `(branch_id, commit_hash)`, replay each group into
   projection tables (`locations`, `package_types`, `package_values`,
   `package_functions`, `package_dependencies`, `deprecations`).
2. `PackageRefsGenerator.generate`.
3. `evaluateAllValues` — for every `package_value` with
   `rt_dval IS NULL`, evaluate the body, persist `rt_dval` +
   `value_type`.
4. WAL checkpoint truncate.

The slim seed (vs a pre-projected one) is intentional: every user
takes the same boot path, and the ops are present for rewind /
inspection.

### Telemetry before the fix

```
seed.applyOps        5252 ms   ← 97% of the cost
seed.generateRefs      24 ms
seed.evaluateValues   109 ms
seed.walCheckpoint      7 ms
─────────────────────────────
total                5551 ms
```

`applyOps` for 9845 ops = 0.53 ms/op. The bottleneck was Fumble's
per-statement connection-from-pool model: each of the ~20k SQL
statements in playback ran on a fresh connection in its own implicit
transaction. Even with `synchronous=NORMAL` + WAL, that's ~20k tiny
WAL commits.

### What we changed

**Rewrote `backend/src/LibDB/PackageOpPlayback.fs` against raw
`Microsoft.Data.Sqlite`** (no Fumble). Every apply* function now takes
a `SqliteConnection`. Public API:

```fsharp
// Bulk path: caller controls transaction boundaries.
val applyOpsOnConnection :
  SqliteConnection -> BranchId -> string option -> PackageOp list
    -> Task<unit>

// Single-shot path: opens its own connection + transaction. Used by
// Inserts.fs at commit time (small op batches).
val applyOps :
  BranchId -> string option -> PackageOp list -> Task<unit>
```

**Rewrote `Seed.applyUnappliedOps`** to open one connection, set
`PRAGMA synchronous=OFF` for the duration of the bulk apply, wrap the
whole thing in one `BEGIN ... COMMIT`, and run all groups + the final
`UPDATE applied=1` inside it. Crash safety isn't a concern here:
`applyOps` is idempotent. If we die mid-apply, the next run re-reads
`applied=0` and replays.

The single-shot `applyOps` wrapper (used by `Inserts.fs`) also got a
transaction by default. Same atomic semantics as before, but using
one connection + one transaction instead of one connection per
statement.

### Telemetry after

```
seed.applyOps        1251 ms   ← 4.2× faster
seed.generateRefs      21 ms
seed.evaluateValues   101 ms
seed.walCheckpoint     15 ms
─────────────────────────────
total                1541 ms
```

The remaining 1.25s in `applyOps` is real work: deserializing 9845
op blobs, computing/checking hashes, dispatching, raw SQL execution.

### Adjacent benefit: `reload-packages` should also be faster

`LocalExec.reloadPackages → LibDB.Inserts.insertAndApplyOpsWithCommit`
calls the same `applyOps`. The convenience wrapper now uses one
connection + one transaction for the whole batch (was N connections
via Fumble pool with auto-commit-each-statement). DB-write portion
should drop similarly. **But:** if parsing dominates the 40s reload
(likely — tree-sitter + F# parser over every `.dark` file), this
only shaves the tail. Worth instrumenting `LoadPackagesFromDisk.load`
if you want to quantify which slice is which.

---

## 4. The big size win — dropping unused heavyweight packages

### Finding

A grep audit found that **`FSharpPlus` had zero `open FSharpPlus`
statements** across the whole backend. Only 5 explicit qualified-name
callsites in 3 files, and **the package was referenced by 10 different
`paket.references` files**. The historical size analysis pegged
FSharpPlus alone at 7.6 MB of metadata.

Same pattern for `FSharpx.Extras` (4 callsites in
`Prelude/ResizeArray.fs`, zero `open`s) and absolutely zero usage of
`FSharp.SystemTextJson` + `NodaTime.Serialization.SystemTextJson` —
those were stale carryovers from when `Json.Vanilla` existed.

### What we replaced

```
Prelude/Dictionary.fs        FSharpPlus.Dictionary.tryGetValue/keys/values
                             → Dictionary.TryGetValue + .Keys + .Values
Prelude/Map.fs               FSharpPlus.Map.union (×2 via mergeFavoring*)
                             → Map.fold (fun acc k v -> Map.add k v acc) ...
Prelude/ResizeArray.fs       FSharpx.Collections.ResizeArray.{iter,map,toList,toSeq}
                             → trivial for/let-rec replacements
Builtins.Pure/Libs/String.fs FSharpPlus.List.intersperse
                             → 4-line inline implementation
```

Total: **9 callsites**, all in helper modules. None of them used
advanced FSharpPlus features (Lens, computational monad zoo, etc.).
Just basic collection utilities the F# stdlib (or trivial
hand-written code) covers.

### Packages dropped

From `backend/paket.dependencies`:

```
- nuget FSharpPlus = 1.5.0
- nuget FSharpx.Extras = 3.1.0
- nuget FSharp.SystemTextJson = 1.3.13
- nuget NodaTime.Serialization.SystemTextJson = 1.3.0
```

From `paket.references` files (10 of them):

```
- FSharpPlus            (Cli, Prelude, LibExecution, 7× Builtins)
- FSharpX.Extras        (Prelude)
- FSharp.SystemTextJson (Prelude)
- NodaTime.Serialization.SystemTextJson (Prelude)
```

### Size impact

```
Before drop:  31.4 MB
After drop:   27.1 MB
Δ:            −4.3 MB (−14%)
```

Behavior unchanged: same cold-start time (1.6s vs 1.54s — noise),
same warm-start (0.2s), same `ldd` (no new deps surfaced).

---

## 5. Files changed (uncommitted)

```
M  backend/Directory.Build.props                 NoWarn NU1510
M  backend/paket.dependencies                    drop 4 unused deps
M  backend/src/*/paket.references (10 files)     drop unused refs
M  backend/src/Cli/Cli.fsproj                    DirectPInvoke + per-RID
                                                 NativeLibrary; 7 AOT
                                                 subsystem switches;
                                                 Ilc{Generate*,Fold*}
M  backend/src/Prelude/Dictionary.fs             inline tryGetValue/keys/values
M  backend/src/Prelude/Map.fs                    inline mergeFavoring*
M  backend/src/Prelude/ResizeArray.fs            inline iter/map/toList/toSeq
M  backend/src/Builtins/Builtins.Pure/Libs/String.fs   inline intersperse
M  backend/src/LibDB/PackageOpPlayback.fs        rewritten raw Sqlite;
                                                 shared-connection bulk path
M  backend/src/LibDB/Seed.fs                     one-conn/one-tx/sync=OFF
                                                 cold-start; single
                                                 applied=1 sweep
M  scripts/build/build-sqlite.sh                 re-purposed: emits .a per
                                                 runtime via zig cc
?? backend/src/Cli/lib/libe_sqlite3-*.a          6 archives, ~26 MB total
?? thinking/cli-aot-progress-2026-05-12.md       this report
```

`backend/testfiles/execution/stdlib/nomodule.dark` is also listed as
modified but predates this session and isn't part of the AOT work.

---

## 6. Smoke-test record

```
$ TESTDIR=$(mktemp -d)
$ cp clis/darklang-alpha-61cb32ac75-linux-x64 $TESTDIR/darklang
$ cd $TESTDIR && HOME=$TESTDIR/fake-home

$ time ./darklang eval "1L + 2L"
Setting up Darklang CLI data directory at /tmp/.../.darklang
CLI data directory setup complete
Growing package DB from ops (9845 ops to apply)...
Package DB ready
3
real    0m1.608s

$ time ./darklang eval "1L + 2L"
3
real    0m0.216s

$ ./darklang eval "10L * 5L"
50

$ ldd ./darklang
        linux-vdso.so.1
        libm.so.6
        libc.so.6
        /lib64/ld-linux-x86-64.so.2
```

The SQL-heavy `growIfNeeded` path (previously failing under AOT with
`DllNotFoundException`) ran 9845 ops to completion. No segfaults, no
silent failures, no stderr leakage. `ldd` confirms no
`libe_sqlite3.so` dependency. Binary file size 27.1 MB.

---

## 7. What's still on the table

### Easy wins, deferred

- **Build the other 5 RID AOT binaries.** Static archives exist for
  linux-musl/arm64/arm and osx-x64/arm64. Just need to run
  `./scripts/build/build-release-cli-exes.sh --aot --runtimes=…` for
  each. linux-musl-x64 in particular: the last attempt
  (`1b32e0827`) hit a missing musl-cross CRT; zig might "just work"
  now.
- **Commit the per-runtime `.a` archives.** ~26 MB across 6 RIDs.
  Pattern-matches the tree-sitter `lib/` precedent (~30 MB committed
  there). Currently untracked.
- **Switch `Microsoft.Data.Sqlite` → `Microsoft.Data.Sqlite.Core` +
  `SQLitePCLRaw.provider.e_sqlite3`.** Drops the bundle's `.so` from
  the AOT publish dir. Build-output tidiness; no user-visible win.

### Medium effort, medium reward

- **Prepared-statement reuse in `applyOpsOnConnection`.** Each SQL
  template currently rebuilds a `SqliteCommand` per call. Caching one
  prepared statement per op-type and reusing it across the 9845 ops
  could shave another 200–400 ms off cold start. The trade-off is
  managing a small map of `SqliteCommand`s and resetting parameters
  between calls.
- **Batched dependency inserts.** `updateDependencies` runs DELETE +
  INSERT per item. Collecting `(hash, deps)` pairs and doing one big
  batch at the end would reduce statement count further. Tricky to
  preserve correct ordering when ops can re-add an item.
- **Audit the ~5 remaining IL3050 sites** currently masked by
  `SuppressTrimAnalysisWarnings=true`. Per the prior AOT commit
  (`4cd03ebb0`), these were "unchanged from before — handled in
  follow-on work." Worth flipping the suppression off temporarily
  and reading what's there.

### Bigger-effort size levers (not yet pulled)

- **`IlcGenerateMapFile=true` closure analysis.** Adds ~30s to the
  AOT build and emits a map showing exactly which types/methods
  contributed what to the final binary. Best tool for "where else
  is the fat?" None of the ad-hoc switches will help past a certain
  point — the next layer of wins requires data on what's actually
  in `.rodata` (21 MB) and `.hydrated` (7 MB).
- **Replace Ply with the built-in F# `task` CE.** F# 6+ has a native
  `task` CE; Ply was a stopgap. The codebase still does
  `open FSharp.Control.Tasks` and uses `uply { }` extensively. Ply
  is ~12 KB but the bigger value is one fewer top-level dep and
  fewer F# computation-expression types in the metadata. Touches
  every `uply { }` block — large mechanical refactor.
- **Decouple `Builtins.Matter` (and `Builtins.CliHost`) from
  `LibCloud`.** `Builtins.Matter/Libs/DB.fs` and
  `Builtins.CliHost/Libs/Cli.fs` both `open LibCloud.Toplevels`,
  pulling LibCloud (`IsTrimmable=false`) into the AOT closure. After
  the recent "drop Cloud branding" pass, LibCloud is a stub — moving
  Toplevels into `LibDB` (where it morally belongs) would let us
  drop LibCloud from the CLI graph entirely.
- **`FsRegEx` audit.** It's a thin wrapper over
  `System.Text.RegularExpressions` (in net10 framework). If we use
  only basic regex features we can drop the wrapper and call the
  framework directly.

### Reload-packages timing audit (you flagged 40s)

Same `applyOps` path runs from `Inserts.insertAndApplyOpsWithCommit`,
so the convenience wrapper change should help. But if parsing
dominates the 40s (likely — tree-sitter + F# parser per `.dark`
file), the DB side is a small slice. To know which is which:
instrument `LoadPackagesFromDisk.load` and look at per-file parse
timings vs the `seed.applyOps` figure.

### Cross-cutting / platform

- **macOS code signing** is still a pre-existing problem (issue
  #5307, vault `Code-Signing.md`). The AOT binary inherits it.
- **Linux back-compat.** A NativeAOT binary built on Linux N only
  runs on Linux ≥ N. CI strategy: build on the oldest target distro
  we support, or accept that AOT binaries serve only modern Linux
  and fall back to JIT for older distros.
- **Win-x64 / win-arm64 AOT path** needs both (a) the `.lib` static
  archive built for those targets and (b) validation that the
  Windows AOT publish flow through ilc / link.exe works. Low
  priority until the unix paths are stable.

---

## 8. Sources + breadcrumbs

**Modified files** (all in this session):
- `backend/Directory.Build.props`
- `backend/paket.dependencies`
- 10 × `backend/src/.../paket.references`
- `backend/src/Cli/Cli.fsproj`
- `backend/src/Prelude/{Dictionary.fs, Map.fs, ResizeArray.fs}`
- `backend/src/Builtins/Builtins.Pure/Libs/String.fs`
- `backend/src/LibDB/PackageOpPlayback.fs`
- `backend/src/LibDB/Seed.fs`
- `scripts/build/build-sqlite.sh`

**New artifacts** (untracked):
- `backend/src/Cli/lib/libe_sqlite3-{linux-x64,linux-musl-x64,linux-arm64,linux-arm,osx-x64,osx-arm64}.a`
- `clis/darklang-alpha-61cb32ac75-linux-x64` (27 MB AOT binary)

**External:**
- [SQLite amalgamation 3.46.0](https://www.sqlite.org/2024/sqlite-amalgamation-3460000.zip) (SHA256-pinned)
- [zig 0.11.0](https://ziglang.org/) — devcontainer at `~/zig/zig`
- [DirectPInvoke + NativeLibrary docs](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/interop)
- [SQLitePCL.raw](https://github.com/ericsink/SQLitePCL.raw) — provider supplying the `[DllImport]`s
- [pileofhacks SQLite-AOT walkthrough](https://pileofhacks.dev/post/a-native-static-binary-with-sqlite-support-in-c/) — recipe we adapted

**Companion docs in `thinking/`:**
- `cli-aot-status-2026-05-12.md` — pre-cold-start-optimization status
  snapshot. Superseded by this report.

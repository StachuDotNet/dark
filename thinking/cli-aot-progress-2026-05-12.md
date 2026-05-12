# CLI NativeAOT — session progress report

**Date:** 2026-05-12
**Branch:** `aot-and-cold-start-improvements` (10 commits ahead of `main`)
**Scope:** Unblock + harden the NativeAOT CLI publish path. Static-link
SQLite, tighten trim/AOT settings, accelerate cold start, drop unused
heavyweight packages, surface latent AOT bugs.

---

## Headline numbers (linux-x64)

| Metric                         | Pre-session         | End of session     | Delta             |
|--------------------------------|---------------------|--------------------|-------------------|
| AOT publishable                | ❌ (sqlite blocker) | ✅                 | unblocked         |
| Binary size                    | n/a (failed)        | **27 MB**          | JITs were 46–77   |
| Runtime deps (`ldd`)           | n/a                 | libc/libm/ld-linux | no libe_sqlite3   |
| Cold start (`eval "1L+2L"`)    | 5.55 s              | **1.49 s**         | **−3.7×**         |
| `seed.applyOps` phase          | 5.25 s              | **1.18 s**         | **−4.4×**         |
| Warm start                     | 0.20 s              | 0.21 s             | unchanged         |
| AOT build warnings             | ~30 (NU1510 noise)  | 48 (all FSharp.Core) | now visible    |
| Float ops under AOT            | crashes             | works              | sprintf fix       |

The 27 MB AOT binary is smaller than every JIT release in `clis/` today
(range 46–77 MB) and starts in 1.5 s cold / 0.2 s warm.

```
Round 1 — SQLite static-link via zig cc                 32 MB  cold 5.6 s
Round 2 — AOT subsystem switches + apply batching       31 MB  cold 1.5 s
Round 3 — drop FSharpPlus / FSharpx / FSharp.STJ        27 MB  cold 1.6 s
Round 4 — fix sprintf "%.Ng" PrintfImpl crash           27 MB  cold 1.5 s
Round 5 — prep-stmt cache + IL warnings unsuppressed    28 MB  cold 1.5 s
```

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
cross-compiles a static archive per target runtime via `zig cc`:

```
linux-x64       linux-musl-x64    linux-arm64    linux-arm
osx-x64         osx-arm64
```

Output: `backend/src/Cli/lib/libe_sqlite3-<rid>.a` (~3.6–5.2 MB each).
**Gitignored** — built locally / in CI on demand. Cache check at top of
script skips when all artifacts already present.

Final compile flags:

```
-Os
-fPIC
-DSQLITE_THREADSAFE=1
-DSQLITE_ENABLE_COLUMN_METADATA      # SQLitePCL binds *_column_*_name unconditionally
-DSQLITE_ENABLE_MATH_FUNCTIONS
-DSQLITE_DEFAULT_MEMSTATUS=0
-DSQLITE_DQS=0                       # reject double-quoted string literals
```

**`backend/src/Cli/Cli.fsproj`** — adds under AOT-conditional ItemGroups:

```xml
<ItemGroup Condition="'$(PublishAot)' == 'true'">
  <DirectPInvoke Include="e_sqlite3" />
</ItemGroup>
<ItemGroup Condition="... And '$(RuntimeIdentifier)' == 'linux-x64'">
  <NativeLibrary Include="lib/libe_sqlite3-linux-x64.a" />
</ItemGroup>
... (one per RID)
```

### Gotchas + how to handle each

**1. `OMIT_LOAD_EXTENSION` breaks the AOT link.** SQLitePCLRaw's
provider includes `[DllImport("e_sqlite3")]` for
`sqlite3_load_extension` and `sqlite3_enable_load_extension`
*unconditionally*. Under JIT they're lazy lookups — never hit if F#
doesn't call them. Under NativeAOT + DirectPInvoke they're hard link
references; missing → linker error.

**What to do:** keep extension loading compiled in (don't add the
OMIT). It's disabled at runtime by default; our code never calls
`sqlite3_enable_load_extension(db, 1)` so the security posture is
unchanged. Costs ~50 KB on the archive — accept it. The only way to
recover the symbols-OFF size would be a fork of SQLitePCLRaw that
omits these `[DllImport]`s, which isn't worth it.

**2. `ENABLE_COLUMN_METADATA` is required (off by default).** Same
pattern. The provider binds `sqlite3_column_{database,origin,table}_name`.
Off by default in stock SQLite builds.

**What to do:** keep `-DSQLITE_ENABLE_COLUMN_METADATA` in build-sqlite.sh.
Same trade-off as above; ~10 KB cost. Don't try to remove without
auditing SQLitePCLRaw's `[DllImport]` surface for any other unconditionally-bound symbols.

**3. `OMIT_AUTOINIT` causes a segfault on first `sqlite3_open`.** The
provider doesn't call `sqlite3_initialize()` explicitly; relies on
SQLite's default auto-init-on-first-API-call.

**What to do:** don't add `-DSQLITE_OMIT_AUTOINIT`. If we ever want to
save the bytes (auto-init code is tiny, ~5 KB), we'd need to also patch
in an explicit `sqlite3_initialize()` call at process startup. Not
worth it.

**4. `tee | tail -1` hides linker failures.** Build script's pipe exit
code defaults to `tail`'s, not `dotnet publish`'s.

**What to do:** always `set -o pipefail` when chaining `dotnet publish`
through pipes. The build script itself has `set -euo pipefail` at the
top — the trap is in invoking scripts. Built into our muscle memory now;
also documented in this report.

### Cross-compilation status

```
linux-x64        ✅  built + smoke-tested
linux-musl-x64   ✅  archive built; AOT binary not yet
linux-arm64      ✅  archive built; AOT binary not yet
linux-arm        ✅  archive built; AOT binary not yet
osx-x64          ✅  archive built; AOT binary not yet
osx-arm64        ✅  archive built; AOT binary not yet
win-x64          ❌  archive not built (`.lib` convention; zig can produce
                     it; deferred because the win AOT path through ilc is
                     also unproven)
win-arm64        ❌  same as win-x64
```

The bundle's `libe_sqlite3.so` still lands in the AOT `publish/` dir
(the SDK copies it as a runtime asset of `bundle_e_sqlite3`). Doesn't
matter — the build script moves only the bare `Cli` binary into `clis/`;
the `.so` stays in `publish/` and never reaches the user. `ldd` on the
shipped binary confirms no `libe_sqlite3.so` dependency. **Per your
direction, leaving the build-output noise alone.**

---

## 2. Trimming + AOT subsystem switches

### Re-added settings that the squash dropped

```xml
<IlcGenerateStackTraceData>false</IlcGenerateStackTraceData>
<IlcFoldIdenticalMethodBodies>true</IlcFoldIdenticalMethodBodies>
```

Per `1b32e0827`, both retained as "safe AOT optimizations" but didn't
survive into `Cli.fsproj` after the merge of #5649.

### New .NET subsystem switches

```xml
<MetadataUpdaterSupport>false</MetadataUpdaterSupport>          <!-- no hot-reload -->
<StartupHookSupport>false</StartupHookSupport>                  <!-- no startup hooks -->
<BuiltInComInteropSupport>false</BuiltInComInteropSupport>
<AutoreleasePoolSupport>false</AutoreleasePoolSupport>          <!-- not macOS-Cocoa -->
<EnableUnsafeBinaryFormatterSerialization>false</EnableUnsafeBinaryFormatterSerialization>
<EnableUnsafeUTF7Encoding>false</EnableUnsafeUTF7Encoding>
<CustomResourceTypesSupport>false</CustomResourceTypesSupport>  <!-- InvariantGlob is on -->
```

### Warning visibility

**Pre-session:** `SuppressTrimAnalysisWarnings=true` — all warnings
hidden behind a single rolled-up `IL3053` for FSharp.Core.

**Now:** `SuppressTrimAnalysisWarnings=false` + `TrimmerSingleWarn=false`.
48 individual warnings surface. Categorized:

```
21 sformat.fs     — F# StructuredFormat (used by `%A` / `%O`-with-records)
13 printf.fs      — F# PrintfImpl (sprintf "%.Ng" etc.)
13 reflect.fs     — F# reflection helpers
 1 prim-types.fs

By code:
14 IL2070  DAM (DynamicallyAccessedMembers) annotation mismatch on `this`
 9 IL3050  RequiresDynamicCode on a reachable method
 9 IL2067  DAM on a parameter
 5 IL2060  MakeGenericMethod with no static type info
 5 IL2075  GetType() returning a type with no DAM annotation
 3 IL2072 / 1 IL2091 / 1 IL2080 / 1 IL2055
```

**Crucially: all 48 are inside FSharp.Core itself.** Zero originate in
our code. They flag F#'s internal reflective/dynamic-code patterns the
trimmer can't reason about. Most are pessimism; the dangerous ones are
where our code happens to call into a flagged path.

We found one such site in the wild (Float toString, see §3 below).
The others may be latent landmines if we add code paths that exercise
F#'s `%A`-style formatters on user types. With the warnings now
visible, a new occurrence in CI is a signal to investigate.

### Warning hygiene (NU1510)

Added to `backend/Directory.Build.props`:

```xml
<NoWarn>$(NoWarn);NU1510</NoWarn>
```

NU1510 fires for transitive package refs that are now shipped by the
net10.0 shared framework (System.Memory etc.). Not actionable from our
side — paket pulls them via Fumble / NodaTime / SQLitePCLRaw. 28
warnings of pure noise pre-fix.

---

## 3. Cold start — 5.6 s → 1.5 s

### Telemetry before

```
seed.applyOps        5252 ms   ← 97% of the cost
seed.generateRefs      24 ms
seed.evaluateValues   109 ms
seed.walCheckpoint      7 ms
─────────────────────────────
total                5551 ms
```

`applyOps` for 9845 ops = 0.53 ms/op. Bottleneck was Fumble's
per-statement connection-from-pool model: each of the ~20k SQL
statements in playback ran on a fresh connection in its own implicit
transaction. Even with `synchronous=NORMAL` + WAL, that's ~20k tiny
WAL commits.

### What we changed

**Rewrote `backend/src/LibDB/PackageOpPlayback.fs` against raw
`Microsoft.Data.Sqlite`** (no Fumble). Every apply* function takes a
`Ctx` bundling a `SqliteConnection` + a `Dictionary<string,
SqliteCommand>` prepared-statement cache. Public surface:

```fsharp
val applyOpsOnConnection :
  SqliteConnection -> BranchId -> string option -> PackageOp list -> Task<unit>
val applyOps :
  BranchId -> string option -> PackageOp list -> Task<unit>
```

**`Seed.applyUnappliedOps`** now opens one connection, sets
`PRAGMA synchronous=OFF` for the duration of the bulk apply, wraps the
whole thing in one `BEGIN ... COMMIT`, runs all op-groups via
`applyOpsOnConnection`, and ends with the bulk
`UPDATE applied=1 WHERE applied=0`. Crash safety isn't a concern:
`applyOps` is idempotent; a mid-batch crash re-replays on next run.

The single-shot `applyOps` wrapper (used by `Inserts.fs`) also got a
transaction by default. Same atomic semantics as before, but one conn
+ one tx instead of one conn per statement.

### Prepared-statement cache

The `Ctx` caches SqliteCommands by SQL text. First call to a given
SQL builds and `Prepare()`s the command; subsequent calls clear the
parameter collection and reuse. Saves ~70 ms (1251 → 1184 in
`applyOps`).

Smaller than hoped — SQLite's internal C-side statement cache was
already handling the SQL-plan-reuse half; we just shaved the
managed-side allocation half.

### Telemetry after

```
seed.applyOps        1184 ms   ← 4.4× faster
seed.generateRefs      21 ms
seed.evaluateValues   101 ms   (unchanged — untouched path)
seed.walCheckpoint     15 ms
─────────────────────────────
total                1494 ms
```

The remaining 1.18 s in `applyOps` is real work: deserializing 9845
op blobs, computing/checking hashes, PT→RT translation, raw SQL
execution.

### A failed experiment: batched dep inserts

Tried collecting all per-item `updateDependencies` calls into a
batch, then running one chunked DELETE + chunked INSERT at the end
of `applyOpsOnConnection`. The math suggested 20k statements → 20.

**Result: 2.5 s cold start (slower than the per-item path).** DB
integrity intact (same row counts), but the new code path was
dominated by:
- Building a 6 KB SQL string per chunk (per call, even on cache hit)
- `Parameters.Clear()` on 6144-param collections
- 6144 `AddWithValue` calls per chunk

The per-item path's small parameter sets and SQLite's internal cache
beat the naive batched approach. Reverted.

**If revisited**: cache the SQL string per chunk-size at module init;
pre-add parameters to the cached command once and reuse parameter
*slots* (set `Parameters[N].Value` instead of `Clear()` + `Add`);
consider smaller chunk sizes (64 or 256). Or accept that the per-item
path is fine and pick a different lever.

---

## 4. Package drops — 4 MB off the binary

A grep audit showed **zero `open FSharpPlus`** statements anywhere in
the backend, despite the package being listed in 10 different
`paket.references` files. Only 9 callsites used qualified names — all
trivially replaceable with stdlib or 4-line hand-rolled equivalents.
Same pattern for `FSharpx.Extras`, plus `FSharp.SystemTextJson` +
`NodaTime.Serialization.SystemTextJson` were stale carryovers from
when `Json.Vanilla` existed.

Replacements:

```
Prelude/Dictionary.fs        FSharpPlus.Dictionary.tryGetValue/keys/values
                             → TryGetValue + .Keys + .Values
Prelude/Map.fs               FSharpPlus.Map.union (×2 via mergeFavoring*)
                             → Map.fold (fun acc k v -> Map.add k v acc) ...
Prelude/ResizeArray.fs       FSharpx.Collections.ResizeArray.{iter,map,
                             toList,toSeq} → for-loop / List.ofSeq / cast
Builtins.Pure/Libs/String.fs FSharpPlus.List.intersperse
                             → 4-line inline implementation
```

Packages dropped from `paket.dependencies`:

```
- FSharpPlus
- FSharpx.Extras
- FSharp.SystemTextJson           (stale; Json.Vanilla was deleted)
- NodaTime.Serialization.SystemTextJson  (stale; only Json.Vanilla used it)
```

Then a separate audit caught `FsRegEx` + `System.Diagnostics.DiagnosticSource`
listed in `paket.references` files with **zero callsites** in the source.
Already trim-removed from the binary, but cleaning the manifest is tidy.

**Size impact**: 31.4 MB → 27.1 MB (−4.3 MB / −14%).

The historical size analysis put FSharpPlus alone at 7.6 MB of
metadata; the trim-mode AOT compiler was pulling all of them into
`.rodata` for no runtime benefit.

---

## 5. Float crash fix — sprintf "%.Ng" goes through PrintfImpl

Broader smoke-testing the AOT binary surfaced a runtime crash on
*any* Float operation that rendered through `Stdlib.Float.toString`
or JSON-serialize-of-DFloat:

```
NotSupportedException: MakeGenericMethod_NoMetadata,
  Microsoft.FSharp.Core.PrintfImpl+Specializations`3
    [Microsoft.FSharp.Core.Unit,System.String,System.String]
    .CaptureFinal1[System.Double](Microsoft.FSharp.Core.PrintfImpl+Step[])
```

3 call sites, identical pattern:

```
Builtins.Pure/Libs/Float.fs    Stdlib.Float.toString (`sprintf "%.12g" f`)
Builtins.Pure/Libs/Json.fs     Stdlib.Json.serialize on DFloat (`%.16g`)
LibSerialization/DvalReprInternalQueryable.fs                   (`%.12g`)
```

`sprintf "%.Ng"` compiles to FSharp.Core PrintfImpl, which uses
`MakeGenericMethod` on `Double` at runtime — Native AOT can't satisfy
that once the type's metadata is trimmed. Replaced each with
`f.ToString("GN", InvariantCulture)` for the same canonical output
and zero reflection.

Found exactly by unsuppressing the AOT analysis warnings: the
`SuppressTrimAnalysisWarnings=true` setting we just turned off would
have flagged these via IL3050 ("RequiresDynamicCode in PrintfImpl").

Verified: `eval "10.0 / 3.0"` → 3.33333333333; list/dict/Json paths
through DFloat all render correctly.

---

## 6. CI changes

`build-cli` job now:

1. **Caches SQLite static archives.** New step before the publish:
   shasum `scripts/build/build-sqlite.sh` (which has the SQLite
   version pinned) as the cache key. On hit, `restore_cache` populates
   `backend/src/Cli/lib/` and the script's own "all artifacts present"
   check short-circuits. On miss, `zig cc` builds the six archives
   (~5 s each) and `save_cache` stashes them. Same pattern as the
   tree-sitter cache, smaller payload.

2. **PRs use `--aot`.** Per-PR validation now exercises the AOT
   publish chain end-to-end. Main keeps `--cross-compile` (JIT) for
   the moment — cross-target AOT publish through ilc hasn't been
   validated for osx/win/musl/arm, so flipping main would drop
   those release artifacts. Once cross-target is proven, the if
   collapses to one branch and `--aot` becomes the only path.

`.gitignore` excludes `backend/src/Cli/lib/libe_sqlite3-*.a` so the
build artifacts don't show up in `assert-clean-worktree`.

---

## 7. Commits on this branch

```
e6a252b10  ci: build SQLite static archives + AOT-publish on PRs
6873bf210  build: drop unused FsRegEx + DiagnosticSource refs
a41741f97  perf: cache prepared SqliteCommands across the playback batch
a526c12cf  aot: unsuppress trim/AOT warnings so latent issues surface
bd9fb989b  aot: kill sprintf "%.Ng" float formatters — PrintfImpl trips trim
2298832dd  docs: AOT progress + status reports in thinking/
c615e5278  size: drop FSharpPlus / FSharpx.Extras / FSharp.STJ / NodaTime.STJ
a7b6a11b9  perf: rewrite package-op playback against raw Sqlite; one tx for cold start
474593fe0  build: silence NU1510 (unprunable transitive package refs)
375fe5b27  aot: static-link libe_sqlite3 + tighten AOT subsystem switches
064eb0e47  aot: build SQLite static archives via zig cc for static-link
```

---

## 8. What's still on the table

### Ready to land (medium effort, validated value)

- **Cross-target AOT publish for the other 5 RIDs.** Static archives
  exist for linux-musl/arm64/arm and osx-x64/arm64; just need to run
  `./scripts/build/build-release-cli-exes.sh --aot --runtimes=…` on
  each and validate. linux-musl-x64 in particular: the last attempt
  (`1b32e0827`) hit a missing musl-cross CRT; zig might "just work"
  now. Once these pass, flip CI's main branch from JIT-cross-compile
  to AOT-cross-compile, and remove the `--aot` flag from the scripts
  (it becomes the only path).

- **Win-x64 / win-arm64 archives + AOT path.** Needs zig cc → `.lib`
  for those targets, plus validating the Windows AOT publish through
  ilc / link.exe. Lower priority than the unix RIDs.

### Ideas for shrinking SQLite static archive further (currently ~5 MB each)

Not acted on yet; just thinking.

- **`-Oz` instead of `-Os`.** clang/zig support `-Oz` for "optimize
  for size even harder than -Os". Worth a try; usually 1-3% smaller.
- **Audit SQLitePCLRaw's `[DllImport]` surface and OMIT what's truly
  unused.** Today we keep load_extension + column-metadata in because
  SQLitePCL binds them unconditionally. A diff of every DllImport in
  SQLitePCLRaw.provider.e_sqlite3.NativeMethods against what
  Microsoft.Data.Sqlite actually calls would show which features could
  be `SQLITE_OMIT_*`-ed if we patched the provider (fork or
  source-include) to drop the unused imports.
- **`SQLITE_OMIT_*` candidates** — TRIGGER, VIEW, SUBQUERY,
  WINDOWFUNC, GET_TABLE, AUTHORIZATION, INTROSPECTION_PRAGMAS,
  PROGRESS_CALLBACK, DECLTYPE. Each needs to be checked against (a)
  SQLitePCL bindings, (b) `backend/migrations/schema.sql`, and (c)
  any Dark-side SQL we run. Probably 0.5-1 MB combined if applicable.
- **Strip symbols from the `.a` more aggressively** — `zig ar` likely
  retains some symbol info. `strip --strip-unneeded` post-build.

### Other potential wins (parked per your direction)

- **Decouple `Builtins.Matter` (and `Builtins.CliHost`) from `LibCloud`.**
  Both `open LibCloud.Toplevels`, pulling LibCloud (`IsTrimmable=false`)
  into the AOT closure. After the "drop Cloud branding" pass, LibCloud
  is essentially a stub — moving Toplevels into `LibDB` (where it
  morally belongs) would let us drop LibCloud from the CLI graph
  entirely.

- **Replace Ply with the built-in F# `task` CE.** F# 6+ has a native
  `task` CE; Ply was a stopgap. The codebase still does
  `open FSharp.Control.Tasks` and uses `uply { }` in ~12 projects.
  Ply is ~12 KB on disk, but one fewer top-level dep and fewer
  computation-expression types in metadata. Touches every `uply { }`
  block — large mechanical refactor.

- **`IlcGenerateMapFile=true` closure analysis.** Adds ~30 s to the
  AOT build and emits a map showing which types/methods contributed
  what to the final binary. Best tool for "where else is the fat?"
  None of the ad-hoc switches will help past a certain point — the
  next layer of wins requires data on what's actually filling the
  `.rodata` (21 MB) and `.hydrated` (7 MB) sections.

- **macOS code signing** is still a pre-existing problem (issue
  #5307, vault `Code-Signing.md`). The AOT binary inherits it.

- **Linux back-compat.** A NativeAOT binary built on Linux N only
  runs on Linux ≥ N. CI strategy: build on the oldest target distro
  we support, or accept that AOT serves only modern Linux and fall
  back to JIT for older distros.

### Cross-cutting

- **`SuppressTrimAnalysisWarnings=false` is now load-bearing.**
  Treat any new trim/AOT warning surfacing in CI as a signal to
  investigate. We found the sprintf "%.Ng" crash exactly this way.

- **The `--aot` flag should go away** once cross-target AOT is
  validated. Both `build-release-cli-exes.sh` and
  `build-and-install-cli-on-host` accept it; once it's the only path,
  drop the flag, drop the `if` branches, drop the warning comments.

---

## 9. Sources + breadcrumbs

**Modified files** (committed on the branch):
- `.circleci/config.yml`
- `.gitignore`
- `backend/Directory.Build.props`
- `backend/paket.dependencies` + `backend/paket.lock`
- 10 × `backend/src/.../paket.references`
- `backend/src/Cli/Cli.fsproj`
- `backend/src/Prelude/{Dictionary.fs, Map.fs, ResizeArray.fs}`
- `backend/src/Builtins/Builtins.Pure/Libs/{String.fs, Float.fs, Json.fs}`
- `backend/src/LibSerialization/DvalReprInternalQueryable.fs`
- `backend/src/LibDB/{PackageOpPlayback.fs, Seed.fs}`
- `scripts/build/{build-sqlite.sh, build-release-cli-exes.sh}`

**External:**
- [SQLite amalgamation 3.46.0](https://www.sqlite.org/2024/sqlite-amalgamation-3460000.zip) (SHA256-pinned)
- [zig 0.11.0](https://ziglang.org/) — devcontainer at `~/zig/zig`
- [DirectPInvoke + NativeLibrary docs](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/interop)
- [SQLitePCL.raw](https://github.com/ericsink/SQLitePCL.raw) — provider supplying the `[DllImport]`s
- [pileofhacks SQLite-AOT walkthrough](https://pileofhacks.dev/post/a-native-static-binary-with-sqlite-support-in-c/) — recipe we adapted
- [Trim analysis warnings reference](https://learn.microsoft.com/en-us/dotnet/core/deploying/trimming/trim-warnings/) — for the IL2xxx / IL3xxx codes

**Companion docs in `thinking/`:**
- `cli-aot-status-2026-05-12.md` — pre-cold-start-optimization snapshot.
  Superseded by this report; left in place for context if you want the
  earlier framing.

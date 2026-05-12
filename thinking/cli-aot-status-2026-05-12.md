# CLI AOT / NativeAOT Status Report

**Date:** 2026-05-12 (updated post-static-link)
**Scope:** Darklang CLI project (`backend/src/Cli`) — current state of NativeAOT
publishing, what's landed, what's left, and the load-bearing TODO that just
got resolved.

---

## TL;DR

**NativeAOT is unblocked.** The CLI now builds with `PublishAot=true` and
runs standalone on the host with **zero `libe_sqlite3.so` dependency** —
SQLite is statically linked into the binary from a `libe_sqlite3-<rid>.a`
archive we build ourselves from the SQLite amalgamation via zig cc. First
end-to-end smoke test (linux-x64):

```
./darklang eval "1L + 2L"   →   3       (cold: 5.6s, warm: 0.2s)
./darklang eval "10L * 5L"  →   50

ldd ./darklang
    linux-vdso.so.1
    libm.so.6
    libc.so.6
    /lib64/ld-linux-x86-64.so.2          ← no libe_sqlite3.so, no libdl

Binary size: 32 MB  (down from ~38 MB on the prior pre-static-link AOT spike;
            JIT release was 46-77 MB)
```

---

## How it works

Three pieces, all in `main`:

**1. `scripts/build/build-sqlite.sh`** (revived from "not currently used"
state) — fetches the SQLite 3.46.0 amalgamation, verifies SHA256, and
cross-compiles a static archive for each runtime using `zig cc`:

```
linux-x64       linux-musl-x64    linux-arm64    linux-arm
osx-x64         osx-arm64
```

Output: `backend/src/Cli/lib/libe_sqlite3-<rid>.a` (~3.5-5.2 MB each).
Cached: skips the build if all artifacts are already present.

Compile flags (post-iteration; see "Gotchas" below):

```
-Os
-DSQLITE_THREADSAFE=1
-DSQLITE_ENABLE_COLUMN_METADATA      # needed for sqlite3_column_{database,origin,table}_name
-DSQLITE_ENABLE_MATH_FUNCTIONS
-DSQLITE_DEFAULT_MEMSTATUS=0
-DSQLITE_DQS=0                       # reject double-quoted string literals
-fPIC
```

**2. `backend/src/Cli/Cli.fsproj`** — adds `<DirectPInvoke Include="e_sqlite3" />`
and per-runtime `<NativeLibrary Include="lib/libe_sqlite3-<rid>.a" />`,
both AOT-conditional:

```xml
<ItemGroup Condition="'$(PublishAot)' == 'true'">
  <DirectPInvoke Include="e_sqlite3" />
</ItemGroup>
<ItemGroup Condition="'$(PublishAot)' == 'true' And '$(RuntimeIdentifier)' == 'linux-x64'">
  <NativeLibrary Include="lib/libe_sqlite3-linux-x64.a" />
</ItemGroup>
... (one ItemGroup per runtime)
```

`DirectPInvoke` rewrites the managed `[DllImport("e_sqlite3")]` calls from
`SQLitePCLRaw.provider.e_sqlite3` into direct link-time references.
`NativeLibrary` tells the AOT linker where to find them. JIT builds ignore
both items.

**3. `scripts/build/build-release-cli-exes.sh`** — already had `--aot`
support from `bea00feec`; no change needed once the above two pieces were
wired up.

---

## What about the bundle's .so?

`Microsoft.Data.Sqlite` transitively pulls `SQLitePCLRaw.bundle_e_sqlite3`,
which ships a `libe_sqlite3.so` runtime asset. That `.so` does get copied
into the AOT publish dir (`backend/Build/out/Cli/Release/net10.0/linux-x64/
publish/`) — but:

1. The published binary doesn't link or load it (verified via `ldd` and a
   clean-tmpdir run).
2. `build-release-cli-exes.sh` does `mv publish/Cli → clis/darklang-<rel>-<rid>`
   — only the bare binary lands in `clis/`. The `.so` stays in `publish/`
   and never reaches the user.
3. `build-and-install-cli-on-host` `docker cp`s only the bare binary; the
   install step extracts only embedded resources (gzipped seed db + README).

So **user filesystem stays clean** even though the build output dir is a
little messy. A possible P2 cleanup: switch from `Microsoft.Data.Sqlite` →
`Microsoft.Data.Sqlite.Core` + `SQLitePCLRaw.provider.e_sqlite3` to drop
the bundle and its `.so` from publish/ entirely. Not blocking; just tidy.

---

## Gotchas hit while building this

### 1. `OMIT_LOAD_EXTENSION` breaks the link

SQLitePCLRaw's provider includes `[DllImport("e_sqlite3")]` for
`sqlite3_load_extension` and `sqlite3_enable_load_extension`
unconditionally. Under JIT, those imports are looked up lazily at first
call — never hit if F# doesn't call them. Under NativeAOT +
DirectPInvoke, they become hard link references, and the linker errors
on missing symbols:

```
undefined reference to `sqlite3_load_extension'
undefined reference to `sqlite3_enable_load_extension'
```

**Fix:** keep extension loading compiled in (drop
`-DSQLITE_OMIT_LOAD_EXTENSION`). It's disabled at runtime by default
(`sqlite3_enable_load_extension(db, 0)`), so the security posture is
unchanged. ~50 KB binary cost.

### 2. `ENABLE_COLUMN_METADATA` is required (off by default)

Same pattern. Provider binds `sqlite3_column_{database,origin,table}_name`
unconditionally; those symbols only exist when SQLite is built with
`-DSQLITE_ENABLE_COLUMN_METADATA`. Add it.

### 3. `OMIT_AUTOINIT` causes a segfault

SQLitePCLRaw's `SQLite3Provider_e_sqlite3` does **not** call
`sqlite3_initialize()` explicitly. It relies on SQLite's default
auto-init-on-first-API-call behavior. Disabling auto-init makes the first
`sqlite3_open` call hit uninitialized internal state and segfault.

**Fix:** drop `-DSQLITE_OMIT_AUTOINIT`. Auto-init is essentially free
(idempotent, O(1) after first call). ~5 KB binary cost.

### 4. `tee | tail -1` hides linker failures

The build script's pipeline exit code defaults to the last command in
the pipeline. `dotnet publish` returning failure inside the pipe is
invisible if you only check the trailing `tail`. Always use
`set -o pipefail` when chaining `dotnet publish` through tee.

---

## Cross-compilation status

```
linux-x64        ✅  built + smoke-tested
linux-musl-x64   ✅  archive built; binary not yet
linux-arm64      ✅  archive built; binary not yet
linux-arm        ✅  archive built; binary not yet
osx-x64          ✅  archive built; binary not yet
osx-arm64        ✅  archive built; binary not yet
win-x64          ❌  archive not built (zig + .lib path needs work;
                     deferred — the windows AOT path is also unproven)
win-arm64        ❌  same as win-x64
```

zig cc handles 6/8 targets out of the box. Windows static libs use the
`.lib` extension by COFF convention; `zig ar` can produce them but the
windows AOT build path through ilc isn't validated either, so the value
of building win-x64 / win-arm64 SQLite archives right now is low.

---

## Current Cli.fsproj — AOT-relevant settings

```xml
<!-- size + correctness -->
<IsTrimmable>true</IsTrimmable>
<TrimMode>link</TrimMode>
<TrimmerRemoveSymbols>true</TrimmerRemoveSymbols>
<SuppressTrimAnalysisWarnings>true</SuppressTrimAnalysisWarnings>
<InvariantGlobalization>true</InvariantGlobalization>
<IlcOptimizationPreference>Size</IlcOptimizationPreference>

<!-- prune .NET subsystems -->
<DebuggerSupport>false</DebuggerSupport>
<EventSourceSupport>false</EventSourceSupport>
<HttpActivityPropagationSupport>false</HttpActivityPropagationSupport>
<UseSystemResourceKeys>true</UseSystemResourceKeys>

<!-- static-link sqlite (AOT only) -->
<DirectPInvoke Include="e_sqlite3" />
<NativeLibrary Include="lib/libe_sqlite3-<rid>.a" />

<!-- trim roots -->
<TrimmerRootAssembly Include="LibExecution" />

<!-- embeds (Release only) -->
<EmbeddedResource Include="../../../rundir/data.db.gz" />
<EmbeddedResource Include="README-to-embed.md" />
```

Per-project `IsTrimmable`:

```
LibExecution / LibDB / LibConfig / Cli / Prelude / LibSerialization  → true
LibParser / LibCloud / LibTreeSitter                                 → false
```

---

## What's remaining

### P1 — should land alongside any AOT release

1. **Cross-build the other 5 runtimes' AOT binaries.** Only linux-x64 is
   smoke-tested. linux-musl-x64 in particular is interesting because we
   have the static archive but haven't tried the AOT link through musl
   (last attempt — `1b32e0827` — hit a missing musl-cross CRT). With
   zig as the cross-toolchain, this *might* now just work.
2. **Re-add `IlcGenerateStackTraceData=false` +
   `IlcFoldIdenticalMethodBodies=true` to `Cli.fsproj`** if they're still
   safe wins; check whether they survived the recent squash (they don't
   appear in current `Cli.fsproj`).
3. **Audit the ~5 remaining IL3050 sites** currently masked by
   `SuppressTrimAnalysisWarnings=true`.
4. **Commit the per-runtime `.a` archives** (currently untracked in
   `backend/src/Cli/lib/`). ~26 MB across 6 RIDs. We already commit
   ~30 MB of tree-sitter `.so`s, so this is precedent-consistent.

### P2 — clean-but-not-blocking

5. **Drop `Microsoft.Data.Sqlite` → `Microsoft.Data.Sqlite.Core` + provider.**
   Removes the bundle's `.so` from publish/ entirely. Requires adding a
   manual `SQLitePCL.raw.SetProvider(new SQLite3Provider_e_sqlite3())`
   call in startup since the bundle's auto-init goes away.
6. **macOS code signing for AOT binaries** (pre-existing).
7. **Linux back-compat: an AOT binary produced on Linux N only runs on
   Linux ≥ N.** Mitigation: build on the oldest reasonable target distro
   in CI. JIT publish doesn't have this issue.
8. **Slim the SQLite build further** — flag candidates: `OMIT_DEPRECATED`,
   `OMIT_EXPLAIN`, `OMIT_SHARED_CACHE`. Be careful not to omit anything
   SQLitePCL imports unconditionally (see gotchas 1-2).

### Pre-AOT history (context only — already done)

9. ✅ Custom binary serialization (MessagePack → hand-rolled). `be58f8dcf`.
10. ✅ FSharpPlus / FSharpx usage minimized to non-reflective paths.
11. ✅ `Json.Vanilla` deleted (was the last reflective JSON site outside
    `FormatV0.Dval`).
12. ✅ ASP.NET removed from CLI closure.
13. ✅ AOT-safe `ToString` overrides on F# DUs. `4cd03ebb0` / `adfa0c738`.
14. ✅ Hand-rolled Utf8Json for `FormatV0.Dval`. `f6cd562bc`.
15. ✅ Gzip-embedded seed db. `c463837e1` / `d09ad6ed7`.
16. ✅ Pruned .NET runtime subsystems. `127c50ec1`.
17. ✅ libe_sqlite3 static link via zig cc + DirectPInvoke. **← this report.**

---

## Size trajectory (linux-x64 published binary)

```
Pre-AOT JIT baseline (no trim)               ~91 MB
JIT + partial trim + custom binser           ~44 MB   (mid-2025)
Recent JIT releases on main                  ~46-77 MB
─── AOT ──────────────────────────────────────────────
First booting AOT (with .so sidecar)         ~47 MB   (4cd03ebb0)
After seed-db gzip                           ~38 MB   (d09ad6ed7)
Current AOT + static SQLite                  ~32 MB   ← here
```

The static SQLite *added* ~5 MB of SQLite code into the binary, but the
overall delta is **smaller** than the previous AOT — likely from removing
the IncludeNativeLibrariesForSelfExtract bookkeeping the JIT path needs.

---

## Smoke test record

```
$ TESTDIR=$(mktemp -d); cp clis/darklang-alpha-61cb32ac75-linux-x64 $TESTDIR/darklang
$ cd $TESTDIR && HOME=$TESTDIR/fake-home
$ time ./darklang eval "1L + 2L"
Setting up Darklang CLI data directory at /tmp/.../.darklang
CLI data directory setup complete
Growing package DB from ops (9845 ops to apply)...
Package DB ready
3
real    0m5.631s

$ time ./darklang eval "1L + 2L"
3
real    0m0.207s

$ ldd ./darklang
        linux-vdso.so.1
        libm.so.6
        libc.so.6
        /lib64/ld-linux-x86-64.so.2
```

`growIfNeeded` (the previously-failing SQL-heavy path that caused
`DllNotFoundException: libe_sqlite3.so`) ran 9845 ops to completion, then
the eval returned the correct result. Warm start drops to 0.2s.

---

## Sources

**Code:**
- `backend/src/Cli/Cli.fsproj` (DirectPInvoke + per-RID NativeLibrary)
- `backend/src/Cli/lib/libe_sqlite3-*.a` (untracked; 6 archives, ~26 MB)
- `scripts/build/build-sqlite.sh` (rewritten to produce static archives)
- `scripts/build/build-release-cli-exes.sh` (unchanged; already had `--aot`)
- `scripts/build-and-install-cli-on-host` (warning removed in earlier
  iteration; current state is clean)

**External:**
- [SQLite amalgamation 3.46.0](https://www.sqlite.org/2024/sqlite-amalgamation-3460000.zip)
  (SHA256 pinned in the build script)
- [zig 0.11.0](https://ziglang.org/), already in the devcontainer at `~/zig/zig`
- [.NET DirectPInvoke docs](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/interop)
- [SQLitePCLRaw](https://github.com/ericsink/SQLitePCL.raw) — provider.e_sqlite3 is what supplies the DllImports we're satisfying

**Git history** — see `git log -i --grep aot` (≈30 commits between
2026-04-28 and 2026-05-12 on `main`).
